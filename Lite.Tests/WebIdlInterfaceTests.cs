using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Web IDL interface objects: prototype chains, toStringTag, constants and
/// constructor identity exposed to script (branded via __lite_brand).</summary>
public static class WebIdlInterfaceTests
{
    private static JsEngine NewEngine()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        root.AddChild(new LayoutNode(null, "BODY", "", sample.Style));
        return JsEngine.Create(root);
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void InterfaceObjects_ChainAndIdentity()
    {
        var e = NewEngine();
        e.Execute(@"
            var el = document.createElement('div');
            globalThis.__results = [
                el instanceof HTMLDivElement,
                el instanceof HTMLElement,
                el instanceof Element,
                el instanceof Node,
                el instanceof EventTarget,
                el.constructor.name,
                Object.prototype.toString.call(el),
                document instanceof Document,
                Object.prototype.toString.call(document),
                typeof Node === 'function',
                typeof Element === 'function' && typeof HTMLElement === 'function',
            ].join(',');
        ");
        Equal("true,true,true,true,true,HTMLDivElement,[object HTMLDivElement],true,[object HTMLDocument],true,true",
            (string?)Global(e, "__results"));
    }

    [Test]
    public static void InterfaceObjects_Constants()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__results = [
                Node.ELEMENT_NODE === 1 && Node.TEXT_NODE === 3 && Node.DOCUMENT_TYPE_NODE === 10,
                document.createTextNode('x').nodeType === Node.TEXT_NODE,
                NodeFilter.FILTER_ACCEPT === 1 && NodeFilter.FILTER_SKIP === 3 && NodeFilter.SHOW_ALL === 4294967295,
            ].join(',');
        ");
        Equal("true,true,true", (string?)Global(e, "__results"));
    }

    [Test]
    public static void InterfaceObjects_ConstructibleAndIllegal()
    {
        var e = NewEngine();
        e.Execute(@"
            var ev = new Event('custom', { bubbles: true });
            var threw = false;
            try { new Element(); } catch (err) { threw = err instanceof TypeError; }
            globalThis.__results = [
                ev instanceof Event,
                !(ev instanceof EventTarget),
                ev.type === 'custom' && ev.bubbles === true,
                threw,
                document.createElementNS('http://www.w3.org/2000/svg', 'rect') instanceof SVGElement,
            ].join(',');
        ");
        Equal("true,true,true,true,true", (string?)Global(e, "__results"));
    }
}
