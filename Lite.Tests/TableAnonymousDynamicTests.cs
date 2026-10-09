using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Diagnostic repros for the table-anonymous-objects official-suite bands:
/// the dynamic-removal white-space band (167-206, abs-pos shrink-to-fit width) and the
/// display-toggle cycle (007/008, the tr dropped by the nested wrap/unwrap rebuild).</summary>
public static class TableAnonymousDynamicTests
{
    [Test]
    public static void StaticAdjacentCells_TextFragmentAlignment()
    {
        // table-anonymous-objects-167 (real markup, whitespace included): red span flow
        // [ws a ws cell-b ws cell-c ws d ws] must render exactly like the green literal
        // "a bc d" — the cell pair adjacent, single spaces elsewhere, one baseline.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div style=\"position: relative; font-size: 2em;\">" +
            "<div id=\"r\" style=\"position: relative; z-index: 1; color: red; padding: 1px;\">\n" +
            "    <span>\n" +
            "      <span>a</span>\n" +
            "      <span style=\"display: table-cell\">b</span>\n" +
            "      <span style=\"display: table-cell\">c</span>\n" +
            "      <span>d</span>\n" +
            "    </span>\n" +
            "  </div>\n" +
            "<div id=\"g\" style=\"position: absolute; z-index: 2; top: 0; color: green; padding: 1px;\">\n" +
            "    a bc d\n" +
            "  </div>\n" +
            "</div></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var red = Find(page.Root, "DIV", "r")!;
        var green = Find(page.Root, "DIV", "g")!;
        DumpFragments(red, "red  ");
        DumpFragments(green, "green");

        // The green overlay paints its own text straight from DisplayText (no inline items),
        // so measure it the way LayoutInlineRun would consume the line: one line, single spaces.
        using var font = TextMeasure.CreateFont(green);
        var wa = font.MeasureText("a");
        var wSpace = font.MeasureText(" ");
        var wbc = font.MeasureText("bc");
        var expectedDx = green.Box.ContentBox.Left + 1f + wa + wSpace + wbc + wSpace;
        var redD = AllNodes(red).SelectMany(n => n.InlineFragments ?? [])
            .FirstOrDefault(f => f.Text.Trim() == "d");
        True(redD.Rect != default, "red 'd' fragment missing");
        True(Math.Abs(redD.Rect.Left - expectedDx) < 1.5f,
            $"red 'd' must sit at the green text's d offset ({redD.Rect.Left:0.##} vs {expectedDx:0.##})");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lite.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static IEnumerable<LayoutNode> AllNodes(LayoutNode node)
    {
        yield return node;
        foreach (var c in node.Children)
            foreach (var d in AllNodes(c)) yield return d;
    }

    private static void DumpFragments(LayoutNode node, string tag)
    {
        if (node.InlineFragments is { Count: > 0 })
        {
            foreach (var f in node.InlineFragments)
                Console.WriteLine($"[{tag}] {node.TagName} '{f.Text.Trim()}' x={f.Rect.Left:0.##} w={f.Rect.Width:0.##} y={f.Rect.Top:0.##} h={f.Rect.Height:0.##}");
        }
        foreach (var c in node.Children) DumpFragments(c, tag);
    }

    [Test]
    public static void AbsPosTable_CellDoesNotWrap()
    {
        // table-anonymous-objects-023's green reference side: an absolutely positioned table
        // shrink-to-fits so its single cell holds "Some text" on ONE line.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div style=\"position: relative; font-size: 2em;\">" +
            "<div id=\"g\" style=\"position: absolute; z-index: 2; top: 0; color: green; padding: 1px;\">" +
            "<table border=\"5\"><tbody><tr><td>Some text</td></tr></tbody></table>" +
            "</div></div></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var table = Find(page.Root, "TABLE");
        var td = Find(page.Root, "TD");
        True(table != null && td != null, "table/cell missing");
        Console.WriteLine($"[023g] table box: {table!.Box.ContentBox} border={table.Box.Border.Left}/{table.Box.Border.Right}");
        Console.WriteLine($"[023g] td box   : {td!.Box.ContentBox}");
        var (min, max) = IntrinsicSizer.ContentMinMax(table, 600);
        Console.WriteLine($"[023g] table intrinsic (min,max) = ({min:0.##}, {max:0.##})");
        using (var font = TextMeasure.CreateFont(td))
        {
            Console.WriteLine($"[023g] MeasureText('Some text') @td font = {font.MeasureText("Some text"):0.##}");
        }
        var mw = TableEngine.MeasureTableWidth(table, 500, 800, 600);
        Console.WriteLine($"[023g] MeasureTableWidth(avail 500) = {mw:0.##}");
        var fragments = AllNodes(page.Root).SelectMany(n => n.InlineFragments ?? []).ToList();
        foreach (var f in fragments) Console.WriteLine($"[023g] fragment '{f.Text}' x={f.Rect.Left:0.##} y={f.Rect.Top:0.##}");
        var textH = td.Box.ContentBox.Height;
        True(textH < 2 * td.GetLineHeight(td.GetFontSize()),
            $"cell text wrapped: td height {textH:0.##} for line-height {td.GetLineHeight(td.GetFontSize()):0.##}");
    }

    [Test]
    public static void VerticalAlignZero_LineBoxGeometry()
    {
        // vertical-align-004's exact shape: the font shorthand comes from a <style> RULE (not
        // an inline attribute), so both the shorthand's line-height component and the cascade
        // walk must agree. The harness renders this 4px apart with a 28px line box while the
        // engine is coincident with the fallback font — register the real Ahem so the unit
        // harness sees the same metrics the conformance harness does.
        Lite.Layout.FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "div { font: 20px/1 Ahem; position: relative; }" +
            "#d3 { position: absolute; top: 0; }" +
            "#s1 { vertical-align: -0px; }" +
            "</style></head><body>" +
            "<div id=\"d1\"><div id=\"d2\"><span id=\"s1\">X</span></div><div id=\"d3\">X</div></div>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var d2 = Find(page.Root, "DIV", "d2");
        var s1 = Find(page.Root, "SPAN", "s1");
        var d3 = Find(page.Root, "DIV", "d3");
        True(d2 != null && s1 != null && d3 != null, "nodes missing");
        Console.WriteLine($"[va004] d2 lh={d2!.GetLineHeight(d2.GetFontSize()):0.##} normal={d2.IsNormalLineHeight()} box={d2.Box.ContentBox}");
        Console.WriteLine($"[va004] s1 lh={s1!.GetLineHeight(s1.GetFontSize()):0.##} box={s1!.Box.ContentBox}");
        Console.WriteLine($"[va004] d3 box={d3!.Box.ContentBox}");
        foreach (var n in AllNodes(page.Root))
            foreach (var f in n.InlineFragments ?? [])
                Console.WriteLine($"[va004] fragment on {n.TagName}#{n.Id}: '{f.Text}' {f.Rect}");
        True(Math.Abs(s1!.Box.ContentBox.Top - d3!.Box.ContentBox.Top) < 0.5f,
            $"span and reference must coincide ({s1.Box.ContentBox.Top:0.##} vs {d3.Box.ContentBox.Top:0.##})");
    }

    [Test]
    public static void DynamicRemoval_WhiteSpaceBand_ShrinkToFitWidth()
    {
        // table-anonymous-objects-170: onload removes the display:table-cell span; the
        // whitespace leaves around the removal sites must collapse and the abs-pos green
        // overlay must shrink-to-fit to exactly one line of "a" + cell "b" + "c d".
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div style=\"position: relative; font-size: 2em;\">" +
            "<div id=\"g\" style=\"position: absolute; z-index: 2; top: 0; color: green; padding: 1px;\">" +
            "<span><span>a</span><span style=\"display: table-cell\" id=\"t\">e</span>" +
            "<span style=\"display: table-cell\">b</span><span>c d</span></span>" +
            "</div>" +
            "<div id=\"r\" style=\"position: relative; z-index: 1; color: red; padding: 1px;\">a bc d</div>" +
            "</div></body></html>", isSrcdoc: true, "http://test/", 800, 600);

        // onload: remove #t
        var t = Find(page.Root, "SPAN", "t");
        True(t != null, "cell span #t missing");
        t!.Parent!.Children.Remove(t);

        BoxEngine.Layout(page.Root, 800, 600);
        var g = Find(page.Root, "DIV", "g");
        var r = Find(page.Root, "DIV", "r");
        True(g != null && r != null, "overlays missing");
        Console.WriteLine($"[170] green content box : {g!.Box.ContentBox}");
        Console.WriteLine($"[170] red   content box : {r!.Box.ContentBox}");
        Console.WriteLine($"[170] green height {g.Box.ContentBox.Height:0.##} vs red height {r.Box.ContentBox.Height:0.##}");
        var (min, max) = IntrinsicSizer.ContentMinMax(g, 600);
        Console.WriteLine($"[170] green intrinsic (min,max) = ({min:0.##}, {max:0.##})");
        True(g.Box.ContentBox.Height <= r.Box.ContentBox.Height + 1f,
            $"green overlay must be one line tall (green {g.Box.ContentBox.Height:0.##} vs red {r.Box.ContentBox.Height:0.##})");
    }

