using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §10.6.6 / §10.8.1 inline-block heights and baselines.</summary>
public static class InlineBlockTests
{
    private static LayoutNode? FindId(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
            if (FindId(c, id) is { } found) return found;
        return null;
    }

    [Test]
    public static void ZeroHeightInlineBlock_BottomAlignsWithAnEmptySibling()
    {
        // border-top-width-006: 'height: 0' plus a 96px top border and an empty 96px-tall box
        // both put their bottom margin edge on the baseline, so their tops line up.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>body{margin:0} div{display:inline-block;width:50px}" +
            " #a{border-top:96px solid black;height:0} #b{height:96px;background:black}</style>" +
            "<div id=\"a\"></div><div id=\"b\"></div>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var a = FindId(page.Root, "a");
        var b = FindId(page.Root, "b");
        True(a is not null && b is not null, "nodes missing");
        True(a!.Box.BorderBox.Height == 96f, $"height:0 box must be 96px (border only), got {a.Box.BorderBox.Height}");
        True(Math.Abs(a.Box.BorderBox.Top - b!.Box.BorderBox.Top) < 0.01f,
            $"tops must align, got {a.Box.BorderBox.Top} vs {b.Box.BorderBox.Top}");
    }

    [Test]
    public static void ShrinkToFit_LeavesRoomForTheBoxsOwnEdges()
    {
        // inline-block-002 at a 450px window: §10.3.9's available width excludes the box's
        // margins, borders and padding, so wrapping text keeps the border box inside the line.
        var text = string.Concat(Enumerable.Repeat("this is an inline-block ", 3));
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>body{margin:0} p{margin:0}" +
            " span{display:inline-block;border:3px solid;margin:8px;padding:8px}</style>" +
            $"<p><span id=\"s\">{text}</span></p>", isSrcdoc: true, "http://test/", 300, 600);
        BoxEngine.Layout(page.Root, 300, 600);
        var s = FindId(page.Root, "s");
        True(s is not null, "node missing");
        True(s!.Box.BorderBox.Right <= 300f - 8f + 0.5f,
            $"the border box must end inside the 300px line minus its 8px margin, got right {s.Box.BorderBox.Right}");
    }

    [Test]
    public static void WrappedBorderedSpan_TakesItsLeftBorderToTheNewLine()
    {
        // inlines-005: bordered <span>X</span> items wrap in a grid; the item opening line 2
        // starts its border box at the line's start, its left border with it (it was left at
        // the end of line 1, so the box hung 3px outside the content edge).
        var spans = string.Concat(Enumerable.Repeat("<span>X</span> ", 40));
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>body{margin:8px} p > span { padding: 0.3em 0; line-height: 3; }" +
            " span span { border: 3px solid blue; }</style><p> <span> " + spans + "</span></p>", isSrcdoc: true, "http://test/", 450, 600);
        BoxEngine.Layout(page.Root, 450, 600);
        var items = new List<SkiaSharp.SKRect>();
        void Walk(LayoutNode n, int depth)
        {
            if (n.TagName == "SPAN" && depth > 0 && n.Children.All(c => c.TagName.StartsWith('#'))) items.Add(n.Box.BorderBox);
            foreach (var c in n.Children) Walk(c, depth + (n.TagName == "SPAN" ? 1 : 0));
        }
        Walk(page.Root, 0);
        var rows = items.GroupBy(r => (int)MathF.Round(r.Top)).OrderBy(g => g.Key).ToList();
        True(rows.Count >= 2, $"the items must wrap, got {rows.Count} line(s)");
        foreach (var row in rows)
        {
            var first = row.Min(r => r.Left);
            True(MathF.Abs(first - 8f) < 0.5f, $"every line must start its first border box at x=8, got {first} at y={row.Key}");
        }
    }

    [Test]
    public static void FirstLineTextTransform_RebreaksOnlyTheFirstLine()
    {
        // first-line-selector-014: 'div:first-line { text-transform: uppercase }' uppercases the
        // first line, broken with the wider uppercase glyphs; the rest wraps untransformed.
        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 16);
        var text = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", 12));
        var lines = TextMeasure.WrapTextWithFirstLine(text, TextTransform.Uppercase, "en", 200f, font, WhiteSpace.Normal, 20f);
        True(lines.Count >= 2, "the text must wrap");
        True(lines[0].Text.Any(char.IsLetter) && !lines[0].Text.Any(char.IsLower), $"line 1 must be uppercase, got '{lines[0].Text}'");
        True(lines[1].Text.Any(char.IsLower), $"line 2 must stay lowercase, got '{lines[1].Text}'");
        True(lines[0].Width <= 200.5f, $"the uppercase first line must fit its width, got {lines[0].Width}");
        var words = string.Join(' ', lines.Select(l => l.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        True(words == 36, $"no word may be lost or repeated, got {words}");
    }

    [Test]
    public static void ListFirstLine_StylesOnlyTheFirstItem()
    {
        // first-line-selector-015: 'ul:first-line' is the first item's first line only.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>ul:first-line{text-transform:uppercase}</style>" +
            "<ul><li id=\"a\">one</li><li id=\"b\">two</li></ul>", isSrcdoc: true, "http://test/", 800, 600);
        var a = FindId(page.Root, "a");
        var b = FindId(page.Root, "b");
        True(a is not null && b is not null, "items missing");
        True(BoxEngine.FirstLineTransform(a!) == TextTransform.Uppercase, "the first item opens the list's first line");
        True(BoxEngine.FirstLineTransform(b!) == TextTransform.None, "the second item is not on the list's first line");
    }

    [Test]
    public static void InsideListMarkerIndentsTheFirstLineOnly()
    {
        // §12.5.1 with list-style-position: inside — the marker box is the first inline box,
        // so line 1 starts past it while line 2 realigns with the content edge. The marker
        // had reserved no inline space at all: it painted over the first letter.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>body{margin:0}" +
            " div{display:list-item;list-style:square inside;font:16px/1.4 serif}" +
            " span{font:16px/1.4 serif}</style>" +
            "<div><span id=\"first\">XXXX</span><br><span id=\"second\">YYYY</span></div>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var first = FindId(page.Root, "first");
        var second = FindId(page.Root, "second");
        True(first is not null && second is not null, "nodes missing");
        var indent = first!.Box.BorderBox.Left - second!.Box.BorderBox.Left;
        True(indent > 8f, $"line 1 must start past the marker (indent {indent}px)");
    }

    [Test]
    public static void InlineLeafKeepsItsTrailingSpaceBeforeAnInlineNeighbour()
    {
        // word-spacing-007: "<span>x </span><span>y</span>" — the space is mid-line, not at a
        // line edge, so §16.6.1 keeps it.
        var page = Parser.ParseChildPage("<!DOCTYPE html><div><span id=\"a\">x </span><span>y</span></div>",
            isSrcdoc: true, "http://test/", 800, 600);
        var a = FindId(page.Root, "a");
        True(a is not null, "span missing");
        True(a!.DisplayText == "x ", $"the boundary space must survive, got '{a.DisplayText}'");
    }

    [Test]
    public static void FirstLetter_DoesNotReachIntoAnInlineBlockChild()
    {
        // first-letter-selector-017: §5.12.2 — an atomic inline ends the first-letter search.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>div:first-letter{color:red} div p{display:inline-block}</style>" +
            "<div><p>Filler</p></div>", isSrcdoc: true, "http://test/", 800, 600);
        bool HasFirstLetter(LayoutNode n) => n.TagName == "#pseudo-first-letter" || n.Children.Any(HasFirstLetter);
        True(!HasFirstLetter(page.Root), "no ::first-letter box may be created inside the inline-block");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
