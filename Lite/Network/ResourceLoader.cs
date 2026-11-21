using Lite.Media;
using SkiaSharp;

namespace Lite.Network;

internal static class ResourceLoader
{
    private static readonly BrowserSession FallbackSession = new();

    internal static SKBitmap? FetchImage(string src, string? baseUrl, BrowserSession? session = null)
    {
        session ??= FallbackSession;
        var cache = session.Images;
        // data: URI support
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            lock (cache) if (cache.TryGetValue(src, out var cachedData)) return Shown(cachedData);
            var bitmap = DecodeDataUri(src, session);
            lock (cache) cache[src] = bitmap;
            return Shown(bitmap);
        }

        var resolved = ResolveUrl(src, baseUrl);
        if (resolved == null) return null;

        lock (cache) if (cache.TryGetValue(resolved, out var cached)) return Shown(cached);

        try
        {
            var bytes = session.Client.GetByteArrayAsync(resolved).Result;
            var bitmap = RememberVector(session, resolved, bytes) ? null : DecodeFetchedImage(bytes);
            lock (cache) cache[resolved] = bitmap;
            return Shown(bitmap);
        }
        catch (Exception error)
        {
            session.Diagnostics.Enqueue($"image {resolved}: {error.Message}");
            lock (cache) cache[resolved] = null;
            return null;
        }
    }

    /// <summary>Fetches a URL and returns the response body as a string.</summary>
    internal static string? FetchText(string url, string? baseUrl, BrowserSession? session = null)
    {
        session ??= FallbackSession;
        var resolved = ResolveUrl(url, baseUrl);
        if (resolved == null) return null;
        try { return session.Client.GetStringAsync(resolved).Result; }
        catch (Exception error) { session.Diagnostics.Enqueue($"text {resolved}: {error.Message}"); return null; }
    }

    /// <summary>Fetches a URL and returns the response body as bytes.</summary>
    internal static byte[]? FetchBytes(string url, string? baseUrl, BrowserSession? session = null)
    {
        session ??= FallbackSession;
        var resolved = ResolveUrl(url, baseUrl);
        if (resolved == null) return null;
        try { return session.Client.GetByteArrayAsync(resolved).Result; }
        catch (Exception error) { session.Diagnostics.Enqueue($"bytes {resolved}: {error.Message}"); return null; }
    }

    private static SKBitmap? DecodeDataUri(string dataUri, BrowserSession session)
    {
        // data:[<mediatype>][;base64],<data> — DataUri handles percent-encoded base64 payloads.
        if (!DataUri.TryDecodeBytes(dataUri, out var bytes, out _)) return null;
        try { return RememberVector(session, dataUri, bytes) ? null : DecodeFetchedImage(bytes); }
        catch { return null; }
    }

    /// <summary>SVG documents fetched as images, per session and URL. SKCodec decodes no SVG,
    /// so <see cref="FetchImage"/> yields null for one; a painter that can size a vector image
    /// (the background painter) asks for it through <see cref="FetchSvgImage"/>.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BrowserSession,
        Dictionary<string, SvgImage>> Vectors = new();

    /// <summary>Keeps <paramref name="bytes"/> as a vector image when they are an SVG document
    /// (checked before raster decoding, which throws on SVG bytes); true when they were.</summary>
    private static bool RememberVector(BrowserSession session, string key, byte[] bytes)
    {
        if (SvgImage.TryParse(bytes) is not { } svg) return false;
        var map = Vectors.GetOrCreateValue(session);
        lock (map) map[key] = svg;
        return true;
    }

    /// <summary>The SVG image at <paramref name="src"/>, when it is one (an image whose bytes
    /// did not decode as a raster). The fetch goes through <see cref="FetchImage"/>, so it is
    /// shared with the raster cache.</summary>
    internal static SvgImage? FetchSvgImage(string src, string? baseUrl, BrowserSession? session = null)
    {
        session ??= FallbackSession;
        var key = src.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? src : ResolveUrl(src, baseUrl);
        if (key is null) return null;
        var map = Vectors.GetOrCreateValue(session);
        lock (map) if (map.TryGetValue(key, out var known)) return known;
        if (FetchImage(src, baseUrl, session) is not null) return null;
        lock (map) return map.GetValueOrDefault(key);
    }

    /// <summary>The painters fetch backgrounds and list images on every paint, so a fetch
    /// marks an animated image as shown (<see cref="AnimatedImages.Touch"/>).</summary>
    private static SKBitmap? Shown(SKBitmap? bitmap)
    {
        AnimatedImages.Touch(bitmap);
        return bitmap;
    }

    /// <summary>A fetched image: a multi-frame one (an animated GIF) becomes an animated
    /// surface showing frame 0; anything else decodes as <see cref="DecodeImageBytes"/>.</summary>
    internal static SKBitmap? DecodeFetchedImage(byte[] bytes)
    {
        using (var codec = SKCodec.Create(new MemoryStream(bytes)))
            if (codec is { FrameCount: > 1 } && AnimatedImages.Decode(codec, Environment.TickCount64, bytes) is { } animated)
                return animated;
        return DecodeImageBytes(bytes);
    }

    /// <summary>
    /// Decodes encoded image bytes into a bitmap whose pixels are straight (un-premultiplied)
    /// alpha, so that semi-/fully-transparent PNGs composite correctly with SKCanvas source-over
    /// (Acid2's eyes are two offset partly-transparent PNGs that must overlap into solid yellow).
    /// </summary>
    internal static SKBitmap? DecodeImageBytes(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        if (codec is null) return SKBitmap.Decode(bytes);
        var info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;
        bitmap.Dispose();
        return SKBitmap.Decode(bytes);
    }

    private static string? ResolveUrl(string src, string? baseUrl) => UrlUtils.Resolve(src, baseUrl);
}