    [Test]
    public static void ToggleCycle_TrStaysInTheTree()
    {
        // table-anonymous-objects-008: the TOGGLING table sits inside an absolutely positioned
        // overlay (the layer-swap twin of 007); after 9 toggles ending at inline the tr must
        // remain AND its cell content must actually render.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div style=\"position: relative; font-size: 2em;\">" +
            "<div style=\"position: absolute; z-index: 2; top: 0; color: green; padding: 1px;\">" +
            "<table border=\"5\">" +
            "<tr><td>Row 1</td></tr><tr><td>Row 2</td></tr><tr><td>Row 3</td></tr>" +
            "<tr id=\"row4\" style=\"display: none\"><td>Row 4</td></tr>" +
            "</table></div></div></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var row4 = Find(page.Root, "TR", "row4");
        True(row4 != null, "row4 missing after parse");

        var displays = new[] { "inline", "none", "inline", "none", "inline", "none", "inline", "none", "inline" };
        foreach (var d in displays)
        {
            row4!.StyleOverrides["display"] = d;
            BoxEngine.Layout(page.Root, 800, 600);
            var again = Find(page.Root, "TR", "row4");
            if (again == null)
            {
                Console.WriteLine($"[008] DUMP after display={d}:");
                Dump(page.Root, 0);
                True(false, $"row4 dropped from the tree after toggling display to {d}");
            }
            row4 = again;
        }
        BoxEngine.Layout(page.Root, 800, 600);
        Console.WriteLine("[008] final tree:");
        Dump(page.Root, 0);
        foreach (var n in AllNodes(page.Root))
        {
            if (n.TagName is "TD" or "TABLE" or "TR")
                Console.WriteLine($"[008] {n.TagName}{(n.Id != null ? "#" + n.Id : "")} box={n.Box.ContentBox} frags={n.InlineFragments?.Count ?? 0}");
            foreach (var f in n.InlineFragments ?? [])
                Console.WriteLine($"[008] fragment on {n.TagName}: '{f.Text}' {f.Rect}");
        }
        var found = AllNodes(page.Root)
            .SelectMany(n => n.InlineFragments ?? [])
            .Any(f => f.Text.Trim() == "Row 4");
        var innerTd = AllNodes(page.Root).Last(n => n.TagName == "TD");
        True(found || innerTd.Box.ContentBox.Width > 0,
            "row4's 'Row 4' text must lay out after the cycle");

        // Paint and count the pixels in row4's cell region: the harness render shows the
        // green overlay's row4 missing entirely even though its boxes are laid out.
        using var bmp = Drawer.DrawToBitmap(800, 600, page.Root, new Viewport { ViewportHeight = 600 });
        var tdBox = innerTd.Box.ContentBox;
        var painted = 0;
        for (var y = (int)tdBox.Top; y < Math.Min((int)tdBox.Bottom, 600); y++)
            for (var x = (int)tdBox.Left; x < Math.Min((int)tdBox.Right, 800); x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red < 200 || c.Green < 200 || c.Blue < 200) painted++;
            }
        Console.WriteLine($"[008] painted pixels in inner cell region: {painted}");
        True(painted > 50, $"row4's cell must paint content (got {painted} pixels)");
    }

