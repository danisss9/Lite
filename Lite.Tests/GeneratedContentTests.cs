using Lite;
using Lite.Extensions;
using Lite.Layout;
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

    [Test]
    public static void CellText_IsBaselineAlignedNextToASpan()
    {
        // §17.5.4: the cell's 'middle' aligns its content in the row, it is not an inline value
        // for the cell's anonymous text. The reference of first-letter-selector-007 puts a span
        // beside the cell's text; both must share the baseline.
        var cell = Find(Parse("<table><tr><td><span>F</span>iller Text</td></tr></table>",
            "td { vertical-align: middle; }"), "TD")!;
        var text = Find(cell, "#text")!;
        True(text.GetVerticalAlign() == VerticalAlignType.Baseline,
            $"cell text aligns {text.GetVerticalAlign()}");
    }

    [Test]
    public static void FirstLetterTail_IsBaselineAlignedInATableCell()
    {
        // The split-off tail is an inline box of its own, so the cell's 'middle' must not reach
        // it either (first-letter-selector-007).
        var cell = Find(Parse("<table><tr><td>Filler Text</td></tr></table>",
            "td { vertical-align: middle; } td:first-letter { color: green; }"), "TD")!;
        var letter = Find(cell, "#pseudo-first-letter");
        var tail = Find(cell, "#text");
        True(letter is not null && tail is not null, "first-letter split missing");
        True(tail!.GetVerticalAlign() == VerticalAlignType.Baseline,
            $"tail aligns {tail.GetVerticalAlign()}");
    }

    [Test]
    public static void ContentUrl_PaintsTheGeneratedImage()
    {
        // §12.2: 'content: url(...)' makes the pseudo box a REPLACED element, so its bitmap must
        // paint through the image path (content-004's green square). The parser fetch is covered
        // by the conformance case; this pins the paint path by handing the box a bitmap.
        var root = Parse("<div></div>", "div:before { content: ''; }");
        var pseudo = Find(root, "#pseudo-before");
        True(pseudo is not null, "the pseudo box is missing");
        using var image = new SkiaSharp.SKBitmap(4, 4);
        image.Erase(new SkiaSharp.SKColor(0, 128, 0));
        pseudo!.Image = image;
        Lite.Layout.BoxEngine.Layout(root, 40, 40);
        using var bitmap = Drawer.DrawToBitmap(40, 40, root, new Viewport { ViewportHeight = 40 });
        var painted = false;
        for (var y = 0; y < 40 && !painted; y++)
        for (var x = 0; x < 40 && !painted; x++)
            if (bitmap.GetPixel(x, y) != SkiaSharp.SKColors.White) painted = true;
        True(painted, "the content image painted nothing");
    }

    [Test]
    public static void GeneratedBox_InheritsAPresentationalHint()
    {
        // content-145: the body's HTML text="green" hint is an inherited colour; the ::before
        // shares the Style object, not the hint override, so it must be copied explicitly.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>body:before { content: 'x' }</style></head>" +
            "<body text=\"green\">text</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var pseudo = Find(page.Root, "#pseudo-before")!;
        True(pseudo.GetColor() == new SkiaSharp.SKColor(0, 128, 0), pseudo.GetColor().ToString());
    }

    [Test]
    public static void Attr_FindsAnHtmlAttributeCaseInsensitively()
    {
        // content-attr-case-001: HTML attribute names are ASCII case-insensitive, so
        // `content: attr(Title)` reads the `title` attribute.
        var pseudo = Find(Parse("<div title=\"PASS\"></div>", "div:before { content: attr(Title); }"),
            "#pseudo-before");
        True(pseudo is not null, "no ::before");
        Equal("PASS", pseudo!.DisplayText);
    }

    [Test]
    public static void PseudoInherit_TakesTheHostsComputedValue()
    {
        // §6.2.1: 'inherit' on a generated box is the originating element's computed value.
        var root = Parse("<div>text</div>",
            "div { white-space: pre } div:before { content: 'a'; white-space: inherit }");
        var pseudo = Find(root, "#pseudo-before")!;
        True(pseudo.GetWhiteSpace() == WhiteSpace.Pre, pseudo.GetWhiteSpace().ToString());

        // first-letter-inherit-001: `div:first-letter { float: inherit }` floats the letter.
        var letter = Find(Parse("<div>text</div>", "div { float: left } div:first-letter { float: inherit }"),
            "#pseudo-first-letter")!;
        True(letter.GetFloat() == FloatType.Left, letter.GetFloat().ToString());
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
