using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite;

internal static class Drawer
{
    private static float _y;
    private static List<HitRegion> _hitRegions = [];

    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, LayoutNode root)
    {
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);

        canvas.Clear(new SKColor(240, 240, 242));
        _y = 64f;
        _hitRegions = [];

        PaintNode(canvas, root, width, height);

        return (bitmap.GetPixels(), _hitRegions);
    }

    private static void PaintNode(SKCanvas canvas, LayoutNode node, int width, int height)
    {
        switch (node.TagName)
        {
            case "DIV":
            {
                var rect = CalculateSizeAndPosition(node, width, height);
                using var paint = new SKPaint { Color = node.GetBackgroundColor(), IsAntialias = true };
                canvas.DrawRect(rect, paint);
                var divCursor = node.GetCursor();
                if (divCursor != CursorType.Default)
                    _hitRegions.Add(new HitRegion(rect, divCursor));
                break;
            }
            case { } h when h.StartsWith('H') && h.Length == 2:
            case "P":
            case "A":
            {
                if (!string.IsNullOrEmpty(node.Text))
                {
                    using var paint = new SKPaint
                    {
                        Color = node.GetColor(),
                        IsAntialias = true,
                    };
                    using var font = new SKFont
                    {
                        Size = node.GetFontSize(),
                        Embolden = node.TagName == "H1",
                        Typeface = SKTypeface.FromFamilyName(node.GetFontFamily())
                    };
                    const float x = 32f;
                    var yBefore = _y;
                    _y = DrawWrappedText(canvas, node.Text, x, _y, width - 64, font, paint, node.IsUnderline());
                    var textCursor = node.GetCursor();
                    if (textCursor != CursorType.Default)
                        _hitRegions.Add(new HitRegion(new SKRect(x, yBefore, x + (width - 64), _y), textCursor, node.Href));
                }
                break;
            }
        }

        foreach (var child in node.Children)
        {
            PaintNode(canvas, child, width, height);
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

        var metrics = font.Metrics;
        var underlineY = y + (metrics.UnderlinePosition ?? font.Size * 0.1f);
        var underlineThickness = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
        using var linePaint = new SKPaint { Color = paint.Color, StrokeWidth = underlineThickness, IsAntialias = true };
        canvas.DrawLine(x, underlineY, x + font.MeasureText(text), underlineY, linePaint);
    }

    private static SKRect CalculateSizeAndPosition(LayoutNode node, int width, int height)
    {
        var rectWidth = node.GetWidth(width);
        var rectHeight = node.GetHeight(height);

        var leftRect = node.GetMarginLeft(width, rectWidth);
        var topRect = node.GetMarginTop(height, rectHeight);

        return new SKRect(leftRect, topRect, leftRect + rectWidth, topRect + rectHeight);
    }
}