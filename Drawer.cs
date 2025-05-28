using SkiaSharp;
using Lite.Extensions;
using Lite.Models;

namespace Lite;

internal static class Drawer
{
    public static IntPtr Draw(int width, int height, IEnumerable<DrawCommand> drawCommands)
    {
        // Create an SKImageInfo that matches the client area.
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Draw the scene into an offscreen SKBitmap.
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);
            
        // Fill the background with blue.
        canvas.Clear(new SKColor(240, 240, 242));
        var y = 64;

        foreach (var drawCommand in drawCommands) {
            switch (drawCommand.TagName)
            {
                case "DIV":
                {
                    var rect = CalculateSizeAndPosition(drawCommand, width, height);
                    using var paint = new SKPaint() { Color = drawCommand.GetBackgroundColor(), IsAntialias = true };
                    canvas.DrawRect(rect, paint);
                    break;
                }
                case { } h when h.StartsWith('H') && h.Length == 2:
                case "P":
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
                        Typeface = SKTypeface.FromFamilyName("Arial")
                    };
                    canvas.DrawText(drawCommand.Text, 32, y, SKTextAlign.Left, font, paint);
                    y += 48;
                    break;
                }
            }
        }

        // Get pointer to the pixel data.
        return bitmap.GetPixels();
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