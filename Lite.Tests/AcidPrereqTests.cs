using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// Phase B item 6 — Acid2 prerequisites, exercised in isolation: percent-encoded data: image
/// payloads, straight-alpha PNG decode, &lt;object&gt; nested fallback, background-attachment:fixed,
/// and min/max-width clamping on absolutely-positioned boxes.
/// </summary>
public static class AcidPrereqTests
{
    // Acid2's 1×1 yellow PNG — a base64 payload whose '/' and '=' are percent-encoded (%2F/%3D).
    private const string YellowPixelPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1Pe" +
        "AAAADElEQVR42mP4%2F58BAAT%2FAf9jgNErAAAAAElFTkSuQmCC";

    // Acid2's 2×2 PNG with an 8-bit alpha channel (two opaque corners, two transparent).
    private const string AlphaCornerPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91Jpz" +
        "AAAABnRSTlMAAAAAAABupgeRAAAABmJLR0QA%2FwD%2FAP%2BgvaeTAAAAEUlEQVR42mP4" +
        "%2F58BCv7%2FZwAAHfAD%2FabwPj4AAAAASUVORK5CYII%3D";

    [Test]
    public static void DataUri_DecodesPercentEncodedBase64()
    {
        var ok = DataUri.TryDecodeBytes(YellowPixelPng, out var bytes, out var mediaType);
        True(ok, "percent-encoded base64 data: should decode");
        Equal("image/png", mediaType);
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        True(bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            "decoded bytes should start with the PNG signature");
    }

    [Test]
    public static void ResourceLoader_DecodesPercentEncodedDataImage()
    {
        var bmp = ResourceLoader.FetchImage(YellowPixelPng, null);
        True(bmp is not null, "percent-encoded data:image/png should decode to a bitmap");
        Equal(1, bmp!.Width);
        Equal(1, bmp.Height);
    }

    [Test]
    public static void AlphaPng_PreservesTransparency()
    {
        var bmp = ResourceLoader.FetchImage(AlphaCornerPng, null);
        True(bmp is not null, "alpha PNG should decode");
        Equal(2, bmp!.Width);
        Equal(2, bmp.Height);
        // The 2×2 image has both fully-opaque and fully-transparent pixels — straight (unpremul)
        // alpha must be preserved so the two offset copies composite into a solid block.
        bool sawOpaque = false, sawTransparent = false;
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                var a = bmp.GetPixel(x, y).Alpha;
                if (a == 255) sawOpaque = true;
                if (a == 0) sawTransparent = true;
            }
        True(sawOpaque, "expected at least one fully-opaque pixel");
        True(sawTransparent, "expected at least one fully-transparent pixel");
    }

    [Test]
    public static void Object_RendersImageAndSuppressesFallback()
    {
        // A loaded <object> is a replaced element: its image is set and its fallback is dropped.
        var node = Parser.ParseFragment($"<object data=\"{YellowPixelPng}\"><b>fallback</b></object>")[0];
        Equal("OBJECT", node.TagName);
        True(node.Image is not null, "object with a decodable image should set node.Image");
        True(node.Children.Count == 0, "a loaded object must not render its fallback content");
    }

    [Test]
    public static void Object_FallsBackWhenResourceFails()
    {
        // data:application/x-unknown,ERROR can't decode as an image → render the fallback content.
        var node = Parser.ParseFragment(
            "<object data=\"data:application/x-unknown,ERROR\"><b>fallback</b></object>")[0];
        Equal("OBJECT", node.TagName);
        True(node.Image is null, "object with an undecodable resource must not set an image");
        True(node.Children.Count > 0, "a failed object must render its fallback content");
    }

    [Test]
    public static void Object_NestedFallbackChain()
    {
        // Acid2's eyes: unknown type → (skipped non-image type) → the real image wins.
        var node = Parser.ParseFragment(
            "<object data=\"data:application/x-unknown,ERROR\">" +
              "<object data=\"about:blank\" type=\"text/html\">" +
                $"<object data=\"{YellowPixelPng}\">ERROR</object>" +
              "</object>" +
            "</object>")[0];
        True(node.Image is null, "outer object (unknown type) falls back");
        var middle = node.Children[0];
        Equal("OBJECT", middle.TagName);
        True(middle.Image is null, "middle object (text/html) falls back");
        var inner = middle.Children[0];
        Equal("OBJECT", inner.TagName);
        True(inner.Image is not null, "inner object loads the PNG");
        True(inner.Children.Count == 0, "inner object's text fallback is dropped");
    }

    [Test]
    public static void BackgroundAttachment_FixedDetected()
    {
        var fixedBg = new LayoutNode(null, "DIV", "", Parser.ParseFragment("<div></div>")[0].Style);
        fixedBg.StyleOverrides["background"] = $"red url({AlphaCornerPng}) fixed 1px 0";
        True(fixedBg.IsBackgroundFixed(), "background shorthand with 'fixed' should be detected");

        var scrollBg = new LayoutNode(null, "DIV", "", Parser.ParseFragment("<div></div>")[0].Style);
        scrollBg.StyleOverrides["background"] = $"red url({AlphaCornerPng}) 1px 0";
        True(!scrollBg.IsBackgroundFixed(), "background without 'fixed' should scroll");
    }

    private static readonly AngleSharp.Css.Dom.ICssStyleDeclaration _style =
        Parser.ParseFragment("<div></div>")[0].Style;

    private static LayoutNode Box(Dictionary<string, string> styles, params LayoutNode[] children)
    {
        var node = new LayoutNode(null, "DIV", "", _style);
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

    [Test]
    public static void AbsolutePositioned_ClampsToMaxWidth()
    {
        // Acid2's scalp pattern: width:140%; max-width:4em pins the box to a fixed size.
        var abs = Box(new()
        {
            ["position"] = "absolute", ["top"] = "0", ["left"] = "0",
            ["width"] = "140%", ["max-width"] = "48px", ["height"] = "10px",
        });
        var cb = Box(new() { ["position"] = "relative", ["width"] = "200px", ["height"] = "100px" }, abs);

        var root = new LayoutNode(null, "HTML", "", _style);
        var body = new LayoutNode(null, "BODY", "", _style);
        root.StyleOverrides["display"] = "block";
        body.StyleOverrides["display"] = "block";
        root.AddChild(body);
        body.AddChild(cb);
        BoxEngine.Layout(root, 800, 600);

        True(Math.Abs(abs.Box.ContentBox.Width - 48f) < 0.5f,
            $"expected width clamped to max-width 48, got {abs.Box.ContentBox.Width}");
    }
}
