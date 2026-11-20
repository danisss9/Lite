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
        var fontSize = child.GetFontSize();
        var margin = child.GetMargin(contentW, viewportHeight, fontSize);
        var padding = child.GetPadding(contentW, viewportHeight, fontSize);
        var border = child.GetBorderWidth();

        // Shrink-to-fit: use explicit width or half container as heuristic. 'width: 0' is a used
        // width of zero — only an AUTO width shrink-to-fits (floats-153's zero-width float with a
        // negative margin must not grow to its text).
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
        // Determine X based on side, respecting existing floats
        float placeContentX;
        // Try to place; if it doesn't fit, slide down
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var (bx, bw) = AvailableBand(floats, placeY, 1, contentX, contentW);
            if (outerW <= bw + 0.5f)
            {
                if (side == FloatType.Left)
                    placeContentX = bx + margin.Left + border.Left + padding.Left;
                else
                    placeContentX = bx + bw - margin.Right - border.Right - padding.Right - childContentW;
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
        placeContentX = side == FloatType.Left
            ? contentX + margin.Left + border.Left + padding.Left
            : contentX + contentW - margin.Right - border.Right - padding.Right - childContentW;

    placed:
        var placeContentY = placeY + margin.Top + border.Top + padding.Top;

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

        // The float's placement already sits at the running collapsed-margin chain's bottom
        // (placeY = runY + pendingMargin) — recorded so the self-collapsing re-anchoring shift
        // only moves the float by the chain's remaining growth (see the ActiveFloat return).
        child.CollapseShiftApplied = chainOffset;
        child.Box = new BoxDimensions
        {
            ContentBox = new SKRect(placeContentX, placeContentY,
                                    placeContentX + childContentW, placeContentY + childContentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        var outerTop = placeY;
        var outerBottom = placeY + margin.Top + border.Top + padding.Top + childContentH + padding.Bottom + border.Bottom + margin.Bottom;
        var outerLeft = placeContentX - padding.Left - border.Left - margin.Left;
        var outerRight = placeContentX + childContentW + padding.Right + border.Right + margin.Right;

        // The float's placement already sits at the running collapsed-margin chain's bottom
        // (placeY = runY + pendingMargin): record that offset so a self-collapsing ancestor's
        // re-anchoring shift only moves the float by the chain's remaining growth.
        return new ActiveFloat(outerLeft, outerTop, outerRight, outerBottom, side, chainOffset, child);
    }

    /// <summary>Lays out a float met inside an inline run: the node's content (a subtree or a
    /// text run) lays out at the float's box, and the returned active float narrows every band
    /// from the float's top down. Sizing follows §10.3.5 (explicit width or shrink-to-fit,
    /// clamped by min/max); the Y placement follows §9.5.1 rule 4 — the float's top aligns
    /// with the current line box's top.</summary>
    private static void PlaceInRunFloat(InlineItem item, List<ActiveFloat> floats,
        float originX, float originY, float lineY, float lineX, float lineHeight, float maxWidth,
        float viewportWidth, float viewportHeight, float bandLeft)
    {
        var node = item.Node!;
        var side = node.GetFloat();
        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(maxWidth, viewportHeight, fontSize);
        var padding = node.GetPadding(maxWidth, viewportHeight, fontSize);
        var border = node.GetBorderWidth();

        // §10.3.5 sizing: explicit width or shrink-to-fit, clamped by min/max (the same
        // discipline the direct-child float path applies).
        var hasExplicitW = !node.IsAutoWidth();
        var explicitW = hasExplicitW ? node.GetWidth(maxWidth) : 0f;
        float childContentW;
        if (hasExplicitW)
        {
            var isBB = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            childContentW = isBB
                ? Math.Max(0, explicitW - border.Left - border.Right - padding.Left - padding.Right)
                : explicitW;
        }
        else
        {
            var maxAvail = Math.Max(0f, maxWidth - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right);
            childContentW = IntrinsicSizer.ShrinkToFit(node, maxAvail, viewportHeight);
        }
        var isBBW = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var maxW = node.GetMaxWidth(maxWidth, fontSize);
        if (maxW < float.PositiveInfinity)
            childContentW = Math.Min(childContentW,
                isBBW ? Math.Max(0f, maxW - border.Left - border.Right - padding.Left - padding.Right) : maxW);
        var minW = node.GetMinWidth(maxWidth, fontSize);
        if (minW > 0f)
            childContentW = Math.Max(childContentW,
                isBBW ? Math.Max(0f, minW - border.Left - border.Right - padding.Left - padding.Right) : minW);
        childContentW = Math.Max(0, childContentW);
        var outerW = margin.Left + border.Left + padding.Left + childContentW + padding.Right + border.Right + margin.Right;

        // The float's box: beside the band's end edge for its side, at the current line's top
        // (§9.5.1 rules 4-5: as high as the line permits, as far to its side as possible). When
        // the float would overlap the line's already-placed content (the content's extent
        // reaches past the float's inner edge — the overflowing nowrap line), it drops below
        // the line box (§9.5.1: a float never overlaps the line's content).
        var (bx, bw) = AvailableBand(floats, originY + lineY, 1, originX, maxWidth);
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
        var outerTop = originY + lineY;
        var contentLeftAbs = originX + bandLeft;
        var contentRightAbs = originX + lineX;
        if (outerLeft < contentRightAbs && outerRight > contentLeftAbs && contentRightAbs > contentLeftAbs)
            outerTop = originY + lineY + lineHeight;
        var placeContentX = outerLeft + margin.Left + border.Left + padding.Left;
        var placeContentY = outerTop + margin.Top + border.Top + padding.Top;

        var explicitH = node.GetHeight(0, 0, viewportHeight);
        float childContentH;
        if (explicitH > 0)
        {
            var isBBH = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            childContentH = isBBH
                ? Math.Max(0, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        }
        else
        {
            childContentH = LayoutChildren(node.Children, placeContentX, placeContentY,
                childContentW, viewportWidth, viewportHeight, 0);
            if (childContentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
            {
                using var font = TextMeasure.CreateFont(node);
                var lh = node.GetLineHeight(fontSize);
                var lines = TextMeasure.WrapText(node.RenderedText(), Math.Max(childContentW, 1f),
                    font, node.GetWhiteSpace(), lh);
                childContentH = lines.Sum(l => l.Height);
            }
        }
        if (explicitH > 0 && node.Style.GetPropertyValueSafe("box-sizing") != "border-box")
            childContentH = explicitH;

        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(placeContentX, placeContentY,
                                    placeContentX + childContentW, placeContentY + childContentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        floats.Add(new ActiveFloat(
            outerLeft, outerTop,
            outerRight, outerTop + margin.Top + border.Top + padding.Top + childContentH + padding.Bottom + border.Bottom + margin.Bottom,
            side, 0, node));
    }
}
