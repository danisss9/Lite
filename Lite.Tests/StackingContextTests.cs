using Lite;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §9.9.1 / Appendix E stacking regressions: a positioned box with
/// 'z-index: auto' is a stacking item without being a stacking context, so its positioned
/// descendants join the parent context and its relative offset still moves them.</summary>
public static class StackingContextTests
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
    public static void AutoZIndexParent_DoesNotTrapNegativeZChild()
    {
        // z-index-abspos-001: the red abs-pos child with z-index:-1 of a green abs-pos parent
        // with z-index:auto belongs to the ROOT stacking context (§9.9.1: 'auto' "does not
        // establish a new local stacking context"; Appendix E step 8) and paints in step 2 —
        // below the parent's step-3 background. No red may be visible.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}</style>" +
            "<div style=\"position:absolute; top:20px; left:20px; width:400px; height:150px; background:green\">" +
            "<div style=\"position:absolute; top:0; left:0; width:300px; height:100px; background:red; z-index:-1\"></div>" +
            "</div>");
        var bitmap = Drawer.DrawToBitmap(500, 200, root, new Viewport { ViewportHeight = 200 });
        using (bitmap)
        {
            True(bitmap.GetPixel(200, 100) == SKColors.Green,
                $"parent background must be green at 200,100, got {bitmap.GetPixel(200, 100)}");
            for (var y = 0; y < 200; y += 5)
                for (var x = 0; x < 500; x += 5)
                    True(bitmap.GetPixel(x, y) != SKColors.Red,
                        $"no red may be visible anywhere, found red at {x},{y}");
        }
    }

    [Test]
    public static void AutoZIndexRelativeAncestor_ReplaysItsOffsetForPositionedDescendants()
    {
        // A z-index:auto relative ancestor no longer paints its positioned descendants inside
        // its own subtree (Appendix E step 8: they join the parent context), so its §9.4.3
        // offset is replayed over them when they paint from the context root — the red box
        // moves down with its shifted parent and the unshifted spot stays white.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}</style>" +
            "<div style=\"position:relative; top:25px; height:60px; width:200px\">" +
            "<div style=\"position:absolute; top:0; left:0; width:40px; height:40px; background:red\"></div>" +
            "</div>");
        var bitmap = Drawer.DrawToBitmap(300, 200, root, new Viewport { ViewportHeight = 200 });
        using (bitmap)
        {
            True(bitmap.GetPixel(10, 30) == SKColors.Red,
                $"red box must sit 25px down (at 10,30), got {bitmap.GetPixel(10, 30)}");
            True(bitmap.GetPixel(10, 5) == SKColors.White.WithAlpha((byte)255),
                $"unshifted spot at 10,5 must stay background, got {bitmap.GetPixel(10, 5)}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
