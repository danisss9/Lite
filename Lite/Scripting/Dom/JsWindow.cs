using Lite.Scripting.Runtime;
using Lite.Models;

namespace Lite.Scripting.Dom;

internal class JsWindow
{
    private readonly JsEngine _engine;
    private int _nextTimerId = 1;

    // Timers share ONE OS timer armed at the earliest deadline. Per-timer timers fired from
    // independent pool threads, so a 20 ms timer could enqueue its callback before a 10 ms one
    // that was merely delayed by load: the WPT window.onerror cases assert the 10 ms error ran
    // before the 20 ms check and failed intermittently on busy shards. Deadlines are drained in
    // (due, id) order on the UI thread instead.
    private sealed class TimerEntry
    {
        public required int Id { get; init; }
        public required long Due { get; set; }
        public required int Period { get; init; }
        public required JsValue Fn { get; init; }
        public bool OneShot => Period <= 0;
    }
    private readonly Dictionary<int, TimerEntry> _timers = [];
    private readonly object _timerLock = new();
    private System.Threading.Timer? _timer;

    // Viewport dimensions
    public int innerWidth { get; set; }
    public int innerHeight { get; set; }

    // Navigation objects (same instances as the globals location/history/navigator).
    public JsLocation location => _engine.Location;
    public JsHistory history => _engine.History;
    public JsNavigator navigator { get; } = new();

    // requestAnimationFrame
    private int _nextRafId = 1;
    private readonly List<(int Id, JsValue Fn)> _rafCallbacks = [];
    internal bool HasPendingRAF => _rafCallbacks.Count > 0;

    // Window-level event listeners (load, hashchange, popstate, message, error, ...). The
    // capture flag participates in the dispatch path: the window is the first object of every
    // node-targeted event path (DOM §2.9), firing capture listeners in capture and bubble
    // listeners (plus the on{type} property) in bubble.
    private readonly List<EventListenerEntry> _listeners = [];

    public JsWindow(JsEngine engine, int viewportWidth = 800, int viewportHeight = 600)
    {
        _engine = engine;
        innerWidth = viewportWidth;
        innerHeight = viewportHeight;
    }

    public void alert(object? message) => Lite.Platform.PlatformDialogs.Alert(message?.ToString() ?? "");

    /// <summary>window.open — the popup path. A sandboxed page without allow-popups gets null
    /// (HTML §6.5.4). The headless environment has no popup surface, so the unsandboxed form
    /// also returns null (the navigation machinery records the gap); the sandbox checks are
    /// what the conformance cluster measures.</summary>
    public JsWindowProxy? open(string? url = null, string? target = null, string? features = null)
    {
        // A sandboxed page without allow-popups is blocked outright (HTML §6.5.4). The headless
        // environment has no popup surface, so every request resolves to null — the sandbox
        // evaluation is the enforcement the conformance cluster measures.
        return null;
    }

    // ---- window-level events ----
    private static bool CaptureOf(JsValue? options) => options switch
    {
        null => false,
        _ when options.IsBoolean() => options.AsBoolean(),
        _ when options.IsObject() => options.AsObject().Get("capture") is { } c && c.IsBoolean() && c.AsBoolean(),
        _ => false,
    };

    public void addEventListener(string type, JsValue fn, JsValue? options = null)
    {
        if (fn is null || fn.IsUndefined()) return;
        _listeners.Add(new EventListenerEntry(type, fn, null, CaptureOf(options)));
    }

    public void removeEventListener(string type, JsValue fn, JsValue? options = null)
    {
        var capture = CaptureOf(options);
        _listeners.RemoveAll(l => l.EventType == type && Equals(l.Handler, fn) && l.Capture == capture);
    }

    /// <summary>The window's slice of a node-targeted dispatch path (DOM §2.9): capture
    /// listeners during capture, bubble listeners plus the on{type} property during bubble.</summary>
    internal bool InvokePathListeners(string type, JsEvent evt, bool capturePhase, JsEngine engine)
    {
        var handled = false;
        foreach (var listener in _listeners.ToList())
        {
            if (listener.EventType != type) continue;
            if (listener.Capture != capturePhase) continue;
            if (evt.ImmediatePropagationStopped) break;
            if (!_listeners.Contains(listener)) continue;
            try { engine.RawEngine.Invoke(listener.Handler, evt); }
            catch (Exception ex) { engine.ReportListenerError(ex); }
            handled = true;
        }
        if (!capturePhase && !evt.ImmediatePropagationStopped)
            handled |= InvokeWindowOnProperty(type, JsValue.FromObject(engine.RawEngine, evt));
        return handled;
    }

