using System.Collections.Concurrent;
using System.Net;
using Jint;
using Jint.Runtime;
using Jint.Runtime.Modules;

namespace Lite.Scripting;

/// <summary>Document-owned browser module fetching; all Jint access stays on the owning thread.</summary>
internal sealed class HttpModuleLoader(string baseUrl, string documentUrl, Action<Action> post) : IModuleLoader, IAsyncModuleLoader, IDisposable
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false });
    private static readonly HashSet<string> JavaScriptMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/javascript", "application/javascript", "application/ecmascript", "text/ecmascript", "application/x-javascript",
        "application/x-ecmascript", "text/javascript1.0", "text/javascript1.1", "text/javascript1.2", "text/javascript1.3",
        "text/javascript1.4", "text/javascript1.5", "text/jscript", "text/livescript", "text/x-ecmascript", "text/x-javascript",
    };
    private readonly ConcurrentDictionary<string, string> _responseUrls = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private Engine? _engine;
    private int _pending;
    internal bool HasPendingLoads => Volatile.Read(ref _pending) > 0;
    internal void Bind(Engine engine) => _engine = engine;
    internal string ResponseUrl(string url) => _responseUrls.GetValueOrDefault(url, url);
    internal void RegisterSourceUrl(string key, string url) => _responseUrls[key] = url;

    public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        var specifier = moduleRequest.Specifier;
        Uri? resolved = null;
        if (Uri.TryCreate(specifier, UriKind.Absolute, out var absolute)) resolved = absolute;
        else if (specifier.StartsWith('/') || specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal))
        {
            var basis = ResponseUrl(referencingModuleLocation ?? baseUrl);
            if (Uri.TryCreate(basis, UriKind.Absolute, out var uri)) Uri.TryCreate(uri, specifier, out resolved);
        }
        if (resolved is null || resolved.Scheme is not ("http" or "https" or "data"))
            throw TypeError($"Cannot resolve browser module specifier '{specifier}'");
        return new(moduleRequest, resolved.AbsoluteUri, resolved, SpecifierType.RelativeOrAbsolute);
    }

    public Module LoadModule(Engine engine, ResolvedSpecifier resolved) =>
        throw TypeError("Network modules require asynchronous loading");

    public void LoadModuleAsync(Engine engine, ResolvedSpecifier resolved, ModuleLoadCompletion completion)
    {
        Interlocked.Increment(ref _pending);
        _ = Fetch();
        async Task Fetch()
        {
            try
            {
                var (code, responseUrl) = await FetchSource(resolved.Uri!, _lifetime.Token).ConfigureAwait(false);
                post(() =>
                {
                    try
                    {
                        if (_lifetime.IsCancellationRequested) completion.SetError(TypeError("Module load cancelled"));
                        else { _responseUrls[resolved.Key] = responseUrl; completion.SetSource(code); }
                    }
                    finally { Interlocked.Decrement(ref _pending); }
                });
            }
            catch (Exception error)
            {
                post(() => { try { completion.SetError(TypeError(error.Message)); } finally { Interlocked.Decrement(ref _pending); } });
            }
        }
    }

    private async Task<(string Code, string Url)> FetchSource(Uri uri, CancellationToken cancellation)
    {
        if (uri.Scheme == "data")
        {
            if (!Lite.Network.DataUri.TryDecodeBytes(uri.AbsoluteUri, out var bytes, out var mime) || !JavaScriptMimeTypes.Contains(mime.Split(';')[0]))
                throw new IOException("Module data URL does not have a JavaScript MIME type");
            return (System.Text.Encoding.UTF8.GetString(bytes), uri.AbsoluteUri);
        }
        var origin = Uri.TryCreate(documentUrl, UriKind.Absolute, out var document) && document.Scheme is "http" or "https"
            ? document.GetLeftPart(UriPartial.Authority) : "null";
        for (var redirects = 0; redirects <= 20; redirects++)
        {
            if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) throw new IOException("Unsupported module URL");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var crossOrigin = origin != uri.GetLeftPart(UriPartial.Authority);
            if (crossOrigin) request.Headers.TryAddWithoutValidation("Origin", origin);
            using var response = await Client.SendAsync(request, cancellation).ConfigureAwait(false);
            if (crossOrigin && (!response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins) ||
                !origins.Any(value => value == "*" || value == origin))) throw new IOException("Module response failed CORS");
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is null) throw new IOException("Module redirect is missing Location");
                uri = new Uri(uri, response.Headers.Location); continue;
            }
            response.EnsureSuccessStatusCode();
            if (!JavaScriptMimeTypes.Contains(response.Content.Headers.ContentType?.MediaType ?? "")) throw new IOException("Module response does not have a JavaScript MIME type");
            return (System.Text.Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false)), uri.AbsoluteUri);
        }
        throw new IOException("Too many module redirects");
    }

    private JavaScriptException TypeError(string message) => _engine is null
        ? new JavaScriptException((Jint.Native.JsValue)message)
        : new JavaScriptException(_engine.Intrinsics.TypeError.Construct(message));
    public void Dispose() => _lifetime.Cancel();
}
