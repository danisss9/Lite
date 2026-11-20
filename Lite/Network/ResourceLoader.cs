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
            var bitmap = DecodeDataUri(src);
            lock (cache) cache[src] = bitmap;
            return Shown(bitmap);
        }

        var resolved = ResolveUrl(src, baseUrl);
        if (resolved == null) return null;

        lock (cache) if (cache.TryGetValue(resolved, out var cached)) return Shown(cached);

        try
        {
            var bytes = session.Client.GetByteArrayAsync(resolved).Result;
            var bitmap = DecodeFetchedImage(bytes);
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

    private static SKBitmap? DecodeDataUri(string dataUri)
    {
        // data:[<mediatype>][;base64],<data> — DataUri handles percent-encoded base64 payloads.
        if (!DataUri.TryDecodeBytes(dataUri, out var bytes, out _)) return null;
        try { return DecodeFetchedImage(bytes); }
        catch { return null; }
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