    /// <summary>Invokes the window's on{type} property handler, if one is set and callable.
    /// <paramref name="evt"/> is whatever the dispatch carries (a JsEvent for path dispatches,
    /// an anonymous event object for the window's own named dispatches).</summary>
    private bool InvokeWindowOnProperty(string type, JsValue evt)
    {
        var property = _engine.RawEngine.GetValue("on" + type);
        if (property.IsUndefined() || property.IsNull() || !property.IsCallable()) return false;
        try
        {
            _engine.RawEngine.Invoke(property, evt);
            return true;
        }
        catch (Exception ex) { _engine.ReportListenerError(ex); return true; }
    }

    /// <summary>Dispatches a window-level event to registered listeners and to the
    /// matching <c>on{type}</c> property if set. <paramref name="evt"/> is the event object
    /// passed to handlers (a JsEvent or plain object).</summary>
    public bool dispatchEvent(JsValue evt)
    {
        var type = evt.IsObject() && evt.AsObject().Get("type") is { } t && !t.IsUndefined()
            ? t.AsString() : "";
        DispatchInternal(type, evt);
        return true;
    }

    /// <summary>Fires a window event by name, building a minimal event object for handlers.</summary>
    internal void DispatchEvent(string type)
    {
        var evt = JsValue.FromObject(_engine.RawEngine, new { type, target = (object?)null });
        DispatchInternal(type, evt);
    }

    /// <summary>Fires a window event by name with a pre-built event object (e.g. a JsEvent).</summary>
    internal void DispatchEvent(string type, JsValue evt) => DispatchInternal(type, evt);

    /// <summary>HTML postMessage on a window: a same-window post schedules a <c>message</c> event
    /// task on this window (browsers deliver it asynchronously — scripts use it for scheduling).
    /// Cross-context posts go through the iframe WindowProxies, which carry both origins.</summary>
    public void postMessage(JsValue message, JsValue? targetOrigin = null,
        JsValue? transfer = null)
    {
        _engine.CheckPostMessageTargetOrigin(targetOrigin);
        var ports = JsMessagePort.TransferPorts(transfer, _engine.MessagePortContext);
        var cloned = StructuredClone.Clone(_engine.RawEngine, message, _engine.RawEngine);
        _engine.EnqueueMacrotask(() => _engine.DeliverMessage(cloned, _engine.Origin,
            _engine.GetWindowProxy(_engine), ports));
    }

    private void DispatchInternal(string type, JsValue evt)
    {
        // The window AS TARGET (load, hashchange, popstate, direct dispatchEvent): every
        // listener fires in registration order regardless of its capture flag (DOM §2.9
        // at-target), then the on{type} property. "error" listener exceptions are not
        // re-reported (the dispatch itself is the report), so an always-throwing error
        // handler cannot recurse forever.
        foreach (var listener in _listeners.ToList())
        {
            if (listener.EventType != type) continue;
            try { _engine.RawEngine.Invoke(listener.Handler, evt); }
            catch (Exception ex)
            {
                if (type != "error") _engine.ReportListenerError(ex);
                else Console.WriteLine($"[JS window {type}] {ex.Message}");
            }
        }
        if (type == "error" && evt.ToObject() is Dom.JsEvent errorEvent)
        {
            // The legacy error handler may cancel the default report by returning true,
            // exactly like preventDefault() on the event.
            var property = _engine.RawEngine.GetValue("on" + type);
            if (property.IsCallable())
            {
                try
                {
                    var result = _engine.RawEngine.Invoke(property, evt.Get("message"), evt.Get("filename"),
                        evt.Get("lineno"), evt.Get("colno"), evt.Get("error"));
                    if (result.IsBoolean() && result.AsBoolean()) errorEvent.preventDefault();
                }
                catch (Exception ex) { Console.WriteLine($"[JS window {type}] {ex.Message}"); }
            }
            return;
        }
        InvokeWindowOnProperty(type, evt);
    }

