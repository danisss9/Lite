using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>A dedicated script realm with asynchronous messages to its owning page.</summary>
internal sealed class JsWorker
{
    private readonly JsEngine _owner;
    private readonly Queue<(object? Data, JsMessagePort[] Ports)> _pending = new();
    private readonly List<JsValue> _listeners = [];
    private readonly List<JsValue> _ownerListeners = [];
    private Engine? _realm;
    private JsMessagePortContext? _messagePortContext;
    private bool _terminated;
    private int _nextTimerId;
    private readonly Dictionary<int, System.Threading.Timer> _timers = [];

    public JsValue? onmessage { get; set; }
    public JsValue? onerror { get; set; }

    public void addEventListener(string type, JsValue callback)
    {
        if (type == "message") _ownerListeners.Add(callback);
    }

    public void removeEventListener(string type, JsValue callback)
    {
        if (type == "message") _ownerListeners.Remove(callback);
    }

    internal JsWorker(JsEngine owner, string source)
    {
        _owner = owner;
        var url = owner.ResolveAgainstCurrent(source) ?? source;
        _ = Task.Run(async () =>
        {
            try
            {
                var script = await LoadSource(url).ConfigureAwait(false);
                owner.EnqueueMacrotask(() => Initialize(script, url));
            }
            catch (Exception ex)
            {
                owner.DocumentState.Session?.Diagnostics.Enqueue($"worker {url}: {ex.Message}");
                owner.EnqueueMacrotask(() => ReportError(ex.Message));
            }
        });
    }

