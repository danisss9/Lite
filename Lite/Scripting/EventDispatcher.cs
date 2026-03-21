using Jint.Native;
using Lite.Models;
using Lite.Scripting.Dom;

namespace Lite.Scripting;

/// <summary>
/// Central event dispatcher with full DOM capturing → at-target → bubbling phases.
/// </summary>
internal static class EventDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="eventType"/> to the node identified by
    /// <paramref name="nodeKey"/>. Also executes inline on* attribute code
    /// (e.g. onclick="...") through the active <see cref="JsEngine"/>.
    /// Returns <c>true</c> if at least one handler ran (caller should redraw).
    /// </summary>
    internal static bool Dispatch(Guid nodeKey, string eventType, LayoutNode? root)
    {
        var node = Find(root, nodeKey);
        if (node is null) return false;

        var engine = JsEngine.Instance;
        if (engine is null) return false;

        var evt = new JsEvent();
        evt.initEvent(eventType.ToLowerInvariant(), true, true);
        evt.target = new JsElement(engine.RawEngine, node);

        return DispatchEvent(node, evt, engine);
    }

    /// <summary>
    /// Dispatches a JsEvent through the full capture → target → bubble path.
    /// </summary>
    internal static bool DispatchEvent(LayoutNode targetNode, JsEvent evt, JsEngine engine)
    {
        var handled = false;
        var eventType = evt.type;

        // Build ancestor chain (root → ... → parent)
        var ancestors = new List<LayoutNode>();
        for (var p = targetNode.Parent; p != null; p = p.Parent)
            ancestors.Add(p);
        ancestors.Reverse(); // root-first

        // Phase 1: CAPTURING (root → parent)
        evt.eventPhase = 1;
        foreach (var ancestor in ancestors)
        {
            if (evt.PropagationStopped) break;
            evt.currentTarget = new JsElement(engine.RawEngine, ancestor);
            if (InvokeListeners(ancestor, eventType, evt, engine, capturePhase: true))
                handled = true;
        }

        // Phase 2: AT_TARGET
        if (!evt.PropagationStopped)
        {
            evt.eventPhase = 2;
            evt.currentTarget = evt.target;
            // Fire both capture and bubble listeners at target
            if (InvokeListeners(targetNode, eventType, evt, engine, capturePhase: true))
                handled = true;
            if (!evt.ImmediatePropagationStopped && InvokeListeners(targetNode, eventType, evt, engine, capturePhase: false))
                handled = true;
            // Inline handler
            if (!evt.ImmediatePropagationStopped)
            {
                var attrKey = "on" + eventType;
                if (targetNode.Attributes.TryGetValue(attrKey, out var inlineCode))
                {
                    engine.Execute(inlineCode);
                    handled = true;
                }
            }
        }

        // Phase 3: BUBBLING
        if (evt.bubbles && !evt.PropagationStopped)
        {
            evt.eventPhase = 3;
            for (int i = ancestors.Count - 1; i >= 0; i--)
            {
                if (evt.PropagationStopped) break;
                var ancestor = ancestors[i];
                evt.currentTarget = new JsElement(engine.RawEngine, ancestor);
                if (InvokeListeners(ancestor, eventType, evt, engine, capturePhase: false))
                    handled = true;
                // Inline handler on ancestors during bubbling
                if (!evt.ImmediatePropagationStopped)
                {
                    var attrKey = "on" + eventType;
                    if (ancestor.Attributes.TryGetValue(attrKey, out var inlineCode))
                    {
                        engine.Execute(inlineCode);
                        handled = true;
                    }
                }
            }
        }

        evt.eventPhase = 0;
        return handled;
    }

    private static bool InvokeListeners(LayoutNode node, string eventType, JsEvent evt, JsEngine engine, bool capturePhase)
    {
        var handled = false;
        foreach (var listener in node.EventListeners.ToList()) // ToList to allow modification during iteration
        {
            if (listener.EventType != eventType) continue;
            if (listener.Capture != capturePhase) continue;
            if (evt.ImmediatePropagationStopped) break;

            try
            {
                if (listener.Handler is not null)
                    engine.RawEngine.Invoke(listener.Handler, evt);
                else
                    listener.LegacyHandler?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JS EventListener] {ex.Message}");
            }
            handled = true;
        }
        return handled;
    }

    internal static LayoutNode? Find(LayoutNode? node, Guid key)
    {
        if (node is null) return null;
        if (node.NodeKey == key) return node;
        foreach (var child in node.Children)
        {
            var result = Find(child, key);
            if (result is not null) return result;
        }
        return null;
    }
}
