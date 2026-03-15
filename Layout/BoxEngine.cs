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

        var contentW = Math.Max(0f, boxWidth - border.Left - border.Right - padding.Left - padding.Right);
        var contentX = x + margin.Left + border.Left + padding.Left;
        var contentY = y + margin.Top + border.Top + padding.Top;

        // Layout children and compute content height
        var contentH = LayoutChildren(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight);

        // Block elements with no children but own text (e.g. <label>, <p>, <h1>):
        // compute height from wrapped text so the box isn't zero-sized
        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font);
            contentH = lines.Sum(l => l.Height);
        }

        // Explicit height overrides (pass size=0 so unset height returns 0)
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
    /// Returns total content height consumed.
    /// </summary>
    private static float LayoutChildren(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight)
    {
        var cursorY = contentY;
        var i       = 0;

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

            if (display == DisplayType.Block)
            {
                var h = LayoutBlock(child, contentX, cursorY, contentW, viewportWidth, viewportHeight);
                cursorY += h;
                i++;
            }
            else
            {
                // Collect consecutive inline / inline-block children
                var run = new List<LayoutNode>();
                while (i < children.Count)
                {
                    var d = children[i].GetDisplay();
                    if (d == DisplayType.Block) break;
                    run.Add(children[i]);
                    i++;
                }
                var runH = LayoutInlineRun(run, contentX, cursorY, contentW, viewportWidth, viewportHeight);
                cursorY += runH;
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
        // We build a flat list of inline items from all nodes in the run
        var items = new List<InlineItem>();
        CollectInlineItems(nodes, items, viewportWidth, viewportHeight);

        if (items.Count == 0) return 0f;

        // Place items into line boxes
        var lineX      = 0f;
        var lineY      = 0f;
        var lineHeight = 0f;

        // First pass: assign positions relative to (originX, originY)
        var placed = new List<(InlineItem item, float relX, float relY)>();
        var lineStart = 0;

        void CommitLine()
        {
            // Go back and assign final Y positions for this line's items
            for (var k = lineStart; k < placed.Count; k++)
            {
                var (it, rx, _) = placed[k];
                placed[k] = (it, rx, lineY);
            }
            lineY += lineHeight;
            lineHeight = 0f;
            lineX      = 0f;
            lineStart  = placed.Count;
        }

        foreach (var item in items)
        {
            // Wrap if item doesn't fit (unless it's the only item on the line)
            if (lineX > 0 && lineX + item.Width > maxWidth)
                CommitLine();

            placed.Add((item, lineX, lineY));
            lineX      += item.Width;
            lineHeight  = Math.Max(lineHeight, item.Height);
        }
        if (placed.Count > lineStart) CommitLine();

        // Apply absolute positions to nodes
        foreach (var (item, relX, relY) in placed)
        {
            var absX = originX + relX;
            var absY = originY + relY;
            ApplyInlineItem(item, absX, absY);
        }

        return lineY; // total height of all lines
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

            if (display == DisplayType.InlineBlock)
            {
                // Inline-block: treat as atomic inline item but with block-model size
                var fontSize  = node.GetFontSize();
                var margin    = node.GetMargin(0, viewportHeight, fontSize);
                var padding   = node.GetPadding(0, viewportHeight, fontSize);
                var border    = node.GetBorderWidth();
                // size=0 default so unset returns 0, not fontSize
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                // Use form defaults when CSS doesn't specify
                var isCheckbox = node.TagName == "INPUT" &&
                                 node.Attributes.TryGetValue("type", out var iType) &&
                                 iType.Equals("checkbox", StringComparison.OrdinalIgnoreCase);
                float defaultW, defaultH;
                if (isCheckbox)       { defaultW = FormLayout.CheckboxSize;    defaultH = FormLayout.CheckboxSize; }
                else if (node.TagName == "BUTTON") { defaultW = 0f;            defaultH = FormLayout.TextInputHeight; }
                else                  { defaultW = FormLayout.TextInputWidth;  defaultH = FormLayout.TextInputHeight; }

                var w = explicitW > 0 ? explicitW : defaultW;
                var h = explicitH > 0 ? explicitH : defaultH;

                // For BUTTON, compute width from label text
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
                // Leaf inline node with text (e.g. <a>, <span>, <label>)
                using var font = TextMeasure.CreateFont(node);
                // We add text as a single item with the total measured width; wrapping is handled by line box
                var (w, h, _) = TextMeasure.MeasureSingleLine(node.DisplayText, font);
                items.Add(new InlineItem(InlineItemKind.Text, node, node.DisplayText, w, h,
                           default, default, default, w, h));
            }
            else if (node.Children.Count > 0)
            {
                // Inline container (e.g. <a> wrapping <span>) — recurse
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

    private enum InlineItemKind { Text, Image, InlineBlock }

    private record InlineItem(
        InlineItemKind Kind,
        LayoutNode     Node,
        string?        Text,
        float          Width,    // total margin-box width used for line placement
        float          Height,   // total margin-box height used for line height
        EdgeSizes      Margin,
        EdgeSizes      Padding,
        EdgeSizes      Border,
        float          ContentW, // inner content width
        float          ContentH  // inner content height
    );
}
