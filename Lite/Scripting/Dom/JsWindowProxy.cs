using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>
/// A cross-context window reference (a WindowProxy), used for an iframe's
/// <c>contentWindow</c> and a child's <c>parent</c>/<c>top</c>. It carries the source context
/// (the one that holds/uses the proxy) and the target context it points at, so
/// <see cref="postMessage"/> can deliver into the target with the source's origin.
/// </summary>
internal sealed class JsWindowProxy
{
    private readonly JsEngine _source;
    private readonly JsEngine _target;

    internal JsWindowProxy(JsEngine source, JsEngine target)
    {
        _source = source;
        _target = target;
    }

    /// <summary>
    /// HTML postMessage: structured-clones <paramref name="message"/> (via the CLR object graph)
    /// and queues a <c>message</c> event on the target window, carrying the sender's origin and a
    /// WindowProxy back to the sender as <c>event.source</c>.
    /// </summary>
    public void postMessage(JsValue message, JsValue? targetOrigin = null, JsValue? transfer = null)
    {
        var data = message.ToObject();   // structured clone across engines via the CLR graph
        var origin = _source.Origin;
        var replySource = new JsWindowProxy(_target, _source);
        _target.EnqueueMacrotask(() => _target.DeliverMessage(data, origin, replySource));
    }

    public JsWindowProxy self => this;
    public JsWindowProxy window => this;
    public int length => 0;
    public bool closed => false;
    public void focus() { }
    public void blur() { }
    public void close() { }
}
