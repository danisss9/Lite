using Jint;
using Jint.Native;
using Lite.Models;
using SkiaSharp;

namespace Lite.Scripting.Dom;

/// <summary>An IntersectionObserverEntry delivered to an IntersectionObserver callback.</summary>
public class JsIntersectionObserverEntry
{
    private readonly Engine _engine;
    private readonly LayoutNode _target;
    private readonly SKRect _bounds;
    private readonly SKRect _root;
    private readonly SKRect _intersection;

    internal JsIntersectionObserverEntry(Engine engine, LayoutNode target, SKRect bounds, SKRect root, SKRect intersection)
    {
        _engine = engine; _target = target; _bounds = bounds; _root = root; _intersection = intersection;
    }

    public JsElement target => JsElement.For(_engine, _target);
    public JsDomRectReadOnly boundingClientRect => Rect(_bounds);
    public JsDomRectReadOnly rootBounds => Rect(_root);
    public JsDomRectReadOnly intersectionRect => Rect(_intersection);

    public double intersectionRatio
    {
        get
        {
            var area = _bounds.Width * _bounds.Height;
            if (area <= 0) return 0;
            var inter = Math.Max(0, _intersection.Width) * Math.Max(0, _intersection.Height);
            return Math.Clamp(inter / area, 0, 1);
        }
    }

    public bool isIntersecting => intersectionRatio > 0;
    public double time => 0;

    private static JsDomRectReadOnly Rect(SKRect r) => new(r.Left, r.Top, r.Width, r.Height);
}

/// <summary>IntersectionObserver. Compares each observed element's border box against the root
/// (the viewport when no root is given) after layout, delivering an entry on first observation and
/// whenever the intersecting state changes.</summary>
public class JsIntersectionObserver
{
    private readonly Engine _engine;
    private readonly JsValue _callback;

    // target → last reported isIntersecting (null = not yet reported → fire initial).
    internal readonly Dictionary<LayoutNode, bool?> Targets = new(ReferenceEqualityComparer.Instance);

    public JsIntersectionObserver(JsValue callback, JsValue? options = null)
    {
        _engine = JsEngine.Instance!.RawEngine;
        _callback = callback;
        IntersectionObserverRegistry.Register(this);
    }

    public void observe(JsElement target) => Targets[target.Node] = null;
    public void unobserve(JsElement target) => Targets.Remove(target.Node);
    public void disconnect() => Targets.Clear();
    public object[] takeRecords() => Array.Empty<object>();

    internal void Deliver()
    {
        var (vw, vh) = JsEngine.Instance?.ViewportSize ?? (0, 0);
        var root = new SKRect(0, 0, vw, vh);

        var entries = new List<JsIntersectionObserverEntry>();
        foreach (var node in Targets.Keys.ToList())
        {
            var bounds = node.Box.BorderBox;
            var intersection = SKRect.Intersect(bounds, root);
            var nowIntersecting = intersection.Width > 0 && intersection.Height > 0;

            if (Targets[node] is { } was && was == nowIntersecting) continue; // unchanged
            Targets[node] = nowIntersecting;
            entries.Add(new JsIntersectionObserverEntry(_engine, node, bounds, root,
                nowIntersecting ? intersection : SKRect.Empty));
        }
        if (entries.Count == 0) return;
        try { _engine.Invoke(_callback, JsValue.FromObject(_engine, entries.ToArray()), JsValue.FromObject(_engine, this)); }
        catch (Exception ex) { Console.WriteLine($"[IntersectionObserver] {ex.Message}"); }
    }
}

/// <summary>Tracks all live IntersectionObservers and delivers intersection changes after layout.</summary>
internal static class IntersectionObserverRegistry
{
    private static readonly List<JsIntersectionObserver> _observers = [];
    public static void Reset() => _observers.Clear();
    public static void Register(JsIntersectionObserver o) => _observers.Add(o);
    public static bool HasObservers => _observers.Count > 0;

    public static void DeliverAll()
    {
        if (_observers.Count == 0) return;
        JsEngine.Instance?.EnsureLayout();
        foreach (var o in _observers.ToArray()) o.Deliver();
    }
}
