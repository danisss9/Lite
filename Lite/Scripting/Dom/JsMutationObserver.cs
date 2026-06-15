using Jint;
using Jint.Native;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>A MutationRecord delivered to a MutationObserver callback.</summary>
public class JsMutationRecord
{
    private readonly Engine _engine;
    private readonly LayoutNode _target;
    private readonly List<LayoutNode> _added;
    private readonly List<LayoutNode> _removed;
    private readonly LayoutNode? _prev;
    private readonly LayoutNode? _next;

    internal JsMutationRecord(Engine engine, string type, LayoutNode target,
        List<LayoutNode>? added = null, List<LayoutNode>? removed = null,
        LayoutNode? prev = null, LayoutNode? next = null,
        string? attributeName = null, string? oldValue = null)
    {
        _engine = engine;
        this.type = type;
        _target = target;
        _added = added ?? [];
        _removed = removed ?? [];
        _prev = prev;
        _next = next;
        this.attributeName = attributeName;
        this.oldValue = oldValue;
    }

    public string type { get; }
    public JsElement target => new(_engine, _target);
    public JsElement[] addedNodes => _added.Select(n => new JsElement(_engine, n)).ToArray();
    public JsElement[] removedNodes => _removed.Select(n => new JsElement(_engine, n)).ToArray();
    public JsElement? previousSibling => _prev is null ? null : new JsElement(_engine, _prev);
    public JsElement? nextSibling => _next is null ? null : new JsElement(_engine, _next);
    public string? attributeName { get; }
    public string? attributeNamespace => null;
    public string? oldValue { get; }
}

/// <summary>MutationObserver. Records are queued by <see cref="MutationObserverRegistry"/> as
/// the DOM mutates and delivered to the callback at the microtask checkpoint.</summary>
public class JsMutationObserver
{
    private readonly Engine _engine;
    private readonly JsValue _callback;

    internal sealed record Target(LayoutNode Node, bool ChildList, bool Attributes, bool CharacterData,
        bool Subtree, bool AttributeOldValue, bool CharacterDataOldValue);

    internal readonly List<Target> Targets = [];
    internal readonly List<JsMutationRecord> Queue = [];

    public JsMutationObserver(JsValue callback)
    {
        _engine = JsEngine.Instance!.RawEngine;
        _callback = callback;
        MutationObserverRegistry.Register(this);
    }

    public void observe(JsElement target, JsValue? options = null)
    {
        bool childList = false, attributes = false, characterData = false,
             subtree = false, attrOld = false, cdOld = false;
        if (options is not null && options.IsObject())
        {
            var o = options.AsObject();
            bool Get(string k, bool dflt = false)
            {
                var v = o.Get(k);
                return v.IsBoolean() ? v.AsBoolean() : dflt;
            }
            childList = Get("childList");
            subtree = Get("subtree");
            attrOld = Get("attributeOldValue");
            cdOld = Get("characterDataOldValue");
            // attributes/characterData default to true if their *OldValue is set.
            var ao = o.Get("attributes");
            attributes = ao.IsBoolean() ? ao.AsBoolean() : attrOld;
            var cd = o.Get("characterData");
            characterData = cd.IsBoolean() ? cd.AsBoolean() : cdOld;
        }
        // Re-observing the same node replaces its options.
        Targets.RemoveAll(t => ReferenceEquals(t.Node, target.Node));
        Targets.Add(new Target(target.Node, childList, attributes, characterData, subtree, attrOld, cdOld));
    }

    public void disconnect()
    {
        Targets.Clear();
        Queue.Clear();
    }

    public JsMutationRecord[] takeRecords()
    {
        var records = Queue.ToArray();
        Queue.Clear();
        return records;
    }

    /// <summary>Invokes the callback with the queued records (called at the microtask checkpoint).</summary>
    internal void Deliver()
    {
        if (Queue.Count == 0) return;
        var records = Queue.ToArray();
        Queue.Clear();
        try
        {
            var arr = JsValue.FromObject(_engine, records);
            _engine.Invoke(_callback, arr, JsValue.FromObject(_engine, this));
        }
        catch (Exception ex) { Console.WriteLine($"[MutationObserver] {ex.Message}"); }
    }
}

/// <summary>
/// Central hub: DOM mutation methods notify it; it matches mutations against registered
/// observers (honoring subtree) and queues MutationRecords, delivered at the microtask
/// checkpoint (<see cref="JsEngine.FlushMicrotasks"/>).
/// </summary>
internal static class MutationObserverRegistry
{
    private static readonly List<JsMutationObserver> _observers = [];

    /// <summary>Cleared when a new engine/page is created.</summary>
    public static void Reset() => _observers.Clear();

    public static void Register(JsMutationObserver observer) => _observers.Add(observer);

    public static bool HasObservers => _observers.Count > 0;

    private static bool Observes(JsMutationObserver.Target t, LayoutNode node)
    {
        if (ReferenceEquals(t.Node, node)) return true;
        if (!t.Subtree) return false;
        for (var n = node.Parent; n != null; n = n.Parent)
            if (ReferenceEquals(t.Node, n)) return true;
        return false;
    }

    public static void NotifyChildList(Engine engine, LayoutNode parent,
        List<LayoutNode>? added, List<LayoutNode>? removed, LayoutNode? prev, LayoutNode? next)
    {
        if (_observers.Count == 0) return;
        foreach (var obs in _observers)
            foreach (var t in obs.Targets)
                if (t.ChildList && Observes(t, parent))
                {
                    obs.Queue.Add(new JsMutationRecord(engine, "childList", parent, added, removed, prev, next));
                    break;
                }
    }

    public static void NotifyAttribute(Engine engine, LayoutNode node, string attributeName, string? oldValue)
    {
        if (_observers.Count == 0) return;
        foreach (var obs in _observers)
            foreach (var t in obs.Targets)
                if (t.Attributes && Observes(t, node))
                {
                    obs.Queue.Add(new JsMutationRecord(engine, "attributes", node,
                        attributeName: attributeName, oldValue: t.AttributeOldValue ? oldValue : null));
                    break;
                }
    }

    public static void NotifyCharacterData(Engine engine, LayoutNode node, string? oldValue)
    {
        if (_observers.Count == 0) return;
        foreach (var obs in _observers)
            foreach (var t in obs.Targets)
                if (t.CharacterData && Observes(t, node))
                {
                    obs.Queue.Add(new JsMutationRecord(engine, "characterData", node,
                        oldValue: t.CharacterDataOldValue ? oldValue : null));
                    break;
                }
    }

    /// <summary>Delivers all queued records to their observers. Runs at the microtask checkpoint.</summary>
    public static void DeliverAll()
    {
        // Snapshot — a callback may mutate and queue more (delivered on the next checkpoint).
        foreach (var obs in _observers.ToArray())
            obs.Deliver();
    }
}
