using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;

namespace Lite.Conformance.Harness;

/// <summary>Loads a page through the real Parser pipeline without creating a window,
/// then pumps the JS event loop — the same pattern Lite.Tests uses headlessly.</summary>
internal static class HeadlessPage
{
    private sealed class FrameClock { internal double Timestamp; }
    private static readonly ConditionalWeakTable<JsEngine, FrameClock> FrameClocks = new();

    private static bool PumpFrame(JsEngine engine)
    {
        var worked = engine.DrainTree();
        engine.FlushMicrotasksTree();
        var clock = FrameClocks.GetValue(engine, _ => new());
        clock.Timestamp += 16;
        return engine.FlushRAFTree(clock.Timestamp) || worked;
    }

    internal static void PumpFrames(JsEngine engine, int count)
    {
        for (var i = 0; i < count; i++) PumpFrame(engine);
    }

    public static (LayoutNode Root, JsEngine Engine) Load(string url, int width = 800, int height = 600)
        => Load(new NavigationRequest(url), width, height);

    internal static (LayoutNode Root, JsEngine Engine) Load(NavigationRequest request, int width = 800, int height = 600)
    {
        var root = Parser.TraversePage(request, width, height).Root;
        var engine = JsEngine.Instance ?? throw new InvalidOperationException("Parser did not create a JsEngine");
        return (root, engine);
    }

    /// <summary>Pumps macrotasks, microtasks, and rAF callbacks until <paramref name="done"/>
    /// returns true or the timeout elapses. Returns the final value of <paramref name="done"/>.</summary>
    public static bool PumpUntil(JsEngine engine, Func<bool> done, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (done()) return true;
            var worked = PumpFrame(engine);
            if (!worked) Thread.Sleep(5);
        }
        return done();
    }

    /// <summary>Pumps until the event loop goes idle (no pending macrotasks or rAF callbacks
    /// for one full turn) or the timeout elapses.</summary>
    public static void PumpUntilIdle(JsEngine engine, int timeoutMs = 5_000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var worked = PumpFrame(engine);
            if (!worked && !engine.HasPendingTreeTasks && !engine.HasPendingTreeRAF) return;
            if (!worked) Thread.Sleep(5);
        }
    }
}
