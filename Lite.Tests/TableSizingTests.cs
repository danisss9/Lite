using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §17.5.2 / §17.4 table width regressions: anonymous children measured with
/// their host cell's box edges, and absolutely positioned tables stretched by their offsets.</summary>
public static class TableSizingTests
{
    private static LayoutNode Layout(string html)
    {
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    private static LayoutNode? Find(LayoutNode node, string tag, string? id = null)
    {
        if (node.TagName == tag && (id is null || node.Id == id)) return node;
        foreach (var c in node.Children)
        {
            var found = Find(c, tag, id);
            if (found is not null) return found;
        }
        return null;
    }

    [Test]
    public static void CellWithGeneratedContent_IsAsWideAsItsText()
    {
        // content-037/080: a ::before (and the #text the cell's own text moves into) share the
        // cell's style object — measuring them added the cell's 5px borders again per child.
        float CellWidth(string cell) => Find(Layout(
            "<!DOCTYPE html><style>td{border:5px solid black;padding:0} td:before{content:attr(abbr)}</style>" +
            $"<table><tr>{cell}</tr></table>"), "TD")!.Box.ContentBox.Width;
        var pseudo = CellWidth("<td abbr=\"PASS\"></td>");
        var mixed = CellWidth("<td abbr=\"\">PASS</td>");
        True(Math.Abs(pseudo - mixed) < 0.5f, $"generated and literal text must size the cell alike: {pseudo} vs {mixed}");
        var text = Find(Layout("<!DOCTYPE html><style>td{border:5px solid black;padding:0}</style>" +
            "<table><tr><td>PASS</td></tr></table>"), "TD")!.Box.ContentBox.Width;
        True(Math.Abs(pseudo - text) < 0.5f, $"the cell must be as wide as its text ({text}), got {pseudo}");
    }

    [Test]
    public static void AbsPosTableWithBothOffsets_KeepsItsAutoTableWidth()
    {
        // no_red_3x3_monospace_table-ref: left/right size only the table wrapper; the table box
        // stays content-sized instead of spreading its columns across the containing block.
        var root = Layout("<!DOCTYPE html><style>div{position:relative;width:600px}" +
            "table{position:absolute;left:1px;right:1px;border-spacing:0} td{padding:0}</style>" +
            "<div><table id=\"t\"><tr><td>a</td><td>b</td></tr></table></div>");
        var t = Find(root, "TABLE", "t");
        True(t is not null, "table missing");
        True(t!.Box.ContentBox.Width < 100f, $"the table must stay content-sized, got {t.Box.ContentBox.Width}");
        True(Math.Abs(t.Box.ContentBox.Left - 9f) < 0.5f, $"the table must sit at left:1px (9), got {t.Box.ContentBox.Left}");
    }

    [Test]
    public static void CollapsedRowBorder_GivesTheRowHalfItsWidth()
    {
        // border-bottom-applies-to-004: an empty row whose collapsed bottom border is 4px is 2px
        // tall (half the border lies in the cells), and the table holds the other half.
        var root = Layout("<!DOCTYPE html><style>body{margin:0} #t{display:table;border-collapse:collapse;width:100px}" +
            "#r{display:table-row;border-bottom:4px solid green} .c{display:table-cell}</style>" +
            "<div id=\"t\"><div id=\"r\"><div class=\"c\"></div><div class=\"c\"></div></div></div>");
        var row = Find(root, "DIV", "r");
        var table = Find(root, "DIV", "t");
        True(row is not null && table is not null, "nodes missing");
        True(Math.Abs(row!.Box.BorderBox.Height - 2f) < 0.01f, $"row must be 2px tall, got {row.Box.BorderBox.Height}");
        True(Math.Abs(table!.Box.BorderBox.Height - 4f) < 0.01f, $"table must be 4px tall, got {table.Box.BorderBox.Height}");
    }

    [Test]
    public static void TableHeight_StretchesRows_CellWidthExcludesEdges_BottomAlignMovesContent()
    {
        // background-043-ref: table { height: 206px } with td { width: 200px; border: 3px;
        // vertical-align: bottom } — a 206x206 cell whose 15px content sits at its bottom.
        var root = Layout("<!DOCTYPE html><style>body{margin:0} table{border-spacing:0;height:206px}" +
            " td{border:3px solid black;padding:0;vertical-align:bottom;width:200px} span{display:inline-block;height:15px;width:10px}</style>" +
            "<table><tr><td id=\"c\"><span id=\"s\"></span></td></tr></table>");
        var cell = Find(root, "TD", "c");
        var span = Find(root, "SPAN", "s");
        True(cell is not null && span is not null, "nodes missing");
        True(Math.Abs(cell!.Box.BorderBox.Width - 206f) < 0.01f, $"cell must be 206px wide, got {cell.Box.BorderBox.Width}");
        True(Math.Abs(cell.Box.BorderBox.Height - 206f) < 0.01f, $"cell must fill the 206px table, got {cell.Box.BorderBox.Height}");
        True(span!.Box.BorderBox.Bottom > 190f, $"bottom-aligned content must sit at the cell bottom, got {span.Box.BorderBox.Bottom}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
