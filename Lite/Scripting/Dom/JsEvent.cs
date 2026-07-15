using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>DOM Event object exposed to JavaScript. Also serves as MouseEvent /
/// KeyboardEvent / CustomEvent (a superset of their properties).</summary>
public class JsEvent
{
    public JsEvent() { }

    /// <summary>new Event(type) / new CustomEvent(type).</summary>
    public JsEvent(string type) => this.type = type;

    /// <summary>new Event(type, { bubbles, cancelable }) / new CustomEvent(type, { detail }).</summary>
    public JsEvent(string type, JsValue options)
    {
        this.type = type;
        if (options.IsObject())
        {
            var o = options.AsObject();
            var b = o.Get("bubbles"); if (b.IsBoolean()) bubbles = b.AsBoolean();
            var c = o.Get("cancelable"); if (c.IsBoolean()) cancelable = c.AsBoolean();
            var d = o.Get("detail"); if (!d.IsUndefined() && !d.IsNull()) detail = d;

            // MouseEvent / WheelEvent / PointerEvent init dictionaries (a superset is read here).
            void Num(string k, Action<float> set) { var v = o.Get(k); if (v.IsNumber()) set((float)v.AsNumber()); }
            void Flag(string k, Action<bool> set) { var v = o.Get(k); if (v.IsBoolean()) set(v.AsBoolean()); }
            void Str(string k, Action<string> set) { var v = o.Get(k); if (v.IsString()) set(v.AsString()); }

            Num("clientX", v => clientX = v); Num("clientY", v => clientY = v);
            Num("button", v => button = (int)v);
            Flag("ctrlKey", v => ctrlKey = v); Flag("shiftKey", v => shiftKey = v);
            Flag("altKey", v => altKey = v); Flag("metaKey", v => metaKey = v);
            Num("deltaX", v => deltaX = v); Num("deltaY", v => deltaY = v);
            Num("deltaZ", v => deltaZ = v); Num("deltaMode", v => deltaMode = (int)v);
            Num("pointerId", v => pointerId = (int)v);
            Str("pointerType", v => pointerType = v);
            Flag("isPrimary", v => isPrimary = v);
            Num("pressure", v => pressure = v);
        }
    }

    public string type { get; internal set; } = string.Empty;
    public bool bubbles { get; internal set; }
    public bool cancelable { get; internal set; }

    /// <summary>CustomEvent payload.</summary>
    public object? detail { get; internal set; }
    public JsElement? target { get; internal set; }
    public JsElement? currentTarget { get; internal set; }
    public int eventPhase { get; internal set; } // 0=NONE, 1=CAPTURING, 2=AT_TARGET, 3=BUBBLING

    // Constants
    public int NONE { get; } = 0;
    public int CAPTURING_PHASE { get; } = 1;
    public int AT_TARGET { get; } = 2;
    public int BUBBLING_PHASE { get; } = 3;

    // Mouse event properties
    public float clientX { get; internal set; }
    public float clientY { get; internal set; }
    public float pageX { get; internal set; }
    public float pageY { get; internal set; }
    public int button { get; internal set; }

    // PopStateEvent / HashChangeEvent properties
    public JsValue? state { get; internal set; }
    public string oldURL { get; internal set; } = string.Empty;
    public string newURL { get; internal set; } = string.Empty;

    // WheelEvent properties (deltaMode: 0=pixel, 1=line, 2=page)
    public float deltaX { get; internal set; }
    public float deltaY { get; internal set; }
    public float deltaZ { get; internal set; }
    public int deltaMode { get; internal set; }
    public int DOM_DELTA_PIXEL { get; } = 0;
    public int DOM_DELTA_LINE { get; } = 1;
    public int DOM_DELTA_PAGE { get; } = 2;

    // PointerEvent properties
    public int pointerId { get; internal set; }
    public string pointerType { get; internal set; } = "mouse";
    public bool isPrimary { get; internal set; } = true;
    public float pressure { get; internal set; }
    public float width { get; internal set; } = 1;
    public float height { get; internal set; } = 1;

    // Keyboard event properties
    public string key { get; internal set; } = string.Empty;
    public int keyCode { get; internal set; }
    public string code { get; internal set; } = string.Empty;
    public bool ctrlKey { get; internal set; }
    public bool shiftKey { get; internal set; }
    public bool altKey { get; internal set; }
    public bool metaKey { get; internal set; }

    internal bool DefaultPrevented { get; private set; }
    internal bool PropagationStopped { get; private set; }
    internal bool ImmediatePropagationStopped { get; private set; }

    /// <summary>The "dispatch flag": set while the event travels its propagation path. While set,
    /// <see cref="initEvent"/> is a no-op and re-dispatching the same event throws InvalidStateError.</summary>
    internal bool Dispatching { get; set; }

    /// <summary>The "initialized flag" (DOM §2.9): constructor-built events are born initialized,
    /// but <c>document.createEvent(...)</c> events are not until <see cref="initEvent"/> runs —
    /// dispatching an uninitialized event throws InvalidStateError.</summary>
    internal bool Initialized { get; set; } = true;

