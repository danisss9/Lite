using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace Lite.Network;

/// <summary>Network and cookie state owned by one browser window.</summary>
internal sealed class BrowserSession : IDisposable
{
    /// <summary>The OS token browsers report for this host: navigator.platform's value.</summary>
    internal static readonly string Platform = OperatingSystem.IsWindows()
        ? "Win32"
        : "Linux " + (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64");

    internal static readonly string UserAgent = "Mozilla/5.0 (" +
        (OperatingSystem.IsWindows() ? "Windows NT 10.0; Win64; x64" : "X11; " + Platform) +
        ") AppleWebKit/537.36 (KHTML, like Gecko) Lite/1.0";

    private readonly CookieContainer _cookies = new();

    /// <summary>Test surface: seed network-set cookies (Set-Cookie responses) directly.</summary>
    internal CookieContainer TestCookieContainer => _cookies;
    internal string Language { get; } = CultureInfo.CurrentUICulture.Name is { Length: > 0 } locale
        ? locale : "en-US";
    internal HttpClient Client { get; }
    internal HttpClient ModuleClient { get; }
    internal HttpClient NoCookieClient { get; }
    internal HttpClient NoCookieModuleClient { get; }
    internal ConcurrentQueue<string> Diagnostics { get; } = new();
    internal Dictionary<string, SkiaSharp.SKBitmap?> Images { get; } = new(StringComparer.Ordinal);

    // ── CSS user-agent controls (CSS 2.1 UA conformance, conform.html#conformance) ──
    /// <summary>The user's style sheet text (the conformance requirement to allow a user
    /// style sheet file; the host loads the file and hands over its text). Stamped as USER
    /// origin: normal declarations lose to author rules, !important ones win (§6.4.1).</summary>
    public string? UserStyleSheet { get; set; }
    /// <summary>Whether author style sheets influence rendering at all (the conformance
    /// requirement to let the user disable author styles). UA and user sheets are unaffected.</summary>
    public bool AuthorStylesEnabled { get; set; } = true;
    /// <summary>The preferred named style sheet set among alternate style sheets (HTML4 §14.3 /
    /// the conformance requirement to select alternate sets). Null keeps the default set.</summary>
    public string? SelectedStyleSheetSet { get; set; }
    /// <summary>The document state of the page this session last loaded, so live control
    /// toggles can refresh the rule view and recascade.</summary>
    internal Models.DocumentState? AttachedDocumentState { get; private set; }

    internal void Attach(Models.DocumentState state) => AttachedDocumentState = state;

    internal BrowserSession()
    {
        Client = CreateClient(allowAutoRedirect: true, useCookies: true);
        ModuleClient = CreateClient(allowAutoRedirect: false, useCookies: true);
        NoCookieClient = CreateClient(allowAutoRedirect: true, useCookies: false);
        NoCookieModuleClient = CreateClient(allowAutoRedirect: false, useCookies: false);
    }

    private HttpClient CreateClient(bool allowAutoRedirect, bool useCookies)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = useCookies,
            AllowAutoRedirect = allowAutoRedirect,
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(Language);
        return client;
    }

    internal string GetDocumentCookie(string address)
    {
        var uri = CookieUri(address);
        // RFC 6265 §5.4: the cookie string lists cookies whose path-length is LONGEST first
        // (creation time breaking ties), excluding HttpOnly cookies script must not see.
        return string.Join("; ", _cookies.GetCookies(uri).Cast<Cookie>()
            .Where(cookie => !cookie.HttpOnly)
            .OrderByDescending(cookie => cookie.Path?.Length ?? 0)
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    internal void SetDocumentCookie(string address, string value)
    {
        var uri = CookieUri(address);
        if (string.IsNullOrWhiteSpace(value)) return;
        var parts = value.Split(';');
        // Script cannot create an HttpOnly cookie. Ignore that attribute while retaining the cookie.
        var nameValue = parts[0].Trim();
        var sep = nameValue.IndexOf('=');
        if (sep <= 0) return; // a set without '=' is dropped per RFC 6265 §6.3 (bogus)
        var name = nameValue[..sep].Trim();
        // Max-Age takes precedence over Expires (RFC 6265 §5.3 step 12): the container parses
        // only expires, so translate — and a non-positive max-age DELETES the cookie.
        var maxAge = parts.Select(p => p.Trim()).FirstOrDefault(p =>
            p.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase));
        if (maxAge is not null && long.TryParse(maxAge[8..].TrimStart('+'), out var seconds))
        {
            if (seconds <= 0)
            {
                // Deleting = storing an expired cookie under the same (name, path, domain):
                // the container filters expired entries on read. SetCookies replaces by key;
                // Add() would silently drop an already-expired cookie.
                var path = CookiePath(parts);
                _cookies.SetCookies(uri, $"{name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path={path}");
                return;
            }
            var expires = DateTime.UtcNow.AddSeconds(seconds);
            if (!parts.Any(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)))
                parts = parts.Append($"expires={expires:R}").ToArray();
        }
        var allowed = parts.Where(part => !part.Trim().Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
        try { _cookies.SetCookies(uri, string.Join(";", allowed)); }
        catch (CookieException error) { Diagnostics.Enqueue($"cookie {uri.Host}: {error.Message}"); }
    }

    /// <summary>The Path attribute of a document.cookie string (default "/").</summary>
    private static string CookiePath(string[] parts)
    {
        var path = parts.Select(p => p.Trim()).FirstOrDefault(p =>
            p.StartsWith("path=", StringComparison.OrdinalIgnoreCase));
        return path is { Length: > 5 } ? path[5..] : "/";
    }

    private static Uri CookieUri(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri : new Uri("http://lite.invalid/");

    public void Dispose()
    {
        Client.Dispose();
        ModuleClient.Dispose();
        NoCookieClient.Dispose();
        NoCookieModuleClient.Dispose();
        foreach (var image in Images.Values) image?.Dispose();
        Images.Clear();
    }
}
