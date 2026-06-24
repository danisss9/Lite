using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// Phase 3 — box-model geometry assertions. Nodes are built with explicit StyleOverrides
/// (read first by TryResolveStyle), so these exercise the engine's own length/cascade code
/// deterministically without depending on AngleSharp's fragment style computation.
/// </summary>
public static class LayoutTests
{
    private static readonly ICssStyleDeclarationCache _styleCache = new();

    // A throwaway non-null ICssStyleDeclaration for LayoutNode construction; all values come
    // from StyleOverrides below.
    private sealed class ICssStyleDeclarationCache
    {
        public AngleSharp.Css.Dom.ICssStyleDeclaration Style { get; } =
            Parser.ParseFragment("<div></div>")[0].Style;
    }

    private static LayoutNode Block(Dictionary<string, string> styles, params LayoutNode[] children)
    {
        var node = new LayoutNode(null, "DIV", "", _styleCache.Style);
        // Zero the box model first so the shared fallback style can't contribute phantom
        // padding/border/margin; callers override specific sides via `styles`.
        node.StyleOverrides["display"] = "block";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            node.StyleOverrides[$"margin-{side}"] = "0";
            node.StyleOverrides[$"padding-{side}"] = "0";
            node.StyleOverrides[$"border-{side}-width"] = "0";
        }
        foreach (var (k, v) in styles) node.StyleOverrides[k] = v;
        foreach (var c in children) node.AddChild(c);
        return node;
    }

    private static LayoutNode LayoutTree(LayoutNode content, int vw = 800, int vh = 600)
    {
        var root = new LayoutNode(null, "HTML", "", _styleCache.Style);
        var body = new LayoutNode(null, "BODY", "", _styleCache.Style);
        root.StyleOverrides["display"] = "block";
        body.StyleOverrides["display"] = "block";
        root.AddChild(body);
        body.AddChild(content);
        BoxEngine.Layout(root, vw, vh);
        return root;
    }

    [Test]
    public static void PercentMargin_ResolvesAgainstContainingBlockWidth()
    {
        var inner = Block(new() { ["margin-top"] = "25%", ["height"] = "40px" });
        var cb = Block(new() { ["width"] = "200px" }, inner);
        LayoutTree(cb);
        True(Math.Abs(inner.Box.Margin.Top - 50f) < 0.5f,
            $"expected margin-top 50 (25% of 200w), got {inner.Box.Margin.Top}");
    }

    [Test]
    public static void PercentPadding_ResolvesAgainstContainingBlockWidth()
    {
        var inner = Block(new() { ["padding-top"] = "10%", ["height"] = "40px" });
        var cb = Block(new() { ["width"] = "200px" }, inner);
        LayoutTree(cb);
        True(Math.Abs(inner.Box.Padding.Top - 20f) < 0.5f,
            $"expected padding-top 20 (10% of 200w), got {inner.Box.Padding.Top}");
    }

    [Test]
    public static void AbsoluteUnits_ResolveToPixels()
    {
        var u = Block(new() { ["width"] = "1in", ["height"] = "75pt" });
        LayoutTree(u);
        True(Math.Abs(u.Box.ContentBox.Width - 96f) < 0.5f, $"expected width 96 (1in), got {u.Box.ContentBox.Width}");
        True(Math.Abs(u.Box.ContentBox.Height - 100f) < 0.5f, $"expected height 100 (75pt), got {u.Box.ContentBox.Height}");
    }

    [Test]
    public static void RemUnit_ResolvesAgainstRootFontSize()
    {
        var r = Block(new() { ["width"] = "2rem", ["height"] = "10px" });
        LayoutTree(r);
        True(Math.Abs(r.Box.ContentBox.Width - 32f) < 0.5f, $"expected width 32 (2rem @16px root), got {r.Box.ContentBox.Width}");
    }

    [Test]
    public static void AbsPosBfc_ContainsFloatWithBottomMargin()
    {
        // §10.6.7 / BFC: an abs-pos container's auto-height includes a float's margin-box.
        // float h=48 + margin-bottom=48 → container height 96.
        var ws1 = new LayoutNode(null, "#text", "\n  ", _styleCache.Style);
        ws1.StyleOverrides["display"] = "inline";
        var flt = Block(new() { ["float"] = "left", ["width"] = "100%", ["height"] = "48px", ["margin-bottom"] = "48px" });
        var ws2 = new LayoutNode(null, "#text", "\n", _styleCache.Style);
        ws2.StyleOverrides["display"] = "inline";
        var container = Block(new() { ["position"] = "absolute", ["width"] = "96px", ["height"] = "auto" }, ws1, flt, ws2);
        LayoutTree(container);
        var h = container.Box.ContentBox.Height;
        True(Math.Abs(h - 96f) < 1f, $"expected abs-pos BFC height 96 (float 48 + mb 48), got {h}");
    }

    [Test]
    public static void BlockInInline_PromotesInlineToBlock()
    {
        // CSS 2.1 §9.2.1.1: an inline box containing a block is broken around it. We model
        // that by promoting the inline to a block container, so the block child stacks.
        var blockChild = Block(new() { ["height"] = "50px" });
        var span = new LayoutNode(null, "SPAN", "", _styleCache.Style);
        span.StyleOverrides["display"] = "inline";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            span.StyleOverrides[$"margin-{side}"] = "0";
            span.StyleOverrides[$"padding-{side}"] = "0";
            span.StyleOverrides[$"border-{side}-width"] = "0";
        }
        span.AddChild(blockChild);
        var container = Block(new() { ["width"] = "200px" }, span);
        LayoutTree(container);

        True(span.GetDisplay() == Lite.Extensions.DisplayType.Block,
            "inline span containing a block child should be promoted to a block container");
        True(Math.Abs(span.Box.ContentBox.Height - 50f) < 1f,
            $"promoted span height should be the block child's 50px, got {span.Box.ContentBox.Height}");
    }

    [Test]
    public static void ParentLastChild_MarginCollapsesThrough()
    {
        // A parent with no bottom border/padding and auto height: the last child's bottom margin
        // collapses through, so the parent's content box is just the child (50), not 50+30.
        var child = Block(new() { ["height"] = "50px", ["margin-bottom"] = "30px" });
        var parent = Block(new() { ["width"] = "100px" }, child);
        var container = Block(new() { ["width"] = "200px" }, parent);
        LayoutTree(container);
        var h = parent.Box.ContentBox.Height;
        True(Math.Abs(h - 50f) < 1f, $"expected parent content height 50 (child margin collapses through), got {h}");
    }

    [Test]
    public static void BottomPadding_PreventsCollapseThrough()
    {
        // With bottom padding, the child's bottom margin is contained: content height = 50 + 30.
        var child = Block(new() { ["height"] = "50px", ["margin-bottom"] = "30px" });
        var parent = Block(new() { ["width"] = "100px", ["padding-bottom"] = "5px" }, child);
        var container = Block(new() { ["width"] = "200px" }, parent);
        LayoutTree(container);
        var h = parent.Box.ContentBox.Height;
        True(Math.Abs(h - 80f) < 1f, $"expected parent content height 80 (margin contained by bottom padding), got {h}");
    }

    private static LayoutNode TableCell(string text)
    {
        var node = new LayoutNode(null, "TD", text, _styleCache.Style);
        node.StyleOverrides["display"] = "table-cell";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            node.StyleOverrides[$"margin-{side}"] = "0";
            node.StyleOverrides[$"padding-{side}"] = "0";
            node.StyleOverrides[$"border-{side}-width"] = "0";
        }
        return node;
    }

    [Test]
    public static void AutoTable_ColumnWidthsTrackContent()
    {
        // CSS 2.1 §17.5.2.2: with automatic layout, a column with short content stays narrow and
        // a column with long content takes the rest — not an even 50/50 split.
        var c1 = TableCell("Hi");
        var c2 = TableCell("this is a much longer piece of cell text");
        var row = new LayoutNode(null, "TR", "", _styleCache.Style);
        row.StyleOverrides["display"] = "table-row";
        row.AddChild(c1);
        row.AddChild(c2);
        var table = new LayoutNode(null, "TABLE", "", _styleCache.Style);
        table.StyleOverrides["display"] = "table";
        table.StyleOverrides["width"] = "300px";
        table.StyleOverrides["border-spacing"] = "0";
        table.AddChild(row);
        LayoutTree(table);

        var w1 = c1.Box.ContentBox.Width;
        var w2 = c2.Box.ContentBox.Width;
        True(w1 > 0f && w2 > 0f, $"both columns should have width (got {w1}, {w2})");
        True(w1 < w2, $"short-content column should be narrower than long-content column (got {w1} vs {w2})");
        True(Math.Abs((w1 + w2) - 300f) < 1.5f, $"columns should fill the 300px table (got {w1 + w2})");
    }

    [Test]
    public static void SelfCollapsingBlock_CollapsesAllAdjoiningMargins()
    {
        // CSS 2.1 §8.3.1: an empty block (no content/border/padding/height) is self-collapsing — its
        // own top and bottom margins are adjoining and collapse with the surrounding margins into a
        // single margin. collapse(10, 20, 40, 5) = 40, so blue sits 50 + 40 below green's top.
        var green = Block(new() { ["height"] = "50px", ["margin-bottom"] = "10px" });
        var empty = Block(new() { ["margin-top"] = "20px", ["margin-bottom"] = "40px" });
        var blue = Block(new() { ["height"] = "50px", ["margin-top"] = "5px" });
        var container = Block(new() { ["width"] = "200px" }, green, empty, blue);
        LayoutTree(container);
        var delta = blue.Box.ContentBox.Top - green.Box.ContentBox.Top;
        True(Math.Abs(delta - 90f) < 1f, $"expected blue 90px below green (50 + collapsed 40), got {delta}");
    }

    [Test]
    public static void NegativeMarginCollapse_PullsBoxesTogether()
    {
        // green h=50 mb=30; blue h=50 mt=-10 → collapsed = 30 + (-10) = 20 → blue top at 70.
        var green = Block(new() { ["height"] = "50px", ["margin-bottom"] = "30px" });
        var blue = Block(new() { ["height"] = "50px", ["margin-top"] = "-10px" });
        var container = Block(new() { ["width"] = "200px" }, green, blue);
        LayoutTree(container);
        True(Math.Abs(blue.Box.ContentBox.Top - green.Box.ContentBox.Top - 70f) < 0.5f,
            $"expected blue 70px below green (50 + collapsed 20), got {blue.Box.ContentBox.Top - green.Box.ContentBox.Top}");
    }
}
