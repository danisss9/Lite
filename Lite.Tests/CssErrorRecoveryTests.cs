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

    [Test]
    public static void EscapedNewlineInString_IsRemoved()
    {
        // Patch 008, CSS 2.1 4.3.7: "the newline itself has to be escaped with a backslash. The
        // newline is subsequently removed from the string" (escapes-000, escaped-newline-001).
        // Upstream appended Environment.NewLine, which put a CR LF into the generated text.
        var sheet = Parse("p { content: \"Th\\\nis \" } q { content: 'a\\\nb' }");
        Equal("\"This \"", Declared(sheet, "p", "content"));
        Equal("\"ab\"", Declared(sheet, "q", "content"));
    }

    [Test]
    public static void EndOfSheet_ClosesOpenConstructs()
    {
        // Patch 009, CSS 2.1 4.2: "User agents must close all open constructs (for example:
        // blocks, parentheses, brackets, rules, strings, and functions) at the end of the style
        // sheet." eof-002 ends inside rgb(, uri-017 inside url(" and url(.
        Equal("rgba(0, 128, 0, 1)", Declared(Parse("div { color: rgb(0, 128, 0"), "div", "color"));
        Equal("url(\"green.png\")", Declared(Parse("p { background-image: url(\"green.png"), "p", "background-image"));
        Equal("url(\"green.png\")", Declared(Parse("p { background-image: url(green.png"), "p", "background-image"));
        // Only the end of the sheet closes them: the scan of a value cut short by Lite's raw
        // rescue still reports the unclosed url( as malformed.
        True(!CssComponentScan.IsWellFormedValue("url(\"a.png\""), "an unclosed url( in a standalone value");
    }

    [Test]
    public static void AtImport_HonorsLeadingPositionEofClosureAndEscapedUrls()
    {
        // CSS 2.1 §6.3: @import is ignored after any non-ignored statement other than @charset/@import.
        var fetched = new List<string>();
        (string Css, string Charset)? Fetch(string url, string? _)
        {
            fetched.Add(url);
            return ("p { color: green; }", "utf-8");
        }

        var afterRule = Lite.Parser.InlineImportsCore(
            "body { color: green; }\n@import url(\"red.css\");",
            "http://example.test/page.html",
            Fetch);
        Equal(0, fetched.Count);
        True(afterRule.Contains("@import"), "non-leading @import stays untouched for the CSS parser");

        // CSS 2.1 §6.3 + §4.2: garbage before @import also terminates the @import prologue (at-import-011).
        var afterGarbage = Lite.Parser.InlineImportsCore(
            "# :unknownpseudo\n@import \"red.css\";\np { color: green; }",
            "http://example.test/page.html",
            Fetch);
        Equal(0, fetched.Count);
        True(afterGarbage.Contains("# :unknownpseudo"), "garbage before @import is preserved");

        // CSS 2.1 §4.2: open string, open url(, and missing semicolon at EOF all close cleanly (eof-005/006, uri-018).
        var eofString = Lite.Parser.InlineImportsCore("@import \"green.css", "http://example.test/page.html", Fetch);
        True(eofString.Contains("color: green"), "unclosed string @import at EOF expands");
        Equal("http://example.test/green.css", fetched[^1]);

        var eofNoSemi = Lite.Parser.InlineImportsCore("@import \"green2.css\"", "http://example.test/page.html", Fetch);
        True(eofNoSemi.Contains("color: green"), "@import without trailing semicolon at EOF expands");
        Equal("http://example.test/green2.css", fetched[^1]);

        var eofUrl = Lite.Parser.InlineImportsCore("@import url(green3.css", "http://example.test/page.html", Fetch);
        True(eofUrl.Contains("color: green"), "unclosed url( @import at EOF expands");
        Equal("http://example.test/green3.css", fetched[^1]);

        // CSS 2.1 §4.3.4: escaped parens inside unquoted url(...) unescape before resolution (cascade-import-001).
        fetched.Clear();
        Lite.Parser.InlineImportsCore(
            "@import url(data:text/css,@import%20url\\(data:text/css,p%7Bcolor:red%7D\\););",
            "http://example.test/page.html",
            Fetch);
        Equal(1, fetched.Count);
        Equal("data:text/css,@import%20url(data:text/css,p%7Bcolor:red%7D);", fetched[0]);
    }

    [Test]
    public static void SelectorValidation_EnforcesCss21GrammarAndCaseInsensitivePseudos()
    {
        // Patch 010, CSS 2.1 §4.1.7 / §5.2.1: empty selector items drop the rule (bad-selector-001).
        Equal(0, Parse("body,,div { color: red; }").Rules.Length);
        Equal(0, Parse(",div { color: red; }").Rules.Length);
        Equal(0, Parse("div, { color: red; }").Rules.Length);

        // CSS 2.1 §5.10 / §5.12: nothing may follow a pseudo-element (pseudo-014/016, c25-pseudo-elmnt-000).
        Equal(0, Parse("p:first-line[id=\"p1\"], #p1 { color: red; }").Rules.Length);
        Equal(0, Parse("p:first-line+p, #p1 { color: red; }").Rules.Length);
        Equal(0, Parse("p:first-line.two { color: red; }").Rules.Length);

        // CSS 2.1 §4.1.3: class/ID identifiers cannot start with digit or '-' + digit (ident-003).
        Equal(0, Parse(".-1ident, .three { color: red; }").Rules.Length);
        Equal(0, Parse("#-1ident, .four { color: red; }").Rules.Length);
        Equal(1, Parse(".-ident, .one { color: green; }").Rules.Length);

        // CSS 2.1 §4.1.3: pseudo-class and pseudo-element names are ASCII case-insensitive (pseudo-007/008).
        var pseudoSheet = Parse("div:fiRsT-cHiLd { color: green; } div:bEfOre { content: 'A'; } div:aFtEr { content: 'B'; }");
        Equal(3, pseudoSheet.Rules.Length);
        Equal("rgba(0, 128, 0, 1)", Declared(pseudoSheet, "div:first-child", "color"));
        Equal("\"A\"", Declared(pseudoSheet, "div:before", "content"));
        Equal("\"B\"", Declared(pseudoSheet, "div:after", "content"));
    }

    [Test]
    public static void ColorSyntax_RejectsFourDigitHexAndMixedRgbUnits()
    {
        // Patch 011, CSS 2.1 §4.3.6: 4-digit hex (#1000) and mixed integer/percentage rgb() are invalid
        // (color-030, outline-color-030, colors-004).
        Equal((string?)null, Declared(Parse("div { color: #1000; }"), "div", "color"));
        Equal((string?)null, Declared(Parse("div { color: #11223344; }"), "div", "color"));
        Equal((string?)null, Declared(Parse("div { color: rgb(100%, 0, 0); }"), "div", "color"));
        Equal("rgba(0, 128, 0, 1)", Declared(Parse("div { color: #008000; }"), "div", "color"));
        Equal("rgba(0, 128, 0, 1)", Declared(Parse("div { color: rgb(0, 128, 0); }"), "div", "color"));
        Equal("rgba(255, 0, 0, 1)", Declared(Parse("div { color: rgb(100%, 0%, 0%); }"), "div", "color"));
    }

    [Test]
    public static void DeclarationPropertyValidationAndSelectorEscapedNewline()
    {
        // Patch 012, CSS 2.1 §4.1.1: property names must be IDENT tokens, not STRING (quoted-keywords-001).
        Equal((string?)null, Declared(Parse("div { 'color': red; }"), "div", "color"));
        // CSS 2.1 §4.2: missing property name ':red;' skips to ';' and preserves 'color: green' (malformed-decl-008).
        Equal("rgba(0, 128, 0, 1)", Declared(Parse("div { :red; color: green; }"), "div", "color"));
        // CSS 2.1 §4.3.7: escaped newline in a selector string is removed (strings-000).
        var sheet = Parse("p[title=\"this is\\\n a test\"] { color: green; }");
        Equal(1, sheet.Rules.Length);
        Equal("p[title=\"this is a test\"]", ((ICssStyleRule)sheet.Rules[0]).SelectorText);
    }
}
