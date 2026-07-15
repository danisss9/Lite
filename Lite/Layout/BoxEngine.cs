using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Computes BoxDimensions for every LayoutNode in the tree before painting.
/// Block elements stack vertically. Inline/inline-block elements flow horizontally in line boxes.
/// </summary>
internal static class BoxEngine
{
    public static void Layout(LayoutNode root, float viewportWidth, float viewportHeight)
    {
        // Establish the root (html) font-size so `rem` units resolve correctly this pass.
        var html = root.TagName == "HTML" ? root : root.Children.FirstOrDefault(c => c.TagName == "HTML") ?? root;
        CssUnits.RootFontSize = html.GetFontSize();

        NormalizeBlockInInline(root);
        NormalizeInteractive(root);

        LayoutBlock(root, 0, 0, viewportWidth, viewportWidth, viewportHeight, viewportHeight); // root: margin-box discarded
        // Second pass: lay out all absolute/fixed nodes now that normal-flow boxes are finalised
        LayoutPositioned(root, root.Box, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// Walks the tree and resolves position:absolute and position:fixed boxes.
    /// containingBox is the padding-box of the nearest positioned ancestor.
    /// viewportBox is the full viewport rect (for position:fixed).
    /// </summary>
    private static void LayoutPositioned(LayoutNode node,
        BoxDimensions containingBox,
        float viewportWidth, float viewportHeight)
    {
        var viewportBox = new BoxDimensions
        {
            ContentBox = new SKRect(0, 0, viewportWidth, viewportHeight)
        };

        foreach (var child in node.Children)
        {
            var pos = child.GetPosition();

            if (pos == PositionType.Absolute || pos == PositionType.Fixed)
            {
                var cb = pos == PositionType.Fixed ? viewportBox : containingBox;
                ResolveAbsoluteBox(child, cb, viewportWidth, viewportHeight);
                // Recurse using this child as the new containing block
                LayoutPositioned(child, child.Box, viewportWidth, viewportHeight);
            }
            else
            {
                // Pass down nearest positioned ancestor as containing block
                var nextCb = child.IsPositioned() ? child.Box : containingBox;
                LayoutPositioned(child, nextCb, viewportWidth, viewportHeight);
            }
        }
    }

    /// <summary>
    /// CSS 2.1 §9.2.1.1 (block-in-inline): when an inline-level box contains an in-flow
    /// block-level box, the inline is broken around the block and anonymous block boxes wrap
    /// the inline pieces. We approximate this by promoting such an inline element to a block
    /// container — its text/inline runs then become anonymous block line-boxes via
    /// <see cref="LayoutChildren"/>, which is the visible result the spec requires.
    /// </summary>
    private static void NormalizeBlockInInline(LayoutNode node)
    {
        foreach (var child in node.Children)
            NormalizeBlockInInline(child);

        if (node.TagName.StartsWith('#')) return;
        // Only pure inline boxes break around a block. inline-block is already a block
        // container *and* inline-level — promoting it to block would wrongly pull it out of
        // its line, so leave it alone.
        if (node.GetDisplay() is not DisplayType.Inline) return;

        bool hasBlockChild = node.Children.Any(c =>
            !c.TagName.StartsWith('#') &&
            c.GetDisplay() is DisplayType.Block or DisplayType.ListItem or DisplayType.Table);
        if (hasBlockChild)
        {
            node.StyleOverrides["display"] = "block";
            // §9.2.1.1: when an inline box is broken around a block, the inline's vertical margins,
            // padding and borders have no effect (they must not shift the block) — drop them on the
            // promoted anonymous block container. The horizontal box-model still applies.
            node.StyleOverrides["margin-top"] = "0";
            node.StyleOverrides["margin-bottom"] = "0";
            node.StyleOverrides["padding-top"] = "0";
            node.StyleOverrides["padding-bottom"] = "0";
            node.StyleOverrides["border-top-width"] = "0";
            node.StyleOverrides["border-bottom-width"] = "0";
        }
    }

    /// <summary>
    /// Interactive-element layout collapse, re-run each layout so toggling <c>open</c> (via click or
    /// JS) re-flows:
    ///   • &lt;details&gt; — when not <c>open</c>, only the first &lt;summary&gt; is shown; the rest collapse.
    ///   • &lt;dialog&gt; — collapsed entirely unless <c>open</c> (the UA <c>dialog:not([open])</c> rule).
    /// The pre-hide display is saved (<see cref="LayoutNode.DetailsSavedDisplay"/>) so it can be
    /// restored on re-open without clobbering an author-specified display.
    /// </summary>
    private static void NormalizeInteractive(LayoutNode node)
    {
        foreach (var child in node.Children)
            NormalizeInteractive(child);

        if (node.TagName == "DIALOG")
            SetHiddenByUa(node, hide: !node.Attributes.ContainsKey("open"));

        if (node.TagName == "DETAILS")
        {
            var open = node.Attributes.ContainsKey("open");
            var summarySeen = false;
            foreach (var child in node.Children)
            {
                if (child.TagName is "#text") continue;
                if (!summarySeen && child.TagName == "SUMMARY") { summarySeen = true; continue; }
                SetHiddenByUa(child, hide: !open);
            }
        }
    }

    /// <summary>Hides/unhides a node via display:none, saving and restoring its prior display value.</summary>
    private static void SetHiddenByUa(LayoutNode n, bool hide)
    {
        if (hide)
        {
            if (n.DetailsSavedDisplay is null)
            {
                n.DetailsSavedDisplay = n.StyleOverrides.GetValueOrDefault("display") ?? "";
                n.StyleOverrides["display"] = "none";
            }
        }
        else if (n.DetailsSavedDisplay is not null)
        {
            if (n.DetailsSavedDisplay.Length == 0) n.StyleOverrides.Remove("display");
            else n.StyleOverrides["display"] = n.DetailsSavedDisplay;
            n.DetailsSavedDisplay = null;
        }
    }

    private static void ResolveAbsoluteBox(LayoutNode node, BoxDimensions cb,
        float viewportWidth, float viewportHeight)
    {
        var cbRect = cb.PaddingBox;
        var fontSize = node.GetFontSize();
        var padding = node.GetPadding(cbRect.Width, cbRect.Height, fontSize);
        var border = node.GetBorderWidth();
        var margin = node.GetMargin(cbRect.Width, cbRect.Height, fontSize);

        var top = node.GetOffsetTop(cbRect.Height, fontSize);
        var right = node.GetOffsetRight(cbRect.Width, fontSize);
        var bottom = node.GetOffsetBottom(cbRect.Height, fontSize);
        var left = node.GetOffsetLeft(cbRect.Width, fontSize);

        // Resolve width
        var explicitW = node.GetWidth(cbRect.Width);
        float contentW;
        if (explicitW > 0)
            contentW = explicitW;
        else if (!float.IsNaN(left) && !float.IsNaN(right))
            contentW = Math.Max(0, cbRect.Width - left - right - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right);
        else
        {
            // Shrink-to-fit (§10.3.7): min(max(min-content, available), max-content), where the
            // available content width is what remains of the containing block after this box's own
            // margins, border, padding and any set left/right offset are removed.
            var availW = cbRect.Width - margin.Left - margin.Right
                       - border.Left - border.Right - padding.Left - padding.Right;
            if (!float.IsNaN(left)) availW -= left;
            else if (!float.IsNaN(right)) availW -= right;
            contentW = IntrinsicSizer.ShrinkToFit(node, Math.Max(0f, availW), viewportHeight);
        }

        // Clamp to min/max-width (§10.4) — min wins over max. (Acid2's fixed scalp uses
        // width:140%; max-width:4em to pin the head's top line to a fixed size.)
        contentW = Math.Min(contentW, node.GetMaxWidth(cbRect.Width, fontSize));
        contentW = Math.Max(contentW, node.GetMinWidth(cbRect.Width, fontSize));

        // Resolve X — §4.1: use FlexStaticX as static position when left/right are both auto
        float contentX;
        if (!float.IsNaN(left))
            contentX = cbRect.Left + left + margin.Left + border.Left + padding.Left;
        else if (!float.IsNaN(right))
            contentX = cbRect.Right - right - margin.Right - border.Right - padding.Right - contentW;
        else if (node.FlexStaticX.HasValue)
            contentX = node.FlexStaticX.Value + margin.Left + border.Left + padding.Left;
        else
            contentX = cbRect.Left + margin.Left + border.Left + padding.Left;

        // Determine this node's explicit content height so children can resolve % heights.
        // height:auto is content-based — GetHeight returns the CB height for auto, so ignore it.
        var autoHeight = node.IsAutoHeight();
        var explicitHEarly = autoHeight ? 0f : node.GetHeight(cbRect.Height, 0, viewportHeight);
        float selfContentH;
        if (explicitHEarly > 0)
        {
            var isBBEarly = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            selfContentH = isBBEarly
                ? Math.Max(0f, explicitHEarly - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitHEarly;
        }
        else if (!float.IsNaN(top) && !float.IsNaN(bottom))
            selfContentH = Math.Max(0, cbRect.Height - top - bottom - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom);
        else
            selfContentH = 0f;

        // Lay out children to get content height
        var contentY0 = cbRect.Top; // temp origin for children layout
        var contentH = LayoutChildren(node.Children,
            contentX, contentY0,
            contentW, viewportWidth, viewportHeight, selfContentH);

        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var lh = node.GetLineHeight(node.GetFontSize());
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, node.GetWhiteSpace(), lh);
            contentH = lines.Sum(l => l.Height);
        }

        var explicitH = autoHeight ? 0f : node.GetHeight(cbRect.Height, 0, viewportHeight);
        if (explicitH > 0)
        {
            var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            contentH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        }
        else if (autoHeight && !float.IsNaN(top) && !float.IsNaN(bottom))
            contentH = Math.Max(0, cbRect.Height - top - bottom - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom);

        // Clamp to min/max-height (§10.7) — min wins over max (Acid2's scalp: min-height:1em
        // overrides max-height:2mm).
        contentH = Math.Min(contentH, node.GetMaxHeight(cbRect.Height, fontSize));
        contentH = Math.Max(contentH, node.GetMinHeight(cbRect.Height, fontSize));

        // Resolve Y — §4.1: use FlexStaticY as static position when top/bottom are both auto
        float contentY;
        if (!float.IsNaN(top))
            contentY = cbRect.Top + top + margin.Top + border.Top + padding.Top;
        else if (!float.IsNaN(bottom))
            contentY = cbRect.Bottom - bottom - margin.Bottom - border.Bottom - padding.Bottom - contentH;
        else if (node.FlexStaticY.HasValue)
            contentY = node.FlexStaticY.Value + margin.Top + border.Top + padding.Top;
        else
            contentY = cbRect.Top + margin.Top + border.Top + padding.Top;

        // Re-layout children at the correct absolute Y
        if (contentY != contentY0)
            LayoutChildren(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight);

        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(contentX, contentY, contentX + contentW, contentY + contentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };
    }

    // -------------------------------------------------------------------------
    // Block layout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a block-level node at the given position.
    /// Returns the total margin-box height consumed (for the parent's y-cursor).
    /// </summary>
    /// <summary>Lays out a block-level node. Returns its total margin-box height and its
    /// effective bottom margin (after any last-child collapse-through).</summary>
    private static (float Height, float BottomMargin) LayoutBlock(
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
            return (0f, 0f);
        }

        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(availableWidth, viewportHeight, fontSize);
        var padding = node.GetPadding(availableWidth, viewportHeight, fontSize);
        var border = node.GetBorderWidth();

        // Explicit width or fill available (pass size=0 so unset width returns 0, not fontSize)
        var explicitW = node.GetWidth(availableWidth);
        var boxWidth = explicitW > 0 ? explicitW : availableWidth - margin.Left - margin.Right;

        // margin: auto centering — when explicit width is set and one or both horizontal margins are auto
        if (explicitW > 0)
        {
            var leftAuto = node.IsAutoMarginLeft();
            var rightAuto = node.IsAutoMarginRight();
            if (leftAuto || rightAuto)
            {
                var remaining = availableWidth - explicitW - border.Left - border.Right - padding.Left - padding.Right;
                if (leftAuto && rightAuto) { margin.Left = margin.Right = MathF.Max(0, remaining / 2f); }
                else if (leftAuto) { margin.Left = MathF.Max(0, remaining); }
                else { margin.Right = MathF.Max(0, remaining); }
            }
        }

        // box-sizing: with content-box (the default), an explicit `width` IS the content width —
        // padding/border are added outside it. Only border-box (and the auto/fill case) subtracts
        // padding+border from the box width. (Height already honors this below.)
        var isBorderBoxW = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var contentW = (explicitW > 0 && !isBorderBoxW)
            ? Math.Max(0f, explicitW)
            : Math.Max(0f, boxWidth - border.Left - border.Right - padding.Left - padding.Right);
        var contentX = x + margin.Left + border.Left + padding.Left;
        var contentY = y + margin.Top + border.Top + padding.Top;

        // Resolve this node's explicit height using parentContentHeight for % and viewportHeight for vh/vw.
        // height:auto is content-based — GetHeight returns the containing-block height for auto (the
        // width-style "fill" behaviour of GetSize), which must NOT be treated as an explicit height.
        var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var explicitH = node.IsAutoHeight() ? 0f : node.GetHeight(parentContentHeight, 0, viewportHeight);
        var knownContentH = explicitH > 0
            ? (isBorderBox ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom) : explicitH)
            : 0f;

