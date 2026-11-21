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

    [Test]
    public static void IndentedLineBesideLeadingFloat_ShiftsBelowWithItsIndent()
    {
        // line-break-after-leading-float's shape: a 40px text-indent plus a 60px inline-block do
        // not fit beside a 60px float in a 100px block. §9.5 rule 7 shifts the (still empty)
        // first line below the float, and it keeps its first-line indent (§16.1).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='width:100px;text-indent:40px'>\n  <div style='float:left;width:60px;height:10px'></div>\n" +
            "  <div id='ib' style='display:inline-block;width:60px;height:20px'></div>\n</div></body></html>");
        var ib = FindNode(root, n => n.Id == "ib")!;
        True(ib is not null, "inline-block not found");
        True(Math.Abs(ib.Box.ContentBox.Left - 40f) < 0.5f,
            $"the shifted first line keeps its 40px indent, got left {ib.Box.ContentBox.Left}");
        True(ib.Box.ContentBox.Top >= 9.5f, $"the line moves below the float, got top {ib.Box.ContentBox.Top}");
    }
    [Test]
    public static void BfcBesideFloats_AvoidsFloatStartingInsideItsHeight()
    {
        // floats-wrap-top-below-bfc-002l's shape: a 150px left float (75px tall) and a 300px
        // right float that drops below it (y=75..150). §9.5: a BFC's border box must not overlap
        // a float's margin box over its whole height, so the second 200x50 BFC at y=50 — whose
        // top band is 250px wide but whose lower half meets the right float — moves below the
        // floats instead of overlapping the right one.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:400px'>" +
            "<div style='float:left;width:150px;height:75px'></div>" +
            "<div style='float:right;width:300px;height:75px'></div>" +
            "<div id='a' style='overflow:hidden;width:200px;height:50px'></div>" +
            "<div id='b' style='overflow:hidden;width:200px;height:50px'></div>" +
            "</div></body></html>");
        var a = FindNode(root, n => n.Id == "a")!;
        var b = FindNode(root, n => n.Id == "b")!;
        True(a is not null && b is not null, "BFC nodes not found");
        True(a.Box.ContentBox.Top < 0.5f && Math.Abs(a.Box.ContentBox.Left - 150f) < 0.5f,
            $"the first BFC sits beside the left float, got {a.Box.ContentBox}");
        True(Math.Abs(b.Box.ContentBox.Top - 150f) < 0.5f && b.Box.ContentBox.Left < 0.5f,
            $"the second BFC must move below the lower float, got {b.Box.ContentBox}");
    }

    [Test]
    public static void BfcBesideFloats_StepsDownOneFloatBottomAtATime()
    {
        // floats-wrap-top-below-bfc-003l's shape: 250px floats on both sides, the right one
        // dropped to y=75. The second 100x50 BFC fits nowhere at y=50, but beside the lower
        // right float at y=75 (band 0..150) it does — rule 6 moves it past the NEXT float
        // bottom, not below every float.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:400px'>" +
            "<div style='float:left;width:250px;height:75px'></div>" +
            "<div style='float:right;width:250px;height:75px'></div>" +
            "<div id='a' style='overflow:hidden;width:100px;height:50px'></div>" +
            "<div id='b' style='overflow:hidden;width:100px;height:50px'></div>" +
            "</div></body></html>");
        var b = FindNode(root, n => n.Id == "b")!;
        True(b is not null, "BFC node not found");
        True(Math.Abs(b.Box.ContentBox.Top - 75f) < 0.5f && b.Box.ContentBox.Left < 0.5f,
            $"the second BFC must sit at (0,75) beside the lower float, got {b.Box.ContentBox}");
    }

    [Test]
    public static void EmptyLine_AvoidsFloatStartingInsideItsHeight()
    {
        // floats-wrap-top-below-inline-002l's shape: the second 200x50 inline-block starts a
        // line at y=50 whose top band (150..400) holds it, but the right float from y=75
        // narrows the band over the line's height to nothing. §9.5 rule 7: the empty line
        // shifts down until the content fits — below the right float, at y=150.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:400px'>" +
            "<div style='float:left;width:150px;height:75px'></div>" +
            "<div style='float:right;width:300px;height:75px'></div>" +
            "<span id='a' style='display:inline-block;vertical-align:top;width:200px;height:50px'></span>\n" +
            "<span id='b' style='display:inline-block;vertical-align:top;width:200px;height:50px'></span>" +
            "</div></body></html>");
        var a = FindNode(root, n => n.Id == "a")!;
        var b = FindNode(root, n => n.Id == "b")!;
        True(a is not null && b is not null, "span nodes not found");
        True(a.Box.ContentBox.Top < 0.5f && Math.Abs(a.Box.ContentBox.Left - 150f) < 0.5f,
            $"the first span sits beside the left float, got {a.Box.ContentBox}");
        True(Math.Abs(b.Box.ContentBox.Top - 150f) < 0.5f && b.Box.ContentBox.Left < 0.5f,
            $"the second span's line must move below the lower float, got {b.Box.ContentBox}");
    }

    [Test]
    public static void FloatRule3_AllowsExtendingPastContainingBlockWhenNoOppositeFloatOverlap()
    {
        // floats-rule3-outside-left-001's shape: a 50px right float in a 500px BFC (x=450..500),
        // and inside a 'margin-right: 100px' child block (x=0..400) a 425px left float. §9.5.1
        // rule 7 only forbids extending past the containing block's right edge when another left
        // float sits to its left; rule 3 only forbids extending past the right float's left edge
        // (450). Since 425 <= 450 and no left float precedes it, the float stays at y=0.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='float:left;width:500px;height:500px'>" +
            "<div style='float:right;width:50px;height:300px'></div>" +
            "<div style='margin-right:100px'>" +
            "<div id='f' style='float:left;width:425px;height:10px'></div>" +
            "</div></div></body></html>");
        var f = FindNode(root, n => n.Id == "f")!;
        True(f is not null, "float not found");
        True(f.Box.ContentBox.Top < 0.5f && f.Box.ContentBox.Left < 0.5f,
            $"the 425px left float fits at (0,0) beside the 450..500 right float, got {f.Box.ContentBox}");
    }

    [Test]
    public static void ClearedFloat_DoesNotAdvanceNormalFlowCursor()
    {
        // floats-zero-height-wrap-001/002 & floats-placement-001: 'clear' on a float is §9.5.2
        // constraint #10 on the float's own outer top; the float is out of flow and must not push
        // subsequent normal-flow inline content down to the cleared float's Y.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:500px'>" +
            "<div style='float:left;width:10px;height:30px'></div>" +
            "<div id='f2' style='float:left;clear:left;width:100px;height:0'></div>" +
            "<span id='s1' style='display:inline-block;vertical-align:bottom;width:300px;height:20px'></span>" +
            "<span id='s2' style='display:inline-block;vertical-align:bottom;width:300px;height:20px'></span>" +
            "</div></body></html>");
        var f2 = FindNode(root, n => n.Id == "f2")!;
        var s1 = FindNode(root, n => n.Id == "s1")!;
        var s2 = FindNode(root, n => n.Id == "s2")!;
        True(f2 is not null && s1 is not null && s2 is not null, "nodes not found");
        True(Math.Abs(f2.Box.ContentBox.Top - 30f) < 0.5f, $"cleared float sits at y=30, got {f2.Box.ContentBox.Top}");
        True(s1.Box.ContentBox.Top < 0.5f && Math.Abs(s1.Box.ContentBox.Left - 10f) < 0.5f,
            $"first line starts at y=0 beside the 10px float, got {s1.Box.ContentBox}");
        True(Math.Abs(s2.Box.ContentBox.Top - 20f) < 0.5f && Math.Abs(s2.Box.ContentBox.Left - 100f) < 0.5f,
            $"second line (y=20..40) wraps past the y=30 zero-height float to x=100, got {s2.Box.ContentBox}");
    }

    [Test]
    public static void AutoHeightBfc_RefitsWhenGrownHeightMeetsWiderFloat()
    {
        // floats-bfc-003's shape: #float1 is 100x25 at y=0..25, #float2 is 200x25 at y=25..50.
        // An auto-height BFC containing two 100x50 floats initially sees a 200px band at y=0 and
        // lays out 50px tall, which overlaps #float2 at y=25..50; re-fitting over its 50px height
        // narrows its band to 100px (x=200..300), stacking its inner floats to 100px tall.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:300px'>" +
            "<div style='float:left;clear:left;width:100px;height:25px'></div>" +
            "<div style='float:left;clear:left;width:200px;height:25px'></div>" +
            "<div id='bfc' style='overflow:hidden'>" +
            "<div style='float:left;width:100px;height:50px'></div>" +
            "<div style='float:left;width:100px;height:50px'></div>" +
            "</div></div></body></html>");
        var bfc = FindNode(root, n => n.Id == "bfc")!;
        True(bfc is not null, "bfc not found");
        True(Math.Abs(bfc.Box.ContentBox.Left - 200f) < 0.5f &&
             Math.Abs(bfc.Box.ContentBox.Width - 100f) < 0.5f &&
             Math.Abs(bfc.Box.ContentBox.Height - 100f) < 0.5f,
            $"#bfc must narrow to 100x100 at x=200 to avoid #float2, got {bfc.Box.ContentBox}");
    }
}
