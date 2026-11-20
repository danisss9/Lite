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

    /// <summary>Width of the narrowest thing that must fit on the first line before it may be
    /// shortened any further: the first atomic inline box, or the first word of the first text
    /// item. Whitespace-only items are skipped — they are dropped at the start of a line.</summary>
    private static float FirstUnbreakableWidth(List<InlineItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case InlineItemKind.OutOfFlow or InlineItemKind.LineBreak or InlineItemKind.InlineEdge:
                    continue;
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
            var (lbx, lbw) = AvailableBand(floats, openLineY, 1, contentX, contentW);
            if (outerW <= lbw - openLineUsedW + 0.5f) placeY = openLineY;
        }
        // §9.5.1 rule 5: the outer top of a floating box may not be higher than the outer top of
        // any block or floated box generated by an element earlier in the source document. The
        // cursor already sits at or below earlier blocks' tops; the active floats are the remaining
        // constraint — applied AFTER the open-line join so the join cannot drop the float back
        // above them (floats-146: the right float C may not rise beside A, above the dropped
        // float B).
        foreach (var f in floats) placeY = Math.Max(placeY, f.Top);
        // Determine X based on side, respecting existing floats
        float outerLeft;
        // Try to place; if it doesn't fit, slide down
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var (bx, bw) = AvailableBand(floats, placeY, 1, contentX, contentW);
            if (outerW <= bw + 0.5f)
            {
                outerLeft = side == FloatType.Left ? bx : bx + bw - outerW;
                goto placed;
            }
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

        // 'width: 0' is a used width of zero — only an AUTO width shrink-to-fits (floats-153's
        // zero-width float with a negative margin must not grow to its text).
        var hasExplicitW = !child.IsAutoWidth();
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
        var maxW = child.GetMaxWidth(contentW, fontSize);
        if (maxW < float.PositiveInfinity && !(IsPercentValue(child, "max-width") && contentW <= 0f))
        {
            var maxContent = isBorderBoxW
                ? Math.Max(0f, maxW - border.Left - border.Right - padding.Left - padding.Right) : maxW;
            childContentW = Math.Min(childContentW, maxContent);
        }
        var minW = child.GetMinWidth(contentW, fontSize);
        if (minW > 0f && !(IsPercentValue(child, "min-width") && contentW <= 0f))
        {
            var minContent = isBorderBoxW
                ? Math.Max(0f, minW - border.Left - border.Right - padding.Left - padding.Right) : minW;
            childContentW = Math.Max(childContentW, minContent);
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
        var isBorderBox = child.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var explicitH = child.GetHeight(parentContentHeight, 0, viewportHeight);
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
            var (jx, jw) = AvailableBand(floats, originY + openLineY, lineH, originX, maxWidth);
            if (outerW <= jw - openLineUsedW + 0.5f)
            {
                lineTopAbs = originY + openLineY;
                joinedLine = true;
            }
        }
        var outerTop = lineTopAbs;
        foreach (var f in floats) outerTop = Math.Max(outerTop, f.Top);
        var dropped = outerTop > lineTopAbs + 0.5f;
        var (bx, bw) = AvailableBand(floats, dropped ? outerTop : lineTopAbs, lineH, originX, maxWidth);
        var contentLeftAbs = originX + bandLeft;
        var contentRightAbs = originX + lineX;
        if (!dropped && outerW > bw + 0.5f)
        {
            // Rule 7: not enough horizontal room beside the line — it moves below the line box.
            dropped = true;
            outerTop = lineTopAbs + lineH;
            (bx, bw) = AvailableBand(floats, outerTop, lineH, originX, maxWidth);
        }
        if (!dropped && side == FloatType.Right)
        {
            // A right float cannot move the line's content leftward: overlapping the line's
            // already-laid-out content (the overflowing nowrap line) drops it below the line.
            var provRight = bx + bw;
            var provLeft = provRight - outerW;
            if (provLeft < contentRightAbs && provRight > contentLeftAbs && contentRightAbs > contentLeftAbs)
            {
                dropped = true;
                outerTop = lineTopAbs + lineH;
                (bx, bw) = AvailableBand(floats, outerTop, lineH, originX, maxWidth);
            }
        }
        if (!dropped && side == FloatType.Left && !joinedLine)
        {
            // Rule 7 on the left side too: re-flowing the line's content beside the float is
            // only possible while the content still fits the remaining band. When it does not
            // — floats-placement-vertical-003's 100px float beside a 5px "H" in a 100px body —
            // the float drops below the line box instead of pushing the content out.
            var contentWOnLine = Math.Max(0f, contentRightAbs - contentLeftAbs);
            if (bx < contentRightAbs && contentRightAbs > contentLeftAbs &&
                contentWOnLine + outerW > bw + 0.5f)
            {
                dropped = true;
                outerTop = lineTopAbs + lineH;
                (bx, bw) = AvailableBand(floats, outerTop, lineH, originX, maxWidth);
            }
        }
        if (dropped)
        {
            // Rule 7 continued: slide downward until either it fits or there are no more floats
            // present (LayoutFloat's loop).
            for (var attempt = 0; attempt < 50; attempt++)
            {
                (bx, bw) = AvailableBand(floats, outerTop, lineH, originX, maxWidth);
                if (outerW <= bw + 0.5f) break;
                var nearest = float.MaxValue;
                foreach (var f in floats)
                    if (f.Bottom > outerTop) nearest = Math.Min(nearest, f.Bottom);
                if (nearest == float.MaxValue) break;
                outerTop = nearest;
            }
        }
        // Rule 9: as far to its side as the band permits (the band at the final outerTop).
        if (!dropped)
        {
            (bx, bw) = AvailableBand(floats, outerTop, lineH, originX, maxWidth);
        }
        float outerLeft;
        float outerRight;
        if (side == FloatType.Right)
        {
            outerRight = bx + bw;
            outerLeft = outerRight - outerW;
        }
        else
        {
            outerLeft = bx;
            outerRight = outerLeft + outerW;
        }
        // §9.5: the current line box — created next to the float — is shortened by the float's
        // margin box, and content that was already on the line re-flows into the remaining band
        // (floats-placement-vertical-001a, floats-006, c414-flt-wrap-001). A dropped float is
        // below the line and displaces nothing on it; a float joined to the run's just-committed
        // line cannot move that closed line's content, and the join's fit test already excludes
        // the overlap.
        var reflow = RunFloatPlacement.None;
        if (!dropped && !joinedLine && side == FloatType.Left && outerLeft < contentRightAbs &&
            contentRightAbs > contentLeftAbs)
            reflow = new RunFloatPlacement(outerRight - contentLeftAbs);

        floats.Add(LayoutFloatBox(node, side, m, outerLeft, outerTop, viewportWidth, viewportHeight,
                                  containingBlockHeight, 0f));
        return reflow;
    }
}