    private static void Dump(LayoutNode node, int depth)
    {
        var d = node.GetDisplay();
        Console.WriteLine($"{new string(' ', depth * 2)}{node.TagName} [{d}]{(node.Id != null ? "#" + node.Id : "")}");
        foreach (var c in node.Children) Dump(c, depth + 1);
    }

    [Test]
    public static void RuleMutation_CaptionCycle_KeepsCell()
    {
        // table-anonymous-objects-023: 10 caption<->"" cycles on a td with forced layouts.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div style=\"position: relative; font-size: 2em;\">" +
            "<table border=\"5\"><tbody><tr><td id=\"t\">Some text</td></tr></tbody></table>" +
            "</div></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var td0 = Find(page.Root, "TD", "t");
        True(td0 != null, "td missing");
        Console.WriteLine($"[023] before: fs={td0!.GetFontSize():0.##} computed={td0.ComputedFontSize:0.##}");
        for (var i = 0; i < 10; i++)
        {
            BoxEngine.Layout(page.Root, 800, 600);
            td0!.StyleOverrides["display"] = "table-caption";
            BoxEngine.Layout(page.Root, 800, 600);
            td0!.StyleOverrides.Remove("display");
            var again = Find(page.Root, "TD", "t");
            True(again != null, $"td dropped after cycle {i}");
            if (!ReferenceEquals(again, td0)) Console.WriteLine($"[023] cycle {i}: td INSTANCE REPLACED");
            td0 = again;
        }
        BoxEngine.Layout(page.Root, 800, 600);
        Console.WriteLine($"[023] after: fs={td0!.GetFontSize():0.##} computed={td0.ComputedFontSize:0.##}");
        Console.WriteLine($"[023] td final box: {td0!.Box.ContentBox}");
        var redTable = Find(page.Root, "TABLE");
        Console.WriteLine($"[023] cycled table box : {redTable!.Box.ContentBox} border={redTable.Box.Border}");
        var green = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div style=\"position: relative; font-size: 2em;\">" +
            "<div style=\"position: absolute; z-index: 2; top: 0; color: green; padding: 1px;\">" +
            "<table border=\"5\"><tbody><tr><td>Some text</td></tr></tbody></table>" +
            "</div></div></body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(green.Root, 800, 600);
        var greenTable = Find(green.Root, "TABLE");
        Console.WriteLine($"[023] fresh table box    : {greenTable!.Box.ContentBox} border={greenTable.Box.Border}");
    }

    private static LayoutNode? Find(LayoutNode node, string tag, string? id = null)
    {
        if (node.TagName == tag && (id == null || node.Attributes.TryGetValue("id", out var v) && v == id))
            return node;
        foreach (var c in node.Children) { var f = Find(c, tag, id); if (f != null) return f; }
        return null;
    }
}