    private async Task<string> LoadSource(string url)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = url.IndexOf(',');
            if (comma < 0) throw new UriFormatException("Invalid worker data URL");
            var metadata = url[..comma];
            var value = url[(comma + 1)..];
            return metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value))
                : Uri.UnescapeDataString(value);
        }
        return await (_owner.DocumentState.Session?.Client
            ?? throw new InvalidOperationException("No browser session")).GetStringAsync(url).ConfigureAwait(false);
    }

    private void Initialize(string script, string url)
    {
        if (_terminated) return;
        try
        {
            var realm = new Engine();
            _realm = realm;
            var portContext = _messagePortContext = new JsMessagePortContext(realm,
                _owner.EnqueueMacrotask);
            if (Uri.TryCreate(url, UriKind.Absolute, out var locationUri))
                realm.SetValue("location", new
                {
                    href = locationUri.AbsoluteUri,
                    origin = locationUri.GetLeftPart(UriPartial.Authority),
                    protocol = locationUri.Scheme + ":",
                    host = locationUri.Authority,
                    hostname = locationUri.Host,
                    pathname = locationUri.AbsolutePath,
                    search = locationUri.Query,
                });
            var started = System.Diagnostics.Stopwatch.StartNew();
            realm.SetValue("performance", new
            {
                now = new Func<double>(() => started.Elapsed.TotalMilliseconds),
                timeOrigin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            realm.SetValue("__textEncodeUtf8", new Func<string, double[]>(
                value => System.Text.Encoding.UTF8.GetBytes(value).Select(b => (double)b).ToArray()));
            realm.SetValue("__textDecodeUtf8", new Func<double[], string>(
                bytes => System.Text.Encoding.UTF8.GetString(bytes.Select(Convert.ToByte).ToArray())));
            realm.SetValue("postMessage", new Action<JsValue, JsValue>((value, transfer) =>
            {
                var data = value.ToObject();
                var ports = JsMessagePort.TransferPorts(transfer, _owner.MessagePortContext);
                _owner.EnqueueMacrotask(() => DeliverToOwner(data, ports));
            }));
            realm.SetValue("__createMessageChannel", new Func<JsMessageChannel>(() => new(portContext)));
            realm.SetValue("__setWorkerTimeout", new Func<JsValue, int, bool, int>((callback, delay, repeat) =>
            {
                var id = ++_nextTimerId;
                var due = Math.Max(0, delay);
                var timer = new System.Threading.Timer(_ =>
                    _owner.EnqueueMacrotask(() =>
                    {
                        if (!_terminated) realm.Invoke(callback);
                        if (!repeat) ClearTimer(id);
                    }), null, due, repeat ? Math.Max(1, due) : Timeout.Infinite);
                _timers[id] = timer;
                return id;
            }));
            realm.SetValue("__clearWorkerTimer", new Action<int>(ClearTimer));
            realm.SetValue("__importScript", new Action<string>(source =>
            {
                var absolute = Uri.TryCreate(new Uri(url), source, out var resolved) ? resolved.AbsoluteUri : source;
                var imported = LoadSource(absolute).GetAwaiter().GetResult();
                realm.Execute(imported, absolute);
            }));
            realm.Execute("""
                globalThis.self = globalThis;
                globalThis.TextEncoder = class TextEncoder {
                  constructor() { this.encoding = 'utf-8'; }
                  encode(value = '') { return new Uint8Array(__textEncodeUtf8(String(value))); }
                  encodeInto(value, destination) {
                    const bytes = this.encode(value);
                    const n = Math.min(bytes.length, destination.length);
                    for (let i = 0; i < n; i++) destination[i] = bytes[i];
                    return { read: String(value).length, written: n };
                  }
                };
                globalThis.TextDecoder = class TextDecoder {
                  constructor() { this.encoding = 'utf-8'; }
                  decode(value) {
                    if (value == null) return '';
                    const bytes = value.buffer
                      ? new Uint8Array(value.buffer, value.byteOffset || 0, value.byteLength)
                      : value;
                    return __textDecodeUtf8(Array.from(bytes));
                  }
                };
                globalThis.MessageChannel = class MessageChannel {
                  constructor() {
                    function port() {
                      const listeners = [];
                      return {
                        onmessage: null,
                        _listeners: listeners,
                        _peer: null,
                        postMessage(data) {
                          const target = this._peer;
                          if (!target) return;
                          Promise.resolve().then(() => {
                            const event = { data, type: 'message', target };
                            if (typeof target.onmessage === 'function') target.onmessage(event);
                            for (const listener of target._listeners) listener(event);
                          });
                        },
                        addEventListener(type, listener) {
                          if (type === 'message') listeners.push(listener);
                        },
                        removeEventListener(type, listener) {
                          if (type !== 'message') return;
                          const index = listeners.indexOf(listener);
                          if (index >= 0) listeners.splice(index, 1);
                        },
                        start() {}, close() { this._peer = null; }
                      };
                    }
                    this.port1 = port();
                    this.port2 = port();
                    this.port1._peer = this.port2;
                    this.port2._peer = this.port1;
                  }
                };
                globalThis.setTimeout = function (callback, delay) {
                  return __setWorkerTimeout(callback, Number(delay) || 0, false);
                };
                globalThis.setInterval = function (callback, delay) {
                  return __setWorkerTimeout(callback, Number(delay) || 0, true);
                };
                globalThis.clearTimeout = globalThis.clearInterval = __clearWorkerTimer;
                globalThis.importScripts = function (...urls) {
                  for (const url of urls) __importScript(String(url));
                };
                globalThis.addEventListener = function (type, handler) {
                  if (type === 'message') __addWorkerMessageListener(handler);
                };
                """);
            realm.SetValue("__addWorkerMessageListener", new Action<JsValue>(_listeners.Add));
            realm.Execute("globalThis.MessageChannel = function MessageChannel() { return __createMessageChannel(); };");
            realm.Execute(script, url);
            while (_pending.Count > 0)
            {
                var (data, ports) = _pending.Dequeue();
                DeliverToWorker(data, ports);
            }
        }
        catch (Exception ex)
        {
            _owner.DocumentState.Session?.Diagnostics.Enqueue($"worker javascript {url}: {ex.Message}");
            ReportError(ex.Message);
        }
    }

    public void postMessage(JsValue value, JsValue? transfer = null)
    {
        if (_terminated) return;
        var data = value.ToObject();
        var ports = JsMessagePort.ExtractPorts(transfer);
        if (_realm is null) _pending.Enqueue((data, ports));
        else _owner.EnqueueMacrotask(() => DeliverToWorker(data, ports));
    }

    private void DeliverToWorker(object? data, JsMessagePort[] ports)
    {
        if (_terminated || _realm is null || _messagePortContext is null) return;
        foreach (var port in ports) port.MoveTo(_messagePortContext);
        var evt = JsValue.FromObject(_realm, new { data, type = "message", ports });
        var handler = _realm.GetValue("onmessage");
        if (!handler.IsUndefined() && !handler.IsNull()) _realm.Invoke(handler, evt);
        foreach (var listener in _listeners.ToArray()) _realm.Invoke(listener, evt);
    }

    private void DeliverToOwner(object? data, JsMessagePort[] ports)
    {
        if (_terminated) return;
        var handler = onmessage;
        var evt = JsValue.FromObject(_owner.RawEngine, new
        {
            data, type = "message", origin = string.Empty, source = (object?)null,
            ports,
        });
        if (handler is not null && !handler.IsUndefined() && !handler.IsNull())
            _owner.RawEngine.Invoke(handler, evt);
        foreach (var listener in _ownerListeners.ToArray()) _owner.RawEngine.Invoke(listener, evt);
    }

    private void ReportError(string message)
    {
        if (_terminated || onerror is null || onerror.IsUndefined() || onerror.IsNull()) return;
        _owner.RawEngine.Invoke(onerror, JsValue.FromObject(_owner.RawEngine, new { message, type = "error" }));
    }

    public void terminate()
    {
        _terminated = true;
        foreach (var timer in _timers.Values) timer.Dispose();
        _timers.Clear();
        _pending.Clear();
        _realm?.Dispose();
        _realm = null;
    }

    private void ClearTimer(int id)
    {
        if (_timers.Remove(id, out var timer)) timer.Dispose();
    }
}
