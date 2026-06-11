using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Phase 1 — JS host objects: URL, URLSearchParams, navigator, dynamic import, import.meta.</summary>
public static class HostObjectTests
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
    public static void Url_ParsesComponents()
    {
        var e = NewEngine();
        e.Execute(@"
            var u = new URL('https://user:pass@example.com:8080/a/b?x=1&y=2#frag');
            globalThis.__proto = u.protocol;
            globalThis.__host = u.host;
            globalThis.__hostname = u.hostname;
            globalThis.__port = u.port;
            globalThis.__path = u.pathname;
            globalThis.__search = u.search;
            globalThis.__hash = u.hash;
            globalThis.__origin = u.origin;
        ");
        Equal("https:", Global(e, "__proto")?.ToString());
        Equal("example.com:8080", Global(e, "__host")?.ToString());
        Equal("example.com", Global(e, "__hostname")?.ToString());
        Equal("8080", Global(e, "__port")?.ToString());
        Equal("/a/b", Global(e, "__path")?.ToString());
        Equal("?x=1&y=2", Global(e, "__search")?.ToString());
        Equal("#frag", Global(e, "__hash")?.ToString());
        Equal("https://example.com:8080", Global(e, "__origin")?.ToString());
    }

    [Test]
    public static void Url_ResolvesRelativeAgainstBase()
    {
        var e = NewEngine();
        e.Execute("globalThis.__h = new URL('../c?z=9', 'https://example.com/a/b/').href;");
        Equal("https://example.com/a/c?z=9", Global(e, "__h")?.ToString());
    }

    [Test]
    public static void UrlSearchParams_GetAppendHas()
    {
        var e = NewEngine();
        e.Execute(@"
            var p = new URLSearchParams('a=1&b=2&a=3');
            globalThis.__a = p.get('a');
            globalThis.__all = p.getAll('a').join(',');
            p.append('c', '4');
            globalThis.__hasC = p.has('c');
            globalThis.__str = p.toString();
        ");
        Equal("1", Global(e, "__a")?.ToString());
        Equal("1,3", Global(e, "__all")?.ToString());
        Equal(true, Convert.ToBoolean(Global(e, "__hasC")));
        Contains("c=4", Global(e, "__str")?.ToString());
    }

    [Test]
    public static void Url_SearchParamsIsLive()
    {
        var e = NewEngine();
        e.Execute(@"
            var u = new URL('https://x.test/?a=1');
            u.searchParams.append('b', '2');
            globalThis.__search = u.search;
        ");
        Contains("b=2", Global(e, "__search")?.ToString());
    }

    [Test]
    public static void Navigator_HasUserAgent()
    {
        var e = NewEngine();
        e.Execute("globalThis.__ua = navigator.userAgent; globalThis.__plat = navigator.platform;");
        Contains("Lite", Global(e, "__ua")?.ToString());
        Equal("Win32", Global(e, "__plat")?.ToString());
    }

    [Test]
    public static void OptionalChaining_And_NullishCoalescing_Work()
    {
        // ES2020 syntax that requires Jint 4.
        var e = NewEngine();
        e.Execute(@"
            var o = { a: { b: 5 } };
            globalThis.__oc = o?.a?.b;
            globalThis.__ocMiss = o?.x?.y;
            globalThis.__nc = (null ?? 'fallback');
        ");
        Equal(5, Convert.ToInt32(Global(e, "__oc")));
        True(Global(e, "__ocMiss") is null, "missing optional chain should be undefined/null");
        Equal("fallback", Global(e, "__nc")?.ToString());
    }

    [Test]
    public static void DynamicImport_And_ImportMeta_Work()
    {
        var e = NewEngine();
        // import.meta must be syntactically valid and accessible as an object inside a module.
        // (Its .url is populated from the loader's resolved URI; inline-added modules have none.)
        e.AddModule("http://test/dyn.js", "export const v = 99; globalThis.__metaType = typeof import.meta;");
        e.Execute(@"
            globalThis.__dynOk = false;
            import('http://test/dyn.js').then(function (m) {
                globalThis.__dynV = m.v;
                globalThis.__dynOk = true;
            });
        ");
        e.DrainTasks();
        Equal(99, Convert.ToInt32(Global(e, "__dynV")));
        Equal(true, Convert.ToBoolean(Global(e, "__dynOk")));
        Equal("object", Global(e, "__metaType")?.ToString());
    }

    [Test]
    public static void Xhr_AsyncDeliversOnEventLoop()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__xhrBody = null;
            var x = new XMLHttpRequest();
            x.open('GET', 'data:text/plain,hello-xhr');
            x.onload = function () { globalThis.__xhrBody = x.responseText; };
            x.send();
        ");
        // data: URIs are handled synchronously by HttpClient on the pool thread, then the
        // delivery is marshalled back; drain runs it on this thread.
        var ok = false;
        for (int i = 0; i < 200 && !ok; i++)
        {
            e.DrainTasks();
            ok = Global(e, "__xhrBody") is not null;
            if (!ok) Thread.Sleep(5);
        }
        Equal("hello-xhr", Global(e, "__xhrBody")?.ToString());
    }
}
