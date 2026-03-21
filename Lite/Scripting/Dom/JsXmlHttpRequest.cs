using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>XMLHttpRequest implementation for JavaScript.</summary>
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

    private string _method = "GET";
    private string _url = "";
    private bool _async = true;
    private readonly Dictionary<string, string> _requestHeaders = [];
    private readonly Dictionary<string, string> _responseHeaders = [];

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
            Task.Run(() => DoSend(body));
        }
        else
        {
            DoSend(body);
        }
    }

    private void DoSend(string? body)
    {
        try
        {
            var request = new HttpRequestMessage(new HttpMethod(_method), _url);
            foreach (var h in _requestHeaders)
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (body != null)
                request.Content = new StringContent(body);

            var response = _client.Send(request);
            status = (int)response.StatusCode;
            statusText = response.ReasonPhrase ?? "";

            _responseHeaders.Clear();
            foreach (var h in response.Headers)
                _responseHeaders[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                _responseHeaders[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);

            readyState = 2; // HEADERS_RECEIVED
            FireReadyStateChange();

            readyState = 3; // LOADING
            FireReadyStateChange();

            responseText = response.Content.ReadAsStringAsync().Result;

            readyState = 4; // DONE
            FireReadyStateChange();
            FireEvent(onload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XHR Error] {ex.Message}");
            readyState = 4;
            FireReadyStateChange();
            FireEvent(onerror);
        }
    }

    public void setRequestHeader(string name, string value) => _requestHeaders[name] = value;

    public string? getResponseHeader(string name) =>
        _responseHeaders.TryGetValue(name.ToLowerInvariant(), out var v) ? v : null;

    public string getAllResponseHeaders() =>
        string.Join("\r\n", _responseHeaders.Select(h => $"{h.Key}: {h.Value}"));

    public void abort()
    {
        readyState = 0;
    }

    private void FireReadyStateChange()
    {
        FireEvent(onreadystatechange);
    }

    private void FireEvent(JsValue? handler)
    {
        if (handler is null || handler.Type == Jint.Runtime.Types.Undefined || handler.Type == Jint.Runtime.Types.Null) return;
        try
        {
            Scripting.JsEngine.Instance?.RawEngine.Invoke(handler);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XHR Callback] {ex.Message}");
        }
    }
}
