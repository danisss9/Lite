using AngleSharp.Css;
using AngleSharp.Css.Values;
using Lite.Models;
using SkiaSharp;

namespace Lite.Extensions;

public static class StyleExtensions
{
    public static SKColor GetBackgroundColor(this LayoutNode node) => GetColor(node, PropertyNames.BackgroundColor, SKColors.Transparent);
    public static SKColor GetColor(this LayoutNode node) => GetColor(node, PropertyNames.Color, SKColors.Black);
    public static float GetFontSize(this LayoutNode node) => GetSize(node, PropertyNames.FontSize, size: 16);
    public static float GetHeight(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.Height, total, size);
    public static float GetMarginLeft(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.MarginLeft, total, size);
    public static float GetMarginTop(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.MarginTop, total, size);
    public static float GetWidth(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.Width, total, size);

    public static bool IsUnderline(this LayoutNode node) =>
        node.Style.GetPropertyValue(PropertyNames.TextDecorationLine).Contains("underline", StringComparison.OrdinalIgnoreCase) ||
        node.Style.GetPropertyValue(PropertyNames.TextDecoration).Contains("underline", StringComparison.OrdinalIgnoreCase);

    public static CursorType GetCursor(this LayoutNode node) =>
        node.Style.GetPropertyValue(PropertyNames.Cursor) switch
        {
            "pointer" => CursorType.Pointer,
            "text"    => CursorType.Text,
            _         => CursorType.Default
        };

    public static string GetFontFamily(this LayoutNode node)
    {
        var value = node.Style.GetPropertyValue(PropertyNames.FontFamily);
        if (string.IsNullOrEmpty(value)) return "Arial";
        var first = value.Split(',')[0].Trim().Trim('"', '\'');
        return first is "system-ui" or "ui-sans-serif" or "-apple-system" ? "Segoe UI" : first;
    }

    private static SKColor GetColor(LayoutNode node, string propertyName, SKColor defaultColor) =>
        node.Style.GetProperty(propertyName).RawValue is Color color
            ? new SKColor(color.R, color.G, color.B, color.A)
            : defaultColor;

    private static float GetSize(LayoutNode node, string propertyName, float total = 0, float size = 0)
    {
        if (node.Style.GetProperty(propertyName).RawValue is Constant<Length>)
        {
            return size == 0 ? total - size : (total - size) / 2f;
        }

        if (node.Style.GetProperty(propertyName).RawValue is not Length length)
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