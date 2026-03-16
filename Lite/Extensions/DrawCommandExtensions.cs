using AngleSharp.Css;
using AngleSharp.Css.Values;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;

namespace Lite.Extensions;

public enum DisplayType { Block, Inline, InlineBlock, None }

public static class StyleExtensions
{
    public static DisplayType GetDisplay(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.Display, out var ov)
            ? ov
            : node.Style.GetPropertyValue(PropertyNames.Display);
        return raw switch
        {
            "block"        => DisplayType.Block,
            "inline-block" => DisplayType.InlineBlock,
            "none"         => DisplayType.None,
            _              => DisplayType.Inline,
        };
    }

    public static bool GetFontBold(this LayoutNode node)
    {
        var weight = node.Style.GetPropertyValue(PropertyNames.FontWeight);
        return weight is "bold" or "bolder" or "700" or "800" or "900";
    }


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

        // Try RawValue (works when AngleSharp exposes the value as Color struct)
        if (node.Style.GetProperty(propertyName).RawValue is Color color)
            return new SKColor(color.R, color.G, color.B, color.A);

        // Fallback: parse the serialized CSS string value (e.g. "#3b82f6", "rgb(59,130,246)")
        var str = node.Style.GetPropertyValue(propertyName);
        if (!string.IsNullOrEmpty(str))
        {
            var parsed = ParseCssColor(str);
            if (parsed.HasValue) return parsed.Value;
        }

        return defaultColor;
    }

    private static SKColor? ParseCssColor(string value)
    {
        value = value.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        var lower = value.ToLowerInvariant();

        if (lower == "transparent") return SKColors.Transparent;

        // rgb(r, g, b) / rgba(r, g, b, a)
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

        // Hex and named colours — SkiaSharp handles all CSS named colours and hex formats
        if (SKColor.TryParse(value, out var skColor))
            return skColor;

        return null;
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
        // Inline style override (e.g. from element.style.setProperty)
        if (node.StyleOverrides.TryGetValue(propertyName, out var overrideStr))
        {
            overrideStr = overrideStr.Trim();
            if (overrideStr.EndsWith("px") && float.TryParse(overrideStr[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px))
                return px;
            if (overrideStr.EndsWith("em") && float.TryParse(overrideStr[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var em))
                return em * size;
            if (overrideStr.EndsWith('%') && float.TryParse(overrideStr[..^1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
                return pct / 100f * total;
        }

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