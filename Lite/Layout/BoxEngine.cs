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
        LayoutBlock(root, 0, 0, viewportWidth, viewportWidth, viewportHeight, viewportHeight);
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

    private static void ResolveAbsoluteBox(LayoutNode node, BoxDimensions cb,
        float viewportWidth, float viewportHeight)
    {
        var cbRect   = cb.PaddingBox;
        var fontSize = node.GetFontSize();
        var padding  = node.GetPadding(cbRect.Width, cbRect.Height, fontSize);
        var border   = node.GetBorderWidth();
        var margin   = node.GetMargin(cbRect.Width, cbRect.Height, fontSize);

        var top    = node.GetOffsetTop   (cbRect.Height, fontSize);
        var right  = node.GetOffsetRight (cbRect.Width,  fontSize);
        var bottom = node.GetOffsetBottom(cbRect.Height, fontSize);
        var left   = node.GetOffsetLeft  (cbRect.Width,  fontSize);

        // Resolve width
        var explicitW = node.GetWidth(cbRect.Width);
        float contentW;
        if (explicitW > 0)
            contentW = explicitW;
        else if (!float.IsNaN(left) && !float.IsNaN(right))
            contentW = Math.Max(0, cbRect.Width - left - right - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right);
        else
        {
            // Shrink-wrap to text content; fall back to half container width for block containers
            if (!string.IsNullOrEmpty(node.DisplayText))
            {
                using var shrinkFont = TextMeasure.CreateFont(node);
                contentW = shrinkFont.MeasureText(node.DisplayText);
            }
            else
                contentW = Math.Max(0, cbRect.Width * 0.5f);
        }

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

        // Determine this node's explicit content height so children can resolve % heights
        var explicitHEarly = node.GetHeight(cbRect.Height, 0, viewportHeight);
        float selfContentH;
        if (explicitHEarly > 0)
        {
            var isBBEarly = node.Style.GetPropertyValue("box-sizing") == "border-box";
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
        var contentH  = LayoutChildren(node.Children,
            contentX, contentY0,
            contentW, viewportWidth, viewportHeight, selfContentH);

        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, node.GetWhiteSpace());
            contentH = lines.Sum(l => l.Height);
        }

        var explicitH = node.GetHeight(cbRect.Height, 0, viewportHeight);
        if (explicitH > 0)
        {
            var isBorderBox = node.Style.GetPropertyValue("box-sizing") == "border-box";
            contentH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        }
        else if (!float.IsNaN(top) && !float.IsNaN(bottom))
            contentH = Math.Max(0, cbRect.Height - top - bottom - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom);

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
            Padding    = padding,
            Border     = border,
            Margin     = margin,
        };
    }

    // -------------------------------------------------------------------------
    // Block layout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a block-level node at the given position.
    /// Returns the total margin-box height consumed (for the parent's y-cursor).
    /// </summary>
    private static float LayoutBlock(
        LayoutNode node,
        float x, float y,
        float availableWidth,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0)
    {
        if (node.GetDisplay() == DisplayType.None)
        {
            node.Box = default;
            return 0f;
        }

        var fontSize = node.GetFontSize();
        var margin   = node.GetMargin(availableWidth, viewportHeight, fontSize);
        var padding  = node.GetPadding(availableWidth, viewportHeight, fontSize);
        var border   = node.GetBorderWidth();

        // Explicit width or fill available (pass size=0 so unset width returns 0, not fontSize)
        var explicitW = node.GetWidth(availableWidth);
        var boxWidth  = explicitW > 0 ? explicitW : availableWidth - margin.Left - margin.Right;

        // margin: auto centering — when explicit width is set and one or both horizontal margins are auto
        if (explicitW > 0)
        {
            var leftAuto  = node.IsAutoMarginLeft();
            var rightAuto = node.IsAutoMarginRight();
            if (leftAuto || rightAuto)
            {
                var remaining = availableWidth - explicitW - border.Left - border.Right - padding.Left - padding.Right;
                if (leftAuto && rightAuto) { margin.Left = margin.Right = MathF.Max(0, remaining / 2f); }
                else if (leftAuto)         { margin.Left  = MathF.Max(0, remaining); }
                else                       { margin.Right = MathF.Max(0, remaining); }
            }
        }

        var contentW = Math.Max(0f, boxWidth - border.Left - border.Right - padding.Left - padding.Right);
        var contentX = x + margin.Left + border.Left + padding.Left;
        var contentY = y + margin.Top + border.Top + padding.Top;

        // Resolve this node's explicit height using parentContentHeight for % and viewportHeight for vh/vw
        var isBorderBox    = node.Style.GetPropertyValue("box-sizing") == "border-box";
        var explicitH      = node.GetHeight(parentContentHeight, 0, viewportHeight);
        var knownContentH  = explicitH > 0
            ? (isBorderBox ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom) : explicitH)
            : 0f;

        var nodeDisplay = node.GetDisplay();
        var contentH = (nodeDisplay == DisplayType.Flex || nodeDisplay == DisplayType.InlineFlex)
            ? FlexEngine.LayoutFlex(node, contentX, contentY, contentW, knownContentH, viewportWidth, viewportHeight)
            : nodeDisplay == DisplayType.Table
                ? TableEngine.LayoutTable(node, contentX, contentY, contentW, viewportWidth, viewportHeight)
                : LayoutChildren(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight, knownContentH);

        // Block elements with no children but own text (e.g. <label>, <p>, <h1>):
        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var ws    = node.GetWhiteSpace();
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, ws);
            contentH = lines.Sum(l => l.Height);
        }

        // Explicit height overrides — respect box-sizing: border-box
        if (explicitH > 0)
        {
            contentH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        }

        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(contentX, contentY, contentX + contentW, contentY + contentH),
            Padding    = padding,
            Border     = border,
            Margin     = margin,
        };

        var totalH = margin.Top + border.Top + padding.Top
                   + contentH
                   + padding.Bottom + border.Bottom + margin.Bottom;
        return totalH;
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

    private static float LayoutChildren(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0)
    {
        var cursorY          = contentY;
        var prevMarginBottom = 0f;
        var i                = 0;

        while (i < children.Count)
        {
            var child   = children[i];
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

            if (display == DisplayType.Block || display == DisplayType.ListItem || display == DisplayType.Flex || display == DisplayType.Table)
            {
                var childFontSize   = child.GetFontSize();
                var childMarginTop  = child.GetMarginTop(total: viewportHeight, size: childFontSize);

                // Collapse adjacent vertical margins: use max, not sum
                var collapsed = Math.Max(prevMarginBottom, childMarginTop);
                var adjust    = collapsed - prevMarginBottom - childMarginTop; // ≤ 0

                var h = LayoutBlock(child, contentX, cursorY + adjust, contentW, viewportWidth, viewportHeight, parentContentHeight);
                cursorY += h + adjust;

                prevMarginBottom = child.GetMarginBottom(total: viewportHeight, size: childFontSize);
                i++;
            }
            else
            {
                // Collect consecutive inline / inline-block / BR children into a run
                var run = new List<LayoutNode>();
                while (i < children.Count)
                {
                    var d = children[i].GetDisplay();
                    if (d == DisplayType.Block || d == DisplayType.ListItem || d == DisplayType.Flex || d == DisplayType.Table) break;
                    run.Add(children[i]);
                    i++;
                }
                // Skip runs that are solely whitespace-only #TEXT nodes — these are
                // inter-block whitespace artifacts (e.g. newlines between <div>s).
                if (run.All(n => n.TagName == "#TEXT" && n.DisplayText.Trim().Length == 0))
                    continue;

                var runH = LayoutInlineRun(run, contentX, cursorY, contentW, viewportWidth, viewportHeight);
                cursorY         += runH;
                prevMarginBottom = 0f;
            }
        }

        return cursorY - contentY;
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

        var lineX      = 0f;
        var lineY      = 0f;
        var lineHeight = 0f;

        var placed    = new List<(InlineItem item, float relX, float relY)>();
        var lineStart = 0;

        void CommitLine()
        {
            for (var k = lineStart; k < placed.Count; k++)
            {
                var (it, rx, _) = placed[k];
                placed[k] = (it, rx, lineY);
            }
            lineY     += lineHeight;
            lineHeight = 0f;
            lineX      = 0f;
            lineStart  = placed.Count;
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

            placed.Add((item, lineX, lineY));
            lineX     += item.Width;
            lineHeight = Math.Max(lineHeight, item.Height);
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
                var fontSize  = node.GetFontSize();
                var margin    = node.GetMargin(0, viewportHeight, fontSize);
                var padding   = node.GetPadding(0, viewportHeight, fontSize);
                var border    = node.GetBorderWidth();
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                // Intrinsic width: max-content of flex items (or explicit width)
                var w = explicitW > 0
                    ? explicitW
                    : FlexEngine.MeasureMaxContentMain(node, 0, 0, viewportWidth, viewportHeight);
                w = Math.Max(w, 0);

                // Intrinsic height: lay out children to compute
                var contentX2 = margin.Left + border.Left + padding.Left;
                var contentY2 = margin.Top  + border.Top  + padding.Top;
                var h = explicitH > 0
                    ? explicitH
                    : FlexEngine.LayoutFlex(node, contentX2, contentY2, w, 0, viewportWidth, viewportHeight);
                h = Math.Max(h, 0);

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top  + border.Top  + padding.Top  + h + padding.Bottom + border.Bottom + margin.Bottom;

                items.Add(new InlineItem(InlineItemKind.InlineFlex, node, null, totalW, totalH,
                           margin, padding, border, w, h));
                continue;
            }

            if (display == DisplayType.InlineBlock)
            {
                var fontSize  = node.GetFontSize();
                var margin    = node.GetMargin(0, viewportHeight, fontSize);
                var padding   = node.GetPadding(0, viewportHeight, fontSize);
                var border    = node.GetBorderWidth();
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                var isCheckbox = node.TagName == "INPUT" &&
                                 node.Attributes.TryGetValue("type", out var iType) &&
                                 iType.Equals("checkbox", StringComparison.OrdinalIgnoreCase);
                float defaultW, defaultH;
                if (isCheckbox)                    { defaultW = FormLayout.CheckboxSize;   defaultH = FormLayout.CheckboxSize; }
                else if (node.TagName == "BUTTON") { defaultW = 0f;                        defaultH = FormLayout.TextInputHeight; }
                else                               { defaultW = FormLayout.TextInputWidth; defaultH = FormLayout.TextInputHeight; }

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
            else if (node.TagName == "IMG")
            {
                var w = node.IntrinsicWidth  > 0 ? (float)node.IntrinsicWidth  : node.Image?.Width  ?? 100f;
                var h = node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 100f;
                items.Add(new InlineItem(InlineItemKind.Image, node, null, w, h,
                           default, default, default, w, h));
            }
            else if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                var (w, h, _) = TextMeasure.MeasureSingleLine(node.DisplayText, font);
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
                var contentY = absY + m.Top  + b.Top  + p.Top;
                node.Box = new BoxDimensions
                {
                    ContentBox = new SKRect(contentX, contentY,
                                            contentX + item.ContentW, contentY + item.ContentH),
                    Margin  = m,
                    Padding = p,
                    Border  = b,
                };
                break;
            }
            case InlineItemKind.InlineFlex:
            {
                var m = item.Margin;
                var p = item.Padding;
                var b = item.Border;
                var contentX = absX + m.Left + b.Left + p.Left;
                var contentY = absY + m.Top  + b.Top  + p.Top;
                node.Box = new BoxDimensions
                {
                    ContentBox = new SKRect(contentX, contentY,
                                            contentX + item.ContentW, contentY + item.ContentH),
                    Margin  = m,
                    Padding = p,
                    Border  = b,
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
        LayoutNode     Node,
        string?        Text,
        float          Width,
        float          Height,
        EdgeSizes      Margin,
        EdgeSizes      Padding,
        EdgeSizes      Border,
        float          ContentW,
        float          ContentH
    );
}
