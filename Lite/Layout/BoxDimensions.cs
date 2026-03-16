using SkiaSharp;

namespace Lite.Layout;

public struct EdgeSizes
{
    public float Top;
    public float Right;
    public float Bottom;
    public float Left;
}

public struct BoxDimensions
{
    public SKRect ContentBox;
    public EdgeSizes Padding;
    public EdgeSizes Border;
    public EdgeSizes Margin;

    public readonly SKRect PaddingBox => new(
        ContentBox.Left   - Padding.Left,
        ContentBox.Top    - Padding.Top,
        ContentBox.Right  + Padding.Right,
        ContentBox.Bottom + Padding.Bottom);

    public readonly SKRect BorderBox => new(
        PaddingBox.Left   - Border.Left,
        PaddingBox.Top    - Border.Top,
        PaddingBox.Right  + Border.Right,
        PaddingBox.Bottom + Border.Bottom);

    public readonly SKRect MarginBox => new(
        BorderBox.Left   - Margin.Left,
        BorderBox.Top    - Margin.Top,
        BorderBox.Right  + Margin.Right,
        BorderBox.Bottom + Margin.Bottom);
}
