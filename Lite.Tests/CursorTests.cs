using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §18.1 'cursor': the keyword set, inheritance, invalid lists, the
/// context-resolved 'auto', and url() cursors with fallback — read through the same hit
/// regions and lookup the native window uses.</summary>
public static class CursorTests
{
    // A 1x1 PNG: a url() cursor image the engine can load without a network.
    private const string LoadableImage =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static (CursorType Type, string? Image) CursorAt(string css, string body, float x, float y)
    {
        var page = Parser.ParseChildPage(
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} {css}</style></head><body>{body}</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var (_, regions) = Drawer.Draw(800, 600, page.Root, new Viewport { ViewportHeight = 600 });
        return HitRegion.CursorAt(regions, x, y);
    }

    private const string Box = "<div id='b' style='width:100px; height:100px'></div>";

    [Test]
    public static void EveryCss21Keyword_ResolvesToItsCursor()
    {
        var expected = new Dictionary<string, CursorType>
        {
            ["default"] = CursorType.Default, ["pointer"] = CursorType.Pointer, ["text"] = CursorType.Text,
            ["crosshair"] = CursorType.Crosshair, ["move"] = CursorType.Move,
            ["e-resize"] = CursorType.EResize, ["ne-resize"] = CursorType.NeResize, ["nw-resize"] = CursorType.NwResize,
            ["n-resize"] = CursorType.NResize, ["se-resize"] = CursorType.SeResize, ["sw-resize"] = CursorType.SwResize,
            ["s-resize"] = CursorType.SResize, ["w-resize"] = CursorType.WResize,
            ["wait"] = CursorType.Wait, ["help"] = CursorType.Help, ["progress"] = CursorType.Progress,
        };
        foreach (var (keyword, type) in expected)
        {
            var (actual, _) = CursorAt($"#b {{ cursor: {keyword} }}", Box, 50, 50);
            True(actual == type, $"cursor: {keyword} must resolve to {type}, got {actual}");
        }
        True(CursorAt("", Box, 50, 50).Type == CursorType.Default, "'auto' over an empty box is the default arrow");
    }

    [Test]
    public static void Cursor_IsInheritedAndInheritKeywordWalksUp()
    {
        True(CursorAt("#o { cursor: pointer }", "<div id='o'>" + Box + "</div>", 50, 50).Type == CursorType.Pointer,
            "cursor is inherited by a child without its own value");
        True(CursorAt("#o { cursor: help } #b { cursor: inherit }", "<div id='o'>" + Box + "</div>", 50, 50).Type == CursorType.Help,
            "'inherit' takes the parent's cursor");
    }

    [Test]
    public static void AutoOverText_IsTheTextCursorAndOnlyOverTheText()
    {
        var body = "<div style='font: 20px/1 Ahem, monospace; width: 400px'>text sample</div>";
        True(CursorAt("", body, 10, 10).Type == CursorType.Text, "'auto' over text resolves to the text cursor");
        True(CursorAt("", body, 380, 10).Type == CursorType.Default, "'auto' beside the text stays the arrow");
        True(CursorAt("div { cursor: pointer }", body, 10, 10).Type == CursorType.Pointer,
            "an explicit keyword wins over the text context");
    }

    [Test]
    public static void InvalidCursorLists_AreIgnoredLikeAnyInvalidDeclaration()
    {
        // A url() list must end with a keyword, and every keyword must be a CSS 2.1 one.
        var body = "<div id='o' style='cursor: help'>" + Box + "</div>";
        True(CursorAt("#b { cursor: url(x.cur) }", body, 50, 50).Type == CursorType.Help,
            "a url() with no closing keyword is invalid: the inherited cursor stays");
        True(CursorAt("#b { cursor: invalid-value, crosshair }", body, 50, 50).Type == CursorType.Help,
            "a list with an unknown keyword is invalid: the inherited cursor stays");
    }

    [Test]
    public static void UrlCursors_UseTheFirstLoadableImageElseFallBack()
    {
        var (loaded, image) = CursorAt($"#b {{ cursor: url(\"{LoadableImage}\"), pointer }}", Box, 50, 50);
        True(loaded == CursorType.Custom && image == LoadableImage, $"a loadable url() cursor is used, got {loaded} {image}");
        var (fallback, none) = CursorAt("#b { cursor: url(http://127.0.0.1:1/missing.cur), crosshair }", Box, 50, 50);
        True(fallback == CursorType.Crosshair && none is null, $"an unloadable url() falls back to the keyword, got {fallback}");
        var (second, secondImage) = CursorAt(
            $"#b {{ cursor: url(http://127.0.0.1:1/missing.cur), url(\"{LoadableImage}\"), wait }}", Box, 50, 50);
        True(second == CursorType.Custom && secondImage == LoadableImage, "the next url() is tried before the keyword");
    }

    [Test]
    public static void NativeHost_MapsEveryKeywordToItsSystemCursor()
    {
        const int arrow = 32512;
        var expected = new Dictionary<CursorType, int>
        {
            [CursorType.Default] = arrow, [CursorType.Auto] = arrow, [CursorType.Pointer] = 32649, [CursorType.Text] = 32513,
            [CursorType.Crosshair] = 32515, [CursorType.Move] = 32646, [CursorType.Wait] = 32514,
            [CursorType.Help] = 32651, [CursorType.Progress] = 32650,
            [CursorType.EResize] = 32644, [CursorType.WResize] = 32644, [CursorType.NResize] = 32645, [CursorType.SResize] = 32645,
            [CursorType.NeResize] = 32643, [CursorType.SwResize] = 32643, [CursorType.NwResize] = 32642, [CursorType.SeResize] = 32642,
        };
        foreach (var (type, id) in expected)
            Equal(id, Lite.Platform.Win32WindowHost.SystemCursorId(type));
    }

    [Test]
    public static void InlineElements_AreCursorAndClickTargetsOverTheirOwnBox()
    {
        // cursor-applies-to-008: an inline box's cursor shows over its own area.
        var (type, _) = CursorAt("", "<div style='display:inline; cursor:pointer; background:blue; font: 20px/1 Ahem'>Filler</div>", 10, 10);
        True(type == CursorType.Pointer, $"an inline element's cursor must show over its box, got {type}");
    }

    [Test]
    public static void CursorOnlyTextRegions_NeverTakeClicks()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'><a href='http://test/x' style='cursor: auto'>link text</a></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var (_, regions) = Drawer.Draw(800, 600, page.Root, new Viewport { ViewportHeight = 600 });
        var top = regions.LastOrDefault(r => !r.CursorOnly && r.Bounds.Contains(5, 5));
        True(top?.Href == "http://test/x", $"the link stays the click target under its own text, got {top?.Href ?? "nothing"}");
    }
}
