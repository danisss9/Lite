namespace Lite.Layout;

/// <summary>
/// CSS 2.1 longhand metadata: each property's initial value and whether
/// it inherits. Backs the cascade-wide keywords `initial` (→ initial value), `inherit` (→ parent
/// value), and `unset` (→ inherit if the property inherits, else initial) for styles set via JS
/// or applied by <see cref="StyleResolver"/>. (AngleSharp already resolves these for parsed CSS.)
/// </summary>
internal static class PropertyTable
{
    private static readonly Dictionary<string, (string Initial, bool Inherited)> Props =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Inherited properties
            ["color"] = ("black", true),
            ["font-size"] = ("16px", true),
            ["font-family"] = ("", true),
            ["font-weight"] = ("normal", true),
            ["font-style"] = ("normal", true),
            ["font-variant"] = ("normal", true),
            ["line-height"] = ("normal", true),
            ["letter-spacing"] = ("normal", true),
            ["word-spacing"] = ("normal", true),
            ["text-align"] = ("left", true),
            ["text-indent"] = ("0", true),
            ["text-transform"] = ("none", true),
            ["white-space"] = ("normal", true),
            ["list-style-type"] = ("disc", true),
            ["list-style-position"] = ("outside", true),
            ["list-style-image"] = ("none", true),
            ["border-collapse"] = ("separate", true),
            ["border-spacing"] = ("0", true),
            ["caption-side"] = ("top", true),
            ["empty-cells"] = ("show", true),
            ["quotes"] = ("\"\\\"\" \"\\\"\" \"'\" \"'\"", true),
            ["orphans"] = ("2", true),
            ["widows"] = ("2", true),
            ["visibility"] = ("visible", true),
            ["cursor"] = ("auto", true),
            ["direction"] = ("ltr", true),

            // Non-inherited properties
            ["display"] = ("inline", false),
            ["background-color"] = ("transparent", false),
            ["background-image"] = ("none", false),
            ["background-attachment"] = ("scroll", false),
            ["background-position"] = ("0% 0%", false),
            ["background-repeat"] = ("repeat", false),
            ["clip"] = ("auto", false),
            ["content"] = ("normal", false),
            ["counter-increment"] = ("none", false),
            ["counter-reset"] = ("none", false),
            ["table-layout"] = ("auto", false),
            ["text-decoration"] = ("none", false),
            ["unicode-bidi"] = ("normal", false),
            ["vertical-align"] = ("baseline", false),
            ["outline-color"] = ("invert", false),
            ["outline-style"] = ("none", false),
            ["outline-width"] = ("medium", false),
            ["page-break-before"] = ("auto", false),
            ["page-break-after"] = ("auto", false),
            ["page-break-inside"] = ("auto", false),
            ["border-top-color"] = ("currentcolor", false),
            ["border-right-color"] = ("currentcolor", false),
            ["border-bottom-color"] = ("currentcolor", false),
            ["border-left-color"] = ("currentcolor", false),
            ["border-top-style"] = ("none", false),
            ["border-right-style"] = ("none", false),
            ["border-bottom-style"] = ("none", false),
            ["border-left-style"] = ("none", false),
            ["opacity"] = ("1", false),
            ["position"] = ("static", false),
            ["float"] = ("none", false),
            ["clear"] = ("none", false),
            ["overflow"] = ("visible", false),
            ["z-index"] = ("auto", false),
            ["width"] = ("auto", false),
            ["height"] = ("auto", false),
            ["min-width"] = ("0", false),
            ["min-height"] = ("0", false),
            ["max-width"] = ("none", false),
            ["max-height"] = ("none", false),
            ["margin-top"] = ("0", false),
            ["margin-right"] = ("0", false),
            ["margin-bottom"] = ("0", false),
            ["margin-left"] = ("0", false),
            ["padding-top"] = ("0", false),
            ["padding-right"] = ("0", false),
            ["padding-bottom"] = ("0", false),
            ["padding-left"] = ("0", false),
            ["border-top-width"] = ("medium", false),
            ["border-right-width"] = ("medium", false),
            ["border-bottom-width"] = ("medium", false),
            ["border-left-width"] = ("medium", false),
            ["top"] = ("auto", false),
            ["right"] = ("auto", false),
            ["bottom"] = ("auto", false),
            ["left"] = ("auto", false),
        };

    public static bool IsInherited(string property) =>
        Props.TryGetValue(property, out var p) && p.Inherited;

    internal static IEnumerable<string> InheritedProperties => Props.Where(p => p.Value.Inherited).Select(p => p.Key);

    public static string? InitialValue(string property) =>
        Props.TryGetValue(property, out var p) ? p.Initial : null;

    public static bool Known(string property) => Props.ContainsKey(property);
}
