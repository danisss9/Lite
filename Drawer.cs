using AngleSharp.Css.Dom;
using SkiaSharp;
using AngleSharp.Css;
using AngleSharp.Css.Values;

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
                    using var paint = new SKPaint() { Color = GetColor(drawCommand), IsAntialias = true };
                    canvas.DrawRect(rect, paint);
                    break;
                }
                case { } h when h.StartsWith('H') && h.Length == 2:
                case "P":
                {
                    using var paint = new SKPaint
                    {
                        Color = SKColors.Black,
                        IsAntialias = true,
                    };
                    using var font = new SKFont
                    {
                        Size = GetFontSize(drawCommand),
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
        var style = command.CssStyleDeclaration;

        var rectWidth = GetValue(style.GetWidth(), width);
        var rectHeight = GetValue(style.GetHeight(), height);

        var leftRect = GetValue(style.GetMarginLeft(), width, rectWidth);
        var topRect = GetValue(style.GetMarginTop(), height, rectHeight);

        return new SKRect(leftRect, topRect, leftRect + rectWidth, topRect + rectHeight);
    }

    private static float GetValue(string css, float total = 0, float size = 0)
    {
        if(string.IsNullOrEmpty(css))
        {
            return 200;
        }
        else if (css == "auto")
        {
            return size == 0 ? (total - size) : ((total - size) / 2f);
        } 
        else 
        {
            return float.TryParse(css.Replace("px", ""), out var value) ? value : 0;
        }
    }

    private static float GetFontSize(DrawCommand command)
    {
        var style = command.CssStyleDeclaration;
        var font = style.GetFontSize();

        if (string.IsNullOrEmpty(font))
        {
            return 16;
        }
        else if (font.Contains("em"))
        {
            return float.TryParse(font.Replace("em", ""), out var value) ? (value * 16) : 0;
        }
        else
        {
            return float.TryParse(font.Replace("px", ""), out var value) ? value : 0;
        }
    }

    private static SKColor GetColor(DrawCommand command, bool isBackgroundColor = false) =>
        command.CssStyleDeclaration.GetProperty(isBackgroundColor ? PropertyNames.BackgroundColor : PropertyNames.Color).RawValue is Color color 
            ? new SKColor(color.R, color.G, color.B, color.A) 
            : SKColors.Transparent;
}

internal record DrawCommand(string? Id, string TagName, string Text, ICssStyleDeclaration CssStyleDeclaration);