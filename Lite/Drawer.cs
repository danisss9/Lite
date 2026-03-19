using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;

namespace Lite;

internal static class Drawer
{
    private static List<HitRegion> _hitRegions = [];

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

        canvas.Save();
        canvas.Translate(0, -viewport.ScrollY);

        PaintNode(canvas, root, width);

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

    private static void PaintFixedNodes(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        foreach (var child in node.Children)
        {
            if (child.GetPosition() == PositionType.Fixed)
                PaintNode(canvas, child, viewportWidth);
            else
                PaintFixedNodes(canvas, child, viewportWidth);
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

            case "HR":
                PaintHorizontalRule(canvas, node);
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

        DrawBorders(canvas, box, node);

        var cursor = node.GetCursor();
        if (cursor != CursorType.Default)
            _hitRegions.Add(new HitRegion(box.BorderBox, cursor));

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

        var cursor = node.GetCursor();
        if (cursor != CursorType.Default)
            _hitRegions.Add(new HitRegion(box.MarginBox, cursor, node.Href));

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
        _hitRegions.Add(new HitRegion(node.Box.BorderBox, cursor, node.Href));

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

        if (inputType == "checkbox")
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
        else
        {
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
                using var phFont  = new SKFont { Size = 12 };
                canvas.DrawText(ph, rect.Left + 4, rect.Top + 14, SKTextAlign.Left, phFont, phPaint);
            }
            else if (!string.IsNullOrEmpty(text))
            {
                using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                using var textFont  = new SKFont { Size = 12 };
                canvas.DrawText(text, rect.Left + 4, rect.Top + 14, SKTextAlign.Left, textFont, textPaint);
            }

            if (isFocused)
            {
                using var caretFont  = new SKFont { Size = 12 };
                var caretX = rect.Left + 4 + caretFont.MeasureText(text);
                using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
                canvas.DrawLine(caretX, rect.Top + 3, caretX, rect.Bottom - 3, caretPaint);
            }

            _hitRegions.Add(new HitRegion(rect, CursorType.Text, NodeKey: node.NodeKey, InputAction: InputAction.TextInput));
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

        // Marker — drawn to the left of the content box
        var markerX = box.ContentBox.Left - 6f;
        var markerY = box.ContentBox.Top;

        using var markerFont  = TextMeasure.CreateFont(node);
        using var markerPaint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

        if (IsInsideOrderedList(node))
        {
            var index      = GetOrderedIndex(node);
            var markerText = $"{index}.";
            var ascent     = -markerFont.Metrics.Ascent;
            canvas.DrawText(markerText, markerX, markerY + ascent, SKTextAlign.Right, markerFont, markerPaint);
        }
        else
        {
            var radius  = markerFont.Size * 0.2f;
            var centerX = markerX - radius * 2;
            var centerY = markerY + markerFont.Size * 0.55f;
            canvas.DrawCircle(centerX, centerY, radius, markerPaint);
        }

        // Paint own text
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            using var font  = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText,
                            box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Width, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
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
            using var p = new SKPaint
            {
                Color       = node.GetBorderTopColor(),
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = maxWidth,
                IsAntialias = true,
            };
            // Inset the rect by half stroke width so it sits on the border box edge
            var inset = maxWidth / 2f;
            var r     = SKRect.Inflate(box.BorderBox, -inset, -inset);
            canvas.DrawRoundRect(r, Math.Max(0, rx - inset), Math.Max(0, ry - inset), p);
            return;
        }

        if (bw.Top > 0)
        {
            using var p = new SKPaint { Color = node.GetBorderTopColor(), StrokeWidth = bw.Top, IsAntialias = true };
            canvas.DrawLine(box.BorderBox.Left, box.BorderBox.Top + bw.Top / 2, box.BorderBox.Right, box.BorderBox.Top + bw.Top / 2, p);
        }
        if (bw.Right > 0)
        {
            using var p = new SKPaint { Color = node.GetBorderRightColor(), StrokeWidth = bw.Right, IsAntialias = true };
            canvas.DrawLine(box.BorderBox.Right - bw.Right / 2, box.BorderBox.Top, box.BorderBox.Right - bw.Right / 2, box.BorderBox.Bottom, p);
        }
        if (bw.Bottom > 0)
        {
            using var p = new SKPaint { Color = node.GetBorderBottomColor(), StrokeWidth = bw.Bottom, IsAntialias = true };
            canvas.DrawLine(box.BorderBox.Left, box.BorderBox.Bottom - bw.Bottom / 2, box.BorderBox.Right, box.BorderBox.Bottom - bw.Bottom / 2, p);
        }
        if (bw.Left > 0)
        {
            using var p = new SKPaint { Color = node.GetBorderLeftColor(), StrokeWidth = bw.Left, IsAntialias = true };
            canvas.DrawLine(box.BorderBox.Left + bw.Left / 2, box.BorderBox.Top, box.BorderBox.Left + bw.Left / 2, box.BorderBox.Bottom, p);
        }
    }

    private static void DrawWrappedText(SKCanvas canvas, LayoutNode node, string text,
                                        float x, float y, float maxWidth,
                                        SKFont font, SKPaint paint)
    {
        var whiteSpace  = node.GetWhiteSpace();
        var textAlign   = node.GetTextAlign();
        var underline   = node.IsUnderline();
        var lineThrough = node.IsLineThrough();

        var lines      = TextMeasure.WrapText(text, maxWidth, font, whiteSpace);
        var lineY      = y;
        var textShadow = node.GetTextShadow();

        foreach (var line in lines)
        {
            // Compute x offset for text-align
            var drawX = textAlign switch
            {
                TextAlign.Center => x + (maxWidth - line.Width) / 2f,
                TextAlign.Right  => x + maxWidth - line.Width,
                _                => x,
            };

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
                canvas.DrawText(line.Text, drawX + ts.OffsetX, baseline + ts.OffsetY, SKTextAlign.Left, font, sp);
            }

            canvas.DrawText(line.Text, drawX, baseline, SKTextAlign.Left, font, paint);

            if (underline)
            {
                var metrics  = font.Metrics;
                var uY       = baseline + (metrics.UnderlinePosition ?? font.Size * 0.1f);
                var uThick   = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = paint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, uY, drawX + line.Width, uY, lp);
            }

            if (lineThrough)
            {
                var strikY   = baseline - font.Size * 0.3f;
                var uThick   = Math.Max(font.Metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = paint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, strikY, drawX + line.Width, strikY, lp);
            }

            lineY += line.Height;
        }
    }
}
