using System.Globalization;
using AngleSharp.Css;
using AngleSharp.Css.Values;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;

namespace Lite.Extensions;

/// <summary>One layer of a box-shadow declaration.</summary>
public record struct BoxShadow(float OffsetX, float OffsetY, float Blur, float Spread, SKColor Color, bool Inset);

/// <summary>A text-shadow declaration.</summary>
public record struct TextShadow(float OffsetX, float OffsetY, float Blur, SKColor Color);

public enum DisplayType     { Block, Inline, InlineBlock, ListItem, Flex, InlineFlex, Table, TableRow, TableCell, None }
public enum TextAlign       { Left, Center, Right, Justify }
public enum WhiteSpace      { Normal, NoWrap, Pre, PreWrap, PreLine }
public enum PositionType    { Static, Relative, Absolute, Fixed }
public enum OverflowType    { Visible, Hidden, Scroll, Auto }
public enum FlexDirection   { Row, RowReverse, Column, ColumnReverse }
public enum FlexWrap        { NoWrap, Wrap, WrapReverse }
public enum JustifyContent  { FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
public enum AlignItems      { Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum AlignSelf       { Auto, Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum AlignContent    { Stretch, FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround }
public enum Visibility      { Visible, Hidden, Collapse }
public enum FloatType       { None, Left, Right }
public enum ClearType       { None, Left, Right, Both }

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
            "list-item"    => DisplayType.ListItem,
            "flex"         => DisplayType.Flex,
            "inline-flex"  => DisplayType.InlineFlex,
            "table"        => DisplayType.Table,
            "table-row"    => DisplayType.TableRow,
            "table-cell"   => DisplayType.TableCell,
            "none"         => DisplayType.None,
            _              => DisplayType.Inline,
        };
    }

