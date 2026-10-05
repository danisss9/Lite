using Lite;
using Lite.Models;
using Lite.Network;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>The CSS user-agent controls (CSS 2.1 UA conformance): a style element's disabled
/// state toggles its sheet out of the cascade, the user may disable author styles, provide a
/// user style sheet (USER origin per §6.4.1: its normal declarations lose to author rules,
/// its !important ones win), and select a preferred alternate style sheet set.</summary>
public static class StyleSheetControlsTests
{
    [Test]
    public static void StyleElementDisabled_SuspendsTheSheetAndReportsTheState()
    {
        // Known store limitation (see the css21.dynamic-recascade profile entry): the
        // AngleSharp style store keeps parse-time computed declarations per element, so a
        // suspended author-!important rule still wins on EXISTING elements until the
        // store-aware recascade lands. What must hold today: the sheet's content is parked,
        // its rules leave the collected rule view, and the IDL state reports disabled.
        var session = new BrowserSession();
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style id='s'>span { display: block !important }</style></head><body>" +
            "<span style=\"display: table-cell\">a b</span>" +
            "<script>document.getElementById('s').disabled = true;</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600, session);
        var styleEl = page.Document!.QuerySelector("style#s");
        True(styleEl is not null, "style element missing");
        True(styleEl!.TextContent.Length == 0, "a suspended sheet must render an empty sheet");
        True(page.Engine.RawEngine.Evaluate("document.getElementById('s').disabled").ToString() == "true",
            "the disabled IDL state must read back true");
        True(!page.Engine.DocumentState!.StyleRules.Any(r => r.Selector == "span"),
            "the suspended sheet's rules must leave the collected rule view");
    }

    [Test]
    public static void StyleElementDisabled_FalseReenables()
    {
        var session = new BrowserSession();
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style id='s'>p { color: green }</style></head><body>" +
            "<p>x</p><script>var s = document.getElementById('s'); s.disabled = true; s.disabled = false;</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600, session);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        True(p!.Style.GetPropertyValue("color").Contains("0, 128, 0"),
            "re-enabling the sheet must restore its rules");
    }

    [Test]
    public static void AuthorStylesDisabled_KeepsUaRulesAndDropsAuthorOnes()
    {
        var session = new BrowserSession { AuthorStylesEnabled = false };
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>p { color: red; margin-top: 40px }</style></head><body>" +
            "<p>x</p></body></html>", isSrcdoc: true, "http://test/", 800, 600, session);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        True(!p!.Style.GetPropertyValue("color").Contains("255, 0, 0"),
            "author color must be gone with author styles disabled");
        True(p.Style.GetPropertyValue("margin-top") == "1em",
            $"the UA sheet's p margin must survive, got {p.Style.GetPropertyValue("margin-top")}");
    }

    [Test]
    public static void UserStyleSheet_LosesToAuthorNormalAndBeatsAuthorImportant()
    {
        // §6.4.1: user normal < author normal, and user !important > author !important (the
        // CSS 2.1 balance-of-power rule).
        var session = new BrowserSession
        {
            UserStyleSheet = "p { color: green } p { background-color: lime !important }",
        };
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "p { color: red } p { background-color: olive !important }" +
            "</style></head><body><p>x</p></body></html>", isSrcdoc: true, "http://test/", 800, 600, session);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        True(p!.Style.GetPropertyValue("color").Contains("255, 0, 0"),
            "author normal must beat user normal");
        True(p.Style.GetPropertyValue("background-color").Contains("0, 255, 0"),
            $"user !important must beat author !important, got {p.Style.GetPropertyValue("background-color")}");
    }

    [Test]
    public static void SelectedStyleSheetSet_PicksTheNamedAlternate()
    {
        var session = new BrowserSession { SelectedStyleSheetSet = "big" };
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head>" +
            "<style>p { color: red }</style>" +
            "<link rel='alternate stylesheet' title='big' href='data:text/css,p%20%7B%20color%3A%20blue%20%7D'>" +
            "</head><body><p>x</p></body></html>", isSrcdoc: true, "http://test/", 800, 600, session);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        True(p!.Style.GetPropertyValue("color").Contains("0, 0, 255"),
            $"the selected alternate set must apply, got {p.Style.GetPropertyValue("color")}");
    }

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children) { var f = Find(c, tag); if (f != null) return f; }
        return null;
    }
}
