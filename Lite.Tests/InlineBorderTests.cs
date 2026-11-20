using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §8.4/§8.6 inline box borders: the horizontal border of a non-replaced inline box is
/// laid out (it advances the line), and the borders paint — around an unsplit box's border box,
/// and per line for a box split over lines with the §8.6 side masks (a rtl box's trailing
/// decorations at the first line's end, leading ones at the last line's start).
/// </summary>
public static class InlineBorderTests
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

    private static void Edges(BoxDimensions box, out float top, out float right, out float bottom, out float left)
    {
        top = box.Border.Top;
        right = box.Border.Right;
        bottom = box.Border.Bottom;
        left = box.Border.Left;
    }

    [Test]
    public static void BorderColor_InitialLeak_ReadsCurrentColor()
    {
        // border-color-012's cascade: a 'border' shorthand that omits the color stores the
        // literal 'initial', and a child's 'border-color: inherit' picks it up — the border
        // color reads as the property's initial value, 'currentColor', resolved against the
        // CHILD's own color (green), and the width reads its initial 'medium' (3px).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<style>.test { display: block; color: red; border: none; }" +
            ".test > .inner { border-color: inherit; border-style: solid; color: green; }</style>" +
            "<div class='test'><span class='inner'>x</span></div></body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var border = span.GetBorderTopColor();
        True(border.Red == 0 && border.Green == 128 && border.Blue == 0,
            $"the border color resolves currentColor to the span's own green, got {border}");
        True(Math.Abs(span.GetOwnBorderWidth().Top - 3f) < 0.5f,
            $"the leaked initial width reads 'medium' (3px), got {span.GetOwnBorderWidth().Top}");
    }

    [Test]
    public static void ElementText_InlineBorders_LayOutAndRecord()
    {
        // border-width-applies-to-008's shape at 1/9th scale: an element carrying its own text
        // takes a Box with real Border edges, and the line advances by the horizontal borders
        // (90px content + 2×10px border = 110px used width on the line).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='border:10px solid red;display:inline;font:10px/1 Ahem'>ab</div>" +
            "</body></html>");
        var div = FindNode(root, n => n.TagName == "DIV")!;
        Edges(div.Box, out var top, out var right, out var bottom, out var left);
        True(top == 10f && right == 10f && bottom == 10f && left == 10f,
            $"the inline div's Border edges record the declared widths, got T{top} R{right} B{bottom} L{left}");
        True(Math.Abs(div.Box.BorderBox.Width - 40f) < 0.5f,
            $"the border box spans content + both horizontal borders (\"ab\" is 20px + 2×10px), got {div.Box.BorderBox.Width}");
        True(Math.Abs(div.Box.BorderBox.Height - 30f) < 0.5f,
            $"the border box grows past the content area vertically too (10px + 2×10px), got {div.Box.BorderBox.Height}");
    }

    [Test]
    public static void Wrapper_InlineBorders_AssemblePerLineFragments()
    {
        // A boxless inline wrapper (its text lives in children) paints its borders from the
        // per-line fragments line layout assembles: one line, all four sides.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<span style='border:10px solid red;padding:2px'><b style='margin:0 4px'>ab</b></span>" +
            "</body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var boxes = span.InlineFragmentBoxes;
        True(boxes is { Count: 1 }, $"the wrapper records one line fragment, got {boxes?.Count ?? 0}");
        if (boxes is { Count: 1 })
        {
            Edges(boxes[0], out var top, out var right, out var bottom, out var left);
            True(top == 10f && right == 10f && bottom == 10f && left == 10f,
                $"both edges landed on the line: all four borders, got T{top} R{right} B{bottom} L{left}");
            // The wrapper's content box starts past its own border+padding (x=12); the b's own
            // 4px leading margin lives INSIDE the wrapper's content area, so it is not deflated.
            True(Math.Abs(boxes[0].ContentBox.Left - (2f + 10f)) < 0.5f,
                $"content starts past border+padding (x=12), got {boxes[0].ContentBox.Left}");
        }
    }

    [Test]
    public static void RtlSplit_TrailingJoinsFirstLine_LeadingJoinsLastLine()
    {
        // rtl-span-only.xht: a rtl box split over lines joins its TRAILING decorations at the
        // first line's end and its LEADING ones at the last line's start — the join sides stay
        // empty, and the border box extends 2px above the text's content area.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div><span style='direction:rtl;border:2px solid;padding:0 10px 0 5px;margin:0 60px 0 30px'>One<br/>Two</span></div>" +
            "</body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var boxes = span.InlineFragmentBoxes;
        True(boxes is { Count: 2 }, $"the split wrapper records two line fragments, got {boxes?.Count ?? 0}");
        if (boxes is { Count: 2 })
        {
            Edges(boxes[0], out var top1, out var right1, out var bottom1, out var left1);
            Edges(boxes[1], out var top2, out var right2, out var bottom2, out var left2);
            True(right1 == 2f && left1 == 0f,
                $"line 1 carries the trailing (right) border only, got R{right1} L{left1}");
            True(left2 == 2f && right2 == 0f,
                $"line 2 carries the leading (left) border only, got L{left2} R{right2}");
            True(top1 == 2f && bottom1 == 2f && top2 == 2f && bottom2 == 2f,
                "the top/bottom borders paint on every line");
            // The border box reaches 2px above the line's content area (nothing vertical is
            // reserved in the line box, §8.4).
            True(boxes[1].BorderBox.Left > boxes[0].BorderBox.Left + 25f,
                $"line 2's border box starts past line 1's trailing margin (30px margin + text), got {boxes[1].BorderBox.Left} vs {boxes[0].BorderBox.Left}");
        }
    }

    [Test]
    public static void LtrSplit_LeadingJoinsFirstLine_TrailingJoinsLastLine()
    {
        // The LTR form: leading decorations at the first line's start, trailing ones at the
        // last line's end.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div><span style='border:2px solid;padding:0 10px 0 5px;margin:0 60px 0 30px'>One<br/>Two</span></div>" +
            "</body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var boxes = span.InlineFragmentBoxes;
        True(boxes is { Count: 2 }, $"the split wrapper records two line fragments, got {boxes?.Count ?? 0}");
        if (boxes is { Count: 2 })
        {
            Edges(boxes[0], out var top1, out var right1, out var bottom1, out var left1);
            Edges(boxes[1], out var top2, out var right2, out var bottom2, out var left2);
            True(left1 == 2f && right1 == 0f,
                $"line 1 carries the leading (left) border only, got L{left1} R{right1}");
            True(right2 == 2f && left2 == 0f,
                $"line 2 carries the trailing (right) border only, got R{right2} L{left2}");
            True(top1 == 2f && bottom1 == 2f && top2 == 2f && bottom2 == 2f,
                "the top/bottom borders paint on every line");
            // Line 2 indents by the trailing margin (60px): One line starts at x=8+30+5+2, Two at
            // 8 + 60 (its trailing margin pushes nothing — line 2 starts at the content edge with
            // the trailing edges after the text), so the border boxes' LEFT edges differ by the
            // leading margin (30+5+2 = 37px of line-1 edges vs 0 on line 2).
            True(boxes[1].BorderBox.Left < boxes[0].BorderBox.Left,
                $"line 2's border box starts at the content edge, left of line 1's (which carries the leading edges), got {boxes[1].BorderBox.Left} vs {boxes[0].BorderBox.Left}");
        }
    }

    [Test]
    public static void InlineBorder_WrapsTheFontContentArea_NotTheLineHeight()
    {
        // §10.6.1 (inlines-003, outline-020): in a 16px Ahem line with line-height 4 (64px), a
        // bordered span's content area is the font's 16px (ascent 12.8 + descent 3.2), centred
        // in the line box — its border must not reach the 64px line edges, or the boxes of two
        // wrapped lines meet.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='font:16px/4 Ahem'><span style='border:8px solid blue'>X</span></div></body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var area = BoxEngine.InlineContentArea(span, span.Box);
        True(MathF.Abs(area.ContentBox.Height - 16f) < 0.5f,
            $"the content area is the font's 16px, got {area.ContentBox.Height}");
        True(MathF.Abs(area.ContentBox.Top - 24f) < 0.5f,
            $"the half-leading (24px) sits above the content area, got top {area.ContentBox.Top}");
        True(area.BorderBox.Top > 0f && area.BorderBox.Bottom < 64f,
            $"the border box stays inside the 64px line, got {area.BorderBox.Top}..{area.BorderBox.Bottom}");
    }

    [Test]
    public static void ClosingMargin_BreaksInsideTheSpansText_NotBeforeIt()
    {
        // c5502-imrgn-r-001: in a 13em box, 'x <span margin-right:4em>xxxxx xxxxx</span> x'
        // fills line 1 up to 'x xxxxx' — the closing margin is glued to the span's last word,
        // so that word moves down with it, not the whole span. Two lines, not three.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<p style='font:15px/1 Ahem;width:13em;margin:0'>x <span style='margin-right:4em'>xxxxx xxxxx</span> x</p></body></html>");
        var p = FindNode(root, n => n.TagName == "P")!;
        True(MathF.Abs(p.Box.ContentBox.Height - 30f) < 0.5f,
            $"the paragraph takes two 15px lines, got height {p.Box.ContentBox.Height}");
    }

    [Test]
    public static void TrailingSpaceAtALineEnd_IsNotCoveredByTheInlineBox()
    {
        // c5502-imrgn-r-002 (§16.6.1): a span broken after 'xx ' does not reach over the
        // removed end-of-line space — its first fragment is one 'xx' (30px) wide, not 45px.
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<p style='font:15px/1 Ahem;width:5em;margin:0'>xx xx <span style='margin-right:4em;background:red'>xx xx</span> xx xx</p></body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var widths = (span.InlineFragments ?? []).Select(f => f.Rect.Width).ToList();
        if (widths.Count == 0 && span.InlineFragmentBoxes is { } boxes) widths = boxes.Select(b => b.ContentBox.Width).ToList();
        True(widths.Count >= 2 && MathF.Abs(widths[0] - 30f) < 0.5f,
            $"the span's first fragment is 30px wide, got [{string.Join(", ", widths)}]");
    }

    [Test]
    public static void ContentAfterAWrappedRun_SitsBesideItsLastLine()
    {
        // A run that wraps breaks into one item per line, so what follows it continues its LAST
        // line: in a 5em (75px) box 'xx xx xx<span>Y</span>' is 'xx xx' / 'xx' + 'Y', the Y at
        // x=30 on line 2 — not past the whole run's width (c5502-imrgn-r-003/004).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<p style='font:15px/1 Ahem;width:5em;margin:0'>xx xx xx<span>Y</span></p></body></html>");
        var span = FindNode(root, n => n.TagName == "SPAN")!;
        var box = span.Box.ContentBox;
        if (box.IsEmpty && span.InlineFragmentBoxes is { Count: > 0 } frags) box = frags[0].ContentBox;
        True(MathF.Abs(box.Left - 30f) < 0.5f && MathF.Abs(box.Top - 15f) < 0.5f,
            $"Y follows 'xx' on line 2 at (30,15), got ({box.Left},{box.Top})");
    }
}
