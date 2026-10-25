using Lite;
using Lite.Layout;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Inline SVG painting: named colours, no phantom stroke, viewport clipping.</summary>
public static class SvgRenderTests
{
    [Test]
    public static void InlineSvgRect_PaintsItsNamedFillEdgeToEdgeAndClipsToTheViewport()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><body style=\"margin:0\"><svg height=\"50\" style=\"position:absolute;left:10px;top:10px\">" +
            "<rect x=\"0\" y=\"0\" width=\"200\" height=\"100\" fill=\"blue\"/></svg></body>",
            isSrcdoc: true, "http://test/", 800, 600);
        using var bitmap = Drawer.DrawToBitmap(400, 200, page.Root, new Viewport { ViewportHeight = 200 });
        // fill="blue" used to paint black, and a missing stroke drew a white 1px outline.
        True(bitmap.GetPixel(10, 10) == SKColors.Blue, $"corner must be solid blue, got {bitmap.GetPixel(10, 10)}");
        True(bitmap.GetPixel(100, 59) == SKColors.Blue, $"inside must be blue, got {bitmap.GetPixel(100, 59)}");
        True(bitmap.GetPixel(100, 61) == SKColors.White, $"the 50px viewport must clip the rect, got {bitmap.GetPixel(100, 61)}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
