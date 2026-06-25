using Jint;
using Jint.Native;

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

    public bool defaultPrevented => DefaultPrevented;

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

    public void initEvent(string typeArg, bool bubblesArg = false, bool cancelableArg = false)
    {
        type = typeArg;
        bubbles = bubblesArg;
        cancelable = cancelableArg;
    }
}
