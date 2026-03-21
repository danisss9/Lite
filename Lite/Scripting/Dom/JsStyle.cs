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
        return old;
    }

    private string Get(string property) => _node.StyleOverrides.GetValueOrDefault(property, string.Empty);

    private void Set(string property, string value)
    {
        if (string.IsNullOrEmpty(value))
            _node.StyleOverrides.Remove(property);
        else
            _node.StyleOverrides[property] = value;
    }
}
