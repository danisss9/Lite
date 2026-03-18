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
        LayoutBlock(root, 0, 0, viewportWidth, viewportWidth, viewportHeight);
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
        float viewportWidth, float viewportHeight)
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

        // Layout children and compute content height
        var contentH = LayoutChildren(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight);

        // Block elements with no children but own text (e.g. <label>, <p>, <h1>):
        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var ws    = node.GetWhiteSpace();
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, ws);
            contentH = lines.Sum(l => l.Height);
        }

        // Explicit height overrides
        var explicitH = node.GetHeight(viewportHeight);
        if (explicitH > 0) contentH = explicitH;

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
    private static float LayoutChildren(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight)
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

            if (display == DisplayType.Block || display == DisplayType.ListItem)
            {
                var childFontSize   = child.GetFontSize();
                var childMarginTop  = child.GetMarginTop(total: viewportHeight, size: childFontSize);

                // Collapse adjacent vertical margins: use max, not sum
                var collapsed = Math.Max(prevMarginBottom, childMarginTop);
                var adjust    = collapsed - prevMarginBottom - childMarginTop; // ≤ 0

                var h = LayoutBlock(child, contentX, cursorY + adjust, contentW, viewportWidth, viewportHeight);
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
                    if (d == DisplayType.Block || d == DisplayType.ListItem) break;
                    run.Add(children[i]);
                    i++;
                }
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

            // <br> → forced line break item
            if (node.TagName == "BR")
            {
                using var brFont = TextMeasure.CreateFont(node);
                var brH = brFont.Size * 1.4f;
                items.Add(new InlineItem(InlineItemKind.LineBreak, node, null, 0, brH,
                           default, default, default, 0, brH));
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

    private enum InlineItemKind { Text, Image, InlineBlock, LineBreak }

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
