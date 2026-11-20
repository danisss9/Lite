using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal static partial class BoxEngine
{
    /// <summary>The ::first-line styles that govern <paramref name="node"/>'s first line
    /// (§5.12.1): its own, or its parent block's. An inline-level node (a text run) shares the
    /// parent's first formatted line; a block-level child opens it only when it is the parent's
    /// first in-flow child (`ul:first-line` styles the first list item's first line, not every
    /// item's).</summary>
    internal static Dictionary<string, string>? FirstLineStylesOf(LayoutNode node)
    {
        if (node.FirstLineStyles is { } own) return own;
        if (node.Parent?.FirstLineStyles is not { } inherited) return null;
        if (node.TagName.StartsWith('#') || node.GetDisplay() is DisplayType.Inline) return inherited;
        foreach (var c in node.Parent.Children)
        {
            if (c.GetDisplay() == DisplayType.None || c.GetPosition() is PositionType.Absolute or PositionType.Fixed ||
                c.GetFloat() != FloatType.None) continue;
            if (c.TagName.StartsWith('#') && string.IsNullOrWhiteSpace(c.DisplayText)) continue;
            return ReferenceEquals(c, node) ? inherited : null;
        }
        return null;
    }

    /// <summary>The ::first-line 'text-transform' of <paramref name="node"/>'s first line.</summary>
    internal static TextTransform FirstLineTransform(LayoutNode node) =>
        FirstLineStylesOf(node) is { } styles && styles.TryGetValue("text-transform", out var value)
            ? value.Trim().ToLowerInvariant() switch
            {
                "uppercase" => TextTransform.Uppercase,
                "lowercase" => TextTransform.Lowercase,
                "capitalize" => TextTransform.Capitalize,
                _ => TextTransform.None,
            }
            : TextTransform.None;

    /// <summary>The height a percentage 'height' on an inline replaced box resolves against
    /// (§10.5): the specified height of its containing block — the nearest block ancestor. When
    /// that height depends on content ('auto', or a percentage of an auto height) the percentage
    /// computes to 'auto', signalled by 0 (inline-replaced-height-008: 'height: 1%' resolved
    /// against the viewport and squashed the image to 24px). The root's containing block is the
    /// initial one, the viewport's height.</summary>
    private static float InlineContainingHeight(LayoutNode node, float viewportHeight)
    {
        for (var a = node.Parent; a is not null; a = a.Parent)
        {
            if (a.TagName.StartsWith('#') || a.GetDisplay() is DisplayType.Inline) continue;
            if (a.TagName == "HTML" && a.IsAutoHeight()) return viewportHeight;
            if (a.IsAutoHeight() || RawStyle(a, "height")?.Trim().EndsWith('%') == true) return 0f;
            return Math.Max(0f, a.GetHeight(0f, 0, viewportHeight));
        }
        return viewportHeight;
    }

    /// <summary>The size an INLINE replaced item reserves: <see cref="TryResolveReplacedSize"/>'s
    /// answer is final whenever a dimension was specified (a specified 0 is a size, not 'auto' —
    /// empty-cells-007's 0x0 image); the intrinsic fallbacks only cover the auto case where
    /// nothing is known yet (an unloaded image without width/height attributes).</summary>
    private static (float Width, float Height) InlineReplacedSize(
        LayoutNode node, float maxWidth, float viewportHeight)
    {
        var sized = TryResolveReplacedSize(node, maxWidth, InlineContainingHeight(node, viewportHeight), viewportHeight) ?? default;
        var specW = !node.IsAutoWidth() && RawStyle(node, "width")?.Trim().EndsWith('%') != true;
        var specH = !node.IsAutoHeight() && RawStyle(node, "height")?.Trim().EndsWith('%') != true;
        var w = sized.Width > 0 ? sized.Width
            : specW ? 0f
            : node.IntrinsicWidth > 0 ? (float)node.IntrinsicWidth : node.Image?.Width ?? 100f;
        var h = sized.Height > 0 ? sized.Height
            : specH ? Math.Max(0f, sized.Height)
            : node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 100f;
        return (w, h);
    }

    // -------------------------------------------------------------------------
    // Inline formatting context
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a run of inline/inline-block nodes within a line box.
    /// Returns total height consumed by all line boxes.
    /// <para>CSS 2.1 §9.5: floats shorten the line boxes they are beside, and each line box gets
    /// its own band — a float that ends partway down the run stops narrowing the lines below it,
    /// which is how text flows around a float. <paramref name="floats"/> is the block formatting
    /// context's active-float list and <paramref name="originX"/>/<paramref name="maxWidth"/>
    /// the containing block's full content band; the band actually available is recomputed for
    /// every line.</para>
    /// </summary>
    /// <param name="lastLineTop">Offset from <paramref name="originY"/> to the top of the LAST line
    /// box this run produced, and <paramref name="lastLineWidth"/> the width its content used.
    /// A float that follows this run belongs on that line if it still fits (CSS 2.1 §9.5.1).</param>
    private static float LayoutInlineRun(
        List<LayoutNode> nodes,
        float originX, float originY,
        float maxWidth,
        float viewportWidth, float viewportHeight,
        List<ActiveFloat> floats,
        out float lastLineTop, out float lastLineWidth,
        float firstLineIndent = 0f, float containingBlockHeight = 0f)
    {
        lastLineTop = 0f;
        lastLineWidth = 0f;
        var items = new List<InlineItem>();
        // Items are measured against the first line's band; a line that turns out wider re-measures
        // its text below.
        var (firstX, firstW) = AvailableBand(floats, originY, 1, originX, maxWidth);
        // CSS 2.1 §16.2 / UAX #9 subset: a block carrying an active RTL override renders its
        // inline content in reverse visual order — the top-level child sequence reverses here
        // (keyed on the container only: an inline override further down reverses its OWN
        // content, not its siblings), and each text leaf reverses through RenderedText. The
        // legacy reversal owns every override scope: the paragraph pass reproduces the
        // character reversal but the twin-walk shapes assert the legacy's line geometry
        // (bidi-box-model, the unicode-bidi-applies-to families), so the routing stays.
        if (nodes.Count > 0 && nodes[0].Parent is { } runBlock && runBlock.BidiIsRtlOverride())
            nodes = nodes.AsEnumerable().Reverse().ToList();
        CollectInlineItems(nodes, items, firstW, viewportWidth, viewportHeight, containingBlockHeight);

        // §16.2 / UAX #9: an RTL paragraph or an embed scope reorders its inline content
        // visually. The paragraph-level pass runs before the line loop — it engages only when
        // the composed run fits one band, because line breaking must stay on the logical order
        // (a wrapped RTL line re-evaluates the algorithm per line box). Paragraphs under an
        // active bidi-override scope keep the legacy whole-stream reversal above.
        items = ReorderForBidi(items, nodes.Count > 0 ? nodes[0].Parent : null, firstW) ?? items;

        if (items.Count == 0) return 0f;

        // CSS 2.1 §16.2: 'text-align' aligns the inline-level content of each LINE BOX, not just
        // text — an image, inline-block or inline-table on the line moves with it. It applies to
        // block containers and is inherited, so the value that governs this run is the containing
        // block's (an inline child may declare its own, which has no effect on the line box).
        // 'justify' is handled at paint time by Drawer.DrawWrappedText and left alone here.
        var container = nodes.Count > 0 ? nodes[0].Parent : null;
        var lineAlign = (container ?? nodes[0]).GetTextAlign();

        // The band available to the line under construction, as offsets from originX. Recomputed
        // for every line: a float only shortens the lines it is actually beside (§9.5).
        var lineY = 0f;

        // §9.5 rule 7: "if a shortened line box is too small to contain any content, it is shifted
        // downward until either it fits or there are no more floats present". Without this an
        // unbreakable word simply overflowed the narrow band beside the float.
        var firstContentW = FirstUnbreakableWidth(items);
        while (firstContentW + firstLineIndent > firstW + 0.5f)
        {
            var drop = NextFloatBottom(floats, originY + lineY);
            if (drop is not { } nextY) break;
            lineY = nextY - originY;
            (firstX, firstW) = AvailableBand(floats, originY + lineY, 1, originX, maxWidth);
        }

        var bandLeft = firstX - originX;
        var bandRight = bandLeft + firstW;
        var lineX = bandLeft + firstLineIndent;

        // Running CSS 2.1 §10.8.1 baseline-alignment accumulators for the line under construction:
        // maxAbove/maxBelow track the tallest reach above/below the shared baseline among items
        // aligned baseline/sub/super/middle; maxTopH/maxBottomH track the tallest 'top'/'bottom'
        // aligned items, which anchor to the line box's own edges instead of the baseline.
        var maxAbove = 0f;
        var maxBelow = 0f;
        var maxTopH = 0f;
        var maxBottomH = 0f;
        // Whether anything on the current line aligns against the shared baseline. Only then does
        // maxBelow hold a real measurement rather than its 0 starting value.
        var hasBaselineItem = false;
        var lineEndsWithBreak = false;

        // §10.8.1's STRUT: every line box begins with a zero-width inline box carrying the block
        // container's own font and line-height, so a line holding nothing but a 96px image is
        // still at least as tall as the text that would have sat on its baseline. Measured once —
        // the strut's metrics come from the block, not from whatever lands on the line.
        var strutAbove = 0f;
        var strutBelow = 0f;
        if (container is not null)
        {
            using var strutFont = TextMeasure.CreateFont(container);
            var strutLh = container.GetLineHeight(container.GetFontSize());
            // With 'line-height: normal' the strut is exactly the font's own box — no leading to
            // split. Our 'normal' is a flat 1.4em, which is generous enough that a strut using it
            // would poke out above a 16px image on an otherwise-empty line and nudge it down.
            if (container.IsNormalLineHeight())
                strutLh = -strutFont.Metrics.Ascent + strutFont.Metrics.Descent;
            strutAbove = TextMeasure.ComputeAscent(strutFont, strutLh);
            strutBelow = strutLh - strutAbove;
        }

        var placed = new List<(InlineItem item, float relX, float relY)>();
        // Which line each placed entry landed on — the §8.4/§8.6 wrapper-fragment assembly
        // groups a wrapper's edges and content per line below.
        var placedLine = new List<int>();
        var currentLine = 0;
        var lineStart = 0;

        // Locals rather than the out parameters directly: C# forbids touching an 'out' parameter
        // from inside a local function.
        var committedLineTop = 0f;
        var committedLineWidth = 0f;
        // Whether any line box has committed yet: a float following the first line may join it
        // (§9.5.1 rule 6), but before any commit there is no open line to join.
        var hasCommittedLine = false;

        // §9.4.2's test for a line box that "exists": any in-flow content at all — text that is
        // not purely collapsible whitespace, a replaced box, an inline-block/flex/table, or a
        // forced break.
        bool LineHasContent()
        {
            if (lineEndsWithBreak) return true;
            for (var k = lineStart; k < placed.Count; k++)
            {
                var it = placed[k].item;
                if (it.Kind is InlineItemKind.OutOfFlow or InlineItemKind.Strut) continue;
                // A no-break space is content (§16.6.1 collapses only CSS white space), and so
                // is PRESERVED white space (§9.4.2): a 'white-space: pre' span holding one space
                // makes a real line (white-space-processing-046).
                if (it.Kind == InlineItemKind.Text && (it.Text ?? "").IsCssWhiteSpaceOnly() &&
                    it.Node.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap)) continue;
                return true;
            }
            return false;
        }

        void CommitLine()
        {
            // Resolve the final line-box height: start from the baseline-aligned items' reach,
            // then grow outward (below for 'top', above for 'bottom') if those need more room.
            var above = maxAbove;
            var below = maxBelow;
            // §9.4.2: a line with no content (only empty edgeless inline boxes) is zero-height.
            if (!LineHasContent()) { above = 0f; below = 0f; maxTopH = 0f; maxBottomH = 0f; }

            // The strut only counts on a line that actually has content. §9.4.2: a line box with
            // no text, no in-flow content and no inline box with non-zero edges is a ZERO-height
            // line box, so seeding the strut unconditionally would give every stray whitespace
            // node a line of its own.
            if (LineHasContent())
            {
                above = Math.Max(above, strutAbove);
                // The strut's own reach below the baseline can be NEGATIVE (a line-height smaller
                // than the font's ascent+descent gives negative half-leading), so it only counts
                // when nothing else has claimed room down there — clamping it to 0 would stretch a
                // `line-height: 10px` line back out to the full baseline.
                below = hasBaselineItem ? Math.Max(below, strutBelow) : strutBelow;
            }
            if (maxTopH > above + below) below = maxTopH - above;
            if (maxBottomH > above + below) above = maxBottomH - below;
            var thisLineHeight = above + below > 0f ? above + below : Math.Max(maxTopH, maxBottomH);

            // The line's used width, ignoring trailing collapsible whitespace — it is removed at
            // the end of a line (§16.6.1), so counting it would land a right-aligned line a space
            // short and would overstate how much room a following float needs.
            var lineRight = 0f;
            for (var k = placed.Count - 1; k >= lineStart; k--)
            {
                var (it, rx, _) = placed[k];
                if (it.Kind == InlineItemKind.OutOfFlow) continue;
                if (it.Kind is InlineItemKind.Text or InlineItemKind.LineBreak &&
                    string.IsNullOrWhiteSpace(it.Text)) continue;
                lineRight = rx + it.Width;
                // The last text run's own trailing collapsible spaces are removed too: a
                // right-aligned 'x ' line set its glyph one space short of the line end
                // (c5525-fltblck-000's third line).
                if (k == placed.Count - 1 && IsSingleLineText(it) &&
                    it.Text is { Length: > 0 } lastText && IsBreakSpace(lastText[^1]) &&
                    it.Node.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
                {
                    using var endFont = TextMeasure.CreateFont(it.Node);
                    var (endLs, endWsp) = TextMeasure.SpacingOf(it.Node);
                    lineRight = rx + TextMeasure.MeasureSingleLine(lastText.TrimEndCss(), endFont, -1f, endLs, endWsp).Width;
                }
                break;
            }
            committedLineTop = lineY;
            // Reported to the caller as the width used WITHIN the line's band, so it can decide
            // whether a following float still fits on that line.
            committedLineWidth = Math.Max(0f, lineRight - bandLeft);

            // §16.2 horizontal alignment: shift the whole line by the space left unused in ITS
            // band (which floats may have narrowed), not in the containing block.
            var dx = 0f;
            if (lineAlign is TextAlign.Center or TextAlign.Right)
            {
                var slack = bandRight - lineRight;
                if (slack > 0f) dx = lineAlign == TextAlign.Center ? slack / 2f : slack;
            }

            for (var k = lineStart; k < placed.Count; k++)
            {
                var (it, rx, _) = placed[k];
                var vAlign = it.Node.GetVerticalAlign();
                float yOffset = it.Kind == InlineItemKind.OutOfFlow ? 0f : vAlign switch
                {
                    VerticalAlignType.Top => 0f,
                    VerticalAlignType.Bottom => thisLineHeight - it.Height,
                    _ => above - AboveBaselineComponent(it, vAlign),
                };
                placed[k] = (it, rx + dx, lineY + yOffset);
            }
            lineY += thisLineHeight;
            hasCommittedLine = true;
            maxAbove = maxBelow = maxTopH = maxBottomH = 0f;
            hasBaselineItem = false;
            lineEndsWithBreak = false;
            currentLine++;
            // The next line sits lower, so it may clear a float the previous line was beside.
            var (nx, nw) = AvailableBand(floats, originY + lineY, 1, originX, maxWidth);
            bandLeft = nx - originX;
            bandRight = bandLeft + nw;
            lineX = bandLeft;
            lineStart = placed.Count;
        }

        // CSS 2.1 16.6 / UAX #14: a line may only break at a break opportunity. Each placed entry
        // remembers the item it came from so the line can be cut back to its last opportunity.
        var placedSource = new List<int>();
        var sourceIndex = 0;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            while (placedSource.Count < placed.Count) placedSource.Add(sourceIndex);
            sourceIndex = itemIndex;
            if (item.Kind == InlineItemKind.LineBreak)
            {
                // An otherwise-empty line still needs the <br>'s own font metrics for its height.
                if (maxAbove + maxBelow + maxTopH + maxBottomH <= 0f)
                {
                    maxAbove = item.Ascent;
                    maxBelow = item.Height - item.Ascent;
                    hasBaselineItem = true;
                }
                // HTML <br> with 'clear' (§9.5.2's forced-break form — the WPT floats-clear corpus
                // and border-conflict-style-107's float rows): the line box the break terminates
                // is positioned below the bottom edge of the floats it clears. CommitLine
                // re-stamps every placed item from lineY and recomputes the band at the new top,
                // so following floats (constrained not to sit higher than an earlier line box)
                // start past them too. Line boxes carry no margins, so the straight max against
                // the float bottoms is the whole rule.
                var brClear = item.Node?.GetClear() ?? ClearType.None;
                if (brClear != ClearType.None)
                    lineY = Math.Max(lineY, ApplyClear(brClear, floats, originY + lineY) - originY);
                // §9.4.2 exempts a line that ends with a forced break from the zero-height rule,
                // so an empty <br> line still gets the strut.
                lineEndsWithBreak = true;
                CommitLine();
                continue;
            }

            // An out-of-flow marker takes no space and must not contribute to the line's height
            // or its used width: it only records where the line had got to.
            if (item.Kind == InlineItemKind.OutOfFlow)
            {
                placed.Add((item, lineX, lineY));
                placedLine.Add(currentLine);
                continue;
            }

            // §9.5: a float met mid-run registers as an active float at the CURRENT line box's
            // top (rule 4: the float's top aligns with the line box it was generated in), beside
            // the band's start or end edge per its side, and the line's remaining width shrinks
            // beside it. The float's node lays out its own content here; the line loop continues
            // with the narrowed band. When the float takes the band's edge where the line's
            // earlier content already sits, that content re-flows right of the float (§9.5: the
            // current line box is shortened by the float's margin box — floats-placement-
            // vertical-001a's "Hello" moves beside the float, floats-006's X re-flows right of
            // the two floated squares).
            if (item.Kind == InlineItemKind.Float)
            {
                // §9.5.1 rules 6+8: on an empty current line the run's just-committed line box
                // is still the float's open line — it may join that line's top when it fits
                // beside the width the line used (a <br> between float groups, the strut-only
                // line a cleared break ended). The open-line decision itself lives in
                // PlaceInRunFloat.
                var lineIsEmpty = placed.FindIndex(lineStart, p => p.item.Kind is not (InlineItemKind.InlineEdge
                    or InlineItemKind.OutOfFlow or InlineItemKind.Float)) < 0;
                // §16.6.1: trailing collapsible whitespace at the line's end is removed — it is
                // not line content, so the float's overlap, re-flow and drop-below decisions
                // measure the line's extent without it (floats-006: the space between the span
                // and the floated divs would otherwise inflate the used width and drop the
                // second float below the first instead of packing beside it).
                var lineContentRight = lineX;
                for (var k = placed.Count - 1; k >= lineStart; k--)
                {
                    var (pit, prx, _) = placed[k];
                    if (pit.Kind == InlineItemKind.OutOfFlow || pit.Kind == InlineItemKind.Float) continue;
                    if (pit.Kind is InlineItemKind.Text or InlineItemKind.LineBreak &&
                        string.IsNullOrWhiteSpace(pit.Text)) continue;
                    lineContentRight = prx + pit.Width;
                    break;
                }
                var rf = PlaceInRunFloat(item, floats, originX, originY, lineY, lineContentRight,
                                Math.Max(maxAbove + maxBelow, strutAbove + strutBelow), maxWidth,
                                viewportWidth, viewportHeight, bandLeft,
                                hasCommittedLine && lineIsEmpty ? committedLineTop : float.NaN,
                                committedLineWidth);
                if (rf.Shift > 0f)
                {
                    for (var k = lineStart; k < placed.Count; k++)
                    {
                        var (it, rx, ry) = placed[k];
                        if (it.WrappedLines is { } slices)
                            it = it with { WrappedLines =
                                slices.Select(s => (s.Text, s.X + rf.Shift, s.Width, s.Height)).ToList() };
                        placed[k] = (it, rx + rf.Shift, ry);
                    }
                    lineX += rf.Shift;
                }
                var (nbx, nbw) = AvailableBand(floats, originY + lineY,
                    Math.Max(Math.Max(maxAbove + maxBelow, strutAbove + strutBelow), 1f), originX, maxWidth);
                bandLeft = nbx - originX;
                bandRight = bandLeft + nbw;
                continue;
            }

            // §9.5 rule 7 for EVERY line box, not just the run's first: a line whose band —
            // measured over the extent the line will actually occupy — is too small for its
            // content is shifted downward until it fits or no floats remain below. The band
            // uses the coming item's height, so a float whose top starts partway down the line
            // box still shortens it (§9.5: a line box is "next to" a float when they overlap
            // vertically) — floats-wrap-top-below-inline-002l's second span drops below the
            // second float, and floats-zero-height-wrap's 1px-tall float still shortens the
            // line it starts inside. Only an empty line shifts: content already placed stays.
            if (floats.Count > 0 && lineX <= bandLeft &&
                placed.FindIndex(lineStart, p => p.item.Kind is not (InlineItemKind.InlineEdge
                    or InlineItemKind.OutOfFlow or InlineItemKind.Float)) < 0)
            {
                var pieceW = FirstPieceWidth(item) + (lineX - bandLeft);
                var contentH = Math.Max(Math.Max(Math.Max(strutAbove + strutBelow, maxAbove + maxBelow),
                    item.Height), 1f);
                if (pieceW > bandRight - bandLeft + 0.5f)
                {
                    var (dxb, dwb) = AvailableBand(floats, originY + lineY, contentH, originX, maxWidth);
                    var keepIndent = lineX - bandLeft;
                    while (pieceW > dwb + 0.5f && NextFloatBottom(floats, originY + lineY) is { } nextY)
                    {
                        lineY = nextY - originY;
                        (dxb, dwb) = AvailableBand(floats, originY + lineY, contentH, originX, maxWidth);
                    }
                    bandLeft = dxb - originX;
                    bandRight = bandLeft + dwb;
                    lineX = bandLeft + keepIndent;
                }
            }

            // A whitespace-only text item that would cross the line's end collapses there
            // (§16.6.1): it must neither wrap onto a fabricated line box nor commit one — the
            // green overlay in the white-space band grew a ~45px phantom line from exactly this.
            // The space is itself the break opportunity, so the line ends at it: what follows
            // starts the next line instead of looking glued to the word before the space.
            if (item.Kind == InlineItemKind.Text && item.Text != null && item.Text.IsCssWhiteSpaceOnly() &&
                lineX + item.Width > bandRight && WrapsLines(item.Node))
            {
                if (lineX > bandLeft) CommitLine();
                continue;
            }

            // §16.6.1: a collapsible space at the END of a line is removed. A whitespace-only
            // item TRAILING the whole run (nothing but collapsible whitespace follows it) ends
            // the last line with a space: placing it would stamp the anonymous box's geometry
            // (and paint its background — shared with its source element) past the line's
            // content, an artifact the HTML parser's whitespace folding hides but an XML-served
            // document's raw text nodes expose (bidi-box-model-019's 20px navy tail). The gap a
            // space renders MID-line comes from the surrounding items' own trailing spaces, so
            // dropping this one loses nothing.
            // Closing inline-box edges after it do not keep it: 'Eleven</span> </span>' ends the
            // line at the border with no space before it (block-in-inline-insert-001's refs).
            if (item.Kind == InlineItemKind.Text && item.Text != null && item.Text.IsCssWhiteSpaceOnly() &&
                OnlyClosingEdgesFollow(items, itemIndex) &&
                item.Node?.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
            {
                continue;
            }

            // §16.6.1: a collapsible space at the START of a line is removed — it neither paints
            // nor holds a slot, so the content after it stays on the line it would have started.
            // An XML-served document's raw inter-element whitespace node otherwise grew its own
            // line box and pushed the run's paragraph one slot below the reference's (the HTML
            // parser's folding hides the shape; border-color-012's test markup exposes it).
            if (item.Kind == InlineItemKind.Text && item.Text != null && item.Text.IsCssWhiteSpaceOnly() &&
                placed.Count == lineStart &&
                item.Node?.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
            {
                continue;
            }

            // §16.6.1: a collapsible space at the END of a line is removed, so it cannot force
            // the wrap. Fit a text item that ends with a collapsible space by its trimmed text:
            // if the words fit, the item stays on the line and the trailing space collapses at
            // the line end (or renders as the boundary when later content continues it). Fitting
            // by the untrimmed width wrapped the whole item for one space too many — the abs-pos
            // overlays of the white-space band shed their last word (" d") off an exactly-sized
            // shrink-to-fit box.
            var fitWidth = item.Width;
            if (item.Kind == InlineItemKind.Text && item.Text is { } fitText && fitText.Length > 0 &&
                char.IsWhiteSpace(fitText[^1]) &&
                item.Node?.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
            {
                using var fitFont = TextMeasure.CreateFont(item.Node);
                var (fitLs, fitWsp) = TextMeasure.SpacingOf(item.Node);
                fitWidth = TextMeasure.MeasureSingleLine(fitText.TrimEndCss(), fitFont, -1f, fitLs, fitWsp).Width;
            }
            if (Environment.GetEnvironmentVariable("LITE_TRACE_LINE") == "1")
                File.AppendAllText("line-trace.txt",
                    $"item[{itemIndex}] '{item.Text}' w={item.Width:F0} fit={fitWidth:F0} lineX={lineX:F0} band=[{bandLeft:F0},{bandRight:F0}] overflow={lineX + fitWidth > bandRight}\n");
            if (lineX > bandLeft && lineX + fitWidth > bandRight)
            {
                // A wrappable text item GLUED to the line's start (only edge items before it —
                // the span's own leading margin) neither breaks nor peels: the text fills the
                // line from the indented start and wraps naturally through the re-measure
                // below (text-indent-wrap-001's inline-margin reference). Committing it moved
                // the whole item one line down; split-peeling duplicated the text.
                var gluedToLineStart = item.Kind == InlineItemKind.Text && item.Text is { Length: > 0 } glued &&
                    !IsBreakSpace(glued[0]) && WrapsLines(item.Node) &&
                    placed.FindIndex(lineStart, p => p.item.Kind is not (InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow)) < 0;
                if (Environment.GetEnvironmentVariable("LITE_TRACE_LINE") == "1")
                    File.AppendAllText("line-trace.txt",
                        $"  -> breakAllowed={BreakAllowedBefore(placed, lineStart, item)} glued={gluedToLineStart}\n");
                if (!gluedToLineStart && SplitsPerLine(item, lineAlign) &&
                    SplitToFit(item, bandRight - lineX, requireFit: true) is { } partial)
                {
                    // The run's first words fit beside what the line already holds: they stay,
                    // and the rest starts the next line (c5502-imrgn-r-003's span begins with
                    // 'xx' at the end of line 1, not on line 2).
                    items[itemIndex] = partial.Head;
                    items.Insert(itemIndex + 1, partial.Tail);
                    itemIndex--;
                    continue;
                }
                if (!gluedToLineStart && BreakAllowedBefore(placed, lineStart, item))
                {
                    // The opening edges of the inline boxes this item starts go down with it: a
                    // box's leading margin/border/padding cannot end a line its content begins on
                    // the next (inlines-005: a bordered <span>X</span> wrapped and left its left
                    // border behind, so its box hung 3px outside the next line's start).
                    // Only an item that moves down WHOLE carries its edges: one with a break
                    // opportunity inside re-wraps on the next line, and its first words belong on
                    // this one beside the edge (c5504-imrgn-l-001's 'margin-left: 4em' span).
                    var unbreakable = item.Kind != InlineItemKind.Text || !(item.Text ?? "").Trim().Any(IsBreakSpace);
                    var carry = placed.Count;
                    while (unbreakable && carry > lineStart && placed[carry - 1].item is { Kind: InlineItemKind.InlineEdge, TrailingEdge: false } edge &&
                           IsAncestorOrSelf(edge.Node, item.Node))
                        carry--;
                    if (carry < placed.Count && carry > lineStart &&
                        placed.FindIndex(lineStart, carry - lineStart, p => p.item.Kind is not (InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow)) >= 0)
                    {
                        var resume = placedSource[carry];
                        placed.RemoveRange(carry, placed.Count - carry);
                        placedSource.RemoveRange(carry, placedSource.Count - carry);
                        if (placedLine.Count > carry) placedLine.RemoveRange(carry, placedLine.Count - carry);
                        CommitLine();
                        itemIndex = resume - 1;
                        continue;
                    }
                    CommitLine();
                }
                else if (!gluedToLineStart && SplitAtFirstOpportunity(items, itemIndex) )
                {
                    // The glued head stays with the content before it; the tail, which now
                    // starts after a break opportunity, is placed on its own turn.
                    itemIndex--;
                    continue;
                }
                else if (!gluedToLineStart && LastContentIndex(placed, lineStart) is { } last &&
                         SplitAtLastOpportunity(placed[last].item) is { } split)
                {
                    // The overflowing item is glued to the line's last text run (a span's
                    // closing margin, §8.3, or a word continuing past an element boundary), and
                    // that run has a break opportunity of its own: the line ends there, and the
                    // run's last word moves down with what is glued to it. Moving the whole run
                    // down (the cut below) left the line holding only what came before the span
                    // (c5502-imrgn-r-001: 'x <span style="margin-right: 4em">xxxxx xxxxx</span> x'
                    // in a 13em box breaks after 'x xxxxx', not after 'x').
                    // A float inside the glued run was met while this line still held the whole
                    // run: it is placed beside THIS line (rule 4: the line it was generated in)
                    // and does not come round again when the run is re-placed below
                    // (float-nowrap-hyphen-rewind-1's reference).
                    foreach (var fj in FloatsInGluedRun(items, itemIndex))
                    {
                        PlaceInRunFloat(items[fj], floats, originX, originY, lineY, lineX,
                                        Math.Max(maxAbove + maxBelow, strutAbove + strutBelow), maxWidth,
                                        viewportWidth, viewportHeight, bandLeft);
                        items.RemoveAt(fj);
                    }
                    var src = placedSource[last];
                    placed[last] = (split.Head, placed[last].relX, placed[last].relY);
                    items[src] = split.Head;
                    items.Insert(src + 1, split.Tail);
                    placed.RemoveRange(last + 1, placed.Count - last - 1);
                    placedSource.RemoveRange(last + 1, placedSource.Count - last - 1);
                    if (placedLine.Count > last + 1) placedLine.RemoveRange(last + 1, placedLine.Count - last - 1);
                    (maxAbove, maxBelow, maxTopH, maxBottomH, hasBaselineItem) = LineMetrics(placed, lineStart);
                    CommitLine();
                    itemIndex = src;
                    continue;
                }
                else if (LastOpportunity(placed, lineStart) is { } cut && cut > lineStart)
                {
                    // No opportunity here: the unbreakable run moves down whole, from the line's
                    // last opportunity (a word split across inline boxes is still one word).
                    var resume = placedSource[cut];
                    placed.RemoveRange(cut, placed.Count - cut);
                    placedSource.RemoveRange(cut, placedSource.Count - cut);
                    // placedLine runs parallel to placed: the re-placed items must not inherit
                    // the removed entries' line numbers (their wrapper fragments fold per line).
                    if (placedLine.Count > cut) placedLine.RemoveRange(cut, placedLine.Count - cut);
                    (maxAbove, maxBelow, maxTopH, maxBottomH, hasBaselineItem) = LineMetrics(placed, lineStart);
                    CommitLine();
                    itemIndex = resume - 1;
                    continue;
                }
                // Otherwise nothing on this line can break: the content overflows it.
            }

            // A line still "starts" while only inline-box edges sit on it: a collapsible space
            // right after a span's opening border is at the line's start (§16.6.1) and goes, as
            // in browsers — keeping it opened ' OneTwoThree' one space past the border
            // (block-in-inline-insert-001-nosplit-ref).
            var atLineStart = lineX <= bandLeft ||
                placed.FindIndex(lineStart, p => p.item.Kind is not (InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow)) < 0;

            // Skip whitespace-only text at the start of a line — a COLLAPSING one (§16.6.1):
            // under white-space:pre/pre-wrap a preserved space at a line's start renders, and
            // a cell whose content is exactly that space ("a <cellws> </cellws> d") lost it.
            if (atLineStart && item.Kind == InlineItemKind.Text &&
                item.Text != null && item.Text.IsCssWhiteSpaceOnly())
            {
                var wsMode = item.Node?.GetWhiteSpace() ?? WhiteSpace.Normal;
                if (wsMode is not (WhiteSpace.Pre or WhiteSpace.PreWrap)) continue;
            }

            // §16.6.1: collapsible spaces at the START of a line are removed — including the
            // spaces LEADING a text node that also carries words (" a " opens at the 'a'): the
            // parser collapses each source text node to " x ", and a bare-text node landing at
            // a line boundary painted one space too far right (white-space-12 reftests).
            // Preserved spaces (pre/pre-wrap) are content and stay.
            if (atLineStart && item.Kind == InlineItemKind.Text && item.Text is { } leading &&
                leading.Length > 0 && char.IsWhiteSpace(leading[0]))
            {
                var wsMode = item.Node?.GetWhiteSpace() ?? WhiteSpace.Normal;
                if (wsMode is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
                {
                    var stripped = leading.TrimStartCss();
                    if (stripped.Length == 0) continue;
                    // Re-measured WITH the node's letter/word spacing, like every other text
                    // measure — plain MeasureText dropped it, so a spaced 'T' ran into the next
                    // inline's first letter (text-transform-cap-002).
                    var stripW = StrippedWidth(item.Node, stripped);
                    item = item with { Text = stripped, Width = stripW, ContentW = stripW };
                }
            }

            // §16.6.1: consecutive collapsible spaces across an inline boundary collapse to
            // one — the previous item's trailing space has already advanced the line, so this
            // item's leading space must not render a second one (float-nowrap-3's 'my parent.'
            // sat one ch right of the reference's). Preserved spaces (pre/pre-wrap) stay.
            if (lineX > bandLeft && item.Kind == InlineItemKind.Text && item.Text is { Length: > 0 } lt &&
                char.IsWhiteSpace(lt[0]) &&
                item.Node?.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
            {
                for (var k = placed.Count - 1; k >= lineStart; k--)
                {
                    var pit = placed[k].item;
                    if (pit.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow or InlineItemKind.Float) continue;
                    if (pit.Kind == InlineItemKind.Text && pit.Text is { Length: > 0 } pt &&
                        char.IsWhiteSpace(pt[^1]) &&
                        pit.Node?.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
                    {
                        var stripped = lt.TrimStartCss();
                        if (stripped.Length == 0) continue;
                        var stripW = StrippedWidth(item.Node, stripped);
                        item = item with { Text = stripped, Width = stripW, ContentW = stripW };
                    }
                    break;
                }
            }

            // For text items wider than the available space, re-measure with wrapping
            var effectiveItem = item;
            if (item.Kind == InlineItemKind.Text && item.Text != null)
            {
                var availW = bandRight - lineX;
                // An item that only overflows by its TRAILING collapsible space fits (the fit
                // check above passed it): re-measuring it here would re-wrap the full text into
                // a rect sized without that space, and the wrap's own line-start strip then ate
                // the LEADING space of " d " — the abs-pos overlays of the white-space band drew
                // their last word one space left of the text-run reference.
                if (availW > 0 && fitWidth > availW && SplitsPerLine(item, lineAlign) &&
                    SplitToFit(item, availW) is { } fit)
                {
                    // §9.4.2/§16.6: an overflowing run breaks at its last opportunity that fits;
                    // the rest starts a new line box as an item of its own, so whatever follows
                    // the run sits beside its LAST line. Re-measuring it as one tall wrapped item
                    // instead placed the next content past the item's full width
                    // (c5502-imrgn-r-003: 'xx' + a span's closing margin + ' xx' on the run's
                    // last line).
                    items[itemIndex] = fit.Head;
                    items.Insert(itemIndex + 1, fit.Tail);
                    item = fit.Head;
                    effectiveItem = item;
                    item.Node.LineBands = null;
                }
                else if (availW > 0 && fitWidth > availW)
                {
                    using var font = TextMeasure.CreateFont(item.Node);
                    var ws = item.Node.GetWhiteSpace();
                    var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
                    // The paragraph wraps line by line against the band each line actually has:
                    // beside a float the first lines are narrow and the ones below it are not.
                    var bands = LineBandsFor(floats, originX + lineX, originY + lineY, lh,
                                             originX, maxWidth, availW);
                    if (bands is null && lineX > bandLeft)
                    {
                        // A wrapped item starting mid-band with no floats in play (the span's
                        // own leading margin): the FIRST band begins at the item's x and the
                        // continuation lines at the line's band start — an inline box's left
                        // margin renders on its first fragment only (§8.3). Without the bands
                        // the painter indented EVERY wrapped line (text-indent-wrap-001).
                        bands = new List<(float X, float Width)>
                        {
                            (originX + lineX, availW),
                            (originX + bandLeft, bandRight - bandLeft),
                        };
                    }
                    var wrapLines = TextMeasure.WrapText(item.Text, Math.Max(availW, 1f), font, ws, lh,
                                                         bands?.Select(b => b.Width).ToList());
                    var wrappedH = wrapLines.Sum(l => l.Height);
                    var usedW = wrapLines.Count > 0 ? wrapLines.Max(l => l.Width) : availW;
                    item.Node.LineBands = bands?.Select(b => (b.X - (originX + lineX), b.Width)).ToList();
                    // Per-line slices with their band-anchored x, for the painter's fragment
                    // records: a wrapped item paints one fragment per line (the background of
                    // an indented span covers line 1 from the margin edge and the rest from the
                    // band start), instead of one rect every line shares.
                    var slices = bands is { Count: > 0 }
                        ? wrapLines.Select((l, i) =>
                        {
                            var b = bands[Math.Min(i, bands.Count - 1)];
                            return (l.Text, b.X, Math.Min(b.Width, l.Width), l.Height);
                        }).ToList()
                        : null;
                    effectiveItem = item with { Width = Math.Max(availW, usedW), Height = wrappedH,
                                                ContentW = availW, ContentH = wrappedH, WrappedLines = slices };
                }
                else item.Node.LineBands = null;
            }

            // §10.8: the vertical edges of a non-replaced inline box do not enter the line box's
            // height calculation, so an edge spacer only advances the pen.
            if (effectiveItem.Kind == InlineItemKind.InlineEdge)
            {
                placed.Add((effectiveItem, lineX, lineY));
                placedLine.Add(currentLine);
                lineX += effectiveItem.Width;
                continue;
            }

            var va = effectiveItem.Node.GetVerticalAlign();
            switch (va)
            {
                case VerticalAlignType.Top:
                    maxTopH = Math.Max(maxTopH, effectiveItem.Height);
                    break;
                case VerticalAlignType.Bottom:
                    maxBottomH = Math.Max(maxBottomH, effectiveItem.Height);
                    break;
                default:
                    maxAbove = Math.Max(maxAbove, AboveBaselineComponent(effectiveItem, va));
                    maxBelow = Math.Max(maxBelow, effectiveItem.Height - AboveBaselineComponent(effectiveItem, va));
                    hasBaselineItem = true;
                    break;
            }

            placed.Add((effectiveItem, lineX, lineY));
            placedLine.Add(currentLine);
            lineX += effectiveItem.Width;
        }
        if (placed.Count > lineStart) CommitLine();

        // A node can end up with several fragments (a forced break inside it, or a run that wrapped);
        // start each layout pass from a clean list so a re-layout does not accumulate stale ones.
        foreach (var (item, _, _) in placed)
        {
            item.Node.InlineFragments = null;
            for (var a = item.Node; a is not null; a = a.Parent)
            {
                if (a != item.Node && a.GetDisplay() != DisplayType.Inline) break;
                a.InlineFragmentBoxes = null;
            }
        }

        // §8.4/§8.6: a boxless inline wrapper's borders paint PER LINE, so line layout assembles
        // each wrapper's per-line border-box fragments from its edge items (which side landed on
        // the line — mirrored for a rtl box split by reordering) and its content's rects.
        var wrapperFragments = new Dictionary<LayoutNode, Dictionary<int, (SKRect? Rect, bool HasLeading, bool HasTrailing)>>();
        for (var pi = 0; pi < placed.Count; pi++)
        {
            var (item, relX, relY) = placed[pi];
            // §16.6.1: collapsible spaces at the END of a line are removed, so the inline boxes
            // around them do not reach over them: a span broken after 'xx ' painted its
            // background under the hanging space (c5502-imrgn-r-002's red notch). The text
            // still paints its spaces; they are invisible ink at the line's end.
            var folded = item;
            if (IsSingleLineText(item) && item.Text is { Length: > 0 } t && IsBreakSpace(t[^1]) &&
                item.Node.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap) &&
                (pi + 1 >= placed.Count || placedLine[pi + 1] != placedLine[pi]))
            {
                using var trimFont = TextMeasure.CreateFont(item.Node);
                var (trimLs, trimWsp) = TextMeasure.SpacingOf(item.Node);
                var trimmed = TextMeasure.MeasureSingleLine(t.TrimEndCss(), trimFont, -1f, trimLs, trimWsp).Width;
                folded = item with { Width = trimmed, ContentW = trimmed };
            }
            ApplyInlineItem(folded, originX + relX, originY + relY, viewportWidth, viewportHeight);
            AccumulateWrapperFragment(wrapperFragments, folded, originX + relX, originY + relY, placedLine[pi]);
        }
        FlushWrapperFragments(wrapperFragments);

        lastLineTop = committedLineTop;
        lastLineWidth = committedLineWidth;
        return lineY;
    }

    /// <summary>§8.4/§8.6 wrapper-fragment assembly: folds one placed item into the per-line
    /// fragment accumulators of every inline wrapper above it. A text item feeds its content
    /// rect to its inline ancestors (the element carrying its own text paints from its Box); an
    /// edge item feeds its span to its own wrapper and the wrappers above, with the side flag
    /// saying WHICH of the box's edges landed on this line (mirrored for a rtl box split by
    /// reordering, §8.6).</summary>
    private static void AccumulateWrapperFragment(
        Dictionary<LayoutNode, Dictionary<int, (SKRect? Rect, bool HasLeading, bool HasTrailing)>> acc,
        InlineItem item, float absX, float absY, int lineIndex)
    {
        if (item.Kind is InlineItemKind.OutOfFlow or InlineItemKind.LineBreak) return;
        // A whitespace-only text item paints no ink and (§16.6.1) holds no line slot — folding
        // its rect gave the wrapper a border fragment around collapsed space (the XML-served
        // border-color-012 markup's inter-span whitespace grew an inner border box).
        if (item.Kind == InlineItemKind.Text && (item.Text ?? "").IsCssWhiteSpaceOnly()) return;
        if (item.Kind == InlineItemKind.InlineEdge)
        {
            var span = new SKRect(absX, absY, absX + item.Width, absY);
            var node = item.Node;
            while (true)
            {
                Fold(acc, node, lineIndex, span, !item.TrailingEdge, item.TrailingEdge);
                node = node.Parent;
                if (node is null || node.GetDisplay() != DisplayType.Inline) break;
            }
            return;
        }
        var rect = new SKRect(absX, absY, absX + item.Width, absY + item.Height);
        // A strut is its own element's (empty) content: it folds onto that element first.
        for (var node = item.Kind == InlineItemKind.Strut ? item.Node : item.Node.Parent; node is not null; node = node.Parent)
        {
            // Anonymous boxes (a :before/:after pseudo's inline box, synthesized text) resolve
            // their styles from the element that produced them — folding the host's border onto
            // their fragment painted the host's box INSIDE its own border (the :before of a
            // bordered div grew an inner ring). They are transparent to the fold.
            if (node.TagName.StartsWith('#') || node.GetDisplay() != DisplayType.Inline) break;
            // §10.6.1: a non-replaced inline box's content area is its OWN font's ascent and
            // descent about the baseline — not the line-height box its text was laid out in.
            // Folding the text's line-height rect made the background, border and outline of
            // a span in a tall line ('font: 2em/2') span the whole line, so the boxes of two
            // wrapped lines met and overlapped (inlines-003, outline-020).
            var folded = rect;
            // A baseline-aligned atomic box (an image in a span) does not stretch the span's
            // content area either: the span's background covers its own font's content area,
            // which reaches below the image's baseline (line-height-122's green line).
            if (item.Kind is InlineItemKind.Text or InlineItemKind.Strut ||
                IsAtomicInline(item) && item.Node.GetVerticalAlign() == VerticalAlignType.Baseline)
            {
                var (ascent, descent) = ContentAreaMetrics(node);
                var baseline = absY + item.Ascent;
                folded = new SKRect(rect.Left, baseline - ascent, rect.Right, baseline + descent);
            }
            Fold(acc, node, lineIndex, folded, null, null);
        }
    }

    /// <summary>The font ascent and descent (both positive) that bound an inline box's content
    /// area (§10.6.1).</summary>
    private static (float Ascent, float Descent) ContentAreaMetrics(LayoutNode node)
    {
        using var font = TextMeasure.CreateFont(node);
        return (-font.Metrics.Ascent, font.Metrics.Descent);
    }

    /// <summary>§10.6.1: a non-replaced inline box's content area is its font's ascent plus
    /// descent, centred in the line-height box its text was laid out in (the half-leading sits
    /// outside it). A text-bearing inline's <see cref="LayoutNode.Box"/> (and each of its
    /// <see cref="LayoutNode.InlineFragments"/>) stays the line-height box layout placed and the
    /// text paints from; its background, borders and outline paint around the content area this
    /// returns. Painting them around the line-height box made a span in a tall line
    /// ('font: 2em/2') span the whole line, so the boxes of wrapped lines met (inlines-003,
    /// outline-020). A rect that is not one line-height tall is returned unchanged.</summary>
    public static SKRect InlineContentArea(LayoutNode node, SKRect lineRect)
    {
        var lh = node.GetLineHeight(node.GetFontSize());
        if (MathF.Abs(lineRect.Height - lh) > 0.5f) return lineRect;
        var (ascent, descent) = ContentAreaMetrics(node);
        var top = lineRect.Top + (lh - (ascent + descent)) / 2f;
        return new SKRect(lineRect.Left, top, lineRect.Right, top + ascent + descent);
    }

    /// <summary>Whether a point falls inside a node's border box for hit testing. An inline
    /// box split over several lines is hit only inside one of its line fragments: its
    /// <see cref="LayoutNode.Box"/> is their bounding union, which also spans whatever sits
    /// beside its first and last lines (a link earlier on the first line was hit as the text
    /// after it, so pressing it did not activate it — c21-activ-ln-000).</summary>
    public static bool HitsBorderBox(LayoutNode node, float x, float y)
    {
        var box = node.Box.BorderBox;
        if (box.Width <= 0 || box.Height <= 0 || !box.Contains(x, y)) return false;
        if (node.InlineFragments is not { Count: > 1 } fragments) return true;
        foreach (var (rect, _) in fragments)
            if (rect.Contains(x, y)) return true;
        return false;
    }

    /// <summary><see cref="InlineContentArea(LayoutNode, SKRect)"/> applied to a box's content
    /// rect; padding and border extend outward from it.</summary>
    public static BoxDimensions InlineContentArea(LayoutNode node, BoxDimensions box)
    {
        box.ContentBox = InlineContentArea(node, box.ContentBox);
        return box;
    }

    private static void Fold(
        Dictionary<LayoutNode, Dictionary<int, (SKRect? Rect, bool HasLeading, bool HasTrailing)>> acc,
        LayoutNode node, int lineIndex, SKRect rect, bool? leading, bool? trailing)
    {
        if (!acc.TryGetValue(node, out var lines))
            acc[node] = lines = [];
        if (!lines.TryGetValue(lineIndex, out var entry))
            lines[lineIndex] = (rect, leading ?? false, trailing ?? false);
        else
            lines[lineIndex] = (entry.Rect is { } r ? SKRect.Union(r, rect) : rect,
                entry.HasLeading || (leading ?? false), entry.HasTrailing || (trailing ?? false));
    }

    /// <summary>§8.4/§8.6: turns a wrapper's per-line unions into border-box fragments for the
    /// painter. A side's margin/padding/border zones exist only on lines where that side's edge
    /// item landed — the join sides hug the content; the top/bottom borders paint outside the
    /// content area on every line (nothing vertical is reserved in the line box).</summary>
    private static void FlushWrapperFragments(
        Dictionary<LayoutNode, Dictionary<int, (SKRect? Rect, bool HasLeading, bool HasTrailing)>> acc)
    {
        foreach (var (node, lines) in acc)
        {
            var boxes = new List<BoxDimensions>();
            var margin = node.GetMargin(0, 0, node.GetFontSize());
            var padding = node.GetPadding(0, 0, node.GetFontSize());
            var border = node.GetBorderWidth();
            // §8.6 split-vs-unsplit: content on ONE painted line means an unsplit box — all
            // four sides paint, and a side whose edge item landed elsewhere (e.g. a whitespace
            // line that §9.4.2 turns into no line box at all) hangs OUTWARD past the content,
            // exactly like the element-text Box path. Content on SEVERAL lines is a split box:
            // a side masks to the join unless its own edge landed on that line.
            var painted = lines.Where(static l => l.Value.Rect is { } r && r.Height > 0f)
                               .OrderBy(static l => l.Key).ToList();
            var unsplit = painted.Count == 1;
            foreach (var kv in lines.OrderBy(static l => l.Key))
            {
                if (kv.Value.Rect is not { } rect || rect.Height <= 0f) continue;
                var (hasLeading, hasTrailing) = (kv.Value.HasLeading, kv.Value.HasTrailing);
                var keepLeft = hasLeading || unsplit;
                var keepRight = hasTrailing || unsplit;
                boxes.Add(new BoxDimensions
                {
                    // Horizontally a reserved side's zones sit INSIDE the union (deflate this
                    // box's own margin/padding/border); a join side hugs the content; an
                    // unsplit box's unreserved side hangs OUTWARD. Vertically nothing is
                    // reserved, so the border box always extends OUTWARD past the content
                    // area (BorderBox.Top = rect.Top - pad - border).
                    ContentBox = new SKRect(
                        hasLeading ? rect.Left + margin.Left + padding.Left + border.Left : rect.Left,
                        rect.Top,
                        hasTrailing ? rect.Right - margin.Right - padding.Right - border.Right : rect.Right,
                        rect.Bottom),
                    Padding = new EdgeSizes
                    {
                        Top = padding.Top,
                        Bottom = padding.Bottom,
                        Left = keepLeft ? padding.Left : 0f,
                        Right = keepRight ? padding.Right : 0f,
                    },
                    Border = new EdgeSizes
                    {
                        Top = border.Top,
                        Bottom = border.Bottom,
                        Left = keepLeft ? border.Left : 0f,
                        Right = keepRight ? border.Right : 0f,
                    },
                });
            }
            node.InlineFragmentBoxes = boxes.Count > 0 ? boxes : null;
        }
    }

    /// <summary>
    /// For an item aligned baseline/sub/super/middle/length/percentage, the distance from ITS
    /// OWN top down to wherever it aligns against the line's shared baseline (CSS 2.1 §10.8.1).
    /// 'baseline' uses the item's intrinsic ascent directly; 'sub'/'super' shift it down/up by
    /// ~0.15em; 'middle' aligns the item's vertical centre with the baseline plus half the
    /// PARENT box's x-height (its first font's, Ahem's 0.8em); a &lt;length&gt;/&lt;percentage&gt;
    /// raises (positive) or lowers (negative) the box's baseline by that distance.
    /// </summary>
    private static float AboveBaselineComponent(InlineItem item, VerticalAlignType vAlign)
    {
        var fontSize = item.Node.GetFontSize();
        var above = vAlign switch
        {
            VerticalAlignType.Middle => item.Height / 2f + HalfParentXHeight(item.Node),
            // §10.8.1: 'text-top'/'text-bottom' align with the PARENT's content area (its font's
            // ascent above / descent below the baseline) — baseline-relative, not the line box's
            // top/bottom they were treated as (vertical-align-117a, vertical-align-top-bottom-001).
            VerticalAlignType.TextTop => ParentContentArea(item.Node).Ascent,
            VerticalAlignType.TextBottom => item.Height - ParentContentArea(item.Node).Descent,
            VerticalAlignType.Sub => item.Ascent - fontSize * 0.15f,
            VerticalAlignType.Super => item.Ascent + fontSize * 0.15f,
            _ => item.Ascent, // baseline
        };
        if (vAlign is VerticalAlignType.Length or VerticalAlignType.Percentage)
            above += item.Node.GetVerticalAlignOffset();
        return above;
    }

    /// <summary>The ascent and descent (both positive) of the aligned box's PARENT's first
    /// font — its content area about the baseline (§10.6.1), which 'text-top' and
    /// 'text-bottom' align with.</summary>
    private static (float Ascent, float Descent) ParentContentArea(LayoutNode node)
    {
        var aligned = node.TagName.StartsWith('#') ? node.Parent ?? node : node;
        return ContentAreaMetrics(aligned.Parent ?? aligned);
    }

    /// <summary>§10.8.1 'middle': half the x-height of the aligned box's PARENT. A text item
    /// is aligned through the element that holds it, so its parent is that element's parent.
    /// A quarter of the item's own font size stood in for it, lowering a middle-aligned Ahem
    /// box 7px against its 48px parent (vertical-align-114).</summary>
    private static float HalfParentXHeight(LayoutNode node)
    {
        var aligned = node.TagName.StartsWith('#') ? node.Parent ?? node : node;
        var parent = aligned.Parent ?? aligned;
        return parent.GetFontSize() * TextMeasure.XHeightRatio(parent) / 2f;
    }

    /// <summary>White-space modes that allow soft wrapping (CSS 2.1 16.6): 'nowrap' and 'pre' never do.</summary>
    private static bool WrapsLines(LayoutNode node) =>
        node.GetWhiteSpace() is WhiteSpace.Normal or WhiteSpace.PreWrap or WhiteSpace.PreLine;

    /// <summary>A collapsible or preserved space that offers a break: NOT a no-break space.</summary>
    private static bool IsBreakSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsAtomicInline(InlineItem item) =>
        item.Kind is InlineItemKind.Image or InlineItemKind.InlineBlock or InlineItemKind.InlineFlex or InlineItemKind.InlineTable;

    private static bool IsAncestorOrSelf(LayoutNode ancestor, LayoutNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// Whether a line may break between the content already on the line (from
    /// <paramref name="lineStart"/>) and <paramref name="next"/>. A space at the boundary under a
    /// wrapping white-space mode is an opportunity, so is an atomic inline beside it (treated as
    /// an ideograph, CSS Text) and a &lt;wbr&gt;; text meeting text with no space is not: the
    /// boundary of an inline box is no break opportunity on its own.
    /// </summary>
    private static bool BreakAllowedBefore(List<(InlineItem item, float relX, float relY)> placed, int lineStart, InlineItem next)
    {
        InlineItem? prev = null;
        for (var k = placed.Count - 1; k >= lineStart; k--)
        {
            var it = placed[k].item;
            if (it.Kind == InlineItemKind.InlineEdge)
            {
                if (it.Node.TagName == "WBR") return true;
                continue;
            }
            if (it.Kind == InlineItemKind.OutOfFlow) continue;
            prev = it;
            break;
        }
        if (prev is null)
        {
            // The line holds only edge items so far (the span's own leading margin, say):
            // a break is the NEXT item's own decision, not a free one. A word glued to the
            // box edge ('<span style="margin-left:100px">This…') has no break opportunity
            // between the boundary and the letter — committing the edge as an empty first
            // line box both phantom-wrapped the line and dropped the indent
            // (text-indent-wrap-001's inline-margin reference rendered the paragraph one
            // line low and unindented).
            return next.Kind == InlineItemKind.Text && next.Text is { Length: > 0 } nx &&
                   IsBreakSpace(nx[0]) && WrapsLines(next.Node);
        }
        return BreakBetween(prev, next);
    }

    private static bool BreakBetween(InlineItem prev, InlineItem next)
    {
        // The space checks consult BOTH sides before deciding: the whitespace between two
        // inline siblings collapses across the boundary, and the soft wrap opportunity
        // survives when EITHER element's own space allows it. Checking the next side alone
        // suppressed every break into a white-space: nowrap span whose leading space sat
        // next to a normal element's breakable trailing space (float-nowrap-3: 'Some' plus a
        // nowrap span never wrapped, the whole line overflowed and its nested float vanished).
        if (next.Kind == InlineItemKind.Text && next.Text is { Length: > 0 } nt && IsBreakSpace(nt[0]) &&
            WrapsLines(next.Node))
            return true;
        if (prev.Kind == InlineItemKind.Text && prev.Text is { Length: > 0 } pt && IsBreakSpace(pt[^1]) &&
            WrapsLines(prev.Node))
            return true;
        if (IsAtomicInline(next)) return WrapsLines(next.Node.Parent ?? next.Node) && (prev.Kind != InlineItemKind.Text || WrapsLines(prev.Node));
        if (IsAtomicInline(prev)) return WrapsLines(prev.Node.Parent ?? prev.Node) && (next.Kind != InlineItemKind.Text || WrapsLines(next.Node));
        return false;
    }

    /// <summary>
    /// The placed index at which the current line can last be cut: just after the content item
    /// preceding the latest break opportunity, keeping the closing edges of the inline boxes that
    /// content belongs to (a span's end padding stays with its last word). Null when the line has
    /// no opportunity after its first item.
    /// </summary>
    private static int? LastOpportunity(List<(InlineItem item, float relX, float relY)> placed, int lineStart)
    {
        for (var k = placed.Count - 1; k > lineStart; k--)
        {
            var it = placed[k].item;
            if (it.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow) continue;
            // Find the content before entry k and decide whether the boundary breaks.
            var j = k - 1;
            var wbr = false;
            while (j >= lineStart && placed[j].item.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow)
            {
                if (placed[j].item.Node.TagName == "WBR") wbr = true;
                j--;
            }
            if (j < lineStart) continue;
            if (!wbr && !BreakBetween(placed[j].item, it)) continue;
            var cut = j + 1;
            while (cut < k && placed[cut].item.Kind == InlineItemKind.InlineEdge &&
                   IsAncestorOrSelf(placed[cut].item.Node, placed[j].item.Node))
                cut++;
            return cut;
        }
        return null;
    }

    /// <summary>
    /// Splits text item <paramref name="index"/> after its first run of break spaces when its
    /// white-space wraps, so the part glued to the preceding content stays on the line while the
    /// rest starts after a real break opportunity. False when there is nothing to split off.
    /// </summary>
    private static bool SplitAtFirstOpportunity(List<InlineItem> items, int index)
    {
        var item = items[index];
        if (item.Kind != InlineItemKind.Text || item.Text is not { Length: > 1 } text || !WrapsLines(item.Node))
            return false;
        var space = 0;
        while (space < text.Length && !IsBreakSpace(text[space])) space++;
        if (space == 0 || space >= text.Length) return false;
        var end = space;
        while (end < text.Length && IsBreakSpace(text[end])) end++;
        if (end >= text.Length) return false;

        using var font = TextMeasure.CreateFont(item.Node);
        var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
        var (ls, wsp) = TextMeasure.SpacingOf(item.Node);
        var head = text[..end];
        var tail = text[end..];
        var (hw, hh, ha) = TextMeasure.MeasureSingleLine(head, font, lh, ls, wsp);
        var (tw, th, ta) = TextMeasure.MeasureSingleLine(tail, font, lh, ls, wsp);
        items[index] = item with { Text = head, Width = hw, Height = hh, ContentW = hw, ContentH = hh, Ascent = ha };
        items.Insert(index + 1, item with { Text = tail, Width = tw, Height = th, ContentW = tw, ContentH = th, Ascent = ta });
        return true;
    }

    /// <summary>The placed index of the line's last content item (not an inline edge or an
    /// out-of-flow record), or null when the line holds none after its start.</summary>
    private static int? LastContentIndex(List<(InlineItem item, float relX, float relY)> placed, int lineStart)
    {
        for (var k = placed.Count - 1; k >= lineStart; k--)
            if (placed[k].item.Kind is not (InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow))
                return k;
        return null;
    }

    /// <summary>The indices of the floats in the run glued to item <paramref name="index"/>
    /// (up to the next break opportunity), last first so they can be removed in turn.</summary>
    private static List<int> FloatsInGluedRun(List<InlineItem> items, int index)
    {
        var found = new List<int>();
        for (var j = index; j < items.Count; j++)
        {
            var it = items[j];
            if (it.Kind == InlineItemKind.Float) { found.Add(j); continue; }
            if (j > index && it.Kind == InlineItemKind.Text && it.Text is { Length: > 0 } t &&
                WrapsLines(it.Node) && IsBreakSpace(t[0]))
                break;
            if (it.Kind is InlineItemKind.LineBreak) break;
        }
        found.Reverse();
        return found;
    }

    /// <summary>Whether an overflowing text item breaks into one item per line (see
    /// <see cref="SplitToFit"/>) rather than being re-measured as one wrapped block. Justified
    /// text keeps the wrapped form: the painter spreads a justified line's gaps across the lines
    /// of one item, and a one-line slice has no "last line" to leave ragged.</summary>
    private static bool SplitsPerLine(InlineItem item, TextAlign lineAlign) =>
        item.Kind == InlineItemKind.Text && WrapsLines(item.Node) &&
        item.Node.GetWhiteSpace() == WhiteSpace.Normal &&
        lineAlign != TextAlign.Justify && item.Node.GetTextAlign() != TextAlign.Justify;

    /// <summary>Breaks a text item at its last opportunity whose text (trailing spaces
    /// hanging, §16.6.1) fits <paramref name="availW"/>; when not even the first word fits, at
    /// the first opportunity (that word overflows alone). The head keeps the spaces after its
    /// last word. Null when the text has no opportunity before its last word.</summary>
    private static (InlineItem Head, InlineItem Tail)? SplitToFit(InlineItem item, float availW, bool requireFit = false)
    {
        if (item.Text is not { Length: > 1 } text) return null;
        using var font = TextMeasure.CreateFont(item.Node);
        var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
        var (ls, wsp) = TextMeasure.SpacingOf(item.Node);
        var start = 0;
        while (start < text.Length && IsBreakSpace(text[start])) start++;   // leading spaces are no opportunity
        var cut = -1;
        for (var i = start; i < text.Length; i++)
        {
            if (!IsBreakSpace(text[i])) continue;
            var wordEnd = i;
            while (i < text.Length && IsBreakSpace(text[i])) i++;
            if (i >= text.Length) break;                                    // trailing spaces only
            var width = TextMeasure.MeasureSingleLine(text[..wordEnd], font, lh, ls, wsp).Width;
            if (width > availW + 0.01f)
            {
                if (cut < 0 && !requireFit) cut = i;   // not even the first word fits: it overflows alone
                break;
            }
            cut = i;
            i--;
        }
        if (cut <= 0 || cut >= text.Length) return null;
        var head = text[..cut];
        var tail = text[cut..];
        var (hw, hh, ha) = TextMeasure.MeasureSingleLine(head, font, lh, ls, wsp);
        var (tw, th, ta) = TextMeasure.MeasureSingleLine(tail, font, lh, ls, wsp);
        return (item with { Text = head, Width = hw, Height = hh, ContentW = hw, ContentH = hh, Ascent = ha, WrappedLines = null },
                item with { Text = tail, Width = tw, Height = th, ContentW = tw, ContentH = th, Ascent = ta, WrappedLines = null });
    }

    /// <summary>True when everything after item <paramref name="index"/> is a closing
    /// inline-box edge, an out-of-flow marker or more collapsible whitespace — nothing that
    /// renders on the line.</summary>
    private static bool OnlyClosingEdgesFollow(List<InlineItem> items, int index)
    {
        for (var j = index + 1; j < items.Count; j++)
        {
            var it = items[j];
            if (it.Kind is InlineItemKind.OutOfFlow || it is { Kind: InlineItemKind.InlineEdge, TrailingEdge: true })
                continue;
            // More collapsible whitespace collapses into this one.
            if (it.Kind == InlineItemKind.Text && it.Text is { } t && t.IsCssWhiteSpaceOnly() &&
                it.Node?.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
                continue;
            return false;
        }
        return true;
    }

    /// <summary>The width of a text slice with its node's font and letter/word spacing.</summary>
    private static float StrippedWidth(LayoutNode node, string text)
    {
        using var font = TextMeasure.CreateFont(node);
        var (ls, wsp) = TextMeasure.SpacingOf(node);
        return TextMeasure.MeasureWithSpacing(text, font, ls, wsp);
    }

    /// <summary>A text item laid out as ONE line. A re-measured item that wrapped carries
    /// <see cref="InlineItem.WrappedLines"/> only when floats gave it bands; its height (the sum
    /// of its lines) is what always tells.</summary>
    private static bool IsSingleLineText(InlineItem item) =>
        item.Kind == InlineItemKind.Text && item.WrappedLines is null &&
        item.Height <= item.Node.GetLineHeight(item.Node.GetFontSize()) + 0.5f;

    /// <summary>Splits a single-line, wrapping text item after its LAST run of break spaces
    /// (the head keeps those spaces, which hang at the line end, §16.6.1). Null when the text
    /// has no break opportunity before its final word.</summary>
    private static (InlineItem Head, InlineItem Tail)? SplitAtLastOpportunity(InlineItem item)
    {
        if (!IsSingleLineText(item) || item.Text is not { Length: > 1 } text || !WrapsLines(item.Node))
            return null;
        var end = text.Length;
        while (end > 0 && IsBreakSpace(text[end - 1])) end--;   // trailing spaces are not a split
        var start = end;
        while (start > 0 && !IsBreakSpace(text[start - 1])) start--;
        if (start == 0 || start >= end) return null;
        var spacesStart = start;
        while (spacesStart > 0 && IsBreakSpace(text[spacesStart - 1])) spacesStart--;
        if (spacesStart == 0) return null;                       // only leading spaces
        using var font = TextMeasure.CreateFont(item.Node);
        var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
        var (ls, wsp) = TextMeasure.SpacingOf(item.Node);
        var head = text[..start];
        var tail = text[start..];
        var (hw, hh, ha) = TextMeasure.MeasureSingleLine(head, font, lh, ls, wsp);
        var (tw, th, ta) = TextMeasure.MeasureSingleLine(tail, font, lh, ls, wsp);
        return (item with { Text = head, Width = hw, Height = hh, ContentW = hw, ContentH = hh, Ascent = ha },
                item with { Text = tail, Width = tw, Height = th, ContentW = tw, ContentH = th, Ascent = ta });
    }

    /// <summary>Recomputes the line's baseline accumulators from what is placed on it — needed
    /// after the line is cut back to an earlier break opportunity.</summary>
    private static (float Above, float Below, float TopH, float BottomH, bool HasBaseline) LineMetrics(
        List<(InlineItem item, float relX, float relY)> placed, int lineStart)
    {
        float above = 0f, below = 0f, topH = 0f, bottomH = 0f;
        var hasBaseline = false;
        for (var k = lineStart; k < placed.Count; k++)
        {
            var it = placed[k].item;
            if (it.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow) continue;
            var va = it.Node.GetVerticalAlign();
            switch (va)
            {
                case VerticalAlignType.Top:
                    topH = Math.Max(topH, it.Height);
                    break;
                case VerticalAlignType.Bottom:
                    bottomH = Math.Max(bottomH, it.Height);
                    break;
                default:
                    above = Math.Max(above, AboveBaselineComponent(it, va));
                    below = Math.Max(below, it.Height - AboveBaselineComponent(it, va));
                    hasBaseline = true;
                    break;
            }
        }
        return (above, below, topH, bottomH, hasBaseline);
    }

    /// <summary>A child that puts nothing on the line: collapsible whitespace, a comment or a
    /// display:none element (a &lt;script&gt; left alone in a split inline's first piece,
    /// block-in-inline-insert-015).</summary>
    private static bool RendersNothingInline(LayoutNode node) =>
        IsCollapsibleWhitespaceText(node) || node.TagName is "#comment" or "#pi" ||
        node.GetDisplay() == DisplayType.None;

    /// <summary>A text node holding only collapsible whitespace (no rendered glyph; a no-break
    /// space is content).</summary>
    private static bool IsCollapsibleWhitespaceText(LayoutNode node) =>
        node.TagName == "#text" && (node.DisplayText ?? "").IsCssWhiteSpaceOnly() &&
        node.GetWhiteSpace() is not (WhiteSpace.Pre or WhiteSpace.PreWrap);

    /// <summary>An element that is an empty, non-replaced inline box (no children, no text):
    /// replaced and form elements keep their own paths.</summary>
    private static bool IsEmptyNonReplacedInline(LayoutNode node) =>
        !node.TagName.StartsWith('#') && string.IsNullOrEmpty(node.DisplayText) &&
        node.TagName is not ("IMG" or "CANVAS" or "VIDEO" or "AUDIO" or "IFRAME" or "OBJECT" or "EMBED"
            or "SVG" or "svg" or "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON" or "METER" or "PROGRESS"
            or "SCRIPT" or "STYLE" or "TEMPLATE") &&
        !IsReplacedImage(node);

    private static InlineItem EdgeItem(LayoutNode node, float width, bool trailing = false) =>
        new(InlineItemKind.InlineEdge, node, null, width, 0, default, default, default, width, 0, 0, trailing);

    /// <summary>
    /// CSS 2.1 §8.4: the HORIZONTAL margins and padding of a non-replaced inline box are laid out
    /// — they push whatever follows along the line — while its vertical ones do not affect the
    /// line box's height. Returns the widths to reserve before and after the box's own content.
    /// </summary>
    internal static (float Leading, float Trailing) InlineBoxEdges(
        LayoutNode node, float maxWidth, float viewportHeight)
    {
        // Anonymous boxes (#text and friends) resolve their styles from the element they came
        // from, so asking them for a margin would apply the parent's a second time.
        if (node.TagName.StartsWith('#')) return (0f, 0f);

        if (node.IsBrokenAroundBlock()) return (0f, 0f);

        // §8.6 (bidi box model): an inline box that is NOT split by bidi reordering keeps its
        // physical sides — 'margin-left' reserves space on the left of the line whatever the
        // element's direction is. The start/end-side placement §8.6 prescribes for boxes SPLIT
        // across reordered fragments needs the full UBA (mixed-direction runs); until then the
        // reversed scopes this engine produces keep every inline box contiguous, so the sides
        // stay physical here too. (The bidi-box-model margins/padding families assert exactly
        // this: side margins are unaffected by directionality.)

        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(maxWidth, viewportHeight, fontSize);
        var padding = node.GetPadding(maxWidth, viewportHeight, fontSize);
        // §8.4: the horizontal MARGIN, PADDING and BORDER of a non-replaced inline box are laid
        // out — borders were omitted here, so a 90px inline border advanced the line by nothing
        // and its ring painted (or clipped) 180px too far left (border-width-applies-to-008).
        var border = node.GetBorderWidth();
        // 'auto' is 0 on an inline box (§10.3.1), not the block-level centring rule.
        var left = (node.IsAutoMarginLeft() ? 0f : margin.Left) + padding.Left + border.Left;
        var right = (node.IsAutoMarginRight() ? 0f : margin.Right) + padding.Right + border.Right;
        return (left, right);
    }

    /// <summary>Whether any inline box in <paramref name="root"/>'s subtree carries horizontal
    /// edge space (margin, padding or border). The bidi scope split reads this: an override
    /// scope WITHOUT edge boxes routes to the paragraph-level reordering (the §16.2 pass
    /// reproduces the whole-stream reversal exactly), while a scope WITH them keeps the legacy
    /// child-sequence reversal — the §8.6 physical-side edge reservation keys on it, and
    /// the paragraph pass does not model split-box edges. The available width only matters for
    /// percentage margins, which the bidi families do not use; a generous value keeps the
    /// predicate a pure function of the declared edges.</summary>
    internal static bool SubtreeHasEdgeBoxes(LayoutNode root)
    {
        var pending = new System.Collections.Generic.Stack<LayoutNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            var (leading, trailing) = InlineBoxEdges(node, 4096f, 600f);
            if (leading > 0f || trailing > 0f) return true;
            foreach (var child in node.Children) pending.Push(child);
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // CSS 2.1 §16.2 / UAX #9 subset
    // -------------------------------------------------------------------------

    // The bidi scope predicates (BidiIsRtlOverride / BidiReversesContent / ReverseText) live on
    // DrawCommandExtensions beside the direction/unicode-bidi style readers, because the
    // reversal has to agree between layout and painting through RenderedText. Layout's two
    // additional obligations are the child-SEQUENCE reversals below: inside a reversed scope
    // the character stream reverses as a whole, so the child order reverses at every level,
    // while each inline box's own edge items stay at its physical boundaries (§8.6 — side
    // margins are unaffected by directionality).

    /// <summary>The paragraph-level bidirectional reordering (§16.2 / UAX #9), applied to the
    /// collected inline items when the composed run carries RTL-class content or an embedding
    /// scope and fits one line. Returns null when the run needs no reordering — the caller keeps
    /// the logical order.
    ///
    /// The text items compose one character stream (in logical order); <see cref="Bidi.Reorder"/>
    /// resolves the embedding levels and the L2 reordering. The visual line then composes at two
    /// granularities: each text item SPLITS at its level-run boundaries (so every fragment is
    /// homogeneous), the fragment sequence runs the L2 passes at fragment granularity (a run
    /// that crosses item boundaries reverses the fragment order), and each fragment's own chars
    /// reverse when its level is odd. With homogeneous fragments that composition is exactly the
    /// whole-stream L2 — the same order the reviewed painted-order judges compare against.
    /// Non-text items (edges, images, inline blocks) ride the fragment passes at their innermost
    /// embedding scope's level.</summary>
    private static List<InlineItem>? ReorderForBidi(List<InlineItem> items, LayoutNode? container, float firstW)
    {
        if (items.Count == 0 || container is null) return null;

        // Paragraphs under an active bidi-override scope keep the legacy whole-stream
        // reversal: the §8.6 physical-side edge reservation and the mirrored join sides of
        // split boxes key on that mechanism, and the pass's forced-R output diverges from it
        // in the twin-walk shapes (the unicode-bidi-applies-to families measure the legacy's
        // line geometry).
        foreach (var it in items)
            if (it.Node?.BidiReversesContent() == true) return null;

        // The cheap gate: strong RTL / Arabic content anywhere in the run, or an embedding
        // scope (an override reverses pure-Latin content too — bidi-box-model-003's
        // 'dnoceS tsriF' carries no RTL-class character).
        var hasBidi = false;
        var streamLength = 0;
        foreach (var it in items)
        {
            if (it.Kind != InlineItemKind.Text || it.Text is not { Length: > 0 } gate) continue;
            streamLength += gate.Length;
            if (!hasBidi && Bidi.HasBidiContent(gate)) hasBidi = true;
        }
        if (!hasBidi)
        {
            // A DECLARED override on the container reverses pure-Latin content too
            // (bidi-box-model-003's 'dnoceS tsriF' carries no RTL-class character). The UA
            // sheet's block-level unicode-bidi: embed must NOT count — it is parity-neutral
            // decoration on every block, and gating on it engaged the pass for paragraphs it
            // never needed to touch (the rtl-span padding reservation, the Latin embed spans
            // the box-model families pin).
            hasBidi = container.GetUnicodeBidi() == "bidi-override";
        }
        if (!hasBidi) return null;

        // Single-line gate: line breaking must stay on the logical order, so a run that wraps
        // keeps it (a wrapped RTL line re-evaluates the algorithm per line box — queued).
        var totalWidth = 0f;
        foreach (var it in items) totalWidth += it.Width;
        if (totalWidth > firstW + 0.5f) return null;

        var baseLtr = container.GetDirection() != "rtl";

        // The embedding scope chain per item: the embed/override declarations between the
        // item's own node and the paragraph container (exclusive — UAX #9 X8: an ancestor
        // block's embedding does not cross the paragraph boundary, so the UA sheet's
        // block-level unicode-bidi: embed must not leak into this paragraph's levels),
        // outermost first. A leaf ELEMENT's own declaration opens the scope over its content
        // (the item's node is that element); a synthesized #text node carries no declaration
        // of its own — its shared parent style object must not double-count the container's
        // scope, which the paragraph base already accounts for.
        bool ChainEquals(LayoutNode? node, List<LayoutNode> chain)
        {
            var own = ScopeChain(node);
            if (own.Count != chain.Count) return false;
            for (var i = 0; i < chain.Count; i++)
                if (!ReferenceEquals(own[i], chain[i])) return false;
            return true;
        }
        List<LayoutNode> ScopeChain(LayoutNode? node)
        {
            var chain = new List<LayoutNode>();
            var start = node is not null && node.TagName.StartsWith('#') ? node.Parent : node;
            for (var n = start; n is not null && !ReferenceEquals(n, container); n = n.Parent)
            {
                if (n.GetUnicodeBidi() is not ("embed" or "bidi-override")) continue;
                chain.Insert(0, n);
            }
            return chain;
        }
        static int ChainLevel(List<LayoutNode> chain, bool baseLtr)
        {
            // X2/X3 down the chain: each scope takes the least (odd|even) level above the level
            // its parent scope established.
            var level = baseLtr ? 0 : 1;
            foreach (var scope in chain)
            {
                var rtl = scope.GetDirection() == "rtl";
                if (rtl) level = level % 2 == 0 ? level + 1 : level + 2;
                else level = level % 2 == 0 ? level + 2 : level + 1;
            }
            return level;
        }

        // Compose the stream and record each text item's range; adjacent text items with the
        // same scope chain merge into one span (a scope's items are contiguous — collection
        // walks depth-first).
        var stream = new System.Text.StringBuilder(streamLength);
        var ranges = new List<(int ItemIndex, int Start, int Length)>();
        var spans = new List<Bidi.Span>();
        var textItems = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Kind != InlineItemKind.Text || string.IsNullOrEmpty(it.Text))
            {
                // Zero-width markers (edges, breaks, out-of-flow) take their innermost scope's
                // level so they ride the fragment passes with the content they sit between.
                continue;
            }
            var start = stream.Length;
            stream.Append(it.Text);
            ranges.Add((i, start, it.Text.Length));
            textItems.Add(i);
        }
        if (ranges.Count == 0) return null;

        // Spans from scope chains: consecutive text items sharing one chain form one span over
        // their combined range (a span per scope, not per item, keeps the X rules' nesting).
        var chainCache = new List<(List<LayoutNode> Chain, int Start, int Length)>();
        foreach (var (itemIndex, start, length) in ranges)
        {
            var chain = ScopeChain(items[itemIndex].Node);
            if (chainCache.Count > 0 && ChainEquals(items[itemIndex].Node, chainCache[^1].Chain))
                chainCache[^1] = (chainCache[^1].Chain, chainCache[^1].Start, chainCache[^1].Length + length);
            else
                chainCache.Add((chain, start, length));
        }
        foreach (var (chain, start, length) in chainCache)
        {
            if (chain.Count == 0) continue;
            var innermost = chain[^1];
            spans.Add(new Bidi.Span(start, length, innermost.GetDirection() == "rtl",
                innermost.GetUnicodeBidi() == "bidi-override"));
        }
        // The establishing block's own declaration boxes the WHOLE paragraph (§16.2: an
        // override/embed on the block applies to its inline content) — the chain walk stops at
        // the container, so this scope never appears in the per-item chains. It comes first so
        // the inner spans' X rules compute from its raised level.
        var containerBidi = container.GetUnicodeBidi();
        // Override only: the UA sheet's embed sits on every block (parity-neutral noise) and
        // a declared container embed has no corpus case — the declared span embeds arrive
        // through the chains above.
        if (containerBidi == "bidi-override")
            spans.Insert(0, new Bidi.Span(0, stream.Length, container.GetDirection() == "rtl",
                Override: true));

        var (levels, _) = Bidi.Reorder(stream.ToString(), baseLtr, spans);

        // Split every text item at its level-run boundaries, then run the L2 passes at fragment
        // granularity (order only) and reverse each odd-level fragment's own chars.
        var fragments = new List<BidiFragment>();
        foreach (var (itemIndex, start, length) in ranges)
        {
            var source = items[itemIndex];
            var logical = source.Text!;
            var runStart = 0;
            for (var k = 1; k <= length; k++)
            {
                if (k < length && levels[start + k] == levels[start + runStart]) continue;
                var slice = logical[runStart..k];
                var level = levels[start + runStart];
                fragments.Add(new BidiFragment(source, slice, level, MeasureSlice(source, slice)));
                runStart = k;
            }
        }
        // Non-text items ride at their innermost scope's level (base level when none).
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == InlineItemKind.Text) continue;
            fragments.Add(new BidiFragment(items[i], null, ChainLevel(ScopeChain(items[i].Node), baseLtr), items[i].Width));
        }

        // Fragment-granularity L2: reverse maximal runs at each level, highest first. Only the
        // ORDER moves here — each fragment's own chars reverse below (odd level ⇒ reversed),
        // which composes with the passes exactly like the whole-stream reversal.
        var order = Enumerable.Range(0, fragments.Count).ToList();
        var highest = 0;
        foreach (var f in fragments) highest = Math.Max(highest, f.Level);
        for (var level = highest; level >= 1; level--)
        {
            var k = 0;
            while (k < order.Count)
            {
                if (fragments[order[k]].Level >= level)
                {
                    var m = k;
                    while (m < order.Count && fragments[order[m]].Level >= level) m++;
                    order.Reverse(k, m - k);
                    k = m;
                }
                else k++;
            }
        }

        var result = new List<InlineItem>(items.Count);
        foreach (var index in order)
        {
            var f = fragments[index];
            if (f.Text is null || f.Source.Kind != InlineItemKind.Text)
            {
                result.Add(f.Source);
                continue;
            }
            var visual = f.Level % 2 == 1 ? StyleExtensions.ReverseText(f.Text) : f.Text;
            result.Add(f.Source with
            {
                Text = visual,
                Width = f.Level % 2 == 1 ? MeasureSlice(f.Source, visual) : f.Width,
            });
        }
        return result;
    }

    /// <summary>One homogeneous slice of a text item (or a riding non-text item) during the
    /// paragraph-level bidi reordering: <see cref="Level"/> is the slice's embedding level.</summary>
    private sealed record BidiFragment(InlineItem Source, string? Text, int Level, float Width);

    private static float MeasureSlice(InlineItem source, string slice)
    {
        if (source.Node is not { } node) return 0f;
        using var font = TextMeasure.CreateFont(node);
        var (ls, wsp) = TextMeasure.SpacingOf(node);
        return TextMeasure.MeasureSingleLine(slice, font, -1f, ls, wsp).Width;
    }

    /// <summary>
    /// Recursively extracts a flat list of InlineItems from inline nodes.
    /// </summary>
    private static void CollectInlineItems(
        IEnumerable<LayoutNode> nodes,
        List<InlineItem> items,
        float maxWidth,
        float viewportWidth, float viewportHeight, float containingBlockHeight = 0f)
    {
        foreach (var node in nodes)
        {
            var display = node.GetDisplay();
            if (display == DisplayType.None) continue;

            // Absolute/fixed are out of flow: they add nothing to the line, but a zero-sized
            // marker rides along so the line box position they would have occupied is recorded
            // as their static position (§10.3.7 / §10.6.4).
            var nodePos = node.GetPosition();
            if (nodePos == PositionType.Absolute || nodePos == PositionType.Fixed)
            {
                items.Add(new InlineItem(InlineItemKind.OutOfFlow, node, null, 0, 0,
                           default, default, default, 0, 0, 0));
                continue;
            }

            // §9.5: a floated element met INSIDE an inline subtree (a span wrapping a float) is
            // out of the line's flow just like a direct-child float — it rides as a marker item
            // and the line loop registers it as an active float at the current line box's top.
            // Without this the run collection fell through every branch (an empty floated span
            // has neither text nor children) and the float never laid out or painted
            // (float-nowrap-3's nested .float span vanished).
            if (node.GetFloat() != FloatType.None)
            {
                items.Add(new InlineItem(InlineItemKind.Float, node, null, 0, 0,
                           default, default, default, 0, 0, 0));
                continue;
            }

            // <br> → forced line break item
            if (node.TagName == "BR")
            {
                using var brFont = TextMeasure.CreateFont(node);
                // 'normal' line-height is the font's natural box, consistent with GetLineHeight.
                var brH = -brFont.Metrics.Ascent + brFont.Metrics.Descent;
                items.Add(new InlineItem(InlineItemKind.LineBreak, node, null, 0, brH,
                           default, default, default, 0, brH, TextMeasure.ComputeAscent(brFont, brH)));
                continue;
            }

            if (display == DisplayType.InlineTable)
            {
                // §17.2 / §9.2.2: an inline-table participates in the line box like inline-block,
                // laying out internally as a table shrink-wrapped to its content (clamped to the
                // available line width).
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();

                var avail = maxWidth > 0 ? maxWidth : viewportWidth;
                var availContent = Math.Max(0f, avail - margin.Left - margin.Right
                    - border.Left - border.Right - padding.Left - padding.Right);
                var w = Math.Max(0f, TableEngine.MeasureTableWidth(node, availContent, viewportWidth, viewportHeight));

                var contentX2 = margin.Left + border.Left + padding.Left;
                var contentY2 = margin.Top + border.Top + padding.Top;
                var h = Math.Max(0f, TableEngine.LayoutTable(node, contentX2, contentY2, w, viewportWidth, viewportHeight));
                // An explicit height on the table acts as a minimum for the table box (§17.5.3).
                if (!node.IsAutoHeight())
                    h = Math.Max(h, node.GetHeight(viewportHeight, 0, viewportHeight));

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                // §17.5.3: the baseline of an inline-table is the baseline of its first row. The
                // preliminary LayoutTable call above already positioned rows/cells in this item's
                // own (0,0)-anchored frame, so the offset it finds is directly usable as Ascent.
                // No baseline content (e.g. an empty table) falls back to the bottom margin edge.
                var tableAscent = TableEngine.GetFirstRowBaseline(node) ?? totalH;

                items.Add(new InlineItem(InlineItemKind.InlineTable, node, null, totalW, totalH,
                           margin, padding, border, w, h, tableAscent));
                continue;
            }

            if (display == DisplayType.InlineFlex)
            {
                // §5: inline-flex acts as inline-block in the parent, uses flex layout internally.
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();
                var explicitW = node.IsAutoWidth() ? 0f : node.GetWidth(maxWidth);
                var explicitH = node.GetHeight(viewportHeight);

                // Form controls are replaced elements even when an author styles them
                // as inline-flex. They have no flex children, so measuring only their
                // children collapses their label to zero width.
                var intrinsicW = FormLayout.IntrinsicWidth(node);
                var w = explicitW > 0
                    ? explicitW
                    : intrinsicW ?? FlexEngine.MeasureMaxContentMain(node, 0, 0, viewportWidth, viewportHeight);
                var horizontalEdges = padding.Left + padding.Right + border.Left + border.Right;
                var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
                if (isBorderBox && explicitW > 0) w = Math.Max(0, w - horizontalEdges);
                var minW = node.GetMinWidth(maxWidth, fontSize);
                var maxW = node.GetMaxWidth(maxWidth, fontSize);
                if (isBorderBox)
                {
                    minW = Math.Max(0, minW - horizontalEdges);
                    if (!float.IsPositiveInfinity(maxW)) maxW = Math.Max(0, maxW - horizontalEdges);
                }
                w = Math.Max(minW, Math.Min(maxW, w));

                // Intrinsic height: lay out children to compute
                var contentX2 = margin.Left + border.Left + padding.Left;
                var contentY2 = margin.Top + border.Top + padding.Top;
                var h = explicitH > 0
                    ? explicitH
                    : FlexEngine.LayoutFlex(node, contentX2, contentY2, w, 0, viewportWidth, viewportHeight);
                h = Math.Max(h, 0);

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                items.Add(new InlineItem(InlineItemKind.InlineFlex, node, null, totalW, totalH,
                           margin, padding, border, w, h, totalH));
                continue;
            }

            // §10.3.10 / §10.6.2: an inline-block REPLACED element is sized exactly like an
            // inline replaced one — 'display: inline-block' on an <img> sent it down the
            // non-replaced path, which knows no intrinsic height (a 15x15 image laid out 0px
            // tall, inline-block-replaced-height-001).
            if (display == DisplayType.InlineBlock && !IsReplacedImage(node))
            {
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();
                var explicitW = node.IsAutoWidth() ? 0f : node.GetWidth(maxWidth);
                // 'height: 0' is a used height like any other — testing explicitH > 0 handed a
                // zero-height inline-block the 20px form-control fallback and lowered every box
                // baseline-aligned against it by 20px (border-top-width-006: 'height: 0' with a
                // 96px top border). Auto is the only "no explicit height".
                var hasExplicitH = !node.IsAutoHeight();
                // §10.5: a percentage height resolves against the containing block's height when
                // that is known (a 100% child of a max-height-clamped inline-block is the clamped
                // height, max-height-applies-to-017); otherwise the viewport stands in, as before.
                var explicitH = hasExplicitH
                    ? node.GetHeight(containingBlockHeight > 0f ? containingBlockHeight : viewportHeight) : 0f;

                node.Attributes.TryGetValue("type", out var iType);
                var inputType = iType?.ToLowerInvariant() ?? "text";
                var isCheckbox = node.TagName == "INPUT" && inputType == "checkbox";
                var isRadio = node.TagName == "INPUT" && inputType == "radio";
                var isRange = node.TagName == "INPUT" && inputType == "range";
                float defaultW, defaultH;
                // Form controls are sized (and baseline-aligned) as replaced boxes; everything else
                // is a real inline-block whose baseline comes from its own content.
                bool isFormControl = true;
                if (isCheckbox) { defaultW = FormLayout.CheckboxSize; defaultH = FormLayout.CheckboxSize; }
                else if (isRadio) { defaultW = FormLayout.RadioSize; defaultH = FormLayout.RadioSize; }
                else if (isRange) { defaultW = FormLayout.RangeWidth; defaultH = FormLayout.RangeHeight; }
                else if (node.TagName == "BUTTON") { defaultW = 0f; defaultH = FormLayout.TextInputHeight; }
                else if (node.TagName == "INPUT")
                {
                    // Every remaining input type (text, password, number, search, file, date,
                    // submit/reset/button/image, …) is a replaced control: IntrinsicWidth knows
                    // each type's width, and the shared control height keeps an auto-height
                    // input a visible, hittable box instead of a zero-tall sliver.
                    defaultW = FormLayout.IntrinsicWidth(node) ?? FormLayout.TextInputWidth;
                    defaultH = FormLayout.TextInputHeight;
                }
                else if (node.TagName == "TEXTAREA") { defaultW = FormLayout.TextareaWidth; defaultH = FormLayout.TextareaHeight; }
                else if (node.TagName == "SELECT") { defaultW = FormLayout.SelectWidth; defaultH = FormLayout.SelectHeight; }
                else if (node.TagName == "PROGRESS") { defaultW = FormLayout.ProgressWidth; defaultH = FormLayout.ProgressHeight; }
                else if (node.TagName == "METER") { defaultW = FormLayout.MeterWidth; defaultH = FormLayout.MeterHeight; }
                else
                {
                    // §10.3.9: shrink-to-fit's available width is the containing block's width
                    // minus the box's own margins, borders and padding. Passing the whole line
                    // width let an inline-block of wrapping text grow until its border box
                    // overflowed the line by those edges (inline-block-002 at a 450px window).
                    var availableW = maxWidth - margin.Left - margin.Right - border.Left - border.Right
                                     - padding.Left - padding.Right;
                    defaultW = IntrinsicSizer.ShrinkToFit(node, Math.Max(availableW, 0f), viewportHeight);
                    // An empty auto-height inline-block is 0 tall (§10.6.6); content measures below.
                    defaultH = 0f;
                    isFormControl = false;
                }

                var w = explicitW > 0 ? explicitW : node.TagName == "INPUT"
                    ? FormLayout.IntrinsicWidth(node) ?? defaultW : defaultW;
                w = Math.Max(node.GetMinWidth(maxWidth, fontSize),
                    Math.Min(w, node.GetMaxWidth(maxWidth, fontSize)));
                var h = hasExplicitH && (explicitH > 0 || !isFormControl) ? explicitH : defaultH;

                if (node.TagName == "BUTTON" && w <= 0)
                {
                    var btnLabel = node.DisplayText;
                    if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
                    if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";
                    using var btnFont = new SKFont { Size = 13 };
                    w = btnFont.MeasureText(btnLabel) + FormLayout.ButtonPaddingX * 2;
                    h = 13f + FormLayout.ButtonPaddingY * 2;
                }

                // A content-bearing inline-block's height is its laid-out content height at its
                // used width (§10.6.6) — the form-control fallback above is for controls. The
                // pre-measure mirrors the flex-item path; the placement case re-lays the subtree
                // at its resolved position, so boxes written here are provisional. The inner
                // layout is what makes an inline-block child's vertical margins count toward the
                // line box: its own line encloses the child's margin box (§10.8.1,
                // margin-collapse-015). LEAVES qualify too: their text lives in DisplayText with
                // no children, and the form-control fallback height (20px) ignored the inherited
                // line-height — a 32px/4 span must be 128px tall, not one default line
                // (absolute-non-replaced-width-017).
                if (!isFormControl && !hasExplicitH &&
                    (node.Children.Count > 0 || !string.IsNullOrEmpty(node.DisplayText)))
                {
                    var marginBoxH = MeasureBlockHeight(node, Math.Max(w, 0f), viewportWidth, viewportHeight);
                    h = Math.Max(0f, marginBoxH - margin.Top - margin.Bottom
                                     - border.Top - border.Bottom - padding.Top - padding.Bottom);
                }

                // §10.7: the used height clamps to 'max-height', then 'min-height' — an
                // inline-block with 'height: 3in; max-height: 1in' is 1in tall
                // (max-height-applies-to-012). A percentage resolves against the containing
                // block's height, which for an inline-level box is not known here: it computes
                // to 'none' (max) or 0 (min), as for an auto-height containing block.
                if (!isFormControl) h = ClampContentHeight(node, h, containingBlockHeight, border, padding, fontSize);

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                // CSS 2.1 §10.8.1: the baseline of an inline-block is the baseline of its LAST
                // in-flow line box — NOT its bottom margin edge. The bottom edge is only correct
                // when the box has no in-flow line boxes or its 'overflow' computes to something
                // other than 'visible' (and for replaced boxes, handled elsewhere). Without this a
                // text-bearing inline-block rides above the surrounding text's baseline by roughly
                // its leading. Only the leaf case (the box's content is its own text, e.g. a
                // generated-content ::after) is resolved here: a box with element children has not
                // had them laid out at this point, so it keeps the bottom-edge fallback.
                var ascent = totalH;
                if (!isFormControl
                    && node.GetOverflow() == OverflowType.Visible)
                {
                    // §10.8.1: a box with in-flow line boxes bases on its LAST line's baseline.
                    // The pre-measure above (MeasureBlockHeight) lays the children out
                    // provisionally at this box's used width, so the baseline is READ from the
                    // laid-out subtree: the first line's baseline minus the first line's top.
                    // The bottom-edge fallback reserved the whole below-baseline part as
                    // ascent, growing every row holding such a box by ~10px (the
                    // text-transform-bicameral family's 42.45px rows against the reference's
                    // 32); a box-font metric guess mis-measures boxes whose first line holds
                    // a taller inline item. Boxes without a readable baseline (no laid-out
                    // children) keep the fallback.
                    if (node.Children.Count == 0
                        && !string.IsNullOrEmpty(node.DisplayText))
                    {
                        using var ibFont = TextMeasure.CreateFont(node);
                        ascent = margin.Top + border.Top + padding.Top
                                 + TextMeasure.ComputeAscent(ibFont, node.GetLineHeight(fontSize));
                    }
                    else if (node.Children.All(c => c.TagName == "#text" || c.GetDisplay() == DisplayType.Inline)
                             && FindFirstBaselineY(node) is { } baselineY)
                    {
                        var firstTop = node.Children
                            .Select(c => c.Box.BorderBox.Top).DefaultIfEmpty(baselineY).Min();
                        ascent = margin.Top + border.Top + padding.Top + (baselineY - firstTop);
                    }
                    // A box holding BLOCK children bases on its LAST line box too: the provisional
                    // measure put this box's margin box at y = 0, so the last line's baseline in that
                    // layout is the ascent. The bottom-edge fallback lowered a 'x' / 'bcd' block pair's
                    // second line below its neighbours' baseline (inline-block-valign-001).
                    else if (node.Children.Count > 0)
                    {
                        // An explicit height skipped the measure above: lay the subtree out
                        // provisionally first, or the boxes read here are stale.
                        if (hasExplicitH) MeasureBlockHeight(node, Math.Max(w, 0f), viewportWidth, viewportHeight);
                        if (FindLastBaselineY(node) is { } lastBaselineY) ascent = lastBaselineY;
                    }
                }

                items.Add(new InlineItem(InlineItemKind.InlineBlock, node, null, totalW, totalH,
                           margin, padding, border, w, h, ascent));
            }
            else if (IsReplacedImage(node))
            {
                // §10.3.2 / §10.6.2: a CSS 'width'/'height' on a replaced element overrides its
                // intrinsic size, and one specified dimension derives the other from the intrinsic
                // ratio. The inline path used to read the intrinsic size alone, so `width: 100%`
                // (or the HTML width="100%" hint) left the image at its natural pixel width.
                var (w, h) = InlineReplacedSize(node, maxWidth, viewportHeight);
                // §8.4: an inline REPLACED box's margins, borders and padding all take part in
                // layout horizontally *and* vertically — unlike a non-replaced inline box, whose
                // vertical edges do not affect line height. Reserving only the content size here
                // meant `img { padding-right: 20px }` advanced the line by nothing at all.
                var imgFontSize = node.GetFontSize();
                var imgMargin = node.GetMargin(maxWidth, viewportHeight, imgFontSize);
                // §10.3.2 / §10.6.2: on an INLINE replaced box every 'auto' margin has a used
                // value of 0. GetMargin's 'auto' means "centre in the containing block", which is
                // the block-level rule — applying it here pushed the image into mid-air.
                if (node.IsAutoMarginTop()) imgMargin.Top = 0f;
                if (node.IsAutoMarginBottom()) imgMargin.Bottom = 0f;
                if (node.IsAutoMarginLeft()) imgMargin.Left = 0f;
                if (node.IsAutoMarginRight()) imgMargin.Right = 0f;
                var imgPadding = node.GetPadding(maxWidth, viewportHeight, imgFontSize);
                var imgBorder = node.GetBorderWidth();
                var imgW = imgMargin.Left + imgBorder.Left + imgPadding.Left + w
                         + imgPadding.Right + imgBorder.Right + imgMargin.Right;
                var imgH = imgMargin.Top + imgBorder.Top + imgPadding.Top + h
                         + imgPadding.Bottom + imgBorder.Bottom + imgMargin.Bottom;
                // Replaced elements have no baseline of their own — CSS 2.1 §10.8's fallback rule
                // aligns their bottom margin edge with the line's baseline (Ascent = full height).
                items.Add(new InlineItem(InlineItemKind.Image, node, null, imgW, imgH,
                           imgMargin, imgPadding, imgBorder, w, h, imgH));
            }
            else if (node.TagName == "WBR")
            {
                // HTML: <wbr> represents a line break opportunity and nothing else.
                items.Add(EdgeItem(node, 0f));
            }
            else if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                var lh = node.GetLineHeight(node.GetFontSize());
                // 'letter-spacing' / 'word-spacing' widen the text the painter draws, so they have
                // to widen the box layout reserves for it too — measuring without them put every
                // following inline box in the wrong place.
                var (ls, wsp) = TextMeasure.SpacingOf(node);

                // §16.6: under pre / pre-wrap / pre-line a newline is a FORCED line break. An
                // inline run measured the whole string as one unbroken item, so a preformatted
                // <span> holding two lines drew them on top of each other.
                var ws = node.GetWhiteSpace();
                // RenderedText already returns the VISUAL string (text-transform plus the
                // §16.2/UAX #9 subset reversal), so measuring it keeps width and fragments
                // in agreement with what painting draws.
                var rendered = node.RenderedText();
                var segments = ws is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine
                               && rendered.Contains('\n')
                    ? rendered.Split('\n')
                    : [rendered];

                var (leading, trailing) = InlineBoxEdges(node, maxWidth, viewportHeight);
                if (leading != 0f) items.Add(EdgeItem(node, leading, trailing: false));
                for (var si = 0; si < segments.Length; si++)
                {
                    if (si > 0)
                        items.Add(new InlineItem(InlineItemKind.LineBreak, node, null, 0, lh,
                                   default, default, default, 0, lh, TextMeasure.ComputeAscent(font, lh)));
                    var (w, h, ascent) = TextMeasure.MeasureSingleLine(segments[si], font, lh, ls, wsp);
                    items.Add(new InlineItem(InlineItemKind.Text, node, segments[si], w, h,
                               default, default, default, w, h, ascent));
                }
                if (trailing != 0f) items.Add(EdgeItem(node, trailing, trailing: true));
            }
            else if (display == DisplayType.Inline && IsEmptyNonReplacedInline(node) &&
                     (node.Children.Count == 0 || node.Children.All(RendersNothingInline)))
            {
                // §10.8/§9.4.2: an EMPTY inline element is still an inline box — its horizontal
                // edges are laid out, its line-height enters the line box (an empty
                // 'line-height: 5' span makes the line 5em tall, empty-inline-003) and its
                // background and borders paint around its zero-width content area
                // (empty-inline-002). It emitted nothing at all.
                var (leading, trailing) = InlineBoxEdges(node, maxWidth, viewportHeight);
                if (leading != 0f) items.Add(EdgeItem(node, leading, trailing: false));
                using var strutFont = TextMeasure.CreateFont(node);
                var strutLh = node.GetLineHeight(node.GetFontSize());
                items.Add(new InlineItem(InlineItemKind.Strut, node, null, 0f, strutLh,
                           default, default, default, 0f, strutLh, TextMeasure.ComputeAscent(strutFont, strutLh)));
                // Its whitespace still collapses with its neighbours like any other (§16.6.1).
                if (node.Children.Count > 0)
                    CollectInlineItems(node.Children, items, maxWidth, viewportWidth, viewportHeight, containingBlockHeight);
                if (trailing != 0f) items.Add(EdgeItem(node, trailing, trailing: true));
            }
            else if (node.Children.Count > 0)
            {
                var (leading, trailing) = InlineBoxEdges(node, maxWidth, viewportHeight);
                // §8.6 (bidi box model): when a 'direction: rtl' inline box is SPLIT across
                // lines, its fragments join on the sides MIRRORED from LTR (rtl-span-only):
                // the box's trailing decorations render at the FIRST line's end and the leading
                // ones at the LAST line's start, with the join sides empty. An UNSPLIT box
                // keeps its physical sides — side margins are unaffected by directionality on
                // one line (the bidi-box-model margins/padding families assert exactly that).
                var isRtlBox = node.GetDirection() == "rtl";
                if (!isRtlBox && leading != 0f) items.Add(EdgeItem(node, leading, trailing: false));
                var subtreeStart = items.Count;
                // §16.2/UAX #9 subset: inside a reversed scope every level's child sequence
                // reverses (the scope's character stream reverses as a whole), while the box's
                // own edge items stay at its physical boundaries.
                var children = node.BidiReversesContent() ? node.Children.AsEnumerable().Reverse() : node.Children;
                CollectInlineItems(children, items, maxWidth, viewportWidth, viewportHeight, containingBlockHeight);
                if (!isRtlBox && trailing != 0f) items.Add(EdgeItem(node, trailing, trailing: true));
                if (isRtlBox && (leading != 0f || trailing != 0f))
                {
                    var subtreeEnd = items.Count;
                    var firstBreak = items.FindIndex(subtreeStart, subtreeEnd - subtreeStart,
                        static i => i.Kind == InlineItemKind.LineBreak);
                    if (firstBreak < 0)
                    {
                        // Unsplit: physical sides.
                        if (leading != 0f) items.Insert(subtreeStart, EdgeItem(node, leading, trailing: false));
                        if (trailing != 0f) items.Add(EdgeItem(node, trailing, trailing: true));
                    }
                    else
                    {
                        var lastBreak = items.FindLastIndex(subtreeEnd - 1, subtreeEnd - subtreeStart,
                            static i => i.Kind == InlineItemKind.LineBreak);
                        // Trailing decorations just before the first line's break...
                        if (trailing != 0f) items.Insert(firstBreak, EdgeItem(node, trailing, trailing: true));
                        // ...and leading decorations just after the last line's break (shifted
                        // one further by the insert above).
                        if (leading != 0f)
                            items.Insert(lastBreak + 1 + (trailing != 0f ? 1 : 0), EdgeItem(node, leading, trailing: false));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds the baseline of the first in-flow line box under <paramref name="node"/>, as an
    /// absolute Y in whatever coordinate space <c>node</c>'s subtree was just laid out in (the
    /// caller subtracts its own reference point). Used for §17.5.3 ("the baseline of an
    /// inline-table is the baseline of the first row"), so this only looks at the FIRST
    /// baseline-contributing descendant in document order — display:none and out-of-flow nodes
    /// are skipped, but visibility:hidden ones still occupy space and so still count (visibility
    /// doesn't affect layout). Returns null if the subtree has no baseline content at all (e.g.
    /// empty cells), so callers can fall back to a bottom-edge alignment.
    /// </summary>
    internal static float? FindFirstBaselineY(LayoutNode node)
    {
        if (node.GetDisplay() == DisplayType.None) return null;
        var pos = node.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return null;

        // A leaf with its own text (a bare-text element, or a synthesized #text node) got its own
        // Box set directly by ApplyInlineItem's Text case — its content-box top is the line top.
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            if (node.DisplayText.Trim().Length == 0) return null; // whitespace-only: no line box
            using var font = TextMeasure.CreateFont(node);
            var lh = node.GetLineHeight(node.GetFontSize());
            return node.Box.ContentBox.Top + TextMeasure.ComputeAscent(font, lh);
        }
        // Replaced elements have no baseline of their own — bottom margin edge (§10.8 fallback).
        if (IsReplacedImage(node))
            return node.Box.ContentBox.Bottom;

        foreach (var child in node.Children)
        {
            if (child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText)) continue;
            var y = FindFirstBaselineY(child);
            if (y.HasValue) return y;
        }
        return null;
    }

    /// <summary>The baseline of the LAST in-flow line box under <paramref name="node"/> (§10.8.1
    /// for inline-blocks), in the coordinate space its subtree was just laid out in; null when it
    /// has none. Floats and out-of-flow boxes do not count; visibility:hidden ones do.</summary>
    internal static float? FindLastBaselineY(LayoutNode node)
    {
        if (node.GetDisplay() == DisplayType.None) return null;
        var pos = node.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return null;
        if (node.TagName != "#text" && node.GetFloat() != FloatType.None) return null;

        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            if (node.DisplayText.Trim().Length == 0) return null;
            using var font = TextMeasure.CreateFont(node);
            var lh = node.GetLineHeight(node.GetFontSize());
            var ascent = TextMeasure.ComputeAscent(font, lh);
            if (node.InlineFragments is { Count: > 0 } fragments) return fragments[^1].Rect.Top + ascent;
            // A block's own wrapped text stacks its lines from the top: the last one ends the box.
            return node.GetDisplay() is DisplayType.Inline
                ? node.Box.ContentBox.Top + ascent
                : node.Box.ContentBox.Bottom - lh + ascent;
        }
        if (IsReplacedImage(node)) return node.Box.ContentBox.Bottom;

        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText)) continue;
            // A box whose 'overflow' is not 'visible' keeps its line boxes to itself and bases at
            // its bottom margin edge (§10.8.1) — inline-block or block alike, empty or not, as
            // browsers synthesize a scroll container's baseline (baseline-block-with-overflow-001
            // matches each block against an inline-block of the same shape).
            if (child.TagName != "#text" && child.GetOverflow() != OverflowType.Visible &&
                child.GetPosition() is not (PositionType.Absolute or PositionType.Fixed) &&
                child.GetDisplay() != DisplayType.None && child.GetFloat() == FloatType.None)
                return child.Box.MarginBox.Bottom;
            if (FindLastBaselineY(child) is { } y) return y;
        }
        return null;
    }

    private static void ApplyInlineItem(InlineItem item, float absX, float absY,
        float viewportWidth, float viewportHeight)
    {
        var node = item.Node;
        switch (item.Kind)
        {
            case InlineItemKind.InlineTable:
                {
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    // Re-run table layout at the resolved position so cells get their final boxes.
                    TableEngine.LayoutTable(node, contentX, contentY, item.ContentW, viewportWidth, viewportHeight);
                    break;
                }
            case InlineItemKind.InlineBlock:
                {
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    if (node.Children.Count > 0)
                        LayoutChildrenImpl(node.Children, contentX, contentY, item.ContentW,
                            viewportWidth, viewportHeight, item.ContentH, 0f, true,
                            new List<ActiveFloat>(), true, out _, out _, out _);
                    break;
                }
            case InlineItemKind.InlineFlex:
                {
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    // Re-invoke flex layout at the resolved position so children get correct boxes
                    FlexEngine.LayoutFlex(node, contentX, contentY, item.ContentW, item.ContentH, 0, 0);
                    break;
                }
            case InlineItemKind.OutOfFlow:
                {
                    // Records only — the box itself is laid out later by the positioned pass.
                    node.StaticX = absX;
                    node.StaticY = absY;
                    break;
                }
            case InlineItemKind.Strut:
                {
                    // An empty inline box: a zero-width content area of its own font about the
                    // baseline (§10.6.1), with its padding and border around it — what paints its
                    // background and borders.
                    // Geometry only: its background, padding and borders paint through the
                    // per-line wrapper fragments the strut folds (AccumulateWrapperFragment).
                    var (strutAscent, strutDescent) = ContentAreaMetrics(node);
                    var strutBaseline = absY + item.Ascent;
                    if (node.Children.Count == 0)
                        node.Box = new BoxDimensions
                        {
                            ContentBox = new SKRect(absX, strutBaseline - strutAscent, absX, strutBaseline + strutDescent),
                        };
                    break;
                }
            case InlineItemKind.InlineEdge:
                // Pure advance. The inline box's own painted geometry comes from the fragments its
                // text children record, so writing a box here would overwrite it with a sliver.
                break;
            case InlineItemKind.Image:
                {
                    // The item spans the margin box; the node's own box starts inside the
                    // margin/border/padding edges so the bitmap lands on the content box and
                    // the decorations get a padding/border box to paint against.
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    break;
                }
            case InlineItemKind.Text:
            case InlineItemKind.LineBreak:
                {
                    // A re-measured item that wrapped paints one fragment per line, each
                    // anchored to its own band x (an indented span's background covers line 1
                    // from the margin edge, the rest from the band start — §8.3 first-fragment
                    // margins). The LineBreak case below stays as-is.
                    if (item.Kind == InlineItemKind.Text && item.WrappedLines is { Count: > 1 } wrapped)
                    {
                        var union = SKRect.Empty;
                        (node.InlineFragments ??= []).Clear();
                        var y = absY;
                        foreach (var (text, fx, fw, fh) in wrapped)
                        {
                            var fr = new SKRect(fx, y, fx + fw, y + fh);
                            (node.InlineFragments).Add((fr, text));
                            union = union.IsEmpty ? fr : SKRect.Union(union, fr);
                            y += fh;
                        }
                        node.Box = new BoxDimensions
                        {
                            ContentBox = union,
                            Padding = node.TagName.StartsWith('#') ? default : node.GetPadding(0, 0, node.GetFontSize()),
                            Border = node.TagName.StartsWith('#') ? default : node.GetBorderWidth(),
                        };
                        break;
                    }
                    var rect = new SKRect(absX, absY, absX + item.ContentW, absY + item.ContentH);
                    // Record every fragment, and let Box span them all: an inline box broken over
                    // two lines has one rect per line, and painting only the last one lost the
                    // background and borders of every line above it.
                    if (item.Kind == InlineItemKind.Text)
                    {
                        (node.InlineFragments ??= []).Add((rect, item.Text ?? ""));
                        if (node.InlineFragments.Count > 1) rect = SKRect.Union(node.Box.ContentBox, rect);
                    }
                    // The line already reserved this box's horizontal padding and border (§8.4),
                    // so record them here too — the painter fills the PADDING box and draws the
                    // border around the border box, and without the edges both would stop short
                    // of the space the line gave the box. Anonymous boxes (#text) resolve their
                    // styles from the element that produced them, so taking edges there would
                    // apply the parent's a second time.
                    var noEdges = node.TagName.StartsWith('#') || node.IsBrokenAroundBlock();
                    node.Box = new BoxDimensions
                    {
                        ContentBox = rect,
                        Padding = noEdges ? default : node.GetPadding(0, 0, node.GetFontSize()),
                        Border = noEdges ? default : node.GetBorderWidth(),
                    };
                    break;
                }
        }
    }

    // -------------------------------------------------------------------------
    // Inline item model
    // -------------------------------------------------------------------------

    /// <summary><see cref="OutOfFlow"/> is a zero-sized marker for an absolutely positioned box
    /// met inside an inline run: it occupies no space on the line but rides along with it, so the
    /// line position it lands on is the box's static position (§10.3.7 / §10.6.4).</summary>
    /// <summary><see cref="InlineEdge"/> is the leading or trailing margin+border+padding of a
    /// non-replaced inline box: it advances the line but has no height of its own (§8.4).</summary>
    private enum InlineItemKind { Text, Image, InlineBlock, InlineFlex, InlineTable, LineBreak, OutOfFlow, InlineEdge, Float, Strut }

    /// <summary><see cref="Ascent"/> is the distance from this item's own top (margin edge) down
    /// to the baseline it should align on (CSS 2.1 §10.8). Text uses the font's half-leading
    /// ascent; a replaced box (image) or one with no baseline of its own (inline-block,
    /// inline-flex) uses its full height, so 'baseline' alignment reduces to aligning its bottom
    /// margin edge with the line's baseline — the spec's fallback rule. inline-table uses the
    /// baseline of its first row (§17.5.3).</summary>
    private record InlineItem(
        InlineItemKind Kind,
        LayoutNode Node,
        string? Text,
        float Width,
        float Height,
        EdgeSizes Margin,
        EdgeSizes Padding,
        EdgeSizes Border,
        float ContentW,
        float ContentH,
        float Ascent,
        bool TrailingEdge = false,
        IReadOnlyList<(string Text, float X, float Width, float Height)>? WrappedLines = null
    );
}
