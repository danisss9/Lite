using Lite.Layout;
using Lite.Models;
using Lite.Extensions;
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

    [Test]
    public static void FirstLineStyles_ScopingAcrossBlockBrAndInlineBlock()
    {
        // CSS 2.1 §5.12.1: ::first-line does not apply after a preceding block child or <br>,
        // while an inline-block on the parent's first line inherits ::first-line for all of its lines.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<style>.fl:first-line { color: green; } .ib { display: inline-block; }</style>" +
            "<div id='afterBlock' class='fl'><div>&nbsp;</div>After block</div>" +
            "<div id='afterBr' class='fl'><br/>After br</div>" +
            "<div id='withIb' class='fl'><span class='ib'>Line1<br/>Line2</span></div>" +
            "</body></html>");
        var afterBlock = FindNode(root, n => n.Id == "afterBlock")!;
        var afterBlockText = afterBlock.Children.Last(c => c.TagName == "#text");
        True(BoxEngine.FirstLineStylesOf(afterBlockText, out _) is null,
            "text after a block sibling is not on parent's first formatted line");

        var afterBr = FindNode(root, n => n.Id == "afterBr")!;
        var afterBrText = afterBr.Children.Last(c => c.TagName == "#text");
        True(BoxEngine.FirstLineStylesOf(afterBrText, out _) is null,
            "text after <br> is on line 2 and must not receive parent's ::first-line");

        var withIb = FindNode(root, n => n.Id == "withIb")!;
        var ibSpan = withIb.Children.First(c => c.TagName == "SPAN");
        var ibLine2 = ibSpan.Children.Last(c => c.TagName == "#text");
        var fl = BoxEngine.FirstLineStylesOf(ibLine2, out var allLines);
        True(fl is not null && allLines,
            "text inside an inline-block on parent's first line inherits ::first-line for all lines");
    }

    [Test]
    public static void WhiteSpacePre_TabStopsAdvanceTo8SpaceMultiples()
    {
        // CSS 2.1 §16.6.1: tabs (U+0009) in preserved whitespace advance to multiples of 8 space widths.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0;font:16px/1 Ahem'>" +
            "<div id='d' style='white-space:pre'>XX&#09;XX</div>" +
            "</body></html>");
        var d = FindNode(root, n => n.Id == "d")!;
        using var font = TextMeasure.CreateFont(d);
        var w = TextMeasure.MeasureWithSpacing("XX\tXX", font, 0f, 0f);
        // 2em ("XX") + 6em (tab to 8em stop) + 2em ("XX") = 10em = 160px.
        True(Math.Abs(w - 160f) < 0.5f, $"expected 10em = 160px for 'XX\\tXX' in 16px Ahem, got {w}");
    }

    [Test]
    public static void WhiteSpaceCollapse_LineEndSpaceBeforeClosingEdgesIsRemoved()
    {
        // CSS 2.1 §16.6.1 step 4: a collapsible space at the end of a line is removed even when
        // closing inline-box edges follow it, so the wrapper's border closes up on the last glyph
        // (border-color-012, block-in-inline-append-002's references).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0;font:20px/1 Ahem'>" +
            "<div><span id='o' style='border-right:10px solid'><span id='t'>XX </span> </span></div>" +
            "</body></html>");
        var t = FindNode(root, n => n.Id == "t")!;
        var o = FindNode(root, n => n.Id == "o")!;
        True(Math.Abs(t.Box.ContentBox.Width - 40f) < 0.5f,
            $"the leaf span's line-end space is removed (2em = 40px), got {t.Box.ContentBox.Width}");
        var outerRight = o.InlineFragmentBoxes is { Count: > 0 } frags
            ? frags[^1].BorderBox.Right
            : o.Box.BorderBox.Right;
        True(Math.Abs(outerRight - 50f) < 0.5f,
            $"the wrapper's right border closes on the glyphs (40 + 10 = 50), got {outerRight}");
    }
    [Test]
    public static void LineHeight_RelativeLengthInheritsAsComputedPixels()
    {
        // CSS 2.1 §10.8.1: a length or percentage 'line-height' computes to an absolute length on
        // the declaring element, and that length is inherited. A 50px child under
        // 'font-size: 10px; line-height: 1em' keeps 10px lines; a 40px child under
        // 'font-size: 20px; line-height: 200%' keeps 40px lines (line-height-125, c548-ln-ht-003).
        // The child declares font longhands, NOT the 'font' shorthand — §15.8 resets line-height
        // to 'normal' on an element whose shorthand omits /<line-height>, which would cut the
        // inheritance the case exercises (line-height-125's .test uses font-size for the same
        // reason).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='font-size:10px;line-height:1em'><div id='a' style='font-size:50px;font-family:Ahem'>X</div></div>" +
            "<div style='font:20px Ahem;line-height:200%'><div id='b' style='font-size:200%'>X</div></div>" +
            "<div style='font:20px Ahem;line-height:2'><div id='c' style='font-size:200%'>X</div></div>" +
            "</body></html>");
        var a = FindNode(root, n => n.Id == "a")!;
        var b = FindNode(root, n => n.Id == "b")!;
        var c = FindNode(root, n => n.Id == "c")!;
        True(Math.Abs(a.GetLineHeight(a.GetFontSize()) - 10f) < 0.5f,
            $"'1em' on a 10px ancestor inherits as 10px, got {a.GetLineHeight(a.GetFontSize())}");
        True(Math.Abs(b.GetLineHeight(b.GetFontSize()) - 40f) < 0.5f,
            $"'200%' on a 20px ancestor inherits as 40px, got {b.GetLineHeight(b.GetFontSize())}");
        True(Math.Abs(c.GetLineHeight(c.GetFontSize()) - 80f) < 0.5f,
            $"a number still inherits as the factor (2 x 40px = 80px), got {c.GetLineHeight(c.GetFontSize())}");
        True(Math.Abs(a.Box.ContentBox.Height - 10f) < 0.5f,
            $"the 50px child's line box is 10px tall, got {a.Box.ContentBox.Height}");
    }
}
