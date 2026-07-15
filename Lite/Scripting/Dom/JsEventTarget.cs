using Jint;
using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>
/// A standalone <c>EventTarget</c> (created via <c>new EventTarget()</c>) — the base interface, not a
/// DOM node. Backs addEventListener / removeEventListener / dispatchEvent with a private listener
/// list. Since it has no place in the node tree there is no capture/bubble path: every matching
/// listener fires in the AT_TARGET phase (capture flag is still honored for add/remove identity).
/// Honors <c>once</c> (removed before invocation) and the event's dispatch flag (re-entrant dispatch
/// of the same event throws InvalidStateError).
/// </summary>
public class JsEventTarget
{
    private readonly record struct Listener(string Type, JsValue Handler, bool Capture, bool Once);
    private readonly List<Listener> _listeners = new();

    public JsEventTarget() { }

    public void addEventListener(string type, JsValue? handler, JsValue? options = null)
    {
        if (handler is null || handler.IsUndefined() || handler.IsNull()) return;
        var (capture, once) = ParseOptions(options, readOnce: true);
        // DOM: a duplicate (type, callback, capture) is not added again.
        if (_listeners.Any(l => l.Type == type && Equals(l.Handler, handler) && l.Capture == capture)) return;
        _listeners.Add(new Listener(type, handler, capture, once));
    }

    public void removeEventListener(string type, JsValue? handler, JsValue? options = null)
    {
        if (handler is null) return;
        var (capture, _) = ParseOptions(options, readOnce: false);
        _listeners.RemoveAll(l => l.Type == type && Equals(l.Handler, handler) && l.Capture == capture);
    }

    public bool dispatchEvent(JsEvent? evt = null)
    {
        if (evt is null)
            throw JsErrors.Native("TypeError",
                "Failed to execute 'dispatchEvent': parameter 1 is not of type 'Event'.");
        if (!evt.Initialized)
            throw JsErrors.Dom("InvalidStateError",
                "Failed to execute 'dispatchEvent': The event provided is uninitialized.");
        if (evt.Dispatching)
            throw JsErrors.Dom("InvalidStateError", "Failed to execute 'dispatchEvent': The event is already being dispatched.");

        var raw = JsEngine.Instance?.RawEngine;
        evt.Dispatching = true;
        evt.eventPhase = 2; // AT_TARGET
        try
        {
            foreach (var l in _listeners.ToList())
            {
                if (l.Type != evt.type) continue;
                if (evt.ImmediatePropagationStopped) break;
                // DOM "inner invoke": a listener removed during this dispatch must not run.
                // (Add dedupes, so value equality identifies the entry.)
                if (!_listeners.Contains(l)) continue;
                if (l.Once) _listeners.RemoveAll(x => x.Equals(l)); // remove before invoking (spec §2.9)
                try { raw?.Invoke(l.Handler, evt); }
                catch (Exception ex) { Console.WriteLine($"[JS EventListener] {ex.Message}"); }
            }
        }
        finally
        {
            evt.eventPhase = 0;
            evt.Dispatching = false;
        }
        return !evt.DefaultPrevented;
    }

    private static (bool Capture, bool Once) ParseOptions(JsValue? options, bool readOnce)
    {
        bool capture = false, once = false;
        if (options is not null)
        {
            if (options.IsBoolean()) capture = options.AsBoolean();
            else if (options.IsObject())
            {
                var o = options.AsObject();
                var c = o.Get("capture"); if (c.IsBoolean()) capture = c.AsBoolean();
                if (readOnce) { var on = o.Get("once"); if (on.IsBoolean()) once = on.AsBoolean(); }
            }
        }
        return (capture, once);
    }
}
