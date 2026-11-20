using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal static partial class BoxEngine
{
    // -------------------------------------------------------------------------
    // Block layout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a block-level node at the given position.
    /// Returns the total margin-box height consumed (for the parent's y-cursor).
    /// </summary>
    /// <summary>Lays out a block-level node. Returns its total margin-box height and its
    /// effective bottom margin (after any last-child collapse-through).</summary>
    private static (float Height, MarginChain BottomMargin) LayoutBlock(
        LayoutNode node,
        float x, float y,
        float availableWidth,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0,
        List<ActiveFloat>? bfcFloats = null)
        => LayoutBlockCore(node, x, y, availableWidth, viewportWidth, viewportHeight, parentContentHeight, bfcFloats);

    private static (float Height, MarginChain BottomMargin) LayoutBlockCore(
        LayoutNode node,
        float x, float y,
        float availableWidth,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0,
        List<ActiveFloat>? bfcFloats = null)
    {
        if (node.GetDisplay() == DisplayType.None)
        {
            node.Box = default;
            return (0f, MarginChain.Empty);
        }

        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(availableWidth, viewportHeight, fontSize);
        // Auto horizontal margins are solved after the used width is known. Treating them as
        // lengths here can shrink an auto-width block to its font size before max-width applies.
        if (node.IsAutoMarginLeft()) margin.Left = 0f;
        if (node.IsAutoMarginRight()) margin.Right = 0f;
        // §10.6.3: an in-flow block's 'auto' top and bottom margins are 0. GetMargin's generic
        // 'auto' centres in the viewport, which sank a 'margin: auto 2px' block to mid-page
        // (inline-formatting-context-023-ref).
        if (node.IsAutoMarginTop()) margin.Top = 0f;
        if (node.IsAutoMarginBottom()) margin.Bottom = 0f;
        var padding = node.GetPadding(availableWidth, viewportHeight, fontSize);
        var border = node.GetBorderWidth();
        // §17.6.2: "in this model, a table does not have padding" — the collapsed frame's
        // half-borders span the outer cells and the table's 'padding' is ignored (the
        // table-backgrounds bc table rendered 13px/11px wider and taller than its reference).
        if (node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable && TableEngine.IsBorderCollapse(node))
            padding = default;

        // Explicit width or fill available (pass size=0 so unset width returns 0, not fontSize).
        // "Specified" is IsAutoWidth, not "> 0": 'width: 0' is a used width of zero, and reading it
        // as auto made a zero-width box fill its container instead.
        var hasExplicitW = !node.IsAutoWidth();
        var explicitW = hasExplicitW ? node.GetWidth(availableWidth) : 0f;
        // A block-level replaced box is sized from the image (§10.3.4 defers to §10.3.2's rules),
        // so its used width behaves like an explicit one for centering and box-sizing purposes.
        var replaced = TryResolveReplacedSize(node, availableWidth, parentContentHeight, viewportHeight);
        var hasUsedW = replaced.HasValue || hasExplicitW;
        var usedW = replaced?.Width ?? explicitW;
        var boxWidth = hasUsedW ? usedW : availableWidth - margin.Left - margin.Right;

        // box-sizing: with content-box (the default), an explicit `width` IS the content width —
        // padding/border are added outside it. Only border-box (and the auto/fill case) subtracts
        // padding+border from the box width. (Height already honors this below.)
        // A HTML TABLE element is the exception (§17.5.2): its width spans its borders and cell
        // spacing — "the sum of the column widths (plus cell spacing or borders)" — so an
        // explicit width is a border-box one (fixed-table-layout-002a's 23+19+382+19+262+19+34
        // = 758). Chromium's UA sheet gives exactly the table element box-sizing: border-box;
        // a generic display:table box (background-position-applies-to-001's div) stays
        // content-box, so the tag — not the display — decides.
        var isHtmlTable = node.TagName == "TABLE";
        var isBorderBoxW = node.Style.GetPropertyValueSafe("box-sizing") == "border-box" || isHtmlTable;
        var contentW = (hasUsedW && !isBorderBoxW)
            ? Math.Max(0f, usedW)
            : Math.Max(0f, boxWidth - border.Left - border.Right - padding.Left - padding.Right);

        // CSS 2.1 §10.4: clamp the tentative used width to max-width, then min-width (min wins),
        // and re-run the width rules with the clamped value. Only the absolutely-positioned path
        // did this, so 'min-width' and 'max-width' had no effect at all on an in-flow block.
        // A percentage that cannot be resolved (no containing-block width) computes to 'none'/0.
        var clampedW = contentW;
        var maxW = node.GetMaxWidth(availableWidth, fontSize);
        if (maxW < float.PositiveInfinity && !(IsPercentValue(node, "max-width") && availableWidth <= 0f))
        {
            var maxContent = isBorderBoxW
                ? Math.Max(0f, maxW - border.Left - border.Right - padding.Left - padding.Right) : maxW;
            if (clampedW > maxContent) clampedW = maxContent;
        }
        var minW = node.GetMinWidth(availableWidth, fontSize);
        if (minW > 0f && !(IsPercentValue(node, "min-width") && availableWidth <= 0f))
        {
            var minContent = isBorderBoxW
                ? Math.Max(0f, minW - border.Left - border.Right - padding.Left - padding.Right) : minW;
            if (clampedW < minContent) clampedW = minContent;
        }
        // An auto-width table is shrink-wrapped. Resolve that width before auto margins so
        // a centered table can divide the remaining space between its two margins.
        if (!hasExplicitW && node.GetDisplay() == DisplayType.Table)
            clampedW = TableEngine.MeasureTableWidth(node, clampedW, viewportWidth, viewportHeight);
        // A clamped box has a known width, so auto margins centre it like an explicit one does.
        var widthIsKnown = hasUsedW || clampedW != contentW;
        contentW = clampedW;

        // margin: auto centering — when the used width is known and one or both horizontal margins are auto
        if (widthIsKnown)
        {
            var leftAuto = node.IsAutoMarginLeft();
            var rightAuto = node.IsAutoMarginRight();
            if (leftAuto || rightAuto)
            {
                var remaining = availableWidth - contentW - border.Left - border.Right - padding.Left - padding.Right;
                if (leftAuto && rightAuto) { margin.Left = margin.Right = MathF.Max(0, remaining / 2f); }
                else if (leftAuto) { margin.Left = MathF.Max(0, remaining); }
                else { margin.Right = MathF.Max(0, remaining); }
            }
        }

        var contentX = x + margin.Left + border.Left + padding.Left;
        // §10.3.3: with a known width and non-auto margins, the difference between the box's
        // outer sum and the containing-block width goes to the ignored margin — under the
        // CONTAINING BLOCK's 'direction: rtl' the left margin absorbs it, anchoring the box at
        // its right edge (the padding-right reftests pull an inner border over an outer one
        // with a negative right margin, which only lines up when this box anchors right).
        if (widthIsKnown && !node.IsAutoMarginLeft() && !node.IsAutoMarginRight() &&
            (node.Parent ?? node).GetDirection() == "rtl")
        {
            var fillTarget = availableWidth - margin.Left - margin.Right
                             - border.Left - border.Right - padding.Left - padding.Right;
            if (Math.Abs(contentW - fillTarget) > 0.5f)
            {
                contentX = x + availableWidth - margin.Right - border.Right - padding.Right
                             - contentW - padding.Left - border.Left;
            }
        }
        var contentY = y + margin.Top + border.Top + padding.Top;

        // Resolve this node's explicit height using parentContentHeight for % and viewportHeight for vh/vw.
        // height:auto is content-based — GetHeight returns the containing-block height for auto (the
        // width-style "fill" behaviour of GetSize), which must NOT be treated as an explicit height.
        var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var hasExplicitH = !node.IsAutoHeight();
        var explicitH = hasExplicitH ? node.GetHeight(parentContentHeight, 0, viewportHeight) : 0f;
        var knownContentH = hasExplicitH
            ? (isBorderBox ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom) : explicitH)
            : 0f;

        var nodeDisplay = node.GetDisplay();
        var establishesBfc = EstablishesBlockFormattingContext(node);
        // CSS 2.1 §11.1.1: "Any space taken up by the scrollbars should be taken out of
        // (subtracted from the dimensions of) the containing block formed by the element with
        // the scrollbars." 'overflow: scroll' always shows both bars, so its flow content lays
        // out in the width left beside the vertical bar's gutter (the box's own border box is
        // unchanged). The root and body hand their scrolling to the viewport.
        var alwaysScrollbars = node.GetOverflow() == OverflowType.Scroll && node.TagName is not ("HTML" or "BODY");
        var flowW = alwaysScrollbars ? Math.Max(0f, contentW - ScrollGutter) : contentW;
        float contentH;
        float trailingMargin = 0f; // last in-flow block child's bottom margin (collapse-through candidate)
        var trailingChain = MarginChain.Empty; // the trailing chain's (maxPositive, minNegative) form
        var chainHasCommittedContent = true; // whether the trailing chain contains committed boxes
        if (nodeDisplay is DisplayType.Flex or DisplayType.InlineFlex)
            contentH = FlexEngine.LayoutFlex(node, contentX, contentY, contentW, knownContentH, viewportWidth, viewportHeight);
        else if (nodeDisplay == DisplayType.Table)
            contentH = TableEngine.LayoutTable(node, contentX, contentY, contentW, viewportWidth, viewportHeight);
        else
        {
            // Float context: a BFC-establishing block (or the root, when no ambient context was
            // passed) owns a fresh list; a normal block threads its ancestor BFC's list so floats
            // declared inside it escape into that context rather than being contained here.
            var ownsFloatContext = establishesBfc || bfcFloats is null;
            // The root's initial containing block has the viewport height even when the root's
            // own height is auto. Its percentage-height children (notably body) need that size.
            var childContainingH = node.TagName == "HTML" && !hasExplicitH ? viewportHeight : knownContentH;
            // The document root is a block-formatting-context boundary: a top margin on BODY
            // (or any first child) must push the content down the canvas, not collapse through
            // the root and escape above the viewport.
            var childBfc = establishesBfc || node.TagName == "HTML";
            // §11.1.1 for 'auto': the vertical bar appears only once the content overflows the
            // fixed height; the content is then laid out again beside the bar's gutter (as
            // browsers do), so it neither paints under the bar nor reads as a sideways overflow.
            var mayGrowVerticalBar = node.GetOverflow() == OverflowType.Auto && hasExplicitH &&
                node.TagName is not ("HTML" or "BODY") && contentW > ScrollGutter;
            for (var pass = 0; ; pass++)
            {
                var floatCtx = ownsFloatContext ? new List<ActiveFloat>() : bfcFloats!;
                contentH = LayoutChildrenImpl(node.Children, contentX, contentY, flowW, viewportWidth, viewportHeight,
                    childContainingH, border.Top + padding.Top, childBfc, floatCtx, ownsFloatContext,
                    out trailingMargin, out trailingChain, out chainHasCommittedContent,
                    nodeDisplay == DisplayType.ListItem ? node.InsideMarkerIndent() : 0f);
                if (pass > 0 || !mayGrowVerticalBar || !ownsFloatContext) break;
                var extent = ScrollableExtent(node);
                var naturalRight = extent?.Right ?? contentX;
                var naturalBottom = extent?.Bottom ?? contentY;
                var sideways = naturalRight - contentX > contentW;
                if (MathF.Max(contentH, naturalBottom - contentY) <= knownContentH - (sideways ? ScrollGutter : 0f)) break;
                flowW = contentW - ScrollGutter;
            }
        }

        // Block elements with no children but own text (e.g. <label>, <p>, <h1>):
        node.OwnTextWidth = 0f;
        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var ws = node.GetWhiteSpace();
            var lh = node.GetLineHeight(node.GetFontSize());
            // An 'inside' list marker consumes first-line width the wrap must respect, or the
            // laid-out line count diverges from what the painter draws (§12.5.1).
            var markerIndent = nodeDisplay == DisplayType.ListItem ? node.InsideMarkerIndent() : 0f;
            // §9.5: a block's own text flows around the floats of its formatting context like
            // any line box — each line takes the band the floats leave it. Wrapping at the full
            // width put 'X X' on a line a right float narrowed to 2em (inline-formatting-
            // context-009). The painter reads the same bands from LineBands.
            List<(float X, float Width)>? textBands = null;
            if (!establishesBfc && bfcFloats is { Count: > 0 } textFloats && markerIndent == 0f)
            {
                var (bx, bw) = AvailableBand(textFloats, contentY, Math.Max(lh, 1f), contentX, flowW);
                textBands = LineBandsFor(textFloats, bx, contentY, lh, contentX, flowW, bw);
            }
            // §16.1: the first line is 'text-indent' narrower (wider, when negative); the
            // painter shifts it by the same amount. A huge negative indent therefore holds the
            // whole paragraph on one line, off to the left (text-indent-010..014).
            var ownIndent = node.GetDirection() != "rtl" ? node.GetTextIndent(flowW, node.GetFontSize()) : 0f;
            if (ownIndent != 0f && markerIndent == 0f)
            {
                textBands ??= [(contentX, flowW), (contentX, flowW)];
                textBands[0] = (textBands[0].X, Math.Max(0f, textBands[0].Width - ownIndent));
            }
            node.LineBands = textBands?.Select(b => (b.X - contentX, b.Width)).ToList();
            // A ::first-line 'text-transform' re-breaks the first line (uppercase is wider).
            var lines = TextMeasure.WrapTextWithFirstLine(node.RenderedText(), FirstLineTransform(node), node.GetLanguage(),
                Math.Max(flowW - markerIndent, 1f), font, ws, lh, textBands?.Select(b => b.Width).ToList());
            contentH = lines.Sum(l => l.Height);
            node.OwnTextWidth = lines.Count == 0 ? 0f : lines.Max(l => l.Width) + markerIndent;
        }

        // Parent–last-child margin collapse-through (CSS 2.1 §8.3.1): when this block has no
        // bottom border/padding, an auto height, and is not a block formatting context, the last
        // in-flow child's bottom margin collapses with this block's own bottom margin (propagating
        // out) rather than adding to the content height. Otherwise it stays inside the content.
        var effectiveBottomChain = MarginChain.Of(margin.Bottom);
        if (trailingMargin != 0f && padding.Bottom == 0f && border.Bottom == 0f
            && node.IsAutoHeight() && !establishesBfc)
            effectiveBottomChain += trailingChain;
        else if (chainHasCommittedContent)
            contentH += trailingMargin;
        var effectiveBottomMargin = effectiveBottomChain.Value;
        // else: the whole content self-collapsed into one chain (§8.3.1) — with a bottom
        // border/padding the chain can neither escape nor extend the height: a bordered box
        // with only empty children renders its borders alone (margin-collapse-128).

        // Explicit height overrides — respect box-sizing: border-box.
        // NOT for a CSS table box: §17.5.3 makes the specified height a MINIMUM the row stretch
        // inside LayoutTable already applies, and the caption (§17.4, in the wrapper) adds on
        // top — clamping to the explicit height cut the caption band out of the wrapper's flow
        // height and pulled following content up over the table (fixed-table-layout-002a).
        // Natural horizontal content extent (§11.1.1 overflow): the widest laid-out child box
        // against the constrained width. A nowrap text line's own overflow is not tracked here —
        // the corpus's scroll cases scroll laid-out block children.
        var naturalW = 0f;
        var scrollContentH = contentH;
        if (node.GetOverflow() is OverflowType.Scroll or OverflowType.Auto or OverflowType.Hidden && ScrollableExtent(node) is var (extentR, extentB))
        {
            // A scroll container's scrollable area spans its descendants as painted —
            // relatively positioned ones included (position-relative-034: a span shifted
            // 'left: 200px' out of an 'overflow: auto' box must be scrollable into view).
            naturalW = extentR - contentX;
            scrollContentH = MathF.Max(contentH, extentB - contentY);
        }
        else
            foreach (var child in node.Children)
                naturalW = MathF.Max(naturalW, child.Box.MarginBox.Right - contentX);
        if (naturalW <= 0f) naturalW = contentW;

        // §17.5.3: a table's specified height is a MINIMUM. LayoutTable stretches its rows to
        // it, but a table whose only content is its own text has no rows to stretch: the
        // height was dropped, so a 'display: table; height: 2em' box with one nbsp was one
        // line tall (inline-table-zorder-001-ref).
        if (hasExplicitH && node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable &&
            !node.Children.Any(c => !c.TagName.StartsWith('#')) &&
            !(RawStyle(node, "height")?.Trim().EndsWith('%') ?? false))
        {
            var minTableH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
            contentH = Math.Max(contentH, minTableH);
        }

        if (hasExplicitH && node.GetDisplay() is not (DisplayType.Table or DisplayType.InlineTable))
        {
            var clampedH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;

            // Per-element overflow scroll: track natural content vs. constrained extent on
            // both axes (the vertical axis was the only one tracked; the background-attachment
            // scroll corpus scrolls horizontally too). 'hidden' is programmatically scrollable
            // via scrollTop/scrollLeft without displaying scrollbars (§11.1.1 / CSSOM View).
            var overflow = node.GetOverflow();
            // The scrollport is the content area minus each shown bar's gutter (§11.1.1), so
            // scrolling to the end brings the content's far edge out from under the bars.
            var (portW, portH) = Scrollport(overflow, alwaysScrollbars, contentW, clampedH, naturalW, scrollContentH, flowW);
            if ((overflow is OverflowType.Scroll or OverflowType.Auto or OverflowType.Hidden) && (scrollContentH > portH || naturalW > portW))
            {
                var ss = node.ScrollState ?? new ElementScrollState();
                ss.ContentHeight = scrollContentH;
                ss.ContainerHeight = portH;
                ss.ContentWidth = naturalW;
                ss.ContainerWidth = portW;
                node.ScrollState = ss;
            }
            else
            {
                node.ScrollState = null;
            }

            contentH = clampedH;
        }
        else
        {
            // aspect-ratio: derive height from width when no explicit height
            var ar = node.GetAspectRatio();
            if (ar > 0)
                contentH = contentW / ar;

            // Horizontal-only overflow with an auto height: the box's height grows to fit the
            // content, so only the width axis can overflow.
            var overflow = node.GetOverflow();
            var portW = alwaysScrollbars ? flowW : contentW;
            var sideways = (overflow is OverflowType.Scroll or OverflowType.Auto or OverflowType.Hidden) && naturalW > portW;
            if (sideways)
            {
                var ss = node.ScrollState ?? new ElementScrollState();
                ss.ContentWidth = naturalW;
                ss.ContainerWidth = portW;
                node.ScrollState = ss;
            }
            else
            {
                node.ScrollState = null;
            }
            // A shown horizontal bar takes its gutter from the height (§11.1.1); with an auto
            // height the box grows to keep its content clear of it (max-height-107's reference
            // reserves exactly that: a 200px box plus its bar). 'scroll' always shows the bar.
            if ((alwaysScrollbars || sideways && overflow == OverflowType.Auto) && node.TagName is not ("HTML" or "BODY"))
                contentH += ScrollGutter;
        }

        // A replaced box's height is the image's (or the ratio-derived) one — it has no in-flow
        // content to measure, so the child/text-derived height above does not apply to it.
        if (replaced.HasValue) contentH = replaced.Value.Height;

        // CSS 2.1 §10.7: clamp the resolved height to min-height/max-height. Percentages resolve
        // against the containing block height; when that is auto (parentContentHeight == 0) an
        // unresolvable percentage max-height computes to 'none' and min-height to 0.
        var maxH = node.GetMaxHeight(parentContentHeight, fontSize);
        if (maxH < float.PositiveInfinity && !(IsPercentValue(node, "max-height") && parentContentHeight <= 0f))
        {
            var maxContent = isBorderBox
                ? Math.Max(0f, maxH - border.Top - border.Bottom - padding.Top - padding.Bottom) : maxH;
            if (contentH > maxContent) contentH = maxContent;
        }
        var minH = node.GetMinHeight(parentContentHeight, fontSize);
        if (minH > 0f && !(IsPercentValue(node, "min-height") && parentContentHeight <= 0f))
        {
            var minContent = isBorderBox
                ? Math.Max(0f, minH - border.Top - border.Bottom - padding.Top - padding.Bottom) : minH;
            if (contentH < minContent) contentH = minContent;
        }

        margin.Bottom = effectiveBottomMargin;
        node.CollapseShiftApplied = 0; // a fresh Box invalidates any prior self-collapse anchoring
        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(contentX, contentY, contentX + contentW, contentY + contentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        var totalH = margin.Top + border.Top + padding.Top
                   + contentH
                   + padding.Bottom + border.Bottom + effectiveBottomMargin;
        return (totalH, effectiveBottomChain);
    }

    /// <summary>Measure a nested flex item's content at its available width before final placement.</summary>
    internal static float MeasureBlockHeight(LayoutNode node, float availableWidth,
        float viewportWidth, float viewportHeight)
        => LayoutBlock(node, 0f, 0f, availableWidth, viewportWidth, viewportHeight).Height;

    /// <summary>
    /// Lays out children of a block container.
    /// Consecutive inline/inline-block children are grouped into inline formatting contexts.
    /// Vertical margins between adjacent block children are collapsed (CSS 2.1 §8.3.1).
    /// Returns total content height consumed.
    /// </summary>
    /// <summary>
    /// CSS 2.1 §10.3.2 / §10.6.2 — the used content size of a REPLACED box (an <c>&lt;img&gt;</c>,
    /// or an <c>&lt;object&gt;</c> that decoded an image). An 'auto' dimension takes the intrinsic
    /// one, and when only one of width/height is specified the other is derived from the intrinsic
    /// ratio. Returns null for a non-replaced box, whose size comes from its content instead.
    /// <para>Only the inline path used to consult the intrinsic size, so a replaced box that was
    /// block-level, floated or absolutely positioned filled its container horizontally (or
    /// shrink-to-fit) and collapsed to zero height — it has no children to measure. §9.7 makes a
    /// floated or absolutely positioned image block-level, so all three paths need this.</para>
    /// </summary>
    /// <summary>A replaced box whose content is a bitmap: an IMG, a loaded OBJECT, or a
    /// ::before/::after box generated by 'content: url(...)' (§12.2) that fetched its image
    /// at parse time. Sized and painted through the same replaced-element rules as IMG.</summary>
    internal static bool IsReplacedImage(LayoutNode node) =>
        node.TagName == "IMG" ||
        (node.TagName == "OBJECT" && node.Image != null) ||
        (node.Image != null && (node.TagName == "#pseudo-before" || node.TagName == "#pseudo-after"));

    internal static (float Width, float Height)? TryResolveReplacedSize(
        LayoutNode node, float cbWidth, float cbHeight, float viewportHeight)
    {
        if (!IsReplacedImage(node)) return null;

        // The width/height content attributes are captured as the intrinsic size (Parser), so this
        // covers both a real bitmap and an attribute-declared size; CSS width/height still wins.
        float iw = node.IntrinsicWidth > 0 ? node.IntrinsicWidth : node.Image?.Width ?? 0f;
        float ih = node.IntrinsicHeight > 0 ? node.IntrinsicHeight : node.Image?.Height ?? 0f;

        // A SPECIFIED length is used as-is, 0 included: reading width:0/height:0 back as 0 conflated
        // it with 'auto' and fell through to the intrinsic size, so a 1x1 image sized to nothing
        // still painted its pixel (empty-cells-007/008). A percentage with no containing-block
        // width to resolve against computes to 'auto' (§10.3.2).
        var rawW = RawStyle(node, "width")?.Trim();
        var hasSpecW = !node.IsAutoWidth() && !(rawW?.EndsWith('%') == true && cbWidth <= 0f);
        var specW = hasSpecW ? Math.Max(0f, node.GetWidth(cbWidth)) : 0f;
        var rawH = RawStyle(node, "height")?.Trim();
        var hasSpecH = !node.IsAutoHeight() && !(rawH?.EndsWith('%') == true && cbHeight <= 0f);
        var specH = hasSpecH ? Math.Max(0f, node.GetHeight(cbHeight, 0, viewportHeight)) : 0f;

        float w, h;
        if (hasSpecW && hasSpecH) (w, h) = (specW, specH);
        else if (hasSpecW) (w, h) = (specW, iw > 0 && ih > 0 ? specW * ih / iw : ih);
        else if (hasSpecH) (w, h) = (iw > 0 && ih > 0 ? specH * iw / ih : iw, specH);
        else (w, h) = (iw, ih);
        return ConstrainReplaced(node, w, h, hasSpecW && hasSpecH, cbWidth, cbHeight,
                                 iw > 0 && ih > 0 ? ih / iw : 0f);
    }

    /// <summary>§10.4's table for replaced elements: min/max-width and min/max-height constrain
    /// the tentative size, keeping the intrinsic ratio when either dimension was 'auto' (with
    /// both specified each axis clamps on its own). Replaced sizing ignored them, so
    /// 'width: 12.5em; max-width: 6.25em; height: auto' drew a 200px image instead of 100px
    /// (inline-replaced-height-010). Percentages without a containing-block size are 'none'
    /// (max) or 0 (min).</summary>
    private static (float Width, float Height) ConstrainReplaced(
        LayoutNode node, float w, float h, bool bothSpecified, float cbWidth, float cbHeight,
        float ratio = 0f)
    {
        var fs = node.GetFontSize();
        float Limit(string prop, float basis, float none, float value) =>
            IsPercentValue(node, prop) && basis <= 0f ? none : value;
        var maxW = Limit("max-width", cbWidth, float.PositiveInfinity, node.GetMaxWidth(cbWidth, fs));
        var minW = Limit("min-width", cbWidth, 0f, node.GetMinWidth(cbWidth, fs));
        var maxH = Limit("max-height", cbHeight, float.PositiveInfinity, node.GetMaxHeight(cbHeight, fs));
        var minH = Limit("min-height", cbHeight, 0f, node.GetMinHeight(cbHeight, fs));
        maxW = Math.Max(maxW, minW);
        maxH = Math.Max(maxH, minH);
        if (float.IsPositiveInfinity(maxW) && minW <= 0f && float.IsPositiveInfinity(maxH) && minH <= 0f)
            return (w, h);
        // The height:width ratio the table preserves: the tentative size's own, or — when that
        // collapsed to 0 ('width: 0; height: auto; min-width: 6.25em') — the intrinsic one, so a
        // zero-sized image grows back to its shape (inline-replaced-height-011/width-017).
        var r = w > 0f && h > 0f ? h / w : ratio;
        if (bothSpecified || r <= 0f)
            return (Math.Clamp(w, minW, maxW), Math.Clamp(h, minH, maxH));

        var wBig = w > maxW; var wSmall = w < minW;
        var hBig = h > maxH; var hSmall = h < minH;
        if (wBig && hBig)
            return maxW / w <= maxH / h
                ? (maxW, Math.Max(minH, maxW * r))
                : (Math.Max(minW, maxH / r), maxH);
        // Both too small: grow along whichever minimum needs the larger scale — the height's
        // when minH reaches past minW at this ratio (minW/w <= minH/h, also defined at 0).
        if (wSmall && hSmall)
            return minH >= minW * r
                ? (Math.Min(maxW, minH / r), minH)
                : (minW, Math.Min(maxH, minW * r));
        if (wSmall && hBig) return (minW, maxH);
        if (wBig && hSmall) return (maxW, minH);
        if (wBig) return (maxW, Math.Max(maxW * r, minH));
        if (wSmall) return (minW, Math.Min(minW * r, maxH));
        if (hBig) return (Math.Max(maxH / r, minW), maxH);
        if (hSmall) return (Math.Min(minH / r, maxW), minH);
        return (w, h);
    }

    /// <summary>Exposed for FlexEngine to lay out flex item children.</summary>
    internal static float LayoutChildrenPublic(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0)
        => LayoutChildren(children, contentX, contentY, contentW, viewportWidth, viewportHeight, parentContentHeight);

    /// <summary>
    /// True when the node establishes a new block formatting context (CSS 2.1 §9.4.1): floats,
    /// absolutely-positioned boxes, non-block block containers (inline-block, table-cell, flex),
    /// and elements with overflow other than visible. A BFC's contents (including child margins)
    /// do not collapse with margins outside it.
    /// </summary>
    private static bool EstablishesBlockFormattingContext(LayoutNode node)
    {
        if (node.GetFloat() != FloatType.None) return true;
        var pos = node.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return true;
        if (node.GetOverflow() != OverflowType.Visible) return true;
        var display = node.GetDisplay();
        if (display is DisplayType.InlineBlock or DisplayType.Flex or DisplayType.InlineFlex
            or DisplayType.TableCell or DisplayType.Table or DisplayType.InlineTable) return true;
        // display: flow-root explicitly establishes a BFC (it maps to Block in GetDisplay).
        if (RawStyle(node, "display") == "flow-root") return true;
        return false;
    }

    /// <summary>Reads a raw style value (override first, then declared) for a property.</summary>
    private static string? RawStyle(LayoutNode node, string prop)
        => node.TryResolveStyle(prop, out var ov) ? ov : node.Style.GetPropertyValueSafe(prop);

    /// <summary>True when the property's value is a percentage (e.g. "100%").</summary>
    private static bool IsPercentValue(LayoutNode node, string prop)
        => RawStyle(node, prop)?.TrimEnd().EndsWith("%") == true;

    /// <summary>
    /// The margin that collapses outward at a block's TOP edge (CSS 2.1 §8.3.1): the block's own
    /// margin-top collapsed with the effective top margins of its first in-flow block children,
    /// following the chain as long as each block has no top border/padding and establishes no block
    /// formatting context. Stops at a border/padding/BFC boundary, or when the first in-flow child
    /// is inline (non-empty text or inline-level box) — either of which contains the margin.
    /// <para>Percentage margins resolve against <paramref name="cbWidth"/> (the parent content
    /// width); descendant percentages reuse it as an approximation since the true per-level
    /// containing-block width isn't known before layout.</para>
    /// </summary>
    private static MarginChain GetEffectiveTopMarginChain(LayoutNode node, float cbWidth)
    {
        var fontSize = node.GetFontSize();
        var ownTop = MarginChain.Of(node.GetMarginTop(total: cbWidth, size: fontSize));

        if (node.GetDisplay() is not (DisplayType.Block or DisplayType.ListItem)) return ownTop;
        if (EstablishesBlockFormattingContext(node)) return ownTop;
        var padding = node.GetPadding(cbWidth, 0, fontSize);
        var border = node.GetBorderWidth();
        if (padding.Top != 0f || border.Top != 0f) return ownTop;

        return ownTop + FirstInFlowSiblingChain(node, cbWidth);
    }

    /// <summary>The collapsed top-margin chain this element's first in-flow content opens: the
    /// first in-flow child's chain — continued through consecutive SELF-COLLAPSING siblings
    /// (§8.3.1: a box with no border/padding/in-flow content/height has its own top and bottom
    /// margins adjoining, so the chain flows THROUGH it into the next sibling's top margin —
    /// the empty div between a body and its first paragraph must not eat the paragraph's
    /// margin-top: clear-applies-to-001's reference shape).</summary>
    private static MarginChain FirstInFlowSiblingChain(LayoutNode node, float cbWidth)
    {
        foreach (var child in node.Children)
        {
            var d = child.GetDisplay();
            if (d == DisplayType.None) continue;
            var pos = child.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed) continue;
            if (child.TagName == "#text")
            {
                // Whitespace-only text is skipped by the flow (inter-block artifact); real text is
                // inline content that contains the margin.
                if (string.IsNullOrWhiteSpace(child.DisplayText)) continue;
                return MarginChain.Empty;
            }
            if (child.GetFloat() != FloatType.None) continue; // floats don't collapse margins
            var isBlock = d is DisplayType.Block or DisplayType.ListItem or DisplayType.Flex or DisplayType.Table;
            if (!isBlock) return MarginChain.Empty; // inline-level first child contains the margin
            // A child with 'clear' set never joins the chain (§8.3.1: the top margin collapses
            // with the first child only when "the child has no clearance"): clearance severs the
            // margins above the child, so its top margin stays inside this block. When clearance
            // ends up not being introduced the child still applies its own effective top margin
            // as the gap below (see LayoutChildrenImpl), so the rendered geometry is unchanged.
            if (child.GetClear() != ClearType.None) return MarginChain.Empty;
            // First in-flow block child: its effective top margin chain collapses with this
            // block's. The (maxPositive, minNegative) pair form survives negatives across three
            // and more levels, which the old scalar nesting lost (margin-collapse-162).
            var childChain = GetEffectiveTopMarginChain(child, cbWidth);
            // A SELF-COLLAPSING child (no in-flow content, no border/padding, not a BFC) lets the
            // chain flow through it: its own bottom margin is adjoining with the NEXT in-flow
            // sibling's top margin, which joins the same chain instead of starting below it.
            if (SelfCollapses(child, cbWidth))
                return childChain + SelfCollapserBottomJoin(child, cbWidth);
            return childChain;
        }
        return MarginChain.Empty;
    }

    /// <summary>The chain a self-collapsing box's BOTTOM margin opens into its following
    /// in-flow siblings: each empty sibling's own (collapsed-through) margins join until a
    /// content-bearing sibling's top chain lands, which terminates the join (its own bottom
    /// side is a different boundary).</summary>
    private static MarginChain SelfCollapserBottomJoin(LayoutNode empty, float cbWidth)
    {
        var fontSize = empty.GetFontSize();
        var bottom = MarginChain.Of(empty.GetMarginBottom(total: cbWidth, size: fontSize));
        var parent = empty.Parent;
        if (parent is null) return bottom;
        var started = false;
        foreach (var sibling in parent.Children)
        {
            if (!started)
            {
                if (ReferenceEquals(sibling, empty)) started = true;
                continue;
            }
            var d = sibling.GetDisplay();
            if (d == DisplayType.None) continue;
            var pos = sibling.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed) continue;
            if (sibling.TagName == "#text")
            {
                if (string.IsNullOrWhiteSpace(sibling.DisplayText)) continue;
                return bottom; // inline content terminates the chain (and contains it)
            }
            if (sibling.GetFloat() != FloatType.None) return bottom;
            var isBlock = d is DisplayType.Block or DisplayType.ListItem or DisplayType.Flex or DisplayType.Table;
            if (!isBlock) return bottom;
            if (sibling.GetClear() != ClearType.None) return bottom;
            return bottom + GetEffectiveTopMarginChain(sibling, cbWidth);
        }
        return bottom;
    }

    /// <summary>Whether <paramref name="node"/> self-collapses: no in-flow content, no
    /// border/padding on either side, auto-or-zero height, not a BFC (§8.3.1) — its own top
    /// and bottom margins are adjoining and the chain flows through it.</summary>
    private static bool SelfCollapses(LayoutNode node, float cbWidth)
    {
        if (node.GetDisplay() is not (DisplayType.Block or DisplayType.ListItem)) return false;
        if (EstablishesBlockFormattingContext(node)) return false;
        var fontSize = node.GetFontSize();
        var padding = node.GetPadding(cbWidth, 0, fontSize);
        var border = node.GetBorderWidth();
        if (padding.Top != 0f || padding.Bottom != 0f || border.Top != 0f || border.Bottom != 0f) return false;
        // A specified height (or min-height) that resolves positive keeps the box's sides apart.
        if (!node.IsAutoHeight() && node.GetHeight(cbWidth, 0, fontSize) > 0f) return false;
        // An element holding its OWN text contains line boxes — the chain stops inside it.
        if (!string.IsNullOrEmpty(node.DisplayText)) return false;
        foreach (var child in node.Children)
        {
            if (child.GetDisplay() == DisplayType.None) continue;
            if (child.GetPosition() is PositionType.Absolute or PositionType.Fixed) continue;
            if (child.TagName == "#text")
            {
                if (!string.IsNullOrWhiteSpace(child.DisplayText)) return false;
                continue;
            }
            if (child.GetFloat() != FloatType.None) return false;
            return false; // any in-flow element child is content
        }
        return true;
    }

    /// <summary>
    /// The effective top margin of <paramref name="node"/>: its own top margin collapsed with the
    /// first in-flow child chain that collapses through (CSS 2.1 §8.3.1). Stops at a border/
    /// padding/BFC boundary, or when the first in-flow child is inline (non-empty text or
    /// inline-level box) — either of which contains the margin.
    /// <para>Percentage margins resolve against <paramref name="cbWidth"/> (the parent content
    /// width); descendant percentages reuse it as an approximation since the true per-level
    /// containing-block width isn't known before layout.</para>
    /// </summary>
    private static float GetEffectiveTopMargin(LayoutNode node, float cbWidth)
        => GetEffectiveTopMarginChain(node, cbWidth).Value;

    /// <summary>
    /// Collapses two adjoining vertical margins per CSS 2.1 §8.3.1: the result is the sum of
    /// the largest positive and the most-negative margin (max(positives) + min(negatives)).
    /// </summary>
    internal static float CollapseMargins(float a, float b)
    {
        var posMax = Math.Max(Math.Max(a, 0f), Math.Max(b, 0f));
        var negMin = Math.Min(Math.Min(a, 0f), Math.Min(b, 0f));
        return posMax + negMin;
    }

    /// <summary>Shifts a laid-out subtree vertically so its boxes sit <paramref name="dy"/>
    /// below their laid-out origin: every box's content box, every line fragment, and every
    /// recorded static position. Idempotent per node — a node already shifted to
    /// <paramref name="dy"/> by a nested self-collapser's fold in the same chain is skipped
    /// (each node records what it received in <see cref="LayoutNode.CollapseShiftApplied"/>).</summary>
    private static void TranslateSubtree(LayoutNode node, float dy)
    {
        var additional = dy - node.CollapseShiftApplied;
        node.CollapseShiftApplied = dy;
        if (MathF.Abs(additional) > 0.01f)
        {
            var box = node.Box;
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top + additional,
                box.ContentBox.Right, box.ContentBox.Bottom + additional);
            node.Box = box;
            if (node.InlineFragments is { } fragments)
                for (var i = 0; i < fragments.Count; i++)
                    fragments[i] = (new SKRect(fragments[i].Rect.Left, fragments[i].Rect.Top + additional,
                        fragments[i].Rect.Right, fragments[i].Rect.Bottom + additional), fragments[i].Text);
            if (node.StaticY is { } sy) node.StaticY = sy + additional;
        }
        foreach (var child in node.Children) TranslateSubtree(child, dy);
    }

    /// <summary>Collapsing margins per CSS 2.1 §8.3.1: a chain of adjoining margins collapses
    /// to the sum of its largest positive and its most-negative member — a single running
    /// scalar loses the negative part (collapse(collapse(2,−4),14) = 12, the true chain = 10),
    /// so the chain is carried as (maxPositive, minNegative) while it accumulates.</summary>
    private readonly record struct MarginChain(float Pos, float Neg)
    {
        public static readonly MarginChain Empty = new(0f, 0f);
        public float Value => Pos + Neg;
        public static MarginChain Of(float margin) => margin >= 0f ? new(margin, 0f) : new(0f, margin);
        public static MarginChain operator +(MarginChain chain, float margin)
            => new(MathF.Max(chain.Pos, MathF.Max(margin, 0f)), MathF.Min(chain.Neg, MathF.Min(margin, 0f)));
        public static MarginChain operator +(MarginChain a, MarginChain b)
            => new(MathF.Max(a.Pos, b.Pos), MathF.Min(a.Neg, b.Neg));
    }

    /// <summary>
    /// Lays out a single floated child (shrink-to-fit width) and returns the ActiveFloat descriptor.
    /// </summary>
    /// <param name="openLineY">Top of the line box the float would join (NaN when none is open),
    /// with <paramref name="openLineUsedW"/> the width its content already occupies.</param>
    /// <summary>§10.7: clamps a used content height to 'max-height', then 'min-height' (min
    /// wins). A percentage resolves against the containing block's height; when that is not
    /// known (<paramref name="cbHeight"/> &lt;= 0) it computes to 'none' (max) or 0 (min).
    /// Under 'box-sizing: border-box' the limits bound the border box.</summary>
    internal static float ClampContentHeight(LayoutNode node, float contentH, float cbHeight,
                                            EdgeSizes border, EdgeSizes padding, float fontSize)
    {
        var borderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var edges = border.Top + border.Bottom + padding.Top + padding.Bottom;
        var max = node.GetMaxHeight(Math.Max(cbHeight, 0f), fontSize);
        if (max < float.PositiveInfinity && !(IsPercentValue(node, "max-height") && cbHeight <= 0f))
            contentH = Math.Min(contentH, borderBox ? Math.Max(0f, max - edges) : max);
        var min = node.GetMinHeight(Math.Max(cbHeight, 0f), fontSize);
        if (min > 0f && !(IsPercentValue(node, "min-height") && cbHeight <= 0f))
            contentH = Math.Max(contentH, borderBox ? Math.Max(0f, min - edges) : min);
        return contentH;
    }

    /// <summary>Back-compat wrapper: returns content height INCLUDING any trailing child margin
    /// (used by callers that don't participate in parent–last-child margin collapsing). These
    /// callers (a float's content, an abs-pos box, a flex item) all establish a new block
    /// formatting context, so they own a fresh float context.</summary>
    private static float LayoutChildren(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0,
        float parentBorderPaddingTop = -1f,
        bool parentEstablishesBfc = false)
    {
        var h = LayoutChildrenImpl(children, contentX, contentY, contentW, viewportWidth, viewportHeight,
            parentContentHeight, parentBorderPaddingTop, parentEstablishesBfc,
            new List<ActiveFloat>(), ownsFloatContext: true, out var trailing, out _, out _);
        return h + trailing;
    }

    /// <summary>
    /// Lays out block-container children. Returns content height EXCLUDING the trailing margin
    /// of the last in-flow block child (reported via <paramref name="trailingMargin"/>), so the
    /// parent can collapse that margin through itself per CSS 2.1 §8.3.1.
    /// <para><paramref name="floats"/> is the active-float list of the containing block formatting
    /// context. A block that establishes a BFC owns its list; a normal (non-BFC) block reuses its
    /// ancestor BFC's list so its floats escape into that context (CSS 2.1 §9.5) — floats are not
    /// contained by a non-BFC parent and interact with the BFC's other floats. Only the BFC root
    /// (<paramref name="ownsFloatContext"/>) grows its height to contain the floats.</para>
    /// </summary>
    private static float LayoutChildrenImpl(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight,
        float parentBorderPaddingTop,
        bool parentEstablishesBfc,
        List<ActiveFloat> floats,
        bool ownsFloatContext,
        out float trailingMargin,
        out MarginChain trailingChain,
        out bool chainHasCommittedContent,
        float firstLineIndent = 0f)
    {
        // Running-margin model (CSS 2.1 §8.3.1): runY is the committed content bottom (the border
        // bottom of the last non-collapsing box) and pendingMargin is the collapsing margin still
        // accumulating below it. The previous single cursor is equivalent to runY + pendingMargin.
        var runY = contentY;
        var pending = MarginChain.Empty;
        var firstBlockSeen = false;
        var lastPlacedWasBlock = false;
        // §8.3.1: when a self-collapsing box with clearance collapses, its collapsed margin
        // chain does NOT collapse with the parent's bottom margin — the chain stays inside the
        // parent's height. True while the LAST adjoining chain contains such a box; a committed
        // box or inline run below it ends the chain (its own trailing margin escapes normally).
        var chainSeveredByClearance = false;
        // The part of the current chain that was already spent ABOVE the flow point: the first
        // self-collapser's effective top margin when it escaped with the parent's own top margin
        // (firstChildCollapse). A sibling following the re-anchored self-collapser joins the same
        // chain and lands at the chain bottom — runY + chain − this escape — rather than
        // re-spending the chain between the siblings (margin-collapse-019/162).
        var chainSpentAbove = false;
        var chainEscapedAbove = 0f;
        // Whether any box committed height or a line box laid out: when the ENTIRE content is
        // self-collapsing (§8.3.1), the one collapsed margin chain neither extends the parent's
        // height nor paints — a bordered box with only empty children renders its borders alone,
        // with no stripe of background where the collapsed margin would sit (margin-collapse-128).
        var hasCommittedContent = false;
        // The floats placed since the last committed box/line box belong to the current
        // self-collapse chain: those that follow real collapsed margins ride the chain's
        // bottom margin edge as later margins join it (margin-collapse-162).
        var chainFloatStart = 0;
        // The line box a following float may join (§9.5.1): NaN once a block-level box has been
        // placed, since there is then no open line.
        var openLineY = float.NaN;
        var openLineUsedW = 0f;
        var i = 0;

        while (i < children.Count)
        {
            var child = children[i];
            var display = child.GetDisplay();

            if (display == DisplayType.None)
            {
                child.Box = default;
                i++;
                continue;
            }

            // Absolute/fixed elements are removed from normal flow, but the flow position they
            // WOULD have taken is their static position (§10.3.7 / §10.6.4) — the used value of
            // an 'auto' left/top. It is the current flow point: after the preceding in-flow
            // content (including the margin still collapsing below it), at the left edge of the
            // band left free by any floats there.
            var pos = child.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed)
            {
                var staticY = runY + pending.Value - (chainSpentAbove ? chainEscapedAbove : 0f);
                var (staticBandX, staticBandW) = AvailableBand(floats, staticY, 1, contentX, contentW);
                // Under 'direction: rtl' the hypothetical box sits flush with the band's RIGHT
                // edge (§10.3.3 anchors blocks at their right edge under the containing block's
                // rtl, and §10.3.7 rule 1's static position follows the hypothetical box) —
                // record the edge the RTL consumer of StaticX resolves from.
                var flowRtl = children.Count > 0 &&
                              (children[0].Parent ?? children[0]).GetDirection() == "rtl";
                child.StaticX = flowRtl ? staticBandX + staticBandW : staticBandX;
                child.StaticY = staticY;
                i++;
                continue;
            }

            // 'float' applies to elements, never to text runs. A #text node shares its parent's
            // computed style, so it would otherwise inherit a (non-inherited) float/clear from a
            // floated parent and spawn a phantom full-width float that collapses the flow band.
            var floatSide = child.TagName == "#text" ? FloatType.None : child.GetFloat();

            // 'clear' applies only to block-level boxes and floats (CSS 2.1 §9.5.2); inline-level
            // boxes ignore it. (Text nodes can carry a non-inherited 'clear' from the parent's
            // computed style — honoring it on an inline run would also stall the run collector.)
            var clear = child.GetClear();
            var isBlockLevel = display == DisplayType.Block || display == DisplayType.ListItem
                            || display == DisplayType.Flex || display == DisplayType.Table;
            // FLOATS with clear keep the cursor-adjust form (the float's margin box starts at the
            // cleared position). Block-level boxes are handled inside their branch below, where
            // the §9.5.2 clearance computation needs the hypothetical border edge.
            if (clear != ClearType.None && floatSide != FloatType.None)
            {
                var clearedForFloat = ApplyClear(clear, floats, runY + pending.Value);
                RetireFloats(floats, clearedForFloat);
                // Keep runY + pendingMargin == cleared so the running margin still collapses with the
                // next box exactly as before (clearance behaviour unchanged).
                runY = clearedForFloat - pending.Value;
            }

            // Handle floated elements — taken out of normal flow but affect available width
            if (floatSide != FloatType.None)
            {
                var af = LayoutFloat(child, floatSide, floats, contentX, runY + pending.Value, contentW,
                                     viewportWidth, viewportHeight, parentContentHeight,
                                     openLineY, openLineUsedW, pending.Value);
                floats.Add(af);
                i++;
                continue;
            }

            if (isBlockLevel)
            {
                var childFontSize = child.GetFontSize();
                // Percentage margins resolve against the containing-block WIDTH (§8.3), so the
                // collapse math must use contentW — matching what LayoutBlock's GetMargin uses.
                var childMarginTop = child.GetMarginTop(total: contentW, size: childFontSize);
                // §8.3.1: when the child has no top border/padding and isn't a BFC, its own first
                // in-flow block child's top margin collapses THROUGH and propagates outward. The
                // effective top margin — the whole collapsed first-child chain — is what collapses
                // with the running margin, so it materialises as space ABOVE the child rather than
                // being absorbed inside it. (Positioning below still uses the child's OWN margin,
                // since the propagated grandchild margins are placed by the child's own recursion.)
                var childEffectiveTop = GetEffectiveTopMargin(child, contentW);

                // First in-flow child: its top margin collapses with the parent's (the child is
                // placed at the parent's content-box top) when the parent has no border/padding top
                // and does not establish a BFC (§8.3.1). Otherwise it collapses with the running
                // margin — max(positives)+min(negatives) so negative margins pull boxes together.
                // A preceding LINE BOX is in-flow content and ends adjoiningness (§8.3.1: no line
                // box may separate the two margins): the pseudo line before content-043's <p> must
                // not let the paragraph's UA top margin collapse through it and vanish.
                var firstChildCollapse = !firstBlockSeen && !hasCommittedContent &&
                    parentBorderPaddingTop == 0f && !parentEstablishesBfc;
                // A SELF-COLLAPSING FIRST child's chain stays open through it: the escaped part
                // of the chain (spent with the parent's own top margin) includes the join to the
                // NEXT in-flow sibling's top margin — the empty div between a body and its first
                // paragraph must not eat the paragraph's margin-top (clear-applies-to-001's
                // shape). Mid-flow self-collapsers keep the own-chain form: their bottom margin
                // folds into the running chain and the next sibling's own top collapses with it
                // there; opening the chain here too double-counted the join
                // (margin-collapse-033/034/035 red-walled).
                if (firstChildCollapse && clear == ClearType.None && SelfCollapses(child, contentW))
                    childEffectiveTop = (MarginChain.Of(childEffectiveTop) +
                        SelfCollapserBottomJoin(child, contentW)).Value;
                // A cleared box's effective top margin never escapes its parent (the chain stops
                // at it, see GetEffectiveTopMargin), so it applies as the gap below even when the
                // box is its parent's first — the first-child escape shortcut would drop it.
                var gap = firstChildCollapse && clear == ClearType.None
                    ? 0f : (pending + childEffectiveTop).Value;
                firstBlockSeen = true;

                // A sibling following a re-anchored self-collapser joins the chain already spent
                // above it: its border edge is the chain bottom (runY + chain − escape), so the
                // escape part is subtracted from the fresh gap (margin-collapse-019: the 40px
                // chain collapsed with the parent's top margin must not open a second gap below).
                var borderTop = runY + gap - (chainSpentAbove ? chainEscapedAbove : 0f);
                // §9.5.2 clearance for block-level boxes: the hypothetical border edge is where
                // this box would sit with clear:none (runY + gap). If that is not past the
                // relevant floats, clearance is introduced: the border edge goes to the float
                // bottom, and the margins ABOVE are severed — the preceding collapsed margin and
                // this box's own top margin both apply as plain spacing inside the parent
                // (§8.3.1: "clearance inhibits margin collapsing and acts as spacing above the
                // margin-top"). The space runY..borderTop then holds pendingMargin +
                // childEffectiveTop + C, with C possibly negative (down to keeping the edge at
                // the hypothetical position).
                var clearanceSevered = false;
                if (clear != ClearType.None)
                {
                    var clearedBorder = ApplyClear(clear, floats, borderTop);
                    if (clearedBorder > borderTop)
                    {
                        borderTop = clearedBorder;
                        pending = MarginChain.Empty;
                        clearanceSevered = true;
                        chainSeveredByClearance = true;
                        chainSpentAbove = false; // clearance severs the margins above the box
                        chainEscapedAbove = 0f;
                        // The chain below (this box's own collapsed-through margins plus
                        // following self-collapsing siblings) is adjoining from the box's top
                        // margin edge — borderTop − childEffectiveTop — not from runY: the
                        // following siblings' top margins collapse with the chain from there.
                        runY = borderTop - childEffectiveTop;
                    }
                    RetireFloats(floats, borderTop);
                }

                // §9.5: a plain in-flow block's border box OVERLAPS the floats beside it — only
                // its inline (line box) content narrows, and a block that establishes a BFC may
                // not overlap at all. Placing every block in the float's wake painted block
                // backgrounds beside the float instead of under it (floats-008/135: the red
                // block must slide UNDER the green/blue float, not out of its way).
                var (effX, effW) = EstablishesBlockFormattingContext(child)
                    ? AvailableBand(floats, borderTop, 1, contentX, contentW)
                    : (contentX, contentW);
                if (effW != contentW || effX != contentX)
                {
                    // §9.5 rule 6: "implementations should clear the said element by placing it
                    // below any preceding floats" when it cannot fit beside them — the border box
                    // must not overlap, and a BFC's specified width is not narrowed by the band
                    // (floats-bfc-001's 200px BFC drops below the 150px float and grows its
                    // auto-height parent to full coverage).
                    var wantedW = child.IsAutoWidth()
                        ? effW
                        : Math.Max(0f, child.GetWidth(contentW)
                                       + child.GetBorderWidth().Left + child.GetBorderWidth().Right
                                       + (child.GetPadding(contentW, viewportHeight, childFontSize).Left
                                          + child.GetPadding(contentW, viewportHeight, childFontSize).Right));
                    if (wantedW > effW + 0.5f)
                    {
                        var floatBottom = borderTop;
                        foreach (var f in floats) floatBottom = Math.Max(floatBottom, f.Bottom);
                        if (floatBottom > borderTop + 0.5f)
                        {
                            borderTop = floatBottom;
                            (effX, effW) = AvailableBand(floats, borderTop, 1, contentX, contentW);
                            RetireFloats(floats, borderTop);
                        }
                    }
                }
                // The pre-layout state of the spent-chain flag: the ride gate below must know
                // whether the CURRENT chain was opened by a re-anchored self-collapser (the box
                // about to fold would set the flag itself before the ride runs).
                var chainWasSpentAbove = chainSpentAbove;
                var (h, childBottomMargin) = LayoutBlock(child, effX, borderTop - childMarginTop, effW,
                                                         viewportWidth, viewportHeight, parentContentHeight, floats);
                var borderBoxH = h - childMarginTop - childBottomMargin.Value;

                // Self-collapsing block (§8.3.1): no in-flow content / border / padding / height (a
                // zero border-box height already implies height is auto-or-0 with no min-height), and
                // not a BFC → its top and bottom margins are adjoining, fold together into the running
                // margin, and the box contributes no height (so neighbouring margins collapse through).
                if (borderBoxH <= 0.01f && !EstablishesBlockFormattingContext(child))
                {
                    var own = MarginChain.Empty + childEffectiveTop + childBottomMargin;
                    pending = firstChildCollapse && !clearanceSevered ? own : pending + own;
                    // §8.3.1: a self-collapsing box's border box sits at the BOTTOM of its
                    // collapsed margin chain — the chain is spent above the box, so its
                    // containing block for absolutely-positioned descendants (and any paint
                    // origin) lands where the following content will (margin-collapse-137:
                    // the abs child's top:0 must coincide with the following sibling the chain
                    // flows into). The first-child escape part of the chain went ABOVE the
                    // parent instead, so it is subtracted to keep that placement unchanged.
                    // A box carrying its own text contains a line box and is NOT self-
                    // collapsing (its text paints from its own position, margin-collapse-130).
                    if (!clearanceSevered && string.IsNullOrEmpty(child.DisplayText))
                    {
                        // The escaped part of the chain is the first self-collapser's effective
                        // top margin when it collapsed with the parent's own top margin — that
                        // part already sits above the parent, spent by the parent's placement.
                        if (firstChildCollapse) chainEscapedAbove = childEffectiveTop;
                        chainSpentAbove = true;
                        var collapsedBorderTop = runY + pending.Value - chainEscapedAbove;
                        var dy = collapsedBorderTop - borderTop;
                        if (MathF.Abs(dy) > 0.01f)
                        {
                            // The whole subtree laid out at the pre-shift origin (floats place
                            // during the flow, abs static positions were recorded there), so
                            // every descendant box, fragment and static position shifts with
                            // the box — as do the floats the subtree escaped into the BFC list.
                            // Nested self-collapsers in the same chain already shifted their own
                            // subtrees by their own fold; the per-node applied marker keeps the
                            // re-shift a no-op for them (the chain collapses to ONE margin).
                            TranslateSubtree(child, dy);
                        }
                        // Chain floats that follow real collapsed margins ride the chain's
                        // bottom margin edge: later siblings' top margins join the same chain
                        // (a float out of flow does not interrupt the adjoining), and the
                        // float's static position tracks the chain's bottom (margin-collapse-
                        // 162's float lands on the following block). A float placed at a chain's
                        // START (no margins above it, placement offset zero) stays there — the
                        // following margins form a chain below it (margin-collapse-123).
                        var chainBottomOffset = pending.Value - chainEscapedAbove;
                        for (var fi = chainFloatStart; fi < floats.Count; fi++)
                        {
                            var f = floats[fi];
                            // Only a SPENT chain (a re-anchored self-collapser opened it — checked
                            // BEFORE this box's own fold set the flag) moves its floats: the
                            // float's own placement point was mid-chain, so later joins drag it
                            // down to the chain's bottom (margin-collapse-162). A plain internal
                            // chain opened by a committed box leaves the float at its placement
                            // flow point — later siblings' margins form a chain BELOW it
                            // (margin-collapse-163's float stays put; margin-collapse-123).
                            if (!chainWasSpentAbove || f.CollapseShift <= 0f) continue;
                            var additional = chainBottomOffset - f.CollapseShift;
                            if (MathF.Abs(additional) > 0.01f)
                            {
                                f.Top += additional;
                                f.Bottom += additional;
                                f.CollapseShift = chainBottomOffset;
                                if (f.Node is { } floatNode && floatNode.CollapseShiftApplied != dy)
                                {
                                    var fb = floatNode.Box;
                                    fb.ContentBox = new SKRect(fb.ContentBox.Left, fb.ContentBox.Top + additional,
                                        fb.ContentBox.Right, fb.ContentBox.Bottom + additional);
                                    floatNode.Box = fb;
                                }
                                if (f.Node is { } fn) fn.CollapseShiftApplied = chainBottomOffset;
                                floats[fi] = f;
                            }
                        }
                    }
                }
                else
                {
                    // Commit: runY advances to the child's border-box bottom; its (effective, after
                    // its own last-child collapse-through) bottom margin becomes the running margin.
                    runY = borderTop + borderBoxH;
                    pending = childBottomMargin;
                    chainSeveredByClearance = false; // a committed box ends the severed chain
                    chainSpentAbove = false; // its placement consumed the spent chain
                    chainEscapedAbove = 0f;
                    hasCommittedContent = true;
                    chainFloatStart = floats.Count; // a new chain starts after committed content
                }
                lastPlacedWasBlock = true;
                openLineY = float.NaN; // a block-level box closes any open line box
                i++;
            }
            else
            {
                // Collect consecutive inline / inline-block / BR children into a run. The current
                // child is always taken first (it is inline — blocks/floats are handled above), so
                // the run is never empty and i always advances. Subsequent block/float children end
                // the run for their own handling; 'clear' is ignored here (inline boxes don't clear).
                var run = new List<LayoutNode> { child };
                i++;
                while (i < children.Count)
                {
                    var d = children[i].GetDisplay();
                    // A display:none box renders nothing and neither joins a run nor splits one
                    // (§17.2.1's render-invisible rule, the same one the table normalizer parks
                    // its children for): skipping it keeps the inline content around it one run,
                    // so whitespace-only text beside it still builds no line box.
                    if (d == DisplayType.None) { i++; continue; }
                    // An out-of-flow box mid-run neither ends the line nor takes part in it: it
                    // stays in the run so the line it sits on is its static position, and so the
                    // inline content around it is not split onto two lines. (§9.7 blockifies it,
                    // so this test must precede the block-level one.)
                    var childPos = children[i].GetPosition();
                    var outOfFlow = childPos == PositionType.Absolute || childPos == PositionType.Fixed;
                    if (!outOfFlow)
                    {
                        if (d == DisplayType.Block || d == DisplayType.ListItem || d == DisplayType.Flex || d == DisplayType.Table) break;
                        if (children[i].TagName != "#text" && children[i].GetFloat() != FloatType.None) break;
                    }
                    run.Add(children[i]);
                    i++;
                }
                // Skip runs with no real inline content — whitespace-only #TEXT around
                // out-of-flow boxes. Neither creates a line box (out-of-flow boxes are removed
                // from the line, whitespace-only text builds none), so the pending margin keeps
                // collapsing across them (§8.3.1; margin-collapse-162's absolute .a must not
                // spend the chain). The out-of-flow members take their static position at the
                // flow point, exactly as the direct-child abs branch records it.
                var runHasInlineContent = run.Any(n => n.TagName == "#text"
                    ? n.DisplayText.Trim().Length > 0
                    : n.GetPosition() is not (PositionType.Absolute or PositionType.Fixed));
                if (!runHasInlineContent)
                {
                    var flowPointY = runY + pending.Value - (chainSpentAbove ? chainEscapedAbove : 0f);
                    foreach (var oof in run.Where(n => n.TagName != "#text"))
                    {
                        var (staticBandX, staticBandW) = AvailableBand(floats, flowPointY, 1, contentX, contentW);
                        // Same edge convention as the direct-child branch: the band's RIGHT edge
                        // under rtl (absolute-non-replaced-width-002).
                        oof.StaticX = (oof.Parent ?? oof).GetDirection() == "rtl"
                            ? staticBandX + staticBandW : staticBandX;
                        oof.StaticY = flowPointY;
                    }
                    continue;
                }

                // Inline content commits the pending block margin (margins don't collapse across a
                // line box) and lays out at the committed position — the chain bottom when a
                // re-anchored self-collapser already spent the chain's escaped part above.
                runY += pending.Value - (chainSpentAbove ? chainEscapedAbove : 0f);
                pending = MarginChain.Empty;
                chainSeveredByClearance = false; // a line box ends the severed chain
                chainSpentAbove = false;
                chainEscapedAbove = 0f;
                var opensFirstLine = !hasCommittedContent;
                hasCommittedContent = true;
                chainFloatStart = floats.Count; // a new chain starts after committed content
                // §12.5.1: an 'inside' list marker is the first inline box of the first line —
                // the run's first line starts past it (lines 2+ realign with the content edge).
                // §16.1: 'text-indent' indents the FIRST formatted line of the block container
                // (a negative value hangs it out to the left). Layout ignored it — only the
                // painter shifted the first line's ink, past where its line was measured.
                var blockIndent = 0f;
                if (opensFirstLine && run.Count > 0 && run[0].Parent is { } runContainer &&
                    runContainer.GetDirection() != "rtl")
                    blockIndent = runContainer.GetTextIndent(contentW, runContainer.GetFontSize());
                var runH = LayoutInlineRun(run, contentX, runY, contentW, viewportWidth, viewportHeight,
                                           floats, out var lastLineTop, out var lastLineWidth,
                                           opensFirstLine ? firstLineIndent + blockIndent : 0f, parentContentHeight);
                // Remember where the run's last line box sits: a float that comes right after it
                // belongs ON that line, not below it (§9.5.1).
                openLineY = runY + lastLineTop;
                openLineUsedW = lastLineWidth;
                runY += runH;
                lastPlacedWasBlock = false;
            }
        }

        // A block formatting context grows to contain its floats (CSS 2.1 §10.6.7); a non-BFC
        // block does not — its floats belong to an ancestor BFC and overflow this box. The last
        // in-flow block child's bottom margin can collapse through only if no float extends past
        // it, and (§8.3.1) not at all when the chain was severed by clearance: the resulting
        // collapsed margin does not collapse with the parent block's bottom margin — it stays
        // inside the parent's height (runY was set to the chain's origin when the severed box
        // folded, so runY + pendingMargin already spans the chain's full extent).
        var contentBottom = runY + pending.Value;
        var flowBottom = contentBottom;
        if (ownsFloatContext)
            foreach (var f in floats)
                contentBottom = Math.Max(contentBottom, f.Bottom);

        trailingChain = (lastPlacedWasBlock && contentBottom <= flowBottom && !chainSeveredByClearance)
            ? pending : MarginChain.Empty;
        trailingMargin = trailingChain.Value;
        chainHasCommittedContent = hasCommittedContent;
        return (contentBottom - contentY) - trailingMargin;
    }
}
