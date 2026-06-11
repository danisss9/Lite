using Jint;
using Jint.Native;
using Lite.Network;

namespace Lite.Scripting.Dom;

/// <summary>
/// XMLHttpRequest. Async requests run the HTTP call on a background thread but marshal every
/// readyState transition and event callback back onto the engine's event loop via
/// <see cref="JsEngine.EnqueueMacrotask"/>, so Jint (which is not thread-safe) is only ever
/// touched from the UI/drain thread. Synchronous requests (async=false) run inline.
/// </summary>
public class JsXmlHttpRequest
{
    private static readonly HttpClient _client = new();

    // Ready states
    public int UNSENT { get; } = 0;
    public int OPENED { get; } = 1;
    public int HEADERS_RECEIVED { get; } = 2;
    public int LOADING { get; } = 3;
    public int DONE { get; } = 4;

    public int readyState { get; private set; } = 0;
    public int status { get; private set; } = 0;
    public string statusText { get; private set; } = "";
    public string responseText { get; private set; } = "";
    public string responseType { get; set; } = "";
    public object? response => responseText;

    public JsValue? onreadystatechange { get; set; }
    public JsValue? onload { get; set; }
    public JsValue? onerror { get; set; }
    public JsValue? onprogress { get; set; }

    private readonly List<(string Type, JsValue Fn)> _listeners = [];

    private string _method = "GET";
    private string _url = "";
    private bool _async = true;
    private readonly Dictionary<string, string> _requestHeaders = [];
    private readonly Dictionary<string, string> _responseHeaders = [];

    public void addEventListener(string type, JsValue fn, JsValue? options = null)
    {
        if (fn is not null && !fn.IsUndefined() && !fn.IsNull()) _listeners.Add((type, fn));
    }

    public void removeEventListener(string type, JsValue fn, JsValue? options = null) =>
        _listeners.RemoveAll(l => l.Type == type && Equals(l.Fn, fn));

    public void open(string method, string url, bool async = true, string? user = null, string? password = null)
    {
        _method = method.ToUpperInvariant();
        _url = url;
        _async = async;
        readyState = 1; // OPENED
        FireReadyStateChange();
    }

    public void send(string? body = null)
    {
        if (_async)
        {
            // HTTP on a pool thread; all engine interaction is marshalled back via the macrotask queue.
            Task.Run(() =>
            {
                var result = DoHttp(body);
                JsEngine.Instance?.EnqueueMacrotask(() => Deliver(result));
            });
        }
        else
        {
            Deliver(DoHttp(body));
        }
    }

    private sealed record HttpResult(bool Ok, int Status, string StatusText, string Body,
        Dictionary<string, string> Headers, string? Error);

    /// <summary>Performs the HTTP request off the engine thread. No Jint interaction here.</summary>
    private HttpResult DoHttp(string? body)
    {
        try
        {
            if (DataUri.IsDataUri(_url))
            {
                if (DataUri.TryDecodeText(_url, out var dataText, out _))
                    return new HttpResult(true, 200, "OK", dataText, [], null);
                return new HttpResult(false, 0, "", "", [], "malformed data URI");
            }

            var request = new HttpRequestMessage(new HttpMethod(_method), _url);
            foreach (var h in _requestHeaders)
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (body != null)
                request.Content = new StringContent(body);

            var response = _client.Send(request);
            var headers = new Dictionary<string, string>();
            foreach (var h in response.Headers)
                headers[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                headers[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);

            var text = response.Content.ReadAsStringAsync().Result;
            return new HttpResult(true, (int)response.StatusCode, response.ReasonPhrase ?? "", text, headers, null);
        }
        catch (Exception ex)
        {
            return new HttpResult(false, 0, "", "", [], ex.Message);
        }
    }

    /// <summary>Applies the result and fires events. Runs on the engine thread.</summary>
    private void Deliver(HttpResult result)
    {
        if (!result.Ok)
        {
            Console.WriteLine($"[XHR Error] {result.Error}");
            readyState = 4;
            FireReadyStateChange();
            FireEvent(onerror, "error");
            return;
        }

        status = result.Status;
        statusText = result.StatusText;
        _responseHeaders.Clear();
        foreach (var kv in result.Headers) _responseHeaders[kv.Key] = kv.Value;

        readyState = 2; FireReadyStateChange();   // HEADERS_RECEIVED
        readyState = 3; FireReadyStateChange();   // LOADING
        responseText = result.Body;
        readyState = 4; FireReadyStateChange();    // DONE
        FireEvent(onload, "load");
    }

    public void setRequestHeader(string name, string value) => _requestHeaders[name] = value;

    public string? getResponseHeader(string name) =>
        _responseHeaders.TryGetValue(name.ToLowerInvariant(), out var v) ? v : null;

    public string getAllResponseHeaders() =>
        string.Join("\r\n", _responseHeaders.Select(h => $"{h.Key}: {h.Value}"));

    public void abort() => readyState = 0;

    private void FireReadyStateChange()
    {
        FireEvent(onreadystatechange, "readystatechange");
    }

    private void FireEvent(JsValue? handler, string type)
    {
        var engine = JsEngine.Instance?.RawEngine;
        if (engine is null) return;
        try
        {
            if (handler is not null && !handler.IsUndefined() && !handler.IsNull())
                engine.Invoke(handler);
            foreach (var (t, fn) in _listeners.ToList())
                if (t == type) engine.Invoke(fn);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XHR Callback] {ex.Message}");
        }
    }
}
