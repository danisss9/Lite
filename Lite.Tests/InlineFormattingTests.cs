using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 inline formatting context and line box unit tests (lane 6.A).
/// </summary>
public static class InlineFormattingTests
{
    private static LayoutNode? FindNode(LayoutNode n, Func<LayoutNode, bool> pred) =>
        pred(n) ? n : n.Children.Select(c => FindNode(c, pred)).FirstOrDefault(r => r is not null);

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static LayoutNode Layout(string html)
    {
        FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    [Test]
    public static void WhiteSpaceCollapse_FirstInlineKeepsSpace_FollowingSpacesDropAcrossStrut()
    {
        // CSS 2.1 §16.6.1: collapsible spaces following another collapsible space across inline
        // boundaries (including across an empty inline strut) collapse away, while the first
        // inline keeps its own trailing space.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0;font:20px/1 Ahem'>" +
            "<div><span id='a'>X </span> <span id='empty'> </span> <span id='b'> X</span></div>" +
            "</body></html>");
        var a = FindNode(root, n => n.Id == "a")!;
        var b = FindNode(root, n => n.Id == "b")!;
        True(Math.Abs(a.Box.ContentBox.Width - 40f) < 0.5f,
            $"first span keeps its trailing space (2em = 40px), got {a.Box.ContentBox.Width}");
        True(Math.Abs(b.Box.ContentBox.Left - 40f) < 0.5f,
            $"second span starts right after first span's space (x=40), got {b.Box.ContentBox.Left}");
        True(Math.Abs(b.Box.ContentBox.Width - 20f) < 0.5f,
            $"second span's leading space collapsed away (1em = 20px), got {b.Box.ContentBox.Width}");
    }

    [Test]
    public static void MixedChildrenPre_PreservesNewlinesAndSpaces()
    {
        // CSS 2.1 §16.6: text nodes inside a mixed-children element with white-space: pre
        // must preserve newlines and spaces (not collapse them via CollapseWhitespace).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0;font:20px/1 Ahem'>" +
            "<pre id='p'><span>A</span>\n<span>B</span></pre>" +
            "</body></html>");
        var pre = FindNode(root, n => n.Id == "p")!;
        var spans = pre.Children.Where(c => c.TagName == "SPAN").ToList();
        True(spans.Count == 2, $"expected 2 spans, got {spans.Count}");
        True(spans[1].Box.ContentBox.Top > spans[0].Box.ContentBox.Top + 10f,
            $"second span should sit on a new line below first (y0={spans[0].Box.ContentBox.Top}, y1={spans[1].Box.ContentBox.Top})");
    }
}
