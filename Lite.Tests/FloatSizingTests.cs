using Lite;
using Lite.Layout;
using Lite.Extensions;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §10.3.5 / §10.3.2: a floated box's computed 'auto' left/right margins are USED as 0 —
/// not the in-flow block's §10.3.3 centering split (float-non-replaced-width-001/003/005,
/// float-replaced-width-001/003/005/006/011).
/// </summary>
public static class FloatSizingTests
{
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

    private static LayoutNode? FindNode(LayoutNode n, Func<LayoutNode, bool> pred) =>
        pred(n) ? n : n.Children.Select(c => FindNode(c, pred)).FirstOrDefault(r => r is not null);

    [Test]
    public static void AutoMarginLeft_FloatUsesZeroNotCentering()
    {
        // float-non-replaced-width-001's shape: margin-left:auto on a left float must place it at
        // the containing block's left edge, not at the centering split (§10.3.5: used value 0).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='border:solid black;height:2in;width:2in'>" +
            "<div style='background-color:blue;float:left;margin-left:auto;height:1in;width:1in'></div>" +
            "</div></body></html>");
        var f = FindNode(root, n => n.GetFloat() == FloatType.Left);
        True(f is not null, "float node not found");
        True(f!.Box.Margin.Left == 0f, $"auto margin-left must be used as 0, got {f.Box.Margin.Left}");
        // The float's border box starts at the container's content edge (the 3px border aside).
        True(Math.Abs(f.Box.ContentBox.Left - 3f) < 0.5f,
            $"float must sit at the containing block's content left edge, got {f.Box.ContentBox.Left}");
    }

    [Test]
    public static void AutoMarginRight_FloatUsesZeroFlushRight()
    {
        // float-non-replaced-width-003's shape: margin-right:auto on a right float leaves it
        // flush with the containing block's right edge (§10.3.5: used value 0).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='border:solid black;height:2in;width:2in'>" +
            "<div style='background-color:blue;float:right;margin-right:auto;height:1in;width:1in'></div>" +
            "</div></body></html>");
        var f = FindNode(root, n => n.GetFloat() == FloatType.Right);
        True(f is not null, "float node not found");
        True(f!.Box.Margin.Right == 0f, $"auto margin-right must be used as 0, got {f.Box.Margin.Right}");
        True(Math.Abs((195f - f.Box.ContentBox.Right)) < 0.5f,
            $"right float must sit flush with the content right edge, got right {f.Box.ContentBox.Right}");
    }

    [Test]
    public static void FloatNotHigherThanEarlierFloat_Top()
    {
        // floats-146's shape: A (12em left), B (14em left — does not fit beside A, drops), then
        // C (4em right). §9.5.1 rule 5: C's top may not be higher than B's (an earlier float),
        // so C lands beside B on the second row instead of beside A on the first.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='width:20em;font-size:16px'>" +
            "<div style='padding:0.2em;margin:0.2em;border:1px solid;width:12em;float:left'>A</div>" +
            "<div style='padding:0.2em;margin:0.2em;border:1px solid;width:14em;float:left'>B</div>" +
            "<div style='padding:0.2em;margin:0.2em;border:1px solid;width:4em;float:right'>C</div>" +
            "</div></body></html>");
        var a = FindNode(root, n => n.GetFloat() == FloatType.Left && n.DisplayText == "A")!;
        var b = FindNode(root, n => n.GetFloat() == FloatType.Left && n.DisplayText == "B")!;
        var c = FindNode(root, n => n.GetFloat() == FloatType.Right && n.DisplayText == "C")!;
        True(a is not null && b is not null && c is not null, "float nodes not found");
        True(b.Box.ContentBox.Top > a.Box.ContentBox.Top + 0.5f,
            "B must drop below A (it does not fit beside it)");
        True(c.Box.ContentBox.Top >= b.Box.ContentBox.Top - 0.5f,
            $"C's top must not be above B's (§9.5.1 rule 5), got C {c.Box.ContentBox.Top} vs B {b.Box.ContentBox.Top}");
    }

    [Test]
    public static void InRunFloat_LaysOutContentLikeDirectChildFloat()
    {
        // stack-floats-001's shape: a float met inside an inline run (text before it) with an
        // explicit height still lays out its block child, and a floated table met the same way
        // lays out through the table engine (border-conflict-style-107's floated tables).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='width:200px;font:20px/1 Ahem'>x" +
            "<div id='f' style='float:left;width:50px;height:60px;padding:20px 0'>" +
            "<div id='inner' style='height:60px'></div></div>" +
            "<table id='t' style='float:right;border-spacing:0'><tr><td id='cell' style='width:30px;height:30px;padding:0'></td></tr></table>" +
            "x</div></body></html>");
        var f = FindNode(root, n => n.Id == "f")!;
        var inner = FindNode(root, n => n.Id == "inner")!;
        var cell = FindNode(root, n => n.Id == "cell")!;
        True(f is not null && inner is not null && cell is not null, "nodes not found");
        True(Math.Abs(inner.Box.ContentBox.Height - 60f) < 0.5f,
            $"the float's block child must be laid out, got height {inner.Box.ContentBox.Height}");
        True(Math.Abs(inner.Box.ContentBox.Top - f.Box.ContentBox.Top) < 0.5f,
            $"the child must sit at the float's content top, got {inner.Box.ContentBox.Top} vs {f.Box.ContentBox.Top}");
        True(cell.Box.ContentBox.Width > 29.5f && cell.Box.ContentBox.Right > 199.5f - 0.5f,
            $"the floated table's cell must be laid out at the right edge, got {cell.Box.ContentBox}");
    }

    [Test]
    public static void LeadingInRunFloat_MovesTheEmptyLinesStartEdge()
    {
        // floats-040's shape: whitespace, a left float, then an inline-block — one inline run.
        // §9.5: the line box beside the float is shortened by its margin box, so the inline-block
        // starts right of the float even though the line held no content when it was placed.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='width:200px'>\n  <div style='float:left;width:50px;height:50px'></div>\n" +
            "  <div id='ib' style='display:inline-block;width:40px;height:40px'></div>\n</div></body></html>");
        var ib = FindNode(root, n => n.Id == "ib")!;
        True(ib is not null, "inline-block not found");
        True(Math.Abs(ib.Box.ContentBox.Left - 50f) < 0.5f,
            $"the inline-block must start at the float's right edge, got {ib.Box.ContentBox.Left}");
        True(ib.Box.ContentBox.Top < 0.5f, $"it stays on the float's line, got top {ib.Box.ContentBox.Top}");
    }
}
