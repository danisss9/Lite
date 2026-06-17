namespace Lite.Layout;

/// <summary>
/// CSS property metadata for the subset Lite renders: each property's initial value and whether
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
            ["visibility"] = ("visible", true),
            ["cursor"] = ("auto", true),
            ["direction"] = ("ltr", true),

            // Non-inherited properties
            ["display"] = ("inline", false),
            ["background-color"] = ("transparent", false),
            ["background-image"] = ("none", false),
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

    public static string? InitialValue(string property) =>
        Props.TryGetValue(property, out var p) ? p.Initial : null;

    public static bool Known(string property) => Props.ContainsKey(property);
}
