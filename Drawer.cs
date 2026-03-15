using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;

namespace Lite;

internal static class Drawer
{
    private static float _y;
    private static bool _measuring;
    private static List<HitRegion> _hitRegions = [];

    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, LayoutNode root, Viewport viewport)
    {
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);

        canvas.Clear(new SKColor(240, 240, 242));
        _y = 64f;
        _measuring = false;
        _hitRegions = [];

        canvas.Save();
        canvas.Translate(0, -viewport.ScrollY);
        PaintNode(canvas, root, width, height);
        canvas.Restore();

        viewport.ContentHeight = _y;
        DrawScrollbar(canvas, viewport, width, height);

        return (bitmap.GetPixels(), _hitRegions);
    }

    private static void DrawScrollbar(SKCanvas canvas, Viewport viewport, int width, int height)
    {
        if (viewport.ContentHeight <= viewport.ViewportHeight) return;

        const float barWidth = 6f;
        const float margin   = 2f;
        var ratio      = viewport.ViewportHeight / viewport.ContentHeight;
        var trackH     = viewport.ViewportHeight;
        var thumbH     = Math.Max(trackH * ratio, 24f);
        var thumbTop   = viewport.ScrollY / viewport.ContentHeight * trackH;
        var x          = width - barWidth - margin;

        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true };
        canvas.DrawRoundRect(x, thumbTop + margin, barWidth, thumbH - margin * 2, 3, 3, paint);
    }

    private static void PaintNode(SKCanvas canvas, LayoutNode node, int width, int height)
    {
        switch (node.TagName)
        {
            case "DIV":
            case "SECTION":
            case "HEADER":
            case "MAIN":
            case "FOOTER":
            case "NAV":
            case "ARTICLE":
            {
                var fontSize  = node.GetFontSize();
                var margin    = node.GetMargin(width, height, fontSize);
                var padding   = node.GetPadding(width, height, fontSize);
                var border    = node.GetBorderWidth();
                var rectWidth = node.GetWidth(width);
                if (rectWidth <= 0) rectWidth = width - 64;

                var left   = 32f + margin.Left;
                var startY = _y + margin.Top;

                // Measure pass — advance _y through children without drawing
                _y = startY + padding.Top + border.Top;
                _measuring = true;
                foreach (var child in node.Children)
                    PaintNode(canvas, child, width, height);
                _measuring = false;
                var endY = _y + padding.Bottom + border.Bottom;

                // Draw background over the measured content area
                var bgColor = node.GetBackgroundColor();
                if (bgColor != SKColors.Transparent)
                {
                    var bgRect = new SKRect(left, startY, left + rectWidth, endY);
                    using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
                    canvas.DrawRect(bgRect, bgPaint);
                }

                // Draw borders
                var box = new BoxDimensions
                {
                    ContentBox = new SKRect(left + padding.Left, startY + padding.Top,
                                           left + padding.Left + rectWidth - padding.Left - padding.Right,
                                           endY - padding.Bottom),
                    Margin  = margin,
                    Padding = padding,
                    Border  = border,
                };
                node.Box = box;
                DrawBorders(canvas, box, node);

                var divCursor = node.GetCursor();
                if (divCursor != CursorType.Default)
                    _hitRegions.Add(new HitRegion(box.BorderBox, divCursor));

                // Real draw pass
                _y = startY + padding.Top + border.Top;
                foreach (var child in node.Children)
                    PaintNode(canvas, child, width, height);

                _y = endY + margin.Bottom;
                return; // children already processed — skip shared loop below
            }
            case "IMG":
            {
                if (_measuring) { _y += (node.IntrinsicHeight > 0 ? node.IntrinsicHeight : node.Image?.Height ?? 100f); break; }

                var drawW = node.IntrinsicWidth  > 0 ? (float)node.IntrinsicWidth  : node.Image?.Width  ?? 100f;
                var drawH = node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 100f;

                var destRect = new SKRect(32f, _y, 32f + drawW, _y + drawH);

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
                        canvas.DrawText(node.Alt, 32f + 4, _y + 14, SKTextAlign.Left, altFont, altPaint);
                    }
                }

                _y += drawH;
                break;
            }
            case { } h when h.StartsWith('H') && h.Length == 2:
            case "P":
            case "LABEL":
            {
                var fontSize = node.GetFontSize();
                var margin   = node.GetMargin(width, height, fontSize);
                var padding  = node.GetPadding(width, height, fontSize);
                var border   = node.GetBorderWidth();

                _y += margin.Top;

                if (!string.IsNullOrEmpty(node.DisplayText))
                {
                    if (!_measuring)
                    {
                        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
                        using var font  = new SKFont
                        {
                            Size     = fontSize,
                            Embolden = node.TagName != "P" && node.TagName != "LABEL",
                            Typeface = SKTypeface.FromFamilyName(node.GetFontFamily()),
                        };

                        var x        = 32f + margin.Left + padding.Left + border.Left;
                        var maxWidth = width - 64 - margin.Left - margin.Right
                                             - padding.Left - padding.Right
                                             - border.Left  - border.Right;

                        var yBefore = _y + padding.Top + border.Top;
                        _y = DrawWrappedText(canvas, node.DisplayText, x, yBefore, maxWidth, font, paint, node.IsUnderline());
                        _y += padding.Bottom + border.Bottom;

                        var cursor = node.GetCursor();
                        if (cursor != CursorType.Default)
                            _hitRegions.Add(new HitRegion(
                                new SKRect(32f, yBefore - padding.Top, 32f + (width - 64), _y),
                                cursor, node.Href));
                    }
                    else
                    {
                        // Measure: estimate height from font size and text length
                        var lineHeight = fontSize * 1.4f;
                        var maxWidth   = width - 64 - margin.Left - margin.Right;
                        var charsPerLine = Math.Max(1, (int)(maxWidth / (fontSize * 0.6f)));
                        var lines = (int)Math.Ceiling((double)node.DisplayText.Length / charsPerLine);
                        _y += padding.Top + border.Top + lines * lineHeight + padding.Bottom + border.Bottom;
                    }
                }

                _y += margin.Bottom;
                break;
            }
            case "INPUT":
            {
                if (_measuring) { _y += FormLayout.TextInputHeight + FormLayout.ElementGap; break; }

                var inputType = node.Attributes.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "text";
                if (inputType == "checkbox")
                {
                    var size = FormLayout.CheckboxSize;
                    var rect = new SKRect(32f, _y, 32f + size, _y + size);

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
                    _y += size + FormLayout.ElementGap;
                }
                else
                {
                    var rect      = new SKRect(32f, _y, 32f + FormLayout.TextInputWidth, _y + FormLayout.TextInputHeight);
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
                    _y += FormLayout.TextInputHeight + FormLayout.ElementGap;
                }
                break;
            }
            case "BUTTON":
            {
                if (_measuring)
                {
                    using var mFont = new SKFont { Size = 13 };
                    var label = node.DisplayText;
                    if (string.IsNullOrEmpty(label)) node.Attributes.TryGetValue("value", out label);
                    if (string.IsNullOrEmpty(label)) label = "Button";
                    _y += 13f + FormLayout.ButtonPaddingY * 2 + FormLayout.ElementGap;
                    break;
                }

                var btnLabel = node.DisplayText;
                if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
                if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";

                using var btnFont = new SKFont { Size = 13 };
                var textWidth = btnFont.MeasureText(btnLabel);
                var btnW = textWidth + FormLayout.ButtonPaddingX * 2;
                var btnH = 13f + FormLayout.ButtonPaddingY * 2;
                var rect = new SKRect(32f, _y, 32f + btnW, _y + btnH);

                using var bgPaint     = new SKPaint { Color = new SKColor(225, 225, 225) };
                canvas.DrawRect(rect, bgPaint);

                using var borderPaint = new SKPaint { Color = new SKColor(173, 173, 173), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
                canvas.DrawRect(rect, borderPaint);

                using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                canvas.DrawText(btnLabel, rect.Left + FormLayout.ButtonPaddingX, rect.Top + FormLayout.ButtonPaddingY + 13, SKTextAlign.Left, btnFont, textPaint);

                _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Button));
                _y += btnH + FormLayout.ElementGap;
                break;
            }
            case "A":
            {
                if (!string.IsNullOrEmpty(node.Text))
                {
                    var fontSize = node.GetFontSize();
                    var padding  = node.GetPadding(width, height, fontSize);
                    var border   = node.GetBorderWidth();

                    if (!_measuring)
                    {
                        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
                        using var font  = new SKFont
                        {
                            Size     = fontSize,
                            Embolden = node.TagName == "H1",
                            Typeface = SKTypeface.FromFamilyName(node.GetFontFamily()),
                        };

                        var x        = 32f + padding.Left + border.Left;
                        var maxWidth = width - 64 - padding.Left - padding.Right - border.Left - border.Right;
                        var yBefore  = _y + padding.Top + border.Top;

                        _y = DrawWrappedText(canvas, node.DisplayText, x, yBefore, maxWidth, font, paint, node.IsUnderline());
                        _y += padding.Bottom + border.Bottom;

                        var textCursor = node.GetCursor();
                        if (textCursor != CursorType.Default)
                            _hitRegions.Add(new HitRegion(new SKRect(32f, yBefore - padding.Top, 32f + (width - 64), _y), textCursor, node.Href));
                    }
                    else
                    {
                        var lineHeight = fontSize * 1.4f;
                        _y += padding.Top + border.Top + lineHeight + padding.Bottom + border.Bottom;
                    }
                }
                break;
            }
        }

        foreach (var child in node.Children)
        {
            PaintNode(canvas, child, width, height);
        }
    }

    private static void DrawBorders(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var bw = box.Border;
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

    private static float DrawWrappedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SKFont font, SKPaint paint, bool underline = false)
    {
        var lineHeight = font.Size * 1.4f;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(candidate) > maxWidth && line.Length > 0)
            {
                DrawLine(canvas, line.ToString(), x, y, font, paint, underline);
                y += lineHeight;
                line.Clear();
                line.Append(word);
            }
            else
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
        }

        if (line.Length > 0)
        {
            DrawLine(canvas, line.ToString(), x, y, font, paint, underline);
            y += lineHeight;
        }

        return y;
    }

    private static void DrawLine(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint, bool underline)
    {
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        if (!underline) return;

        var metrics            = font.Metrics;
        var underlineY         = y + (metrics.UnderlinePosition ?? font.Size * 0.1f);
        var underlineThickness = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
        using var linePaint    = new SKPaint { Color = paint.Color, StrokeWidth = underlineThickness, IsAntialias = true };
        canvas.DrawLine(x, underlineY, x + font.MeasureText(text), underlineY, linePaint);
    }
}
