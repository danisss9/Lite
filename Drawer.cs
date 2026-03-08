using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;

namespace Lite;

internal static class Drawer
{
    private static float _y;
    private static List<HitRegion> _hitRegions = [];

    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, LayoutNode root, Viewport viewport)
    {
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);

        canvas.Clear(new SKColor(240, 240, 242));
        _y = 64f;
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
            {
                var fontSize = node.GetFontSize();
                var margin  = node.GetMargin(width, height, fontSize);
                var padding = node.GetPadding(width, height, fontSize);
                var border  = node.GetBorderWidth();

                var rectWidth  = node.GetWidth(width);
                var rectHeight = node.GetHeight(height);

                // Position content box using margin offsets
                var left = node.GetMarginLeft(width, rectWidth);
                var top  = node.GetMarginTop(height, rectHeight);

                var box = new BoxDimensions
                {
                    ContentBox = new SKRect(left, top, left + rectWidth, top + rectHeight),
                    Margin     = margin,
                    Padding    = padding,
                    Border     = border,
                };
                node.Box = box;

                // Fill background over padding box
                var bgColor = node.GetBackgroundColor();
                if (bgColor != SKColors.Transparent)
                {
                    using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
                    canvas.DrawRect(box.PaddingBox, bgPaint);
                }

                // Draw four borders
                DrawBorders(canvas, box, node);

                var divCursor = node.GetCursor();
                if (divCursor != CursorType.Default)
                    _hitRegions.Add(new HitRegion(box.BorderBox, divCursor));
                break;
            }
            case { } h when h.StartsWith('H') && h.Length == 2:
            case "P":
            case "A":
            {
                if (!string.IsNullOrEmpty(node.Text))
                {
                    var fontSize = node.GetFontSize();
                    var padding  = node.GetPadding(width, height, fontSize);
                    var border   = node.GetBorderWidth();

                    using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
                    using var font = new SKFont
                    {
                        Size     = fontSize,
                        Embolden = node.TagName == "H1",
                        Typeface = SKTypeface.FromFamilyName(node.GetFontFamily()),
                    };

                    // Text starts inset by left padding + border, with max width reduced accordingly
                    var x        = 32f + padding.Left + border.Left;
                    var maxWidth = width - 64 - padding.Left - padding.Right - border.Left - border.Right;
                    var yBefore  = _y + padding.Top + border.Top;

                    _y = DrawWrappedText(canvas, node.Text, x, yBefore, maxWidth, font, paint, node.IsUnderline());
                    _y += padding.Bottom + border.Bottom;

                    var textCursor = node.GetCursor();
                    if (textCursor != CursorType.Default)
                        _hitRegions.Add(new HitRegion(new SKRect(32f, yBefore - padding.Top, 32f + (width - 64), _y), textCursor, node.Href));
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

        var metrics   = font.Metrics;
        var underlineY = y + (metrics.UnderlinePosition ?? font.Size * 0.1f);
        var underlineThickness = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
        using var linePaint = new SKPaint { Color = paint.Color, StrokeWidth = underlineThickness, IsAntialias = true };
        canvas.DrawLine(x, underlineY, x + font.MeasureText(text), underlineY, linePaint);
    }
}