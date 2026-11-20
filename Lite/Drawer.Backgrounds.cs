using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Rendering;
using SkiaSharp;

namespace Lite;

internal static partial class Drawer
{
    /// <summary>§17.5.1 layers 2 and 3: column-group, then column backgrounds, over the
    /// table's background and under the row groups, rows and cells (which paint as children).
    /// Columns generate no box of their own (display maps to none), so they paint from here.</summary>
    private static void PaintColumnBackgrounds(SKCanvas canvas, LayoutNode table)
    {
        void Paint(LayoutNode n)
        {
            if (n.Box.ContentBox.Width <= 0 || n.Box.ContentBox.Height <= 0) return;
            if (n.GetVisibility() != Visibility.Visible) return;
            var color = n.GetBackgroundColor();
            if (color.Alpha > 0)
            {
                using var paint = new SKPaint { Color = color };
                canvas.DrawRect(n.Box.ContentBox, paint);
            }
            DrawBackgroundImage(canvas, n, n.Box);
        }
        foreach (var group in table.Children.Where(TableEngine.IsColumnGroup)) Paint(group);
        foreach (var child in table.Children)
        {
            if (TableEngine.IsColumn(child)) Paint(child);
            else if (TableEngine.IsColumnGroup(child))
                foreach (var col in child.Children.Where(TableEngine.IsColumn)) Paint(col);
        }
    }

    // ---- Linear gradient ----

    private static void DrawLinearGradient(SKCanvas canvas, BoxDimensions box, LayoutNode node, LinearGradient gradient)
    {
        var rect = box.PaddingBox;
        var cx = rect.MidX;
        var cy = rect.MidY;
        var w = rect.Width;
        var h = rect.Height;

        // Convert CSS angle to radians (CSS 0deg = to top, clockwise)
        var rad = (float)((gradient.AngleDeg - 90) * Math.PI / 180.0);
        // Gradient line length: project the box diagonal onto the gradient direction
        var diagLen = (float)(Math.Abs(w * Math.Cos(rad)) + Math.Abs(h * Math.Sin(rad))) / 2f;

        var startX = cx - diagLen * (float)Math.Cos(rad);
        var startY = cy - diagLen * (float)Math.Sin(rad);
        var endX = cx + diagLen * (float)Math.Cos(rad);
        var endY = cy + diagLen * (float)Math.Sin(rad);

        // Resolve stop positions (evenly distribute missing positions)
        var count = gradient.Stops.Count;
        var colors = new SKColor[count];
        var positions = new float[count];
        for (var i = 0; i < count; i++)
        {
            colors[i] = gradient.Stops[i].Color;
            positions[i] = gradient.Stops[i].Position ?? (count > 1 ? (float)i / (count - 1) : 0f);
        }

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(startX, startY),
            new SKPoint(endX, endY),
            colors,
            positions,
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        var (rx, ry) = node.GetBorderRadius(w, h);
        if (rx > 0 || ry > 0) canvas.DrawRoundRect(rect, rx, ry, paint);
        else canvas.DrawRect(rect, paint);
    }

    // ---- Background image ----

    private static void DrawBackgroundImage(SKCanvas canvas, LayoutNode node, BoxDimensions box) =>
        DrawBackgroundImage(canvas, node, box, ReferenceEquals(node, _canvasRoot) ? _canvasArea : null);

    /// <summary>This frame's root element and the canvas it covers (§14.2: the root's
    /// background — its own, or the body's propagated one — covers the entire canvas).</summary>
    private static LayoutNode? _canvasRoot;
    private static SKRect _canvasArea;

