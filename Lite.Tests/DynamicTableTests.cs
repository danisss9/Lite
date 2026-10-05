using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Dynamic anonymous-table generation (CSS 2.1 §17.2.1): render-invisible boxes must
/// not split anonymous runs, and DOM mutations (a script probing layout, appendChild of a text
/// node) must leave one merged cell that shrink-wraps the whole inline run.</summary>
public static class DynamicTableTests
{
    [Test]
    public static void ScriptBetweenTexts_LeavesOneAnonymousCell()
    {
        // table-anonymous-objects-006: `Cell 1 <script/> Cell 2` inside display:table is ONE
        // cell; the layout-probing script renders nothing and must not split the run.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<span style=\"display:table\">Cell 1 <script>void 0;</script> Cell 2</span>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var span = Find(page.Root, "SPAN");
        True(span != null, "span missing");
        BoxEngine.Layout(page.Root, 800, 600);
        var cell = Find(span, "#anon-cell");
        True(cell != null, "expected an anonymous cell under the table span");
        Equal(1, cell!.Children.Count);
        True(cell.Children[0].DisplayText.Contains("Cell 1") &&
             cell.Children[0].DisplayText.Contains("Cell 2"),
            "both texts must share the one anonymous cell");
    }

    [Test]
    public static void AppendedTextNode_FlowsInsideAnonymousCell()
    {
        // table-anonymous-objects-051: appendChild of a text node into an inline-table whose
        // text lives as own text must widen the shrink-wrapped table to the whole run ("bc"),
        // not stack the texts in a cell sized to the first one.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#it { display: inline-table; }</style></head><body>" +
            "<p>a<span id=\"it\">b</span>d</p>" +
            "<script>document.getElementById('it').appendChild(document.createTextNode('c'));</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var span = Find(page.Root, "SPAN");
        True(span != null, "span missing");
        BoxEngine.Layout(page.Root, 800, 600);
        var cell = Find(span, "#anon-cell");
        True(cell != null, "expected an anonymous cell");
        var texts = cell!.Children.Where(c => c.TagName == "#text").ToList();
        Equal(2, texts.Count);
        True(texts.All(t => t.GetDisplay() == DisplayType.Inline),
            "cell text runs must be inline");
        True(texts[0].Box.BorderBox.Top == texts[1].Box.BorderBox.Top,
            "the appended text must share the first text's line, not stack");
        var b = Find(page.Root, "BODY");
        using var bmp = Drawer.DrawToBitmap(800, 600, page.Root, new Viewport { ViewportHeight = 600 });
        True(bmp.Width > 0, "page must render");
    }

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children) { var f = Find(c, tag); if (f != null) return f; }
        return null;
    }
}
