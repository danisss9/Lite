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

    [Test]
    public static void TextIndentIntrinsic_PositiveAndNegative_WithPreAndZeroWidthSpace()
    {
        // CSS 2.1 §16.1 + §10.3.5: text-indent (positive and negative) shifts the first line's
        // intrinsic min-content and max-content widths; <pre> newlines break lines, U+200B has
        // zero width, and leaf inline elements include their own horizontal margins.
        var root = Layout(
            "<!DOCTYPE html><html><body style='font-size:12px'>" +
            "<style>body > div, body > pre { float:left; clear:left; height:2em; border:3px solid; } span.ib { display:inline-block; height:1em; width:1em; }</style>" +
            "<div id='d1' style='text-indent:-3em'><span class='ib' style='width:1em'></span>&#8203;<span class='ib' style='width:3em'></span>&#8203;<span class='ib' style='width:3em'></span></div>" +
            "<div id='d2' style='text-indent:3em'><span class='ib' style='width:1em'></span>&#8203;<span class='ib' style='width:2em'></span></div>" +
            "<pre id='p1' style='text-indent:3em'><span class='ib' style='width:1em'></span>\n<span class='ib' style='width:2em'></span></pre>" +
            "<div id='m1'><span style='margin-left:3em'>X</span></div>" +
            "</body></html>");
        var d1 = FindNode(root, n => n.Id == "d1")!;
        var d2 = FindNode(root, n => n.Id == "d2")!;
        var p1 = FindNode(root, n => n.Id == "p1")!;
        var m1 = FindNode(root, n => n.Id == "m1")!;
        True(Math.Abs(d1.Box.ContentBox.Width - 48f) < 0.5f,
            $"d1 max-content = (-3+1+3+3)em = 48px, got {d1.Box.ContentBox.Width}");
        True(Math.Abs(d2.Box.ContentBox.Width - 72f) < 0.5f,
            $"d2 max-content = (3+1+2)em = 72px, got {d2.Box.ContentBox.Width}");
        True(Math.Abs(p1.Box.ContentBox.Width - 48f) < 0.5f,
            $"p1 max-content = max(3+1, 2)em = 48px, got {p1.Box.ContentBox.Width}");
        True(m1.Box.ContentBox.Width > 36f,
            $"m1 includes leaf span's 3em margin-left (>36px), got {m1.Box.ContentBox.Width}");
    }
}
