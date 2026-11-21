using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal static partial class BoxEngine
{
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
                // Pass down nearest positioned ancestor as containing block. For an INLINE
                // positioned box the Box field is not its geometry (ApplyInlineItem leaves inline
                // wrappers boxless — their painted geometry is their fragments), so §10.1's
                // inline containing block is computed from those fragments instead.
                var nextCb = child.IsPositioned() ? ContainingBlockOf(child) : containingBox;
                LayoutPositioned(child, nextCb, viewportWidth, viewportHeight);
            }
        }
    }

    /// <summary>The containing block a positioned descendant sees through this box (§10.1). A
    /// block-level positioned box is its own Box; an inline positioned box contributes the
    /// bounding box of its padding boxes across every line it fragments over — collected from the
    /// inline geometry its in-flow subtree recorded during layout.</summary>
    private static BoxDimensions ContainingBlockOf(LayoutNode node)
    {
        if (node.GetDisplay() is not DisplayType.Inline) return node.Box;
        var rect = InlineExtent(node);
        if (rect.Width <= 0f && rect.Height <= 0f) return node.Box;
        var pad = node.GetPadding(0, 0, node.GetFontSize());
        var bord = node.GetBorderWidth();
        return new BoxDimensions
        {
            // ContentBox carries the PADDING-box extent with zero Padding/Border edges so
            // ResolveAbsoluteBox's cb.PaddingBox reads exactly §10.1's bounding box.
            ContentBox = new SKRect(
                rect.Left - pad.Left - bord.Left, rect.Top - pad.Top - bord.Top,
                rect.Right + pad.Right + bord.Right, rect.Bottom + pad.Bottom + bord.Bottom),
        };
    }

    /// <summary>Bounding box of an inline box's line fragments: the node's own fragments plus, in
    /// flow, the fragments/atomic boxes of its inline descendants. Block-level and out-of-flow
    /// descendants are not part of the inline box's extent.</summary>
    private static SKRect InlineExtent(LayoutNode node)
    {
        var has = false;
        var rect = SKRect.Empty;
        void Add(SKRect r)
        {
            if (r.Width <= 0f && r.Height <= 0f) return;
            rect = has ? SKRect.Union(rect, r) : r;
            has = true;
        }
        foreach (var f in node.InlineFragments ?? [])
            Add(f.Rect);
        foreach (var c in node.Children)
        {
            if (c.GetDisplay() == DisplayType.None) continue;
            var pos = c.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed) continue;
            var d = c.GetDisplay();
            if (d is DisplayType.Block or DisplayType.ListItem or DisplayType.Table
                or DisplayType.TableRow or DisplayType.TableRowGroup or DisplayType.TableCell
                or DisplayType.Flex) continue;
            // An atomic inline (inline-block/table/flex or an image) owns a real Box; a plain
            // inline wrapper's extent lives in its fragments.
            if (d is DisplayType.InlineBlock or DisplayType.InlineTable or DisplayType.InlineFlex
                || BoxEngine.IsReplacedImage(c))
                Add(c.Box.ContentBox);
            else
                Add(InlineExtent(c));
        }
        return has ? rect : SKRect.Empty;
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

        // §10.3.7 / §10.6.4: an 'auto' margin resolves to zero unless its axis's constraint
        // equation is over-determined (offsets AND size all given — then the leftover is
        // shared out below). GetMargin's generic 'auto' (centre in the containing block) is
        // the in-flow rule; applying it here put a top:auto abs-pos box 136px off (its auto
        // top margin "centred" it, absolute-non-replaced-height-010).
        if (float.IsNaN(top) || node.IsAutoHeight() || float.IsNaN(bottom))
        {
            if (node.IsAutoMarginTop()) margin.Top = 0f;
            if (node.IsAutoMarginBottom()) margin.Bottom = 0f;
        }
        var explicitWEarly = TryResolveReplacedSize(node, cbRect.Width, cbRect.Height, viewportHeight)?.Width
                             ?? node.GetWidth(cbRect.Width);
        if (float.IsNaN(left) || float.IsNaN(right) || explicitWEarly <= 0)
        {
            if (node.IsAutoMarginLeft()) margin.Left = 0f;
            if (node.IsAutoMarginRight()) margin.Right = 0f;
        }

        // Resolve width. An absolutely positioned replaced box takes its size from the image
        // (§10.3.7's shrink-to-fit and the children-derived height below never see it).
        var replaced = TryResolveReplacedSize(node, cbRect.Width, cbRect.Height, viewportHeight);
        var explicitW = replaced?.Width ?? node.GetWidth(cbRect.Width);
        float contentW;
        if (explicitW > 0)
        {
            // §17.5.2: a HTML TABLE's specified width spans its borders and spacing (border-box) —
            // the same rule the in-flow path applies (fixed-table-layout-002a). A generic
            // display:table box stays content-box.
            contentW = node.TagName == "TABLE"
                ? Math.Max(0f, explicitW - border.Left - border.Right - padding.Left - padding.Right)
                : explicitW;
        }
        else if (!float.IsNaN(left) && !float.IsNaN(right) &&
                 node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable)
            // Only the table WRAPPER takes the solved width (§17.4); the table box inside keeps
            // its auto table width — content-sized, at most what the offsets leave
            // (no_red_3x3_monospace_table-ref: columns stretched across the containing block).
            contentW = IntrinsicSizer.ShrinkToFit(node, Math.Max(0, cbRect.Width - left - right - margin.Left
                - margin.Right - border.Left - border.Right - padding.Left - padding.Right), viewportHeight);
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
            // Both offsets auto: the box starts at its static position, so only what is left of
            // the containing block from there is available to it (§10.3.7 rule 3).
            else if (node.StaticX is { } sx) availW -= Math.Max(0f, sx - cbRect.Left);
            contentW = IntrinsicSizer.ShrinkToFit(node, Math.Max(0f, availW), viewportHeight);
        }

        // Clamp to min/max-width (§10.4) — min wins over max. (Acid2's fixed scalp uses
        // width:140%; max-width:4em to pin the head's top line to a fixed size.)
        contentW = Math.Min(contentW, node.GetMaxWidth(cbRect.Width, fontSize));
        contentW = Math.Max(contentW, node.GetMinWidth(cbRect.Width, fontSize));

        // §10.3.7: when 'left', 'width' and 'right' are all given, an auto margin absorbs the
        // space left over — two of them split it equally, which is how an absolutely positioned
        // box is centred in its containing block. Auto margins used to compute to zero, so such a
        // box sat at its 'left' offset instead.
        var isRtl = node.GetDirection() == "rtl";
        if (!float.IsNaN(left) && !float.IsNaN(right))
        {
            var mlAuto = node.IsAutoMarginLeft();
            var mrAuto = node.IsAutoMarginRight();
            if (mlAuto || mrAuto)
            {
                // Whatever the containing block has left after the offsets, the box and any
                // margin that is NOT auto — that remainder is what the auto margins take.
                var slack = cbRect.Width - left - right - contentW
                            - border.Left - border.Right - padding.Left - padding.Right
                            - (mlAuto ? 0f : margin.Left) - (mrAuto ? 0f : margin.Right);
                if (mlAuto && mrAuto)
                {
                    // A negative remainder is pushed entirely onto the end side (§10.3.7 rule 4).
                    if (slack >= 0f) margin.Left = margin.Right = slack / 2f;
                    else if (isRtl) { margin.Right = 0f; margin.Left = slack; }
                    else { margin.Left = 0f; margin.Right = slack; }
                }
                else if (mlAuto) margin.Left = slack;
                else margin.Right = slack;
            }
        }

        // Resolve X — §10.3.7 rule 1: with 'left' and 'right' both auto the box takes its static
        // position, which under 'direction: rtl' is measured from the RIGHT edge — the hypothetical
        // static box sits at the end of the line, so an RTL containing block puts it flush right.
        float contentX;
        if (!float.IsNaN(left))
            contentX = cbRect.Left + left + margin.Left + border.Left + padding.Left;
        else if (!float.IsNaN(right))
            contentX = cbRect.Right - right - margin.Right - border.Right - padding.Right - contentW;
        else if (isRtl)
            // StaticX records the hypothetical box's band RIGHT edge under rtl (the flow point
            // where the box would have been) — the box hangs its right margin edge there, not at
            // the containing block's edge (the static position lives inside the flow, e.g. inside
            // BODY's margins when the containing block is the initial one).
            contentX = node.StaticX is { } rtlBandRight
                ? rtlBandRight - margin.Right - border.Right - padding.Right - contentW
                : cbRect.Right - margin.Right - border.Right - padding.Right - contentW;
        else if (node.StaticX.HasValue)
            contentX = node.StaticX.Value + margin.Left + border.Left + padding.Left;
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

        // Lay out children to get content height. The formatting context is the box's OWN display,
        // exactly as LayoutBlock dispatches it: an absolutely positioned table or flex container
        // used to fall through to plain block-children layout, which flowed its rows and cells as
        // inline content — an abs-pos <table> came out as one long wrapping line of cell text.
        float LayoutAbsChildren(float originY, float knownH) => node.GetDisplay() switch
        {
            DisplayType.Flex or DisplayType.InlineFlex =>
                FlexEngine.LayoutFlex(node, contentX, originY, contentW, knownH, viewportWidth, viewportHeight),
            DisplayType.Table or DisplayType.InlineTable =>
                TableEngine.LayoutTable(node, contentX, originY, contentW, viewportWidth, viewportHeight),
            _ => LayoutChildren(node.Children, contentX, originY, contentW, viewportWidth, viewportHeight, knownH),
        };

        var contentY0 = cbRect.Top; // temp origin for children layout
        var contentH = LayoutAbsChildren(contentY0, selfContentH);

        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var lh = node.GetLineHeight(node.GetFontSize());
            var lines = TextMeasure.WrapText(node.RenderedText(), Math.Max(contentW, 1f), font, node.GetWhiteSpace(), lh);
            contentH = lines.Sum(l => l.Height);
        }

        var explicitH = autoHeight ? 0f : node.GetHeight(cbRect.Height, 0, viewportHeight);
        if (explicitH > 0 && node.GetDisplay() is not (DisplayType.Table or DisplayType.InlineTable))
        {
            // A table's specified height is a minimum (§17.5.3) applied by the row stretch, and
            // its caption adds on top (§17.4) — the same rule as the in-flow path.
            var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            contentH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        }
        else if (autoHeight && !float.IsNaN(top) && !float.IsNaN(bottom))
            contentH = Math.Max(0, cbRect.Height - top - bottom - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom);
        if (replaced.HasValue) contentH = replaced.Value.Height;

        // Clamp to min/max-height (§10.7) — min wins over max (Acid2's scalp: min-height:1em
        // overrides max-height:2mm).
        contentH = Math.Min(contentH, node.GetMaxHeight(cbRect.Height, fontSize));
        contentH = Math.Max(contentH, node.GetMinHeight(cbRect.Height, fontSize));

        // §10.6.4: the vertical axis works the same way — with 'top', 'height' and 'bottom' all
        // given, auto margins share out what is left, centring the box vertically.
        if (!float.IsNaN(top) && !float.IsNaN(bottom))
        {
            var mtAuto = node.IsAutoMarginTop();
            var mbAuto = node.IsAutoMarginBottom();
            if (mtAuto || mbAuto)
            {
                var slack = cbRect.Height - top - bottom - contentH
                            - border.Top - border.Bottom - padding.Top - padding.Bottom
                            - (mtAuto ? 0f : margin.Top) - (mbAuto ? 0f : margin.Bottom);
                if (mtAuto && mbAuto)
                {
                    if (slack >= 0f) margin.Top = margin.Bottom = slack / 2f;
                    else { margin.Top = 0f; margin.Bottom = slack; }
                }
                else if (mtAuto) margin.Top = slack;
                else margin.Bottom = slack;
            }
        }

        // Resolve Y — §4.1: use StaticY as static position when top/bottom are both auto
        float contentY;
        if (!float.IsNaN(top))
            contentY = cbRect.Top + top + margin.Top + border.Top + padding.Top;
        else if (!float.IsNaN(bottom))
            contentY = cbRect.Bottom - bottom - margin.Bottom - border.Bottom - padding.Bottom - contentH;
        else if (node.StaticY.HasValue)
            contentY = node.StaticY.Value + margin.Top + border.Top + padding.Top;
        else
            contentY = cbRect.Top + margin.Top + border.Top + padding.Top;

        // Re-layout children at the correct absolute Y
        if (contentY != contentY0)
            LayoutAbsChildren(contentY, selfContentH);

        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(contentX, contentY, contentX + contentW, contentY + contentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        // Positioned scroll containers (overflow: scroll | auto | hidden) compute their scroll
        // state after node.Box is set so descendant relative-position percentage offsets resolve
        // against this box's final content dimensions (§9.4.3 / §11.1.1).
        var absOverflow = node.GetOverflow();
        if (absOverflow is OverflowType.Scroll or OverflowType.Auto or OverflowType.Hidden
            && ScrollableExtent(node) is var (absExtentR, absExtentB))
        {
            var absNaturalW = MathF.Max(contentW, absExtentR - contentX);
            var absScrollH = MathF.Max(contentH, absExtentB - contentY);
            var absAlwaysBars = absOverflow == OverflowType.Scroll;
            var absFlowW = absAlwaysBars ? MathF.Max(0f, contentW - ScrollGutter) : contentW;
            var (absPortW, absPortH) = Scrollport(absOverflow, absAlwaysBars, contentW, contentH, absNaturalW, absScrollH, absFlowW);
            if (absScrollH > absPortH || absNaturalW > absPortW)
            {
                var ss = node.ScrollState ?? new ElementScrollState();
                ss.ContentHeight = absScrollH;
                ss.ContainerHeight = absPortH;
                ss.ContentWidth = absNaturalW;
                ss.ContainerWidth = absPortW;
                node.ScrollState = ss;
            }
            else
            {
                node.ScrollState = null;
            }
        }
        else
        {
            node.ScrollState = null;
        }
    }

    /// <summary>CSS 2.1 §11.1.1: "UAs must apply the 'overflow' property set on the root
    /// element to the viewport. When the root element is an HTML 'html' element … and that
    /// element has an HTML 'body' element as a child, user agents must instead apply the
    /// 'overflow' property from the first such child element to the viewport, if the value
    /// on the root element is 'visible'." <paramref name="root"/> is the HTML box or the
    /// document box above it. Reads the DECLARED value: the element it propagates from gets a
    /// used value of 'visible' (┬º11.1.1), which is what <c>GetOverflow</c> returns.</summary>
    internal static OverflowType ViewportOverflow(LayoutNode root)
    {
        var html = root.TagName == "HTML" ? root : root.Children.FirstOrDefault(c => c.TagName == "HTML");
        if (html is null) return root.DeclaredOverflow();
        var overflow = html.DeclaredOverflow();
        if (overflow == OverflowType.Visible && html.Children.FirstOrDefault(c => c.TagName == "BODY") is { } body)
            overflow = body.DeclaredOverflow();
        return overflow;
    }

    /// <summary>A 'position: relative' box's paint-time shift (§9.4.3): 'top' wins over
    /// 'bottom'; percentages resolve against the containing block. When neither 'left' nor
    /// 'right' is 'auto' the position is over-constrained: "If the 'direction' property of the
    /// containing block is 'ltr', the value of 'left' wins … If the 'direction' property of the
    /// containing block is 'rtl', 'right' wins and 'left' is ignored."</summary>
    internal static (float Dx, float Dy) RelativeOffset(LayoutNode node)
    {
        var fontSize = node.GetFontSize();
        // Percentage offsets use the containing block, not the positioned box itself.
        var containingWidth = node.Parent?.Box.ContentBox.Width ?? node.Box.ContentBox.Width;
        var containingHeight = node.Parent?.Box.ContentBox.Height ?? node.Box.ContentBox.Height;
        var t = node.GetOffsetTop(containingHeight, fontSize);
        var l = node.GetOffsetLeft(containingWidth, fontSize);
        var r = node.GetOffsetRight(containingWidth, fontSize);
        var b = node.GetOffsetBottom(containingHeight, fontSize);
        float dx;
        if (!float.IsNaN(l) && !float.IsNaN(r))
            dx = RelativeContainingBlockDirection(node) == "rtl" ? -r : l;
        else
            dx = !float.IsNaN(l) ? l : !float.IsNaN(r) ? -r : 0f;
        var dy = !float.IsNaN(t) ? t : !float.IsNaN(b) ? -b : 0f;
        return (dx, dy);
    }

    /// <summary>The 'direction' of a relatively positioned box's containing block (§10.1 item
    /// 2): the nearest block container ancestor — an inline ancestor's own 'direction' (under
    /// 'unicode-bidi') does not count.</summary>
    private static string RelativeContainingBlockDirection(LayoutNode node)
    {
        for (var a = node.Parent; a is not null; a = a.Parent)
        {
            if (a.TagName.Length > 0 && a.TagName[0] == '#') continue;
            if (a.GetDisplay() is DisplayType.Inline) continue;
            return a.GetDirection();
        }
        return node.GetDirection();
    }

    /// <summary>The far edges of a scroll container's in-flow content (§11.1.1): every
    /// descendant box, shifted by the relative offsets on its way down (§9.4.3: "relative
    /// positioning … may cause boxes to overlap … [and] overflow"). Absolutely positioned
    /// boxes are left out, and a descendant that clips its own overflow contributes only its
    /// own box. Returns null when nothing rendered.</summary>
    internal static (float Right, float Bottom)? ScrollableExtent(LayoutNode box) => ScrollableExtent(box, page: false);

    /// <summary>The page's scrollable extent (§11.1.1 applied to the viewport): the root's
    /// descendants as painted, absolutely positioned ones included (abspos-zero-width-001's
    /// 2000px-tall box makes the page scroll); fixed boxes stay with the viewport, and the
    /// root's and body's 'overflow' belongs to the viewport, so they never clip here.</summary>
    internal static (float Right, float Bottom) PageExtent(LayoutNode root)
    {
        var extent = ScrollableExtent(root, page: true);
        return (MathF.Max(root.Box.MarginBox.Right, extent?.Right ?? 0f), MathF.Max(root.Box.MarginBox.Bottom, extent?.Bottom ?? 0f));
    }

    private static (float Right, float Bottom)? ScrollableExtent(LayoutNode box, bool page)
    {
        float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        // A box's own text line can be wider than the box (an unbreakable word, max-height-109).
        void OwnText(LayoutNode n, float dx)
        {
            if (n.OwnTextWidth > 0f) right = MathF.Max(right, n.Box.ContentBox.Left + n.OwnTextWidth + dx);
        }
        OwnText(box, 0f);
        void Walk(LayoutNode n, float dx, float dy)
        {
            foreach (var c in n.Children)
            {
                var pos = c.GetPosition();
                if (pos is PositionType.Fixed || pos is PositionType.Absolute && !page || c.GetDisplay() == DisplayType.None) continue;
                var (rx, ry) = pos == PositionType.Relative ? RelativeOffset(c) : (0f, 0f);
                var (ox, oy) = (dx + rx, dy + ry);
                // The page's scrollable area reaches the border boxes it paints: a negative margin
                // must not pull a box that overflows the viewport back inside (c5502-mrgn-r-002).
                var mb = page ? c.Box.BorderBox : c.Box.MarginBox;
                if (mb.Width > 0f || mb.Height > 0f)
                {
                    right = MathF.Max(right, mb.Right + ox);
                    bottom = MathF.Max(bottom, mb.Bottom + oy);
                }
                if (c.GetOverflow() != OverflowType.Visible && !(page && c.TagName is "HTML" or "BODY")) continue;
                OwnText(c, ox);
                Walk(c, ox, oy);
            }
        }
        Walk(box, 0f, 0f);
        return float.IsNegativeInfinity(right) ? null : (right, bottom);
    }

    /// <summary>The scrollbars a box shows (§11.1.1): 'scroll' always both; 'auto' the bar of
    /// each axis that overflows. The root's and body's 'overflow' belongs to the viewport.</summary>
    internal static (bool Vertical, bool Horizontal) ShownScrollbars(LayoutNode node)
    {
        if (node.TagName is "HTML" or "BODY" || node.TagName.StartsWith('#')) return (false, false);
        return node.GetOverflow() switch
        {
            OverflowType.Scroll => (true, true),
            OverflowType.Auto when node.ScrollState is { } ss => (ss.NeedsScrollbar, ss.NeedsHorizontalScrollbar),
            _ => (false, false),
        };
    }

    /// <summary>The box's scrollport: its padding box less the gutter of each shown bar — the
    /// area its content is clipped to and scrolls within (the bars own their gutters).</summary>
    internal static SKRect ScrollportRect(LayoutNode node)
    {
        var pb = node.Box.PaddingBox;
        var (vertical, horizontal) = ShownScrollbars(node);
        return new SKRect(pb.Left, pb.Top,
            MathF.Max(pb.Left, pb.Right - (vertical ? ScrollGutter : 0f)),
            MathF.Max(pb.Top, pb.Bottom - (horizontal ? ScrollGutter : 0f)));
    }

    /// <summary>The inline space one scrollbar takes from its box: the bar plus its margin.</summary>
    internal const float ScrollGutter = ElementScrollState.BarWidth + ElementScrollState.BarMargin;

    /// <summary>The scrollport of a box with a fixed content area (§11.1.1): each axis loses
    /// the gutter of the bar drawn across it — the vertical bar takes width, the horizontal
    /// bar takes height. 'scroll' always draws both (its flow content already laid out beside
    /// the vertical gutter). 'auto' lays its content out before knowing whether a bar
    /// appears: a genuine horizontal overflow takes its gutter from the height (and may make
    /// the height overflow in turn), and the vertical bar takes its gutter from the width only
    /// when the content scrolls sideways anyway — content that exactly fills the box keeps the
    /// vertical bar as an overlay (taking its width would invent a horizontal overflow there;
    /// browsers re-lay such content out narrower instead).</summary>
    internal static (float Width, float Height) Scrollport(OverflowType overflow, bool alwaysScrollbars,
        float contentW, float contentH, float naturalW, float naturalH, float flowW)
    {
        if (alwaysScrollbars)
            return (Math.Max(0f, contentW - ScrollGutter), Math.Max(0f, contentH - ScrollGutter));
        if (overflow != OverflowType.Auto)
            return (contentW, contentH);
        // 'auto' content already laid out beside the vertical gutter (LayoutBlockCore's second
        // pass) has a vertical bar; its width overflows only past that narrower line.
        var besideBar = flowW < contentW;
        var horizontal = naturalW > (besideBar ? flowW : contentW);
        var vertical = besideBar || naturalH > contentH - (horizontal ? ScrollGutter : 0f);
        return (Math.Max(0f, contentW - (vertical && (besideBar || horizontal) ? ScrollGutter : 0f)),
                Math.Max(0f, contentH - (horizontal ? ScrollGutter : 0f)));
    }
}
