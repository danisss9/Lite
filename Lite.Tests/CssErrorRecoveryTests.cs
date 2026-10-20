using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §4.2 error recovery in the vendored AngleSharp.Css (patch 001): malformed
/// statements and declarations are skipped observing strings and the matching pairs (), [] and
/// {}; a BAD_STRING or BAD_URI invalidates its construct. Each case is lifted from the official
/// suite (at-rule-*, uri-*), where the unpatched parser resynchronised at the wrong token.</summary>
public static class CssErrorRecoveryTests
{
    private static ICssStyleSheet Parse(string css) => new CssParser().ParseStyleSheet(css);

    private static string? Declared(ICssStyleSheet sheet, string selector, string property)
    {
        string? value = null;
        foreach (var rule in sheet.Rules)
            if (rule is ICssStyleRule style && style.SelectorText == selector &&
                style.Style.GetPropertyValue(property) is { Length: > 0 } v)
                value = v;
        return value;
    }

    [Test]
    public static void PercentageRgb_ClampsToTheGamut()
    {
        // Patch 002: 101% clips to 255 (the unclamped byte cast wrapped it to 2); rounding is
        // upstream's (colors-001 needs 50% to be #80, i.e. CSS 'green' at 0% 50% 0%).
        var sheet = Parse("a { color: rgb(50%, 1%, 101%) } b { color: rgb(-5%, 100%, 0%) }");
        Equal("rgba(128, 3, 255, 1)", Declared(sheet, "a", "color"));
        Equal("rgba(0, 255, 0, 1)", Declared(sheet, "b", "color"));
    }

    [Test]
    public static void StrayAtSign_DropsTheMalformedRuleSetThroughItsBlock()
    {
        // at-rule-001: '@ import' is not an at-keyword, so everything up to the end of the
        // following block is one malformed rule set — the div rule must not survive the ';'.
        var sheet = Parse("@ import \"x.css\";\ndiv { color: red }\n* { color: green }");
        Equal(null, Declared(sheet, "div", "color"));
        Equal("rgba(0, 128, 0, 1)", Declared(sheet, "*", "color"));
    }

    [Test]
    public static void UnclosedUrl_IsABadUriAndDropsTheDeclaration()
    {
        // uri-003: url("x"; never closes, so it is not a URI token.
        var sheet = Parse("div { background: url(\"red_box.png\";\n}");
        Equal(null, Declared(sheet, "div", "background-image"));
        // uri-016: whitespace inside an unquoted url( makes a BAD_URI up to the ')'.
        sheet = Parse(".one { background: red url(test/* test); color: green; }");
        Equal(null, Declared(sheet, ".one", "background-color"));
        Equal("rgba(0, 128, 0, 1)", Declared(sheet, ".one", "color"));
    }

    [Test]
    public static void BadString_DropsOnlyItsDeclaration()
    {
        var sheet = Parse("p { color: red; color: \"broken\n; background-color: green }");
        Equal("rgba(255, 0, 0, 1)", Declared(sheet, "p", "color"));
        Equal("rgba(0, 128, 0, 1)", Declared(sheet, "p", "background-color"));
    }

    [Test]
    public static void UnknownAtRule_PreludeObservesBracketPairs()
    {
        // at-rule-013: the ';' inside [ ] does not end @foo; the one after the ( ) group does.
        var sheet = Parse("@media all { #a { color: green; } @foo [; #a { color: red; } ] (; } ) ; #b { color: green; } }");
        var media = (ICssMediaRule)sheet.Rules[0];
        var selectors = media.Rules.OfType<ICssStyleRule>().Select(r => r.SelectorText).ToArray();
        Equal("#a,#b", string.Join(",", selectors));
        Equal("rgba(0, 128, 0, 1)", media.Rules.OfType<ICssStyleRule>().First().Style.GetPropertyValue("color"));
    }

    [Test]
    public static void ComponentScan_RejectsMalformedValues()
    {
        True(CssComponentScan.IsWellFormedValue("red url(a.png) no-repeat"), "a plain value is well formed");
        True(CssComponentScan.IsWellFormedValue("url( \"a b.png\" )"), "a quoted url with spaces is a URI token");
        True(!CssComponentScan.IsWellFormedValue("url(\"a.png\";"), "an unclosed url( is a BAD_URI");
        True(!CssComponentScan.IsWellFormedValue("url(a b.png)"), "whitespace inside an unquoted url is a BAD_URI");
        True(!CssComponentScan.IsWellFormedValue("\"broken\nred"), "a newline ends a BAD_STRING");
        True(!CssComponentScan.IsWellFormedValue("red; color: blue"), "a top-level ';' ends the value");
    }
}
