using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Phase 2 — location, history, popstate/hashchange, document.title.</summary>
public static class NavigationTests
{
    private static JsEngine NewEngine()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        root.AddChild(new LayoutNode(null, "BODY", "", sample.Style));
        var engine = JsEngine.Create(root);
        // Seed a concrete http base so location components are well-defined.
        engine.NotifyNavigated("http://localhost/");
        return engine;
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void History_PushStateUpdatesLengthAndState()
    {
        var e = NewEngine();
        e.Execute(@"
            history.pushState({ a: 1 }, '', '?page=1');
            history.pushState({ a: 2 }, '', '?page=2');
            globalThis.__len = history.length;
            globalThis.__state = history.state.a;
        ");
        // initial entry + 2 pushed = 3
        Equal(3, Convert.ToInt32(Global(e, "__len")));
        Equal(2, Convert.ToInt32(Global(e, "__state")));
    }

    [Test]
    public static void History_ReplaceStateDoesNotGrow()
    {
        var e = NewEngine();
        e.Execute(@"
            history.pushState({}, '', '?a');
            var before = history.length;
            history.replaceState({ r: 9 }, '', '?b');
            globalThis.__before = before;
            globalThis.__after = history.length;
            globalThis.__state = history.state.r;
        ");
        Equal(Convert.ToInt32(Global(e, "__before")), Convert.ToInt32(Global(e, "__after")));
        Equal(9, Convert.ToInt32(Global(e, "__state")));
    }

    [Test]
    public static void History_BackFiresPopState()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__popped = null;
            window.addEventListener('popstate', function (ev) { globalThis.__popped = ev.state ? ev.state.n : 'nostate'; });
            history.pushState({ n: 1 }, '', '?one');
            history.pushState({ n: 2 }, '', '?two');
            history.back();
        ");
        // back() moves to the {n:1} entry and fires popstate with that state.
        Equal(1, Convert.ToInt32(Global(e, "__popped")));
    }

    [Test]
    public static void Location_ReflectsCurrentUrlComponents()
    {
        var e = NewEngine();
        e.Execute(@"
            history.pushState({}, '', '/path/page?q=1#sec');
            globalThis.__path = location.pathname;
            globalThis.__search = location.search;
            globalThis.__hash = location.hash;
        ");
        Equal("/path/page", Global(e, "__path")?.ToString());
        Equal("?q=1", Global(e, "__search")?.ToString());
        Equal("#sec", Global(e, "__hash")?.ToString());
    }

    [Test]
    public static void Location_HashChangeFiresHashchangeNotReload()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__hc = 0;
            window.addEventListener('hashchange', function () { globalThis.__hc++; });
            location.hash = 'section-2';
            globalThis.__hash = location.hash;
        ");
        Equal(1, Convert.ToInt32(Global(e, "__hc")));
        Equal("#section-2", Global(e, "__hash")?.ToString());
    }

    [Test]
    public static void DocumentTitle_RoundTrips()
    {
        var e = NewEngine();
        e.Execute("document.title = 'Hello Lite'; globalThis.__t = document.title;");
        Equal("Hello Lite", Global(e, "__t")?.ToString());
    }

    [Test]
    public static void WindowLocationAndGlobalLocationAreSame()
    {
        var e = NewEngine();
        e.Execute("globalThis.__same = (window.location === location);");
        Equal(true, Convert.ToBoolean(Global(e, "__same")));
    }
}
