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
