using System.Net.Http;
using System.Text;

namespace Lite.Scripting.Dom;

/// <summary>Minimal navigator object for feature detection and UA strings.</summary>
public class JsNavigator
{
    private readonly Lite.Network.BrowserSession? _session;
    private readonly Func<string>? _currentUrl;

    public string userAgent { get; } = Lite.Network.BrowserSession.UserAgent;
    public JsNavigator() : this(null) { }
    internal JsNavigator(Lite.Network.BrowserSession? session, Func<string>? currentUrl = null)
    {
        _session = session;
        _currentUrl = currentUrl;
        language = session?.Language ?? "en-US";
        var neutral = language.Split('-')[0];
        languages = neutral == language ? [language] : [language, neutral];
    }
    public string appName { get; } = "Netscape";
    public string appVersion { get; } = "5.0 (Windows)";
    public string platform { get; } = "Win32";
    public string product { get; } = "Gecko";
    public string vendor { get; } = "";
    public string language { get; }
    public string[] languages { get; }
    public bool onLine { get; } = true;
    public bool cookieEnabled { get; } = true;
    public int hardwareConcurrency { get; } = Environment.ProcessorCount;
    public int maxTouchPoints { get; } = 0;

    public bool javaEnabled() => false;

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
