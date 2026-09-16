using Jint;
using Jint.Native;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>A read-only rectangle (DOMRectReadOnly) used for observer entry geometry.</summary>
public class JsDomRectReadOnly
{
    internal JsDomRectReadOnly(double x, double y, double width, double height)
    {
        this.x = x; this.y = y; this.width = width; this.height = height;
        left = x; top = y; right = x + width; bottom = y + height;
    }
    public double x { get; }
    public double y { get; }
    public double width { get; }
    public double height { get; }
    public double top { get; }
    public double left { get; }
    public double right { get; }
    public double bottom { get; }
}

/// <summary>A ResizeObserverEntry delivered to a ResizeObserver callback.</summary>
public class JsResizeObserverEntry
{
    private readonly Engine _engine;
    private readonly LayoutNode _target;
    internal JsResizeObserverEntry(Engine engine, LayoutNode target) { _engine = engine; _target = target; }

    public JsElement target => JsElement.For(_engine, _target);

    /// <summary>The element's content-box rectangle (relative to its own padding edge).</summary>
    public JsDomRectReadOnly contentRect =>
        new(0, 0, _target.Box.ContentBox.Width, _target.Box.ContentBox.Height);

    public object[] contentBoxSize =>
        [new BoxSize(_target.Box.ContentBox.Width, _target.Box.ContentBox.Height)];

    public object[] borderBoxSize =>
        [new BoxSize(_target.Box.BorderBox.Width, _target.Box.BorderBox.Height)];

    public sealed record BoxSize(double inlineSize, double blockSize);
}

/// <summary>ResizeObserver. Observed elements' content-box sizes are compared after each layout
/// (at the task checkpoint via <see cref="ResizeObserverRegistry"/>); a callback fires the first
/// time an element is observed and whenever its size changes.</summary>
public class JsResizeObserver
{
    private readonly Engine _engine;
    private readonly JsValue _callback;

    // target → last reported (width, height); null = observed but not yet reported (fire initial).
    internal readonly Dictionary<LayoutNode, (float W, float H)?> Targets = new(ReferenceEqualityComparer.Instance);

    public JsResizeObserver(JsValue callback)
    {
        _engine = callback.AsObject().Engine;
        _callback = callback;
        ResizeObserverRegistry.Register(_engine, this);
    }

    public void observe(JsElement target, JsValue? options = null) => Targets[target.Node] = null;
    public void unobserve(JsElement target) => Targets.Remove(target.Node);
    public void disconnect() => Targets.Clear();

    /// <summary>Compares current sizes to the last reported ones and invokes the callback with the
    /// changed entries. Called by the registry after layout.</summary>
    internal void Deliver()
    {
        var entries = new List<JsResizeObserverEntry>();
        foreach (var node in Targets.Keys.ToList())
        {
            var cur = (node.Box.ContentBox.Width, node.Box.ContentBox.Height);
            var last = Targets[node];
            if (last is { } l && Math.Abs(l.W - cur.Item1) < 0.01f && Math.Abs(l.H - cur.Item2) < 0.01f)
                continue;
            Targets[node] = cur;
            entries.Add(new JsResizeObserverEntry(_engine, node));
        }
        if (entries.Count == 0) return;
        try { _engine.Invoke(_callback, JsValue.FromObject(_engine, entries.ToArray()), JsValue.FromObject(_engine, this)); }
        catch (Exception ex) { Console.WriteLine($"[ResizeObserver] {ex.Message}"); }
    }
}

/// <summary>Tracks all live ResizeObservers and delivers size changes after layout.</summary>
internal static class ResizeObserverRegistry
{
    private static readonly ObserverRegistry<JsResizeObserver> Registrations = new();
    public static void Register(Engine engine, JsResizeObserver observer) => Registrations.Register(engine, observer);

    public static void DeliverAll(Engine engine)
    {
        var observers = Registrations.For(engine);
        if (observers.Count == 0) return;
        JsEngine.For(engine)?.EnsureLayout();
        foreach (var o in observers.ToArray()) o.Deliver();
    }
}
