using AngleSharp.Dom;
using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Parse-fidelity probes over the script-visible DOM: the doctype's identity limbs
/// and the option value fallback that must stay IDL-only (never an authored attribute). Each
/// case mirrors a html5lib tree-dump shape that the parse-fidelity corpus asserts.</summary>
public static class ParseFidelityTests
{
    private static LayoutNode? Find(LayoutNode node, Func<LayoutNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } hit) return hit;
        return null;
    }

    private static string Probe(string script)
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body><div id=\"host\"></div>" +
            "<script>document.getElementById('host').textContent = String(" + script + ");</script></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var host = Find(page.Root, n => n.Attributes.GetValueOrDefault("id") == "host");
        True(host != null, "host div missing");
        return host!.DisplayText ?? string.Empty;
    }

    [Test]
    public static void Doctype_ExposesNameNodeTypeAndEmptyIds()
    {
        Equal("html|10||", Probe(
            "document.doctype.name + '|' + document.doctype.nodeType + '|' + document.doctype.publicId + '|' + document.doctype.systemId"));
    }

    [Test]
    public static void Doctype_FirstChildOfDocumentCarriesName()
    {
        Equal("html|10", Probe(
            "document.childNodes[0].name + '|' + document.childNodes[0].nodeType"));
    }

    [Test]
    public static void Doctype_PublicAndSystemIdsRoundTrip()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"http://www.w3.org/TR/html4/strict.dtd\">" +
            "<html><head></head><body><div id=\"host\"></div>" +
            "<script>document.getElementById('host').textContent = document.doctype.name + '|' +" +
            "document.doctype.publicId + '|' + document.doctype.systemId;</script></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var host = Find(page.Root, n => n.Attributes.GetValueOrDefault("id") == "host");
        True(host != null, "host div missing");
        Equal("html|-//W3C//DTD HTML 4.01//EN|http://www.w3.org/TR/html4/strict.dtd", host!.DisplayText);
    }

    [Test]
    public static void BareOption_SerializesWithoutAValueAttribute()
    {
        // html5lib (innerHTML in select): "<select><option>" must serialize the option with
        // no attributes — the text fallback is IDL behavior, not an authored attribute.
        Equal("<option>x</option>", Probe(
            "(function(){ var s = document.createElement('select');" +
            "s.innerHTML = '<option>x</option>'; return s.innerHTML; })()"));
    }

    [Test]
    public static void OptionValue_IdlFallsBackToTextAndMirrorsTheAttribute()
    {
        Equal("x|y", Probe(
            "(function(){ var s = document.createElement('select');" +
            "s.innerHTML = '<option>x</option><option value=\"y\">z</option>';" +
            "return s.options[0].value + '|' + s.options[1].value; })()"));
    }

    [Test]
    public static void EngineOriginSheets_AreHiddenFromHeadChildNodes()
    {
        // The parser injects the UA (and user) origin sheets into the document for cascade
        // origins; script-visible child lists must not include them (HTML §4.2.2: UA sheets
        // are not author content). An author <style> stays visible.
        Equal("0|1", Probe(
            "(function(){ var bare = document.createElement('div');" +
            "var a = document.createElement('div');" +
            "a.innerHTML = '<style>.x { color: red; }</style>'; " +
            "return document.head.childNodes.length + '|' + a.childNodes.length; })()"));
    }

    [Test]
    public static void EngineOriginSheets_FirstAndLastChild_ReflectVisibility()
    {
        // With no author styles the head's visible child list is empty; firstChild/lastChild
        // agree with childNodes.
        Equal("null|null|0", Probe(
            "document.head.firstChild + '|' + document.head.lastChild + '|' + document.head.children.length"));
    }

    [Test]
    public static void Noscript_ParsesRawTextWithScriptingEnabled()
    {        // REC §8.2.5.4.3 (in head noscript): with the scripting flag on, noscript content
        // parses as raw text — one text child, no elements. The engine registers a scripting
        // service for parsing semantics only; its evaluation entry point no-ops and scripts
        // still execute through Lite's own loader (this test's host-div script proves it).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><noscript><strong>A</strong></noscript></head>" +
            "<body><div id=\"host\"></div>" +
            "<script>document.getElementById('host').textContent = (function(){ " +
            "var n = document.querySelector('noscript'); return n === null ? 'missing' : " +
            "n.childNodes.length + '|' + n.textContent; })();</script></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var host = Find(page.Root, n => n.Attributes.GetValueOrDefault("id") == "host");
        True(host != null, "host div missing");
        Equal("1|<strong>A</strong>", host!.DisplayText);
    }

    [Test]
    public static void OriginSheetInjection_DoesNotSynthesizeHead()
    {
        // template.dat #3: with <html> but no <head> in the source, text-level UA-sheet
        // injection synthesized <head>…</head>, flipping the parser from before-head to
        // after-head so a following <template> landed in body. The DOM-level injection must
        // leave the parse untouched: the template stays in head (REC §8.2.5.4.3).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><template>Hello</template>",
            isSrcdoc: true, "http://test/", 800, 600);
        var head = Find(page.Root, n => n.TagName == "HEAD");
        True(head != null, "head missing");
        True(head!.Children.Any(c => c.TagName == "TEMPLATE"), "template must stay in head");
    }

    [Test]
    public static void OriginSheetInjection_KeepsLeadingCommentDocumentLevel()
    {
        // comments01.dat #11: a bogus comment before <html> must stay a document-level
        // sibling; text-level sheet injection used to splice the UA sheet ahead of it and
        // re-anchor it into head.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><!-- c --><html><head></head><body></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        True(page.Document is not null, "no document");
        True(page.Document!.ChildNodes[1] is IComment, "leading comment must precede html at document level");
        var head = Find(page.Root, n => n.TagName == "HEAD");
        True(head != null && head.Children.All(c => c.TagName != "#comment"), "no comments may land in head");
    }

    [Test]
    public static void Template_AfterHead_LandsInHeadWithoutDoubleHead()
    {
        // template.dat #103-105: after </head>, the template must process using the in-head
        // rules against the existing head, and the post-template reset must honor the head
        // pointer (reset html → after head synthesizes the body; before head created a
        // second empty head). One head carrying the template, plus a body — nothing more.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><head></head><template>Foo</template>",
            isSrcdoc: true, "http://test/", 800, 600);
        var doc = page.Document!;
        var heads = doc.ChildNodes.OfType<IElement>()
            .Concat(doc.ChildNodes.OfType<IElement>().SelectMany(e => e.ChildNodes.OfType<IElement>()))
            .Where(e => e.LocalName == "head")
            .ToList();
        Equal(1, heads.Count);
        var head = heads[0];
        True(head.ChildNodes.OfType<IElement>().Any(e => e.LocalName == "template"), "template must sit in head");
        var template = head.ChildNodes.OfType<IElement>().First(e => e.LocalName == "template")
            as AngleSharp.Html.Dom.IHtmlTemplateElement;
        True(template is not null && template.Content.TextContent == "Foo", "template content must hold Foo");
        True(doc.QuerySelector("body") is not null, "the body must be synthesized");
    }

    [Test]
    public static void Menuitem_StartTag_ReconstructsActiveFormatting()
    {
        // menuitem-element.dat: with </p> having popped <b> off the open-element stack
        // (it stays in the active formatting list), the unknown <menuitem> start tag takes
        // the "any other start tag" rule — reconstruct first, so the menuitem nests inside
        // the re-opened <b> instead of landing directly in body.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><p><b></p><menuitem>",
            isSrcdoc: true, "http://test/", 800, 600);
        var doc = page.Document!;
        var body = doc.QuerySelector("body")!;
        True(body != null, "no body");
        var elements = body.ChildNodes.OfType<AngleSharp.Dom.IElement>().ToList();
        Equal(2, elements.Count);
        Equal("p", elements[0].LocalName);
        Equal("b", elements[1].LocalName);
        True(elements[1].ChildNodes.OfType<AngleSharp.Dom.IElement>().Any(e => e.LocalName == "menuitem"),
            "menuitem must nest inside the reconstructed <b>");
    }

    [Test]
    public static void SelectInTable_TemplateClose_RestoresSelectInTableMode()
    {
        // template.dat: after </template> inside a select whose ancestors are table-
        // related, the reset's select step must switch to "in select in table" (a table
        // element sits below the select), so the <caption> start tag closes the select and
        // reprocesses through the in-table rules — the caption lands in the table, not
        // its text inside the select.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><body><table><tr><td><select><template>Foo</template><caption>A</table>",
            isSrcdoc: true, "http://test/", 800, 600);
        var doc = page.Document!;
        var select = doc.QuerySelector("select");
        True(select != null, "no select");
        True(select!.ChildNodes.OfType<AngleSharp.Dom.IElement>().Any(e => e.LocalName == "template"),
            "the template stays inside the select");
        var caption = doc.QuerySelector("caption");
        True(caption != null, "no caption — the select-in-table reset must reprocess it");
        Equal("A", caption!.TextContent);
        var table = doc.QuerySelector("table")!;
        True(table.ChildNodes.OfType<AngleSharp.Dom.IElement>()
                .Any(e => e.LocalName == "caption" && e.ParentElement == table),
            "the caption must be a table child, not select content");
        True(string.IsNullOrWhiteSpace(select.TextContent),
            "no stray text may leak into the select");
    }
}