    public static FlexDirection GetFlexDirection(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.FlexDirection, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexDirection);
        return raw switch
        {
            "row-reverse"    => FlexDirection.RowReverse,
            "column"         => FlexDirection.Column,
            "column-reverse" => FlexDirection.ColumnReverse,
            _                => FlexDirection.Row,
        };
    }

    public static FlexWrap GetFlexWrap(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.FlexWrap, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexWrap);
        return raw switch
        {
            "wrap"         => FlexWrap.Wrap,
            "wrap-reverse" => FlexWrap.WrapReverse,
            _              => FlexWrap.NoWrap,
        };
    }

    public static JustifyContent GetJustifyContent(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.JustifyContent, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.JustifyContent);
        return raw switch
        {
            "flex-end"      => JustifyContent.FlexEnd,
            "center"        => JustifyContent.Center,
            "space-between" => JustifyContent.SpaceBetween,
            "space-around"  => JustifyContent.SpaceAround,
            "space-evenly"  => JustifyContent.SpaceEvenly,
            _               => JustifyContent.FlexStart,
        };
    }

    public static AlignItems GetAlignItems(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.AlignItems, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.AlignItems);
        return raw switch
        {
            "flex-start" => AlignItems.FlexStart,
            "flex-end"   => AlignItems.FlexEnd,
            "center"     => AlignItems.Center,
            "baseline"   => AlignItems.Baseline,
            _            => AlignItems.Stretch,
        };
    }

    public static AlignSelf GetAlignSelf(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.AlignSelf, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.AlignSelf);
        return raw switch
        {
            "flex-start" => AlignSelf.FlexStart,
            "flex-end"   => AlignSelf.FlexEnd,
            "center"     => AlignSelf.Center,
            "baseline"   => AlignSelf.Baseline,
            "stretch"    => AlignSelf.Stretch,
            _            => AlignSelf.Auto,
        };
    }

    public static AlignContent GetAlignContent(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("align-content", out var ov)
            ? ov : node.Style.GetPropertyValue("align-content");
        return raw switch
        {
            "flex-start"    => AlignContent.FlexStart,
            "flex-end"      => AlignContent.FlexEnd,
            "center"        => AlignContent.Center,
            "space-between" => AlignContent.SpaceBetween,
            "space-around"  => AlignContent.SpaceAround,
            _               => AlignContent.Stretch,
        };
    }

    public static int GetOrder(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("order", out var ov)
            ? ov : node.Style.GetPropertyValue("order");
        return int.TryParse(raw, out var v) ? v : 0;
    }

    public static Visibility GetVisibility(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.Visibility, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.Visibility);
        return raw switch
        {
            "hidden"   => Visibility.Hidden,
            "collapse" => Visibility.Collapse,
            _          => Visibility.Visible,
        };
    }

    public static float GetMinWidth(this LayoutNode node, float total = 0, float size = 0)
        => GetSizeOrDefault(node, PropertyNames.MinWidth, total, size, 0f);
    public static float GetMaxWidth(this LayoutNode node, float total = 0, float size = 0)
        => GetSizeOrDefault(node, PropertyNames.MaxWidth, total, size, float.PositiveInfinity);
    public static float GetMinHeight(this LayoutNode node, float total = 0, float size = 0)
        => GetSizeOrDefault(node, PropertyNames.MinHeight, total, size, 0f);
    public static float GetMaxHeight(this LayoutNode node, float total = 0, float size = 0)
        => GetSizeOrDefault(node, PropertyNames.MaxHeight, total, size, float.PositiveInfinity);

    public static bool IsAutoMarginTop(this LayoutNode node) =>
        node.Style.GetProperty(PropertyNames.MarginTop).RawValue is Constant<Length>;
    public static bool IsAutoMarginBottom(this LayoutNode node) =>
        node.Style.GetProperty(PropertyNames.MarginBottom).RawValue is Constant<Length>;

    /// <summary>Returns true when min-width is auto/unset (not explicitly set to a length).</summary>
    public static bool IsAutoMinWidth(this LayoutNode node)
    {
        if (node.StyleOverrides.TryGetValue("min-width", out var ov) && ov.Trim() is not ("auto" or ""))
            return false;
        var raw = node.Style.GetProperty(PropertyNames.MinWidth).RawValue;
        return raw is null or Constant<Length>;
    }

    /// <summary>Returns true when min-height is auto/unset (not explicitly set to a length).</summary>
    public static bool IsAutoMinHeight(this LayoutNode node)
    {
        if (node.StyleOverrides.TryGetValue("min-height", out var ov) && ov.Trim() is not ("auto" or ""))
            return false;
        var raw = node.Style.GetProperty(PropertyNames.MinHeight).RawValue;
        return raw is null or Constant<Length>;
    }

    public static float GetFlexGrow(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.FlexGrow, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexGrow);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    public static float GetFlexShrink(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.FlexShrink, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexShrink);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1f;
    }

    /// <summary>Returns flex-basis in px, or float.NaN for 'auto'/'content'.</summary>
    public static float GetFlexBasis(this LayoutNode node, float containerMain)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.FlexBasis, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexBasis);
        if (string.IsNullOrEmpty(raw) || raw == "auto" || raw == "content") return float.NaN;
        if (raw.EndsWith("px") && float.TryParse(raw[..^2],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
        if (raw.EndsWith('%') && float.TryParse(raw[..^1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            // Percentage with indefinite container resolves to auto (§9.2)
            if (float.IsPositiveInfinity(containerMain) || containerMain == float.MaxValue) return float.NaN;
            return pct / 100f * containerMain;
        }
        if (node.Style.GetProperty(PropertyNames.FlexBasis).RawValue is Length l)
            return l.Type == Length.Unit.Px ? (float)l.Value : float.NaN;
        return float.NaN;
    }

    public static float GetGapRow(this LayoutNode node, float containerH, float fontSize)
        => GetGap(node, PropertyNames.RowGap, "row-gap", containerH, fontSize);
    public static float GetGapColumn(this LayoutNode node, float containerW, float fontSize)
        => GetGap(node, PropertyNames.ColumnGap, "column-gap", containerW, fontSize);

    /// <summary>Returns the gap value in px, or 0 when the property is not set.</summary>
    private static float GetGap(LayoutNode node, string prop, string fallback, float total, float fontSize)
    {
        // Check StyleOverrides first (populated from matched CSS rules)
        foreach (var name in new[] { prop, fallback })
        {
            if (node.StyleOverrides.TryGetValue(name, out var ov) && !string.IsNullOrEmpty(ov))
            {
                ov = ov.Trim();
                if (ov.EndsWith("px") && float.TryParse(ov[..^2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
                if (ov.EndsWith("em") && float.TryParse(ov[..^2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var em)) return em * fontSize;
                if (ov.EndsWith('%') && float.TryParse(ov[..^1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct)) return pct / 100f * total;
                // Plain number (px assumed)
                if (float.TryParse(ov, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var plain)) return plain;
            }
        }

        // Fallback to AngleSharp computed style
        foreach (var name in new[] { prop, "grid-row-gap", "grid-column-gap" })
        {
            var raw = node.Style.GetProperty(name).RawValue;
            if (raw is Length l)
            {
                return l.Type switch
                {
                    Length.Unit.Px      => (float)l.Value,
                    Length.Unit.Em      => (float)l.Value * fontSize,
                    Length.Unit.Percent => (float)l.Value / 100f * total,
                    _                   => 0f,
                };
            }
        }
        return 0f;
    }

    public static bool GetFontBold(this LayoutNode node)
    {
        var weight = node.Style.GetPropertyValue(PropertyNames.FontWeight);
        return weight is "bold" or "bolder" or "700" or "800" or "900";
    }

    public static bool GetFontItalic(this LayoutNode node)
    {
        var style = node.StyleOverrides.TryGetValue(PropertyNames.FontStyle, out var ov)
            ? ov
            : node.Style.GetPropertyValue(PropertyNames.FontStyle);
        return style is "italic" or "oblique";
    }

    public static bool IsLineThrough(this LayoutNode node) =>
        node.Style.GetPropertyValue(PropertyNames.TextDecorationLine).Contains("line-through", StringComparison.OrdinalIgnoreCase) ||
        node.Style.GetPropertyValue(PropertyNames.TextDecoration).Contains("line-through", StringComparison.OrdinalIgnoreCase);

    public static TextAlign GetTextAlign(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.TextAlign, out var ov)
            ? ov
            : node.Style.GetPropertyValue(PropertyNames.TextAlign);
        return raw switch
        {
            "center"  => TextAlign.Center,
            "right"   => TextAlign.Right,
            "justify" => TextAlign.Justify,
            _         => TextAlign.Left,
        };
    }

    /// <summary>Returns the computed line-height in pixels. Falls back to fontSize * 1.4.</summary>
    public static float GetLineHeight(this LayoutNode node, float fontSize)
    {
        if (node.StyleOverrides.TryGetValue(PropertyNames.LineHeight, out var ov))
        {
            ov = ov.Trim();
            if (ov.EndsWith("px") && float.TryParse(ov[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px))
                return px;
            if (float.TryParse(ov,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mult))
                return mult * fontSize;
        }

        var raw = node.Style.GetProperty(PropertyNames.LineHeight).RawValue;
        if (raw is Length lh2)
        {
            return lh2.Type switch
            {
                Length.Unit.Px      => (float)lh2.Value,
                Length.Unit.Em      => (float)lh2.Value * fontSize,
                Length.Unit.Percent => (float)lh2.Value / 100f * fontSize,
                _                   => fontSize * 1.4f,
            };
        }

        // Unitless number (e.g. line-height: 1.5) — try string parse
        var str = node.Style.GetPropertyValue(PropertyNames.LineHeight);
        if (!string.IsNullOrEmpty(str) && str != "normal" &&
            float.TryParse(str, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num * fontSize;

        return fontSize * 1.4f;
    }

    public static WhiteSpace GetWhiteSpace(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.WhiteSpace, out var ov)
            ? ov
            : node.Style.GetPropertyValue(PropertyNames.WhiteSpace);
        return raw switch
        {
            "nowrap"   => WhiteSpace.NoWrap,
            "pre"      => WhiteSpace.Pre,
            "pre-wrap" => WhiteSpace.PreWrap,
            "pre-line" => WhiteSpace.PreLine,
            _          => WhiteSpace.Normal,
        };
    }

    public static PositionType GetPosition(this LayoutNode node) =>
        node.Style.GetPropertyValue(PropertyNames.Position) switch
        {
            "relative" => PositionType.Relative,
            "absolute" => PositionType.Absolute,
            "fixed"    => PositionType.Fixed,
            _          => PositionType.Static,
        };

    public static bool IsPositioned(this LayoutNode node) =>
        node.GetPosition() != PositionType.Static;

    public static OverflowType GetOverflow(this LayoutNode node) =>
        node.Style.GetPropertyValue(PropertyNames.Overflow) switch
        {
            "hidden" => OverflowType.Hidden,
            "scroll" => OverflowType.Scroll,
            "auto"   => OverflowType.Auto,
            _        => OverflowType.Visible,
        };

    public static int GetZIndex(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue(PropertyNames.ZIndex, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.ZIndex);
        return int.TryParse(raw, out var z) ? z : 0;
    }

    /// <summary>
    /// Returns the resolved border-radius as (Rx, Ry) in pixels.
    /// Rx is relative to the element width, Ry to the element height.
    /// Returns (0, 0) when unset.
    /// </summary>
    public static (float Rx, float Ry) GetBorderRadius(this LayoutNode node, float width, float height)
    {
        var raw = node.StyleOverrides.TryGetValue("border-radius", out var ov)
            ? ov : node.Style.GetPropertyValue("border-radius");
        if (string.IsNullOrWhiteSpace(raw)) return (0f, 0f);
        raw = raw.Trim();
        if (raw.EndsWith("px") && float.TryParse(raw[..^2],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px))
        {
            var r = Math.Max(0f, px);
            return (r, r);
        }
        if (raw.EndsWith('%') && float.TryParse(raw[..^1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            var f = Math.Clamp(pct / 100f, 0f, 1f);
            return (f * width, f * height);
        }
        return (0f, 0f);
    }

    public static float GetOpacity(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("opacity", out var ov)
            ? ov : node.Style.GetPropertyValue("opacity");
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? Math.Clamp(f, 0f, 1f) : 1f;
    }

    public static FloatType GetFloat(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("float", out var ov)
            ? ov : node.Style.GetPropertyValue("float");
        return raw switch
        {
            "left"  => FloatType.Left,
            "right" => FloatType.Right,
            _       => FloatType.None,
        };
    }

    public static ClearType GetClear(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("clear", out var ov)
            ? ov : node.Style.GetPropertyValue("clear");
        return raw switch
        {
            "left"  => ClearType.Left,
            "right" => ClearType.Right,
            "both"  => ClearType.Both,
            _       => ClearType.None,
        };
    }

    /// <summary>Parses all box-shadow layers. Returns empty list when unset.</summary>
    public static List<BoxShadow> GetBoxShadows(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("box-shadow", out var ov)
            ? ov : node.Style.GetPropertyValue("box-shadow");
        if (string.IsNullOrWhiteSpace(raw) || raw == "none") return [];
        var result = new List<BoxShadow>();
        foreach (var layer in SplitShadowLayers(raw))
        {
            if (TryParseShadowLayer(layer, out var s)) result.Add(s);
        }
        return result;
    }

    /// <summary>Parses the text-shadow property. Returns null when unset.</summary>
    public static TextShadow? GetTextShadow(this LayoutNode node)
    {
        var raw = node.StyleOverrides.TryGetValue("text-shadow", out var ov)
            ? ov : node.Style.GetPropertyValue("text-shadow");
        if (string.IsNullOrWhiteSpace(raw) || raw == "none") return null;
        // text-shadow uses same token format as box-shadow but without spread/inset
        if (TryParseShadowLayer(SplitShadowLayers(raw).FirstOrDefault() ?? "", out var s))
            return new TextShadow(s.OffsetX, s.OffsetY, s.Blur, s.Color);
        return null;
    }

    // ---- shadow parsing helpers ----

    /// <summary>Splits comma-separated shadow layers, keeping commas inside functional notation intact.</summary>
    private static IEnumerable<string> SplitShadowLayers(string value)
    {
        var sb  = new System.Text.StringBuilder();
        int depth = 0;
        foreach (var c in value)
        {
            if      (c == '(') { depth++;  sb.Append(c); }
            else if (c == ')') { depth--;  sb.Append(c); }
            else if (c == ',' && depth == 0)
            {
                var layer = sb.ToString().Trim();
                if (layer.Length > 0) yield return layer;
                sb.Clear();
            }
            else sb.Append(c);
        }
        var last = sb.ToString().Trim();
        if (last.Length > 0) yield return last;
    }

    /// <summary>Tokenises one shadow layer (spaces as delimiters, functional notation kept intact).</summary>
    private static IEnumerable<string> ShadowTokens(string layer)
    {
        var sb    = new System.Text.StringBuilder();
        int depth = 0;
        foreach (var c in layer)
        {
            if (c == '(') { depth++; sb.Append(c); }
            else if (c == ')') { depth--; sb.Append(c); }
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static bool TryParseLengthPx(string token, out float px)
    {
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return float.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out px);
        // Unitless 0 is valid
        if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out px) && px == 0f)
            return true;
        px = 0;
        return false;
    }

    private static bool TryParseShadowLayer(string layer, out BoxShadow shadow)
    {
        shadow = default;
        var tokens  = ShadowTokens(layer).ToList();
        bool inset  = tokens.Remove("inset");
        var lengths = new List<float>();
        SKColor color = new SKColor(0, 0, 0, 102); // default: rgba(0,0,0,0.4)
        bool hasColor = false;
        foreach (var token in tokens)
        {
            if (TryParseLengthPx(token, out var px))
                lengths.Add(px);
            else
            {
                var c = ParseCssColor(token);
                if (c.HasValue) { color = c.Value; hasColor = true; }
            }
        }
        if (lengths.Count < 2) return false;
        shadow = new BoxShadow(
            OffsetX: lengths[0],
            OffsetY: lengths[1],
            Blur:    lengths.Count > 2 ? lengths[2] : 0f,
            Spread:  lengths.Count > 3 ? lengths[3] : 0f,
            Color:   color,
            Inset:   inset);
        return true;
    }

    /// <summary>Returns the value of a position offset property (top/right/bottom/left).
    /// Returns float.NaN when the property is 'auto' or unset.</summary>
    public static float GetOffsetTop(this LayoutNode node, float total = 0, float size = 0)    => GetOffset(node, PropertyNames.Top,    total, size);
    public static float GetOffsetRight(this LayoutNode node, float total = 0, float size = 0)  => GetOffset(node, PropertyNames.Right,  total, size);
    public static float GetOffsetBottom(this LayoutNode node, float total = 0, float size = 0) => GetOffset(node, PropertyNames.Bottom, total, size);
    public static float GetOffsetLeft(this LayoutNode node, float total = 0, float size = 0)   => GetOffset(node, PropertyNames.Left,   total, size);

    private static float GetOffset(LayoutNode node, string prop, float total, float size)
    {
        if (node.StyleOverrides.TryGetValue(prop, out var ov))
        {
            ov = ov.Trim();
            if (ov == "auto") return float.NaN;
            if (ov.EndsWith("px") && float.TryParse(ov[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
            if (ov.EndsWith('%') && float.TryParse(ov[..^1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct)) return pct / 100f * total;
        }
        if (node.Style.GetProperty(prop).RawValue is Constant<Length>) return float.NaN; // auto
        if (node.Style.GetProperty(prop).RawValue is Length l)
            return l.Type switch
            {
                Length.Unit.Px      => (float)l.Value,
                Length.Unit.Em      => (float)l.Value * size,
                Length.Unit.Percent => (float)l.Value / 100f * total,
                _                   => float.NaN,
            };
        return float.NaN;
    }

    /// <summary>Returns true when the given horizontal margin side is 'auto'.</summary>
    public static bool IsAutoMarginLeft(this LayoutNode node) =>
        node.Style.GetProperty(PropertyNames.MarginLeft).RawValue is Constant<Length>;

    public static bool IsAutoMarginRight(this LayoutNode node) =>
        node.Style.GetProperty(PropertyNames.MarginRight).RawValue is Constant<Length>;

    public static SKColor GetBackgroundColor(this LayoutNode node) => GetColor(node, PropertyNames.BackgroundColor, SKColors.Transparent);
    public static SKColor GetColor(this LayoutNode node) => GetColor(node, PropertyNames.Color, SKColors.Black);
    public static float GetFontSize(this LayoutNode node) => GetSize(node, PropertyNames.FontSize, size: 16);
    public static float GetHeight(this LayoutNode node, float total = 0, float size = 0, float viewportHeight = -1f) => GetSize(node, PropertyNames.Height, total, size, viewportHeight);
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
        return first switch
        {
            "system-ui" or "ui-sans-serif" or "-apple-system" or "BlinkMacSystemFont" => "Segoe UI",
            "monospace" or "ui-monospace" or "Courier" or "Courier New"               => "Consolas",
            _                                                                          => first,
        };
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

    /// <summary>Returns a size property value or <paramref name="defaultValue"/> when unset/auto/none.</summary>
    private static float GetSizeOrDefault(LayoutNode node, string propertyName, float total, float size, float defaultValue, float viewportSize = -1f)
    {
        // Check StyleOverrides first
        if (node.StyleOverrides.TryGetValue(propertyName, out var overrideStr))
        {
            overrideStr = overrideStr.Trim();
            if (overrideStr is "" or "auto" or "none") return defaultValue;
            if (overrideStr.EndsWith("px") && float.TryParse(overrideStr[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
            if (overrideStr.EndsWith("em") && float.TryParse(overrideStr[..^2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var em)) return em * size;
            if (overrideStr.EndsWith('%') && float.TryParse(overrideStr[..^1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct)) return pct / 100f * total;
        }

        var raw = node.Style.GetProperty(propertyName).RawValue;
        if (raw is null or Constant<Length>) return defaultValue; // auto/none/unset
        if (raw is not Length length) return defaultValue;

        var vp = viewportSize >= 0 ? viewportSize : total;
        return length.Type switch
        {
            Length.Unit.Px      => (float)length.Value,
            Length.Unit.Em      => (float)length.Value * size,
            Length.Unit.Percent => (float)length.Value / 100f * total,
            Length.Unit.Vw      => (float)length.Value / 100f * vp,
            Length.Unit.Vh      => (float)length.Value / 100f * vp,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Resolves a size CSS property.
    /// <paramref name="total"/> is used for percentage units (e.g. parent content height).
    /// <paramref name="viewportSize"/> is used for vh/vw units; falls back to <paramref name="total"/> when -1.
    /// </summary>
    private static float GetSize(LayoutNode node, string propertyName, float total = 0, float size = 0, float viewportSize = -1f)
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

        var vp = viewportSize >= 0 ? viewportSize : total;
        return length.Type switch
        {
            Length.Unit.Em      => (float)length.Value * size,
            Length.Unit.Px      => (float)length.Value,
            Length.Unit.Vw      => (float)length.Value / 100f * vp,
            Length.Unit.Vh      => (float)length.Value / 100f * vp,
            Length.Unit.Percent => (float)length.Value / 100f * total,
            _ => size
        };
    }
}