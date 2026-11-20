using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>The paragraph-level bidi reordering (CSS 2.1 §16.2 / UAX #9 subset): the
/// algorithm Bidi.Reorder ports the reviewed char-order derivation
/// (scripts/gen_css21_bidi_expectations.py) rule for rule, and the inline-run composition
/// reorders items so the painted glyph order equals the reviewed painted-order needles.
/// These pins guard the algorithm and the two composition sites (inline runs, block-leaf
/// text) against regressions in either direction.</summary>
public static class BidiUbaTests
{
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var child in node.Children)
            if (Find(child, tag) is { } hit) return hit;
        return null;
    }

    [Test]
    public static void Reorder_BaseLtr_ArabicIslandReverses()
    {
        // direction-unicode-bidi-009's shape: an Arabic island inside LTR text reverses; the
        // latin words keep their order.
        var (_, visual) = Bidi.Reorder("W3C \u0646\u0634\u0627\u0637 \u0627\u0644\u062a\u062f\u0648\u064a\u0644 i18n activity!", baseLtr: true, []);
        Equal("W3C \u0644\u064a\u0648\u062f\u062a\u0644\u0627 \u0637\u0627\u0634\u0646 i18n activity!", Normalize(visual));
    }

    [Test]
    public static void Reorder_BaseRtl_FullReversalRestoresLtrIslands()
    {
        // direction-unicode-bidi-006's shape (the reference image's box order): the whole line
        // reverses, the LTR islands read logical again inside the mirrored line.
        var (_, visual) = Bidi.Reorder(
            "\u0646\u0634\u0627\u0637 \u0627\u0644\u062a\u062f\u0648\u064a\u0644 \"i18n activity, \u0627\u062e\u062a\u0628\u0627\u0631\"!",
            baseLtr: false, []);
        Equal("!\"\u0631\u0627\u0628\u062a\u062e\u0627 ,i18n activity\" \u0644\u064a\u0648\u062f\u062a\u0644\u0627 \u0637\u0627\u0634\u0646", Normalize(visual));
    }

    [Test]
    public static void Reorder_LtrEmbedSpan_IslandStaysLogical()
    {
        // direction-unicode-bidi-005's shape: an LTR embed under an RTL paragraph keeps its
        // LTR content logical; the surrounding RTL text and the trailing neutrals reverse.
        var (_, visual) = Bidi.Reorder(
            "\u0646\u0634\u0627\u0637 \u0627\u0644\u062a\u062f\u0648\u064a\u0644 \"i18n activity, \u0627\u062e\u062a\u0628\u0627\u0631\"!",
            baseLtr: false, [new Bidi.Span(14, 21, Rtl: false, Override: false)]);
        Equal("!\"i18n activity, \u0631\u0627\u0628\u062a\u062e\u0627\" \u0644\u064a\u0648\u062f\u062a\u0644\u0627 \u0637\u0627\u0634\u0646", Normalize(visual));
    }

    [Test]
    public static void Reorder_LtrOverrideSpan_ContentStaysLogical()
    {
        // direction-unicode-bidi-013's shape: an LTR bidi-override forces its whole content
        // to LTR — the Arabic inside paints in LOGICAL order, not reversed.
        var (_, visual) = Bidi.Reorder(
            "i18n activity \"\u0646\u0634\u0627\u0637 \u0627\u0644\u062a\u062f\u0648\u064a\u0644\u060c W3C\"!",
            baseLtr: true, [new Bidi.Span(14, 17, Rtl: false, Override: true)]);
        Equal("i18n activity \"\u0646\u0634\u0627\u0637 \u0627\u0644\u062a\u062f\u0648\u064a\u0644\u060c W3C\"!", Normalize(visual));
    }

    [Test]
    public static void Reorder_RtlOverrideSpan_WholeStreamReverses()
    {
        // An RTL bidi-override reverses everything inside the scope, latin included — the
        // legacy whole-stream reversal's semantics the paragraph pass must reproduce.
        var (_, visual) = Bidi.Reorder("abc \u0627\u0628\u062c", baseLtr: false,
            [new Bidi.Span(0, 7, Rtl: true, Override: true)]);
        Equal("\u062c\u0628\u0627 cba", Normalize(visual));
    }

    [Test]
    public static void BlockLeaf_TextReordersForPainting()
    {
        // A text-only block's own content is one bidi paragraph: the block-leaf paint path
        // reads the visual order off BidiVisualBlockText.
        const string doc = """
<html><head><style>
div { direction: rtl; font-size: 28px; }
</style></head>
<body><div>W3C &#x0646;&#x0634;&#x0627;&#x0637; i18n!</div></body></html>
""";
        using var server = new ScratchServer(doc);
        using var session = new BrowserSession();
        var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/leaf.htm"), 800, 600, session);
        BoxEngine.Layout(page.Root, 800, 600);
        var div = Find(page.Root, "DIV");
        True(div != null, "expected the div in the layout tree");
        True(div!.Box.ContentBox.Width > 100, "the block must be laid out before the visual read");
        using var font = TextMeasure.CreateFont(div);
        var visual = div.BidiVisualBlockText(div.Box.ContentBox.Width, font);
        // Base RTL: the whole line reverses, the latin islands stay logical (the reference
        // derivation's output for this input).
        Equal("!i18n \u0637\u0627\u0634\u0646 W3C", Normalize(visual));
        // The painter paints exactly this string (the judge's painted-order evidence).
        True(div.RenderedText().Contains('\u0646'), "RenderedText stays logical for layout");
    }

    [Test]
    public static void Reorder_ExplicitRloPdfIsAnEmbeddingAndRendersNothing()
    {
        // §16.2/UAX #9 X2-X5/X7/X9: an explicit RLO opens an RTL override level, PDF closes
        // it, and both are removed from the rendering (zero width, no glyph).
        var (levels, visual) = Bidi.Reorder("ab\u202Ecd\u202Cef", baseLtr: true, []);
        True(visual == "abdcef",
            $"expected abdcef, got {string.Join(",", visual.Select(c => ((int)c).ToString("X")))} / levels " +
            $"{string.Join(",", levels)}");
        Equal(0, levels[0]);
        Equal(1, levels[3]);
        Equal(1, levels[4]);
        Equal(0, levels[6]);
        True(!visual.Contains('\u202E') && !visual.Contains('\u202C'),
            "the explicit controls must not paint: " + string.Join(",", visual.Select(c => ((int)c).ToString("X"))));
        Equal("ab cdef", Bidi.WithoutExplicitFormatting("ab c\u202Ede\u202Cf"));
    }

    [Test]
    public static void InlineRun_ExplicitRloSpansItemsAndOmitsControlWidth()
    {
        // bidi-005..011's shape: RLO/PDF inside one inline scope reorder characters across the
        // item boundaries, and the controls reserve no advance width (Ahem would charge 20px
        // each, the red gaps line-breaking-bidi-001 exposed).
        Lite.Layout.FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div id='d' style='font:20px/1 Ahem;width:400px'>AA<span id='s'>\u202ECD</span>\u202CEF</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var span = Find(page.Root, "SPAN")!;
        var div = Find(page.Root, "DIV")!;
        True(span.InlineFragments is { Count: > 0 } && span.InlineFragments[^1].Text == "DC",
            $"the RLO must reverse the span's 'CD' to 'DC', got '{span.InlineFragments?.LastOrDefault().Text}'");
        True(Math.Abs(span.Box.ContentBox.Left - 40f) < 0.5f,
            $"the span starts after 'AA' (40px); the removed controls take no width, got {span.Box.ContentBox.Left}");
        // 'EF' follows with no control advance: its fragment ends at 6 glyphs = 120px.
        LayoutNode? ef = null;
        void Walk(LayoutNode n)
        {
            if (n.DisplayText.EndsWith("EF", StringComparison.Ordinal)) ef = n;
            foreach (var c in n.Children) Walk(c);
        }
        Walk(page.Root);
        True(ef?.InlineFragments is { Count: > 0 } frags && Math.Abs(frags[^1].Rect.Right - 120f) < 0.5f,
            $"six rendered glyphs end at 120px at 20px Ahem (the two controls add nothing), got {ef?.InlineFragments?.LastOrDefault().Rect.Right}");
    }

    [Test]
    public static void InlineRun_EmptyInlineBoxDoesNotSplitTheL2Run()
    {
        // CSS2 text/bidi-span-001: "Inline boxes should not affect bidi reordering" — the empty
        // span between two Hebrew letters is a zero-width marker, so the two letters stay one
        // level-1 run and reverse together.
        Lite.Layout.FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='font:20px/1 Ahem'>\u05D0<span></span>\u05D1</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        float LeftOf(string text)
        {
            float left = -1f;
            void Walk(LayoutNode n)
            {
                foreach (var (rect, fragText) in n.InlineFragments ?? [])
                    if (fragText.Contains(text)) left = rect.Left;
                foreach (var c in n.Children) Walk(c);
            }
            Walk(page.Root);
            return left;
        }
        var alef = LeftOf("\u05D0");
        var bet = LeftOf("\u05D1");
        True(alef >= 0 && bet >= 0 && bet < alef,
            $"the empty span must not split the run: bet should paint left of alef, got bet={bet}, alef={alef}");
    }

    [Test]
    public static void InlineRun_EdgeLessRtlOverrideSpanOwnsMixedContent()
    {
        // direction-unicode-bidi-019's shape: a bidi-override span inside an RTL paragraph whose
        // content mixes Latin and Arabic. The paragraph-level pass owns the edge-less scope, so
        // the span's whole stream renders in reverse order and the surrounding RTL text reorders
        // around it (the legacy whole-stream reversal could only handle strong-LTR content).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div id='d' style='direction:rtl;font-size:20px'>" +
            "\u0646\u0634\u0627\u0637 \u0627\u0644\u062a\u062f\u0648\u064a\u0644 \"<span id='s' style='unicode-bidi:bidi-override'>" +
            "i18n activity, \u0627\u062e\u062a\u0628\u0627\u0631</span>\"!</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        LayoutNode? span = null;
        void Walk(LayoutNode n)
        {
            if (n.Id == "s") span = n;
            foreach (var c in n.Children) Walk(c);
        }
        Walk(page.Root);
        True(span?.InlineFragments is { Count: > 0 } fragments,
            "the override span must paint a fragment");
        var text = string.Concat(span!.InlineFragments!.Select(f => f.Text));
        Equal("\u0631\u0627\u0628\u062a\u062e\u0627 ,ytivitca n81i", text);
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private sealed class ScratchServer : IDisposable
    {
        private readonly WebApplication _app;
        internal string BaseUrl => _app.Urls.Single();

        internal ScratchServer(string body)
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddRoutingCore();
            _app = builder.Build();
            _app.Run(async ctx =>
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(body);
            });
            _app.Start();
        }

        public void Dispose() => _app.StopAsync().GetAwaiter().GetResult();
    }
}
