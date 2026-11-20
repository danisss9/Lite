using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Lane 6.H: a background SHORTHAND sets every background longhand to its initial
/// value (CSS 2.1 §14.2.1 / background-reset-001). The parse-time cascade stores a shorthand
/// as its longhands, so a restyle must be able to retract those longhands — otherwise a
/// dynamically restyled body kept the old background (background-root-101 through -102).</summary>
public static class BackgroundRestyleTests
{
    [Test]
    public static void RestyledBackgroundShorthand_RetractsItsParseTimeLonghands()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "body.before{background:red;color:yellow}" +
            "body.after{background:green;color:white}" +
            "</style></head><body class=before><p>x</p></body></html>",
            isSrcdoc: true, "http://test/", 400, 200);
        var body = page.Root.Children.First(c => c.TagName == "BODY");
        var before = body.GetBackgroundColor();
        True(before.Red == 255 && before.Green == 0, "the before rule paints red");

        // The DOM element and the layout-node attributes move together in the live engine
        // (JsElement.OnAttributeChanged); reproduce both halves of the class change.
        body.Attributes["class"] = "after";
        ((AngleSharp.Dom.IElement)body.DomNode!).SetAttribute("class", "after");
        StyleResolver.Apply(body, ["background", "color"]);

        var after = body.GetBackgroundColor();
        True(after.Red == 0 && after.Green == 128,
            $"the after rule paints green, got {after}");
    }

    [Test]
    public static void InlineBackground_StaysWhenAnUnrelatedClassChangeRecascades()
    {
        // reference-wait.html's TestRendered handler sets style.backgroundColor and then
        // clears the class: the re-cascade must not retract the inline longhand.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html class=reftest-wait><head><style>html,body{margin:0;background:red}</style></head>" +
            "<body></body></html>",
            isSrcdoc: true, "http://test/", 400, 200);
        var html = page.Root;
        html.StyleOverrides["background-color"] = "blue";
        html.CascadeAppliedProps.Remove("background-color");
        StyleResolver.Apply(html, []);

        var bg = html.GetBackgroundColor();
        True(bg.Blue == 255 && bg.Red == 0, $"the inline background-color survives, got {bg}");
    }
}
