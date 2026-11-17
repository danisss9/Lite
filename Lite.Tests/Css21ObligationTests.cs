using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Focused evidence for CSS 2.1 obligations that no applicable suite case maps to:
/// each test pins one user-agent requirement of the pinned Recommendation and is cited by
/// that obligation's mapping in Profile/css21-requirements.json.</summary>
public static class Css21ObligationTests
{
    /// <summary>§9.6.1: "For continuous media, fixed boxes do not move when the document is
    /// scrolled." The in-flow marker proves the scroll happened; the fixed box stays put.</summary>
    [Test]
    public static void FixedBox_DoesNotMoveWhenTheDocumentScrolls()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='position:fixed; top:10px; left:10px; width:40px; height:40px; background:#008000'></div>" +
            "<div style='margin-left:100px; width:40px; height:40px; background:#0000ff'></div>" +
            "<div style='height:3000px'></div></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var viewport = new Viewport { ViewportHeight = 600 };
        using (var before = Drawer.DrawToBitmap(800, 600, page.Root, viewport))
        {
            True(IsGreen(before.GetPixel(30, 30)), $"the fixed box must paint before scrolling, got {before.GetPixel(30, 30)}");
            True(IsBlue(before.GetPixel(120, 20)), $"the in-flow marker must paint before scrolling, got {before.GetPixel(120, 20)}");
        }
        viewport.ScrollTo(500);
        using var after = Drawer.DrawToBitmap(800, 600, page.Root, viewport);
        True(!IsBlue(after.GetPixel(120, 20)), "the document must actually scroll: the in-flow marker has to leave the viewport");
        True(IsGreen(after.GetPixel(30, 30)), $"the fixed box must stay at its viewport position, got {after.GetPixel(30, 30)}");
    }

    /// <summary>§4.3.8: a UA that does not support a value "should ignore that value when
    /// parsing style sheets, as if that value was an illegal value". Lite implements no run-in
    /// layout, so 'display: run-in' must leave the earlier declaration in force.</summary>
    [Test]
    public static void UnsupportedValue_IsIgnoredAsIfIllegal()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>p { display: inline } p { display: run-in }</style></head>" +
            "<body><p>x</p></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        var display = p!.TryResolveStyle("display", out var resolved) ? resolved : p.Style.GetPropertyValue("display");
        True(display == "inline", $"an unsupported display value must be dropped like an illegal one, got '{display}'");
    }

    /// <summary>§15.3.1: "User agents should provide reasonable default choices for the generic
    /// font families". Every generic keyword resolves to an installed face, the serif and
    /// sans-serif choices differ, and the monospace choice really has fixed advances.</summary>
    [Test]
    public static void GenericFontFamilies_ResolveToReasonableInstalledFaces()
    {
        string Resolve(string family)
        {
            var page = Parser.ParseChildPage(
                $"<!DOCTYPE html><html><body><p style='font-family: {family}'>x</p></body></html>",
                isSrcdoc: true, "http://test/", 800, 600);
            return Find(page.Root, "P")!.GetFontFamily();
        }
        var faces = new Dictionary<string, string>();
        foreach (var generic in new[] { "serif", "sans-serif", "cursive", "fantasy", "monospace" })
        {
            var face = Resolve(generic);
            using var typeface = SKTypeface.FromFamilyName(face);
            True(typeface is not null && string.Equals(typeface.FamilyName, face, StringComparison.OrdinalIgnoreCase),
                $"'{generic}' must resolve to an installed face, got '{face}'");
            faces[generic] = face;
        }
        True(!string.Equals(faces["serif"], faces["sans-serif"], StringComparison.OrdinalIgnoreCase),
            $"serif and sans-serif must be different faces, both were '{faces["serif"]}'");
        using var mono = SKTypeface.FromFamilyName(faces["monospace"]);
        using var font = new SKFont(mono, 20);
        True(Math.Abs(font.MeasureText("iiiiiiii") - font.MeasureText("WWWWWWWW")) < 0.5f,
            $"the monospace choice '{faces["monospace"]}' must have fixed advances");
    }

    /// <summary>§3.2 UA conformance point 2: the UA must attempt to retrieve every associated
    /// style sheet and display the document with those it can retrieve. An unreachable linked
    /// sheet and an unreachable @import must not stop the reachable sheets from applying.</summary>
    [Test]
    public static void AssociatedStyleSheets_ApplyAroundRetrievalFailures()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head>" +
            "<link rel='stylesheet' href='http://127.0.0.1:1/unreachable.css'>" +
            "<link rel='stylesheet' href='data:text/css,p%20%7B%20color%3A%20green%20%7D'>" +
            "<style>@import url(http://127.0.0.1:1/unreachable-import.css); p { background-color: lime }</style>" +
            "</head><body><p>x</p></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        var color = p!.TryResolveStyle("color", out var c) ? c : p.Style.GetPropertyValue("color");
        var background = p.TryResolveStyle("background-color", out var b) ? b : p.Style.GetPropertyValue("background-color");
        True(color.Contains("0, 128, 0"), $"the retrievable linked sheet must apply, got color {color}");
        True(background.Contains("0, 255, 0"), $"the inline sheet must apply past its failed @import, got {background}");
    }

    /// <summary>§7.3: the UA recognizes the CSS 2.1 media type names, which are
    /// case-insensitive, and rendering for 'screen' ignores rules for the other types.</summary>
    [Test]
    public static void MediaTypes_AreRecognizedCaseInsensitivelyAndOthersIgnored()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "@media SCREEN { p { color: green } }" +
            "@media tv { p { color: red } }" +
            "@media print, handheld { p { background-color: red } }" +
            "</style><style media='screen'>p { margin-left: 30px }</style>" +
            "<style media='Screen'>p { margin-right: 20px }</style>" +
            "<style media='projection'>p { margin-left: 60px }</style></head>" +
            "<body><p>x</p></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var p = Find(page.Root, "P");
        True(p != null, "p missing");
        string Read(string property) => p!.TryResolveStyle(property, out var value) ? value : p.Style.GetPropertyValue(property);
        True(Read("color").Contains("0, 128, 0"), $"@media SCREEN must apply on screen, got color {Read("color")}");
        True(!Read("background-color").Contains("255, 0, 0"), $"print/handheld rules must not apply on screen, got {Read("background-color")}");
        True(Read("margin-left") == "30px", $"media='screen' must apply and 'projection' must not, got margin-left {Read("margin-left")}");
        True(Read("margin-right") == "20px", $"media='Screen' must match case-insensitively, got margin-right {Read("margin-right")}");
    }

    private static bool IsGreen(SKColor c) => c.Green > 100 && c.Red < 60 && c.Blue < 60;
    private static bool IsBlue(SKColor c) => c.Blue > 150 && c.Red < 60 && c.Green < 60;

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var child in node.Children) { var found = Find(child, tag); if (found != null) return found; }
        return null;
    }
}
