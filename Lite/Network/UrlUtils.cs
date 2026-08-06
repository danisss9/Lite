namespace Lite.Network;

/// <summary>URL helpers shared by every place that resolves a document-relative reference.</summary>
internal static class UrlUtils
{
    /// <summary>
    /// True when <paramref name="url"/> starts with a scheme (RFC 3986 §3.1), i.e. it is an
    /// absolute URL that must NOT be resolved against a base.
    /// <para><see cref="Uri.TryCreate(string, UriKind, out Uri)"/> with
    /// <see cref="UriKind.Absolute"/> cannot answer this: on Unix it accepts any path-absolute
    /// reference ("/fonts/ahem.css") as an implicit <c>file:</c> URI, so a root-relative
    /// stylesheet, image or form action was handed to the HTTP client verbatim instead of being
    /// resolved against the document base — it then failed with "invalid request URI".</para>
    /// </summary>
    public static bool IsAbsolute(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        var colon = url.IndexOf(':');
        if (colon <= 0) return false;
        if (!char.IsAsciiLetter(url[0])) return false;
        for (int i = 1; i < colon; i++)
        {
            var c = url[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.') return false;
        }
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    /// <summary>
    /// Resolves <paramref name="url"/> against <paramref name="baseUrl"/>, returning null when it
    /// is relative and there is no usable base.
    /// </summary>
    public static string? Resolve(string? url, string? baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (IsAbsolute(url)) return url;
        if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) &&
            Uri.TryCreate(b, url, out var resolved))
            return resolved.ToString();
        return null;
    }
}
