using AngleSharp.Css;
using AngleSharp.Css.Values;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;

namespace Lite.Extensions;

public static class StyleExtensions
{
    public static SKColor GetBackgroundColor(this LayoutNode node) => GetColor(node, PropertyNames.BackgroundColor, SKColors.Transparent);
    public static SKColor GetColor(this LayoutNode node) => GetColor(node, PropertyNames.Color, SKColors.Black);
    public static float GetFontSize(this LayoutNode node) => GetSize(node, PropertyNames.FontSize, size: 16);
    public static float GetHeight(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.Height, total, size);
    public static float GetWidth(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.Width, total, size);

    // Margins
    public static float GetMarginTop(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.MarginTop, total, size);
    public static float GetMarginRight(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.MarginRight, total, size);
    public static float GetMarginBottom(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.MarginBottom, total, size);
    public static float GetMarginLeft(this LayoutNode node, float total = 0, float size = 0) => GetSize(node, PropertyNames.MarginLeft, total, size);

    public static EdgeSizes GetMargin(this LayoutNode node, float totalWidth = 0, float totalHeight = 0, float fontSize = 16) => new()
    {
        Top    = GetSize(node, PropertyNames.MarginTop,    totalHeight, fontSize),
        Right  = GetSize(node, PropertyNames.MarginRight,  totalWidth,  fontSize),
        Bottom = GetSize(node, PropertyNames.MarginBottom, totalHeight, fontSize),
        Left   = GetSize(node, PropertyNames.MarginLeft,   totalWidth,  fontSize),
    };

    // Padding
    public static EdgeSizes GetPadding(this LayoutNode node, float totalWidth = 0, float totalHeight = 0, float fontSize = 16) => new()
    {
        Top    = GetSize(node, PropertyNames.PaddingTop,    totalHeight, fontSize),
        Right  = GetSize(node, PropertyNames.PaddingRight,  totalWidth,  fontSize),
        Bottom = GetSize(node, PropertyNames.PaddingBottom, totalHeight, fontSize),
        Left   = GetSize(node, PropertyNames.PaddingLeft,   totalWidth,  fontSize),
    };

    // Border widths
    public static EdgeSizes GetBorderWidth(this LayoutNode node) => new()
    {
        Top    = GetBorderSideWidth(node, PropertyNames.BorderTopWidth),
        Right  = GetBorderSideWidth(node, PropertyNames.BorderRightWidth),
        Bottom = GetBorderSideWidth(node, PropertyNames.BorderBottomWidth),
        Left   = GetBorderSideWidth(node, PropertyNames.BorderLeftWidth),
    };

    public static SKColor GetBorderTopColor(this LayoutNode node)    => GetColor(node, PropertyNames.BorderTopColor,    SKColors.Black);
    public static SKColor GetBorderRightColor(this LayoutNode node)   => GetColor(node, PropertyNames.BorderRightColor,  SKColors.Black);
    public static SKColor GetBorderBottomColor(this LayoutNode node)  => GetColor(node, PropertyNames.BorderBottomColor, SKColors.Black);
    public static SKColor GetBorderLeftColor(this LayoutNode node)    => GetColor(node, PropertyNames.BorderLeftColor,   SKColors.Black);

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

    private static SKColor GetColor(LayoutNode node, string propertyName, SKColor defaultColor)
    {
        if (node.StyleOverrides.TryGetValue(propertyName, out var overrideValue))
        {
            var parsed = ParseCssColor(overrideValue);
            if (parsed.HasValue) return parsed.Value;
        }

        return node.Style.GetProperty(propertyName).RawValue is Color color
            ? new SKColor(color.R, color.G, color.B, color.A)
            : defaultColor;
    }

    private static SKColor? ParseCssColor(string value)
    {
        value = value.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        // Hex via SkiaSharp (#rgb, #rrggbb, #rgba, #rrggbbaa)
        if (value.StartsWith('#') && SKColor.TryParse(value, out var hexColor))
            return hexColor;

        // rgb(r, g, b) / rgba(r, g, b, a)
        var lower = value.ToLowerInvariant();
        if (lower.StartsWith("rgb") && lower.Contains('(') && lower.EndsWith(')'))
        {
            var inner = lower[(lower.IndexOf('(') + 1)..^1];
            var parts = inner.Split(',');
            if (parts.Length >= 3 &&
                byte.TryParse(parts[0].Trim(), out var r) &&
                byte.TryParse(parts[1].Trim(), out var g) &&
                byte.TryParse(parts[2].Trim(), out var b))
            {
                byte a = 255;
                if (parts.Length == 4 && float.TryParse(parts[3].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var alpha))
                    a = (byte)(alpha * 255);
                return new SKColor(r, g, b, a);
            }
        }

        // Named colours
        return lower switch
        {
            "red"         => SKColors.Red,
            "green"       => new SKColor(0, 128, 0),
            "blue"        => SKColors.Blue,
            "black"       => SKColors.Black,
            "white"       => SKColors.White,
            "gray"        => SKColors.Gray,
            "grey"        => SKColors.Gray,
            "yellow"      => SKColors.Yellow,
            "orange"      => new SKColor(255, 165, 0),
            "purple"      => new SKColor(128, 0, 128),
            "pink"        => new SKColor(255, 192, 203),
            "cyan"        => SKColors.Cyan,
            "magenta"     => SKColors.Magenta,
            "lime"        => new SKColor(0, 255, 0),
            "navy"        => new SKColor(0, 0, 128),
            "teal"        => new SKColor(0, 128, 128),
            "silver"      => new SKColor(192, 192, 192),
            "maroon"      => new SKColor(128, 0, 0),
            "transparent" => SKColors.Transparent,
            _             => null
        };
    }

    private static float GetBorderSideWidth(LayoutNode node, string propertyName)
    {
        var raw = node.Style.GetProperty(propertyName).RawValue;
        if (raw is Length borderLength && borderLength.Type == Length.Unit.Px)
            return (float)borderLength.Value;
        return 0f;
    }

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