using Jint;
using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>
/// Backing implementation for the JavaScript fetch() shim. The HTTP request runs on a
/// background thread; the result is marshaled back onto the engine's event loop and the
/// supplied JS callback is invoked there (so Jint is only touched from the UI thread).
/// Supports http(s) and data: URLs.
/// </summary>
internal static class JsFetch
{
    private static readonly HttpClient _client = new();

    /// <summary>Result object handed to the JS callback. Field names are read from JS.</summary>
    public sealed class FetchResult
    {
        public bool ok { get; set; }
        public int status { get; set; }
        public string statusText { get; set; } = "";
        public string body { get; set; } = "";
        public string? error { get; set; }
    }

    /// <summary>JS signature: __nativeFetch(url, options, callback).</summary>
    internal static void Native(JsEngine engine, string url, JsValue options, JsValue callback)
    {
        var method = "GET";
        string? requestBody = null;
        if (options.IsObject())
        {
            var obj = options.AsObject();
            var m = obj.Get("method");
            if (m.IsString()) method = m.AsString().ToUpperInvariant();
            var b = obj.Get("body");
            if (b.IsString()) requestBody = b.AsString();
        }

        var baseUrl = Parser.BaseUrl;
        System.Threading.Tasks.Task.Run(() =>
        {
            var result = Execute(url, method, requestBody, baseUrl);
            // Hop back to the UI thread before touching Jint.
            engine.EnqueueMacrotask(() =>
            {
                try { engine.RawEngine.Invoke(callback, JsValue.FromObject(engine.RawEngine, result)); }
                catch (Exception ex) { Console.WriteLine($"[fetch callback] {ex.Message}"); }
            });
        });
    }

    private static FetchResult Execute(string url, string method, string? body, string? baseUrl)
    {
        try
        {
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return FromDataUri(url);

            var resolved = ResolveUrl(url, baseUrl);
            using var request = new HttpRequestMessage(new HttpMethod(method), resolved);
            if (body is not null)
                request.Content = new StringContent(body);

            using var response = _client.Send(request);
            var text = response.Content.ReadAsStringAsync().Result;
            return new FetchResult
            {
                ok = (int)response.StatusCode is >= 200 and < 300,
                status = (int)response.StatusCode,
                statusText = response.ReasonPhrase ?? "",
                body = text,
            };
        }
        catch (Exception ex)
        {
            return new FetchResult { error = ex.Message, status = 0, ok = false };
        }
    }

    private static FetchResult FromDataUri(string dataUri)
    {
        if (!Lite.Network.DataUri.TryDecodeText(dataUri, out var text, out _))
            return new FetchResult { error = "malformed data URI" };
        return new FetchResult { ok = true, status = 200, statusText = "OK", body = text };
    }

    private static string ResolveUrl(string src, string? baseUrl)
    {
        if (Uri.TryCreate(src, UriKind.Absolute, out _)) return src;
        if (baseUrl is not null && Uri.TryCreate(new Uri(baseUrl), src, out var resolved))
            return resolved.ToString();
        return src;
    }
}
