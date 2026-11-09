using Lite;
using System.Linq;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Border-conflict probes: what the style system computes for col/colgroup
/// borders, and what ResolveCollapsedBorders yields at each cell edge.</summary>
public static class BorderConflictProbeTests
{
    [Test]
    public static void ColGroupBorder_WidthsComputed()
    {
        // table-anonymous-objects-030's shape: colgroup red 5px vs col black 5px.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "table { border-collapse: collapse; height: 2in; width: 2in; }" +
            "colgroup { border: 5px solid red; }" +
            "col { border: 5px solid black; }" +
            "</style></head><body><table><colgroup><col></colgroup>" +
            "<tr><td></td></tr></table></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        foreach (var n in All(page.Root))
        {
            if (n.TagName is not ("COL" or "COLGROUP" or "TD" or "TABLE")) continue;
            var b = n.GetBorderWidth();
            Console.WriteLine($"[bc030] {n.TagName}: T={b.Top:0.##}/{n.GetBorderStyleTop()} R={b.Right:0.##}/{n.GetBorderStyleRight()} " +
                              $"B={b.Bottom:0.##}/{n.GetBorderStyleBottom()} L={b.Left:0.##}/{n.GetBorderStyleLeft()}");
        }
        var td = All(page.Root).First(n => n.TagName == "TD");
        foreach (var kv in td.CollapsedEdgeBorders ?? [])
            Console.WriteLine($"[bc030] td {kv.Key}: {kv.Value.Width:0.##}/{kv.Value.Style}/{kv.Value.Color}");
        True(td.CollapsedEdgeBorders is { Count: 4 }, "collapsed edges missing");
        // §17.6.2.1: the column outranks the column group on equal width and style — every
        // resolved edge must be the col's black, never the colgroup's red.
        foreach (var kv in td.CollapsedEdgeBorders!)
        {
            True(kv.Value.Style != BorderStyle.None && kv.Value.Width > 0, $"edge {kv.Key} resolved to nothing");
            True(kv.Value.Color != SKColors.Red, $"edge {kv.Key} resolved to the colgroup's red");
        }
    }

    [Test]
    public static void StaticInlineBorderStyle_JoinsTheConflict()
    {
        // border-collapse-dynamic-cell-001's starting state: a static style="border-style:
        // solid" on the target cell must reach the §17.6.2.1 resolution — at the 10px width
        // tie solid outranks the sheet's outset, so every edge around the target reads solid
        // (AngleSharp keeps an inline shorthand under its own key; the longhand keys carry
        // the sheet's outset unless the parser stamps the inline declaration expanded).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>td { border: 10px green outset; }</style></head>" +
            "<body style='margin:0'><table style='border-collapse:collapse'><tr>" +
            "<td>c11</td><td style='border-style:solid' id='target'>c22</td></tr></table></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var td = All(page.Root).First(n => n.Attributes.GetValueOrDefault("id") == "target");
        True(td.GetBorderStyleTop() == BorderStyle.Solid,
            $"the inline solid reaches the border readers, got {td.GetBorderStyleTop()}");
        True(td.CollapsedEdgeBorders is { Count: 4 }, "collapsed edges missing");
        foreach (var kv in td.CollapsedEdgeBorders!)
            True(kv.Value.Style == BorderStyle.Solid,
                $"edge {kv.Key} resolves the inline solid at the width tie, got {kv.Value.Style}");
    }

    private static IEnumerable<LayoutNode> All(LayoutNode node)
    {
        yield return node;
        foreach (var c in node.Children)
            foreach (var d in All(c)) yield return d;
    }
}
