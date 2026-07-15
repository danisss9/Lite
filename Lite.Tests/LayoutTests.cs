using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
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

    private static LayoutNode Tagged(string tag, Dictionary<string, string> styles)
    {
        var node = new LayoutNode(null, tag, "", _styleCache.Style);
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            node.StyleOverrides[$"margin-{side}"] = "0";
            node.StyleOverrides[$"padding-{side}"] = "0";
            node.StyleOverrides[$"border-{side}-width"] = "0";
        }
        node.StyleOverrides["display"] = "block";
        foreach (var (k, v) in styles) node.StyleOverrides[k] = v;
        return node;
    }

    [Test]
    public static void Details_ClosedHidesNonSummaryContent()
    {
        // A closed <details> shows only its first <summary>; the rest is collapsed. Toggling `open`
        // reflows to reveal it.
        var summary = Tagged("SUMMARY", new() { ["height"] = "20px" });
        var content = Tagged("DIV", new() { ["height"] = "100px" });
        var details = Tagged("DETAILS", new());
        details.AddChild(summary);
        details.AddChild(content);
        var root = LayoutTree(Block(new() { ["width"] = "200px" }, details));

        True(Math.Abs(details.Box.ContentBox.Height - 20f) < 1f,
            $"closed details should show only the 20px summary, got {details.Box.ContentBox.Height}");

        details.Attributes["open"] = "";
        BoxEngine.Layout(root, 800, 600);
        True(Math.Abs(details.Box.ContentBox.Height - 120f) < 1f,
            $"open details should show summary + 100px content (120), got {details.Box.ContentBox.Height}");
    }

    [Test]
    public static void Dialog_HiddenUnlessOpen()
    {
        // A <dialog> is display:none (collapsed) unless it has the open attribute.
        var content = Tagged("DIV", new() { ["height"] = "60px" });
        var dialog = Tagged("DIALOG", new());
        dialog.AddChild(content);
        var root = LayoutTree(Block(new() { ["width"] = "200px" }, dialog));

        True(dialog.Box.ContentBox.Height < 1f,
            $"a closed dialog should be hidden (0 height), got {dialog.Box.ContentBox.Height}");

        dialog.Attributes["open"] = "";
        BoxEngine.Layout(root, 800, 600);
        True(Math.Abs(dialog.Box.ContentBox.Height - 60f) < 1f,
            $"an open dialog should show its 60px content, got {dialog.Box.ContentBox.Height}");
    }

    [Test]
    public static void Progress_DefaultInlineBlockSize()
    {
        // <progress> with no explicit size gets the UA replaced-element default (160x16).
        var progress = Tagged("PROGRESS", new() { ["display"] = "inline-block" });
        LayoutTree(Block(new() { ["width"] = "400px" }, progress));
        True(Math.Abs(progress.Box.ContentBox.Width - 160f) < 1f,
            $"default progress width should be 160, got {progress.Box.ContentBox.Width}");
        True(Math.Abs(progress.Box.ContentBox.Height - 16f) < 1f,
            $"default progress height should be 16, got {progress.Box.ContentBox.Height}");
    }

    [Test]
    public static void Meter_DefaultInlineBlockSize()
    {
        var meter = Tagged("METER", new() { ["display"] = "inline-block" });
        LayoutTree(Block(new() { ["width"] = "400px" }, meter));
        True(Math.Abs(meter.Box.ContentBox.Width - 80f) < 1f,
            $"default meter width should be 80, got {meter.Box.ContentBox.Width}");
        True(Math.Abs(meter.Box.ContentBox.Height - 16f) < 1f,
            $"default meter height should be 16, got {meter.Box.ContentBox.Height}");
    }

    [Test]
    public static void Progress_PaintsDeterminateFill()
    {
        // A determinate <progress value=0.5 max=1> paints a blue fill over the left half of its
        // grey track. Sample 25% across (filled → blue) and 75% (empty → grey), relative to the
        // computed box so the assertion is position-independent.
        var progress = Tagged("PROGRESS",
            new() { ["display"] = "inline-block", ["width"] = "200px", ["height"] = "20px" });
        progress.Attributes["value"] = "0.5";
        progress.Attributes["max"] = "1";
        var root = LayoutTree(Block(new() { ["width"] = "400px" }, progress));

        using var bmp = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
        var box = progress.Box.ContentBox;
        var y = (int)box.MidY;
        var fill = bmp.GetPixel((int)(box.Left + box.Width * 0.25f), y);
        var empty = bmp.GetPixel((int)(box.Left + box.Width * 0.75f), y);

        True(fill.Blue > 150 && fill.Red < 120,
            $"filled region should be blue (#0078D7), got {fill}");
        True(empty.Red > 150 && Math.Abs(empty.Red - empty.Blue) < 20 && Math.Abs(empty.Red - empty.Green) < 20,
            $"empty region should be the grey track, got {empty}");
    }

    // -------------------------------------------------------------------------
    // Intrinsic sizing / shrink-to-fit (§10.3.5 / §10.3.7)
    // -------------------------------------------------------------------------

    private static LayoutNode InlineBlock(Dictionary<string, string> styles, params LayoutNode[] children)
    {
        var node = Block(styles, children);
        node.StyleOverrides["display"] = "inline-block";
        return node;
    }

    [Test]
    public static void ShrinkToFit_AbsPos_UsesChildMaxContentWidth()
    {
        // An auto-width abs-pos box shrinks to fit: its max-content is its widest block child's
        // outer width. child content 120 + padding 10*2 = 140.
        var child = Block(new() { ["width"] = "120px", ["padding-left"] = "10px", ["padding-right"] = "10px", ["height"] = "20px" });
        var abs = Block(new() { ["position"] = "absolute", ["width"] = "auto" }, child);
        LayoutTree(Block(new() { ["width"] = "800px" }, abs));
        True(Math.Abs(abs.Box.ContentBox.Width - 140f) < 0.5f,
            $"abs-pos shrink-to-fit width should be child outer 140, got {abs.Box.ContentBox.Width}");
    }

    [Test]
    public static void ShrinkToFit_StackedBlocks_TakeWidest()
    {
        // Block children stack, so max-content is the widest — 100, not 60 and not their sum.
        var a = Block(new() { ["width"] = "60px", ["height"] = "20px" });
        var b = Block(new() { ["width"] = "100px", ["height"] = "20px" });
        var abs = Block(new() { ["position"] = "absolute", ["width"] = "auto" }, a, b);
        LayoutTree(Block(new() { ["width"] = "800px" }, abs));
        True(Math.Abs(abs.Box.ContentBox.Width - 100f) < 0.5f,
            $"stacked-block shrink-to-fit should take the widest child (100), got {abs.Box.ContentBox.Width}");
    }

    [Test]
    public static void ShrinkToFit_InlineChildren_SumAcrossLine()
    {
        // Two inline-blocks flow on one line, so max-content is their sum (50 + 50 = 100) — the key
        // improvement over the old "widest single child" heuristic (which returned 50).
        var a = InlineBlock(new() { ["width"] = "50px", ["height"] = "20px" });
        var b = InlineBlock(new() { ["width"] = "50px", ["height"] = "20px" });
        var abs = Block(new() { ["position"] = "absolute", ["width"] = "auto" }, a, b);
        LayoutTree(Block(new() { ["width"] = "800px" }, abs));
        True(Math.Abs(abs.Box.ContentBox.Width - 100f) < 0.5f,
            $"inline children should sum across the line (100), got {abs.Box.ContentBox.Width}");
    }

    [Test]
    public static void ShrinkToFit_MinContentFloorsAboveAvailable()
    {
        // min-content can exceed the available width: a fixed 300px child fixes both min and max to
        // 300, so the box overflows its 200px containing block rather than shrinking below content.
        var child = Block(new() { ["width"] = "300px", ["height"] = "20px" });
        var abs = Block(new() { ["position"] = "absolute", ["width"] = "auto" }, child);
        LayoutTree(Block(new() { ["width"] = "200px" }, abs));
        True(Math.Abs(abs.Box.ContentBox.Width - 300f) < 0.5f,
            $"min-content (300) should floor shrink-to-fit above the 200px available, got {abs.Box.ContentBox.Width}");
    }

    [Test]
    public static void ShrinkToFit_Float_UsesIntrinsicWidth()
    {
        // A float with auto width and an explicit-width child shrinks to that child's outer width.
        var child = Block(new() { ["width"] = "90px", ["height"] = "20px" });
        var flt = Block(new() { ["float"] = "left", ["width"] = "auto" }, child);
        LayoutTree(Block(new() { ["width"] = "800px" }, flt));
        True(Math.Abs(flt.Box.ContentBox.Width - 90f) < 0.5f,
            $"float shrink-to-fit width should be child 90, got {flt.Box.ContentBox.Width}");
    }
}
