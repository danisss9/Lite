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
