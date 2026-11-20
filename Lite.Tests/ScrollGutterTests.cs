using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §11.1.1: the space scrollbars take is subtracted from the box's
/// containing block, and scrolling to the end brings the content's far edge into view.</summary>
public static class ScrollGutterTests
{
    private static LayoutNode? FindId(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
            if (FindId(c, id) is { } found) return found;
        return null;
    }

    private static LayoutNode Layout(string css, string body)
    {
        var page = Parser.ParseChildPage($"<!DOCTYPE html><style>body{{margin:0}} {css}</style>{body}",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    [Test]
    public static void OverflowScroll_FlowContentLaysOutBesideTheVerticalGutter()
    {
        // max-height-105: an auto-width child of a 200px 'overflow: scroll' box fills the
        // width left beside the always-present vertical bar; the box itself stays 200px.
        var root = Layout("#s{width:200px;max-height:400px;overflow:scroll} #c{height:200px}",
            "<div id=\"s\"><div id=\"c\"></div></div>");
        var s = FindId(root, "s")!;
        var c = FindId(root, "c")!;
        True(s.Box.BorderBox.Width == 200f, $"the scroll box keeps its width, got {s.Box.BorderBox.Width}");
        True(c.Box.BorderBox.Width == 200f - BoxEngine.ScrollGutter,
            $"the child fills the width beside the gutter, got {c.Box.BorderBox.Width}");
    }

    [Test]
    public static void OverflowScroll_ScrollportExcludesBothGutters()
    {
        // background-089: a 400x400 child in a 200x200 'overflow: scroll' box scrolls until
        // its bottom-right corner clears both bars.
        var root = Layout("#s{width:200px;height:200px;overflow:scroll} #c{width:400px;height:400px}",
            "<div id=\"s\"><div id=\"c\"></div></div>");
        var ss = FindId(root, "s")!.ScrollState;
        True(ss is not null, "the scroll box must track its scroll state");
        var port = 200f - BoxEngine.ScrollGutter;
        True(ss!.ContainerWidth == port && ss.ContainerHeight == port,
            $"the scrollport is {port}px square, got {ss.ContainerWidth}x{ss.ContainerHeight}");
        True(ss.MaxScroll == 400f - port && ss.MaxScrollX == 400f - port,
            $"the scroll range reaches the content's far edges, got {ss.MaxScrollX}x{ss.MaxScroll}");
    }

    [Test]
    public static void OverflowAuto_AnUnbreakableWordScrollsSideways()
    {
        // max-height-109: a 15-letter word at 200px Ahem in an auto box overflows it sideways.
        var root = Layout("#s{max-height:400px;overflow:auto} #c{font:200px/1 Ahem}",
            "<div id=\"s\"><div id=\"c\">XXXXXXXXXXXXXXX</div></div>");
        var ss = FindId(root, "s")!.ScrollState;
        True(ss is not null && ss.NeedsHorizontalScrollbar,
            $"the word must overflow sideways, got {(ss is null ? "no scroll state" : $"content {ss.ContentWidth} in {ss.ContainerWidth}")}");
    }

    [Test]
    public static void OverflowScroll_ContentThatFitsShowsNoThumb()
    {
        // max-height-108: one 200px glyph in an 'overflow: scroll' box fits — both bars stay
        // inactive (no axis may report overflow).
        var root = Layout("#s{max-height:400px;overflow:scroll} #c{font:200px/1 Ahem}",
            "<div id=\"s\"><div id=\"c\">X</div></div>");
        var ss = FindId(root, "s")!.ScrollState;
        True(ss is null || !ss.NeedsScrollbar && !ss.NeedsHorizontalScrollbar,
            $"nothing overflows, got content {ss?.ContentWidth}x{ss?.ContentHeight} in {ss?.ContainerWidth}x{ss?.ContainerHeight}");
    }

    [Test]
    public static void ClippingBoxes_PaintTheirOwnBorders()
    {
        // overflow-scrollbar-001: a clipping box's border lies outside the padding box it clips
        // its content to; the box's own border must still paint, bars or not.
        foreach (var overflow in new[] { "visible", "hidden", "scroll", "auto" })
        {
            var root = Layout($"#s{{border:5px solid blue;overflow:{overflow};width:100px;height:50px;padding:10px}}",
                "<div id=\"s\">x</div>");
            using var bitmap = Drawer.DrawToBitmap(400, 300, root, new Viewport { ViewportHeight = 300 });
            var bb = FindId(root, "s")!.Box.BorderBox;
            var left = bitmap.GetPixel((int)bb.Left + 2, (int)((bb.Top + bb.Bottom) / 2));
            var right = bitmap.GetPixel((int)bb.Right - 2, (int)((bb.Top + bb.Bottom) / 2));
            True(left == SkiaSharp.SKColors.Blue && right == SkiaSharp.SKColors.Blue,
                $"overflow:{overflow} must paint its blue border, got {left} / {right}");
        }
    }

    [Test]
    public static void ViewportOverflow_ComesFromTheRootThenTheBody()
    {
        // overflow-root-001 / overflow-html-body-001: the root's value applies to the
        // viewport; when the root's is 'visible', the body's does instead.
        True(BoxEngine.ViewportOverflow(Layout("html{overflow:hidden}", "<p>x</p>")) == OverflowType.Hidden, "root value");
        True(BoxEngine.ViewportOverflow(Layout("body{overflow:hidden}", "<p>x</p>")) == OverflowType.Hidden, "body value");
        True(BoxEngine.ViewportOverflow(Layout("html{overflow:auto} body{overflow:hidden}", "<p>x</p>")) == OverflowType.Auto,
            "a non-visible root value wins over the body's");
        True(BoxEngine.ViewportOverflow(Layout("", "<p>x</p>")) == OverflowType.Visible, "default");
    }

    [Test]
    public static void OverflowAuto_ContentThatFillsTheBoxDoesNotScrollSideways()
    {
        // An 'auto' box lays its content out before its bars exist: a vertical overflow must
        // not turn the full-width content into a phantom horizontal overflow.
        var root = Layout("#s{width:200px;height:100px;overflow:auto} #c{height:300px}",
            "<div id=\"s\"><div id=\"c\"></div></div>");
        var ss = FindId(root, "s")!.ScrollState;
        True(ss is not null && ss.NeedsScrollbar, "the box must scroll vertically");
        True(!ss!.NeedsHorizontalScrollbar, $"no horizontal overflow, got content {ss.ContentWidth} in {ss.ContainerWidth}");
    }

    [Test]
    public static void ScrollExtent_IgnoresAbsposChildWithAnOutsideContainingBlock()
    {
        // abspos-overflow-001..009: the abs-pos child of an overflow box whose containing block
        // is the viewport escapes the box's clip (§11.1.1) and must not widen its scrollable
        // area — 11.1.1 paints it unclipped, so there is nothing to scroll into view. The box's
        // only child being out-of-flow used to fall into the raw widest-child fallback, which
        // read the child's margin box and — on every re-layout (DrawToBitmap runs one) — made
        // the 200px box's content 300px wide and the thumb proportionally short.
        var html = "<!DOCTYPE html><style>body{margin:0}" +
            "#s{width:200px;height:200px;overflow:scroll}" +
            "#a{position:absolute;top:0;left:0;width:300px;height:20px}</style>" +
            "<div id=\"s\"><div id=\"a\">PASS</div></div>";
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        var s = FindId(page.Root, "s")!;
        for (var pass = 0; pass < 2; pass++)
        {
            BoxEngine.Layout(page.Root, 800, 600);
            var ss = s.ScrollState;
            True(ss is not null, $"pass {pass}: the scroll box must track its state");
            True(ss!.ContentWidth == 200f,
                $"pass {pass}: the escaped abspos child must not widen the content past the box (the 200px full-width thumb the references render), got {ss.ContentWidth}");
        }
    }
}
