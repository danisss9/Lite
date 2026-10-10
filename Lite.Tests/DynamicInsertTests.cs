using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Dynamic block-in-inline insertion (CSS 2.1 §9.2.1.1) — the DOM mutation path behind
/// the WPT insert-* reftests. Each case mirrors their shape: a layout is forced first
/// (document.body.offsetHeight), then a block is inserted before a reference node that may live
/// inside content the layout normalizers wrapped in anonymous pieces. The inserted block must
/// land in DOM order — matching either the split or the nosplit reference — never fall off the
/// end of the container.</summary>
public static class DynamicInsertTests
{
    private static LayoutNode? Find(LayoutNode node, Func<LayoutNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } hit) return hit;
        return null;
    }

    private static LayoutNode? FindById(LayoutNode node, string id)
        => Find(node, n => n.Attributes.GetValueOrDefault("id") == id);

    [Test]
    public static void InsertBlock_IntoInlineRun_LandsAtDomPosition()
    {
        // insert-block-in-inlines-middle-001: a forced layout, then insertBefore(block, span#ip)
        // into a container of pure inline content. The block must separate "Several inline
        // elements are" (above) from "in this sentence." (below).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            ".container { font: 20px/1 Ahem; } .inserted { margin: 1em 0; }" +
            "</style></head><body>" +
            "<div class=\"container\" id=\"c\"><span>Several</span> <span>inline elements</span> are " +
            "<span id=\"ip\">in this</span> sentence.</div>" +
            "<script>" +
            "document.body.offsetHeight;" +
            "var b = document.createElement('div');" +
            "b.appendChild(document.createTextNode('Inserted'));" +
            "b.className = 'inserted';" +
            "document.getElementById('c').insertBefore(b, document.getElementById('ip'));" +
            "</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var container = FindById(page.Root, "c");
        True(container != null, "container missing");
        var inserted = Find(container!, n => n.TagName == "DIV" && n.DisplayText.Contains("Inserted"));
        True(inserted != null, "inserted block missing from the container subtree");
        var ip = FindById(container!, "ip");
        True(ip != null, "insertion-point span lost");
        var firstSpan = Find(container!, n => n.TagName == "SPAN" && n.DisplayText.Contains("Several"));
        True(firstSpan != null, "leading span lost");

        // The inserted block sits between the leading inline run and the trailing content.
        True(firstSpan!.Box.BorderBox.Bottom <= inserted!.Box.BorderBox.Top,
            "content before the insertion point must render above the inserted block");
        True(ip!.Box.BorderBox.Top >= inserted.Box.BorderBox.Bottom,
            "content after the insertion point must render below the inserted block");
    }

    [Test]
    public static void InsertBlock_IntoBlocksAndInlines_LandsAfterExistingBlock()
    {
        // insert-block-in-blocks-n-inlines-middle-001: the container already mixes blocks and
        // inlines, so the forced layout has SPLIT the inline into anonymous pieces and the
        // insertion-point span lives inside one. The new block must still land before it.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            ".container { font: 20px/1 Ahem; } .inserted { margin: 1em 0; }" +
            "</style></head><body>" +
            "<div class=\"container\" id=\"c\"><span>Some</span> <span>pertinent</span> " +
            "<div>block</div> <span id=\"ip\">in this</span> sentence.</div>" +
            "<script>" +
            "document.body.offsetHeight;" +
            "var b = document.createElement('div');" +
            "b.appendChild(document.createTextNode('Inserted'));" +
            "b.className = 'inserted';" +
            "document.getElementById('c').insertBefore(b, document.getElementById('ip'));" +
            "</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);

        var container = FindById(page.Root, "c");
        True(container != null, "container missing");
        var existingBlock = Find(container!, n => n.TagName == "DIV" && n.DisplayText.Contains("block")
            && !n.DisplayText.Contains("Inserted"));
        True(existingBlock != null, "pre-existing block lost");
        var inserted = Find(container!, n => n.TagName == "DIV" && n.DisplayText.Contains("Inserted"));
        True(inserted != null, "inserted block missing — it fell out of the container");
        var ip = FindById(container!, "ip");
        True(ip != null, "insertion-point span lost");

        // Container order: leading run, pre-existing block, inserted block, trailing run.
        True(existingBlock!.Box.BorderBox.Bottom <= inserted!.Box.BorderBox.Top,
            "the inserted block must come after the pre-existing block");
        True(ip!.Box.BorderBox.Top >= inserted.Box.BorderBox.Bottom,
            "the insertion-point content must come after the inserted block");
    }

    [Test]
    public static void AppendTextNode_NextToGeneratedBlock_MaterializesAsChild()
    {
        // block-in-inline-insert-016b: an empty inline with a block ::after gains a text node;
        // the text must render as a piece of the split inline ABOVE the generated block — not
        // vanish into the folded own text the splitter cannot see.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#i { border: 2px solid; } #i:after { display: block; content: 'Two'; }" +
            "</style></head><body>" +
            "<span id=\"i\"></span>" +
            "<script>document.getElementById('i').appendChild(document.createTextNode('One'));</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        // The mutation-time structure: the text must sit as a #text child beside the synthetic
        // block, not in the element's own folded text the splitter cannot see.
        var i = FindById(page.Root, "i");
        True(i != null, "span missing");
        True(i!.Children.Any(c => c.TagName == "#text" && c.DisplayText.Contains("One")),
            "appended text must materialize as a #text child beside generated content");
        True(string.IsNullOrEmpty(i.DisplayText), "folded own text must stay empty beside the carrier");
        // After layout the original span is detached (the splitter installs anonymous pieces),
        // so assert positions in the rendered tree.
        BoxEngine.Layout(page.Root, 800, 600);
        var one = Find(page.Root, c => c.TagName == "#text" && c.DisplayText.Contains("One"));
        var pseudo = Find(page.Root, c => c.TagName == "#pseudo-after");
        True(one != null && pseudo != null && pseudo!.Box.BorderBox.Top >= one!.Box.BorderBox.Bottom,
            "the generated block must render below the appended text line");
    }

    [Test]
    public static void InsertBlock_PaintsItsContent()
    {
        // table-anonymous-objects-008's harness shape: a forced layout between mutations, then
        // the final render must PAINT the inserted content, not just lay boxes out.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            ".inserted { color: #fe0000; font: 20px/1 Ahem; }" +
            "</style></head><body>" +
            "<div id=\"c\"><span>before</span></div>" +
            "<script>" +
            "document.body.offsetHeight;" +
            "var b = document.createElement('div');" +
            "b.className = 'inserted';" +
            "b.appendChild(document.createTextNode('Inserted'));" +
            "document.getElementById('c').appendChild(b);" +
            "</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var inserted = FindById(page.Root, "c")!.Children.FirstOrDefault(n => n.TagName == "DIV");
        True(inserted != null, "inserted block missing from container children");
        BoxEngine.Layout(page.Root, 800, 600);
        using var bmp = Drawer.DrawToBitmap(800, 600, page.Root, new Viewport { ViewportHeight = 600 });
        var red = 0;
        foreach (var px in bmp.Pixels)
            if (px.Red == 254 && px.Green == 0 && px.Blue == 0) red++;
        True(red > 0, $"inserted block text must paint (got {red} #fe0000 pixels)");
    }

    [Test]
    public static void AppendTextNode_ToFoldedElement_SyncsOwnText()
    {
        // A div whose content is its own folded text gains a text node: the folded text must
        // grow, not stay at the parse-time value.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div id=\"t\">hello</div>" +
            "<script>" +
            "document.getElementById('t').appendChild(document.createTextNode(' world'));" +
            "</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var div = FindById(page.Root, "t");
        True(div != null, "div missing");
        Equal("hello world", div!.DisplayText);
        BoxEngine.Layout(page.Root, 800, 600);
        True(div.Box.ContentBox.Width > 0, "div must lay out content");
    }

    [Test]
    public static void TextContentSetter_ReplacesElementChildrenAndKeepsPseudos()
    {
        // textContent replaces all DOM children with one text node; the LayoutNode children must
        // follow (old element boxes must not linger), and parse-time ::before content survives.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#t::before { content: 'B '; }" +
            "</style></head><body>" +
            "<div id=\"t\">hello <span id=\"inner\">inner</span></div>" +
            "<script>document.getElementById('t').textContent = 'replaced';</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var div = FindById(page.Root, "t");
        True(div != null, "div missing");
        True(!div!.Children.Any(c => c.TagName == "SPAN"),
            "replaced element children must not linger in the layout tree");
        True(FindById(page.Root, "inner") is null || FindById(page.Root, "inner")!.Parent != div,
            "the removed span must no longer be a layout child");
        True(div.Children.Any(c => c.TagName == "#pseudo-before"),
            "parse-time ::before content must survive the replacement");
        // The text reaches the painter through a #text carrier (the mixed representation) or
        // the element's own folded text — either way 'replaced' must be the visible content,
        // and the element's own text must not duplicate the carrier's.
        var ownText = div!.DisplayText;
        var carrierText = string.Concat(div.Children.Where(c => c.TagName == "#text").Select(c => c.DisplayText));
        True(ownText.Contains("replaced") || carrierText.Contains("replaced"),
            $"text must update, got own='{ownText}' carrier='{carrierText}'");
        True(!(ownText.Contains("replaced") && carrierText.Contains("replaced")),
            "own text and the #text carrier must not double-paint the replacement");
    }

    [Test]
    public static void RemoveChild_DetachesFromLayoutAndDom()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body>" +
            "<div id=\"c\"><span id=\"a\">a</span><span id=\"b\">b</span></div>" +
            "<script>document.getElementById('c').removeChild(document.getElementById('a'));</script>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var container = FindById(page.Root, "c");
        True(container != null, "container missing");
        True(!container!.Children.Any(n => n.Attributes.GetValueOrDefault("id") == "a"),
            "removed span must not remain a layout child");
        var b = FindById(container, "b");
        True(b != null && b!.Parent == container, "remaining span must stay in place");
    }
}
