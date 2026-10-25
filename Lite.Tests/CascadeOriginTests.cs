using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §6.4.1 origins in the computed cascade, and §14.2 canvas propagation
/// from a root whose background is transparent.</summary>
public static class CascadeOriginTests
{
    private static LayoutNode Layout(string html)
    {
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children)
            if (Find(c, tag) is { } found) return found;
        return null;
    }

    [Test]
    public static void AuthorUniversalRule_BeatsTheMoreSpecificUaRule()
    {
        // background-root-009: '* { margin: 1em }' is an author rule; the UA's
        // 'body { margin: 8px }' loses to it whatever the specificity.
        var body = Find(Layout("<!DOCTYPE html><style>* { margin: 20px }</style><body><p>x</p></body>"), "BODY");
        True(body is not null, "body missing");
        True(body!.Box.Margin.Top == 20f, $"author '*' margin must win over the UA body margin, got {body.Box.Margin.Top}");
    }

    [Test]
    public static void TransparentRoot_PropagatesTheBodyBackgroundToTheCanvas()
    {
        // 'transparent' computes to rgba(0, 0, 0, 0): the root counts as transparent and the
        // body's background fills the canvas (it used to clear the canvas to nothing).
        var root = Layout("<!DOCTYPE html><style>html { background: transparent } body { background: green; height: 10px }</style><body></body>");
        using var bitmap = Drawer.DrawToBitmap(200, 200, root, new Viewport { ViewportHeight = 200 });
        True(bitmap.GetPixel(150, 150) == new SKColor(0, 128, 0), $"canvas must be the body's green, got {bitmap.GetPixel(150, 150)}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
