using Lite;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §9.5.2 through the HTML <code>&lt;br clear&gt;</code> form: the line box a cleared
/// forced break terminates is positioned below the bottom edge of the floats it clears, so
/// floated groups separated by <code>&lt;br clear="both"&gt;</code> stack in rows instead of
/// packing onto one band (the border-conflict-style-107 float-square shape and the WPT
/// floats-clear corpus).
/// </summary>
public static class FloatClearBreakTests
{
    private static LayoutNode? FindNode(LayoutNode n, Func<LayoutNode, bool> pred) =>
        pred(n) ? n : n.Children.Select(c => FindNode(c, pred)).FirstOrDefault(r => r is not null);

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static LayoutNode Layout(string html)
    {
        FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    [Test]
    public static void ClearedBr_StartsNextFloatGroupBelowPreviousFloats()
    {
        // border-conflict-style-107's shape: two 50x50 floated tables (25px collapsed borders
        // around an empty cell), a <br clear:both>, two more — the second pair must start BELOW
        // the first pair's float bottoms, not beside them on the same band.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<style>br{clear:both}table{border-collapse:collapse;float:left}td{padding:0}" +
            "td{border:25px solid green}</style>" +
            "<table><tr><td></td></tr></table>" +
            "<table><tr><td></td></tr></table>" +
            "<br>" +
            "<table><tr><td></td></tr></table>" +
            "<table><tr><td></td></tr></table>" +
            "</body></html>");
        var tables = new List<LayoutNode>();
        Collect(root, tables);
        True(tables.Count == 4, $"expected 4 floated tables, got {tables.Count}");
        // Row 1: the two tables side by side at the body's content top.
        True(Math.Abs(tables[0].Box.ContentBox.Top - tables[1].Box.ContentBox.Top) < 0.5f,
            $"the first group's tables share a row, got {tables[0].Box.ContentBox.Top} vs {tables[1].Box.ContentBox.Top}");
        True(Math.Abs((tables[1].Box.ContentBox.Left - tables[0].Box.ContentBox.Left) - 50f) < 0.5f,
            $"the second table packs 50px right of the first (one table's outer width), got delta {tables[1].Box.ContentBox.Left - tables[0].Box.ContentBox.Left}");
        // Row 2: the cleared break drops the second group below the first group's floats
        // (exactly one 50px row lower — content boxes sit 12.5px inside the collapsed border).
        True(Math.Abs((tables[2].Box.ContentBox.Top - tables[0].Box.ContentBox.Top) - 50f) < 0.5f,
            $"the cleared break must start the second group one 50px row below the first, got delta {tables[2].Box.ContentBox.Top - tables[0].Box.ContentBox.Top}");
        True(Math.Abs(tables[3].Box.ContentBox.Top - tables[2].Box.ContentBox.Top) < 0.5f,
            "the second group's tables share the second row");
        True(Math.Abs(tables[2].Box.ContentBox.Left - tables[0].Box.ContentBox.Left) < 0.5f,
            $"the second group starts back at the first group's left edge, got left {tables[2].Box.ContentBox.Left} vs {tables[0].Box.ContentBox.Left}");

        static void Collect(LayoutNode n, List<LayoutNode> acc)
        {
            if (string.Equals(n.TagName, "table", StringComparison.OrdinalIgnoreCase)) acc.Add(n);
            foreach (var c in n.Children) Collect(c, acc);
        }
    }

    [Test]
    public static void ClearedBr_OnlyClearsWhenFloatsPresent()
    {
        // A cleared break with no floats in scope leaves the flow alone (ApplyClear's no-float
        // path): two lines of text keep their normal line stacking.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='font:20px/20px Ahem'>a<br style='clear:both'>b</div>" +
            "</body></html>");
        var div = FindNode(root, n => string.Equals(n.TagName, "div", StringComparison.OrdinalIgnoreCase))!;
        // Two 20px lines; the cleared break must not add clearance.
        True(Math.Abs(div.Box.ContentBox.Height - 40f) < 0.5f,
            $"a cleared break without floats must not stretch the flow, got height {div.Box.ContentBox.Height}");
    }

    [Test]
    public static void UnclearedBr_StillPacksFloatsBesideTheBand()
    {
        // Without 'clear', a plain <br> between float groups keeps the floats on one band —
        // guarding against over-clearing.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<style>table{border-collapse:collapse;float:left}td{padding:0;border:25px solid green}</style>" +
            "<table><tr><td></td></tr></table>" +
            "<br>" +
            "<table><tr><td></td></tr></table>" +
            "</body></html>");
        var tables = new List<LayoutNode>();
        Collect(root, tables);
        True(tables.Count == 2, $"expected 2 floated tables, got {tables.Count}");
        True(Math.Abs(tables[1].Box.ContentBox.Top - tables[0].Box.ContentBox.Top) < 0.5f,
            $"a plain break must not clear, got tops {tables[0].Box.ContentBox.Top} vs {tables[1].Box.ContentBox.Top}");

        static void Collect(LayoutNode n, List<LayoutNode> acc)
        {
            if (string.Equals(n.TagName, "table", StringComparison.OrdinalIgnoreCase)) acc.Add(n);
            foreach (var c in n.Children) Collect(c, acc);
        }
    }
}