    /// <summary><paramref name="paintArea"/> overrides where the image paints (and repeats):
    /// the canvas background (§14.2) is positioned against the root box but "covers the entire
    /// canvas", so it paints over the whole page, not just the root's padding box.</summary>
    private static void DrawBackgroundImage(SKCanvas canvas, LayoutNode node, BoxDimensions box, SKRect? paintArea)
    {
        var imageUrl = node.GetBackgroundImage();
        if (imageUrl == null) return;
        var bitmap = ResourceLoader.FetchImage(imageUrl, node.DocumentState?.BaseUrl, node.DocumentState?.Session);
        if (bitmap == null) return;

        var repeat = node.GetBackgroundRepeat();
        var (posX, posY) = node.GetBackgroundPosition();
        var (sizeW, sizeH) = node.GetBackgroundSize();

        // background-attachment: fixed — the positioning area is the viewport (in document
        // coordinates the canvas is translated by -scrollY, so the viewport's top-left sits at
        // y = scrollY; the accumulated SCROLLABLE-BOX scroll offsets shift the viewport back
        // down in local space, and a fixed background must compensate them — CSS 2.1 §14.2.1:
        // scrolling any ancestor box does not move the image). The painted background is still
        // clipped to the element's padding box, but its placement is independent of where the
        // element scrolled to.
        bool fixedBg = node.IsBackgroundFixed();
        var clipArea = paintArea ?? box.PaddingBox;
        var area = fixedBg
            ? new SKRect(_bgFixedScrollX, _viewportScrollY + _bgFixedScrollY,
                         _bgFixedScrollX + _viewportWidth, _viewportScrollY + _bgFixedScrollY + _viewportHeight)
            : box.PaddingBox;

        // Resolve image draw size
        float fontSize = node.GetFontSize();
        float drawW = bitmap.Width, drawH = bitmap.Height;
        if (sizeW == "cover" || sizeW == "contain")
        {
            var scaleX = area.Width / bitmap.Width;
            var scaleY = area.Height / bitmap.Height;
            var scale = sizeW == "cover" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            drawW = bitmap.Width * scale;
            drawH = bitmap.Height * scale;
        }
        else
        {
            if (sizeW != "auto") drawW = ResolveBgDimension(sizeW, area.Width, fontSize);
            if (sizeH != "auto") drawH = ResolveBgDimension(sizeH, area.Height, fontSize);
        }

        // Resolve position
        float offX = ResolveBgPosition(posX, area.Width, drawW, fontSize);
        float offY = ResolveBgPosition(posY, area.Height, drawH, fontSize);

        canvas.Save();
        var (rx, ry) = paintArea is null ? node.GetBorderRadius(clipArea.Width, clipArea.Height) : (0f, 0f);
        if (rx > 0 || ry > 0)
            canvas.ClipRoundRect(new SKRoundRect(clipArea, rx, ry), antialias: true);
        else
            canvas.ClipRect(clipArea);

        bool repeatX = repeat is "repeat" or "repeat-x";
        bool repeatY = repeat is "repeat" or "repeat-y";

        // Repeated tiles cover the painted area: the box's own (its padding box) or, for the
        // canvas background, the whole page.
        var tiled = paintArea ?? area;
        float startX = repeatX ? area.Left + offX - (float)Math.Ceiling((area.Left + offX - tiled.Left) / drawW) * drawW : area.Left + offX;
        float startY = repeatY ? area.Top + offY - (float)Math.Ceiling((area.Top + offY - tiled.Top) / drawH) * drawH : area.Top + offY;
        float endX = repeatX ? tiled.Right : startX + drawW;
        float endY = repeatY ? tiled.Bottom : startY + drawH;

        for (float py = startY; py < endY; py += drawH)
        {
            for (float px = startX; px < endX; px += drawW)
            {
                var dest = new SKRect(px, py, px + drawW, py + drawH);
                canvas.DrawBitmap(bitmap, dest);
                if (!repeatX) break;
            }
            if (!repeatY) break;
        }

        canvas.Restore();
    }

    private static float ResolveBgDimension(string val, float total, float fontSize)
    {
        val = val.Trim();
        if (val.EndsWith("px") && float.TryParse(val[..^2], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
        if (val.EndsWith('%') && float.TryParse(val[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct)) return pct / 100f * total;
        // Non-px lengths (em, pt, …) resolve through the shared unit table against the
        // element's font size; percentages already resolved above.
        if (CssUnits.TryParse(val, fontSize, total, total, total, out var length)) return length;
        return total;
    }

    private static float ResolveBgPosition(string val, float areaSize, float imageSize, float fontSize)
    {
        val = val.Trim();
        // A percentage aligns pct% of the image with pct% of the area — the offset basis is
        // (area − image), not the area itself (CSS 2.1 §14.2.1).
        if (val.EndsWith('%') && float.TryParse(val[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return (areaSize - imageSize) * pct / 100f;
        if (CssUnits.TryParse(val, fontSize, areaSize, areaSize, areaSize, out var length))
            return length;
        return val switch
        {
            "left" or "top" => 0f,
            "center" => (areaSize - imageSize) / 2f,
            "right" or "bottom" => areaSize - imageSize,
            _ => 0f,
        };
    }
}
