using System.Diagnostics;
using Lite.Models;
using Lite.Scripting;

namespace Lite.Conformance.Harness;

/// <summary>Loads a page through the real Parser pipeline without creating a window,
/// then pumps the JS event loop — the same pattern Lite.Tests uses headlessly.</summary>
internal static class HeadlessPage
{
    public static (LayoutNode Root, JsEngine Engine) Load(string url, int width = 800, int height = 600)
    {
        var root = Parser.TraverseHtml(url, width, height);
        var engine = JsEngine.Instance ?? throw new InvalidOperationException("Parser did not create a JsEngine");
        return (root, engine);
    }

    /// <summary>Pumps macrotasks, microtasks, and rAF callbacks until <paramref name="done"/>
    /// returns true or the timeout elapses. Returns the final value of <paramref name="done"/>.</summary>
    public static bool PumpUntil(JsEngine engine, Func<bool> done, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        double rafClock = 0;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (done()) return true;
            var worked = engine.DrainTree();
            engine.FlushMicrotasksTree();
            rafClock += 16;
            worked |= engine.FlushRAFTree(rafClock);
            if (!worked) Thread.Sleep(5);
        }
        return done();
    }

    /// <summary>Pumps until the event loop goes idle (no pending macrotasks or rAF callbacks
    /// for one full turn) or the timeout elapses.</summary>
    public static void PumpUntilIdle(JsEngine engine, int timeoutMs = 5_000)
    {
        var sw = Stopwatch.StartNew();
        double rafClock = 0;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var worked = engine.DrainTree();
            engine.FlushMicrotasksTree();
            rafClock += 16;
            worked |= engine.FlushRAFTree(rafClock);
            if (!worked && !engine.HasPendingTreeTasks && !engine.HasPendingTreeRAF) return;
            if (!worked) Thread.Sleep(5);
        }
    }
}
