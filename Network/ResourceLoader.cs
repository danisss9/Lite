using SkiaSharp;

namespace Lite.Network;

internal static class ResourceLoader
{
    private static readonly HttpClient _client = new();
    private static readonly Dictionary<string, SKBitmap?> _cache = [];

    internal static SKBitmap? FetchImage(string src, string? baseUrl)
    {
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

    private static string? ResolveUrl(string src, string? baseUrl)
    {
        if (Uri.TryCreate(src, UriKind.Absolute, out _)) return src;
        if (baseUrl != null && Uri.TryCreate(new Uri(baseUrl), src, out var resolved))
            return resolved.ToString();
        return null;
    }
}
