using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal static partial class BoxEngine
{
    // -------------------------------------------------------------------------
    // Float tracking
    // -------------------------------------------------------------------------

    /// <summary>Represents an active float whose occupied area affects subsequent layout.</summary>
    private record struct ActiveFloat(float Left, float Top, float Right, float Bottom, FloatType Side, float CollapseShift, LayoutNode Node)
    {
        public ActiveFloat(float l, float t, float r, float b, FloatType s, float cs)
            : this(l, t, r, b, s, cs, null) { }
    }

    /// <summary>
    /// Returns the Y at which an element with the given <paramref name="clear"/> value
    /// should start, ensuring it is below any relevant active floats.
    /// </summary>
    private static float ApplyClear(ClearType clear, List<ActiveFloat> floats, float cursorY)
    {
        if (clear == ClearType.None || floats.Count == 0) return cursorY;
        var y = cursorY;
        foreach (var f in floats)
        {
            if (clear == ClearType.Both ||
                (clear == ClearType.Left && f.Side == FloatType.Left) ||
                (clear == ClearType.Right && f.Side == FloatType.Right))
            {
                y = Math.Max(y, f.Bottom);
            }
        }
        return y;
    }

    /// <summary>
    /// Computes the available horizontal band at a given Y position,
    /// narrowed by any active floats that overlap vertically.
    /// Returns (effectiveX, effectiveWidth).
    /// </summary>
    private static (float x, float w) AvailableBand(
        List<ActiveFloat> floats, float y, float height,
        float contentX, float contentW)
    {
        var left = contentX;
        var right = contentX + contentW;
        foreach (var f in floats)
        {
            // Float overlaps vertically with the band [y, y+height)?
            if (f.Bottom <= y || f.Top >= y + height) continue;
            if (f.Side == FloatType.Left)
                left = Math.Max(left, f.Right);
            else
                right = Math.Min(right, f.Left);
        }
        return (left, Math.Max(0, right - left));
    }

    /// <summary>The nearest float bottom strictly below <paramref name="y"/>, or null when no
    /// float reaches past it — i.e. the next Y at which the available band can only get wider.</summary>
    private static float? NextFloatBottom(List<ActiveFloat> floats, float y)
    {
        float? best = null;
        foreach (var f in floats)
            if (f.Bottom > y + 0.5f && (best is null || f.Bottom < best))
                best = f.Bottom;
        return best;
    }

    /// <summary>
    /// The highest position at or below <paramref name="y"/> where a box of the given border-box
    /// <paramref name="width"/> and <paramref name="height"/> fits beside the floats, with the band
    /// (absolute left edge and width) it gets there. CSS 2.1 §9.5: a line box or the border box of
    /// a box that establishes a block formatting context must not overlap the margin box of any
    /// float in the same BFC — so the band is measured over the box's WHOLE height, not a 1px
    /// probe at its top: a float whose top starts partway down the box still narrows it
    /// (floats-wrap-top-below-bfc-002l: a 50px BFC at y=50 must not overlap a float starting at
    /// y=75). When the band is too narrow the box moves down to the next float bottom, one float
    /// at a time (the band can only widen there), until it fits or no float reaches lower —
    /// floats-wrap-top-below-003l's box fits beside the lower float at y=75 rather than going
    /// below every float.
    /// </summary>
    private static (float Y, float X, float W) FitBandBelow(
        List<ActiveFloat> floats, float y, float height, float width,
        float contentX, float contentW)
    {
        var h = Math.Max(height, 1f);
        var (bx, bw) = AvailableBand(floats, y, h, contentX, contentW);
        for (var attempt = 0; attempt < 512 && width > bw + 0.5f; attempt++)
        {
            // No float beside the box: it is wider than the containing block on its own and
            // overflows it where it stands — moving down cannot make it fit.
            if (bx <= contentX + 0.5f && bw >= contentW - 0.5f) break;
            if (NextFloatBottom(floats, y) is not { } next) break;
            y = next;
            (bx, bw) = AvailableBand(floats, y, h, contentX, contentW);
        }
        return (y, bx, bw);
    }

    /// <summary>Width of the narrowest thing that must fit on the first line before it may be
    /// shortened any further: the first atomic inline box, or the first word of the first text
    /// item. Whitespace-only items are skipped — they are dropped at the start of a line. A
    /// forced break (&lt;br&gt;) ends the first line before any following content, and a float is
    /// out of flow (float-no-content-beside-001).</summary>
    private static float FirstUnbreakableWidth(List<InlineItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case InlineItemKind.OutOfFlow or InlineItemKind.InlineEdge:
                    continue;
                case InlineItemKind.LineBreak or InlineItemKind.Float:
                    return 0f;
                case InlineItemKind.Text when item.Text is { } t:
                    var trimmed = t.TrimStartCss();
                    if (trimmed.Length == 0) continue;
                    if (item.Node.GetWhiteSpace() is WhiteSpace.NoWrap or WhiteSpace.Pre) return item.Width;
                    var end = trimmed.IndexOf(' ');
                    var word = end < 0 ? trimmed : trimmed[..end];
                    using (var font = TextMeasure.CreateFont(item.Node))
                        return font.MeasureText(word);
                default:
                    return item.Width;
            }
        }
        return 0f;
    }

    /// <summary><see cref="FirstUnbreakableWidth"/> for a single item: the first word of a text
    /// item (whitespace-only items measure 0), or the whole width of an atomic one. This is the
    /// piece a line box must be able to hold before it may stop shifting downward past float
    /// bottoms (§9.5 rule 7).</summary>
    private static float FirstPieceWidth(InlineItem item)
    {
        if (item.Kind != InlineItemKind.Text) return item.Width;
        if (item.Text is not { } t) return 0f;
        var trimmed = t.TrimStartCss();
        if (trimmed.Length == 0) return 0f;
        if (item.Node.GetWhiteSpace() is WhiteSpace.NoWrap or WhiteSpace.Pre) return item.Width;
        var end = trimmed.IndexOf(' ');
        var word = end < 0 ? trimmed : trimmed[..end];
        using var font = TextMeasure.CreateFont(item.Node);
        return font.MeasureText(word);
    }

    /// <summary>
    /// The band (absolute left edge and width) of each successive line box of a wrapped text run
    /// starting at <paramref name="startY"/>, ending once no float reaches any lower — from there
    /// on every line has the containing block's full width, which the last entry carries.
    /// Returns null when no float shortens any of them.
    /// </summary>
    private static List<(float X, float Width)>? LineBandsFor(
        List<ActiveFloat> floats, float startX, float startY, float lineHeight,
        float contentX, float contentW, float firstLineWidth)
    {
        if (floats.Count == 0 || lineHeight <= 0f) return null;
        var lowest = floats.Max(f => f.Bottom);
        if (lowest <= startY + 0.5f) return null;

        var bands = new List<(float X, float Width)> { (startX, firstLineWidth) };
        var y = startY + lineHeight;
        // Bounded by the lowest float, so this cannot run away on a tall block.
        while (y < lowest && bands.Count < 512)
        {
            var (bx, bw) = AvailableBand(floats, y, lineHeight, contentX, contentW);
            bands.Add((bx, bw));
            y += lineHeight;
        }
        bands.Add((contentX, contentW));   // below every float: the full band
        return bands;
    }

    /// <summary>Remove floats whose bottom edge is at or above <paramref name="y"/>.</summary>
    private static void RetireFloats(List<ActiveFloat> floats, float y)
    {
        floats.RemoveAll(f => f.Bottom <= y);
    }

    /// <summary>
    /// CSS 2.1 §9.5.1 rules 1, 2, 3, 7, 9: tries to place a float of outer width
    /// <paramref name="outerW"/> on <paramref name="side"/> at vertical position <paramref name="y"/>
    /// within the containing block [<paramref name="contentX"/>, <paramref name="contentX"/> + <paramref name="contentW"/>].
    /// Unlike line boxes (which are clamped to the containing block's horizontal edges), a float
    /// with no same-side float to its outer side may extend past the opposite edge of its
    /// containing block (rule 7), provided it does not overlap an opposite-side float in the same
    /// BFC even when that float lies outside the containing block (rule 3,
    /// floats-rule3-outside-left-001 / floats-rule3-outside-right-001).
    /// </summary>
    private static bool TryPlaceFloatAt(
        List<ActiveFloat> floats, FloatType side, float y, float outerW,
        float contentX, float contentW, out float outerLeft)
    {
        var cbLeft = contentX;
        var cbRight = contentX + contentW;
        var leftEdge = cbLeft;
        var rightEdge = cbRight;
        var minRightFloatLeft = float.PositiveInfinity;
        var maxLeftFloatRight = float.NegativeInfinity;
        var hasLeftFloat = false;
        var hasRightFloat = false;

        foreach (var f in floats)
        {
            if (f.Bottom <= y || f.Top >= y + 1f) continue;
            if (f.Side == FloatType.Left)
            {
                hasLeftFloat = true;
                leftEdge = Math.Max(leftEdge, f.Right);
                maxLeftFloatRight = Math.Max(maxLeftFloatRight, f.Right);
            }
            else
            {
                hasRightFloat = true;
                rightEdge = Math.Min(rightEdge, f.Left);
                minRightFloatLeft = Math.Min(minRightFloatLeft, f.Left);
            }
        }

        if (side == FloatType.Left)
        {
            outerLeft = leftEdge;
            var outerRight = outerLeft + outerW;
            if (outerRight > minRightFloatLeft + 0.5f) return false;
            if (hasLeftFloat && outerRight > cbRight + 0.5f) return false;
            return true;
        }
        else
        {
            var outerRight = rightEdge;
            outerLeft = outerRight - outerW;
            if (outerLeft < maxLeftFloatRight - 0.5f) return false;
            if (hasRightFloat && outerLeft < cbLeft - 0.5f) return false;
            return true;
        }
    }

    private static ActiveFloat LayoutFloat(
        LayoutNode child, FloatType side,
        List<ActiveFloat> floats,
        float contentX, float cursorY, float contentW,
        float viewportWidth, float viewportHeight, float parentContentHeight,
        float openLineY = float.NaN, float openLineUsedW = 0f, float chainOffset = 0f)
    {
        var m = MeasureFloat(child, contentW, viewportHeight, parentContentHeight);
        var outerW = m.OuterWidth;

        // Find placement Y — must not overlap existing floats on the same side
        var placeY = cursorY;

        // CSS 2.1 §9.5.1 (rules 5-6): the top of a float is aligned with the top of the CURRENT
        // line box — a float written after inline content sits beside that content, and only drops
        // below the line when it no longer fits there (rule 7). Without this, `text <img
        // style="float:right">` pushed the image onto its own line under the text.
        if (!float.IsNaN(openLineY) && openLineY < placeY)
        {
            var (_, lbw) = AvailableBand(floats, openLineY, 1, contentX, contentW);
            if (openLineUsedW > 0f
                ? outerW <= lbw - openLineUsedW + 0.5f
                : TryPlaceFloatAt(floats, side, openLineY, outerW, contentX, contentW, out _))
                placeY = openLineY;
        }
        // §9.5.1 rule 5: the outer top of a floating box may not be higher than the outer top of
        // any block or floated box generated by an element earlier in the source document. The
        // cursor already sits at or below earlier blocks' tops; the active floats are the remaining
        // constraint — applied AFTER the open-line join so the join cannot drop the float back
        // above them (floats-146: the right float C may not rise beside A, above the dropped
        // float B).
        foreach (var f in floats) placeY = Math.Max(placeY, f.Top);
        // §9.5.2: 'clear' on a float requires its top outer edge to be below the bottom outer edge
        // of any earlier left-/right-floating boxes (constraint #10, applied to the float's own
        // placement without advancing the normal-flow cursor or retiring earlier floats).
        placeY = ApplyClear(child.GetClear(), floats, placeY);
        // Determine X based on side, respecting existing floats (§9.5.1 rules 1, 2, 3, 7, 9).
        float outerLeft;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (TryPlaceFloatAt(floats, side, placeY, outerW, contentX, contentW, out outerLeft))
                goto placed;
            // Slide down past the nearest float bottom
            var nearest = float.MaxValue;
            foreach (var f in floats)
                if (f.Bottom > placeY) nearest = Math.Min(nearest, f.Bottom);
            if (nearest == float.MaxValue) break;
            placeY = nearest;
        }
        // Fallback: place at current position
        outerLeft = side == FloatType.Left ? contentX : contentX + contentW - outerW;

    placed:
        // The float's placement already sits at the running collapsed-margin chain's bottom
        // (placeY = runY + pendingMargin): record that offset so a self-collapsing ancestor's
        // re-anchoring shift only moves the float by the chain's remaining growth.
        return LayoutFloatBox(child, side, m, outerLeft, placeY, viewportWidth, viewportHeight,
                              parentContentHeight, chainOffset);
    }

    /// <summary>A float's used horizontal metrics (§10.3.5 / §10.3.2): its margins (auto used
    /// as 0), padding, border, used content width and the margin-box width placement works
    /// with, plus the replaced intrinsic size when it is a replaced element.</summary>
    private readonly record struct FloatMetrics(EdgeSizes Margin, EdgeSizes Padding, EdgeSizes Border,
        float ContentWidth, float OuterWidth, (float Width, float Height)? Replaced);

    /// <summary>Resolves a float's used width — shared by the direct-child path
    /// (<see cref="LayoutFloat"/>) and the in-run path (<see cref="PlaceInRunFloat"/>), so a
    /// float sizes the same however it was met. §10.3.5 (non-replaced: explicit width or
    /// shrink-to-fit) / §10.3.2 (replaced: the intrinsic size), then §10.4's max-width and
    /// min-width clamps.</summary>
    private static FloatMetrics MeasureFloat(LayoutNode child, float contentW, float viewportHeight,
        float parentContentHeight)
    {
        var fontSize = child.GetFontSize();
        var margin = child.GetMargin(contentW, viewportHeight, fontSize);
        // §10.3.5 (non-replaced) / §10.3.2 (replaced): a floated box's computed 'auto' left/right
        // margins are USED as 0 — the generic resolver's (total-size)/2 split is the in-flow
        // block's §10.3.3 centering rule, which does not apply here (float-non-replaced-width-
        // 001/003/005, float-replaced-width-001/003/005/006/011).
        if (child.IsAutoMarginLeft()) margin.Left = 0f;
        if (child.IsAutoMarginRight()) margin.Right = 0f;
        var padding = child.GetPadding(contentW, viewportHeight, fontSize);
        var border = child.GetBorderWidth();
        // Generated ::before/::after boxes share their originating element's Style object, so
        // non-inherited box edges (margin, padding, border) and width/height must only apply when
        // declared on the pseudo-element itself (floats-114's floated ::before/::after inside a
        // bordered, padded <p>).
        var isPseudo = child.TagName.StartsWith("#pseudo", StringComparison.Ordinal);
        if (isPseudo)
        {
            var ov = child.StyleOverrides;
            bool HasOv(string prop, string shorthand) => ov.ContainsKey(prop) || ov.ContainsKey(shorthand);
            if (!HasOv("margin-top", "margin")) margin.Top = 0f;
            if (!HasOv("margin-right", "margin")) margin.Right = 0f;
            if (!HasOv("margin-bottom", "margin")) margin.Bottom = 0f;
            if (!HasOv("margin-left", "margin")) margin.Left = 0f;
            if (!HasOv("padding-top", "padding")) padding.Top = 0f;
            if (!HasOv("padding-right", "padding")) padding.Right = 0f;
            if (!HasOv("padding-bottom", "padding")) padding.Bottom = 0f;
            if (!HasOv("padding-left", "padding")) padding.Left = 0f;
            bool HasBorderOv(string s) => ov.ContainsKey($"border-{s}-width") || ov.ContainsKey($"border-{s}")
                || ov.ContainsKey("border-width") || ov.ContainsKey("border");
            if (!HasBorderOv("top")) border.Top = 0f;
            if (!HasBorderOv("right")) border.Right = 0f;
            if (!HasBorderOv("bottom")) border.Bottom = 0f;
            if (!HasBorderOv("left")) border.Left = 0f;
        }

        // 'width: 0' is a used width of zero — only an AUTO width shrink-to-fits (floats-153's
        // zero-width float with a negative margin must not grow to its text).
        var hasExplicitW = !child.IsAutoWidth() && (!isPseudo || child.StyleOverrides.ContainsKey("width"));
        var explicitW = hasExplicitW ? child.GetWidth(contentW) : 0f;
        // A floated replaced box (§9.7 makes it block-level) is sized from the image, not
        // shrink-to-fit over children it does not have.
        var replaced = TryResolveReplacedSize(child, contentW, parentContentHeight, viewportHeight);
        if (replaced.HasValue) { explicitW = replaced.Value.Width; hasExplicitW = true; }
        var maxAvail = contentW - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right;
        float childContentW;
        if (hasExplicitW)
        {
            var isBB = child.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            childContentW = isBB
                ? Math.Max(0, explicitW - border.Left - border.Right - padding.Left - padding.Right)
                : explicitW;
        }
        else
        {
            // Shrink-to-fit (§10.3.5): min(max(min-content, available), max-content).
            childContentW = IntrinsicSizer.ShrinkToFit(child, Math.Max(0f, maxAvail), viewportHeight);
        }
        childContentW = Math.Max(0, childContentW);

        // §10.4: the tentative used width — shrink-to-fit or explicit — is clamped to max-width,
        // then min-width (min wins). The block and abs-pos paths applied this; the float path
        // didn't, so a float's max-width-constrained width never reached its parent's
        // shrink-to-fit (absolute-non-replaced-width-019..024) nor its own box.
        var isBorderBoxW = child.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var maxW = (!isPseudo || child.StyleOverrides.ContainsKey("max-width"))
            ? child.GetMaxWidth(contentW, fontSize) : float.PositiveInfinity;
        if (maxW < float.PositiveInfinity && !(IsPercentValue(child, "max-width") && contentW <= 0f))
        {
            var maxContent = isBorderBoxW
                ? Math.Max(0f, maxW - border.Left - border.Right - padding.Left - padding.Right) : maxW;
            childContentW = Math.Min(childContentW, maxContent);
        }
        var minW = (!isPseudo || child.StyleOverrides.ContainsKey("min-width"))
            ? child.GetMinWidth(contentW, fontSize) : 0f;
        if (minW > 0f && !(IsPercentValue(child, "min-width") && contentW <= 0f))
        {
            var minContent = isBorderBoxW
                ? Math.Max(0f, minW - border.Left - border.Right - padding.Left - padding.Right) : minW;
            childContentW = Math.Max(childContentW, minContent);
        }
        // §17.5.2.2: a table's width cannot be smaller than the minimum width required by its
        // columns/cells, even when a smaller explicit 'width' is specified on the floated table
        // (floated-table-wider-than-specified).
        if (child.GetDisplay() == DisplayType.Table)
        {
            var tableMinW = TableEngine.MeasureTableContentWidths(child, Math.Max(0f, maxAvail), viewportHeight).SumMin;
            childContentW = Math.Max(childContentW, tableMinW);
        }

        var outerW = margin.Left + border.Left + padding.Left + childContentW + padding.Right + border.Right + margin.Right;
        return new FloatMetrics(margin, padding, border, childContentW, outerW, replaced);
    }

    /// <summary>Lays a float out with its margin box's top-left corner at
    /// (<paramref name="outerLeft"/>, <paramref name="outerTop"/>) — shared by both float paths,
    /// so a float met inside an inline run lays its content out exactly like a direct-child
    /// float: a flex or table float through its own engine, a block float's children always
    /// (an explicit height fixes the box's height, not whether its content exists; §10.6.3 /
    /// §10.6.7 lay the content out either way — stack-floats-001's lime block inside a
    /// 'height: 3em' float), and §10.7's min/max-height clamp.</summary>
    private static ActiveFloat LayoutFloatBox(LayoutNode child, FloatType side, FloatMetrics m,
        float outerLeft, float outerTop, float viewportWidth, float viewportHeight,
        float parentContentHeight, float chainOffset)
    {
        var (margin, padding, border, childContentW, _, replaced) = m;
        var fontSize = child.GetFontSize();
        var placeContentX = outerLeft + margin.Left + border.Left + padding.Left;
        var placeContentY = outerTop + margin.Top + border.Top + padding.Top;

        // Lay out children inside the float
        var isPseudo = child.TagName.StartsWith("#pseudo", StringComparison.Ordinal);
        var isBorderBox = child.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var explicitH = (!isPseudo || child.StyleOverrides.ContainsKey("height"))
            ? child.GetHeight(parentContentHeight, 0, viewportHeight) : 0f;
        var knownH = explicitH > 0
            ? (isBorderBox ? Math.Max(0, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom) : explicitH)
            : 0f;
        // §10.7: the children see the CLAMPED height (a 100% child of a 'height: 200px;
        // max-height: 100px' float is 100px tall).
        if (knownH > 0f) knownH = ClampContentHeight(child, knownH, parentContentHeight, border, padding, fontSize);

        var nodeDisplay = child.GetDisplay();
        var childContentH = (nodeDisplay == DisplayType.Flex || nodeDisplay == DisplayType.InlineFlex)
            ? FlexEngine.LayoutFlex(child, placeContentX, placeContentY, childContentW, knownH, viewportWidth, viewportHeight)
            : nodeDisplay == DisplayType.Table
                ? TableEngine.LayoutTable(child, placeContentX, placeContentY, childContentW, viewportWidth, viewportHeight)
                : LayoutChildren(child.Children, placeContentX, placeContentY, childContentW, viewportWidth, viewportHeight, knownH);

        if (childContentH == 0 && !string.IsNullOrEmpty(child.DisplayText))
        {
            using var font = TextMeasure.CreateFont(child);
            var lh = child.GetLineHeight(child.GetFontSize());
            var lines = TextMeasure.WrapText(child.RenderedText(), Math.Max(childContentW, 1f), font, child.GetWhiteSpace(), lh);
            childContentH = lines.Sum(l => l.Height);
        }
        if (explicitH > 0)
            childContentH = isBorderBox
                ? Math.Max(0, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        if (replaced.HasValue) childContentH = replaced.Value.Height;
        // §10.7: a float's used height clamps to min/max-height like any block's — the float
        // path skipped it, so 'height: 200px; max-height: 100px' stayed 200px
        // (max-height-applies-to-018).
        else childContentH = ClampContentHeight(child, childContentH, parentContentHeight, border, padding, fontSize);

        child.CollapseShiftApplied = chainOffset;
        child.Box = new BoxDimensions
        {
            ContentBox = new SKRect(placeContentX, placeContentY,
                                    placeContentX + childContentW, placeContentY + childContentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        var outerBottom = outerTop + margin.Top + border.Top + padding.Top + childContentH + padding.Bottom + border.Bottom + margin.Bottom;
        var outerRight = outerLeft + margin.Left + border.Left + padding.Left + childContentW + padding.Right + border.Right + margin.Right;
        return new ActiveFloat(outerLeft, outerTop, outerRight, outerBottom, side, chainOffset, child);
    }

    /// <summary>The outcome of placing a float that was met inside an inline run: a positive
    /// <see cref="Shift"/> moves the current line box's already-laid-out content right of the
    /// float (§9.5 — the line box created next to the float is shortened by its margin box, and
    /// content that was already on the line re-flows into the remaining band).</summary>
    private readonly record struct RunFloatPlacement(float Shift)
    {
        public static readonly RunFloatPlacement None = default;
    }

    /// <summary>Lays out a float met inside an inline run: the node's content (a subtree or a
    /// text run) lays out at the float's box, and the returned active float narrows every band
    /// from the float's top down. Sizing follows §10.3.5 / §10.3.2 (explicit width, replaced
    /// intrinsic size, or shrink-to-fit, clamped by min/max); the Y placement follows §9.5.1
    /// rules 4-9 — as high as the current line box allows (or, when the run's last line box was
    /// just committed empty of content, on THAT line's top — rule 6 + rule 8, the same open-line
    /// join the direct-child path applies), no higher than an earlier float (rule 5), as far to
    /// its side as the band permits (rule 9), dropping below the line when there is not enough
    /// horizontal room (rule 7).</summary>
    private static RunFloatPlacement PlaceInRunFloat(InlineItem item, List<ActiveFloat> floats,
        float originX, float originY, float lineY, float lineX, float lineHeight, float maxWidth,
        float viewportWidth, float viewportHeight, float bandLeft,
        float openLineY = float.NaN, float openLineUsedW = 0f, float containingBlockHeight = 0f)
    {
        var node = item.Node!;
        var side = node.GetFloat();
        var m = MeasureFloat(node, maxWidth, viewportHeight, containingBlockHeight);
        var outerW = m.OuterWidth;

        // §9.5.1: the float joins the current line box (rules 4/6 — its outer top is the line's
        // top) but may not be higher than an earlier float's outer top (rule 5). Past that floor
        // it is no longer beside the line and the placement becomes the plain slide-down form.
        // The band is measured over the extent the line actually occupies: a float whose top
        // starts partway down the line box still shortens it (§9.5 — a line box is "next to" a
        // float when they overlap vertically).
        var lineTopAbs = originY + lineY;
        var lineH = Math.Max(lineHeight, 1f);
        // §9.5.1 rules 6+8: a float generated while the run's last line box is still "open" in
        // source order — the line a <br> just ended, or the run's last line before a following
        // float — belongs on that line when it fits beside what the line already holds (the
        // same open-line join the direct-child float path applies). The line is closed, so its
        // content cannot re-flow: the fit test below is the whole join condition.
        var joinedLine = false;
        if (!float.IsNaN(openLineY) && originY + openLineY < lineTopAbs - 0.5f)
        {
            var (_, jw) = AvailableBand(floats, originY + openLineY, lineH, originX, maxWidth);
            if (openLineUsedW > 0f
                ? outerW <= jw - openLineUsedW + 0.5f
                : TryPlaceFloatAt(floats, side, originY + openLineY, outerW, originX, maxWidth, out _))
            {
                lineTopAbs = originY + openLineY;
                joinedLine = true;
            }
        }
        var outerTop = lineTopAbs;
        foreach (var f in floats) outerTop = Math.Max(outerTop, f.Top);
        // §9.5.2 constraint #10: 'clear' on an in-run float places its top outer edge below
        // earlier matching floats without advancing the inline flow cursor.
        outerTop = ApplyClear(node.GetClear(), floats, outerTop);
        var dropped = outerTop > lineTopAbs + 0.5f;
        var (bx, bw) = AvailableBand(floats, dropped ? outerTop : lineTopAbs, lineH, originX, maxWidth);
        var contentLeftAbs = originX + bandLeft;
        var contentRightAbs = originX + lineX;
        var lineHasContent = contentRightAbs > contentLeftAbs + 0.5f;
        float provLeft = 0f;
        if (!dropped && !TryPlaceFloatAt(floats, side, lineTopAbs, outerW, originX, maxWidth, out provLeft))
        {
            // Rule 7: not enough horizontal room beside existing floats at the line top.
            // When the line holds content, the float drops below the line box; when the line is
            // still empty (c414-flt-wrap-001's right float after a 1x1 left float), it slides
            // down past the blocking float's bottom without skipping an empty strut height.
            dropped = true;
            if (lineHasContent) outerTop = lineTopAbs + lineH;
        }
        if (!dropped && side == FloatType.Right && lineHasContent)
        {
            // A right float cannot move the line's content leftward: overlapping the line's
            // already-laid-out content (the overflowing nowrap line) drops it below the line.
            var provRight = provLeft + outerW;
            if (provLeft < contentRightAbs && provRight > contentLeftAbs)
            {
                dropped = true;
                outerTop = lineTopAbs + lineH;
            }
        }
        if (!dropped && side == FloatType.Left && !joinedLine && lineHasContent)
        {
            // Rule 7 on the left side too: re-flowing the line's content beside the float is
            // only possible while the content still fits the remaining band. When it does not
            // — floats-placement-vertical-003's 100px float beside a 5px "H" in a 100px body —
            // the float drops below the line box instead of pushing the content out.
            var contentWOnLine = Math.Max(0f, contentRightAbs - contentLeftAbs);
            if (bx < contentRightAbs && contentWOnLine + outerW > bw + 0.5f)
            {
                dropped = true;
                outerTop = lineTopAbs + lineH;
            }
        }
        float outerLeft = provLeft;
        if (dropped)
        {
            // Rule 7 continued: slide downward until either it fits or there are no more floats
            // present (LayoutFloat's loop).
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (TryPlaceFloatAt(floats, side, outerTop, outerW, originX, maxWidth, out outerLeft))
                    goto placedInRun;
                var nearest = float.MaxValue;
                foreach (var f in floats)
                    if (f.Bottom > outerTop) nearest = Math.Min(nearest, f.Bottom);
                if (nearest == float.MaxValue) break;
                outerTop = nearest;
            }
            outerLeft = side == FloatType.Left ? originX : originX + maxWidth - outerW;
        }
    placedInRun:
        var outerRight = outerLeft + outerW;
        var af = LayoutFloatBox(node, side, m, outerLeft, outerTop, viewportWidth, viewportHeight,
                                containingBlockHeight, 0f);
        floats.Add(af);
        // §9.5: the current line box — created next to the float — is shortened by the float's
        // margin box, and content that was already on the line re-flows into the remaining band
        // (floats-placement-vertical-001a, floats-006, c414-flt-wrap-001). A zero-height float at
        // the line top (af.Bottom <= lineTopAbs, floats-placement-001) does not narrow the line's
        // band and therefore does not shift lineX.
        var reflow = RunFloatPlacement.None;
        if (!dropped && !joinedLine && side == FloatType.Left && af.Bottom > lineTopAbs &&
            outerRight > contentLeftAbs &&
            (outerLeft < contentRightAbs || outerLeft <= contentLeftAbs + 0.5f))
            reflow = new RunFloatPlacement(outerRight - contentLeftAbs);

        return reflow;
    }

    /// <summary>True when an inline box is broken around an IN-FLOW block-level child (§9.2.1.1).
    /// Floated and absolutely/fixed-positioned children are blockified by §9.7 for their own
    /// layout, but they are out of flow and do not split their enclosing inline box
    /// (floats-149's inline wrapper around two floated spans keeps its margin-left).</summary>
    private static bool IsBrokenAroundInFlowBlock(LayoutNode node)
    {
        if (node.GetDisplay() is not (DisplayType.Inline or DisplayType.None)) return false;
        foreach (var child in node.Children)
        {
            if (child.GetFloat() != FloatType.None) continue;
            if (child.GetPosition() is PositionType.Absolute or PositionType.Fixed) continue;
            if (child.GetDisplay() is DisplayType.Block or DisplayType.Flex or DisplayType.Table
                                    or DisplayType.ListItem)
                return true;
        }
        return false;
    }

    /// <summary>CSS 2.1 §9.5: "The border box of a table, a block-level replaced element, or an
    /// element in the normal flow that establishes a new block formatting context (such as an
    /// element with 'overflow' other than 'visible') must not overlap the margin box of any
    /// floats in the same block formatting context as the element itself."</summary>
    private static bool AvoidsFloatsInBfc(LayoutNode child) =>
        EstablishesBlockFormattingContext(child) || IsReplacedImage(child);

    private static (float BorderTop, float EffX, float EffW, float WantedW, float ProbeH) FitBfcBesideFloats(
        LayoutNode child, List<ActiveFloat> floats, float borderTop,
        float contentX, float contentW, float parentContentHeight, float viewportHeight, float childFontSize)
    {
        var childPad = child.GetPadding(contentW, viewportHeight, childFontSize);
        if (child.GetDisplay() is DisplayType.Table or DisplayType.InlineTable && TableEngine.IsBorderCollapse(child))
            childPad = default;
        var childBorder = child.GetBorderWidth();
        var horizBp = childBorder.Left + childBorder.Right + childPad.Left + childPad.Right;
        var vertBp = childBorder.Top + childBorder.Bottom + childPad.Top + childPad.Bottom;
        var isHtmlTable = child.TagName == "TABLE";
        var isBB = child.Style.GetPropertyValueSafe("box-sizing") == "border-box" || isHtmlTable;
        var replaced = TryResolveReplacedSize(child, contentW, parentContentHeight, viewportHeight);
        float wantedW;
        if (replaced.HasValue)
        {
            wantedW = (isBB ? Math.Max(0f, replaced.Value.Width - horizBp) : replaced.Value.Width) + horizBp;
        }
        else if (!child.IsAutoWidth())
        {
            var specW = child.GetWidth(contentW);
            wantedW = (isBB ? Math.Max(0f, specW - horizBp) : specW) + horizBp;
        }
        else
        {
            wantedW = 0f;
        }
        var maxW = child.GetMaxWidth(contentW, childFontSize);
        if (maxW < float.PositiveInfinity && !(IsPercentValue(child, "max-width") && contentW <= 0f))
        {
            var maxBorderBox = isBB ? Math.Max(0f, maxW) : maxW + horizBp;
            if (wantedW > maxBorderBox) wantedW = maxBorderBox;
        }
        var minW = child.GetMinWidth(contentW, childFontSize);
        if (minW > 0f && !(IsPercentValue(child, "min-width") && contentW <= 0f))
        {
            var minBorderBox = isBB ? Math.Max(0f, minW) : minW + horizBp;
            if (wantedW < minBorderBox) wantedW = minBorderBox;
        }
        // §17.5.2.2: a table's border box cannot shrink narrower than its minimum content width
        // plus its horizontal border/padding (floats-wrap-bfc-003-left-table / 003-right-table /
        // floats-wrap-bfc-004).
        if (child.GetDisplay() == DisplayType.Table)
        {
            var tableMinBorderBox = TableEngine.MeasureTableContentWidths(child, contentW, viewportHeight).SumMin + horizBp;
            wantedW = Math.Max(wantedW, tableMinBorderBox);
        }

        var probeH = 1f;
        if (replaced.HasValue)
        {
            var repContentH = isBB ? Math.Max(0f, replaced.Value.Height - vertBp) : replaced.Value.Height;
            repContentH = ClampContentHeight(child, repContentH, parentContentHeight, childBorder, childPad, childFontSize);
            probeH = repContentH + vertBp;
        }
        else if (!child.IsAutoHeight() && !IsPercentValue(child, "height"))
        {
            var specH = child.GetHeight(parentContentHeight, childFontSize, viewportHeight);
            var specContentH = isBB ? Math.Max(0f, specH - vertBp) : specH;
            specContentH = ClampContentHeight(child, specContentH, parentContentHeight,
                                              childBorder, childPad, childFontSize);
            probeH = specContentH + vertBp;
        }
        var (fitY, fitX, fitW) = FitBandBelow(floats, borderTop, probeH, wantedW, contentX, contentW);
        if (fitY > borderTop + 0.5f)
        {
            borderTop = fitY;
            RetireFloats(floats, borderTop);
        }
        return (borderTop, fitX, fitW, wantedW, probeH);
    }
}
