using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>CSS style declaration proxy — writes into LayoutNode.StyleOverrides.</summary>
public class JsStyle
{
    private readonly LayoutNode _node;
    public JsStyle(LayoutNode node) => _node = node;

    // ---- colour ----
    public string color
    {
        get => _node.StyleOverrides.GetValueOrDefault("color", string.Empty);
        set => Set("color", value);
    }
    public string backgroundColor
    {
        get => _node.StyleOverrides.GetValueOrDefault("background-color", string.Empty);
        set => Set("background-color", value);
    }

    public string opacity
    {
        get => _node.StyleOverrides.GetValueOrDefault("opacity", string.Empty);
        set => Set("opacity", value);
    }
    public string borderRadius
    {
        get => _node.StyleOverrides.GetValueOrDefault("border-radius", string.Empty);
        set => Set("border-radius", value);
    }

    // ---- display / visibility ----
    public string display
    {
        get => _node.StyleOverrides.GetValueOrDefault("display", string.Empty);
        set => Set("display", value);
    }

    // ---- text ----
    public string fontSize
    {
        get => _node.StyleOverrides.GetValueOrDefault("font-size", string.Empty);
        set => Set("font-size", value);
    }
    public string fontWeight
    {
        get => _node.StyleOverrides.GetValueOrDefault("font-weight", string.Empty);
        set => Set("font-weight", value);
    }

    // ---- box ----
    public string width
    {
        get => _node.StyleOverrides.GetValueOrDefault("width", string.Empty);
        set => Set("width", value);
    }
    public string height
    {
        get => _node.StyleOverrides.GetValueOrDefault("height", string.Empty);
        set => Set("height", value);
    }

    /// <summary>Generic setter — mirrors the JS <c>element.style.setProperty()</c> method.</summary>
    public void setProperty(string property, string value) => Set(property, value);

    private void Set(string property, string value)
    {
        if (string.IsNullOrEmpty(value))
            _node.StyleOverrides.Remove(property);
        else
            _node.StyleOverrides[property] = value;
    }
}