    public bool defaultPrevented => DefaultPrevented;

    /// <summary>Untrusted for script-created events; only user-agent-generated events are trusted.</summary>
    public bool isTrusted { get; internal set; }

    /// <summary>Legacy alias for <see cref="target"/>.</summary>
    public JsElement? srcElement => target;

    /// <summary>Legacy accessor for the stop-propagation flag (true once stopPropagation ran).</summary>
    public bool cancelBubble
    {
        get => PropagationStopped;
        set { if (value) stopPropagation(); }
    }

    /// <summary>Legacy IE-style flag: reads !defaultPrevented; setting it false cancels the event.</summary>
    public bool returnValue
    {
        get => !DefaultPrevented;
        set { if (!value) preventDefault(); }
    }

    public void preventDefault()
    {
        if (cancelable) DefaultPrevented = true;
    }

    public void stopPropagation() => PropagationStopped = true;

    public void stopImmediatePropagation()
    {
        PropagationStopped = true;
        ImmediatePropagationStopped = true;
    }

    /// <summary>Legacy Event.initEvent(type, bubbles?, cancelable?). Per DOM §2.9: the type arg is
    /// mandatory (0 args → TypeError), the call is a no-op while the event is being dispatched, and it
    /// resets the stop-propagation / stop-immediate / canceled flags. <paramref name="typeArg"/> is a
    /// JsValue so a missing first arg is detectable; the bool args are JsValue? so they coerce (and
    /// default to false when omitted).</summary>
    public void initEvent(JsValue? typeArg = null, JsValue? bubblesArg = null, JsValue? cancelableArg = null)
    {
        if (typeArg is null || typeArg.IsUndefined())
            throw JsErrors.Native("TypeError", "Failed to execute 'initEvent' on 'Event': 1 argument required, but only 0 present.");
        if (Dispatching) return; // dispatch flag set: the setter must short-circuit.

        type = TypeConverter.ToString(typeArg);
        bubbles = bubblesArg is not null && TypeConverter.ToBoolean(bubblesArg);
        cancelable = cancelableArg is not null && TypeConverter.ToBoolean(cancelableArg);
        Initialized = true;
        DefaultPrevented = false;
        PropagationStopped = false;
        ImmediatePropagationStopped = false;
    }

    /// <summary>Internal, strongly-typed initializer used by host dispatch code (not the JS surface).</summary>
    internal void Init(string typeArg, bool bubblesArg = false, bool cancelableArg = false)
    {
        type = typeArg;
        bubbles = bubblesArg;
        cancelable = cancelableArg;
        Initialized = true;
        DefaultPrevented = false;
        PropagationStopped = false;
        ImmediatePropagationStopped = false;
    }

    /// <summary>Legacy CustomEvent.initCustomEvent(type, bubbles?, cancelable?, detail?).</summary>
    public void initCustomEvent(JsValue typeArg, JsValue? bubblesArg = null, JsValue? cancelableArg = null, JsValue? detailArg = null)
    {
        initEvent(typeArg, bubblesArg, cancelableArg);
        if (!Dispatching && detailArg is not null && !detailArg.IsUndefined()) detail = detailArg;
    }

    /// <summary>Legacy MouseEvent.initMouseEvent — only the subset this engine tracks is stored.</summary>
    public void initMouseEvent(JsValue typeArg, JsValue? bubblesArg = null, JsValue? cancelableArg = null,
        JsValue? view = null, JsValue? detailArg = null, JsValue? screenXArg = null, JsValue? screenYArg = null,
        JsValue? clientXArg = null, JsValue? clientYArg = null, JsValue? ctrl = null, JsValue? alt = null,
        JsValue? shift = null, JsValue? meta = null, JsValue? btn = null, params JsValue[] _)
    {
        initEvent(typeArg, bubblesArg, cancelableArg);
        if (Dispatching) return;
        if (clientXArg is not null && clientXArg.IsNumber()) clientX = (float)clientXArg.AsNumber();
        if (clientYArg is not null && clientYArg.IsNumber()) clientY = (float)clientYArg.AsNumber();
        if (btn is not null && btn.IsNumber()) button = (int)btn.AsNumber();
        if (ctrl is not null && ctrl.IsBoolean()) ctrlKey = ctrl.AsBoolean();
        if (alt is not null && alt.IsBoolean()) altKey = alt.AsBoolean();
        if (shift is not null && shift.IsBoolean()) shiftKey = shift.AsBoolean();
        if (meta is not null && meta.IsBoolean()) metaKey = meta.AsBoolean();
    }

    /// <summary>Legacy KeyboardEvent.initKeyboardEvent — subset stored.</summary>
    public void initKeyboardEvent(JsValue typeArg, JsValue? bubblesArg = null, JsValue? cancelableArg = null,
        JsValue? view = null, JsValue? keyArg = null, params JsValue[] _)
    {
        initEvent(typeArg, bubblesArg, cancelableArg);
        if (!Dispatching && keyArg is not null && keyArg.IsString()) key = keyArg.AsString();
    }
}
