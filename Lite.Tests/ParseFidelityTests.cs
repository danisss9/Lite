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
    {
        // REC §8.2.5.4.3 (in head noscript): with the scripting flag on, noscript content
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
}
