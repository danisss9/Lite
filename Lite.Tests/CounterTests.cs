using Lite.Layout;
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

    [Test]
    public static void ListItems_ImplicitlyIncrementTheListItemCounter()
    {
        // §12.4: a list element implicitly resets 'list-item' and every list item increments it.
        var texts = Generated("li:before { content: counter(list-item); }",
            "<ol><li></li><li></li><li></li></ol>");
        True(texts.SequenceEqual(["1", "2", "3"]), string.Join(",", texts));
    }

    [Test]
    public static void AuthorResetOfListItem_ParticipatesInTheOrdinal()
    {
        // counter-reset-increment-002: reset -4, then the item's own implicit increment → -3.
        var texts = Generated("ol { counter-reset: list-item -4; } li:before { content: counter(list-item); }",
            "<ol><li></li><li></li></ol>");
        True(texts.SequenceEqual(["-3", "-2"]), string.Join(",", texts));
    }

    [Test]
    public static void Markers_UseTheListItemCounter()
    {
        // list-style-type-017: `start="-3"` sets the first ordinal, and the marker follows it.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>ol { counter-reset: list-item -3; " +
            "list-style-position: inside; }</style></head>" +
            "<body><ol start=\"-3\"><li>a</li><li>b</li></ol></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        Lite.Layout.BoxEngine.Layout(page.Root, 800, 600);
        var markers = new List<string>();
        void Walk(LayoutNode node)
        {
            if (node.TagName == "#pseudo-marker") markers.Add(node.DisplayText.Trim());
            foreach (var child in node.Children) Walk(child);
        }
        Walk(page.Root);
        True(markers.SequenceEqual(["-3.", "-2."]), string.Join("|", markers));
    }

    [Test]
    public static void NestedLists_RestartTheListItemCounter()
    {
        var texts = Generated("li:before { content: counter(list-item); }",
            "<ol><li><ol><li></li></ol></li><li></li></ol>");
        True(texts.SequenceEqual(["1", "1", "2"]), string.Join(",", texts));
    }

    [Test]
    public static void OrderedListIndex_FollowsReversedStartValueAndDynamicChanges()
    {
        // HTML §4.4.6: 'start' seeds the first ordinal, 'reversed' counts down (from the item
        // count when 'start' is absent), and an 'li value' sets that item and continues from
        // it. The index is read from the current list so script changes renumber at once.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><body>" +
            "<ol reversed start=5><li id=a>A</li><li id=b>B</li><li id=c>C</li></ol>" +
            "<ol><li id=d value=5>D</li><li id=e>E</li><li id=f value=2>F</li><li id=g>G</li></ol>" +
            "<ol reversed><li id=h>H</li><div><li id=i>I</li><li id=j>J</li></div></ol>" +
            "<ol id=list><li id=k>K</li><li id=l>L</li></ol>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        LayoutNode Find(string id)
        {
            LayoutNode? Found(LayoutNode n) => n.Id == id ? n :
                n.Children.Select(Found).FirstOrDefault(r => r is not null);
            return Found(page.Root)!;
        }
        Equal(5, CssCounterStyles.ListItemIndex(Find("a")));
        Equal(4, CssCounterStyles.ListItemIndex(Find("b")));
        Equal(3, CssCounterStyles.ListItemIndex(Find("c")));
        Equal(5, CssCounterStyles.ListItemIndex(Find("d")));
        Equal(6, CssCounterStyles.ListItemIndex(Find("e")));
        Equal(2, CssCounterStyles.ListItemIndex(Find("f")));
        Equal(3, CssCounterStyles.ListItemIndex(Find("g")));
        // A wrapper between the list and its items does not start a new list (reversed-1d).
        Equal(3, CssCounterStyles.ListItemIndex(Find("h")));
        Equal(2, CssCounterStyles.ListItemIndex(Find("i")));
        Equal(1, CssCounterStyles.ListItemIndex(Find("j")));
        // Script-set 'reversed' and an appended item renumber the existing markers.
        page.Engine!.RawEngine.Execute(
            "document.getElementById('list').setAttribute('reversed',''); " +
            "var li=document.createElement('li'); li.id='m'; li.textContent='M'; " +
            "document.getElementById('list').appendChild(li);");
        Equal(3, CssCounterStyles.ListItemIndex(Find("k")));
        Equal(2, CssCounterStyles.ListItemIndex(Find("l")));
        Equal(1, CssCounterStyles.ListItemIndex(Find("m")));
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
