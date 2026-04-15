namespace Lite.Layout;

/// <summary>
/// Tracks scroll state for an element with overflow:scroll or overflow:auto.
/// </summary>
public class ElementScrollState
{
    public float ScrollY { get; private set; }
    public float ContentHeight { get; set; }
    public float ContainerHeight { get; set; }

    public float MaxScroll => Math.Max(0f, ContentHeight - ContainerHeight);
    public bool NeedsScrollbar => ContentHeight > ContainerHeight;

    public const float BarWidth = 6f;
    public const float BarMargin = 2f;

    public void ScrollBy(float delta)
    {
        ScrollY = Math.Clamp(ScrollY + delta, 0f, MaxScroll);
    }

    public void ScrollTo(float y)
    {
        ScrollY = Math.Clamp(y, 0f, MaxScroll);
    }

    public float ThumbHeight
    {
        get
        {
            if (ContentHeight <= 0f) return 0f;
            return Math.Max(20f, ContainerHeight * (ContainerHeight / ContentHeight));
        }
    }

    public float ThumbTop(float containerTop)
    {
        if (MaxScroll <= 0f) return containerTop;
        var trackH = ContainerHeight - ThumbHeight;
        return containerTop + trackH * (ScrollY / MaxScroll);
    }

    public bool HitThumb(float localX, float localY, float containerRight, float containerTop)
    {
        if (!NeedsScrollbar) return false;
        var barLeft = containerRight - BarWidth - BarMargin;
        var thumbTop = ThumbTop(containerTop);
        return localX >= barLeft && localX <= containerRight
               && localY >= thumbTop && localY <= thumbTop + ThumbHeight;
    }

    public bool HitTrack(float localX, float containerRight)
    {
        if (!NeedsScrollbar) return false;
        var barLeft = containerRight - BarWidth - BarMargin;
        return localX >= barLeft && localX <= containerRight;
    }

    public float ScrollYFromThumbTop(float screenY, float containerTop, float grabOffset)
    {
        var trackH = ContainerHeight - ThumbHeight;
        if (trackH <= 0f) return 0f;
        var ratio = (screenY - grabOffset - containerTop) / trackH;
        return ratio * MaxScroll;
    }
}
