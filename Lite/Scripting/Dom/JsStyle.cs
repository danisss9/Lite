using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>CSS style declaration proxy — writes into LayoutNode.StyleOverrides.</summary>
public class JsStyle
{
    private readonly LayoutNode _node;
    public JsStyle(LayoutNode node) => _node = node;

    // ---- colour ----
    public string color { get => Get("color"); set => Set("color", value); }
    public string backgroundColor { get => Get("background-color"); set => Set("background-color", value); }
    public string opacity { get => Get("opacity"); set => Set("opacity", value); }
    public string borderRadius { get => Get("border-radius"); set => Set("border-radius", value); }
    public string boxShadow { get => Get("box-shadow"); set => Set("box-shadow", value); }
    public string textShadow { get => Get("text-shadow"); set => Set("text-shadow", value); }

    // ---- display / visibility ----
    public string display { get => Get("display"); set => Set("display", value); }
    public string visibility { get => Get("visibility"); set => Set("visibility", value); }
    public string overflow { get => Get("overflow"); set => Set("overflow", value); }
    public string position { get => Get("position"); set => Set("position", value); }
    public string zIndex { get => Get("z-index"); set => Set("z-index", value); }
    public string cssFloat { get => Get("float"); set => Set("float", value); }
    public string clear { get => Get("clear"); set => Set("clear", value); }

    // ---- text ----
    public string fontSize { get => Get("font-size"); set => Set("font-size", value); }
    public string fontWeight { get => Get("font-weight"); set => Set("font-weight", value); }
    public string fontFamily { get => Get("font-family"); set => Set("font-family", value); }
    public string fontStyle { get => Get("font-style"); set => Set("font-style", value); }
    public string textAlign { get => Get("text-align"); set => Set("text-align", value); }
    public string textDecoration { get => Get("text-decoration"); set => Set("text-decoration", value); }
    public string lineHeight { get => Get("line-height"); set => Set("line-height", value); }
    public string letterSpacing { get => Get("letter-spacing"); set => Set("letter-spacing", value); }
    public string whiteSpace { get => Get("white-space"); set => Set("white-space", value); }

    // ---- box ----
    public string width { get => Get("width"); set => Set("width", value); }
    public string height { get => Get("height"); set => Set("height", value); }
    public string minWidth { get => Get("min-width"); set => Set("min-width", value); }
    public string maxWidth { get => Get("max-width"); set => Set("max-width", value); }
    public string minHeight { get => Get("min-height"); set => Set("min-height", value); }
    public string maxHeight { get => Get("max-height"); set => Set("max-height", value); }

    // ---- margin ----
    public string margin { get => Get("margin"); set => Set("margin", value); }
    public string marginTop { get => Get("margin-top"); set => Set("margin-top", value); }
    public string marginRight { get => Get("margin-right"); set => Set("margin-right", value); }
    public string marginBottom { get => Get("margin-bottom"); set => Set("margin-bottom", value); }
    public string marginLeft { get => Get("margin-left"); set => Set("margin-left", value); }

    // ---- padding ----
    public string padding { get => Get("padding"); set => Set("padding", value); }
    public string paddingTop { get => Get("padding-top"); set => Set("padding-top", value); }
    public string paddingRight { get => Get("padding-right"); set => Set("padding-right", value); }
    public string paddingBottom { get => Get("padding-bottom"); set => Set("padding-bottom", value); }
    public string paddingLeft { get => Get("padding-left"); set => Set("padding-left", value); }

    // ---- border ----
    public string border { get => Get("border"); set => Set("border", value); }
    public string borderWidth { get => Get("border-width"); set => Set("border-width", value); }
    public string borderColor { get => Get("border-color"); set => Set("border-color", value); }
    public string borderStyle { get => Get("border-style"); set => Set("border-style", value); }

    // ---- positioning ----
    public string top { get => Get("top"); set => Set("top", value); }
    public string left { get => Get("left"); set => Set("left", value); }
    public string right { get => Get("right"); set => Set("right", value); }
    public string bottom { get => Get("bottom"); set => Set("bottom", value); }

    // ---- flex ----
    public string flexDirection { get => Get("flex-direction"); set => Set("flex-direction", value); }
    public string justifyContent { get => Get("justify-content"); set => Set("justify-content", value); }
    public string alignItems { get => Get("align-items"); set => Set("align-items", value); }
    public string flexGrow { get => Get("flex-grow"); set => Set("flex-grow", value); }
    public string flexShrink { get => Get("flex-shrink"); set => Set("flex-shrink", value); }
    public string flexBasis { get => Get("flex-basis"); set => Set("flex-basis", value); }
    public string gap { get => Get("gap"); set => Set("gap", value); }

    // ---- transform / animation ----
    public string transform { get => Get("transform"); set => Set("transform", value); }
    public string transition { get => Get("transition"); set => Set("transition", value); }
    public string animation { get => Get("animation"); set => Set("animation", value); }

    // ---- cursor ----
    public string cursor { get => Get("cursor"); set => Set("cursor", value); }

    // ---- content (::before/::after) ----
    public string content { get => Get("content"); set => Set("content", value); }

    // ---- multi-column ----
    public string columnCount { get => Get("column-count"); set => Set("column-count", value); }
    public string columnWidth { get => Get("column-width"); set => Set("column-width", value); }
    public string columnGap { get => Get("column-gap"); set => Set("column-gap", value); }

