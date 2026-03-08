namespace Lite.Layout;

internal class Viewport
{
    public float ScrollY { get; private set; }
    public float ContentHeight { get; set; }
    public float ViewportHeight { get; set; }

    private float MaxScroll => Math.Max(0, ContentHeight - ViewportHeight);

    public void ScrollBy(float delta) => ScrollTo(ScrollY + delta);

    public void ScrollTo(float y) => ScrollY = Math.Clamp(y, 0, MaxScroll);
}
