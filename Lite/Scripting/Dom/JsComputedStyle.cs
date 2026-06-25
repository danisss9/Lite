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
        // Resolved/used values for layout properties come from the computed box (CSSOM returns
        // used values for these — e.g. an auto margin resolves to its used pixel length).
        switch (property)
        {
            case "margin-top": return Px(_node.Box.Margin.Top);
            case "margin-right": return Px(_node.Box.Margin.Right);
            case "margin-bottom": return Px(_node.Box.Margin.Bottom);
            case "margin-left": return Px(_node.Box.Margin.Left);
            case "padding-top": return Px(_node.Box.Padding.Top);
            case "padding-right": return Px(_node.Box.Padding.Right);
            case "padding-bottom": return Px(_node.Box.Padding.Bottom);
            case "padding-left": return Px(_node.Box.Padding.Left);
            case "border-top-width": return Px(_node.Box.Border.Top);
            case "border-right-width": return Px(_node.Box.Border.Right);
            case "border-bottom-width": return Px(_node.Box.Border.Bottom);
            case "border-left-width": return Px(_node.Box.Border.Left);
            case "width": return Px(_node.Box.ContentBox.Width);
            case "height": return Px(_node.Box.ContentBox.Height);
        }
        // Otherwise: overrides first, then declared style.
        if (_node.TryResolveStyle(property, out var val)) return val;
        return _node.Style.GetPropertyValueSafe(property) ?? "";
    }

    private static string Px(float v) =>
        v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "px";

    // Common properties
    public string display => getPropertyValue("display");
    public string color => getPropertyValue("color");
    public string backgroundColor => getPropertyValue("background-color");
    public string width => getPropertyValue("width");
    public string height => getPropertyValue("height");
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
