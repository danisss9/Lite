using Lite.Extensions;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>Read-only CSSStyleDeclaration returned by getComputedStyle().</summary>
public class JsComputedStyle
{
    private readonly LayoutNode _node;
    public JsComputedStyle(LayoutNode node) => _node = node;

    public string getPropertyValue(string property)
    {
        // Check overrides first, then declared style
        if (_node.TryResolveStyle(property, out var val)) return val;
        return _node.Style.GetPropertyValue(property) ?? "";
    }

    // Common properties
    public string display => getPropertyValue("display");
    public string color => getPropertyValue("color");
    public string backgroundColor => getPropertyValue("background-color");
    public string width => _node.Box.ContentBox.Width + "px";
    public string height => _node.Box.ContentBox.Height + "px";
    public string fontSize => getPropertyValue("font-size");
    public string fontWeight => getPropertyValue("font-weight");
    public string fontFamily => getPropertyValue("font-family");
    public string fontStyle => getPropertyValue("font-style");
    public string lineHeight => getPropertyValue("line-height");
    public string textAlign => getPropertyValue("text-align");
    public string textDecoration => getPropertyValue("text-decoration");
    public string position => getPropertyValue("position");
    public string top => getPropertyValue("top");
    public string left => getPropertyValue("left");
    public string right => getPropertyValue("right");
    public string bottom => getPropertyValue("bottom");
    public string margin => getPropertyValue("margin");
    public string marginTop => getPropertyValue("margin-top");
    public string marginRight => getPropertyValue("margin-right");
    public string marginBottom => getPropertyValue("margin-bottom");
    public string marginLeft => getPropertyValue("margin-left");
    public string padding => getPropertyValue("padding");
    public string paddingTop => getPropertyValue("padding-top");
    public string paddingRight => getPropertyValue("padding-right");
    public string paddingBottom => getPropertyValue("padding-bottom");
    public string paddingLeft => getPropertyValue("padding-left");
    public string borderTopWidth => getPropertyValue("border-top-width");
    public string borderRightWidth => getPropertyValue("border-right-width");
    public string borderBottomWidth => getPropertyValue("border-bottom-width");
    public string borderLeftWidth => getPropertyValue("border-left-width");
    public string overflow => getPropertyValue("overflow");
    public string visibility => getPropertyValue("visibility");
    public string opacity => getPropertyValue("opacity");
    public string zIndex => getPropertyValue("z-index");
    public string cursor => getPropertyValue("cursor");
    public string boxShadow => getPropertyValue("box-shadow");
    public string borderRadius => getPropertyValue("border-radius");
    public string transform => getPropertyValue("transform");
    public string transition => getPropertyValue("transition");
    public string animation => getPropertyValue("animation");
}