    // ---- cssText ----
    public string cssText
    {
        get => string.Join("; ", _node.StyleOverrides.Select(kv => $"{kv.Key}: {kv.Value}"));
        set
        {
            _node.StyleOverrides.Clear();
            _node.CascadeAppliedProps.Clear();
            foreach (var declaration in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = declaration.Split(':', 2);
                if (parts.Length == 2)
                    Set(parts[0].Trim(), parts[1].Trim());
            }
        }
    }

    /// <summary>Generic setter — mirrors the JS <c>element.style.setProperty()</c> method.</summary>
    public void setProperty(string property, string value, string? priority = null) => Set(property, value);

    public string getPropertyValue(string property) => Get(property);

    public string removeProperty(string property)
    {
        var old = Get(property);
        _node.StyleOverrides.Remove(property);
        foreach (var (longhand, _) in Expand(property, Get(property)))
            _node.StyleOverrides.Remove(longhand);
        _node.CascadeAppliedProps.Remove(property);
        return old;
    }

    private string Get(string property) => _node.StyleOverrides.GetValueOrDefault(property, string.Empty);

    private void Set(string property, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _node.StyleOverrides.Remove(property);
            foreach (var (longhand, _) in Expand(property, Get(property)))
                _node.StyleOverrides.Remove(longhand);
        }
        else
        {
            _node.StyleOverrides[property] = value;
            // The cascade store keeps shorthands expanded into longhands; every reader (border
            // widths/colors, margins, the table's §17.6.2.1 conflict resolution) queries the
            // longhands, so a shorthand mutation must expand or it never shows — the
            // border-collapse-dynamic families read stale green through exactly this gap.
            foreach (var (longhand, longhandValue) in Expand(property, value))
                _node.StyleOverrides[longhand] = longhandValue;
        }
        // This is now an author *inline* style: drop any cascade marking so a later
        // re-resolution treats it as inline (normal rules can't override it; it isn't
        // retracted) per CSS 2.1 §6.4.3.
        _node.CascadeAppliedProps.Remove(property);
    }

    private static readonly Dictionary<string, string[]> ShorthandLonghands = new(StringComparer.Ordinal)
    {
        ["border-color"] = ["border-top-color", "border-right-color", "border-bottom-color", "border-left-color"],
        ["border-width"] = ["border-top-width", "border-right-width", "border-bottom-width", "border-left-width"],
        ["border-style"] = ["border-top-style", "border-right-style", "border-bottom-style", "border-left-style"],
        ["margin"] = ["margin-top", "margin-right", "margin-bottom", "margin-left"],
        ["padding"] = ["padding-top", "padding-right", "padding-bottom", "padding-left"],
    };

    private static readonly string[] BorderStyleKeywords =
        ["none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset"];
    private static readonly string[] BorderWidthKeywords = ["thin", "medium", "thick"];

    /// <summary>Splits on top-level whitespace (parentheses protect rgb(...) components).</summary>
    private static List<string> TopLevelParts(string value)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = -1;
        for (var i = 0; i <= value.Length; i++)
        {
            var atEnd = i == value.Length;
            if (!atEnd && (value[i] == '(' || value[i] == '[')) depth++;
            else if (!atEnd && (value[i] == ')' || value[i] == ']')) depth--;
            if (!atEnd && depth == 0 && (value[i] == ' ' || value[i] == '\t'))
            {
                if (start >= 0) { parts.Add(value[start..i]); start = -1; }
            }
            else if (start < 0 && !atEnd) start = i;
        }
        if (start >= 0) parts.Add(value[start..]);
        return parts;
    }

    /// <summary>The 1–4 value expansion (§8.3/§8.5): top right→bottom=top, bottom→top,
    /// left→right; four explicit slots otherwise.</summary>
    private static (string Top, string Right, string Bottom, string Left) Slots(List<string> parts) =>
        parts.Count switch
        {
            1 => (parts[0], parts[0], parts[0], parts[0]),
            2 => (parts[0], parts[1], parts[0], parts[1]),
            3 => (parts[0], parts[1], parts[2], parts[1]),
            _ => (parts[0], parts[1], parts[2], parts[3]),
        };

    /// <summary>Expands a shorthand mutation into the longhands the cascade readers query.
    /// 'border' follows §8.5.3's reset: each unstated component resets (width medium,
    /// style none, colour currentcolor) — el.style.border='yellow' on a bordered box must
    /// drop the width and style like a browser, not stack a colour onto them.</summary>
    internal static IEnumerable<(string Property, string Value)> Expand(string property, string value)
    {
        if (ShorthandLonghands.TryGetValue(property, out var longhands))
        {
            var parts = TopLevelParts(value);
            if (parts.Count == 0 || parts.Count > 4) yield break;
            var (top, right, bottom, left) = Slots(parts);
            yield return (longhands[0], top);
            yield return (longhands[1], right);
            yield return (longhands[2], bottom);
            yield return (longhands[3], left);
            yield break;
        }
        if (property == "border")
        {
            string? width = null, style = null, color = null;
            foreach (var part in TopLevelParts(value))
            {
                var lower = part.Trim().ToLowerInvariant();
                if (BorderStyleKeywords.Contains(lower)) style = part;
                else if (BorderWidthKeywords.Contains(lower) ||
                         char.IsDigit(lower[0]) || lower[0] == '.' || lower[0] == '-')
                    width = part;
                else color = part;
            }
            var sides = new[] { "top", "right", "bottom", "left" };
            foreach (var side in sides)
            {
                yield return ($"border-{side}-width", width ?? "medium");
                yield return ($"border-{side}-style", style ?? "none");
                yield return ($"border-{side}-color", color ?? "currentcolor");
            }
            yield break;
        }
    }
}
