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
    public static void BlockInInline_BreaksInlineAroundBlock()
    {
        // CSS 2.1 §9.2.1.1: an inline box containing an in-flow block is broken around it — the
        // block is hoisted to sibling position in the nearest block container and stacks as a
        // full-width block. Text before/after become anonymous line boxes (inline pieces).
        var blockChild = Block(new() { ["height"] = "50px" });
        var before = new LayoutNode(null, "#text", "before", _styleCache.Style) { };
        before.StyleOverrides["display"] = "inline";
        var after = new LayoutNode(null, "#text", "after", _styleCache.Style) { };
        after.StyleOverrides["display"] = "inline";
        var container = Block(new() { ["width"] = "200px" }, Span(before, blockChild, after));
        LayoutTree(container);

        // The block child was hoisted out of the span and laid out as a real block: full container
        // width (200) and its own 50px height, positioned below the leading "before" line box.
        True(Math.Abs(blockChild.Box.ContentBox.Width - 200f) < 1f,
            $"hoisted block should fill the 200px container width, got {blockChild.Box.ContentBox.Width}");
        True(Math.Abs(blockChild.Box.ContentBox.Height - 50f) < 1f,
            $"hoisted block should keep its 50px height, got {blockChild.Box.ContentBox.Height}");
        True(blockChild.Box.ContentBox.Top > 1f,
            $"hoisted block should sit below the leading inline piece, got top {blockChild.Box.ContentBox.Top}");
        // The block is hoisted to sibling position: its parent is the block container itself.
        True(blockChild.Parent == container,
            "hoisted block's parent should be the block container, not the original span");
    }

    [Test]
    public static void BlockInInline_MultipleBlocksStackWithMargins()
    {
        // block-in-inline-007 shape: A <block/> <block/> D — the two blocks (margin-top 10) are
        // hoisted out of the inline and stack; text A and D become inline pieces. Each block's
        // top margin collapses correctly against the running flow.
        var b1 = Block(new() { ["height"] = "10px", ["margin-top"] = "10px", ["margin-bottom"] = "10px" });
        var b2 = Block(new() { ["height"] = "10px", ["margin-top"] = "10px", ["margin-bottom"] = "10px" });
        var a = new LayoutNode(null, "#text", "A", _styleCache.Style);
        a.StyleOverrides["display"] = "inline";
        var d = new LayoutNode(null, "#text", "D", _styleCache.Style);
        d.StyleOverrides["display"] = "inline";
        var container = Block(new() { ["width"] = "200px" }, Span(a, b1, b2, d));
        LayoutTree(container);

        // Both blocks are full-width siblings; b2 sits below b1 with their 10px margins collapsed.
        True(Math.Abs(b1.Box.ContentBox.Width - 200f) < 1f,
            $"b1 should fill container width, got {b1.Box.ContentBox.Width}");
        True(Math.Abs(b2.Box.ContentBox.Width - 200f) < 1f,
            $"b2 should fill container width, got {b2.Box.ContentBox.Width}");
        var gap = b2.Box.ContentBox.Top - b1.Box.ContentBox.Bottom;
        True(Math.Abs(gap - 10f) < 1f,
            $"collapsed margin between the two hoisted blocks should be 10px, got {gap}");
    }

    /// <summary>An inline SPAN (zeroed box model) wrapping the given children — used for
    /// block-in-inline propagation tests.</summary>
    private static LayoutNode Span(params LayoutNode[] children)
    {
        var span = new LayoutNode(null, "SPAN", "", _styleCache.Style);
        span.StyleOverrides["display"] = "inline";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            span.StyleOverrides[$"margin-{side}"] = "0";
            span.StyleOverrides[$"padding-{side}"] = "0";
            span.StyleOverrides[$"border-{side}-width"] = "0";
        }
        foreach (var c in children) span.AddChild(c);
        return span;
    }

    [Test]
    public static void BlockInInline_EmptyPiecesAreDroppedBlockHoisted()
    {
        // CSS 2.1 §9.2.1.1: an inline whose only in-flow content is a block (no text on either side)
        // produces empty pieces that show nothing — only the block is laid out, hoisted to sibling
        // position at the container's content top (this is the block-in-inline-003 "no red" shape).
        var block = Block(new() { ["height"] = "40px" });
        var container = Block(new() { ["width"] = "200px" }, Span(block));
        LayoutTree(container);

        True(block.Parent == container, "the block should be hoisted to the block container");
        True(container.Children.Count == 1 && container.Children[0] == block,
            "no empty inline pieces should be emitted around a block that is the inline's only content");
        True(Math.Abs(block.Box.ContentBox.Top - 0f) < 0.5f,
            $"the hoisted block should sit at the container content top (0), got {block.Box.ContentBox.Top}");
    }

    [Test]
    public static void BlockInInline_FloatedInlineContainingBlockIsNotBroken()
    {
        // A block inside a FLOATED inline is out of flow relative to the outer inline, so §9.2.1.1
        // does NOT break the outer inline around it (the float is its own formatting root). This
        // guards Acid2's floated span>em>strong smile, which must stay nested (regression guard).
        var strong = Block(new() { ["width"] = "60px", ["height"] = "10px" });
        var em = new LayoutNode(null, "EM", "", _styleCache.Style);
        em.StyleOverrides["display"] = "inline";
        em.StyleOverrides["float"] = "left";
        em.AddChild(strong);
        var span = Span(em);
        var container = Block(new() { ["width"] = "200px" }, span);
        LayoutTree(container);

        True(container.Children.Count == 1 && container.Children[0] == span,
            "the span must not be split when its block is inside a floated descendant");
        True(em.Parent == span && strong.Parent == em,
            "the floated em and its block must stay nested inside the span (not hoisted)");
    }

    [Test]
    public static void BlockInInline_RelativePositionPropagatesToHoistedBlock()
    {
        // CSS 2.1 §9.2.1.1 last sentence: relative positioning of the broken inline also translates
        // the block-level box hoisted out of it (block-in-inline-relpos-001). The propagation stamps
        // the inline's position/offsets onto the otherwise-static hoisted block.
        var block = Block(new() { ["height"] = "20px" });
        var span = new LayoutNode(null, "SPAN", "", _styleCache.Style);
        span.StyleOverrides["display"] = "inline";
        span.StyleOverrides["position"] = "relative";
        span.StyleOverrides["left"] = "20px";
        span.AddChild(block);
        var container = Block(new() { ["width"] = "200px" }, span);
        LayoutTree(container);

        True(block.StyleOverrides.GetValueOrDefault("position") == "relative",
            "the hoisted block should inherit the inline's relative positioning");
        True(block.StyleOverrides.GetValueOrDefault("left") == "20px",
            $"the hoisted block should take the inline's left offset, got '{block.StyleOverrides.GetValueOrDefault("left")}'");
    }

    [Test]
    public static void FirstChildMargin_PropagatesOutOfParent()
    {
        // CSS 2.1 §8.3.1: a block with no top border/padding does not contain its first in-flow
        // child's top margin — it collapses through and appears ABOVE the block. green h=50; then
        // wrapper (no border/padding) whose first child has margin-top:30 → the 30 shows between
        // green's bottom and the wrapper's content, so inner starts at 50+30 = 80 (not 50).
        var green = Block(new() { ["height"] = "50px" });
        var inner = Block(new() { ["height"] = "40px", ["margin-top"] = "30px" });
        var wrapper = Block(new() { }, inner);
        var container = Block(new() { ["width"] = "200px" }, green, wrapper);
        LayoutTree(container);
        True(Math.Abs(inner.Box.ContentBox.Top - 80f) < 0.5f,
            $"first child's margin-top should propagate out of the wrapper (inner at 80), got {inner.Box.ContentBox.Top}");
    }

    [Test]
    public static void BlockInInline_NegativeMbCollapsesWithPositiveMt()
    {
        // WPT block-in-inline-negative-mb-collapses-with-positive-mt: two spans each wrap a block.
        // first: h20, mb-20 (propagates out of span1 as -20); second: h20, mt50 (propagates out of
        // span2 as +50). Collapsed gap = 50 + (-20) = 30, so second starts at 20 + 30 = 50.
        var first = Block(new() { ["width"] = "100px", ["height"] = "20px", ["margin-bottom"] = "-20px" });
        var second = Block(new() { ["width"] = "100px", ["height"] = "20px", ["margin-top"] = "50px" });
        var container = Block(new() { ["width"] = "100px" }, Span(first), Span(second));
        LayoutTree(container);
        True(Math.Abs(first.Box.ContentBox.Top - 0f) < 0.5f,
            $"first block should sit at the top (0), got {first.Box.ContentBox.Top}");
        True(Math.Abs(second.Box.ContentBox.Top - 50f) < 0.5f,
            $"second block should sit at 50 (20 + collapsed gap 30), got {second.Box.ContentBox.Top}");
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

    // -------------------------------------------------------------------------
    // Anonymous table boxes (§17.2.1) + inline-table
    // -------------------------------------------------------------------------

    [Test]
    public static void AnonymousTableBoxes_WrapBareContent()
    {
        // CSS 2.1 §17.2.1: a display:table with bare (non-row) content generates an anonymous
        // table-row and table-cell around it, so the content lays out as a real cell.
        var content = Block(new() { ["width"] = "60px", ["height"] = "20px" });
        var table = Block(new() { ["display"] = "table", ["border-spacing"] = "0" }, content);
        LayoutTree(Block(new() { ["width"] = "400px" }, table));

        True(content.Parent is { TagName: "#anon-cell" },
            $"bare content should be reparented under an anonymous cell, got {content.Parent?.TagName}");
        True(content.Parent!.Parent is { TagName: "#anon-row" },
            $"the anonymous cell should sit under an anonymous row, got {content.Parent!.Parent?.TagName}");
        True(Math.Abs(content.Box.ContentBox.Height - 20f) < 1f,
            $"bare content should lay out (h20) inside the anonymous cell, got {content.Box.ContentBox.Height}");
    }

    [Test]
    public static void AnonymousTableBoxes_ProperTableUntouched()
    {
        // A well-formed TABLE>TR>TD is left exactly as authored — no anonymous wrapping.
        var cell = TableCell("Hi");
        var row = new LayoutNode(null, "TR", "", _styleCache.Style);
        row.StyleOverrides["display"] = "table-row";
        row.AddChild(cell);
        var table = new LayoutNode(null, "TABLE", "", _styleCache.Style);
        table.StyleOverrides["display"] = "table";
        table.AddChild(row);
        LayoutTree(Block(new() { ["width"] = "400px" }, table));
        True(table.Children.Count == 1 && ReferenceEquals(table.Children[0], row),
            "a proper table's row should not be wrapped in an anonymous box");
        True(cell.Parent is { TagName: "TR" }, "a proper cell should stay under its TR");
    }

    [Test]
    public static void TableRowGroup_DisplayRecursesForRows()
    {
        // display:table-row-group (on a non-TBODY element) must be recognized so CollectRows
        // recurses into it — otherwise it maps to Inline and the table finds no rows.
        var cell = TableCell("Hi");
        cell.StyleOverrides["width"] = "40px";
        var row = Tagged("SPAN", new() { ["display"] = "table-row" });
        row.AddChild(cell);
        var group = Tagged("SPAN", new() { ["display"] = "table-row-group" });
        group.AddChild(row);
        var table = Tagged("TABLE", new() { ["display"] = "table", ["border-spacing"] = "0" });
        table.AddChild(group);
        LayoutTree(Block(new() { ["width"] = "400px" }, table));
        True(group.GetDisplay() == Lite.Extensions.DisplayType.TableRowGroup,
            $"table-row-group should map to TableRowGroup, got {group.GetDisplay()}");
        True(Math.Abs(cell.Box.ContentBox.Width - 40f) < 1f,
            $"cell inside a table-row-group should lay out with its 40px width, got {cell.Box.ContentBox.Width}");
    }

    [Test]
    public static void InlineTable_ShrinkToFitWidth()
    {
        // An inline-table shrinks to fit its content: a single 60px cell → table content width ~60
        // (border-spacing:0). It also participates as an atomic inline (gets a Box).
        var cellContent = Block(new() { ["width"] = "60px", ["height"] = "20px" });
        var itable = Block(new() { ["display"] = "inline-table", ["border-spacing"] = "0" }, cellContent);
        var container = Block(new() { ["width"] = "400px" }, itable);
        LayoutTree(container);
        True(Math.Abs(itable.Box.ContentBox.Width - 60f) < 2f,
            $"inline-table should shrink-to-fit its 60px content, got {itable.Box.ContentBox.Width}");
    }

    [Test]
    public static void InlineLevelContent_PaintsAboveLaterBlockBackground()
    {
        // CSS 2.1 Appendix E: in-flow INLINE-level content (step 5) paints ABOVE in-flow BLOCK
        // backgrounds (step 3). A green inline-table in div1, overlapped by a LATER red block
        // (margin-top pulls it up over div1) → the green must win in the overlap region.
        var cell = Block(new() { ["width"] = "40px", ["height"] = "40px" });
        var itable = Block(new() { ["display"] = "inline-table", ["border-spacing"] = "0", ["background-color"] = "#008000" }, cell);
        var div1 = Block(new() { }, itable);
        var red = Block(new() { ["width"] = "40px", ["height"] = "40px", ["background-color"] = "#ff0000", ["margin-top"] = "-40px" });
        var root = LayoutTree(Block(new() { ["width"] = "200px" }, div1, red));

        using var bmp = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
        var b = itable.Box.ContentBox;
        var px = bmp.GetPixel((int)(b.Left + b.Width / 2f), (int)(b.Top + b.Height / 2f));
        True(px.Green > 120 && px.Red < 120,
            $"inline-table (green) should paint above the later block's red background, got {px}");
    }

    [Test]
    public static void VisibilityHidden_SuppressesOwnPaintButNotVisibleChild()
    {
        // CSS 2.1 §11.2: a visibility:hidden box still occupies its layout space but paints
        // nothing of its own, while a descendant that is visible IS still painted. The child
        // here carries no visibility override, so it resolves to the initial 'visible' —
        // exactly the case that must survive the parent's suppression.
        var child = Block(new() { ["width"] = "50px", ["height"] = "50px", ["background-color"] = "#008000" });
        var hidden = Block(new()
        {
            ["visibility"] = "hidden",
            ["width"] = "100px",
            ["height"] = "100px",
            ["background-color"] = "#ff0000",
        }, child);
        var root = LayoutTree(hidden);

        using var bmp = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });

        // Sample below the child, still inside the hidden parent: its red must not be painted, so
        // the canvas shows through. Red is (255,0,0) and the canvas is white, so the green channel
        // — not the red one — is what distinguishes them.
        var hb = hidden.Box.ContentBox;
        var own = bmp.GetPixel((int)(hb.Left + hb.Width / 2f), (int)(hb.Bottom - 10f));
        True(own.Green > 200 && own.Blue > 200,
            $"visibility:hidden box must not paint its own background, got {own}");

        // The visible child still paints.
        var cb = child.Box.ContentBox;
        var kid = bmp.GetPixel((int)(cb.Left + cb.Width / 2f), (int)(cb.Top + cb.Height / 2f));
        True(kid.Green > 120 && kid.Red < 120,
            $"a visible child of a hidden box must still paint, got {kid}");

        // The box keeps its place in the flow: hiding is a paint effect, not display:none.
        True(Math.Abs(hb.Height - 100f) < 0.5f,
            $"hidden box should still occupy 100px of layout, got {hb.Height}");
    }

    // NOTE: the CDATA-in-<style> regression is guarded by the css21 reftest `cdata-style`, not
    // by a unit test here. The in-process ParseChildPage path does not reproduce it - only a
    // document loaded through the normal page pipeline does - so a unit test passes either way and
    // would give false confidence as a regression guard.

    private static LayoutNode? FindNode(LayoutNode n, Func<LayoutNode, bool> pred) =>
        pred(n) ? n : n.Children.Select(c => FindNode(c, pred)).FirstOrDefault(r => r != null);

    [Test]
    public static void InlineBlock_AlignsOnItsOwnTextBaselineNotItsBottomEdge()
    {
        // CSS 2.1 §10.8.1: an inline-block with overflow:visible aligns the baseline of its last
        // line box with the parent's baseline — NOT its bottom margin edge. Both boxes here use
        // the same font and line-height, so their ascents are equal and their tops must line up.
        // With the old bottom-edge rule the inline-block rode higher by (its height − its ascent).
        var page = Parser.ParseChildPage(
            "<html><head><style>#ib { display: inline-block; }</style></head>" +
            "<body><div>abc<span id='ib'>xyz</span></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var ib = FindNode(page.Root, n => n.Id == "ib");
        var text = FindNode(page.Root, n => n.TagName == "#text" && n.DisplayText.Trim() == "abc");
        True(ib != null && text != null, "expected both the inline-block and the sibling text node");
        True(Math.Abs(ib!.Box.ContentBox.Top - text!.Box.ContentBox.Top) < 1.5f,
            $"inline-block should share the sibling text's baseline: inline-block top " +
            $"{ib.Box.ContentBox.Top} vs text top {text.Box.ContentBox.Top}");
    }

    [Test]
    public static void PseudoElement_KeepsBoundarySpaceBeforeGeneratedContent()
    {
        // The element's own text is trimmed because spaces at the start/end of a LINE are dropped
        // (§16.6.1) — but an ::after continues the line, so the space between them is mid-line and
        // must survive. Otherwise "<div>abc </div>" + ::after renders as "abcxyz".
        var page = Parser.ParseChildPage(
            "<html><head><style>#d::after { content: \"xyz\"; }</style></head>" +
            "<body><div id='d'>abc </div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        var textChild = FindNode(page.Root, n => n.TagName == "#text" && n.DisplayText.Contains("abc"));
        True(textChild != null, "expected the element's text to become a #text child next to ::after");
        True(textChild!.DisplayText.EndsWith(" "),
            $"the space before generated content must be kept, got \"{textChild.DisplayText}\"");
    }

    // NOTE: the solid-border crisp-edge behaviour is guarded by the css21 reftest
    // border-vs-background, not by a unit test. A synthetic bordered box happens to rasterise
    // identically either way at integer coordinates, so a unit test passes with and without the
    // fix; only comparing a bordered box against a background-filled one reproduces it.

    [Test]
    public static void TextWhitespace_CollapsesNewlinesKeepsNbspAndHonoursPreLine()
    {
        // CSS 2.1 §16.6: a newline is collapsible whitespace and therefore a line-break
        // opportunity — line breaking splits on U+0020, so an uncollapsed newline meant
        // source-wrapped text never wrapped. U+00A0 is NOT collapsible (that is its purpose), and
        // 'pre-line' keeps newlines as forced breaks.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#p { white-space: pre-line; }</style></head><body>" +
            "<div id='n'>a\nb</div><div id='s'>a\u00A0b</div><div id='p'>a\nb</div>" +
            "</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        var n = FindNode(page.Root, x => x.Id == "n");
        var s = FindNode(page.Root, x => x.Id == "s");
        var p = FindNode(page.Root, x => x.Id == "p");
        True(n?.DisplayText == "a b",
            $"a newline must collapse to a space so the line can break, got \"{n?.DisplayText}\"");
        True(s?.DisplayText == "a\u00A0b",
            $"U+00A0 must survive collapsing, got \"{s?.DisplayText}\"");
        True(p?.DisplayText.Contains('\n') == true,
            $"white-space:pre-line must keep the newline, got \"{p?.DisplayText}\"");
    }

    [Test]
    public static void PseudoElementRule_DoesNotStyleTheOriginatingElement()
    {
        // A pseudo-ELEMENT rule styles a generated or partial box, never the element itself.
        // AngleSharp matches `div:first-letter` against the DIV, so the whole element took the
        // first-letter font-size and colour; the rules are now lifted out of the cascade and
        // re-applied only as FirstLetterStyles.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#d:first-letter { color: green; font-size: 36px; }" +
            "</style></head><body><div id='d'>Text</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        var d = FindNode(page.Root, n => n.Id == "d");
        True(d != null, "expected the div");
        True(d!.FirstLetterStyles?.ContainsKey("font-size") == true,
            "the ::first-letter rule must still be captured as FirstLetterStyles");
        True(d.GetFontSize() < 30f,
            $"the element itself must not take the ::first-letter font-size, got {d.GetFontSize()}");
    }

    [Test]
    public static void BorderShorthandWithoutWidth_UsesMediumNotZero()
    {
        // CSS 2.1 §8.5.1: 'border-width' has an initial value of 'medium' (3px), so
        // `border: solid black` paints a 3px border. AngleSharp expands that shorthand's style
        // correctly but computes the omitted width to 0px, which made such borders vanish.
        // An authored zero must still be honoured.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#a { border: solid black; } #z { border: 0 solid black; } #s { border-style: solid; }" +
            "</style></head><body><div id='a'></div><div id='z'></div><div id='s'></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var a = FindNode(page.Root, n => n.Id == "a");
        var z = FindNode(page.Root, n => n.Id == "z");
        var s = FindNode(page.Root, n => n.Id == "s");
        True(a != null && Math.Abs(a.Box.Border.Top - 3f) < 0.5f,
            $"`border: solid black` should be 3px (medium), got {a?.Box.Border.Top}");
        True(s != null && Math.Abs(s.Box.Border.Top - 3f) < 0.5f,
            $"`border-style: solid` should be 3px (medium), got {s?.Box.Border.Top}");
        True(z != null && z.Box.Border.Top < 0.5f,
            $"an authored `border: 0 solid black` must stay 0, got {z?.Box.Border.Top}");
    }

    [Test]
    public static void PseudoDisplayInherit_ResolvesToHostDisplay()
    {
        // 'display: inherit' on generated content must take the originating element's display.
        // Pseudo styles are written straight into StyleOverrides, bypassing the cascade's
        // css-wide-keyword resolution, so the literal "inherit" would otherwise reach GetDisplay
        // and fall through to 'inline'.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#d::after { content: \"xyz\"; display: inherit; }</style>" +
            "</head><body><div id='d'>abc</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        var pseudo = FindNode(page.Root, n => n.TagName == "#pseudo-after");
        True(pseudo != null, "expected a ::after pseudo node to be generated");
        True(pseudo!.GetDisplay() == DisplayType.Block,
            $"display:inherit should resolve to the host's 'block', got {pseudo.GetDisplay()} " +
            $"(override=\"{pseudo.StyleOverrides.GetValueOrDefault("display")}\")");
    }

    [Test]
    public static void MisparentedTableCell_GetsAnonymousRowAndTable()
    {
        // CSS 2.1 §17.2.1 "generate missing parents": a display:table-cell whose parent is a plain
        // block needs BOTH an anonymous table-row and an anonymous table generated around it.
        var cell = Block(new() { ["display"] = "table-cell", ["width"] = "40px", ["height"] = "20px" });
        var container = Block(new() { ["width"] = "200px" }, cell);
        var root = LayoutTree(container);

        True(cell.Parent?.TagName == "#anon-row",
            $"cell should gain an anonymous row parent, got {cell.Parent?.TagName}");
        True(cell.Parent?.Parent?.TagName == "#anon-table",
            $"the anonymous row should sit inside an anonymous table, got {cell.Parent?.Parent?.TagName}");
        True(cell.Parent?.Parent?.Parent == container,
            "the anonymous table should take the cell's place in the original container");

        // Normalization re-runs on every layout pass, so it must be idempotent — a second pass
        // must not wrap the already-generated boxes in yet another table.
        BoxEngine.Layout(root, 800, 600);
        True(cell.Parent?.Parent?.Parent == container,
            "a second layout pass must not nest another anonymous table");
    }

    [Test]
    public static void AdjacentMisparentedTableRows_ShareOneAnonymousTable()
    {
        // Consecutive misparented internal table boxes belong to the SAME generated table, so two
        // adjacent display:table-row boxes become two rows of one table rather than two tables.
        var r1 = Block(new() { ["display"] = "table-row", ["height"] = "20px" });
        var r2 = Block(new() { ["display"] = "table-row", ["height"] = "20px" });
        var container = Block(new() { ["width"] = "200px" }, r1, r2);
        LayoutTree(container);

        True(r1.Parent?.TagName == "#anon-table" && ReferenceEquals(r1.Parent, r2.Parent),
            $"both rows should share one anonymous table, got {r1.Parent?.TagName} / {r2.Parent?.TagName}");
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

    /// <summary>A replaced box: an IMG carrying an intrinsic size, as the parser records from the
    /// width/height content attributes.</summary>
    private static LayoutNode Img(int intrinsicW, int intrinsicH, Dictionary<string, string>? styles = null)
    {
        var node = new LayoutNode(null, "IMG", "", _styleCache.Style);
        // The shared fallback style is a DIV's, so 'display' must be reset to the UA value for an
        // image or it would resolve to block and never reach the inline formatting context.
        node.StyleOverrides["display"] = "inline";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            node.StyleOverrides[$"margin-{side}"] = "0";
            node.StyleOverrides[$"padding-{side}"] = "0";
            node.StyleOverrides[$"border-{side}-width"] = "0";
        }
        node.IntrinsicWidth = intrinsicW;
        node.IntrinsicHeight = intrinsicH;
        if (styles is not null) foreach (var (k, v) in styles) node.StyleOverrides[k] = v;
        return node;
    }

    [Test]
    public static void ReplacedBox_UsesIntrinsicSizeWhenBlockFloatedOrAbsolute()
    {
        // CSS 2.1 §10.3.2/§10.6.2: an 'auto' dimension of a replaced box takes the intrinsic one.
        // Only the inline path consulted it, so a block-level image filled its container and every
        // out-of-flow image collapsed to zero height (there are no children to measure).
        var blockImg = Img(96, 96, new() { ["display"] = "block" });
        var floatImg = Img(96, 96, new() { ["float"] = "left" });
        var absImg = Img(96, 96, new() { ["position"] = "absolute", ["top"] = "0", ["left"] = "0" });
        LayoutTree(Block(new() { ["width"] = "400px" }, blockImg, floatImg, absImg));

        foreach (var (name, img) in new[] { ("block", blockImg), ("float", floatImg), ("abs", absImg) })
        {
            True(Math.Abs(img.Box.ContentBox.Width - 96f) < 0.5f,
                $"{name} replaced width should be the intrinsic 96, got {img.Box.ContentBox.Width}");
            True(Math.Abs(img.Box.ContentBox.Height - 96f) < 0.5f,
                $"{name} replaced height should be the intrinsic 96, got {img.Box.ContentBox.Height}");
        }
    }

    [Test]
    public static void ReplacedBox_DerivesMissingDimensionFromIntrinsicRatio()
    {
        // §10.6.2: with only 'width' given, the used height preserves the intrinsic ratio.
        var img = Img(200, 100, new() { ["display"] = "block", ["width"] = "50px" });
        LayoutTree(Block(new() { ["width"] = "400px" }, img));
        True(Math.Abs(img.Box.ContentBox.Height - 25f) < 0.5f,
            $"height should follow the 2:1 intrinsic ratio (25), got {img.Box.ContentBox.Height}");
    }

    [Test]
    public static void TextAlignRight_MovesInlineLevelBoxesNotJustText()
    {
        // CSS 2.1 §16.2 aligns the whole LINE BOX. text-align had a single consumer in the text
        // painter, so an image or inline-block on the line ignored it entirely.
        var img = Img(96, 96);
        var cb = Block(new() { ["width"] = "400px", ["text-align"] = "right" }, img);
        LayoutTree(cb);
        var expected = cb.Box.ContentBox.Right - 96f;
        True(Math.Abs(img.Box.ContentBox.Left - expected) < 0.5f,
            $"right-aligned image should start at {expected}, got {img.Box.ContentBox.Left}");
    }

    [Test]
    public static void FloatedTableCell_IsBlockified()
    {
        // CSS 2.1 §9.7: a floated box is block-level, so 'display: table-cell' computes to 'block'
        // (and it is laid out as one) instead of waiting for a table that will never exist.
        var cell = Block(new() { ["display"] = "table-cell", ["float"] = "right", ["width"] = "96px", ["height"] = "96px" });
        var cb = Block(new() { ["width"] = "400px" }, cell);
        LayoutTree(cb);

        True(cell.GetDisplay() == DisplayType.Block,
            $"a floated table-cell should compute to display:block, got {cell.GetDisplay()}");
        True(Math.Abs(cell.Box.ContentBox.Right - cb.Box.ContentBox.Right) < 0.5f,
            $"it should float to the right edge {cb.Box.ContentBox.Right}, got {cell.Box.ContentBox.Right}");
    }

    [Test]
    public static void FloatAfterInlineContent_SitsOnTheCurrentLineBox()
    {
        // CSS 2.1 §9.5.1: the top of a float aligns with the top of the CURRENT line box, so a
        // float written after inline content sits beside it rather than dropping below the line.
        var img = Img(20, 60);
        var flt = Block(new() { ["float"] = "right", ["width"] = "20px", ["height"] = "20px" });
        var cb = Block(new() { ["width"] = "400px" }, img, flt);
        LayoutTree(cb);

        True(Math.Abs(flt.Box.ContentBox.Top - img.Box.ContentBox.Top) < 0.5f,
            $"float should share the line box top {img.Box.ContentBox.Top}, got {flt.Box.ContentBox.Top}");
    }

    [Test]
    public static void InlineBlock_PaintsBackgroundAndBorder()
    {
        // display:inline-block was missing from the paint dispatch's block-container list (which
        // already had inline-table and inline-flex), so one with no text painted nothing at all
        // and one with text got only a content-box fill and no border.
        var ib = Block(new()
        {
            ["display"] = "inline-block",
            ["width"] = "40px",
            ["height"] = "40px",
            ["background-color"] = "#008000",
        });
        var root = LayoutTree(Block(new() { ["width"] = "200px" }, ib));

        using var bmp = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
        var b = ib.Box.ContentBox;
        var px = bmp.GetPixel((int)(b.Left + b.Width / 2f), (int)(b.Top + b.Height / 2f));
        True(px.Green > 120 && px.Red < 120,
            $"an empty sized inline-block must paint its background, got {px}");
    }

    [Test]
    public static void FixedPositionBox_IsPainted()
    {
        // position:fixed boxes are skipped by the normal paint pass and left to PaintFixedNodes,
        // which routed straight back into that same skip — so they laid out correctly and were
        // then never painted at all.
        var fixedBox = Block(new()
        {
            ["position"] = "fixed",
            ["top"] = "0",
            ["left"] = "0",
            ["width"] = "40px",
            ["height"] = "40px",
            ["background-color"] = "#008000",
        });
        var root = LayoutTree(Block(new() { ["width"] = "200px" }, fixedBox));

        using var bmp = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
        var px = bmp.GetPixel(20, 20);
        True(px.Green > 120 && px.Red < 120,
            $"a position:fixed box must paint (expected green at 20,20), got {px}");
    }

    [Test]
    public static void FirstLetter_BecomesARealInlineBoxThatSizesTheLine()
    {
        // CSS 2.1 §5.12.1 + §10.8: ::first-letter is a real inline box, so its font and
        // 'line-height' size the line box it sits on. Styling it only at paint time (re-drawing
        // the first characters larger) left the line box sized for the parent's font, so the
        // block was too short and the letter overlapped whatever was above it.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "div:first-letter { color: green; font-size: 36px; line-height: 2; }" +
            "</style></head><body><div id='d'>)T)est</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var d = FindNode(page.Root, n => n.Id == "d");
        var letter = FindNode(page.Root, n => n.TagName == "#pseudo-first-letter");
        True(letter != null, "::first-letter must generate an inline box");
        // Leading AND trailing punctuation belong to the first letter (Ps/Pe/Pi/Pf/Po).
        Equal(")T)", letter!.DisplayText);
        True(Math.Abs(letter.GetFontSize() - 36f) < 0.5f,
            $"the first-letter box must take the pseudo-element font-size, got {letter.GetFontSize()}");
        // line-height:2 on a 36px box makes the line box 72px tall.
        True(d!.Box.ContentBox.Height > 60f,
            $"the first-letter box must size the line box, got {d.Box.ContentBox.Height}");
    }

    [Test]
    public static void FirstLetter_PunctuationOnlyTextGetsNoBox()
    {
        // No letter to style: the run is punctuation all the way down, so no box is generated
        // and the text is left exactly as authored.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>div:first-letter { color: green; }" +
            "</style></head><body><div id='d'>...</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        True(FindNode(page.Root, n => n.TagName == "#pseudo-first-letter") is null,
            "punctuation-only text must not produce a ::first-letter box");
    }

    [Test]
    public static void AbsPos_AutoOffsetsUseTheStaticPosition()
    {
        // CSS 2.1 §10.3.7 / §10.6.4: with left/top auto the box goes where it WOULD have been in
        // normal flow, not at the containing block's origin. The abs-pos box here follows a 50px
        // block inside an unpositioned wrapper, so it belongs at y=50 — the old code pinned it to
        // the containing block (the relative outer div) at y=0.
        var spacer = Block(new() { ["height"] = "50px" });
        var abs = Block(new() { ["position"] = "absolute", ["width"] = "20px", ["height"] = "20px" });
        var wrapper = Block(new() { ["margin-left"] = "30px" }, spacer, abs);
        var outer = Block(new() { ["position"] = "relative", ["width"] = "200px" }, wrapper);
        LayoutTree(outer);

        True(Math.Abs(abs.Box.ContentBox.Top - 50f) < 0.5f,
            $"static position should put the box below the 50px block, got {abs.Box.ContentBox.Top}");
        True(Math.Abs(abs.Box.ContentBox.Left - 30f) < 0.5f,
            $"static position should follow the wrapper's left edge (30), got {abs.Box.ContentBox.Left}");
    }

    [Test]
    public static void LineBoxes_WidenBelowAFloatTheyAreNoLongerBesideIt()
    {
        // CSS 2.1 §9.5: each line box gets its own band. A float 20px tall narrows only the lines
        // beside it; the lines below start back at the container's left edge. The engine used to
        // compute one band for the whole run, so every line stayed indented.
        var flt = Block(new() { ["float"] = "left", ["width"] = "60px", ["height"] = "20px" });
        var one = Block(new() { ["display"] = "inline-block", ["width"] = "60px", ["height"] = "30px" });
        var two = Block(new() { ["display"] = "inline-block", ["width"] = "60px", ["height"] = "30px" });
        var container = Block(new() { ["width"] = "150px" }, flt, one, two);
        LayoutTree(container);

        True(Math.Abs(one.Box.ContentBox.Left - 60f) < 0.5f,
            $"the first line sits beside the float (x=60), got {one.Box.ContentBox.Left}");
        True(Math.Abs(two.Box.ContentBox.Left - 0f) < 0.5f,
            $"the second line clears the 20px float and starts at x=0, got {two.Box.ContentBox.Left}");
    }

    [Test]
    public static void ExplicitZeroSize_IsNotTreatedAsAuto()
    {
        // 'width: 0' / 'height: 0' are used values, not "unspecified". The engine tested for a
        // specified size with "GetWidth() > 0", so a zero-sized box filled its container's width
        // and grew to its content's height instead of collapsing.
        var zero = Block(new() { ["width"] = "0", ["height"] = "0" },
                         Block(new() { ["height"] = "40px" }));
        LayoutTree(Block(new() { ["width"] = "200px" }, zero));

        True(Math.Abs(zero.Box.ContentBox.Width) < 0.5f,
            $"width:0 must stay 0, got {zero.Box.ContentBox.Width}");
        True(Math.Abs(zero.Box.ContentBox.Height) < 0.5f,
            $"height:0 must stay 0, got {zero.Box.ContentBox.Height}");
    }

    [Test]
    public static void BackgroundOnAFractionalBoundary_PaintsWholePixels()
    {
        // A background is snapped to device pixels like replaced content is, so a box whose edges
        // land on a fraction paints solid rows rather than a half-covered anti-aliased fringe.
        // Reftests constantly compare a filled box against an image of the same size, and the
        // fringe alone was enough to blow the pixel budget.
        var box = Block(new()
        {
            ["margin-top"] = "10.4px",
            ["width"] = "50px",
            ["height"] = "20px",
            ["background-color"] = "#000000",
        });
        var root = LayoutTree(Block(new() { ["width"] = "200px" }, box));

        using var bmp = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
        var top = (int)MathF.Round(box.Box.PaddingBox.Top);
        var px = bmp.GetPixel(10, top);
        True(px.Red == 0 && px.Green == 0 && px.Blue == 0,
            $"the top row of the background must be solid, got {px}");
    }

    [Test]
    public static void RelativeFontSize_IsComputedOnceAndInheritedAsALength()
    {
        // CSS 2.1 §15.7: 'font-size' computes to a length and descendants inherit THAT. The style
        // engine handed every descendant the specified value ("2em") and re-resolved it against
        // the parent's already-scaled size, so one `font-size: 2em` doubled again at every level:
        // a four-deep subtree reached 512px. Only an element that declares its own font-size may
        // rescale.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#big { font-size: 2em } #own { font-size: 2em }" +
            " #sh { font: 1in monospace }</style></head><body>" +
            "<div id='big'><div id='mid'><span id='deep'>x</span></div><div id='own'>y</div></div>" +
            "<div id='sh'>z</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        float Fs(string id) => FindNode(page.Root, n => n.Id == id)!.GetFontSize();
        True(Math.Abs(Fs("big") - 32f) < 0.5f, $"2em of 16px is 32, got {Fs("big")}");
        True(Math.Abs(Fs("mid") - 32f) < 0.5f, $"an inherited font-size must not rescale, got {Fs("mid")}");
        True(Math.Abs(Fs("deep") - 32f) < 0.5f, $"nor two levels down, got {Fs("deep")}");
        True(Math.Abs(Fs("own") - 64f) < 0.5f, $"a re-declared 2em does scale again, got {Fs("own")}");
        True(Math.Abs(Fs("sh") - 96f) < 0.5f, $"the 'font' shorthand sets the size (1in), got {Fs("sh")}");
    }

    [Test]
    public static void NegativeLengths_AreInvalidAndFallBackToTheInitialValue()
    {
        // CSS 2.1: a negative 'width'/'height'/'border-width' is invalid, so the declaration is
        // dropped and the property keeps its initial value. AngleSharp hands the negative length
        // through, which turned `width: -1px` into a zero-width box and dropped a border whose
        // side still had a visible style.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#w { width: -1px; height: 20px; }" +
            "#b { border-top-style: solid; border-top-width: -1px; height: 20px; }" +
            "</style></head><body><div id='w'></div><div id='b'></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var w = FindNode(page.Root, n => n.Id == "w")!;
        var b = FindNode(page.Root, n => n.Id == "b")!;
        True(w.Box.ContentBox.Width > 100f,
            $"an invalid negative width falls back to auto (fills the block), got {w.Box.ContentBox.Width}");
        True(Math.Abs(b.Box.Border.Top - 3f) < 0.5f,
            $"an invalid negative border-width falls back to medium (3px), got {b.Box.Border.Top}");
    }

    [Test]
    public static void LineTooNarrowForItsContent_ShiftsBelowTheFloat()
    {
        // CSS 2.1 §9.5 rule 7: "if a shortened line box is too small to contain any content, it
        // is shifted downward until either it fits or there are no more floats present". The
        // engine used to leave the content overflowing the narrow band beside the float.
        var flt = Block(new() { ["float"] = "left", ["width"] = "60px", ["height"] = "40px" });
        var wide = Block(new() { ["display"] = "inline-block", ["width"] = "90px", ["height"] = "20px" });
        var container = Block(new() { ["width"] = "100px" }, flt, wide);
        LayoutTree(container);

        True(Math.Abs(wide.Box.ContentBox.Left) < 0.5f,
            $"the shifted line starts at the container's left edge, got {wide.Box.ContentBox.Left}");
        True(wide.Box.ContentBox.Top >= 40f - 0.5f,
            $"and below the 40px float, got {wide.Box.ContentBox.Top}");
    }

    [Test]
    public static void MinAndMaxWidth_ClampAnInFlowBlock()
    {
        // CSS 2.1 §10.4. Only the absolutely-positioned path clamped, so 'min-width' and
        // 'max-width' had no effect at all on a normal block: it just filled its container.
        var capped = Block(new() { ["max-width"] = "40px", ["height"] = "10px" });
        var floored = Block(new() { ["width"] = "10px", ["min-width"] = "70px", ["height"] = "10px" });
        // min-width wins over max-width when they conflict.
        var both = Block(new() { ["min-width"] = "80px", ["max-width"] = "20px", ["height"] = "10px" });
        LayoutTree(Block(new() { ["width"] = "200px" }, capped, floored, both));

        True(Math.Abs(capped.Box.ContentBox.Width - 40f) < 0.5f,
            $"max-width should cap the fill width at 40, got {capped.Box.ContentBox.Width}");
        True(Math.Abs(floored.Box.ContentBox.Width - 70f) < 0.5f,
            $"min-width should raise the used width to 70, got {floored.Box.ContentBox.Width}");
        True(Math.Abs(both.Box.ContentBox.Width - 80f) < 0.5f,
            $"min-width wins over max-width, expected 80, got {both.Box.ContentBox.Width}");
    }

    [Test]
    public static void SynthesizedBoxes_DoNotInheritTheParentsPositionOrFloat()
    {
        // An anonymous table box borrows its originating element's style object, so a parent's
        // NON-inherited 'position: absolute' read back as the anonymous box's own and took it out
        // of its parent's flow — the parent then measured no content and shrank to nothing, and
        // the cells stacked vertically instead of sitting side by side.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body><div style='position: relative'>" +
            "<div id='abs' style='position: absolute; top: 0'>" +
            "<span style='display: table-cell'>ab</span>" +
            "<span style='display: table-cell'>cd</span>" +
            "</div></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var abs = FindNode(page.Root, n => n.Id == "abs")!;
        var table = FindNode(abs, n => n.TagName == "#anon-table");
        True(table != null, "the bare cells must be wrapped in an anonymous table");
        True(table!.GetPosition() == PositionType.Static,
            "an anonymous box must not pick up the parent's position");
        True(abs.Box.ContentBox.Width > 10f,
            $"the abs-pos box shrink-wraps to its table, got {abs.Box.ContentBox.Width}");
        var cells = new List<LayoutNode>();
        void Collect(LayoutNode n)
        {
            if (n.GetDisplay() == DisplayType.TableCell) cells.Add(n);
            foreach (var c in n.Children) Collect(c);
        }
        Collect(abs);
        True(cells.Count == 2 && Math.Abs(cells[0].Box.ContentBox.Top - cells[1].Box.ContentBox.Top) < 0.5f,
            "the two cells share a row, so they sit at the same top");
        True(cells[1].Box.ContentBox.Left > cells[0].Box.ContentBox.Left,
            "and side by side");
    }

    [Test]
    public static void PreFormattedText_KeepsItsLeadingAndTrailingSpaces()
    {
        // §16.6.1's "remove spaces at the start and end of a line" only applies where white-space
        // collapses. Trimming unconditionally made a `white-space: pre` cell holding one space
        // zero pixels wide.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body>" +
            "<span id='p' style='display: inline-block; white-space: pre'> </span>" +
            "</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var p = FindNode(page.Root, n => n.Id == "p")!;
        Equal(" ", p.DisplayText);
        True(p.Box.ContentBox.Width > 1f,
            $"a preserved space must have width, got {p.Box.ContentBox.Width}");
    }

    [Test]
    public static void ReplacedElement_HonoursCssSizeAndPresentationalHints()
    {
        // §10.3.2 / §10.6.2: a CSS width/height on a replaced element overrides its intrinsic
        // size, and HTML's width/height content attributes are presentational hints for those
        // properties — not the intrinsic size. Reading them as intrinsic derived the missing
        // dimension from one axis's attribute and the other axis's bitmap: width="100%" with
        // height="50" on a 1x1 image came out 39200px tall. The inline path ignored CSS size
        // altogether, so a percentage width left the image at its natural pixel width.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body><div id='cb' style='width: 200px'>" +
            "<img id='pct' width='50%' height='20' alt='x'/>" +
            "<img id='px' width='30' height='40' alt='x'/>" +
            "</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var pct = FindNode(page.Root, n => n.Id == "pct")!;
        var px = FindNode(page.Root, n => n.Id == "px")!;
        True(Math.Abs(pct.Box.ContentBox.Width - 100f) < 1f,
            $"width=\"50%\" of a 200px block is 100, got {pct.Box.ContentBox.Width}");
        True(Math.Abs(pct.Box.ContentBox.Height - 20f) < 1f,
            $"height=\"20\" stays 20, got {pct.Box.ContentBox.Height}");
        True(Math.Abs(px.Box.ContentBox.Width - 30f) < 1f,
            $"width=\"30\" is 30, got {px.Box.ContentBox.Width}");
        True(Math.Abs(px.Box.ContentBox.Height - 40f) < 1f,
            $"height=\"40\" is 40, got {px.Box.ContentBox.Height}");
    }

    [Test]
    public static void ZeroLineHeight_CollapsesTheLineBox()
    {
        // 'line-height: 0' is a real value — it collapses the line box so the text overflows its
        // own box. Text measurement used 0 as the sentinel for "the caller did not say", so it
        // silently substituted the 1.4em default and the block came out several lines tall.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#z { line-height: 0; font-size: 20px; width: 20px }" +
            "</style></head><body><div id='z'>X X X</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var z = FindNode(page.Root, n => n.Id == "z")!;
        True(z.Box.ContentBox.Height < 1f,
            $"line-height:0 collapses every line box, expected height 0, got {z.Box.ContentBox.Height}");
    }

    [Test]
    public static void LetterSpacing_WidensTheBoxLayoutReservesForText()
    {
        // 'letter-spacing' widens the text the painter draws, so it has to widen the box layout
        // reserves for it too — measuring without it put every following inline box in the wrong
        // place. The span here is 3 characters, so it gains 2 gaps of 10px.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#a { letter-spacing: 10px } " +
            "span { font-size: 16px }</style></head>" +
            "<body><div><span id='a'>abc</span><span id='b'>x</span></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var a = FindNode(page.Root, n => n.Id == "a")!;
        var b = FindNode(page.Root, n => n.Id == "b")!;
        True(a.Box.ContentBox.Width > 20f + 20f,
            $"three letters plus two 10px gaps, got {a.Box.ContentBox.Width}");
        True(b.Box.ContentBox.Left >= a.Box.ContentBox.Right - 0.5f,
            $"the next inline box starts after the spaced text, got {b.Box.ContentBox.Left} vs {a.Box.ContentBox.Right}");
    }

    [Test]
    public static void InlinePreformattedText_BreaksAtItsNewlines()
    {
        // §16.6: under 'white-space: pre' a newline is a forced break. An inline run measured the
        // whole string as one unbroken item, so a preformatted <span> holding two lines drew them
        // on top of each other.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#p { white-space: pre }</style></head>" +
            "<body><div id='d'><span id='p'>XX\nXX</span></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var p = FindNode(page.Root, n => n.Id == "p")!;
        var frags = p.InlineFragments;
        True(frags is { Count: 2 },
            $"the preformatted newline makes two line-box fragments, got {frags?.Count ?? 0}");
        True(frags![1].Rect.Top > frags[0].Rect.Top + 1f,
            $"the second fragment sits below the first, got {frags[0].Rect.Top} then {frags[1].Rect.Top}");
        Equal("XX", frags[0].Text);
        Equal("XX", frags[1].Text);
    }

    [Test]
    public static void AbsPos_AutoMarginsAbsorbTheLeftoverSpace()
    {
        // CSS 2.1 §10.3.7 / §10.6.4: with the offsets and the size all given, an auto margin takes
        // the space left over, and two of them split it — which is how an absolutely positioned
        // box is centred in its containing block. Auto margins used to compute to zero, so such a
        // box sat at its 'left' offset. A non-auto margin on the other side comes off the top of
        // the remainder first.
        var centred = Block(new()
        {
            ["position"] = "absolute", ["left"] = "0", ["right"] = "0",
            ["width"] = "100px", ["height"] = "20px",
            ["margin-left"] = "auto", ["margin-right"] = "auto",
        });
        var oneSided = Block(new()
        {
            ["position"] = "absolute", ["left"] = "0", ["right"] = "0",
            ["width"] = "100px", ["height"] = "20px",
            ["margin-left"] = "auto", ["margin-right"] = "50px",
        });
        var outer = Block(new() { ["position"] = "relative", ["width"] = "400px", ["height"] = "100px" },
                          centred, oneSided);
        LayoutTree(outer);

        True(Math.Abs(centred.Box.ContentBox.Left - 150f) < 0.5f,
            $"a 100px box in a 400px block centres at x=150, got {centred.Box.ContentBox.Left}");
        True(Math.Abs(oneSided.Box.ContentBox.Left - 250f) < 0.5f,
            $"400 - 100 - 50 leaves 250 for the auto left margin, got {oneSided.Box.ContentBox.Left}");
    }

    [Test]
    public static void AbsPos_RtlContainingBlockPlacesTheStaticPositionOnTheRight()
    {
        // §10.3.7 rule 1: with left and right both auto the box takes its static position, and
        // under 'direction: rtl' that is measured from the right edge.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#cb { position: relative; direction: rtl; width: 200px; height: 100px }" +
            "#b { position: absolute; width: 50px; height: 20px }" +
            "</style></head><body><div id='cb'><div id='b'></div></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var cb = FindNode(page.Root, n => n.Id == "cb")!;
        var b = FindNode(page.Root, n => n.Id == "b")!;
        True(Math.Abs(b.Box.ContentBox.Right - cb.Box.ContentBox.Right) < 0.5f,
            $"an RTL containing block puts it flush right ({cb.Box.ContentBox.Right}), got {b.Box.ContentBox.Right}");
    }

    [Test]
    public static void Table_CellSpacingAndCellPaddingAttributesApply()
    {
        // HTML 4 §11.2.1 presentational hints: cellspacing is 'border-spacing' on the table and
        // cellpadding is 'padding' on each cell. Ignoring them left the UA defaults (2px spacing,
        // 1px cell padding) in place, so a `cellpadding="0" cellspacing="0"` table sat 3px lower
        // and wider than the CSS-equivalent it is meant to match.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body>" +
            "<table id='t' cellspacing='0' cellpadding='0'><tr><td id='c'>a</td><td>b</td></tr></table>" +
            "</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var t = FindNode(page.Root, n => n.Id == "t")!;
        var c = FindNode(page.Root, n => n.Id == "c")!;
        Equal("0px", t.StyleOverrides.GetValueOrDefault("border-spacing"));
        True(c.Box.Padding.Top == 0f && c.Box.Padding.Left == 0f,
            $"cellpadding=0 clears the UA cell padding, got {c.Box.Padding.Top}/{c.Box.Padding.Left}");
        True(Math.Abs(c.Box.BorderBox.Left - t.Box.ContentBox.Left) < 0.5f,
            $"with no spacing the first cell starts at the table's content edge, " +
            $"got {c.Box.BorderBox.Left} vs {t.Box.ContentBox.Left}");
    }

    [Test]
    public static void AbsolutelyPositionedTable_LaysOutAsATable()
    {
        // An absolutely positioned box laid its children out as plain blocks whatever its own
        // display was, so an abs-pos <table> flowed its rows and cells as inline content — one
        // long wrapping line of cell text instead of a grid.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>#t { position: absolute; top: 0; left: 0 }</style></head>" +
            "<body><table id='t' cellspacing='0' cellpadding='0'>" +
            "<tr><td id='a'>a</td><td id='b'>b</td></tr>" +
            "<tr><td id='c'>c</td><td>d</td></tr></table></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var a = FindNode(page.Root, n => n.Id == "a")!;
        var b = FindNode(page.Root, n => n.Id == "b")!;
        var c = FindNode(page.Root, n => n.Id == "c")!;
        True(Math.Abs(a.Box.ContentBox.Top - b.Box.ContentBox.Top) < 0.5f,
            "cells of one row share a top");
        True(b.Box.ContentBox.Left > a.Box.ContentBox.Left,
            "and sit side by side");
        True(c.Box.ContentBox.Top > a.Box.ContentBox.Top,
            "the second row is below the first");
    }

    [Test]
    public static void AnonymousTableBoxes_TrackADynamicDisplayChange()
    {
        // Anonymous boxes are a function of the current display values. A pass that only ever
        // added wrappers could not track a change to one: setting a cell to display:none wrapped
        // it in an anonymous cell + table (a display:none box is not a proper table child), and
        // restoring 'table-cell' left the wrappers behind, so the cell stayed nested in a table
        // of its own instead of rejoining the row.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body><div id='d' style='display: block'>" +
            "<span style='display: table-cell'>a</span>" +
            "<span id='t' style='display: table-cell'>b</span>" +
            "<span style='display: table-cell'>c</span>" +
            "</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var t = FindNode(page.Root, n => n.Id == "t")!;
        t.StyleOverrides["display"] = "none";
        BoxEngine.Layout(page.Root, 800, 600);
        t.StyleOverrides["display"] = "table-cell";
        BoxEngine.Layout(page.Root, 800, 600);

        var row = FindNode(page.Root, n => n.TagName == "#anon-row")!;
        var cells = row.Children.Where(c => c.GetDisplay() == DisplayType.TableCell).ToList();
        True(cells.Count == 3, $"all three cells rejoin the one row, got {cells.Count}");
        True(FindNode(row, n => n.TagName == "#anon-cell") is null,
            "no stale anonymous cell survives the round trip");
        True(cells.All(c => Math.Abs(c.Box.ContentBox.Top - cells[0].Box.ContentBox.Top) < 0.5f),
            "and they share the row's top");
    }
}
