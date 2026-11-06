using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the DOM authority package: the HTMLCollection/NamedNodeMap
/// legacy platform-object semantics (own property names, supported-property protection, the
/// 2^32-1 boundary, prototype-chain receivers), getElementsByTagNameNS, the verbatim
/// DOMTokenList stringifier and its per-interface coverage, DOMStringMap enumeration, the
/// title's engine-tree authority, initial-parse comment visibility, the wrong-typed wrapper
/// TypeError, the query-path origin-sheet exclusion, and the TreeWalker/NodeIterator residuals
/// (null filter, detached currentNode, previousNode's descend-before-check).</summary>
public static class DomAuthorityTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    // ---- HTMLCollection legacy platform-object semantics ----

    [Test]
    public static void HTMLCollection_OwnPropertyNamesAreIndicesThenSupportedNames()
    {
        var engine = Parsed("""
            <!doctype html><body>
            <span></span><span id=''></span><span id='some-id'></span><span id='some-id'></span>
            <span name=''></span><span name='some-name'></span><span name='some-name'></span>
            <span id='another-id' name='another-name'></span>
            </body></html>
            """);
        Run(engine, """
            var c = document.getElementsByTagName('span');
            var names = Object.getOwnPropertyNames(c);
            globalThis.out = names.join(',');
            """);
        Equal("0,1,2,3,4,5,6,7,some-id,some-name,another-id,another-name", Eval(engine, "out"));
    }

    [Test]
    public static void HTMLCollection_SupportedPropertiesResistDeleteAndDefine()
    {
        var engine = Parsed("<!doctype html><body><i id=foo></i></body></html>");
        // Sloppy mode first: assignments to supported properties are silent no-ops, deletes fail.
        Run(engine, """
            var c = document.getElementsByTagName('i');
            var out = [];
            c[0] = 5;
            out.push(c[0].id);
            delete c[0];
            out.push(c[0].id);
            out.push(c.foo.id);
            var d = Object.getOwnPropertyDescriptor(c, 0);
            out.push(d.enumerable + '/' + d.configurable + '/' + d.writable);
            var n = Object.getOwnPropertyDescriptor(c, 'foo');
            out.push(n.enumerable + '/' + n.configurable + '/' + n.writable);
            globalThis.out = out.join('|');
            """);
        Equal("foo|foo|foo|true/true/false|false/true/false", Eval(engine, "out"));
        // Strict mode then: supported properties throw on write and delete.
        Run(engine, """
            'use strict';
            var c = document.getElementsByTagName('i');
            var out = [];
            try { c[0] = 5; out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { Object.defineProperty(c, 0, { value: 5 }); out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { delete c[0]; out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { delete c.foo; out.push('no-throw'); } catch (e) { out.push(e.name); }
            out.push(c[0].id + '/' + c.foo.id);
            globalThis.out = out.join('|');
            """);
        Equal("TypeError|TypeError|TypeError|TypeError|foo/foo", Eval(engine, "out"));
    }

    [Test]
    public static void HTMLCollection_IndexBoundaryFallsToNamedLookup()
    {
        var engine = Parsed("""
            <!doctype html><body>
            <i id='4294967293'></i><i id='4294967295'></i>
            </body></html>
            """);
        Run(engine, """
            var c = document.getElementsByTagName('i');
            var out = [];
            out.push(c[0] === document.getElementById('4294967293'));
            out.push(c['4294967293'] === undefined);
            out.push(c['4294967295'] === document.getElementById('4294967295'));
            out.push(c.namedItem('4294967293') === document.getElementById('4294967293'));
            out.push(c.namedItem(-2) === null);
            var probe = c.item(4294967296);
            out.push(probe === null || probe === undefined ? 'null' : probe.id);
            var probe2 = c.item(4294967297);
            out.push(probe2 === null || probe2 === undefined ? 'null' : probe2.id);
            out.push(c.item(-2) === null);
            globalThis.out = out.join('|');
            """);
        // item() takes an unsigned long: ToUint32 wraps 2^32 back to index 0 and -2 to 2^32-2.
        Equal("true|true|true|true|true|4294967293|4294967295|true", Eval(engine, "out"));
    }

    [Test]
    public static void HTMLCollection_ExpandoShadowsLaterNamedProperty()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var c = document.getElementsByTagName('span');
            var out = [];
            out.push(c['new-id'] === undefined);
            c['new-id'] = 5;
            out.push(c['new-id']);
            var span = document.createElement('span');
            span.id = 'new-id';
            document.body.appendChild(span);
            out.push(c.namedItem('new-id') === span);
            out.push(c['new-id']);
            delete c['new-id'];
            out.push(c['new-id'] === span);
            c['new-id2'] = 7;
            var span2 = document.createElement('span');
            span2.id = 'new-id2';
            document.body.appendChild(span2);
            delete c['new-id2'];
            out.push(c['new-id2'] === span2);
            globalThis.out = out.join('|');
            """);
        Equal("true|5|true|5|true|true", Eval(engine, "out"));
    }

    [Test]
    public static void HTMLCollection_AsPrototypeHasNoMembersButKeepsNamedLookup()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var el = document.createElement('p');
            el.id = 'named';
            document.body.appendChild(el);
            var c = document.getElementsByTagName('p');
            var out = [];
            try { var l = Object.create(c).length; out.push('len:' + l); } catch (e) { out.push('threw:' + e.name); }
            var obj = Object.create(c);
            out.push(obj.named === el);
            obj.named = 'own';
            out.push(obj.named);
            out.push(c.named === el);
            out.push(Object.getOwnPropertyNames(c).join(','));
            globalThis.out = out.join('|');
            """);
        Equal("threw:TypeError|true|own|true|0,named", Eval(engine, "out"));
    }

    [Test]
    public static void HTMLCollection_CollectionIsLiveAndIterable()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var c = document.getElementsByTagName('span');
            var out = [];
            out.push(c.length);
            out.push('values' in c);
            out.push('forEach' in c);
            out.push(Symbol.iterator in c);
            var span = document.createElement('span');
            document.body.appendChild(span);
            out.push(c.length);
            out.push(c[0] === span);
            var iterated = [];
            for (var item of c) iterated.push(item === span);
            globalThis.out = out.join('|') + '|' + iterated.join(',');
            """);
        Equal("0|false|false|true|1|true|true", Eval(engine, "out"));
    }

    // ---- getElementsByTagNameNS ----

    [Test]
    public static void GetElementsByTagNameNS_MatchesNamespaceAndWildcards()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var svgA = document.createElementNS('http://www.w3.org/2000/svg', 'a');
            var htmlA = document.createElement('a');
            document.body.appendChild(svgA);
            document.body.appendChild(htmlA);
            var out = [];
            out.push(document.getElementsByTagNameNS('http://www.w3.org/2000/svg', 'a').length);
            out.push(document.getElementsByTagNameNS('http://www.w3.org/1999/xhtml', 'a').length);
            out.push(document.getElementsByTagNameNS('*', 'a').length);
            out.push(document.getElementsByTagNameNS('', 'a').length);
            out.push(document.getElementsByTagNameNS('http://www.w3.org/2000/svg', '*').length);
            out.push(document.body.getElementsByTagNameNS('http://www.w3.org/2000/svg', 'a').length);
            globalThis.out = out.join('|');
            """);
        Equal("1|1|2|0|1|1", Eval(engine, "out"));
    }

    // ---- DOMTokenList: verbatim stringifier, value, coverage ----

    [Test]
    public static void DOMTokenList_StringifierReflectsAttributeVerbatim()
    {
        var engine = Parsed("<!doctype html><body><span class='   a  a b '></span></body></html>");
        Run(engine, """
            var span = document.querySelector('span');
            var out = [];
            out.push(String(document.createElement('span').classList));
            out.push(String(span.classList));
            out.push(span.classList.toString());
            out.push(span.classList.value);
            out.push(span.className);
            out.push(span.classList.length);
            out.push(span.classList[1]);
            out.push(span.classList.contains('a'));
            span.classList.add('c');
            out.push(span.getAttribute('class'));
            globalThis.out = out.join('|');
            """);
        // The stringifier and value read the attribute verbatim; the ordered set dedupes and a
        // mutation serializes it; indexed access reads the token list.
        Equal("|   a  a b |   a  a b |   a  a b |   a  a b |2|b|true|a b c", Eval(engine, "out"));
    }

    [Test]
    public static void DOMTokenList_CoverageGatesByInterfaceAndNamespace()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            var label = Object.prototype.toString.call(document.createElement('output').htmlFor);
            out.push(label);
            out.push(typeof document.createElement('a').htmlFor);
            var lab = document.createElement('label');
            lab.setAttribute('for', 'x');
            out.push(lab.htmlFor);
            var iframe = document.createElement('iframe');
            out.push(Object.prototype.toString.call(iframe.sandbox));
            out.push(typeof document.createElement('td').sandbox);
            var link = document.createElement('link');
            out.push(Object.prototype.toString.call(link.sizes));
            out.push(Object.prototype.toString.call(document.createElement('div').classList));
            globalThis.out = out.join('|');
            """);
        Equal("[object DOMTokenList]|undefined|x|[object DOMTokenList]|undefined|[object DOMTokenList]|[object DOMTokenList]",
            Eval(engine, "out"));
    }

    // ---- DOMStringMap enumeration ----

    [Test]
    public static void DOMStringMap_OwnNamesCoverEmptyAndHyphenForms()
    {
        var engine = Parsed("""
            <!doctype html><body>
            <div id=edge1 data-='012'></div>
            <div id=edge2 data-id-='y'></div>
            <div id=user data-id='1' data-user='j' data-date-of-birth></div>
            </body></html>
            """);
        Run(engine, """
            var out = [];
            out.push(Object.getOwnPropertyNames(document.getElementById('edge1').dataset).join(','));
            out.push(Object.getOwnPropertyNames(document.getElementById('edge2').dataset).join(','));
            out.push(Object.getOwnPropertyNames(document.getElementById('user').dataset).join(','));
            out.push(document.getElementById('edge1').dataset['']);
            globalThis.out = out.join('|');
            """);
        // The bare data- attribute is the empty supported name; a trailing hyphen stays.
        Equal("|id-|id,user,dateOfBirth|012", Eval(engine, "out"));
    }

    // ---- NamedNodeMap supported property names ----

    [Test]
    public static void NamedNodeMap_OwnPropertyNamesAreIndicesThenAttributeNames()
    {
        var engine = Parsed("""
            <!doctype html><body><input id=result type=text value='' width=200px></body></html>
            """);
        Run(engine, """
            var el = document.getElementById('result');
            var out = [];
            out.push(Object.getOwnPropertyNames(el.attributes).join(','));
            el.removeAttribute('width');
            out.push(Object.getOwnPropertyNames(el.attributes).join(','));
            out.push(el.attributes.value.value);
            globalThis.out = out.join('|');
            """);
        Equal("0,1,2,3,id,type,value,width|0,1,2,id,type,value|", Eval(engine, "out"));
    }

    // ---- title authority on the engine tree ----

    [Test]
    public static void Title_FollowsEngineTreeMutations()
    {
        var engine = Parsed("<!doctype html><html><head><title>Old</title></head><body></body></html>");
        Run(engine, """
            var out = [];
            out.push(document.title);
            document.querySelector('title').textContent = 'FromElement';
            out.push(document.title);
            document.title = 'FromSetter';
            out.push(document.querySelector('title').textContent);
            out.push(document.head.querySelector('title') === null);
            globalThis.out = out.join('|');
            """);
        Equal("Old|FromElement|FromSetter|false", Eval(engine, "out"));
    }

    // ---- initial-parse comments are script-visible ----

    [Test]
    public static void InitialParse_ProjectsCommentsAmongChildren()
    {
        var engine = Parsed("<!doctype html><body><!--c0--><p>a</p><!--c1--></body></html>");
        Run(engine, """
            var out = [];
            var nodes = document.body.childNodes;
            out.push(nodes.length);
            out.push(nodes[0].nodeType);
            out.push(nodes[0].data);
            out.push(nodes[2].data);
            out.push(nodes[1].tagName);
            globalThis.out = out.join('|');
            """);
        Equal("3|8|c0|c1|P", Eval(engine, "out"));
    }

    // ---- wrong-typed wrappers raise DOMException/TypeError, never a CLR message ----

    [Test]
    public static void WrongTypeWrapperArgumentsThrowCatchableDomErrors()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            try { document.body.removeChild(document); out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { document.body.appendChild(document); out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { document.body.replaceChild(document, document.body.firstChild); out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { document.body.appendChild(document.doctype); out.push('no-throw'); } catch (e) { out.push(e.name); }
            try { document.createTreeWalker(document.body).currentNode = 5; out.push('no-throw'); } catch (e) { out.push(e.name); }
            globalThis.out = out.join('|');
            """);
        Equal("NotFoundError|HierarchyRequestError|HierarchyRequestError|HierarchyRequestError|TypeError",
            Eval(engine, "out"));
    }

    // ---- query paths exclude the engine-injected origin sheets ----

    [Test]
    public static void QueryPathsExcludeOriginSheets()
    {
        var engine = Parsed("""
            <!doctype html><html><head><style>p { color: red; }</style></head>
            <body><p>text</p></body></html>
            """);
        Run(engine, """
            var out = [];
            out.push(document.getElementsByTagName('style').length);
            out.push(document.querySelectorAll('style').length);
            out.push(document.querySelectorAll('head > style').length);
            out.push(document.head.querySelectorAll('style').length);
            out.push(document.head.childNodes.length);
            globalThis.out = out.join('|');
            """);
        // The page's one author style is visible; the injected UA sheet is not — through the
        // child lists or any query path.
        Equal("1|1|1|1|1", Eval(engine, "out"));
    }

    // ---- TreeWalker / NodeIterator residuals ----

    [Test]
    public static void TreeWalker_PreviousNodeDescendsBeforeChecking()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var root = document.createElement('div'); root.id = 'root';
            var a1 = document.createElement('div'); a1.id = 'A1';
            var b1 = document.createElement('div'); b1.id = 'B1';
            var b2 = document.createElement('div'); b2.id = 'B2';
            var c1 = document.createElement('div'); c1.id = 'C1';
            var c2 = document.createElement('div'); c2.id = 'C2';
            var c3 = document.createElement('div'); c3.id = 'C3';
            var d1 = document.createElement('div'); d1.id = 'D1';
            var d2 = document.createElement('div'); d2.id = 'D2';
            root.appendChild(a1); a1.appendChild(b1); a1.appendChild(b2);
            b1.appendChild(c1); b1.appendChild(c2); b2.appendChild(c3);
            c2.appendChild(d1); c2.appendChild(d2);
            var w = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT, function (node) {
                return node.id == 'C2' ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT;
            });
            var out = [];
            out.push(w.firstChild().id);
            out.push(w.nextNode().id);
            out.push(w.nextNode().id);
            out.push(w.nextNode().id);
            out.push(w.previousNode().id);
            globalThis.out = out.join('|');
            """);
        Equal("A1|B1|C1|B2|C1", Eval(engine, "out"));
    }

    [Test]
    public static void TreeWalker_NullFilterActsAsNoFilter()
    {
        var engine = Parsed("<!doctype html><body><p>a</p><p>b</p></body></html>");
        Run(engine, """
            var out = [];
            var w = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, null);
            out.push(w.filter === null);
            var count = 0;
            while (w.nextNode() !== null) count++;
            out.push(count);
            var w2 = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, undefined);
            out.push(w2.filter === null);
            var w3 = document.createTreeWalker(document.body, null, null);
            out.push(w3.nextNode() === null);
            globalThis.out = out.join('|');
            """);
        Equal("true|2|true|true", Eval(engine, "out"));
    }

    [Test]
    public static void TreeWalker_DetachedCurrentNodeEndsTheWalk()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var p = document.createElement('p');
            document.body.appendChild(p);
            var w = document.createTreeWalker(document.body);
            w.currentNode = p;
            document.body.removeChild(p);
            var out = [];
            out.push(w.nextNode() === null);
            out.push(w.currentNode === document.body);
            globalThis.out = out.join('|');
            """);
        Equal("true|true", Eval(engine, "out"));
    }

    [Test]
    public static void NodeIterator_DocumentRootDefaultsAndDetach()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var it = document.createNodeIterator(document);
            var out = [];
            it.detach();
            it.detach();
            out.push(it.root === document);
            out.push(it.referenceNode === document);
            out.push(it.pointerBeforeReferenceNode);
            out.push(it.filter === null);
            out.push(it.nextNode() === document.documentElement);
            out.push(Object.prototype.toString.call(it));
            var it2 = document.createNodeIterator(document, null, null);
            out.push(it2.whatToShow);
            globalThis.out = out.join('|');
            """);
        // document-rooted iterators expose the document as root/referenceNode; whatToShow 0
        // comes from a null second argument (explicit, not the default).
        Equal("true|true|true|true|true|[object NodeIterator]|0", Eval(engine, "out"));
    }
}
