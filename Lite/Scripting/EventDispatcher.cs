using Lite.Scripting.Runtime;
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
        evt.Init(eventType, true, true);
        evt.target = JsElement.For(engine.RawEngine, node);

        return DispatchEvent(node, evt, engine);
    }

    /// <summary>Dispatches a simple named event to a specific node through the given engine
    /// (e.g. an iframe's <c>load</c>). Builds the JsEvent and runs the full propagation path.
    /// <paramref name="trusted"/> marks user-agent-generated events — focus/blur fired by the
    /// focus() API or by interaction report <c>isTrusted</c> true, like real browsers.</summary>
    internal static bool DispatchToNode(LayoutNode node, string eventType, JsEngine engine, bool bubbles = false, bool trusted = false)
    {
        var evt = new JsEvent();
        evt.Init(eventType, bubbles, false);
        evt.isTrusted = trusted;
        evt.target = JsElement.For(engine.RawEngine, node);
        return DispatchEvent(node, evt, engine);
    }

    /// <summary>
    /// Dispatches a JsEvent through the full propagation path (DOM §2.9): window → document →
    /// the node ancestors root-first → the target, capture then at-target then bubble. The
    /// window and document are virtual path entries — they exist only when the target is
    /// attached to the engine's document tree, and each carries its own listener store.
    /// </summary>
    internal static bool DispatchEvent(LayoutNode targetNode, JsEvent evt, JsEngine engine)
    {
        // Dispatch flag (DOM §2.9): an event already travelling its path can't be re-dispatched.
        if (evt.Dispatching)
            throw JsErrors.Dom("InvalidStateError", "Failed to execute 'dispatchEvent': The event is already being dispatched.");

        var handled = false;
        var eventType = evt.type;
        evt.Dispatching = true;

        // The target is on the document's node tree only if the ancestor walk reaches the
        // document's root; detached fragments dispatch without the window/document entries.
        var document = engine.DocumentFacade;
        var attached = document is not null &&
            WalkToRoot(targetNode, document.RootNode);

        // Build ancestor chain (root → ... → parent)
        var ancestors = new List<LayoutNode>();
        for (var p = targetNode.Parent; p != null; p = p.Parent)
            ancestors.Add(p);
        ancestors.Reverse(); // root-first

        // Phase 1: CAPTURING (window → document → root → parent)
        evt.eventPhase = 1;
        if (attached && !evt.PropagationStopped)
        {
            evt.currentTarget = engine.RawEngine.GetValue("window");
            if (engine.WindowPathListeners(eventType, evt, capture: true))
                handled = true;
        }
        if (attached && !evt.PropagationStopped)
        {
            evt.currentTarget = engine.RawEngine.GetValue("document");
            if (document!.InvokePathListeners(eventType, evt, capturePhase: true))
                handled = true;
        }
        foreach (var ancestor in ancestors)
        {
            if (evt.PropagationStopped) break;
            evt.currentTarget = JsElement.For(engine.RawEngine, ancestor);
            if (InvokeListeners(ancestor, eventType, evt, engine, capturePhase: true))
                handled = true;
        }

        // Phase 2: AT_TARGET — capture listeners first, then non-capture, each in registration
        // order (the pinned DOM4-era dispatch contract: dom/events/EventTarget-dispatchEvent's
        // [1, 3, 2] ordering).
        if (!evt.PropagationStopped)
        {
            evt.eventPhase = 2;
            evt.currentTarget = evt.target;
            if (InvokeListeners(targetNode, eventType, evt, engine, capturePhase: true))
                handled = true;
            if (!evt.ImmediatePropagationStopped && InvokeListeners(targetNode, eventType, evt, engine, capturePhase: false))
                handled = true;
            // Inline handler
            if (!evt.ImmediatePropagationStopped && InvokeHandlerAttribute(targetNode, eventType, evt, engine))
                handled = true;
        }

        // Phase 3: BUBBLING (parent → root → document → window)
        if (evt.bubbles && !evt.PropagationStopped)
        {
            evt.eventPhase = 3;
            for (int i = ancestors.Count - 1; i >= 0; i--)
            {
                if (evt.PropagationStopped) break;
                var ancestor = ancestors[i];
                evt.currentTarget = JsElement.For(engine.RawEngine, ancestor);
                if (InvokeListeners(ancestor, eventType, evt, engine, capturePhase: false))
                    handled = true;
                // Inline handler on ancestors during bubbling
                if (!evt.ImmediatePropagationStopped && InvokeHandlerAttribute(ancestor, eventType, evt, engine))
                    handled = true;
            }
            if (attached && !evt.PropagationStopped)
            {
                evt.currentTarget = engine.RawEngine.GetValue("document");
                if (document!.InvokePathListeners(eventType, evt, capturePhase: false))
                    handled = true;
            }
            if (attached && !evt.PropagationStopped && !evt.ImmediatePropagationStopped)
            {
                evt.currentTarget = engine.RawEngine.GetValue("window");
                if (engine.WindowPathListeners(eventType, evt, capture: false))
                    handled = true;
            }
        }

        evt.eventPhase = 0;
        evt.Dispatching = false;
        // The propagation flags describe the dispatch, not the event object: cancelBubble and
        // stop-immediate read false once the path completes (dom/events/Event-cancelBubble).
        evt.ResetPropagationFlags();
        // Run Promise continuations scheduled by the handlers (microtask checkpoint).
        engine.FlushMicrotasks();
        return handled;
    }

    private static bool WalkToRoot(LayoutNode node, LayoutNode root)
    {
        for (var p = (LayoutNode?)node; p != null; p = p.Parent)
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    /// <summary>Runs <paramref name="node"/>'s <c>on&lt;type&gt;</c> content attribute, if it has one.
    /// The attribute value is a function body taking the event as its <c>event</c> parameter, with
    /// <c>this</c> bound to the element (HTML §8.1.5.1).</summary>
    private static bool InvokeHandlerAttribute(LayoutNode node, string eventType, JsEvent evt, JsEngine engine)
    {
        if (!node.Attributes.TryGetValue("on" + eventType, out var inlineCode)) return false;
        engine.ExecuteEventHandler(inlineCode, JsValue.FromObject(engine.RawEngine, JsElement.For(engine.RawEngine, node)),
            JsValue.FromObject(engine.RawEngine, evt));
        return true;
    }

    private static bool InvokeListeners(LayoutNode node, string eventType, JsEvent evt, JsEngine engine, bool capturePhase)
    {
        var handled = false;
        foreach (var listener in node.EventListeners.ToList()) // ToList to allow modification during iteration
        {
            if (listener.EventType != eventType) continue;
            if (listener.Capture != capturePhase) continue;
            if (evt.ImmediatePropagationStopped) break;

            // The snapshot is taken at phase start, but DOM "inner invoke" checks the live list:
            // a listener removed by an earlier listener (or a nested dispatch) must not run.
            // Reference identity — a remove + re-add during dispatch is a new listener that was
            // not in the snapshot, so neither the old nor the new entry fires this dispatch.
            if (!node.EventListeners.Any(l => ReferenceEquals(l, listener))) continue;

            // A `once` listener is removed before it runs, so re-entrant dispatch can't re-invoke it.
            if (listener.Once) node.EventListeners.Remove(listener);

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

    internal static LayoutNode? Find(LayoutNode? root, Guid key)
    {
        if (root is null) return null;
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.NodeKey == key) return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
        return null;
    }
}
