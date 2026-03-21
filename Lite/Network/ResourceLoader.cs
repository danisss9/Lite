using SkiaSharp;

namespace Lite.Network;

internal static class ResourceLoader
{
    private static readonly HttpClient _client = new();
    private static readonly Dictionary<string, SKBitmap?> _cache = [];

    internal static SKBitmap? FetchImage(string src, string? baseUrl)
    {
        // data: URI support
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (_cache.TryGetValue(src, out var cachedData)) return cachedData;
            var bitmap = DecodeDataUri(src);
            _cache[src] = bitmap;
            return bitmap;
        }

        var resolved = ResolveUrl(src, baseUrl);
        if (resolved == null) return null;

        if (_cache.TryGetValue(resolved, out var cached)) return cached;

        try
        {
            var bytes = _client.GetByteArrayAsync(resolved).Result;
            var bitmap = SKBitmap.Decode(bytes);
            _cache[resolved] = bitmap;
            return bitmap;
        }
        catch
        {
            _cache[resolved] = null;
            return null;
        }
    }

    /// <summary>Fetches a URL and returns the response body as a string.</summary>
    internal static string? FetchText(string url, string? baseUrl)
    {
        var resolved = ResolveUrl(url, baseUrl);
        if (resolved == null) return null;
        try { return _client.GetStringAsync(resolved).Result; }
        catch { return null; }
    }

    /// <summary>Fetches a URL and returns the response body as bytes.</summary>
    internal static byte[]? FetchBytes(string url, string? baseUrl)
    {
        var resolved = ResolveUrl(url, baseUrl);
        if (resolved == null) return null;
        try { return _client.GetByteArrayAsync(resolved).Result; }
        catch { return null; }
    }

    private static SKBitmap? DecodeDataUri(string dataUri)
    {
        // data:[<mediatype>][;base64],<data>
        try
        {
            var commaIdx = dataUri.IndexOf(',');
            if (commaIdx < 0) return null;
            var meta = dataUri[5..commaIdx]; // after "data:"
            var data = dataUri[(commaIdx + 1)..];
            byte[] bytes;
            if (meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
                bytes = Convert.FromBase64String(data);
            else
                bytes = System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
            return SKBitmap.Decode(bytes);
        }
        catch { return null; }
    }

    private static string? ResolveUrl(string src, string? baseUrl)
    {
        if (Uri.TryCreate(src, UriKind.Absolute, out _)) return src;
        if (baseUrl != null && Uri.TryCreate(new Uri(baseUrl), src, out var resolved))
            return resolved.ToString();
        return null;
    }
}
