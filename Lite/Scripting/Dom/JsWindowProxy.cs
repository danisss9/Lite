using Lite.QuickJs;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>
/// A cross-context window reference (a WindowProxy), used for an iframe's
/// <c>contentWindow</c> and a child's <c>parent</c>/<c>top</c>. It carries the source context
/// (the one that holds/uses the proxy) and the target context it points at, so
/// <see cref="postMessage"/> can deliver into the target with the source's origin.
/// </summary>
internal sealed class JsWindowProxy : IJsDynamicMembers
{
    private readonly JsEngine _source;
    private readonly JsEngine _target;

    internal JsWindowProxy(JsEngine source, JsEngine target)
    {
        _source = source;
        _target = target;
    }

    /// <summary>
    /// HTML postMessage: validates <paramref name="targetOrigin"/> against the recipient's
    /// origin, structured-clones the message into the target realm, and queues a
    /// <c>message</c> event on the target window carrying the sender's origin and a
    /// WindowProxy back to the sender as <c>event.source</c>.
    /// </summary>
    public void postMessage(JsValue message, JsValue? targetOrigin = null, JsValue? transfer = null)
    {
        _target.CheckPostMessageTargetOrigin(targetOrigin);
        var ports = JsMessagePort.TransferPorts(transfer, _target.MessagePortContext);
        var cloned = StructuredClone.Clone(_source.RawEngine, message, _target.RawEngine);
        var replySource = _target.GetWindowProxy(_source);
        var origin = _source.Origin;
        _target.EnqueueMacrotask(() => _target.DeliverMessage(cloned, origin, replySource, ports));
    }

    public JsWindowProxy self => this;
    public JsWindowProxy window => this;
    public Dictionary<string, object> frames => _target.GetFrameProxies(_source);
    public int length => _target.NestedEngines().Count();
    public JsDocument? document => _source.Origin == _target.Origin ? _target.DocumentFacade : null;
    public JsLocation? location => _source.Origin == _target.Origin ? _target.Location : null;
    public string name => _target.RawEngine.GetValue("name").ToString();
    public bool closed => false;
    public void focus() { }
    public void blur() { }
    public void close() { }

    /// <summary>The window's remaining globals, answered in the ACCESSING realm: an interface
    /// name answers a constructor whose instanceof brand-checks the host object (a cross-realm
    /// wrapper cannot be shared as a JS value), and any other global answers its primitive
    /// value or undefined.</summary>
    public QuickJsValue GetMember(QuickJsRealm requesting, string name)
    {
        if (string.IsNullOrEmpty(name)) return requesting.Undefined();
        if (WebIdlInterfaces.IsInterfaceName(name))
            return Engine.BrandConstructor(requesting, name, value => IsInstanceOf(value, name));
        var value = _target.RawEngine.GetValue(name);
        if (value.IsBoolean()) return requesting.Bool(value.AsBoolean());
        if (value.IsNumber()) return requesting.Number(value.AsNumber());
        if (value.IsString()) return requesting.String(value.AsString());
        return requesting.Undefined();
    }

    /// <summary>The interface-chain test behind a cross-realm <c>instanceof</c>: an element is
    /// also a Node/Element/HTMLElement (its per-tag interface is what the brand records), and
    /// every other host object matches its own interface name.</summary>
    private static bool IsInstanceOf(object value, string name) => name switch
    {
        "EventTarget" or "Node" or "Element" or "HTMLElement" => value is JsElement,
        "Document" or "HTMLDocument" => value is JsDocument,
        "Window" => value is JsWindowProxy,
        "Location" => value is JsLocation,
        "History" => value is JsHistory,
        "Navigator" => value is JsNavigator,
        _ => WebIdlInterfaces.For(value) == name,
    };
}
