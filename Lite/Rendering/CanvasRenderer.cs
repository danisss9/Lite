using Lite.Models;
using SkiaSharp;

namespace Lite.Rendering;

/// <summary>Renders the bitmap from a CanvasRenderingContext2D onto the page.</summary>
internal static class CanvasRenderer
{
    internal static void Render(SKCanvas canvas, LayoutNode canvasNode)
    {
        var box = canvasNode.Box;
        // If JS has drawn onto this canvas, its bitmap is stored in canvasNode.Image
        if (canvasNode.Image != null)
        {
            canvas.DrawBitmap(canvasNode.Image, box.ContentBox);
        }
        else
        {
            // Draw empty canvas background (white by default)
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(box.ContentBox, paint);
        }
    }
}
