namespace Lite.Layout;

internal class Viewport
{
    public float ScrollY { get; private set; }
    public float ContentHeight { get; set; }
    public float ViewportHeight { get; set; }

    private float MaxScroll => Math.Max(0, ContentHeight - ViewportHeight);

    public void ScrollBy(float delta) => ScrollTo(ScrollY + delta);

    public void ScrollTo(float y) => ScrollY = Math.Clamp(y, 0, MaxScroll);

    // ── Scrollbar geometry (must match Drawer.DrawScrollbar constants) ──

    public const float BarWidth = 6f;
    public const float BarMargin = 2f;

    public bool HasScrollbar => ContentHeight > ViewportHeight;

    /// <summary>X coordinate of the scrollbar track's left edge.</summary>
    public float TrackX(float windowWidth) => windowWidth - BarWidth - BarMargin;

    public float ThumbHeight => HasScrollbar
        ? Math.Max(ViewportHeight * (ViewportHeight / ContentHeight), 24f)
        : 0f;

    public float ThumbTop => HasScrollbar
        ? ScrollY / ContentHeight * ViewportHeight
        : 0f;

    /// <summary>Returns true if the screen-space point (x, y) is inside the scrollbar thumb.</summary>
    public bool HitThumb(float x, float y, float windowWidth)
    {
        if (!HasScrollbar) return false;
        var tx = TrackX(windowWidth);
        var tt = ThumbTop + BarMargin;
        var th = ThumbHeight - BarMargin * 2;
        return x >= tx && x <= tx + BarWidth && y >= tt && y <= tt + th;
    }

    /// <summary>Returns true if the screen-space point is inside the scrollbar track area.</summary>
    public bool HitTrack(float x, float windowWidth)
    {
        if (!HasScrollbar) return false;
        return x >= TrackX(windowWidth);
    }

    /// <summary>
    /// Given a screen Y during a drag, converts it to a ScrollY value.
    /// <paramref name="grabOffset"/> is the Y offset within the thumb where the drag started.
    /// </summary>
    public float ScrollYFromThumbTop(float screenY, float grabOffset)
    {
        var thumbTop = screenY - grabOffset;
        // thumbTop / trackH == scrollY / contentHeight
        return thumbTop / ViewportHeight * ContentHeight;
    }
}
