using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Diagnostic probe: @media-block shorthand storage (case-sensitive-001).</summary>
public static class MediaBlockProbe
{
    [Test]
    public static void MediaBlockBackground_Applies()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "p { color: white; height: 100px; }" +
            "@media all { p { background: url(support/swatch-red.png); } }" +
            "</style></head><body><p>x</p></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngineLayout(page.Root);
        var p = Find(page.Root, n => n.TagName == "P");
        True(p is not null, "the p element exists in the layout tree");
        if (p is null) return;
        True(p.MediaOverrides.Count > 0, $"MediaOverrides empty (keys: {string.Join(",", p.MediaOverrides.Keys)})");
        True(p.TryResolveStyle("background-image", out var img) && img.Contains("swatch-red"),
            $"background-image resolved to '{(p.TryResolveStyle("background-image", out var i2) ? i2 : "<none>")}', media keys: {string.Join(",", p.MediaOverrides.Keys.Select(k => k + "=" + p.MediaOverrides[k]))}");
    }

    private static void BoxEngineLayout(Lite.Models.LayoutNode root) => Lite.Layout.BoxEngine.Layout(root, 800, 600);
    private static Lite.Models.LayoutNode? Find(Lite.Models.LayoutNode n, Func<Lite.Models.LayoutNode, bool> f)
    {
        if (f(n)) return n;
        foreach (var c in n.Children) { var r = Find(c, f); if (r is not null) return r; }
        return null;
    }
}
