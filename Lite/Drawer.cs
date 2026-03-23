using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Rendering;
using SkiaSharp;

namespace Lite;

internal static class Drawer
{
    private static List<HitRegion> _hitRegions = [];
    /// <summary>Maps HitRegion index → select option index for dropdown options.</summary>
    internal static Dictionary<int, int> SelectOptionMap { get; } = [];
    /// <summary>Deferred dropdown to draw on top of everything.</summary>
    private static (LayoutNode node, SKRect rect, string selectedVal)? _pendingDropdown;

    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, LayoutNode root, Viewport viewport)
    {
        // Layout pass — compute node.Box for every node
        BoxEngine.Layout(root, width, height);

        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap    = new SKBitmap(imageInfo);
        var canvas    = new SKCanvas(bitmap);

        // Propagate body background to the viewport canvas (matches browser behaviour)
        var body      = root.Children.FirstOrDefault(c => c.TagName == "BODY") ?? root;
        var clearColor = body.GetBackgroundColor();
        if (clearColor == SKColors.Transparent) clearColor = new SKColor(240, 240, 242);
        canvas.Clear(clearColor);
        _hitRegions = [];
        SelectOptionMap.Clear();
        _pendingDropdown = null;

        canvas.Save();
        canvas.Translate(0, -viewport.ScrollY);

        PaintNode(canvas, root, width);

        // Draw deferred dropdown overlay on top of all content (but within scroll context)
        if (_pendingDropdown is var (ddNode, ddRect, ddSelected))
            DrawDropdownOverlay(canvas, ddNode, ddRect, ddSelected);

        canvas.Restore();

        // Paint position:fixed nodes after restoring scroll so they stay on screen
        PaintFixedNodes(canvas, root, width);

        viewport.ContentHeight = root.Box.MarginBox.Bottom;
        DrawScrollbar(canvas, viewport, width, height);

        return (bitmap.GetPixels(), _hitRegions);
    }

    // -------------------------------------------------------------------------
    // Scrollbar
    // -------------------------------------------------------------------------

    private static void DrawScrollbar(SKCanvas canvas, Viewport viewport, int width, int height)
    {
        if (viewport.ContentHeight <= viewport.ViewportHeight) return;

        const float barWidth = 6f;
        const float margin   = 2f;
        var ratio    = viewport.ViewportHeight / viewport.ContentHeight;
        var trackH   = viewport.ViewportHeight;
        var thumbH   = Math.Max(trackH * ratio, 24f);
        var thumbTop = viewport.ScrollY / viewport.ContentHeight * trackH;
        var x        = width - barWidth - margin;

        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true };
        canvas.DrawRoundRect(x, thumbTop + margin, barWidth, thumbH - margin * 2, 3, 3, paint);
    }

    // -------------------------------------------------------------------------
    // Paint tree
    // -------------------------------------------------------------------------

    private static void PaintFixedNodes(SKCanvas canvas, LayoutNode root, int viewportWidth)
    {
        var stack = new Stack<LayoutNode>();
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        for (int i = root.Children.Count - 1; i >= 0; i--)
            stack.Push(root.Children[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.GetPosition() == PositionType.Fixed)
                PaintNode(canvas, node, viewportWidth);
            else
                for (int i = node.Children.Count - 1; i >= 0; i--)
                    stack.Push(node.Children[i]);
        }
    }

    private static void PaintNode(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display = node.GetDisplay();
        if (display == DisplayType.None) return;

        // Skip fixed nodes in the normal tree pass — painted separately after scroll restore
        if (node.GetPosition() == PositionType.Fixed) return;

        // position:relative — translate by offset, paint normally, then restore
        var pos = node.GetPosition();
        if (pos == PositionType.Relative)
        {
            var fontSize = node.GetFontSize();
            var t = node.GetOffsetTop   (node.Box.ContentBox.Height, fontSize);
            var l = node.GetOffsetLeft  (node.Box.ContentBox.Width,  fontSize);
            var r = node.GetOffsetRight (node.Box.ContentBox.Width,  fontSize);
            var b = node.GetOffsetBottom(node.Box.ContentBox.Height, fontSize);
            var dx = !float.IsNaN(l) ? l : !float.IsNaN(r) ? -r : 0f;
            var dy = !float.IsNaN(t) ? t : !float.IsNaN(b) ? -b : 0f;
            if (dx != 0f || dy != 0f)
            {
                canvas.Save();
                canvas.Translate(dx, dy);
                PaintNodeInner(canvas, node, viewportWidth);
                canvas.Restore();
                return;
            }
        }

        PaintNodeInner(canvas, node, viewportWidth);
    }

    private static void PaintNodeInner(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var opacity  = node.GetOpacity();
        var overflow = node.GetOverflow();
        var clip     = overflow == OverflowType.Hidden || overflow == OverflowType.Scroll || overflow == OverflowType.Auto;

        var (clipRx, clipRy) = clip
            ? node.GetBorderRadius(node.Box.PaddingBox.Width, node.Box.PaddingBox.Height)
            : (0f, 0f);

        // opacity < 1 — composite the subtree into a temporary layer at reduced alpha
        if (opacity < 1f)
        {
            using var alphaPaint = new SKPaint { Color = SKColors.Transparent.WithAlpha((byte)(opacity * 255)) };
            canvas.SaveLayer(alphaPaint);
        }
        else if (clip)
        {
            canvas.Save();
        }

        if (clip)
        {
            if (clipRx > 0 || clipRy > 0)
            {
                var rr = new SKRoundRect(node.Box.PaddingBox, clipRx, clipRy);
                canvas.ClipRoundRect(rr, antialias: true);
            }
            else
            {
                canvas.ClipRect(node.Box.PaddingBox);
            }
        }

        PaintNodeContent(canvas, node, viewportWidth);

        if (opacity < 1f || clip) canvas.Restore();
    }

    private static void PaintNodeContent(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display = node.GetDisplay();

        switch (node.TagName)
        {
            case { } h when h.StartsWith('H') && h.Length == 2 && char.IsDigit(h[1]):
            case "P":
            case "LABEL":
                PaintTextBlock(canvas, node, viewportWidth);
                PaintChildrenSorted(canvas, node, viewportWidth);
                return;

            case "A":
                PaintAnchor(canvas, node, viewportWidth);
                return;

            case "IMG":
                PaintImage(canvas, node);
                return;

            case "INPUT":
                PaintInput(canvas, node);
                return;

            case "BUTTON":
                PaintButton(canvas, node);
                return;

            case "TEXTAREA":
                PaintTextarea(canvas, node);
                return;

            case "SELECT":
                PaintSelect(canvas, node);
                return;

            case "HR":
                PaintHorizontalRule(canvas, node);
                return;

            case "SVG":
                SvgRenderer.Render(canvas, node);
                return;

            case "CANVAS":
                CanvasRenderer.Render(canvas, node);
                return;
        }

        if (display == DisplayType.ListItem)
        {
            PaintListItem(canvas, node, viewportWidth);
            return;
        }

        if (display == DisplayType.Block || display == DisplayType.Flex || display == DisplayType.InlineFlex
            || display == DisplayType.Table || display == DisplayType.TableRow || display == DisplayType.TableCell)
        {
            PaintBlock(canvas, node, viewportWidth);
            return;
        }

        // Generic inline: if this node carries text (e.g. <strong>, <em>, <mark>, <code>, #TEXT)
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var bgColor = node.GetBackgroundColor();
            if (bgColor != SKColors.Transparent)
            {
                using var bgPaint = new SKPaint { Color = bgColor };
                canvas.DrawRect(node.Box.ContentBox, bgPaint);
            }
            using var font  = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText,
                node.Box.ContentBox.Left, node.Box.ContentBox.Top,
                node.Box.ContentBox.Width, font, paint);
            return;
        }

        // Inline container with children (e.g. <span>, <a> wrappers) — recurse
        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    // -------------------------------------------------------------------------
    // Block containers
    // -------------------------------------------------------------------------

    private static void PaintBlock(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        // Background
        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else                   canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        // Background image
        DrawBackgroundImage(canvas, node, box);

        DrawBorders(canvas, box, node);
        DrawOutline(canvas, box, node);

        var cursor = node.GetCursor();
        _hitRegions.Add(new HitRegion(box.BorderBox, cursor, NodeKey: node.NodeKey));

        // Draw own text content (block elements like <div>Text</div> with no child nodes)
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            using var font  = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

            var textX = box.ContentBox.Left;
            var textY = box.ContentBox.Top;

            // Flex containers with direct text: apply justify-content / align-items
            var nodeDisplay = node.GetDisplay();
            var isFlex = nodeDisplay == DisplayType.Flex || nodeDisplay == DisplayType.InlineFlex;
            var textMaxW = box.ContentBox.Width;
            if (isFlex)
            {
                var ws    = node.GetWhiteSpace();
                var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(box.ContentBox.Width, 1f), font, ws);
                var textH = lines.Sum(l => l.Height);
                var textW = lines.Count > 0 ? lines.Max(l => l.Width) : 0f;

                var dir   = node.GetFlexDirection();
                var isRow = dir == FlexDirection.Row || dir == FlexDirection.RowReverse;

                if (isRow)
                {
                    // justify-content controls horizontal, align-items controls vertical
                    textX += node.GetJustifyContent() switch
                    {
                        JustifyContent.Center       => (box.ContentBox.Width - textW) / 2f,
                        JustifyContent.FlexEnd      => box.ContentBox.Width - textW,
                        JustifyContent.SpaceAround  => (box.ContentBox.Width - textW) / 2f,
                        JustifyContent.SpaceEvenly   => (box.ContentBox.Width - textW) / 2f,
                        _ => 0f,
                    };
                    textY += node.GetAlignItems() switch
                    {
                        AlignItems.Center   => (box.ContentBox.Height - textH) / 2f,
                        AlignItems.FlexEnd  => box.ContentBox.Height - textH,
                        _ => 0f,
                    };
                }
                else
                {
                    // Column: justify-content controls vertical, align-items controls horizontal
                    textY += node.GetJustifyContent() switch
                    {
                        JustifyContent.Center       => (box.ContentBox.Height - textH) / 2f,
                        JustifyContent.FlexEnd      => box.ContentBox.Height - textH,
                        JustifyContent.SpaceAround  => (box.ContentBox.Height - textH) / 2f,
                        JustifyContent.SpaceEvenly   => (box.ContentBox.Height - textH) / 2f,
                        _ => 0f,
                    };
                    textX += node.GetAlignItems() switch
                    {
                        AlignItems.Center   => (box.ContentBox.Width - textW) / 2f,
                        AlignItems.FlexEnd  => box.ContentBox.Width - textW,
                        _ => 0f,
                    };
                }
                // Use measured text width so DrawWrappedText doesn't add its own alignment offset
                textMaxW = textW;
            }

            DrawWrappedText(canvas, node, node.DisplayText,
                            textX, textY, textMaxW, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    /// <summary>
    /// Only absolute/fixed nodes (or relative with an explicit z-index) participate in
    /// z-index stacking. position:relative without z-index paints in normal document order.
    /// </summary>
    private static bool NeedsZSort(LayoutNode c)
    {
        var p = c.GetPosition();
        if (p == PositionType.Absolute || p == PositionType.Fixed) return true;
        if (p == PositionType.Relative)
        {
            // Only create stacking context when z-index is explicitly set (not "auto"/unset)
            var raw = c.Style.GetPropertyValue(AngleSharp.Css.PropertyNames.ZIndex);
            return !string.IsNullOrEmpty(raw) && raw != "auto" && int.TryParse(raw, out _);
        }
        return false;
    }

    /// <summary>Paints children sorted by z-index (negative first, then 0+).</summary>
    private static void PaintChildrenSorted(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display  = node.GetDisplay();
        var isFlex   = display == DisplayType.Flex || display == DisplayType.InlineFlex;
        var children = node.Children;

        // §5.4: Flex containers paint children in order-modified document order.
        // Within each order bucket: negative z-index first, then normal, then non-negative stacked.
        IEnumerable<LayoutNode> orderedChildren = isFlex
            ? children.OrderBy(c => c.GetOrder())
            : (IEnumerable<LayoutNode>)children;

        // Fast path — no z-sorted children
        if (!orderedChildren.Any(NeedsZSort))
        {
            foreach (var child in orderedChildren)
                PaintNode(canvas, child, viewportWidth);
            return;
        }

        var childList = orderedChildren.ToList();
        // Negative z-index first, then normal flow (incl. position:relative without z-index), then non-negative stacked
        var negZ   = childList.Where(c => NeedsZSort(c) && c.GetZIndex() < 0).OrderBy(c => c.GetZIndex()).ToList();
        var normal = childList.Where(c => !NeedsZSort(c)).ToList();
        var posZ   = childList.Where(c => NeedsZSort(c) && c.GetZIndex() >= 0).OrderBy(c => c.GetZIndex()).ToList();

        foreach (var c in negZ)   PaintNode(canvas, c, viewportWidth);
        foreach (var c in normal) PaintNode(canvas, c, viewportWidth);
        foreach (var c in posZ)   PaintNode(canvas, c, viewportWidth);
    }

    // -------------------------------------------------------------------------
    // Text elements (H1-H6, P, LABEL)
    // -------------------------------------------------------------------------

    private static void PaintTextBlock(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else                   canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        DrawBorders(canvas, box, node);
        DrawOutline(canvas, box, node);

        var cursor = node.GetCursor();
        _hitRegions.Add(new HitRegion(box.MarginBox, cursor, node.Href, NodeKey: node.NodeKey));

        if (string.IsNullOrEmpty(node.DisplayText)) return;

        using var font  = TextMeasure.CreateFont(node);
        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

        DrawWrappedText(canvas, node, node.DisplayText,
                        box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Width, font, paint);
    }

    // -------------------------------------------------------------------------
    // Anchor
    // -------------------------------------------------------------------------

    private static void PaintAnchor(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var cursor = node.GetCursor();
        _hitRegions.Add(new HitRegion(node.Box.BorderBox, cursor, node.Href, NodeKey: node.NodeKey));

        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var box     = node.Box;
            using var font  = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText, box.ContentBox.Left, box.ContentBox.Top,
                            box.ContentBox.Width, font, paint);
            return;
        }

        // Anchor wraps child nodes (e.g. #TEXT created by parser for mixed content)
        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    // -------------------------------------------------------------------------
    // Image
    // -------------------------------------------------------------------------

    private static void PaintImage(SKCanvas canvas, LayoutNode node)
    {
        var destRect = node.Box.ContentBox;

        if (node.Image != null)
        {
            canvas.DrawBitmap(node.Image, destRect);
        }
        else
        {
            using var borderPaint = new SKPaint { Color = SKColors.Gray, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRect(destRect, borderPaint);

            if (!string.IsNullOrEmpty(node.Alt))
            {
                using var altPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
                using var altFont  = new SKFont { Size = 12 };
                canvas.DrawText(node.Alt, destRect.Left + 4, destRect.Top + 14, SKTextAlign.Left, altFont, altPaint);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    private static void PaintInput(SKCanvas canvas, LayoutNode node)
    {
        var rect      = node.Box.ContentBox;
        var inputType = node.Attributes.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "text";

        switch (inputType)
        {
            case "checkbox":
                PaintCheckbox(canvas, node, rect);
                return;
            case "radio":
                PaintRadio(canvas, node, rect);
                return;
            case "range":
                PaintRange(canvas, node, rect);
                return;
        }

        // Text-like inputs: text, password, number, email, etc.
        var isFocused = FormState.FocusedInput == node.NodeKey;
        var isPassword = inputType == "password";

        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color       = isFocused ? new SKColor(0, 120, 215) : new SKColor(150, 150, 150),
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = isFocused ? 2f : 1f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, borderPaint);

        node.Attributes.TryGetValue("value", out var defaultVal);
        var text = FormState.GetTextValue(node.NodeKey, defaultVal);
        var displayText = isPassword && !string.IsNullOrEmpty(text)
            ? new string('\u2022', text.Length)
            : text;

        if (string.IsNullOrEmpty(text) && node.Attributes.TryGetValue("placeholder", out var ph))
        {
            using var phPaint = new SKPaint { Color = new SKColor(170, 170, 170), IsAntialias = true };
            using var phFont  = new SKFont { Size = 12 };
            canvas.DrawText(ph, rect.Left + 4, rect.Top + 14, SKTextAlign.Left, phFont, phPaint);
        }
        else if (!string.IsNullOrEmpty(displayText))
        {
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var textFont  = new SKFont { Size = 12 };
            canvas.DrawText(displayText, rect.Left + 4, rect.Top + 14, SKTextAlign.Left, textFont, textPaint);
        }

        // Number input: draw up/down arrows with separate hit regions
        if (inputType == "number")
        {
            var arrowW = 16f;
            var arrowX = rect.Right - arrowW;

            // Divider line
            using var divPaint = new SKPaint { Color = new SKColor(200, 200, 200), StrokeWidth = 1 };
            canvas.DrawLine(arrowX, rect.Top, arrowX, rect.Bottom, divPaint);
            canvas.DrawLine(arrowX, rect.MidY, rect.Right, rect.MidY, divPaint);

            using var arrowPaint = new SKPaint { Color = new SKColor(100, 100, 100), IsAntialias = true, Style = SKPaintStyle.Fill };
            // Up arrow
            var upPath = new SKPath();
            upPath.MoveTo(arrowX + 4, rect.MidY - 2);
            upPath.LineTo(arrowX + arrowW - 4, rect.MidY - 2);
            upPath.LineTo(arrowX + arrowW / 2f, rect.Top + 3);
            upPath.Close();
            canvas.DrawPath(upPath, arrowPaint);
            // Down arrow
            var downPath = new SKPath();
            downPath.MoveTo(arrowX + 4, rect.MidY + 2);
            downPath.LineTo(arrowX + arrowW - 4, rect.MidY + 2);
            downPath.LineTo(arrowX + arrowW / 2f, rect.Bottom - 3);
            downPath.Close();
            canvas.DrawPath(downPath, arrowPaint);

            // Hit regions: up arrow, down arrow (added before the text input region so they take priority)
            var upRect = new SKRect(arrowX, rect.Top, rect.Right, rect.MidY);
            var downRect = new SKRect(arrowX, rect.MidY, rect.Right, rect.Bottom);
            _hitRegions.Add(new HitRegion(upRect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.NumberUp));
            _hitRegions.Add(new HitRegion(downRect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.NumberDown));
        }

        if (isFocused)
        {
            using var caretFont  = new SKFont { Size = 12 };
            var caretX = rect.Left + 4 + caretFont.MeasureText(displayText ?? "");
            using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
            canvas.DrawLine(caretX, rect.Top + 3, caretX, rect.Bottom - 3, caretPaint);
        }

        var textHitRect = inputType == "number"
            ? new SKRect(rect.Left, rect.Top, rect.Right - 16f, rect.Bottom)
            : rect;
        _hitRegions.Add(new HitRegion(textHitRect, CursorType.Text, NodeKey: node.NodeKey, InputAction: InputAction.TextInput));
    }

    private static void PaintCheckbox(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint { Color = new SKColor(150, 150, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(rect, borderPaint);

        var defaultChecked = node.Attributes.ContainsKey("checked");
        if (FormState.IsChecked(node.NodeKey, defaultChecked))
        {
            using var checkPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(rect.Left + 2, rect.MidY, rect.MidX - 1, rect.Bottom - 3, checkPaint);
            canvas.DrawLine(rect.MidX - 1, rect.Bottom - 3, rect.Right - 2, rect.Top + 3, checkPaint);
        }

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Checkbox));
    }

    private static void PaintRadio(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        // Register in radio group
        var groupName = node.Attributes.GetValueOrDefault("name", "default");
        FormState.RegisterRadio(node.NodeKey, groupName);

        var cx = rect.MidX;
        var cy = rect.MidY;
        var radius = Math.Min(rect.Width, rect.Height) / 2f;

        using var bgPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, bgPaint);

        using var borderPaint = new SKPaint { Color = new SKColor(150, 150, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, borderPaint);

        var defaultChecked = node.Attributes.ContainsKey("checked");
        if (FormState.IsChecked(node.NodeKey, defaultChecked))
        {
            using var dotPaint = new SKPaint { Color = new SKColor(0, 120, 215), IsAntialias = true };
            canvas.DrawCircle(cx, cy, radius * 0.45f, dotPaint);
        }

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Radio));
    }

    private static void PaintRange(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        // Track
        var trackY = rect.MidY;
        var trackH = 4f;
        using var trackPaint = new SKPaint { Color = new SKColor(200, 200, 200), IsAntialias = true };
        canvas.DrawRoundRect(rect.Left, trackY - trackH / 2, rect.Width, trackH, 2, 2, trackPaint);

        // Thumb position based on value (use FormState if available, else attribute)
        node.Attributes.TryGetValue("min", out var minStr);
        node.Attributes.TryGetValue("max", out var maxStr);
        node.Attributes.TryGetValue("value", out var valStr);
        float.TryParse(minStr ?? "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min);
        float.TryParse(maxStr ?? "100", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max);

        var currentVal = FormState.GetTextValue(node.NodeKey, valStr ?? "50");
        float.TryParse(currentVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val);
        val = Math.Clamp(val, min, max);
        var ratio = max > min ? (val - min) / (max - min) : 0.5f;
        var thumbX = rect.Left + ratio * rect.Width;

        // Filled portion
        using var fillPaint = new SKPaint { Color = new SKColor(0, 120, 215), IsAntialias = true };
        canvas.DrawRoundRect(rect.Left, trackY - trackH / 2, thumbX - rect.Left, trackH, 2, 2, fillPaint);

        // Thumb
        using var thumbPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var thumbBorder = new SKPaint { Color = new SKColor(0, 120, 215), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawCircle(thumbX, trackY, 7, thumbPaint);
        canvas.DrawCircle(thumbX, trackY, 7, thumbBorder);

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Range));
    }

    private static void PaintTextarea(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;
        var isFocused = FormState.FocusedInput == node.NodeKey;

        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color       = isFocused ? new SKColor(0, 120, 215) : new SKColor(150, 150, 150),
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = isFocused ? 2f : 1f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, borderPaint);

        node.Attributes.TryGetValue("value", out var defaultVal);
        var text = FormState.GetTextValue(node.NodeKey, defaultVal);

        if (string.IsNullOrEmpty(text) && node.Attributes.TryGetValue("placeholder", out var ph))
        {
            using var phPaint = new SKPaint { Color = new SKColor(170, 170, 170), IsAntialias = true };
            using var phFont  = new SKFont(SKTypeface.FromFamilyName("Consolas"), 13);
            canvas.DrawText(ph, rect.Left + 4, rect.Top + 16, SKTextAlign.Left, phFont, phPaint);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var textFont  = new SKFont(SKTypeface.FromFamilyName("Consolas"), 13);
            var lines = text.Split('\n');
            var lineY = rect.Top + 16;
            foreach (var line in lines)
            {
                if (lineY > rect.Bottom) break;
                canvas.DrawText(line, rect.Left + 4, lineY, SKTextAlign.Left, textFont, textPaint);
                lineY += 18;
            }
        }

        _hitRegions.Add(new HitRegion(rect, CursorType.Text, NodeKey: node.NodeKey, InputAction: InputAction.TextInput));
    }

    private static void PaintSelect(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;

        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color       = new SKColor(150, 150, 150),
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, borderPaint);

        // Display selected option text
        node.Attributes.TryGetValue("value", out var defaultVal);
        node.Attributes.TryGetValue("_options", out var optionsStr);
        node.Attributes.TryGetValue("_optionValues", out var optValStr);

        var selectedVal = FormState.GetTextValue(node.NodeKey, defaultVal);
        var displayText = selectedVal;
        if (optionsStr != null && optValStr != null)
        {
            var opts = optionsStr.Split('|');
            var vals = optValStr.Split('|');
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i] == selectedVal && i < opts.Length) { displayText = opts[i]; break; }
            }
        }

        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var textFont  = new SKFont { Size = 13 };
        canvas.DrawText(displayText, rect.Left + 4, rect.MidY + 5, SKTextAlign.Left, textFont, textPaint);

        // Drop-down arrow
        var arrowX = rect.Right - 16;
        using var arrowPaint = new SKPaint { Color = new SKColor(100, 100, 100), IsAntialias = true, Style = SKPaintStyle.Fill };
        var path = new SKPath();
        path.MoveTo(arrowX, rect.MidY - 3);
        path.LineTo(arrowX + 10, rect.MidY - 3);
        path.LineTo(arrowX + 5, rect.MidY + 4);
        path.Close();
        canvas.DrawPath(path, arrowPaint);

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.SelectDropdown));

        // Defer dropdown overlay to draw on top of everything
        if (FormState.OpenDropdown == node.NodeKey)
            _pendingDropdown = (node, rect, selectedVal);
    }

    private static void DrawDropdownOverlay(SKCanvas canvas, LayoutNode node, SKRect rect, string selectedVal)
    {
        node.Attributes.TryGetValue("_options", out var optionsStr);
        node.Attributes.TryGetValue("_optionValues", out var optValStr);
        if (optionsStr == null || optValStr == null) return;

        var opts = optionsStr.Split('|');
        var vals = optValStr.Split('|');
        var itemH = 24f;
        var dropW = rect.Width;
        var dropH = opts.Length * itemH;
        var dropRect = new SKRect(rect.Left, rect.Bottom, rect.Left + dropW, rect.Bottom + dropH);

        // Shadow
        using var shadowPaint = new SKPaint { Color = new SKColor(0, 0, 0, 40), IsAntialias = true };
        canvas.DrawRect(new SKRect(dropRect.Left + 2, dropRect.Top + 2, dropRect.Right + 2, dropRect.Bottom + 2), shadowPaint);

        // Background
        using var dropBg = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(dropRect, dropBg);
        using var dropBorder = new SKPaint { Color = new SKColor(150, 150, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(dropRect, dropBorder);

        using var optFont = new SKFont { Size = 13 };
        for (int i = 0; i < opts.Length; i++)
        {
            var optY = dropRect.Top + i * itemH;
            var optRect = new SKRect(dropRect.Left, optY, dropRect.Right, optY + itemH);

            // Highlight selected
            if (vals[i] == selectedVal)
            {
                using var hlPaint = new SKPaint { Color = new SKColor(0, 120, 215, 30) };
                canvas.DrawRect(optRect, hlPaint);
            }

            using var optPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            canvas.DrawText(opts[i], optRect.Left + 4, optRect.Top + 17, SKTextAlign.Left, optFont, optPaint);

            // Each option is a clickable hit region — store index in SelectOptionMap
            _hitRegions.Add(new HitRegion(optRect, CursorType.Pointer, NodeKey: node.NodeKey,
                InputAction: InputAction.SelectDropdown));
            SelectOptionMap[_hitRegions.Count - 1] = i;
        }
    }

    // -------------------------------------------------------------------------
    // Button
    // -------------------------------------------------------------------------

    private static void PaintButton(SKCanvas canvas, LayoutNode node)
    {
        var box  = node.Box;
        var rect = box.ContentBox;

        var btnLabel = node.DisplayText;
        if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
        if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";

        DrawBoxShadows(canvas, box, node);

        var bgColor = node.GetBackgroundColor();
        if (bgColor == SKColors.Transparent) bgColor = new SKColor(225, 225, 225);
        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
        var (brx, bry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
        if (brx > 0 || bry > 0) canvas.DrawRoundRect(box.PaddingBox, brx, bry, bgPaint);
        else                     canvas.DrawRect(box.PaddingBox, bgPaint);

        DrawBorders(canvas, node.Box, node);

        using var btnFont   = new SKFont { Size = 13 };
        var textColor = node.GetColor();
        using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };
        canvas.DrawText(btnLabel, rect.Left + FormLayout.ButtonPaddingX, rect.Top + FormLayout.ButtonPaddingY + 13,
                        SKTextAlign.Left, btnFont, textPaint);

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Button));
    }

    // -------------------------------------------------------------------------
    // Horizontal rule
    // -------------------------------------------------------------------------

    private static void PaintHorizontalRule(SKCanvas canvas, LayoutNode node)
    {
        var box   = node.Box;
        var y     = box.BorderBox.Top + box.Border.Top / 2f;
        var color = node.GetBorderTopColor();
        var thick = box.Border.Top > 0 ? box.Border.Top : 1f;

        using var paint = new SKPaint { Color = color, StrokeWidth = thick, IsAntialias = false };
        canvas.DrawLine(box.BorderBox.Left, y, box.BorderBox.Right, y, paint);
    }

    // -------------------------------------------------------------------------
    // List item
    // -------------------------------------------------------------------------

    private static void PaintListItem(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        // Background
        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else                   canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        DrawBorders(canvas, box, node);

        var listStyleType = node.GetListStyleType();
        var listStylePos  = node.GetListStylePosition();
        float insideMarkerWidth = 0f; // track width consumed by inside marker

        if (listStyleType != ListStyleType.None)
        {
            using var markerFont  = TextMeasure.CreateFont(node);
            using var markerPaint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

            var markerText = GetMarkerText(listStyleType, node);
            var ascent     = -markerFont.Metrics.Ascent;

            if (listStylePos == ListStylePosition.Inside)
            {
                // Inside: marker is part of content flow — draw it and offset content
                if (!string.IsNullOrEmpty(markerText))
                {
                    var markerStr = markerText + " ";
                    insideMarkerWidth = markerFont.MeasureText(markerStr);
                    canvas.DrawText(markerStr, box.ContentBox.Left, box.ContentBox.Top + ascent,
                                    SKTextAlign.Left, markerFont, markerPaint);
                }
                else
                {
                    var bulletSize = markerFont.Size * 0.2f;
                    insideMarkerWidth = bulletSize * 2 + markerFont.Size * 0.3f + 4f;
                    DrawBulletMarker(canvas, listStyleType, box.ContentBox.Left + markerFont.Size * 0.3f,
                                     box.ContentBox.Top + markerFont.Size * 0.55f, markerFont.Size, markerPaint);
                }
            }
            else
            {
                // Outside: marker is drawn to the left of content box
                var markerX = box.ContentBox.Left - 6f;
                var markerY = box.ContentBox.Top;

                if (!string.IsNullOrEmpty(markerText))
                {
                    canvas.DrawText(markerText, markerX, markerY + ascent,
                                    SKTextAlign.Right, markerFont, markerPaint);
                }
                else
                {
                    DrawBulletMarker(canvas, listStyleType, markerX - markerFont.Size * 0.2f * 2,
                                     markerY + markerFont.Size * 0.55f, markerFont.Size, markerPaint);
                }
            }
        }

        // Paint own text — offset by inside marker width if applicable
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            using var font  = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText,
                            box.ContentBox.Left + insideMarkerWidth, box.ContentBox.Top,
                            box.ContentBox.Width - insideMarkerWidth, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    private static string? GetMarkerText(ListStyleType type, LayoutNode node)
    {
        var index = GetOrderedIndex(node);
        return type switch
        {
            ListStyleType.Decimal            => $"{index}.",
            ListStyleType.DecimalLeadingZero => $"{index:D2}.",
            ListStyleType.LowerAlpha         => $"{(char)('a' + (index - 1) % 26)}.",
            ListStyleType.UpperAlpha         => $"{(char)('A' + (index - 1) % 26)}.",
            ListStyleType.LowerRoman         => $"{ToRoman(index).ToLowerInvariant()}.",
            ListStyleType.UpperRoman         => $"{ToRoman(index)}.",
            _ => null, // bullet types return null
        };
    }

    private static void DrawBulletMarker(SKCanvas canvas, ListStyleType type, float cx, float cy, float fontSize, SKPaint paint)
    {
        var radius = fontSize * 0.2f;
        switch (type)
        {
            case ListStyleType.Disc:
                canvas.DrawCircle(cx, cy, radius, paint);
                break;
            case ListStyleType.Circle:
                using (var strokePaint = new SKPaint { Color = paint.Color, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true })
                    canvas.DrawCircle(cx, cy, radius, strokePaint);
                break;
            case ListStyleType.Square:
                canvas.DrawRect(cx - radius, cy - radius, radius * 2, radius * 2, paint);
                break;
        }
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999) return number.ToString();
        var sb = new System.Text.StringBuilder();
        int[] values   = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] syms  = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        for (int i = 0; i < values.Length; i++)
        {
            while (number >= values[i]) { sb.Append(syms[i]); number -= values[i]; }
        }
        return sb.ToString();
    }

    private static bool IsInsideOrderedList(LayoutNode node)
    {
        var p = node.Parent;
        while (p != null)
        {
            if (p.TagName == "OL") return true;
            if (p.TagName == "UL") return false;
            p = p.Parent;
        }
        return false;
    }

    private static int GetOrderedIndex(LayoutNode node)
    {
        if (node.Parent == null) return 1;
        var index = 1;
        foreach (var sibling in node.Parent.Children)
        {
            if (sibling == node) break;
            if (sibling.TagName == "LI") index++;
        }
        return index;
    }

    // -------------------------------------------------------------------------
    // Shadows
    // -------------------------------------------------------------------------

    private static void DrawBoxShadows(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var shadows = node.GetBoxShadows();
        if (shadows.Count == 0) return;

        var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);

        // Paint shadows in reverse order (last layer first, per CSS spec)
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            var s     = shadows[i];
            if (s.Inset) continue;   // inset shadows not yet supported

            var sigma = s.Blur / 2f;
            var shadowRect = SKRect.Inflate(box.PaddingBox, s.Spread, s.Spread);
            shadowRect.Offset(s.OffsetX, s.OffsetY);
            var srx = Math.Max(0, rx + s.Spread);
            var sry = Math.Max(0, ry + s.Spread);

            using var paint = new SKPaint
            {
                Color      = s.Color,
                IsAntialias = true,
                MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
            };

            if (srx > 0 || sry > 0) canvas.DrawRoundRect(shadowRect, srx, sry, paint);
            else                     canvas.DrawRect(shadowRect, paint);
        }
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static void DrawBorders(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var bw = box.Border;
        var (rx, ry) = node.GetBorderRadius(box.BorderBox.Width, box.BorderBox.Height);

        if (rx > 0 || ry > 0)
        {
            // With border-radius draw a single stroked rounded rect using top-border values
            var maxWidth = Math.Max(Math.Max(bw.Top, bw.Right), Math.Max(bw.Bottom, bw.Left));
            if (maxWidth <= 0) return;
            var style = node.GetBorderStyleTop();
            if (style == BorderStyle.None || style == BorderStyle.Hidden) return;
            using var p = new SKPaint
            {
                Color       = node.GetBorderTopColor(),
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = maxWidth,
                IsAntialias = true,
            };
            ApplyBorderStyle(p, style);
            var inset = maxWidth / 2f;
            var r     = SKRect.Inflate(box.BorderBox, -inset, -inset);
            if (r.Width <= 0 || r.Height <= 0) return;
            canvas.DrawRoundRect(r, Math.Max(0, rx - inset), Math.Max(0, ry - inset), p);
            return;
        }

        DrawBorderSide(canvas, box.BorderBox.Left, box.BorderBox.Top + bw.Top / 2,
                        box.BorderBox.Right, box.BorderBox.Top + bw.Top / 2,
                        bw.Top, node.GetBorderTopColor(), node.GetBorderStyleTop());
        DrawBorderSide(canvas, box.BorderBox.Right - bw.Right / 2, box.BorderBox.Top,
                        box.BorderBox.Right - bw.Right / 2, box.BorderBox.Bottom,
                        bw.Right, node.GetBorderRightColor(), node.GetBorderStyleRight());
        DrawBorderSide(canvas, box.BorderBox.Left, box.BorderBox.Bottom - bw.Bottom / 2,
                        box.BorderBox.Right, box.BorderBox.Bottom - bw.Bottom / 2,
                        bw.Bottom, node.GetBorderBottomColor(), node.GetBorderStyleBottom());
        DrawBorderSide(canvas, box.BorderBox.Left + bw.Left / 2, box.BorderBox.Top,
                        box.BorderBox.Left + bw.Left / 2, box.BorderBox.Bottom,
                        bw.Left, node.GetBorderLeftColor(), node.GetBorderStyleLeft());
    }

    private static void DrawBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2,
                                        float width, SKColor color, BorderStyle style)
    {
        if (width <= 0 || style == BorderStyle.None || style == BorderStyle.Hidden) return;

        if (style == BorderStyle.Double && width >= 3f)
        {
            // Draw two lines with a gap in between
            var lineW = Math.Max(1f, width / 3f);
            var offset = width / 2f - lineW / 2f;
            var isHorizontal = Math.Abs(y1 - y2) < 0.01f;
            using var p = new SKPaint { Color = color, StrokeWidth = lineW, IsAntialias = true };
            if (isHorizontal)
            {
                canvas.DrawLine(x1, y1 - offset, x2, y2 - offset, p);
                canvas.DrawLine(x1, y1 + offset, x2, y2 + offset, p);
            }
            else
            {
                canvas.DrawLine(x1 - offset, y1, x2 - offset, y2, p);
                canvas.DrawLine(x1 + offset, y1, x2 + offset, y2, p);
            }
            return;
        }

        if (style == BorderStyle.Groove || style == BorderStyle.Ridge)
        {
            var halfW = width / 2f;
            var isHorizontal = Math.Abs(y1 - y2) < 0.01f;
            var dark = DarkenColor(color, 0.6f);
            var light = LightenColor(color, 1.4f);
            var c1 = style == BorderStyle.Groove ? dark : light;
            var c2 = style == BorderStyle.Groove ? light : dark;
            using var p1 = new SKPaint { Color = c1, StrokeWidth = halfW, IsAntialias = true };
            using var p2 = new SKPaint { Color = c2, StrokeWidth = halfW, IsAntialias = true };
            if (isHorizontal)
            {
                canvas.DrawLine(x1, y1 - halfW / 2f, x2, y2 - halfW / 2f, p1);
                canvas.DrawLine(x1, y1 + halfW / 2f, x2, y2 + halfW / 2f, p2);
            }
            else
            {
                canvas.DrawLine(x1 - halfW / 2f, y1, x2 - halfW / 2f, y2, p1);
                canvas.DrawLine(x1 + halfW / 2f, y1, x2 + halfW / 2f, y2, p2);
            }
            return;
        }

        if (style == BorderStyle.Inset || style == BorderStyle.Outset)
        {
            var isTop  = Math.Abs(y1 - y2) < 0.01f && y1 < (y1 + y2) / 2f + 1; // approximate
            var isLeft = Math.Abs(x1 - x2) < 0.01f && x1 < (x1 + x2) / 2f + 1;
            bool darken = style == BorderStyle.Inset ? (isTop || isLeft) : !(isTop || isLeft);
            var c = darken ? DarkenColor(color, 0.6f) : LightenColor(color, 1.4f);
            using var p = new SKPaint { Color = c, StrokeWidth = width, IsAntialias = true };
            canvas.DrawLine(x1, y1, x2, y2, p);
            return;
        }

        using var paint = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true };
        ApplyBorderStyle(paint, style);
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void ApplyBorderStyle(SKPaint paint, BorderStyle style)
    {
        switch (style)
        {
            case BorderStyle.Dotted:
                paint.PathEffect = SKPathEffect.CreateDash([paint.StrokeWidth, paint.StrokeWidth * 2], 0);
                break;
            case BorderStyle.Dashed:
                paint.PathEffect = SKPathEffect.CreateDash([paint.StrokeWidth * 3, paint.StrokeWidth * 2], 0);
                break;
        }
    }

    private static SKColor DarkenColor(SKColor c, float factor)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red * factor, 0, 255),
            (byte)Math.Clamp(c.Green * factor, 0, 255),
            (byte)Math.Clamp(c.Blue * factor, 0, 255),
            c.Alpha);
    }

    private static SKColor LightenColor(SKColor c, float factor)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red * factor, 0, 255),
            (byte)Math.Clamp(c.Green * factor, 0, 255),
            (byte)Math.Clamp(c.Blue * factor, 0, 255),
            c.Alpha);
    }

    // ---- Background image ----

    private static readonly Dictionary<string, SKBitmap?> _bgImageCache = new();

    private static void DrawBackgroundImage(SKCanvas canvas, LayoutNode node, BoxDimensions box)
    {
        var imageUrl = node.GetBackgroundImage();
        if (imageUrl == null) return;

        if (!_bgImageCache.TryGetValue(imageUrl, out var bitmap))
        {
            bitmap = ResourceLoader.FetchImage(imageUrl, Parser.BaseUrl);
            _bgImageCache[imageUrl] = bitmap;
        }
        if (bitmap == null) return;

        var repeat = node.GetBackgroundRepeat();
        var (posX, posY) = node.GetBackgroundPosition();
        var (sizeW, sizeH) = node.GetBackgroundSize();
        var area = box.PaddingBox;

        // Resolve image draw size
        float drawW = bitmap.Width, drawH = bitmap.Height;
        if (sizeW == "cover" || sizeW == "contain")
        {
            var scaleX = area.Width / bitmap.Width;
            var scaleY = area.Height / bitmap.Height;
            var scale = sizeW == "cover" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            drawW = bitmap.Width * scale;
            drawH = bitmap.Height * scale;
        }
        else
        {
            if (sizeW != "auto") drawW = ResolveBgDimension(sizeW, area.Width);
            if (sizeH != "auto") drawH = ResolveBgDimension(sizeH, area.Height);
        }

        // Resolve position
        float offX = ResolveBgPosition(posX, area.Width, drawW);
        float offY = ResolveBgPosition(posY, area.Height, drawH);

        canvas.Save();
        var (rx, ry) = node.GetBorderRadius(area.Width, area.Height);
        if (rx > 0 || ry > 0)
            canvas.ClipRoundRect(new SKRoundRect(area, rx, ry), antialias: true);
        else
            canvas.ClipRect(area);

        bool repeatX = repeat is "repeat" or "repeat-x";
        bool repeatY = repeat is "repeat" or "repeat-y";

        float startX = repeatX ? area.Left + offX - (float)Math.Ceiling((offX) / drawW) * drawW : area.Left + offX;
        float startY = repeatY ? area.Top + offY - (float)Math.Ceiling((offY) / drawH) * drawH : area.Top + offY;
        float endX = repeatX ? area.Right : startX + drawW;
        float endY = repeatY ? area.Bottom : startY + drawH;

        for (float py = startY; py < endY; py += drawH)
        {
            for (float px = startX; px < endX; px += drawW)
            {
                var dest = new SKRect(px, py, px + drawW, py + drawH);
                canvas.DrawBitmap(bitmap, dest);
                if (!repeatX) break;
            }
            if (!repeatY) break;
        }

        canvas.Restore();
    }

    private static float ResolveBgDimension(string val, float total)
    {
        val = val.Trim();
        if (val.EndsWith("px") && float.TryParse(val[..^2], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
        if (val.EndsWith('%') && float.TryParse(val[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct)) return pct / 100f * total;
        return total;
    }

    private static float ResolveBgPosition(string val, float areaSize, float imageSize)
    {
        val = val.Trim();
        return val switch
        {
            "left" or "top" => 0f,
            "center" => (areaSize - imageSize) / 2f,
            "right" or "bottom" => areaSize - imageSize,
            _ when val.EndsWith("px") && float.TryParse(val[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px) => px,
            _ when val.EndsWith('%') && float.TryParse(val[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct) => (areaSize - imageSize) * pct / 100f,
            _ => 0f,
        };
    }

    // ---- Outline drawing ----

    private static void DrawOutline(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var style = node.GetOutlineStyle();
        if (style == BorderStyle.None) return;
        var width = node.GetOutlineWidth();
        if (width <= 0) return;
        var color = node.GetOutlineColor();
        var offset = node.GetOutlineOffset();

        var outlineRect = SKRect.Inflate(box.BorderBox, offset + width / 2f, offset + width / 2f);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true,
        };
        ApplyBorderStyle(paint, style);
        canvas.DrawRect(outlineRect, paint);
    }

    private static void DrawWrappedText(SKCanvas canvas, LayoutNode node, string text,
                                        float x, float y, float maxWidth,
                                        SKFont font, SKPaint paint)
    {
        var whiteSpace     = node.GetWhiteSpace();
        var textAlign      = node.GetTextAlign();
        var underline      = node.IsUnderline();
        var lineThrough    = node.IsLineThrough();
        var textTransform  = node.GetTextTransform();
        var letterSpacing  = node.GetLetterSpacing(font.Size);
        var wordSpacing    = node.GetWordSpacing(font.Size);
        var textIndent     = node.GetTextIndent(maxWidth, font.Size);

        // Apply text-transform
        text = StyleExtensions.ApplyTextTransform(text, textTransform);

        var lines      = TextMeasure.WrapText(text, maxWidth, font, whiteSpace);
        var lineY      = y;
        var textShadow = node.GetTextShadow();
        var isFirstLine = true;

        foreach (var line in lines)
        {
            // Measure actual draw width accounting for letter-spacing
            var lineWidth = line.Width;
            if (letterSpacing != 0f || wordSpacing != 0f)
                lineWidth = MeasureWithSpacing(line.Text, font, letterSpacing, wordSpacing);

            // Compute x offset for text-align
            var drawX = textAlign switch
            {
                TextAlign.Center => x + (maxWidth - lineWidth) / 2f,
                TextAlign.Right  => x + maxWidth - lineWidth,
                _                => x,
            };

            // Apply text-indent on first line
            if (isFirstLine && textIndent != 0f)
            {
                drawX += textIndent;
                isFirstLine = false;
            }
            else
            {
                isFirstLine = false;
            }

            // SkiaSharp draws at baseline; add ascent to convert top→baseline
            var baseline = lineY + line.Ascent;

            // text-shadow drawn before (behind) the main text
            if (textShadow.HasValue)
            {
                var ts    = textShadow.Value;
                var sigma = ts.Blur / 2f;
                using var sp = new SKPaint
                {
                    Color      = ts.Color,
                    IsAntialias = true,
                    MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
                };
                DrawTextWithSpacing(canvas, line.Text, drawX + ts.OffsetX, baseline + ts.OffsetY, font, sp, letterSpacing, wordSpacing);
            }

            DrawTextWithSpacing(canvas, line.Text, drawX, baseline, font, paint, letterSpacing, wordSpacing);

            if (underline)
            {
                var metrics  = font.Metrics;
                var uY       = baseline + (metrics.UnderlinePosition ?? font.Size * 0.1f);
                var uThick   = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = paint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, uY, drawX + lineWidth, uY, lp);
            }

            if (lineThrough)
            {
                var strikY   = baseline - font.Size * 0.3f;
                var uThick   = Math.Max(font.Metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = paint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, strikY, drawX + lineWidth, strikY, lp);
            }

            lineY += line.Height;
        }
    }

    /// <summary>Draws text with custom letter-spacing and word-spacing.</summary>
    private static void DrawTextWithSpacing(SKCanvas canvas, string text, float x, float y,
                                            SKFont font, SKPaint paint, float letterSpacing, float wordSpacing)
    {
        if (letterSpacing == 0f && wordSpacing == 0f)
        {
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
            return;
        }

        var curX = x;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            canvas.DrawText(s, curX, y, SKTextAlign.Left, font, paint);
            curX += font.MeasureText(s) + letterSpacing;
            if (ch == ' ') curX += wordSpacing;
        }
    }

    /// <summary>Measures text width including letter-spacing and word-spacing.</summary>
    private static float MeasureWithSpacing(string text, SKFont font, float letterSpacing, float wordSpacing)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        foreach (var ch in text)
        {
            w += font.MeasureText(ch.ToString()) + letterSpacing;
            if (ch == ' ') w += wordSpacing;
        }
        // Remove trailing letter-spacing
        w -= letterSpacing;
        return Math.Max(0f, w);
    }
}
