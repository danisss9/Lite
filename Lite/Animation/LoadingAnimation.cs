using SkiaSharp;

namespace Lite.Animation;

/// <summary>
/// The browser-style loading indicator shown while a navigation is fetched, parsed and
/// rendered on a background thread. The page being left is dimmed as a backdrop and an
/// indeterminate accent bar sweeps across the top of the window until the new page is ready.
/// </summary>
internal sealed class LoadingAnimation : IDisposable
{
    private const float BarHeight = 3f;
    private const float SweepSeconds = 1.15f;   // time for one left→right pass of the bar
    private static readonly SKColor AccentColor = new(0, 120, 215);
    private static readonly SKColor TrackColor = new(0, 120, 215, 45);

    private readonly SKImage? _backdrop; // snapshot of the page we're navigating away from
    private readonly long _startMs;

    public LoadingAnimation(SKImage? backdrop)
    {
        _backdrop = backdrop;
        _startMs = Environment.TickCount64;
    }

    public long ElapsedMs => Environment.TickCount64 - _startMs;

    /// <summary>
    /// Renders the current loading frame into a freshly allocated bitmap sized to the current
    /// client area. The bitmap is handed back via <paramref name="frame"/> so the caller can
    /// keep it alive while it is on screen.
    /// </summary>
    public IntPtr RenderFrame(int width, int height, out SKBitmap frame)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        frame = new SKBitmap(info);
        using var canvas = new SKCanvas(frame);
        canvas.Clear(SKColors.White);

        // Dimmed snapshot of the outgoing page, so it's clearly inactive while loading.
        if (_backdrop != null)
        {
            canvas.DrawImage(_backdrop, 0, 0);
            using var dim = new SKPaint { Color = SKColors.White.WithAlpha(120) };
            canvas.DrawRect(0, 0, width, height, dim);
        }

        // Track behind the indeterminate bar.
        using (var track = new SKPaint { Color = TrackColor, IsAntialias = true })
            canvas.DrawRect(0, 0, width, BarHeight, track);

        // Sweeping accent segment, eased so it accelerates and decelerates across the width.
        var p = (ElapsedMs / 1000f % SweepSeconds) / SweepSeconds;
        var eased = EaseInOutCubic(p);
        var segW = width * 0.30f;
        var x = eased * (width + segW) - segW; // slides from just off-left to just off-right
        using (var seg = new SKPaint { Color = AccentColor, IsAntialias = true })
            canvas.DrawRoundRect(new SKRect(x, 0, x + segW, BarHeight), BarHeight / 2f, BarHeight / 2f, seg);

        return frame.GetPixels();
    }

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;

    public void Dispose() => _backdrop?.Dispose();
}