        var nodeDisplay = node.GetDisplay();
        var establishesBfc = EstablishesBlockFormattingContext(node);
        float contentH;
        float trailingMargin = 0f; // last in-flow block child's bottom margin (collapse-through candidate)
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
            var floatCtx = ownsFloatContext ? new List<ActiveFloat>() : bfcFloats!;
            contentH = LayoutChildrenImpl(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight,
                knownContentH, border.Top + padding.Top, establishesBfc, floatCtx, ownsFloatContext, out trailingMargin);
        }

        // Block elements with no children but own text (e.g. <label>, <p>, <h1>):
        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var ws = node.GetWhiteSpace();
            var lh = node.GetLineHeight(node.GetFontSize());
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, ws, lh);
            contentH = lines.Sum(l => l.Height);
        }

        // Parent–last-child margin collapse-through (CSS 2.1 §8.3.1): when this block has no
        // bottom border/padding, an auto height, and is not a block formatting context, the last
        // in-flow child's bottom margin collapses with this block's own bottom margin (propagating
        // out) rather than adding to the content height. Otherwise it stays inside the content.
        var effectiveBottomMargin = margin.Bottom;
        if (trailingMargin != 0f && padding.Bottom == 0f && border.Bottom == 0f
            && node.IsAutoHeight() && !establishesBfc)
            effectiveBottomMargin = CollapseMargins(margin.Bottom, trailingMargin);
        else
            contentH += trailingMargin;

        // Explicit height overrides — respect box-sizing: border-box
        if (explicitH > 0)
        {
            var clampedH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;

            // Per-element overflow scroll: track natural content vs. constrained height
            var overflow = node.GetOverflow();
            if ((overflow == OverflowType.Scroll || overflow == OverflowType.Auto) && contentH > clampedH)
            {
                var ss = node.ScrollState ?? new ElementScrollState();
                ss.ContentHeight = contentH;
                ss.ContainerHeight = clampedH;
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

            node.ScrollState = null;
        }

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
        return (totalH, effectiveBottomMargin);
    }

    /// <summary>
    /// Lays out children of a block container.
    /// Consecutive inline/inline-block children are grouped into inline formatting contexts.
    /// Vertical margins between adjacent block children are collapsed (CSS 2.1 §8.3.1).
    /// Returns total content height consumed.
    /// </summary>
    /// <summary>Exposed for FlexEngine to lay out flex item children.</summary>
    internal static float LayoutChildrenPublic(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0)
        => LayoutChildren(children, contentX, contentY, contentW, viewportWidth, viewportHeight, parentContentHeight);

    // -------------------------------------------------------------------------
    // Float tracking
    // -------------------------------------------------------------------------

    /// <summary>Represents an active float whose occupied area affects subsequent layout.</summary>
    private record struct ActiveFloat(float Left, float Top, float Right, float Bottom, FloatType Side);

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
            or DisplayType.TableCell or DisplayType.Table) return true;
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
    /// Collapses two adjoining vertical margins per CSS 2.1 §8.3.1: the result is the sum of
    /// the largest positive and the most-negative margin (max(positives) + min(negatives)).
    /// </summary>
    internal static float CollapseMargins(float a, float b)
    {
        var posMax = Math.Max(Math.Max(a, 0f), Math.Max(b, 0f));
        var negMin = Math.Min(Math.Min(a, 0f), Math.Min(b, 0f));
        return posMax + negMin;
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

    /// <summary>Remove floats whose bottom edge is at or above <paramref name="y"/>.</summary>
    private static void RetireFloats(List<ActiveFloat> floats, float y)
    {
        floats.RemoveAll(f => f.Bottom <= y);
    }

    /// <summary>
    /// Lays out a single floated child (shrink-to-fit width) and returns the ActiveFloat descriptor.
    /// </summary>
    private static ActiveFloat LayoutFloat(
        LayoutNode child, FloatType side,
        List<ActiveFloat> floats,
        float contentX, float cursorY, float contentW,
        float viewportWidth, float viewportHeight, float parentContentHeight)
    {
        var fontSize = child.GetFontSize();
        var margin = child.GetMargin(contentW, viewportHeight, fontSize);
        var padding = child.GetPadding(contentW, viewportHeight, fontSize);
        var border = child.GetBorderWidth();

        // Shrink-to-fit: use explicit width or half container as heuristic
        var explicitW = child.GetWidth(contentW);
        var maxAvail = contentW - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right;
        float childContentW;
        if (explicitW > 0)
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

        var outerW = margin.Left + border.Left + padding.Left + childContentW + padding.Right + border.Right + margin.Right;

        // Find placement Y — must not overlap existing floats on the same side
        var placeY = cursorY;
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
            var lines = TextMeasure.WrapText(child.DisplayText, Math.Max(childContentW, 1f), font, child.GetWhiteSpace(), lh);
            childContentH = lines.Sum(l => l.Height);
        }
        if (explicitH > 0)
            childContentH = isBorderBox
                ? Math.Max(0, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;

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

        return new ActiveFloat(outerLeft, outerTop, outerRight, outerBottom, side);
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
            new List<ActiveFloat>(), ownsFloatContext: true, out var trailing);
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
        out float trailingMargin)
    {
        // Running-margin model (CSS 2.1 §8.3.1): runY is the committed content bottom (the border
        // bottom of the last non-collapsing box) and pendingMargin is the collapsing margin still
        // accumulating below it. The previous single cursor is equivalent to runY + pendingMargin.
        var runY = contentY;
        var pendingMargin = 0f;
        var firstBlockSeen = false;
        var lastPlacedWasBlock = false;
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

            // Absolute/fixed elements are removed from normal flow
            var pos = child.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed)
            {
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
            if (clear != ClearType.None && (floatSide != FloatType.None || isBlockLevel))
            {
                var cleared = ApplyClear(clear, floats, runY + pendingMargin);
                RetireFloats(floats, cleared);
                // Keep runY + pendingMargin == cleared so the running margin still collapses with the
                // next box exactly as before (clearance behaviour unchanged).
                runY = cleared - pendingMargin;
            }

            // Handle floated elements — taken out of normal flow but affect available width
            if (floatSide != FloatType.None)
            {
                var af = LayoutFloat(child, floatSide, floats, contentX, runY + pendingMargin, contentW,
                                     viewportWidth, viewportHeight, parentContentHeight);
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

                // First in-flow child: its top margin collapses with the parent's (the child is
                // placed at the parent's content-box top) when the parent has no border/padding top
                // and does not establish a BFC (§8.3.1). Otherwise it collapses with the running
                // margin — max(positives)+min(negatives) so negative margins pull boxes together.
                var firstChildCollapse = !firstBlockSeen && parentBorderPaddingTop == 0f && !parentEstablishesBfc;
                var gap = firstChildCollapse ? 0f : CollapseMargins(pendingMargin, childMarginTop);
                firstBlockSeen = true;

                var borderTop = runY + gap;
                var (effX, effW) = AvailableBand(floats, borderTop, 1, contentX, contentW);
                var (h, childBottomMargin) = LayoutBlock(child, effX, borderTop - childMarginTop, effW,
                                                         viewportWidth, viewportHeight, parentContentHeight, floats);
                var borderBoxH = h - childMarginTop - childBottomMargin;

                // Self-collapsing block (§8.3.1): no in-flow content / border / padding / height (a
                // zero border-box height already implies height is auto-or-0 with no min-height), and
                // not a BFC → its top and bottom margins are adjoining, fold together into the running
                // margin, and the box contributes no height (so neighbouring margins collapse through).
                if (borderBoxH <= 0.01f && !EstablishesBlockFormattingContext(child))
                {
                    var own = CollapseMargins(childMarginTop, childBottomMargin);
                    pendingMargin = firstChildCollapse ? own : CollapseMargins(pendingMargin, own);
                }
                else
                {
                    // Commit: runY advances to the child's border-box bottom; its (effective, after
                    // its own last-child collapse-through) bottom margin becomes the running margin.
                    runY = borderTop + borderBoxH;
                    pendingMargin = childBottomMargin;
                }
                lastPlacedWasBlock = true;
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
                    if (d == DisplayType.Block || d == DisplayType.ListItem || d == DisplayType.Flex || d == DisplayType.Table) break;
                    if (children[i].TagName != "#text" && children[i].GetFloat() != FloatType.None) break;
                    run.Add(children[i]);
                    i++;
                }
                // Skip runs that are solely whitespace-only #TEXT nodes — these are
                // inter-block whitespace artifacts (e.g. newlines between <div>s).
                if (run.All(n => n.TagName == "#text" && n.DisplayText.Trim().Length == 0))
                    continue;

                // Inline content commits the pending block margin (margins don't collapse across a
                // line box) and lays out at the committed position.
                runY += pendingMargin;
                pendingMargin = 0f;
                var (effX, effW) = AvailableBand(floats, runY, 1, contentX, contentW);
                var runH = LayoutInlineRun(run, effX, runY, effW, viewportWidth, viewportHeight);
                runY += runH;
                lastPlacedWasBlock = false;
            }
        }

        // A block formatting context grows to contain its floats (CSS 2.1 §10.6.7); a non-BFC
        // block does not — its floats belong to an ancestor BFC and overflow this box. The last
        // in-flow block child's bottom margin can collapse through only if no float extends past it.
        var contentBottom = runY + pendingMargin;
        var flowBottom = contentBottom;
        if (ownsFloatContext)
            foreach (var f in floats)
                contentBottom = Math.Max(contentBottom, f.Bottom);

        trailingMargin = (lastPlacedWasBlock && contentBottom <= flowBottom) ? pendingMargin : 0f;
        return (contentBottom - contentY) - trailingMargin;
    }

    // -------------------------------------------------------------------------
    // Inline formatting context
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a run of inline/inline-block nodes within a line box.
    /// Returns total height consumed by all line boxes.
    /// </summary>
    private static float LayoutInlineRun(
        List<LayoutNode> nodes,
        float originX, float originY,
        float maxWidth,
        float viewportWidth, float viewportHeight)
    {
        var items = new List<InlineItem>();
        CollectInlineItems(nodes, items, viewportWidth, viewportHeight);

        if (items.Count == 0) return 0f;

        var lineX = 0f;
        var lineY = 0f;
        var lineHeight = 0f;

        var placed = new List<(InlineItem item, float relX, float relY)>();
        var lineStart = 0;

        void CommitLine()
        {
            for (var k = lineStart; k < placed.Count; k++)
            {
                var (it, rx, _) = placed[k];
                var vAlign = it.Node.GetVerticalAlign();
                float yOffset = vAlign switch
                {
                    VerticalAlignType.Top => 0f,
                    VerticalAlignType.Bottom => lineHeight - it.Height,
                    VerticalAlignType.Middle => (lineHeight - it.Height) / 2f,
                    VerticalAlignType.Sub => lineHeight - it.Height + it.Height * 0.15f,
                    VerticalAlignType.Super => -it.Height * 0.15f,
                    VerticalAlignType.TextTop => 0f,
                    VerticalAlignType.TextBottom => lineHeight - it.Height,
                    _ => lineHeight - it.Height, // baseline: align bottoms
                };
                placed[k] = (it, rx, lineY + yOffset);
            }
            lineY += lineHeight;
            lineHeight = 0f;
            lineX = 0f;
            lineStart = placed.Count;
        }

        foreach (var item in items)
        {
            if (item.Kind == InlineItemKind.LineBreak)
            {
                // Force a new line; ensure minimum height from the <br>'s font
                if (lineHeight == 0f) lineHeight = item.Height;
                CommitLine();
                continue;
            }

            if (lineX > 0 && lineX + item.Width > maxWidth)
                CommitLine();

            // Skip whitespace-only text at the start of a line
            if (lineX == 0 && item.Kind == InlineItemKind.Text &&
                item.Text != null && item.Text.Trim().Length == 0)
                continue;

            // For text items wider than the available space, re-measure with wrapping
            var effectiveItem = item;
            if (item.Kind == InlineItemKind.Text && item.Text != null)
            {
                var availW = maxWidth - lineX;
                if (availW > 0 && item.Width > availW)
                {
                    using var font = TextMeasure.CreateFont(item.Node);
                    var ws = item.Node.GetWhiteSpace();
                    var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
                    var wrapLines = TextMeasure.WrapText(item.Text, Math.Max(availW, 1f), font, ws, lh);
                    var wrappedH = wrapLines.Sum(l => l.Height);
                    effectiveItem = item with { Width = availW, Height = wrappedH, ContentW = availW, ContentH = wrappedH };
                }
            }

            placed.Add((effectiveItem, lineX, lineY));
            lineX += effectiveItem.Width;
            lineHeight = Math.Max(lineHeight, effectiveItem.Height);
        }
        if (placed.Count > lineStart) CommitLine();

        foreach (var (item, relX, relY) in placed)
        {
            ApplyInlineItem(item, originX + relX, originY + relY);
        }

        return lineY;
    }

    /// <summary>
    /// Recursively extracts a flat list of InlineItems from inline nodes.
    /// </summary>
    private static void CollectInlineItems(
        IEnumerable<LayoutNode> nodes,
        List<InlineItem> items,
        float viewportWidth, float viewportHeight)
    {
        foreach (var node in nodes)
        {
            var display = node.GetDisplay();
            if (display == DisplayType.None) continue;

            // Absolute/fixed are out of flow — skip in inline runs
            var nodePos = node.GetPosition();
            if (nodePos == PositionType.Absolute || nodePos == PositionType.Fixed) continue;

            // <br> → forced line break item
            if (node.TagName == "BR")
            {
                using var brFont = TextMeasure.CreateFont(node);
                var brH = brFont.Size * 1.4f;
                items.Add(new InlineItem(InlineItemKind.LineBreak, node, null, 0, brH,
                           default, default, default, 0, brH));
                continue;
            }

            if (display == DisplayType.InlineFlex)
            {
                // §5: inline-flex acts as inline-block in the parent, uses flex layout internally.
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                // Intrinsic width: max-content of flex items (or explicit width)
                var w = explicitW > 0
                    ? explicitW
                    : FlexEngine.MeasureMaxContentMain(node, 0, 0, viewportWidth, viewportHeight);
                w = Math.Max(w, 0);

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
                           margin, padding, border, w, h));
                continue;
            }

            if (display == DisplayType.InlineBlock)
            {
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                node.Attributes.TryGetValue("type", out var iType);
                var inputType = iType?.ToLowerInvariant() ?? "text";
                var isCheckbox = node.TagName == "INPUT" && inputType == "checkbox";
                var isRadio = node.TagName == "INPUT" && inputType == "radio";
                var isRange = node.TagName == "INPUT" && inputType == "range";
                float defaultW, defaultH;
                if (isCheckbox) { defaultW = FormLayout.CheckboxSize; defaultH = FormLayout.CheckboxSize; }
                else if (isRadio) { defaultW = FormLayout.RadioSize; defaultH = FormLayout.RadioSize; }
                else if (isRange) { defaultW = FormLayout.RangeWidth; defaultH = FormLayout.RangeHeight; }
                else if (node.TagName == "BUTTON") { defaultW = 0f; defaultH = FormLayout.TextInputHeight; }
                else if (node.TagName == "TEXTAREA") { defaultW = FormLayout.TextareaWidth; defaultH = FormLayout.TextareaHeight; }
                else if (node.TagName == "SELECT") { defaultW = FormLayout.SelectWidth; defaultH = FormLayout.SelectHeight; }
                else if (node.TagName == "PROGRESS") { defaultW = FormLayout.ProgressWidth; defaultH = FormLayout.ProgressHeight; }
                else if (node.TagName == "METER") { defaultW = FormLayout.MeterWidth; defaultH = FormLayout.MeterHeight; }
                else { defaultW = FormLayout.TextInputWidth; defaultH = FormLayout.TextInputHeight; }

                var w = explicitW > 0 ? explicitW : defaultW;
                var h = explicitH > 0 ? explicitH : defaultH;

                if (node.TagName == "BUTTON" && w <= 0)
                {
                    var btnLabel = node.DisplayText;
                    if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
                    if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";
                    using var btnFont = new SKFont { Size = 13 };
                    w = btnFont.MeasureText(btnLabel) + FormLayout.ButtonPaddingX * 2;
                    h = 13f + FormLayout.ButtonPaddingY * 2;
                }

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                items.Add(new InlineItem(InlineItemKind.InlineBlock, node, null, totalW, totalH,
                           margin, padding, border, w, h));
            }
            else if (node.TagName == "IMG" || (node.TagName == "OBJECT" && node.Image != null))
            {
                var w = node.IntrinsicWidth > 0 ? (float)node.IntrinsicWidth : node.Image?.Width ?? 100f;
                var h = node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 100f;
                items.Add(new InlineItem(InlineItemKind.Image, node, null, w, h,
                           default, default, default, w, h));
            }
            else if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                var lh = node.GetLineHeight(node.GetFontSize());
                var (w, h, _) = TextMeasure.MeasureSingleLine(node.DisplayText, font, lh);
                items.Add(new InlineItem(InlineItemKind.Text, node, node.DisplayText, w, h,
                           default, default, default, w, h));
            }
            else if (node.Children.Count > 0)
            {
                CollectInlineItems(node.Children, items, viewportWidth, viewportHeight);
            }
        }
    }

    private static void ApplyInlineItem(InlineItem item, float absX, float absY)
    {
        var node = item.Node;
        switch (item.Kind)
        {
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
            case InlineItemKind.Image:
            case InlineItemKind.Text:
            case InlineItemKind.LineBreak:
                {
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(absX, absY, absX + item.ContentW, absY + item.ContentH),
                    };
                    break;
                }
        }
    }

    // -------------------------------------------------------------------------
    // Inline item model
    // -------------------------------------------------------------------------

    private enum InlineItemKind { Text, Image, InlineBlock, InlineFlex, LineBreak }

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
        float ContentH
    );
}
