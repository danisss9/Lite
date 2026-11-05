using Lite;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §8.6 (box model for inline elements in bidirectional context) and §16.2
/// direction/unicode-bidi — the official bidi-box-model families. Side margins/padding of
/// inline boxes stay on their physical sides under 'direction: rtl', and a 'bidi-override'
/// RTL scope renders its strong-LTR content in reverse visual order.
/// </summary>
public static class BidiBoxModelTests
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

    [Test]
    public static void RtlOverride_KeepsSpanMarginLeftOnPhysicalLeft()
    {
        // bidi-box-model-010: a span with 'margin-left: 2em' under 'direction: rtl' starts its
        // text one margin further right, exactly like the LTR reference — side margins are
        // unaffected by directionality (§8.6). The margin used to be dropped outright for rtl
        // inlines, landing the span's text 40px early.
        Lite.Layout.FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='text-align:left;width:20em;font:20px/1 Ahem;background:navy'>" +
            "First <span id='s' style='direction:rtl;unicode-bidi:bidi-override;margin-left:40px'>dnoceS</span></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var s = FindNode(page.Root, n => n.Id == "s")!;
        // "First " = 6 glyphs at 20px = 120px, then the 40px margin puts the span's text at 160px.
        True(Math.Abs(s.Box.ContentBox.Left - 160f) < 0.5f,
            $"the rtl span's text should start at 160px (120px of 'First ' + the 40px margin), got {s.Box.ContentBox.Left}");
        True(s.InlineFragments is { Count: > 0 } && s.InlineFragments[^1].Text == "Second",
            $"the override should reverse 'dnoceS' to 'Second', got '{s.InlineFragments?.LastOrDefault().Text}'");
    }

    [Test]
    public static void RtlNormal_KeepsSpanPaddingLeftOnPhysicalLeft()
    {
        // The bidi-box-model padding band (028-033/037-042) through its 'normal' variant: the
        // span's 'padding-left' reserves space on the line even under 'direction: rtl'.
        Lite.Layout.FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='text-align:left;width:20em;font:20px/1 Ahem;background:navy'>" +
            "First <span id='s' style='direction:rtl;padding-left:40px'>dnoceS</span></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var s = FindNode(page.Root, n => n.Id == "s")!;
        True(Math.Abs(s.Box.ContentBox.Left - 160f) < 0.5f,
            $"the rtl span's text should start at 160px (120px of 'First ' + the 40px padding), got {s.Box.ContentBox.Left}");
        // 'normal' never reverses strong-LTR content.
        True(s.InlineFragments is { Count: > 0 } && s.InlineFragments[^1].Text == "dnoceS",
            $"'unicode-bidi: normal' must leave the text order alone, got '{s.InlineFragments?.LastOrDefault().Text}'");
    }

    [Test]
    public static void RtlOverride_OnBlock_ReversesWholeLineContent()
    {
        // bidi-box-model-003: 'direction: rtl; unicode-bidi: bidi-override' on the paragraph
        // reverses its whole character stream, so source '<span>dnoceS</span> tsriF' paints
        // "First Second" — the reversed leaf texts land in reversed box order.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<p id='p' style='direction:rtl;unicode-bidi:bidi-override'>" +
            "<span id='s'>dnoceS</span> tsriF</p></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var s = FindNode(page.Root, n => n.Id == "s")!;
        // The span's fragment is the reversed leaf text and it paints AFTER the reversed text
        // node's "First " run — not at the line's left edge, where the span sits in source order.
        True(s.InlineFragments is { Count: > 0 } && s.InlineFragments[^1].Text == "Second",
            $"the override should reverse the span's leaf text to 'Second', got '{s.InlineFragments?.LastOrDefault().Text}'");
        True(s.Box.ContentBox.Left > 0f,
            $"the reversed span should paint after the 'First ' run, got left={s.Box.ContentBox.Left}");
    }

    [Test]
    public static void RtlOverride_OnInline_ReversesItsOwnContentOnly()
    {
        // bidi-box-model-006: the override sits on the SPAN, so 'First ' keeps its logical order
        // and position while the span's own 'dnoceS' paints as 'Second' right after it.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<p id='p'>First <span id='s' style='direction:rtl;unicode-bidi:bidi-override'>dnoceS</span></p></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var s = FindNode(page.Root, n => n.Id == "s")!;
        True(s.InlineFragments is { Count: > 0 } && s.InlineFragments[^1].Text == "Second",
            $"the span's own override should reverse 'dnoceS' to 'Second', got '{s.InlineFragments?.LastOrDefault().Text}'");
        // 'First ' (5 glyphs + the collapsing space) occupies the line start; the span follows it.
        True(s.Box.ContentBox.Left > 0f,
            $"the span should still paint after 'First ', got left={s.Box.ContentBox.Left}");
    }

    [Test]
    public static void RtlEmbed_KeepsLatinInLogicalOrder()
    {
        // bidi-box-model-012: 'unicode-bidi: embed' + rtl boxes strong-LTR content at an even
        // level — the text keeps its logical visual order; nothing about the run itself moves.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<p>First <span id='s' style='direction:rtl;unicode-bidi:embed'>dnoceS</span></p></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var s = FindNode(page.Root, n => n.Id == "s")!;
        True(s.InlineFragments is { Count: > 0 } && s.InlineFragments[^1].Text == "dnoceS",
            $"an embed must not reverse strong-LTR content, got '{s.InlineFragments?.LastOrDefault().Text}'");
    }

    [Test]
    public static void OverrideOnOuterBlock_DoesNotReachIntoInnerBlock()
    {
        // UAX #9 X8: each block container starts its own bidi paragraph, so an override on the
        // outer block reverses the child sequence between blocks but not an inner block's text.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='direction:rtl;unicode-bidi:bidi-override'>" +
            "<div id='inner'><span id='s'>abc</span></div></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var s = FindNode(page.Root, n => n.Id == "s")!;
        True(s.InlineFragments is { Count: > 0 } && s.InlineFragments[^1].Text == "abc",
            $"an inner block's content keeps its own paragraph direction, got '{s.InlineFragments?.LastOrDefault().Text}'");
    }
}
