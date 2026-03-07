using AngleSharp.Css;
using AngleSharp.Css.Values;
using Lite.Models;
using SkiaSharp;

namespace Lite.Extensions;

public static class DrawCommandExtensions
{
    public static SKColor GetBackgroundColor(this DrawCommand command) => GetColor(command, PropertyNames.BackgroundColor, SKColors.Transparent);
    public static SKColor GetColor(this DrawCommand command) => GetColor(command, PropertyNames.Color, SKColors.Black);
    public static float GetFontSize(this DrawCommand command) => GetSize(command, PropertyNames.FontSize, size: 16);
    public static float GetHeight(this DrawCommand command, float total = 0, float size = 0) => GetSize(command, PropertyNames.Height, total, size);
    public static float GetMarginLeft(this DrawCommand command, float total = 0, float size = 0) => GetSize(command, PropertyNames.MarginLeft, total, size);
    public static float GetMarginTop(this DrawCommand command, float total = 0, float size = 0) => GetSize(command, PropertyNames.MarginTop, total, size);
    public static float GetWidth(this DrawCommand command, float total = 0, float size = 0) => GetSize(command, PropertyNames.Width, total, size);

    public static bool IsUnderline(this DrawCommand command) =>
        command.CssStyleDeclaration.GetPropertyValue(PropertyNames.TextDecorationLine).Contains("underline", StringComparison.OrdinalIgnoreCase) ||
        command.CssStyleDeclaration.GetPropertyValue(PropertyNames.TextDecoration).Contains("underline", StringComparison.OrdinalIgnoreCase);

    public static CursorType GetCursor(this DrawCommand command) =>
        command.CssStyleDeclaration.GetPropertyValue(PropertyNames.Cursor) switch
        {
            "pointer" => CursorType.Pointer,
            "text"    => CursorType.Text,
            _         => CursorType.Default
        };

    public static string GetFontFamily(this DrawCommand command)
    {
        var value = command.CssStyleDeclaration.GetPropertyValue(PropertyNames.FontFamily);
        if (string.IsNullOrEmpty(value)) return "Arial";
        var first = value.Split(',')[0].Trim().Trim('"', '\'');
        return first is "system-ui" or "ui-sans-serif" or "-apple-system" ? "Segoe UI" : first;
    }
    
    private static SKColor GetColor(DrawCommand command, string propertyName, SKColor defaultColor) =>
        command.CssStyleDeclaration.GetProperty(propertyName).RawValue is Color color 
            ? new SKColor(color.R, color.G, color.B, color.A) 
            : defaultColor;

    private static float GetSize(DrawCommand command, string propertyName, float total = 0, float size = 0)
    {
        if (command.CssStyleDeclaration.GetProperty(propertyName).RawValue is Constant<Length>)
        {
            return size == 0 ? total - size : (total - size) / 2f;
        }
        
        if (command.CssStyleDeclaration.GetProperty(propertyName).RawValue is not Length length)
        {
            return size;
        }

        return length.Type switch
        {
            Length.Unit.Em => (float)length.Value * size,
            Length.Unit.Px => (float)length.Value,
            Length.Unit.Vw => (float)length.Value / 100f * total,
            Length.Unit.Vh => (float)length.Value / 100f * total,
            Length.Unit.Percent => (float)length.Value / 100f * total,
            _ => size
        };
    }
}