using AngleSharp.Css.Dom;
using Lite.Conformance.Harness;
using AngleSharp.Css.Parser;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §4.1.3: at-rule names, medium names, property names, function keywords and
/// pseudo-class names are ASCII case-insensitive. Lite's own layers lowercase what they touch;
/// whatever AngleSharp 1.0.2 drops at parse time never reaches them.</summary>
public static class CssCaseSensitivityProbe
{
    private static ICssStyleSheet Parse(string css) => new CssParser().ParseStyleSheet(css);

    [Test]
    public static void MediaBlock_WithMixedCaseAtRuleAndProperty_Parses()
    {
        var sheet = Parse("@MeDIa aLL { p { BacKgrOUNd: UrL(support/swatch-green.png); } }");
        var media = sheet.Rules.OfType<ICssMediaRule>().FirstOrDefault();
        True(media is not null, $"the @MeDIa rule parsed as a media rule (rules: {string.Join(", ", sheet.Rules.Select(r => r.Type))})");
        if (media is not null)
        {
            True(media.Media.MediaText.Trim().Equals("all", StringComparison.OrdinalIgnoreCase),
                $"media text was '{media.Media.MediaText}'");
            var style = media.Rules.OfType<ICssStyleRule>().FirstOrDefault();
            True(style is not null, "the inner style rule parsed");
            var bg = style?.Style.GetPropertyValue("background");
            True(bg?.Contains("swatch-green.png") == true, $"background parsed as '{bg}'");
        }
    }

    [Test]
    public static void MixedCasePseudoClass_SelectorParses()
    {
        var sheet = Parse("p:FiRSt-cHIlD { color: green; } :LinK, :viSiTed { color: red; }");
        var selectors = sheet.Rules.OfType<ICssStyleRule>().Select(r => r.SelectorText).ToList();
        True(selectors.Count == 2, $"expected 2 style rules, got {selectors.Count}: {string.Join(" | ", selectors)}");
    }

    [Test]
    public static void LangCaseVariants_SelectorParses()
    {
        var sheet = Parse(":lang(en) { color: green; } :lang(FR) { color: green; }");
        var selectors = sheet.Rules.OfType<ICssStyleRule>().Select(r => r.SelectorText).ToList();
        True(selectors.Count == 2, $"expected 2 style rules, got {selectors.Count}: {string.Join(" | ", selectors)}");
    }

    [Test]
    public static void BackgroundImageLonghand_IsKeptByTheParser()
    {
        var sheet = Parse("p { color: white; background-image: url(support/green.png); }");
        var style = sheet.Rules.OfType<ICssStyleRule>().FirstOrDefault()?.Style;
        True(style is not null, "style rule parsed");
        var img = style!.GetPropertyValue("background-image");
        True(img.Contains("green.png"), $"background-image read as '{img}'");
        var cssText = style.CssText;
        True(cssText.Contains("background-image"), $"css text was '{cssText}'");
    }



    [Test]
    public static void Case003_LinkColor_ComputesGreen()
    {
        var html = File.ReadAllText(Path.Combine(ConformancePaths.Vendor,
            "css21-official-20110323", "html4", "case-sensitive-003.htm"));
        var page = Parser.ParseChildPage(html, isSrcdoc: true,
            "http://test/css21-official/html4/case-sensitive-003.htm", 800, 600);
        var anchor = FindTag(page.Root, "A");
        True(anchor is not null, "an anchor exists");
        if (anchor is not null)
        {
            var color = anchor.TryResolveStyle("color", out var c) ? c : Get(anchor.DomNode as AngleSharp.Dom.IElement, "color");
            True(color.Contains("green") || color.Contains("0, 128") || color.Trim() == "green",
                $"anchor color computed as '{color}'; overrides: {string.Join(",", anchor.StyleOverrides.Select(kv => kv.Key + "=" + kv.Value))}");
            var deco = anchor.TryResolveStyle("text-decoration", out var d) ? d : Get(anchor.DomNode as AngleSharp.Dom.IElement, "text-decoration");
            True(deco.Trim() != "underline", $"anchor text-decoration computed as '{deco}'");
        }
    }

    private static string Get(AngleSharp.Dom.IElement e, string prop)
    {
        try
        {
            if (e is null) return "<null-elem>";
            return Parser.ComputeCurrentStyleFor(e).GetPropertyValue(prop) ?? "<empty>";
        }
        catch (Exception ex) { return "<err " + ex.Message + ">"; }
        catch { return "<err>"; }
    }

    private static Lite.Models.LayoutNode? FindTag(Lite.Models.LayoutNode n, string tag)
    {
        if (n.TagName == tag) return n;
        foreach (var c in n.Children) { var r = FindTag(c, tag); if (r is not null) return r; }
        return null;
    }

    [Test]
    public static void MixedCaseLang_Matches()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body><p lang='en'>x</p></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var pElem = FindTag(page.Root, "P")!.DomNode as AngleSharp.Dom.IElement;
        True(pElem is not null, "p found");
        True(pElem!.Matches(":lAnG(en)"), "p[lang=en] matched :lAnG(en)");
    }
}
