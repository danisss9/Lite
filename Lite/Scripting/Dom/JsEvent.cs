using Jint;
using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>DOM Event object exposed to JavaScript.</summary>
public class JsEvent
{
    public string type { get; internal set; } = string.Empty;
    public bool bubbles { get; internal set; }
    public bool cancelable { get; internal set; }
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
