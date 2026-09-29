using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>The engine and event loop that currently own a message port.</summary>
internal sealed class JsMessagePortContext
{
    public Engine Engine { get; }
    public Action<Action> Enqueue { get; }
    public JsMessagePortContext(JsEngine page)
        : this(page.RawEngine, page.EnqueueMacrotask) { }

    public JsMessagePortContext(Engine engine, Action<Action> enqueue)
    {
        Engine = engine;
        Enqueue = enqueue;
    }
}

/// <summary>A pair of entangled ports. A port can move between pages and workers.</summary>
internal sealed class JsMessageChannel
{
    public JsMessagePort port1 { get; }
    public JsMessagePort port2 { get; }

    public JsMessageChannel(JsMessagePortContext owner)
    {
        port1 = new JsMessagePort(owner);
        port2 = new JsMessagePort(owner);
        port1.Peer = port2;
        port2.Peer = port1;
    }
}

internal sealed class JsMessagePort
{
    private JsMessagePortContext _owner;
    private readonly List<JsValue> _listeners = [];
    private bool _closed;

    internal JsMessagePort? Peer { get; set; }
    public JsValue? onmessage { get; set; }
    public JsValue? onmessageerror { get; set; }

    internal JsMessagePort(JsMessagePortContext owner) => _owner = owner;

    public void postMessage(JsValue value, JsValue? transfer = null)
    {
        if (_closed || Peer is not { _closed: false } peer) return;
        var data = value.ToObject();
        var ports = TransferPorts(transfer, peer._owner);
        peer._owner.Enqueue(() => peer.Deliver(data, ports));
    }

    private void Deliver(object? data, JsMessagePort[] ports)
    {
        if (_closed) return;
        var evt = JsValue.FromObject(_owner.Engine, new { type = "message", data, ports, target = this });
        if (onmessage is { } handler && !handler.IsUndefined() && !handler.IsNull())
            _owner.Engine.Invoke(handler, evt);
        foreach (var listener in _listeners.ToArray()) _owner.Engine.Invoke(listener, evt);
    }

    public void addEventListener(string type, JsValue callback, JsValue? options = null)
    {
        if (type == "message") _listeners.Add(callback);
    }

    public void removeEventListener(string type, JsValue callback, JsValue? options = null)
    {
        if (type == "message") _listeners.Remove(callback);
    }

    public void start() { }
    public void close() { _closed = true; Peer = null; }

    internal void MoveTo(JsMessagePortContext destination) => _owner = destination;

    internal static JsMessagePort[] TransferPorts(JsValue? transfer, JsMessagePortContext destination)
    {
        var ports = ExtractPorts(transfer);
        foreach (var port in ports) port.MoveTo(destination);
        return ports;
    }

    internal static JsMessagePort[] ExtractPorts(JsValue? transfer)
    {
        if (transfer is null || transfer.IsUndefined() || transfer.IsNull() || !transfer.IsObject())
            return [];
        var array = transfer.AsObject();
        var length = array.Get("length");
        if (!length.IsNumber()) return [];
        var count = Math.Clamp((int)length.AsNumber(), 0, 1024);
        var ports = new List<JsMessagePort>(count);
        for (var i = 0; i < count; i++)
        {
            var candidate = array.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToObject();
            if (candidate is JsMessagePort port) ports.Add(port);
        }
        return ports.ToArray();
    }
}
