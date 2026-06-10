using System.Diagnostics;
using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Item 2 — event loop, storage, fetch, modules, cookies.</summary>
public static class EventLoopTests
{
    private static JsEngine NewEngine()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        root.AddChild(new LayoutNode(null, "BODY", "", sample.Style));
        return JsEngine.Create(root);
    }

    private static bool WaitFor(Func<bool> cond, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(5);
        }
        return cond();
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void Macrotask_DrainRunsQueuedTask()
    {
        var e = NewEngine();
        e.EnqueueMacrotask(() => e.Execute("globalThis.__ran = 1;"));
        True(e.HasPendingTasks, "task should be pending before drain");
        True(e.DrainTasks(), "drain should report work done");
        Equal(1, Convert.ToInt32(Global(e, "__ran")));
    }

    [Test]
    public static void SetTimeout_EnqueuesAndRunsOnDrain()
    {
        var e = NewEngine();
        e.Execute("globalThis.__t = 0; setTimeout(function(){ globalThis.__t = 5; }, 1);");
        // Timer fires on a pool thread and enqueues; UI thread drains.
        True(WaitFor(() => e.HasPendingTasks), "setTimeout should enqueue a task");
        e.DrainTasks();
        Equal(5, Convert.ToInt32(Global(e, "__t")));
    }

    [Test]
    public static void PromiseContinuation_RunsAfterTimerCallback()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__steps = '';
            setTimeout(function () {
                globalThis.__steps += 'timer';
                Promise.resolve().then(function () { globalThis.__steps += '-microtask'; });
            }, 1);
        ");
        True(WaitFor(() => e.HasPendingTasks), "timer should enqueue");
        e.DrainTasks();
        // The microtask scheduled inside the timer callback must run during the same drain.
        Equal("timer-microtask", Global(e, "__steps")?.ToString());
    }

    [Test]
    public static void LocalStorage_SetAndGet()
    {
        var e = NewEngine();
        e.Execute("localStorage.setItem('k', 'v'); globalThis.__v = localStorage.getItem('k');");
        Equal("v", Global(e, "__v")?.ToString());
    }

    [Test]
    public static void SessionStorage_LengthAndRemove()
    {
        var e = NewEngine();
        e.Execute(@"
            sessionStorage.setItem('a', '1');
            sessionStorage.setItem('b', '2');
            globalThis.__len = sessionStorage.length;
            sessionStorage.removeItem('a');
            globalThis.__len2 = sessionStorage.length;
        ");
        Equal(2, Convert.ToInt32(Global(e, "__len")));
        Equal(1, Convert.ToInt32(Global(e, "__len2")));
    }

    [Test]
    public static void Fetch_DataUri_ResolvesJson()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__body = null;
            fetch('data:application/json,{""n"":42}')
                .then(function (r) { return r.json(); })
                .then(function (j) { globalThis.__body = j.n; });
        ");
        // Background fetch enqueues the resolve callback; drain delivers it and runs the chain.
        True(WaitFor(() => e.HasPendingTasks), "fetch should enqueue a resolve callback");
        e.DrainTasks();
        Equal(42, Convert.ToInt32(Global(e, "__body")));
    }

    [Test]
    public static void Cookie_RoundTrips()
    {
        var e = NewEngine();
        e.Execute("document.cookie = 'session=abc; path=/'; globalThis.__c = document.cookie;");
        Contains("session=abc", Global(e, "__c")?.ToString());
    }

    [Test]
    public static void Module_AddAndImportRuns()
    {
        var e = NewEngine();
        // Inline modules are registered under an absolute specifier (matching what the loader
        // resolves to), so Add + Import find the same module without a network fetch.
        e.AddModule("http://test/mod.js", "globalThis.__modValue = 7; export const x = 7;");
        e.ImportModule("http://test/mod.js");
        Equal(7, Convert.ToInt32(Global(e, "__modValue")));
    }
}
