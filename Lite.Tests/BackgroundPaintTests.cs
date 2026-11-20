using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Lane 6.H: background layers on inline-level and list-item boxes. CSS 2.1 §14.2:
/// the background image paints and repeats over the element's padding box — for an inline box
/// that is the content area line layout assembled; a list item is an ordinary block box for
/// this layer.</summary>
public static class BackgroundPaintTests
{
    private static string GreenTile()
    {
        var file = Path.Combine(Lite.Conformance.Harness.ConformancePaths.Vendor,
            "css21-official-20110323", "html4", "support", "green15x15.png");
        return "url(data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(file)) + ")";
    }

    private static LayoutNode? FindById(LayoutNode node, string id)
    {
        if (node.Attributes.TryGetValue("id", out var value) && value == id) return node;
        foreach (var child in node.Children)
            if (FindById(child, id) is { } hit) return hit;
        return null;
    }

    private static (int Green, int Red) Count(SKBitmap bitmap, SKRect area)
    {
        True(area.Width > 3 && area.Height > 3, $"the painted area should be laid out, got {area}");
        var left = Math.Max(1, (int)area.Left + 1);
        var top = Math.Max(1, (int)area.Top + 1);
        var right = Math.Min(bitmap.Width - 1, (int)area.Right - 1);
        var bottom = Math.Min(bitmap.Height - 1, (int)area.Bottom - 1);
        var green = 0;
        var red = 0;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red == 0 && c.Green == 128 && c.Blue == 0) green++;
                if (c.Red == 255 && c.Green == 0 && c.Blue == 0) red++;
            }
        return (green, red);
    }

    [Test]
    public static void InlineBackgroundImage_PaintsOverTheContentArea()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>body{margin:0}" +
            "#t{display:inline;font:96px/1 serif;background-color:red}</style></head>" +
            "<body><div id=t>&nbsp;&nbsp;&nbsp;</div></body></html>",
            isSrcdoc: true, "http://test/", 400, 200);
        var node = FindById(page.Root, "t")!;
        node.StyleOverrides["background-image"] = GreenTile();
        node.StyleOverrides["background-repeat"] = "repeat";

        using var bitmap = Drawer.DrawToBitmap(400, 200, page.Root, new Viewport { ViewportHeight = 200 });
        var (green, red) = Count(bitmap, BoxEngine.InlineContentArea(node, node.Box).PaddingBox);
        True(green > 0, "the inline background image should paint over its content area");
        Equal(0, red);
    }

    [Test]
    public static void ListItemBackgroundImage_PaintsOverItsBox()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>body{margin:0}" +
            "#t{display:list-item;width:60px;height:60px;background-color:red}</style></head>" +
            "<body><div id=t></div></body></html>",
            isSrcdoc: true, "http://test/", 400, 200);
        var node = FindById(page.Root, "t")!;
        node.StyleOverrides["background-image"] = GreenTile();
        node.StyleOverrides["background-repeat"] = "repeat";

        using var bitmap = Drawer.DrawToBitmap(400, 200, page.Root, new Viewport { ViewportHeight = 200 });
        var (green, red) = Count(bitmap, node.Box.PaddingBox);
        True(green > 0, "the list item's background image should paint over its padding box");
        Equal(0, red);
    }
}
