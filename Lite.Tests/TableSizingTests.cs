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
    public static void TableBackground_DoesNotCoverTheTopCaption()
    {
        // caption-side-applies-to-009: §17.4 puts the caption in the table wrapper, OUTSIDE the
        // table box — the table's background paints only on the band below the caption, or the
        // caption's text renders on top of it.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}" +
            "#t{display:table;background:blue} #c{display:table-caption}" +
            ".r{display:table-row} .cell{display:table-cell;width:100px;height:50px;background:blue}</style>" +
            "<div id=\"t\"><div id=\"c\">cap</div><div class=\"r\"><div class=\"cell\"></div></div></div>");
        var bitmap = Drawer.DrawToBitmap(200, 200, root, new Viewport { ViewportHeight = 200 });
        using (bitmap)
        {
            True(bitmap.GetPixel(60, 10) == SkiaSharp.SKColors.White,
                $"the caption band must not take the table's blue, got {bitmap.GetPixel(60, 10)}");
            True(bitmap.GetPixel(60, 40) == SkiaSharp.SKColors.Blue,
                $"the table box below the caption must be blue, got {bitmap.GetPixel(60, 40)}");
        }
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

    [Test]
    public static void FixedLayout_SplitsLeftoverEquallyAndColumnsPaintBackgrounds()
    {
        // fixed-table-layout-003a01/017: the first-row cell's width fixes its column, the other
        // columns share the rest equally whatever their padding, and a col background paints.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>body{margin:0} table{table-layout:fixed;width:400px;border-spacing:0}" +
            " td{padding:0 60px;height:20px} #t{width:80px} col.o{background:orange}</style>" +
            "<table><col class=\"o\"/><col/><col/><tr><td></td><td id=\"t\"></td><td></td></tr></table>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var t = Find(page.Root, "TD", "t");
        True(t is not null, "cell missing");
        True(Math.Abs(t!.Box.BorderBox.Left - 100f) < 0.01f && Math.Abs(t.Box.BorderBox.Width - 200f) < 0.01f,
            $"tested cell must span 100..300, got {t.Box.BorderBox}");
        using var bitmap = Drawer.DrawToBitmap(400, 100, page.Root, new Viewport { ViewportHeight = 100 });
        True(bitmap.GetPixel(50, 10) == new SkiaSharp.SKColor(255, 165, 0), $"the first column must paint orange, got {bitmap.GetPixel(50, 10)}");
    }

    [Test]
    public static void EmptyCellsInherit_OnTheCell_ResolvesTheKeyword()
    {
        // empty-cells-003: tr { empty-cells: hide } with .empty { empty-cells: inherit } — the
        // raw keyword sat in the override channel and read back as not-hide, so the cell painted.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}" +
            "#t{display:table;background:green;border-spacing:0}" +
            ".r{display:table-row;empty-cells:hide}" +
            ".e{display:table-cell;background:red;empty-cells:inherit;width:40px;height:20px}" +
            ".ok{display:table-cell;width:40px;height:20px}</style>" +
            "<div id=\"t\"><div class=\"r\"><div class=\"e\"></div><div class=\"ok\"></div></div></div>");
        var bitmap = Drawer.DrawToBitmap(200, 100, root, new Viewport { ViewportHeight = 100 });
        using (bitmap)
            True(bitmap.GetPixel(10, 10) == SkiaSharp.SKColors.Green,
                $"the inherited hide must suppress the empty cell, got {bitmap.GetPixel(10, 10)}");
    }

    [Test]
    public static void EmptyCellsHide_KeepsNbspAndPreWhitespaceCells()
    {
        // empty-cells-005: an NBSP run is content (CSS white space is only space/tab/LF/CR/FF);
        // empty-cells-006: under pre, even a white-space-only run renders — both cells paint.
        var nbsp = Layout("<!DOCTYPE html><style>body{margin:0}" +
            "#t{display:table;background:green;border-spacing:0}" +
            ".c{display:table-cell;empty-cells:hide;background:red;width:40px;height:20px}</style>" +
            "<div id=\"t\"><div class=\"r\"><div class=\"c\">&nbsp;&nbsp;</div></div></div>");
        using (var bitmap = Drawer.DrawToBitmap(200, 100, nbsp, new Viewport { ViewportHeight = 100 }))
            True(bitmap.GetPixel(10, 10) == SkiaSharp.SKColors.Red,
                $"an NBSP-only cell is not empty, got {bitmap.GetPixel(10, 10)}");
        var pre = Layout("<!DOCTYPE html><style>body{margin:0}" +
            "#t{display:table;background:green;border-spacing:0}" +
            ".c{display:table-cell;empty-cells:hide;background:red;width:40px;height:20px;white-space:pre}</style>" +
            "<div id=\"t\"><div class=\"r\"><div class=\"c\">  </div></div></div>");
        using (var bitmap = Drawer.DrawToBitmap(200, 100, pre, new Viewport { ViewportHeight = 100 }))
            True(bitmap.GetPixel(10, 10) == SkiaSharp.SKColors.Red,
                $"a pre white-space-only cell is not empty, got {bitmap.GetPixel(10, 10)}");
    }

    [Test]
    public static void EmptyCellsHide_DoesNotApplyInTheCollapsingModel()
    {
        // empty-cells-in-collapsed-border-model-001: §17.6.1.1 scopes empty-cells to the
        // separated-borders model; a collapsed table's empty cell still paints.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}" +
            "#t{display:table;background:green;border-collapse:collapse;empty-cells:hide}" +
            ".c{display:table-cell;background:red;width:40px;height:20px}</style>" +
            "<div id=\"t\"><div class=\"r\"><div class=\"c\"></div></div></div>");
        var bitmap = Drawer.DrawToBitmap(200, 100, root, new Viewport { ViewportHeight = 100 });
        using (bitmap)
            True(bitmap.GetPixel(10, 10) == SkiaSharp.SKColors.Red,
                $"a collapsed table ignores empty-cells: hide, got {bitmap.GetPixel(10, 10)}");
    }

    [Test]
    public static void ReplacedSize_UsesSpecifiedZeroNotTheIntrinsicFallback()
    {
        // empty-cells-007/008: img { height: 0; width: 0 } inside a hidden-empty check — the
        // specified 0 read back as 'auto' and the 1x1 intrinsic fell in.
        var root = Layout("<!DOCTYPE html><img src=\"missing.png\" width=\"1\" height=\"1\" " +
                          "style=\"width:0;height:0\">");
        var img = Find(root, "IMG");
        True(img is not null, "img missing");
        True(img!.Box.ContentBox.Width == 0f && img.Box.ContentBox.Height == 0f,
            $"a specified 0x0 must be used as-is, got {img.Box.ContentBox.Width}x{img.Box.ContentBox.Height}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
