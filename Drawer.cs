using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite;

internal static class Drawer
{
    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, IEnumerable<DrawCommand> drawCommands)
    {
        // Create an SKImageInfo that matches the client area.
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Draw the scene into an offscreen SKBitmap.
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);

        // Fill the background with blue.
        canvas.Clear(new SKColor(240, 240, 242));
        var y = 64f;
        var hitRegions = new List<HitRegion>();

        foreach (var drawCommand in drawCommands) {
            switch (drawCommand.TagName)
            {
                case "DIV":
                {
                    var rect = CalculateSizeAndPosition(drawCommand, width, height);
                    using var paint = new SKPaint { Color = drawCommand.GetBackgroundColor(), IsAntialias = true };
                    canvas.DrawRect(rect, paint);
                    var divCursor = drawCommand.GetCursor();
                    if (divCursor != CursorType.Default)
                        hitRegions.Add(new HitRegion(rect, divCursor));
                    break;
                }
                case { } h when h.StartsWith('H') && h.Length == 2:
                case "P":
                case "A":
                {
                    using var paint = new SKPaint
                    {
                        Color = drawCommand.GetColor(),
                        IsAntialias = true,
                    };
                    using var font = new SKFont
                    {
                        Size = drawCommand.GetFontSize(),
                        Embolden = drawCommand.TagName == "H1",
                        Typeface = SKTypeface.FromFamilyName(drawCommand.GetFontFamily())
                    };
                    const float x = 32f;
                    var yBefore = y;
                    y = DrawWrappedText(canvas, drawCommand.Text, x, y, width - 64, font, paint, drawCommand.IsUnderline());
                    var textCursor = drawCommand.GetCursor();
                    if (textCursor != CursorType.Default)
                        hitRegions.Add(new HitRegion(new SKRect(x, yBefore, x + (width - 64), y), textCursor, drawCommand.Href));
                    break;
                }
            }
        }

        return (bitmap.GetPixels(), hitRegions);
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

    private static SKRect CalculateSizeAndPosition(DrawCommand command, int width, int height)
    {
        var rectWidth = command.GetWidth(width);
        var rectHeight = command.GetHeight(height);

        var leftRect = command.GetMarginLeft(width, rectWidth);
        var topRect = command.GetMarginTop(height, rectHeight);

        return new SKRect(leftRect, topRect, leftRect + rectWidth, topRect + rectHeight);
    }
}