using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace Lite.Network;

/// <summary>Network and cookie state owned by one browser window.</summary>
internal sealed class BrowserSession : IDisposable
{
    internal const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Lite/1.0";

    private readonly CookieContainer _cookies = new();
    internal string Language { get; } = CultureInfo.CurrentUICulture.Name is { Length: > 0 } locale
        ? locale : "en-US";
    internal HttpClient Client { get; }
    internal HttpClient ModuleClient { get; }
    internal HttpClient NoCookieClient { get; }
    internal HttpClient NoCookieModuleClient { get; }
    internal ConcurrentQueue<string> Diagnostics { get; } = new();
    internal Dictionary<string, SkiaSharp.SKBitmap?> Images { get; } = new(StringComparer.Ordinal);

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
        return string.Join("; ", _cookies.GetCookies(uri).Cast<Cookie>()
            .Where(cookie => !cookie.HttpOnly)
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    internal void SetDocumentCookie(string address, string value)
    {
        var uri = CookieUri(address);
        if (string.IsNullOrWhiteSpace(value)) return;
        var parts = value.Split(';');
        // Script cannot create an HttpOnly cookie. Ignore that attribute while retaining the cookie.
        var allowed = parts.Where(part => !part.Trim().Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
        try { _cookies.SetCookies(uri, string.Join(";", allowed)); }
        catch (CookieException error) { Diagnostics.Enqueue($"cookie {uri.Host}: {error.Message}"); }
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
