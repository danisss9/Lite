using System.Net.Http;
using System.Text;

namespace Lite.Scripting.Dom;

/// <summary>Minimal navigator object for feature detection and UA strings.</summary>
using Lite.Scripting.Runtime;

public class JsNavigator
{
    private readonly Lite.Network.BrowserSession? _session;
    private readonly Func<string>? _currentUrl;
    private readonly Engine? _pageEngine;

    /// <summary>JsNavigator's engine-scoped error form needs the page's raw engine; tests and
    /// hosts that construct a bare navigator use the singleton fallback.</summary>
    internal Lite.Scripting.Runtime.Engine? PageEngine { get => _pageEngine; }

    public string userAgent { get; } = Lite.Network.BrowserSession.UserAgent;
    public JsNavigator() : this(null) { }
    internal JsNavigator(Lite.Network.BrowserSession? session, Func<string> currentUrl, Engine? pageEngine) : this(session, currentUrl)
    {
        _pageEngine = pageEngine;
    }
    internal JsNavigator(Lite.Network.BrowserSession? session, Func<string>? currentUrl = null)
    {
        _session = session;
        _currentUrl = currentUrl;
        language = session?.Language ?? "en-US";
        var neutral = language.Split('-')[0];
        languages = neutral == language ? [language] : [language, neutral];
    }
    public string appCodeName { get; } = "Mozilla";
    public string appName { get; } = "Netscape";
    public string appVersion { get; } = OperatingSystem.IsWindows() ? "5.0 (Windows)" : "5.0 (X11)";
    public string platform { get; } = Lite.Network.BrowserSession.Platform;
    public string product { get; } = "Gecko";
    public string vendor { get; } = "";
    public string language { get; }
    public string[] languages { get; }
    public bool onLine { get; } = true;
    public bool cookieEnabled { get; } = true;
    public int hardwareConcurrency { get; } = Environment.ProcessorCount;
    public int maxTouchPoints { get; } = 0;

    public bool javaEnabled() => false;

    /// <summary>PDF viewer capability surface (HTML §7.5.2 in its modern shape): the two PDF
    /// MIME types and the viewer plugin entries, plus pdfViewerEnabled — all present so
    /// feature detection reads a real shape (plugins-and-mimetypes).</summary>
    public bool pdfViewerEnabled => true;

    /// <summary>Plugins and mimeTypes built over one shared entry set so the back-references
    /// agree (mimeTypes.item(i).enabledPlugin === plugins.first).</summary>
    public JsPluginArray plugins => SharedPlugins.Value;
    public JsMimeTypeArray mimeTypes { get; } = new(SharedPlugins.Value);

    private static readonly Lazy<JsPluginArray> SharedPlugins = new(() => new JsPluginArray());

    /// <summary>registerProtocolHandler: the scheme must be a whitelisted safe scheme or
    /// start with "web+", and the URL must be same-origin with a "%s" placeholder — otherwise
    /// SyntaxError (HTML §7.5.3).</summary>
    public void registerProtocolHandler(string scheme, string url)
    {
        var pageEngine = _pageEngine ?? JsEngine.Instance?.RawEngine ?? throw JsErrors.Native("SyntaxError",
            "Failed to execute 'registerProtocolHandler': no browsing context.");
        var safe = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bitcoin", "geo", "im", "irc", "ircs", "magnet", "mailto", "mms", "mx", "news", "nntp",
          "openpgp4fpr", "sip", "sms", "smsto", "ssh", "tel", "urn", "webcal", "wtai", "xmpp" };
        if (!safe.Contains(scheme) && !(scheme.Length > 4 && scheme.StartsWith("web+", StringComparison.Ordinal)
            && scheme[4..].Length >= 1 && scheme[4..].All(char.IsAsciiLetterOrDigit)))
            throw JsErrors.Native(pageEngine, "SyntaxError",
                $"Failed to execute 'registerProtocolHandler': The scheme '{scheme}' is not safe.");
        var origin = JsEngine.Instance?.Origin ?? "null";
        // IsAbsolute must gate the parse: UriKind.Absolute alone accepts a path-absolute
        // reference ("/handler?u=%s") as an implicit file: URI on Unix, which then reads as
        // cross-origin instead of resolving against the page (see UrlUtils.IsAbsolute).
        Uri? u;
        if (Lite.Network.UrlUtils.IsAbsolute(url) && Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            u = parsed;
        else if (Uri.TryCreate(new Uri(origin), url, out var resolved))
            u = resolved;
        else
            throw JsErrors.Native(pageEngine, "SyntaxError",
                "Failed to execute 'registerProtocolHandler': The url is not valid.");
        if (u.IsAbsoluteUri && $"{u.Scheme}://{u.Authority}" != origin)
            throw JsErrors.Native(pageEngine, "SyntaxError",
                "Failed to execute 'registerProtocolHandler': The url must be same-origin.");
        if (!url.Contains("%s", StringComparison.Ordinal))
            throw JsErrors.Native(pageEngine, "SyntaxError",
                "Failed to execute 'registerProtocolHandler': The url must contain '%s'.");
    }

    public void unregisterProtocolHandler(string scheme, string url) { }

    /// <summary>
    /// navigator.sendBeacon(url, data) — fire-and-forget POST for telemetry (HTML “beacon” API).
    /// String payloads use <c>text/plain;charset=UTF-8</c>. The request carries the session's
    /// cookies and never blocks or reports failures to script. Returns true when accepted.
    /// </summary>
    public bool sendBeacon(string url, object? data = null)
    {
        if (_session is null) return false;
        if (!Lite.Network.UrlUtils.IsAbsolute(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            if (!Uri.TryCreate(_currentUrl?.Invoke() ?? "", UriKind.Absolute, out var baseUri) ||
                !Uri.TryCreate(baseUri, url, out var resolved))
                return false;
            url = resolved.AbsoluteUri;
        }
        var text = data as string;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, url);
                if (text is not null)
                    message.Content = new StringContent(text, Encoding.UTF8, "text/plain");
                using var _ = await _session.Client.SendAsync(message).ConfigureAwait(false);
            }
            catch { /* beacons are fire-and-forget: failures are not observable by design */ }
        });
        return true;
    }
}