    internal bool HasListener(string type) => _listeners.Any(l => l.EventType == type);

    public int setTimeout(JsValue fn, int delay = 0) => AddTimer(fn, Math.Max(0, delay), period: 0);

    public int setInterval(JsValue fn, int delay = 0) => AddTimer(fn, Math.Max(1, delay), period: Math.Max(1, delay));

    private int AddTimer(JsValue fn, int delayMs, int period)
    {
        var id = _nextTimerId++;
        lock (_timerLock)
        {
            _timers[id] = new TimerEntry
            {
                Id = id,
                Due = Environment.TickCount64 + delayMs,
                Period = period,
                Fn = fn,
            };
            ArmTimerLocked();
        }
        return id;
    }

    /// <summary>Arms the one OS timer at the earliest pending deadline (caller holds the lock).</summary>
    private void ArmTimerLocked()
    {
        if (_timers.Count == 0)
        {
            _timer?.Dispose();
            _timer = null;
            return;
        }
        var next = _timers.Values.Min(t => t.Due);
        var due = Math.Max(0, next - Environment.TickCount64);
        if (_timer is null)
            _timer = new System.Threading.Timer(_ => _engine.EnqueueMacrotask(ProcessDueTimers), null, due, Timeout.Infinite);
        else
            _timer.Change(due, Timeout.Infinite);
    }

    /// <summary>Runs every timer due now, in (due, id) order, on the UI thread. Each callback gets
    /// its own error containment and microtask checkpoint, so a throwing timer cannot skip the
    /// rest of the due batch.</summary>
    private void ProcessDueTimers()
    {
        List<JsValue> due;
        lock (_timerLock)
        {
            var now = Environment.TickCount64;
            due = _timers.Values
                .Where(t => t.Due <= now)
                .OrderBy(t => t.Due).ThenBy(t => t.Id)
                .Select(t => t.Fn)
                .ToList();
            foreach (var timer in _timers.Values.Where(t => t.Due <= now).ToList())
            {
                if (timer.OneShot) _timers.Remove(timer.Id);
                else timer.Due = now + timer.Period;
            }
            ArmTimerLocked();
        }
        foreach (var fn in due) _engine.InvokeTimerCallback(fn);
    }

    public void clearTimeout(int id)
    {
        lock (_timerLock)
        {
            if (_timers.Remove(id)) ArmTimerLocked();
        }
    }

    public void clearInterval(int id) => clearTimeout(id);

    internal void StopTimers()
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
            _timers.Clear();
        }
        _rafCallbacks.Clear();
    }

    // ---- getComputedStyle (Phase 7) ----
    public JsComputedStyle getComputedStyle(JsElement element, string? pseudoElement = null)
    {
        // getComputedStyle reports used values for layout properties (margins/padding/width/...),
        // so the box must be laid out first.
        JsEngine.Instance?.EnsureLayout();
        return new JsComputedStyle(element.Node);
    }

    // ---- requestAnimationFrame / cancelAnimationFrame ----
    public int requestAnimationFrame(JsValue fn)
    {
        var id = _nextRafId++;
        _rafCallbacks.Add((id, fn));
        return id;
    }

    public void cancelAnimationFrame(int id)
    {
        _rafCallbacks.RemoveAll(r => r.Id == id);
    }

    // ---- scroll methods ----
    public void scrollTo(int x, int y)
    {
        _engine.ScrollTo(y);
    }

    public void scrollBy(int x, int y)
    {
        _engine.ScrollBy(y);
    }

    public double scrollX => 0;
    public double scrollY => _engine.GetScrollY();
    public double pageXOffset => 0;
    public double pageYOffset => _engine.GetScrollY();

    /// <summary>Invokes all pending RAF callbacks with the given timestamp and clears the queue.</summary>
    internal void FlushRAF(double timestamp)
    {
        if (_rafCallbacks.Count == 0) return;
        var callbacks = _rafCallbacks.ToList();
        _rafCallbacks.Clear();
        foreach (var (_, fn) in callbacks)
        {
            try { _engine.RawEngine.Invoke(fn, timestamp); }
            catch (Exception ex) { Console.WriteLine($"[JS RAF] {ex.Message}"); }
        }
    }
}
