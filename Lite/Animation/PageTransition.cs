using SkiaSharp;

namespace Lite.Animation;

/// <summary>
/// A short cross-fade + slide-up animation used to reveal a freshly loaded page,
/// fading out of the loading screen the way modern browsers (and SPAs) settle a page in.
///
/// The two endpoints are captured as static snapshots, so the document is effectively
/// frozen for the (sub-300ms) duration.
/// </summary>
internal sealed class PageTransition : IDisposable
{
    private const float DurationMs = 260f;
    private const float SlidePx = 20f; // distance the new page travels up as it fades in

    private readonly SKImage _from;
    private readonly SKImage _to;
    private readonly long _startMs;

    public PageTransition(SKImage from, SKImage to)
    {
        _from = from;
        _to = to;
        _startMs = Environment.TickCount64;
    }

    public bool IsComplete => Environment.TickCount64 - _startMs >= DurationMs;

    /// <summary>
    /// Composites the current frame of the transition into a freshly allocated bitmap sized
    /// to the current client area and returns a pointer to its pixels. The bitmap is handed
    /// back via <paramref name="frame"/> so the caller can keep it alive while it is on screen.
    /// </summary>
    public IntPtr RenderFrame(int width, int height, out SKBitmap frame)
    {
        var t = Math.Clamp((Environment.TickCount64 - _startMs) / DurationMs, 0f, 1f);
        var eased = EaseOutCubic(t);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        frame = new SKBitmap(info);
        using var canvas = new SKCanvas(frame);
        canvas.Clear(SKColors.White);

        // Outgoing frame (the loading screen): fades out in place.
        using (var fromPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * (1f - eased))) })
            canvas.DrawImage(_from, 0, 0, fromPaint);

        // Incoming page: slides up into place while fading in.
        var dy = SlidePx * (1f - eased);
        using (var toPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * eased)) })
            canvas.DrawImage(_to, 0, dy, toPaint);

        return frame.GetPixels();
    }

    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    public void Dispose()
    {
        _from.Dispose();
        _to.Dispose();
    }
}
