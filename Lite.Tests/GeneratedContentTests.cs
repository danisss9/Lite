using Lite.Extensions;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §12.1 generated boxes: a ::before/::after is an inline box of its own, so the
/// non-inherited properties of the originating element (here a table cell's UA
/// 'vertical-align: middle') must not leak into it (§10.8.1: the initial value is 'baseline').</summary>
public static class GeneratedContentTests
{
    [Test]
    public static void BeforeInATableCell_IsBaselineAlignedNotMiddle()
    {
        // content-037: the green "PASS PASS" of `td:before` sat 2px below the reference cell's
        // text because the pseudo read the cell's 'middle' as its own vertical-align.
        var (pseudo, cell) = BeforeInCell("");
        True(pseudo.GetVerticalAlign() == VerticalAlignType.Baseline,
            $"pseudo aligns {pseudo.GetVerticalAlign()}");
        True(cell.Box.ContentBox.Height < 18f,
            $"the cell's line box grew to {cell.Box.ContentBox.Height}");
    }

    [Test]
    public static void BeforeDeclaringVerticalAlign_KeepsItsOwnValue()
    {
        // The default only applies when the pseudo rule says nothing: an authored
        // 'vertical-align' on ::before still wins (§12.1: the pseudo-element's own declarations).
        var (pseudo, _) = BeforeInCell("vertical-align: top;");
        True(pseudo.GetVerticalAlign() == VerticalAlignType.Top,
            $"pseudo aligns {pseudo.GetVerticalAlign()}");
    }

    [Test]
    public static void InsideMarker_IsTheFirstInlineChildOfTheItem()
    {
        // §12.5.1: for 'list-style-position: inside' the marker box is the first inline box of
        // the principal block box — before ::before and before the content.
        var item = Find(Parse("<ol><li>one</li></ol>", "li { list-style-position: inside; }"), "LI")!;
        True(item is not null, "no LI");
        True(item.Children.Count > 0 && item.Children[0].TagName == "#pseudo-marker",
            string.Join(",", item.Children.Select(c => c.TagName)));
        True(item.Children[0].DisplayText.StartsWith("1. "), item.Children[0].DisplayText);
    }

    [Test]
    public static void EmptyOutsideListItem_HasItsMarkerLineBox()
    {
        // An empty list item is one marker line tall (§12.5.1: the marker box is an inline box of
        // the item), so its outside marker lands where the reference puts it — an empty <li> is
        // not a zero-height box (list-style-applies-to-012/014).
        var item = Find(Parse("<div></div>", "div { display: list-item; list-style: square; }"), "DIV")!;
        True(item is not null, "no DIV");
        True(item.Children.Any(c => c.TagName == "#pseudo-marker"), "marker child missing");
        True(item.Box.ContentBox.Height > 10f, $"item height {item.Box.ContentBox.Height}");
    }

    [Test]
    public static void FirstLetter_DoesNotConsumeTheListMarker()
    {
        // §12.5.1 puts the marker before the content, so `li::first-letter` styles the item's own
        // first letter, not the bullet.
        var item = Find(Parse("<ol><li>alfa</li></ol>",
            "li { list-style-position: inside; } li:first-letter { color: red; }"), "LI")!;
        var letter = Find(item, "#pseudo-first-letter");
        True(letter is not null, "no ::first-letter box");
        Equal("a", letter!.DisplayText);
    }

    private static LayoutNode Parse(string body, string css)
    {
        var page = Parser.ParseChildPage(
            $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        Lite.Layout.BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var child in node.Children)
            if (Find(child, tag) is { } found) return found;
        return null;
    }

    private static (LayoutNode Pseudo, LayoutNode Cell) BeforeInCell(string pseudoDeclarations)
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>td { vertical-align: middle; } " +
            $"td:before {{ content: 'x'; {pseudoDeclarations} }}</style></head>" +
            "<body><table><tr><td></td></tr></table></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        LayoutNode? cell = null, pseudo = null;
        void Walk(LayoutNode node)
        {
            if (node.TagName == "TD") cell = node;
            if (node.TagName == "#pseudo-before") pseudo = node;
            foreach (var child in node.Children) Walk(child);
        }
        Walk(page.Root);
        True(cell is not null && pseudo is not null, "table cell or ::before missing");
        return (pseudo!, cell!);
    }
}
