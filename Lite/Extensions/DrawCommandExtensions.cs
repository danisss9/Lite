using System.Globalization;
using AngleSharp.Css;
using AngleSharp.Css.Values;
using Lite.Layout;
using Lite.Models;
using Lite.Rendering;
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
public enum TextTransform   { None, Uppercase, Lowercase, Capitalize }
public enum BorderStyle     { None, Solid, Dotted, Dashed, Double, Groove, Ridge, Inset, Outset, Hidden }
public enum ListStyleType   { Disc, Circle, Square, Decimal, DecimalLeadingZero, LowerAlpha, UpperAlpha, LowerRoman, UpperRoman, None }
public enum ListStylePosition { Outside, Inside }
public enum VerticalAlignType { Baseline, Top, Middle, Bottom, TextTop, TextBottom, Sub, Super }

public static class StyleExtensions
{
    public static DisplayType GetDisplay(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.Display, out var ov)
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
        var raw = node.TryResolveStyle(PropertyNames.FlexDirection, out var ov)
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
        var raw = node.TryResolveStyle(PropertyNames.FlexWrap, out var ov)
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
        var raw = node.TryResolveStyle(PropertyNames.JustifyContent, out var ov)
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
        var raw = node.TryResolveStyle(PropertyNames.AlignItems, out var ov)
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
        var raw = node.TryResolveStyle(PropertyNames.AlignSelf, out var ov)
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
        var raw = node.TryResolveStyle("align-content", out var ov)
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
        var raw = node.TryResolveStyle("order", out var ov)
            ? ov : node.Style.GetPropertyValue("order");
        return int.TryParse(raw, out var v) ? v : 0;
    }

    public static Visibility GetVisibility(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.Visibility, out var ov)
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
        if (node.TryResolveStyle("min-width", out var ov) && ov.Trim() is not ("auto" or ""))
            return false;
        var raw = node.Style.GetProperty(PropertyNames.MinWidth).RawValue;
        return raw is null or Constant<Length>;
    }

    /// <summary>Returns true when min-height is auto/unset (not explicitly set to a length).</summary>
    public static bool IsAutoMinHeight(this LayoutNode node)
    {
        if (node.TryResolveStyle("min-height", out var ov) && ov.Trim() is not ("auto" or ""))
            return false;
        var raw = node.Style.GetProperty(PropertyNames.MinHeight).RawValue;
        return raw is null or Constant<Length>;
    }

    public static float GetFlexGrow(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.FlexGrow, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexGrow);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    public static float GetFlexShrink(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.FlexShrink, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexShrink);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1f;
    }

    /// <summary>Returns flex-basis in px, or float.NaN for 'auto'/'content'.</summary>
    public static float GetFlexBasis(this LayoutNode node, float containerMain)
    {
        var raw = node.TryResolveStyle(PropertyNames.FlexBasis, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FlexBasis);
        if (string.IsNullOrEmpty(raw) || raw == "auto" || raw == "content") return float.NaN;
        if (TryEvalCalc(raw, containerMain, 16f, containerMain, out var calcPx)) return calcPx;
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
            if (node.TryResolveStyle(name, out var ov) && !string.IsNullOrEmpty(ov))
            {
                ov = ov.Trim();
                if (TryEvalCalc(ov, total, fontSize, total, out var calcPx)) return calcPx;
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
        var weight = node.TryResolveStyle(PropertyNames.FontWeight, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.FontWeight);
        return weight is "bold" or "bolder" or "700" or "800" or "900";
    }

    public static bool GetFontItalic(this LayoutNode node)
    {
        var style = node.TryResolveStyle(PropertyNames.FontStyle, out var ov)
            ? ov
            : node.Style.GetPropertyValue(PropertyNames.FontStyle);
        return style is "italic" or "oblique";
    }

    public static bool IsLineThrough(this LayoutNode node)
    {
        var line = node.TryResolveStyle(PropertyNames.TextDecorationLine, out var ov1)
            ? ov1 : node.Style.GetPropertyValue(PropertyNames.TextDecorationLine);
        if (line.Contains("line-through", StringComparison.OrdinalIgnoreCase)) return true;
        var dec = node.TryResolveStyle(PropertyNames.TextDecoration, out var ov2)
            ? ov2 : node.Style.GetPropertyValue(PropertyNames.TextDecoration);
        return dec.Contains("line-through", StringComparison.OrdinalIgnoreCase);
    }

    public static TextAlign GetTextAlign(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.TextAlign, out var ov)
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
        if (node.TryResolveStyle(PropertyNames.LineHeight, out var ov))
        {
            ov = ov.Trim();
            if (TryEvalCalc(ov, fontSize, fontSize, fontSize, out var calcPx)) return calcPx;
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
        var raw = node.TryResolveStyle(PropertyNames.WhiteSpace, out var ov)
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

    public static PositionType GetPosition(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.Position, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.Position);
        return raw switch
        {
            "relative" => PositionType.Relative,
            "absolute" => PositionType.Absolute,
            "fixed"    => PositionType.Fixed,
            _          => PositionType.Static,
        };
    }

    public static bool IsPositioned(this LayoutNode node) =>
        node.GetPosition() != PositionType.Static;

    public static OverflowType GetOverflow(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.Overflow, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.Overflow);
        return raw switch
        {
            "hidden" => OverflowType.Hidden,
            "scroll" => OverflowType.Scroll,
            "auto"   => OverflowType.Auto,
            _        => OverflowType.Visible,
        };
    }

    public static int GetZIndex(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.ZIndex, out var ov)
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
        var raw = node.TryResolveStyle("border-radius", out var ov)
            ? ov : node.Style.GetPropertyValue("border-radius");
        if (string.IsNullOrWhiteSpace(raw)) return (0f, 0f);
        raw = raw.Trim();
        if (TryEvalCalc(raw, width, 16f, width, out var calcPx)) { var rc = Math.Max(0f, calcPx); return (rc, rc); }
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
        var raw = node.TryResolveStyle("opacity", out var ov)
            ? ov : node.Style.GetPropertyValue("opacity");
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? Math.Clamp(f, 0f, 1f) : 1f;
    }

    public static FloatType GetFloat(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("float", out var ov)
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
        var raw = node.TryResolveStyle("clear", out var ov)
            ? ov : node.Style.GetPropertyValue("clear");
        return raw switch
        {
            "left"  => ClearType.Left,
            "right" => ClearType.Right,
            "both"  => ClearType.Both,
            _       => ClearType.None,
        };
    }

    public static TextTransform GetTextTransform(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("text-transform", out var ov)
            ? ov : node.Style.GetPropertyValue("text-transform");
        return raw switch
        {
            "uppercase"  => TextTransform.Uppercase,
            "lowercase"  => TextTransform.Lowercase,
            "capitalize" => TextTransform.Capitalize,
            _            => TextTransform.None,
        };
    }

    public static float GetLetterSpacing(this LayoutNode node, float fontSize = 16)
    {
        var raw = node.TryResolveStyle("letter-spacing", out var ov)
            ? ov : node.Style.GetPropertyValue("letter-spacing");
        if (string.IsNullOrWhiteSpace(raw) || raw == "normal") return 0f;
        raw = raw.Trim();
        if (raw.EndsWith("px") && float.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)) return px;
        if (raw.EndsWith("em") && float.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em)) return em * fontSize;
        return 0f;
    }

    public static float GetWordSpacing(this LayoutNode node, float fontSize = 16)
    {
        var raw = node.TryResolveStyle("word-spacing", out var ov)
            ? ov : node.Style.GetPropertyValue("word-spacing");
        if (string.IsNullOrWhiteSpace(raw) || raw == "normal") return 0f;
        raw = raw.Trim();
        if (raw.EndsWith("px") && float.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)) return px;
        if (raw.EndsWith("em") && float.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em)) return em * fontSize;
        return 0f;
    }

    public static float GetTextIndent(this LayoutNode node, float totalWidth = 0, float fontSize = 16)
        => GetSizeOrDefault(node, "text-indent", totalWidth, fontSize, 0f);

    public static BorderStyle GetBorderStyleTop(this LayoutNode node) => GetBorderStyleSide(node, "border-top-style");
    public static BorderStyle GetBorderStyleRight(this LayoutNode node) => GetBorderStyleSide(node, "border-right-style");
    public static BorderStyle GetBorderStyleBottom(this LayoutNode node) => GetBorderStyleSide(node, "border-bottom-style");
    public static BorderStyle GetBorderStyleLeft(this LayoutNode node) => GetBorderStyleSide(node, "border-left-style");

    private static BorderStyle GetBorderStyleSide(LayoutNode node, string prop)
    {
        var raw = node.TryResolveStyle(prop, out var ov)
            ? ov : node.Style.GetPropertyValue(prop);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Try shorthand 'border-style'
            raw = node.TryResolveStyle("border-style", out var ov2)
                ? ov2 : node.Style.GetPropertyValue("border-style");
        }
        return raw?.Trim() switch
        {
            "dotted" => BorderStyle.Dotted,
            "dashed" => BorderStyle.Dashed,
            "double" => BorderStyle.Double,
            "groove" => BorderStyle.Groove,
            "ridge"  => BorderStyle.Ridge,
            "inset"  => BorderStyle.Inset,
            "outset" => BorderStyle.Outset,
            "hidden" => BorderStyle.Hidden,
            "none"   => BorderStyle.None,
            "solid"  => BorderStyle.Solid,
            _        => BorderStyle.Solid,
        };
    }

    public static ListStyleType GetListStyleType(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("list-style-type", out var ov)
            ? ov : node.Style.GetPropertyValue("list-style-type");
        // Also check parent (UL/OL) if not set on LI
        if (string.IsNullOrWhiteSpace(raw) && node.Parent != null)
        {
            raw = node.Parent.TryResolveStyle("list-style-type", out var ov2)
                ? ov2 : node.Parent.Style.GetPropertyValue("list-style-type");
        }
        return raw?.Trim() switch
        {
            "circle"               => ListStyleType.Circle,
            "square"               => ListStyleType.Square,
            "decimal"              => ListStyleType.Decimal,
            "decimal-leading-zero" => ListStyleType.DecimalLeadingZero,
            "lower-alpha" or "lower-latin" => ListStyleType.LowerAlpha,
            "upper-alpha" or "upper-latin" => ListStyleType.UpperAlpha,
            "lower-roman"          => ListStyleType.LowerRoman,
            "upper-roman"          => ListStyleType.UpperRoman,
            "none"                 => ListStyleType.None,
            _                      => ListStyleType.Disc,
        };
    }

    public static ListStylePosition GetListStylePosition(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("list-style-position", out var ov)
            ? ov : node.Style.GetPropertyValue("list-style-position");
        if (string.IsNullOrWhiteSpace(raw) && node.Parent != null)
        {
            raw = node.Parent.TryResolveStyle("list-style-position", out var ov2)
                ? ov2 : node.Parent.Style.GetPropertyValue("list-style-position");
        }
        return raw?.Trim() switch
        {
            "inside" => ListStylePosition.Inside,
            _        => ListStylePosition.Outside,
        };
    }

    public static VerticalAlignType GetVerticalAlign(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("vertical-align", out var ov)
            ? ov : node.Style.GetPropertyValue("vertical-align");
        return raw?.Trim() switch
        {
            "top"         => VerticalAlignType.Top,
            "middle"      => VerticalAlignType.Middle,
            "bottom"      => VerticalAlignType.Bottom,
            "text-top"    => VerticalAlignType.TextTop,
            "text-bottom" => VerticalAlignType.TextBottom,
            "sub"         => VerticalAlignType.Sub,
            "super"       => VerticalAlignType.Super,
            _             => VerticalAlignType.Baseline,
        };
    }

    // ---- Outline properties ----

    public static float GetOutlineWidth(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("outline-width", out var ov)
            ? ov : node.Style.GetPropertyValue("outline-width");
        if (string.IsNullOrWhiteSpace(raw)) return 0f;
        raw = raw.Trim();
        return raw switch
        {
            "thin"   => 1f,
            "medium" => 3f,
            "thick"  => 5f,
            _ => raw.EndsWith("px") && float.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? px : 0f,
        };
    }

    public static SKColor GetOutlineColor(this LayoutNode node)
        => GetColor(node, "outline-color", SKColors.Black);

    public static BorderStyle GetOutlineStyle(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("outline-style", out var ov)
            ? ov : node.Style.GetPropertyValue("outline-style");
        return raw?.Trim() switch
        {
            "dotted" => BorderStyle.Dotted,
            "dashed" => BorderStyle.Dashed,
            "double" => BorderStyle.Double,
            "groove" => BorderStyle.Groove,
            "ridge"  => BorderStyle.Ridge,
            "inset"  => BorderStyle.Inset,
            "outset" => BorderStyle.Outset,
            "solid"  => BorderStyle.Solid,
            _        => BorderStyle.None,
        };
    }

    public static float GetOutlineOffset(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("outline-offset", out var ov)
            ? ov : node.Style.GetPropertyValue("outline-offset");
        if (string.IsNullOrWhiteSpace(raw)) return 0f;
        raw = raw.Trim();
        if (raw.EndsWith("px") && float.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)) return px;
        return 0f;
    }

    // ---- Background image properties ----

    public static string? GetBackgroundImage(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("background-image", out var ov)
            ? ov : node.Style.GetPropertyValue("background-image");
        if (string.IsNullOrWhiteSpace(raw) || raw == "none") return null;
        // Extract url('...') or url(...)
        raw = raw.Trim();
        if (raw.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = raw[4..];
            if (inner.EndsWith(')')) inner = inner[..^1];
            return inner.Trim().Trim('"', '\'');
        }
        return null;
    }

    public static string GetBackgroundRepeat(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("background-repeat", out var ov)
            ? ov : node.Style.GetPropertyValue("background-repeat");
        if (string.IsNullOrWhiteSpace(raw)) return "repeat";
        return raw.Trim();
    }

    public static (string X, string Y) GetBackgroundPosition(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("background-position", out var ov)
            ? ov : node.Style.GetPropertyValue("background-position");
        if (string.IsNullOrWhiteSpace(raw)) return ("0%", "0%");
        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var x = parts.Length > 0 ? parts[0] : "0%";
        var y = parts.Length > 1 ? parts[1] : "50%";
        return (x, y);
    }

    public static (string W, string H) GetBackgroundSize(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("background-size", out var ov)
            ? ov : node.Style.GetPropertyValue("background-size");
        if (string.IsNullOrWhiteSpace(raw) || raw == "auto") return ("auto", "auto");
        if (raw.Trim() == "cover") return ("cover", "cover");
        if (raw.Trim() == "contain") return ("contain", "contain");
        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var w = parts.Length > 0 ? parts[0] : "auto";
        var h = parts.Length > 1 ? parts[1] : "auto";
        return (w, h);
    }

    /// <summary>Applies text-transform to a string.</summary>
    public static string ApplyTextTransform(string text, TextTransform transform)
    {
        return transform switch
        {
            TextTransform.Uppercase => text.ToUpperInvariant(),
            TextTransform.Lowercase => text.ToLowerInvariant(),
            TextTransform.Capitalize => CapitalizeText(text),
            _ => text,
        };
    }

    private static string CapitalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.ToCharArray();
        bool newWord = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i])) { newWord = true; }
            else if (newWord) { chars[i] = char.ToUpperInvariant(chars[i]); newWord = false; }
        }
        return new string(chars);
    }

    /// <summary>Parses all box-shadow layers. Returns empty list when unset.</summary>
    public static List<BoxShadow> GetBoxShadows(this LayoutNode node)
    {
        var raw = node.TryResolveStyle("box-shadow", out var ov)
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
        var raw = node.TryResolveStyle("text-shadow", out var ov)
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
        if (node.TryResolveStyle(prop, out var ov))
        {
            ov = ov.Trim();
            if (ov == "auto") return float.NaN;
            if (TryEvalCalc(ov, total, size, total, out var calcPx)) return calcPx;
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

    public static bool IsUnderline(this LayoutNode node)
    {
        var line = node.TryResolveStyle(PropertyNames.TextDecorationLine, out var ov1)
            ? ov1 : node.Style.GetPropertyValue(PropertyNames.TextDecorationLine);
        if (line.Contains("underline", StringComparison.OrdinalIgnoreCase)) return true;
        var dec = node.TryResolveStyle(PropertyNames.TextDecoration, out var ov2)
            ? ov2 : node.Style.GetPropertyValue(PropertyNames.TextDecoration);
        return dec.Contains("underline", StringComparison.OrdinalIgnoreCase);
    }

    public static CursorType GetCursor(this LayoutNode node)
    {
        var raw = node.TryResolveStyle(PropertyNames.Cursor, out var ov)
            ? ov : node.Style.GetPropertyValue(PropertyNames.Cursor);
        return raw switch
        {
            "pointer" => CursorType.Pointer,
            "text"    => CursorType.Text,
            _         => CursorType.Default,
        };
    }

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
        if (node.TryResolveStyle(propertyName, out var overrideValue))
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

        // hsl(h, s%, l%) / hsla(h, s%, l%, a)
        if (lower.StartsWith("hsl") && lower.Contains('(') && lower.EndsWith(')'))
        {
            return Rendering.SvgRenderer.ParseHsl(lower);
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
        var vp = viewportSize >= 0 ? viewportSize : total;
        // Check StyleOverrides first
        if (node.TryResolveStyle(propertyName, out var overrideStr))
        {
            overrideStr = overrideStr.Trim();
            if (overrideStr is "" or "auto" or "none") return defaultValue;
            if (TryEvalCalc(overrideStr, total, size, vp, out var calcPx)) return calcPx;
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
        var vp = viewportSize >= 0 ? viewportSize : total;
        // Inline style override (e.g. from element.style.setProperty)
        if (node.TryResolveStyle(propertyName, out var overrideStr))
        {
            overrideStr = overrideStr.Trim();
            if (TryEvalCalc(overrideStr, total, size, vp, out var calcPx))
                return calcPx;
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
            Length.Unit.Em      => (float)length.Value * size,
            Length.Unit.Px      => (float)length.Value,
            Length.Unit.Vw      => (float)length.Value / 100f * vp,
            Length.Unit.Vh      => (float)length.Value / 100f * vp,
            Length.Unit.Percent => (float)length.Value / 100f * total,
            _ => size
        };
    }

    // ── calc() evaluator ──────────────────────────────────────────────────────
    //
    // Grammar (recursive descent):
    //   sum     = product ( ('+' | '-') product )*
    //   product = unary   ( ('*' | '/') unary   )*
    //   unary   = '-' unary | '+' unary | primary
    //   primary = VALUE | '(' sum ')'
    //
    // Units resolved at leaf time:  px em rem % vw vh  — and bare numbers (unitless).

    /// <summary>
    /// Evaluates a CSS <c>calc()</c> expression string to a pixel value.
    /// Returns false when <paramref name="raw"/> does not start with <c>calc(</c>.
    /// </summary>
    internal static bool TryEvalCalc(string raw, float total, float em, float vp, out float result)
    {
        result = 0f;
        if (string.IsNullOrEmpty(raw)) return false;
        raw = raw.Trim();
        if (!raw.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)) return false;

        var inner = StripCalcWrapper(raw);
        if (inner is null) return false;

        // Flatten nested calc() calls into plain parentheses
        inner = System.Text.RegularExpressions.Regex.Replace(
            inner, @"calc\s*\(", "(", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        try
        {
            var tokens = CalcTokenize(inner);
            int pos    = 0;
            result = CalcSum(tokens, ref pos, total, em, vp);
            return float.IsFinite(result);
        }
        catch { return false; }
    }

    private static string? StripCalcWrapper(string raw)
    {
        int start = raw.IndexOf('(');
        if (start < 0) return null;
        int depth = 0;
        for (int i = start; i < raw.Length; i++)
        {
            if      (raw[i] == '(') depth++;
            else if (raw[i] == ')') { depth--; if (depth == 0) return raw[(start + 1)..i].Trim(); }
        }
        return null;
    }

    // ── Tokenizer ─────────────────────────────────────────────────────────────

    private enum CalcTokType { Value, Plus, Minus, Star, Slash, LParen, RParen }
    private readonly record struct CalcToken(CalcTokType Type, float Number = 0f, string Unit = "");

    private static List<CalcToken> CalcTokenize(string expr)
    {
        var tokens = new List<CalcToken>();
        int i = 0;
        while (i < expr.Length)
        {
            if (char.IsWhiteSpace(expr[i])) { i++; continue; }
            switch (expr[i])
            {
                case '+': tokens.Add(new CalcToken(CalcTokType.Plus));   i++; break;
                case '-': tokens.Add(new CalcToken(CalcTokType.Minus));  i++; break;
                case '*': tokens.Add(new CalcToken(CalcTokType.Star));   i++; break;
                case '/': tokens.Add(new CalcToken(CalcTokType.Slash));  i++; break;
                case '(': tokens.Add(new CalcToken(CalcTokType.LParen)); i++; break;
                case ')': tokens.Add(new CalcToken(CalcTokType.RParen)); i++; break;
                default:
                    if (char.IsDigit(expr[i]) || expr[i] == '.')
                    {
                        int s = i;
                        while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                        float.TryParse(expr[s..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
                        int us = i;
                        while (i < expr.Length && (char.IsLetter(expr[i]) || expr[i] == '%')) i++;
                        tokens.Add(new CalcToken(CalcTokType.Value, num, expr[us..i].ToLowerInvariant()));
                    }
                    else i++;
                    break;
            }
        }
        return tokens;
    }

    // ── Recursive descent ─────────────────────────────────────────────────────

    private static float CalcSum(List<CalcToken> t, ref int p, float total, float em, float vp)
    {
        var v = CalcProduct(t, ref p, total, em, vp);
        while (p < t.Count && t[p].Type is CalcTokType.Plus or CalcTokType.Minus)
        {
            var op = t[p++].Type;
            var r  = CalcProduct(t, ref p, total, em, vp);
            v = op == CalcTokType.Plus ? v + r : v - r;
        }
        return v;
    }

    private static float CalcProduct(List<CalcToken> t, ref int p, float total, float em, float vp)
    {
        var v = CalcUnary(t, ref p, total, em, vp);
        while (p < t.Count && t[p].Type is CalcTokType.Star or CalcTokType.Slash)
        {
            var op = t[p++].Type;
            var r  = CalcUnary(t, ref p, total, em, vp);
            v = op == CalcTokType.Star ? v * r : (r == 0f ? float.NaN : v / r);
        }
        return v;
    }

    private static float CalcUnary(List<CalcToken> t, ref int p, float total, float em, float vp)
    {
        if (p < t.Count && t[p].Type == CalcTokType.Minus) { p++; return -CalcPrimary(t, ref p, total, em, vp); }
        if (p < t.Count && t[p].Type == CalcTokType.Plus)  { p++; return  CalcPrimary(t, ref p, total, em, vp); }
        return CalcPrimary(t, ref p, total, em, vp);
    }

    private static float CalcPrimary(List<CalcToken> t, ref int p, float total, float em, float vp)
    {
        if (p >= t.Count) return 0f;
        if (t[p].Type == CalcTokType.LParen)
        {
            p++;
            var v = CalcSum(t, ref p, total, em, vp);
            if (p < t.Count && t[p].Type == CalcTokType.RParen) p++;
            return v;
        }
        if (t[p].Type == CalcTokType.Value)
        {
            var tok = t[p++];
            return tok.Unit switch
            {
                "px"  or "" => tok.Number,
                "%"         => tok.Number / 100f * total,
                "em"        => tok.Number * em,
                "rem"       => tok.Number * 16f,
                "vw"        => tok.Number / 100f * vp,
                "vh"        => tok.Number / 100f * vp,
                _           => tok.Number,
            };
        }
        return 0f;
    }
}