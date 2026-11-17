using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the document/range/node-method increment: the
/// constructible Document and Range interface objects (new Document() is an XML document
/// per DOM §3.5; new Range() returns a range over the document), document.createCDATASection's
/// HTML NotSupportedError, and the DOM4 node methods the reviewed clusters measure
/// (isSameNode/isEqualNode/baseURI/closest/splitText/lookupNamespaceURI/lookupPrefix).</summary>
public static class DomNodeMethodTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    // ---- constructible Document ----

    [Test]
    public static void NewDocument_IsAnXmlDocumentFacade()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var doc = new Document();
            globalThis.out = [
              doc instanceof Document, doc instanceof Node,
              Object.getPrototypeOf(doc) === Document.prototype,
              doc.contentType, doc.doctype === null, doc.documentElement === null,
              doc.URL, doc.characterSet
            ].join('|');
            """);
        Equal("true|true|true|application/xml|true|true|about:blank|UTF-8", Eval(engine, "out"));
    }

    [Test]
    public static void NewDocument_CreatesXmlNamesVerbatimAndHtmlInterfaceThroughNs()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var doc = new Document();
            globalThis.out = [
              doc.createElement("DIV").localName,
              doc.createElementNS("http://www.w3.org/1999/xhtml", "a").constructor === HTMLAnchorElement
            ].join('|');
            """);
        Equal("DIV|true", Eval(engine, "out"));
    }

    // ---- constructible Range ----

    [Test]
    public static void NewRange_ReturnsRangeOverTheDocument()
    {
        var engine = Parsed("<!doctype html><body><p id=a>text</p></body></html>");
        Run(engine, """
            var r = new Range();
            var sc = r.startContainer;
            globalThis.out = [
              r instanceof Range,
              sc === document, typeof sc, sc === undefined || sc === null ? 'none' : (sc.nodeType + ':' + sc.nodeName),
              r.startOffset === 0, r.collapsed
            ].join('|');
            var a = document.getElementById('a');
            r.selectNode(a);
            globalThis.out2 = [r.collapsed, r.startContainer === document.body].join('|');
            """);
        Eval(engine, "globalThis.out");
        Eval(engine, "globalThis.out2");
        Equal("true|true|object|9:#document|true|true", Eval(engine, "globalThis.out"));
        Equal("false|true", Eval(engine, "globalThis.out2"));
    }

    // ---- createCDATASection ----

    [Test]
    public static void CreateCdataSection_ThrowsNotSupportedErrorInHtmlDocuments()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var threw = '';
            try { document.createCDATASection("x"); } catch (e) { threw = e.name; }
            globalThis.out = threw;
            """);
        Equal("NotSupportedError", Eval(engine, "out"));
    }

    [Test]
    public static void CreateCdataSection_WorksInXmlDocuments()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var doc = new Document();
            var c = doc.createCDATASection("1234");
            globalThis.out = [c.data, c.nodeType].join('|');
            """);
        Equal("1234|4", Eval(engine, "out"));
    }

    // ---- node identity / equality ----

    [Test]
    public static void IsSameNode_IsIdentity()
    {
        var engine = Parsed("<!doctype html><body><p id=a>t</p><p id=b>t</p></body></html>");
        Run(engine, """
            var a = document.getElementById('a');
            var b = document.getElementById('b');
            globalThis.out = [a.isSameNode(a), a.isSameNode(b), a.isSameNode(null)].join('|');
            """);
        Equal("true|false|false", Eval(engine, "out"));
    }

    [Test]
    public static void IsEqualNode_IsDeepStructuralEquality()
    {
        var engine = Parsed("<!doctype html><body><p class=x>t</p><p class=x>t</p><p class=y>t</p></body></html>");
        Run(engine, """
            var ps = document.getElementsByTagName('p');
            globalThis.out = [ps[0].isEqualNode(ps[1]), ps[0].isEqualNode(ps[2]), ps[0].isEqualNode(document)].join('|');
            """);
        Equal("true|false|false", Eval(engine, "out"));
    }

    // ---- baseURI ----

    [Test]
    public static void BaseURI_IsTheDocumentUrl()
    {
        var engine = Parsed("<!doctype html><body><p id=a>t</p></body></html>");
        Run(engine, """
            globalThis.out = document.getElementById('a').baseURI;
            """);
        Equal("http://test/", Eval(engine, "out"));
    }

    // ---- closest ----

    [Test]
    public static void Closest_WalksSelfThenAncestors()
    {
        var engine = Parsed("""
            <!doctype html><body>
            <div id=outer><p id=inner><span id=target>text</span></p></div>
            </body></html>
            """);
        Run(engine, """
            var t = document.getElementById('target');
            globalThis.out = [
              t.closest('#target').id,
              t.closest('p').id,
              t.closest('div').id,
              t.closest('section') === null
            ].join('|');
            """);
        Equal("target|inner|outer|true", Eval(engine, "out"));
    }

    // ---- splitText ----

    [Test]
    public static void SplitText_SplitsTheTextNode()
    {
        var engine = Parsed("<!doctype html><body><p id=a>abcdef</p></body></html>");
        Run(engine, """
            var p = document.getElementById('a');
            var text = p.firstChild;
            var tail = text.splitText(3);
            globalThis.out = [text.data, tail.data, p.childNodes.length].join('|');
            """);
        Equal("abc|def|2", Eval(engine, "out"));
    }

    // ---- namespace lookup ----

    [Test]
    public static void LookupNamespaceURI_ResolvesInScopeNamespaces()
    {
        var engine = Parsed("""
            <!doctype html><body>
            <div id=d xmlns="http://www.w3.org/1999/xhtml" xmlns:pub="http://example.com/publish">
              <pub:page id=p>text</pub:page>
            </div>
            </body></html>
            """);
        Run(engine, """
            var p = document.getElementById('p');
            globalThis.out = [
              p.lookupNamespaceURI(null),
              p.lookupNamespaceURI('pub'),
              p.lookupNamespaceURI('nothing'),
              document.lookupNamespaceURI('xml')
            ].join('|');
            """);
        // Array.join renders null/undefined slots as empty strings.
        Equal("http://www.w3.org/1999/xhtml|http://example.com/publish||http://www.w3.org/XML/1998/namespace",
            Eval(engine, "out"));
    }

    [Test]
    public static void LookupPrefix_ResolvesInScopePrefixes()
    {
        var engine = Parsed("""
            <!doctype html><body>
            <div id=d xmlns:pub="http://example.com/publish"><pub:page id=p>text</pub:page></div>
            </body></html>
            """);
        Run(engine, """
            globalThis.out = document.getElementById('p').lookupPrefix('http://example.com/publish');
            """);
        Equal("pub", Eval(engine, "out"));
    }
}
