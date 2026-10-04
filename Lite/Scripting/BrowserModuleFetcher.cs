using System.Net;
using Lite.Network;

namespace Lite.Scripting;

/// <summary>Fetches browser modules without touching a JavaScript engine.</summary>
internal sealed class BrowserModuleFetcher(BrowserSession session, string documentUrl)
{
    private static readonly HashSet<string> JavaScriptMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/javascript", "application/javascript", "application/ecmascript", "text/ecmascript", "application/x-javascript",
        "application/x-ecmascript", "text/javascript1.0", "text/javascript1.1", "text/javascript1.2", "text/javascript1.3",
        "text/javascript1.4", "text/javascript1.5", "text/jscript", "text/livescript", "text/x-ecmascript", "text/x-javascript",
    };

    internal async Task<(string Code, string Url)> FetchSource(Uri uri, ModuleFetchOptions? options,
        CancellationToken cancellation)
    {
        if (uri.Scheme == "data")
        {
            if (!DataUri.TryDecodeBytes(uri.AbsoluteUri, out var bytes, out var mime) ||
                !JavaScriptMimeTypes.Contains(mime.Split(';')[0]))
                throw new IOException("Module data URL does not have a JavaScript MIME type");
            return (System.Text.Encoding.UTF8.GetString(bytes), uri.AbsoluteUri);
        }
        var origin = Uri.TryCreate(documentUrl, UriKind.Absolute, out var document) && document.Scheme is "http" or "https"
            ? document.GetLeftPart(UriPartial.Authority) : "null";
        // Credentials mode travels with the whole graph: the entry fetch's mode governs every
        // descendant, so the referrer is the only per-hop part that changes.
        var credentialsMode = options?.CredentialsMode ?? "same-origin";
        for (var redirects = 0; redirects <= 20; redirects++)
        {
            if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new IOException("Unsupported module URL");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var crossOrigin = origin != uri.GetLeftPart(UriPartial.Authority);
            if (crossOrigin) request.Headers.TryAddWithoutValidation("Origin", origin);
            if (options?.Referrer is { Length: > 0 } referrer &&
                Uri.TryCreate(referrer, UriKind.Absolute, out var referrerUri) && !referrerUri.IsFile)
                // Default referrer policy (strict-origin-when-cross-origin): cross-origin
                // requests carry only the origin, same-origin requests the full URL.
                request.Headers.Referrer = crossOrigin ? new Uri(origin) : referrerUri;
            // "same-origin" attaches cookies to same-origin requests only; "omit" never does;
            // "include" always does — and a credentialed cross-origin response then needs the
            // exact origin echoed back, because "*" is not valid with credentials.
            var withCredentials = credentialsMode switch
            {
                "omit" => false,
                "include" => true,
                _ => !crossOrigin,
            };
            using var response = await (withCredentials ? session.ModuleClient : session.NoCookieModuleClient)
                .SendAsync(request, cancellation).ConfigureAwait(false);
            if (crossOrigin && !CrossOriginAllowed(response, origin, withCredentials))
                throw new IOException("Module response failed CORS");
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is null) throw new IOException("Module redirect is missing Location");
                uri = new Uri(uri, response.Headers.Location); continue;
            }
            response.EnsureSuccessStatusCode();
            if (!JavaScriptMimeTypes.Contains(response.Content.Headers.ContentType?.MediaType ?? ""))
                throw new IOException("Module response does not have a JavaScript MIME type");
            return (System.Text.Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(cancellation)
                .ConfigureAwait(false)), uri.AbsoluteUri);
        }
        throw new IOException("Too many module redirects");
    }

    private static bool CrossOriginAllowed(HttpResponseMessage response, string origin, bool withCredentials)
    {
        if (!response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins)) return false;
        if (withCredentials) return origins.Any(value => value.Trim() == origin);
        return origins.Any(value => value.Trim() == "*" || value.Trim() == origin);
    }
}
