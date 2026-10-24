using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §12.4 counters: document order across ::before, the children and ::after,
/// counter properties on pseudo-elements, implicit resets, and non-displayed boxes.</summary>
public static class CounterTests
{
    [Test]
    public static void PseudoElementIncrement_AppliesBeforeItsContent()
    {
        // content-011: an unreset counter incremented by the ::before that shows it reads 1.
        var texts = Generated("div:before { counter-increment: test; content: counter(test, lower-roman); }",
            "<div></div>");
        True(texts.SequenceEqual(["i"]), string.Join(",", texts));
    }

    [Test]
    public static void AfterReadsCountersAfterTheChildren()
    {
        var texts = Generated(
            "#c { counter-reset: n; } p { counter-increment: n; } #c:before, #c:after { content: counter(n); }",
            "<div id=\"c\"><p>a</p><p>b</p><p>c</p></div>");
        True(texts.SequenceEqual(["0", "3"]), string.Join(",", texts));
    }

    [Test]
    public static void ExplicitZeroIncrement_LeavesTheCounterUnchanged()
    {
        var texts = Generated("body { counter-reset: n 4; } p { counter-increment: n 0; } p:before { content: counter(n); }",
            "<p>a</p>");
        True(texts.SequenceEqual(["4"]), string.Join(",", texts));
    }

    [Test]
    public static void UndisplayedPseudoElement_CannotChangeCounters()
    {
        // counter-increment-not-generated-001 / counter-reset-not-generated-001.
        var texts = Generated(
            "#div1:before { counter-increment: c 10; display: none; } div:before { content: counter(c); }",
            "<div id=\"div1\"><div></div></div>");
        True(texts.SequenceEqual(["0"]), string.Join(",", texts));
    }

    [Test]
    public static void SiblingReset_ScopesOverFollowingSiblingsAndIsReplacedByTheNext()
    {
        // content-counter-007 / §12.4.1: a reset on a span covers its following siblings; a later
        // sibling's reset replaces that instance (counters() shows no extra nesting level).
        var texts = Generated(
            "#t { counter-reset: c; } #t span { counter-increment: c; } #t span:before { content: counters(c, '.'); }",
            "<div id=\"t\"><span></span><span style=\"counter-reset: c 48\"></span><span></span>" +
            "<span style=\"counter-reset: c 388\"></span><span></span></div>");
        True(texts.SequenceEqual(["1", "1.49", "1.50", "1.389", "1.390"]), string.Join(",", texts));
    }

    [Test]
    public static void Quotes_NestInDocumentOrderAndHonourTheQuotesProperty()
    {
        // §12.3.2 (content-157): no-open-quote nests silently, so the close-quote after it emits
        // the level-1 close; a close-quote at depth 0 emits nothing; open-quote takes the pair for
        // its depth from 'quotes' (the last pair beyond the list).
        var texts = Generated(
            "div { quotes: '<' '>' '[' ']'; } #a:before { content: no-open-quote; } #a:after { content: close-quote; }" +
            "#b:before { content: close-quote 'x'; } #c:before { content: open-quote; } #c:after { content: close-quote; }" +
            "#d:before { content: open-quote; } #d:after { content: close-quote; }",
            "<div id=\"a\"></div><div id=\"b\"></div><div id=\"c\"><div id=\"d\"></div></div>");
        True(texts.SequenceEqual(["", ">", "x", "<", "[", "]", ">"]), string.Join(",", texts));
    }

    private static List<string> Generated(string css, string body)
    {
        var page = Parser.ParseChildPage($"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var texts = new List<string>();
        void Walk(LayoutNode node)
        {
            if (node.TagName is "#pseudo-before" or "#pseudo-after" && node.GetDisplayKeyword() != "none")
                texts.Add(node.DisplayText);
            foreach (var child in node.Children) Walk(child);
        }
        Walk(page.Root);
        return texts;
    }

    private static string GetDisplayKeyword(this LayoutNode node) =>
        node.StyleOverrides.TryGetValue("display", out var display) ? display.Trim().ToLowerInvariant() : "";
}
