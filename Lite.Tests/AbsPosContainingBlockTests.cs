using Lite;
using Lite.Models;
using Lite.Layout;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §10.1 containing-block regressions from the abspos / containing-block-initial
/// families: the root element itself positioned, positioned inline ancestors, and body background
/// propagation through anonymous table wrappers (§14.2).</summary>
public static class AbsPosContainingBlockTests
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
    public static void AbsPosRoot_ResolvesAgainstInitialContainingBlock()
    {
        // abspos-containing-block-initial-004a: html itself is position:absolute at 100,100
        // inside the initial containing block; the positioned pass must resolve it.
        var root = Layout("<!DOCTYPE html><html style=\"position:absolute; left:100px; top:100px;" +
            "width:100px; height:100px; border:10px solid black; margin:0\"><body></body></html>");
        var box = root.Box.ContentBox;
        True(box.Left == 110f && box.Top == 110f,
            $"html border box must sit at 110,110 (content 100,100 + border 10), got {box.Left},{box.Top}");
        True(box.Width == 100f && box.Height == 100f, $"html content box must be 100x100, got {box.Width}x{box.Height}");
    }

    [Test]
    public static void AbsPosInlineContainingBlock_IsTheInlineExtent()
    {
        // abspos-inline-003: the abs-pos span's containing block is the RELATIVE inline span
        // around it (§10.1) — its padding box, where the line fragment landed.
        var root = Layout("<!DOCTYPE html><style>p{font:16px monospace;margin:0}</style>" +
            "<p><span>filler text </span>The test has <span id=\"t\"><span id=\"a\" " +
            "style=\"position:absolute; top:0; left:0\">PASSED</span><span>FAILED</span></span>.</p>");
        var a = Find(root, "SPAN", "a");
        var t = Find(root, "SPAN", "t");
        True(a is not null && t is not null, "nodes missing");
        // The abs box's top-left must coincide with the .test span's fragment top-left.
        True(Math.Abs(a!.Box.ContentBox.Left - t!.Box.ContentBox.Left) < 1f &&
             Math.Abs(a.Box.ContentBox.Top - t.Box.ContentBox.Top) < 1f,
            $"abs box {a.Box.ContentBox.Location} must align with the inline CB fragment {t.Box.ContentBox.Location}");
    }

    [Test]
    public static void BodyBackground_PropagatesThroughAnonymousTableWrappers()
    {
        // abspos-containing-block-initial-004e: html display:table wraps BODY in anonymous
        // table boxes; §14.2 still propagates the DOM body's background to the canvas.
        var root = Layout("<!DOCTYPE html><html style=\"display:table\"><body " +
            "style=\"position:absolute; left:100px; top:100px; width:100px; height:100px; " +
            "border:10px solid black; background:yellow; margin:0\"></body></html>");
        var bitmap = Drawer.DrawToBitmap(200, 200, root, new Viewport { ViewportHeight = 200 });
        using (bitmap)
        {
            // The canvas must be yellow (propagated) away from the box, not the UA white.
            True(bitmap.GetPixel(180, 180) == SKColors.Yellow,
                $"canvas must take the body's propagated yellow, got {bitmap.GetPixel(180, 180)}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
