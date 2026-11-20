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
    // -------------------------------------------------------------------------
    // Shadows
    // -------------------------------------------------------------------------

    private static void DrawBoxShadows(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var shadows = node.GetBoxShadows();
        if (shadows.Count == 0) return;

        var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);

        // Paint shadows in reverse order (last layer first, per CSS spec)
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            var s = shadows[i];
            if (s.Inset) continue;   // inset shadows not yet supported

            var sigma = s.Blur / 2f;
            var shadowRect = SKRect.Inflate(box.PaddingBox, s.Spread, s.Spread);
            shadowRect.Offset(s.OffsetX, s.OffsetY);
            var srx = Math.Max(0, rx + s.Spread);
            var sry = Math.Max(0, ry + s.Spread);

            using var paint = new SKPaint
            {
                Color = s.Color,
                IsAntialias = true,
                MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
            };

            if (srx > 0 || sry > 0) canvas.DrawRoundRect(shadowRect, srx, sry, paint);
            else canvas.DrawRect(shadowRect, paint);
        }
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static void DrawBorders(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var bw = box.Border;
        var (rx, ry) = node.GetBorderRadius(box.BorderBox.Width, box.BorderBox.Height);

        if (rx > 0 || ry > 0)
        {
            // With border-radius draw a single stroked rounded rect using top-border values
            var maxWidth = Math.Max(Math.Max(bw.Top, bw.Right), Math.Max(bw.Bottom, bw.Left));
            if (maxWidth <= 0) return;
            var style = node.GetBorderStyleTop();
            if (style == BorderStyle.None || style == BorderStyle.Hidden) return;
            using var p = new SKPaint
            {
                Color = node.GetBorderTopColor(),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = maxWidth,
                IsAntialias = true,
            };
            ApplyBorderStyle(p, style);
            var inset = maxWidth / 2f;
            var r = SKRect.Inflate(box.BorderBox, -inset, -inset);
            if (r.Width <= 0 || r.Height <= 0) return;
            canvas.DrawRoundRect(r, Math.Max(0, rx - inset), Math.Max(0, ry - inset), p);
            return;
        }

        // Collapsed tables: each edge paints the §17.6.2.1 dominant border, which may come
        // from an adjoining cell or the table rather than this node's own properties.
        var cw = node.CollapsedEdgeBorders;
        (float width, BorderStyle style, SKColor color) Top() =>
            cw is { } && cw.TryGetValue("top", out var t) ? (t.Width, t.Style, t.Color) : (bw.Top, node.GetBorderStyleTop(), node.GetBorderTopColor());
        (float width, BorderStyle style, SKColor color) Right() =>
            cw is { } && cw.TryGetValue("right", out var r) ? (r.Width, r.Style, r.Color) : (bw.Right, node.GetBorderStyleRight(), node.GetBorderRightColor());
        (float width, BorderStyle style, SKColor color) Bottom() =>
            cw is { } && cw.TryGetValue("bottom", out var b) ? (b.Width, b.Style, b.Color) : (bw.Bottom, node.GetBorderStyleBottom(), node.GetBorderBottomColor());
        (float width, BorderStyle style, SKColor color) Left() =>
            cw is { } && cw.TryGetValue("left", out var l) ? (l.Width, l.Style, l.Color) : (bw.Left, node.GetBorderStyleLeft(), node.GetBorderLeftColor());
        var topE = Top(); var rightE = Right(); var bottomE = Bottom(); var leftE = Left();

        // §17.6.2: collapsed borders are centred on the grid lines. A collapsed cell's border box
        // holds half of each winning border, so its edges ARE the grid lines: paint each winner
        // centred there (the half outside is the neighbour's or the table's half), running past
        // the corners far enough to meet the crossing borders.
        if (cw is not null)
        {
            var g = box.BorderBox;
            DrawBorderSide(canvas, g.Left - leftE.width / 2, g.Top, g.Right + rightE.width / 2, g.Top,
                            topE.width, topE.color, topE.style);
            DrawBorderSide(canvas, g.Right, g.Top - topE.width / 2, g.Right, g.Bottom + bottomE.width / 2,
                            rightE.width, rightE.color, rightE.style);
            DrawBorderSide(canvas, g.Left - leftE.width / 2, g.Bottom, g.Right + rightE.width / 2, g.Bottom,
                            bottomE.width, bottomE.color, bottomE.style);
            DrawBorderSide(canvas, g.Left, g.Top - topE.width / 2, g.Left, g.Bottom + bottomE.width / 2,
                            leftE.width, leftE.color, leftE.style);
            return;
        }

        var outer = node.CollapsedOuterEdges;
        LayoutNode? tableNode = null;
        if (outer is not null)
            for (var a = node.Parent; a is not null; a = a.Parent)
                if (a.GetDisplay() == DisplayType.Table) { tableNode = a; break; }
        var tableBox = tableNode?.Box.BorderBox ?? box.BorderBox;

        var topY = outer?.Contains("top") == true ? tableBox.Top : box.BorderBox.Top;
        var bottomY = outer?.Contains("bottom") == true ? tableBox.Bottom : box.BorderBox.Bottom;
        var leftX = outer?.Contains("left") == true ? tableBox.Left : box.BorderBox.Left;
        var rightX = outer?.Contains("right") == true ? tableBox.Right : box.BorderBox.Right;

        DrawBorderSide(canvas, leftX, topY + topE.width / 2,
                        rightX, topY + topE.width / 2,
                        topE.width, topE.color, topE.style);
        DrawBorderSide(canvas, rightX - rightE.width / 2, topY,
                        rightX - rightE.width / 2, bottomY,
                        rightE.width, rightE.color, rightE.style);
        DrawBorderSide(canvas, leftX, bottomY - bottomE.width / 2,
                        rightX, bottomY - bottomE.width / 2,
                        bottomE.width, bottomE.color, bottomE.style);
        DrawBorderSide(canvas, leftX + leftE.width / 2, topY,
                        leftX + leftE.width / 2, bottomY,
                        leftE.width, leftE.color, leftE.style);
    }

    private static void DrawBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2,
                                        float width, SKColor color, BorderStyle style)
    {
        if (width <= 0 || style == BorderStyle.None || style == BorderStyle.Hidden) return;

        if (style == BorderStyle.Double && width >= 3f)
        {
            // Draw two lines with a gap in between
            var lineW = Math.Max(1f, width / 3f);
            var offset = width / 2f - lineW / 2f;
            var isHorizontal = Math.Abs(y1 - y2) < 0.01f;
            using var p = new SKPaint { Color = color, StrokeWidth = lineW, IsAntialias = true };
            if (isHorizontal)
            {
                canvas.DrawLine(x1, y1 - offset, x2, y2 - offset, p);
                canvas.DrawLine(x1, y1 + offset, x2, y2 + offset, p);
            }
            else
            {
                canvas.DrawLine(x1 - offset, y1, x2 - offset, y2, p);
                canvas.DrawLine(x1 + offset, y1, x2 + offset, y2, p);
            }
            return;
        }

        if (style == BorderStyle.Groove || style == BorderStyle.Ridge)
        {
            var halfW = width / 2f;
            var isHorizontal = Math.Abs(y1 - y2) < 0.01f;
            var dark = DarkenColor(color, 0.6f);
            var light = LightenColor(color, 1.4f);
            var c1 = style == BorderStyle.Groove ? dark : light;
            var c2 = style == BorderStyle.Groove ? light : dark;
            using var p1 = new SKPaint { Color = c1, StrokeWidth = halfW, IsAntialias = true };
            using var p2 = new SKPaint { Color = c2, StrokeWidth = halfW, IsAntialias = true };
            if (isHorizontal)
            {
                canvas.DrawLine(x1, y1 - halfW / 2f, x2, y2 - halfW / 2f, p1);
                canvas.DrawLine(x1, y1 + halfW / 2f, x2, y2 + halfW / 2f, p2);
            }
            else
            {
                canvas.DrawLine(x1 - halfW / 2f, y1, x2 - halfW / 2f, y2, p1);
                canvas.DrawLine(x1 + halfW / 2f, y1, x2 + halfW / 2f, y2, p2);
            }
            return;
        }

        if (style == BorderStyle.Inset || style == BorderStyle.Outset)
        {
            var isTop = Math.Abs(y1 - y2) < 0.01f && y1 < (y1 + y2) / 2f + 1; // approximate
            var isLeft = Math.Abs(x1 - x2) < 0.01f && x1 < (x1 + x2) / 2f + 1;
            bool darken = style == BorderStyle.Inset ? (isTop || isLeft) : !(isTop || isLeft);
            var c = darken ? DarkenColor(color, 0.6f) : LightenColor(color, 1.4f);
            using var p = new SKPaint { Color = c, StrokeWidth = width, IsAntialias = true };
            canvas.DrawLine(x1, y1, x2, y2, p);
            return;
        }

        // A solid side is an axis-aligned rectangle, so fill it directly instead of stroking a
        // centred line. A stroke spreads width/2 to each side of the centre and antialiases the
        // result, which leaves a soft fringe on the two long edges; a background of the same size
        // is a crisp rect fill. CSS 2.1 reftests constantly compare a bordered box against a
        // background-filled one, so the fringe alone fails them. Dashed/dotted still need the
        // stroke path effect.
        if (style == BorderStyle.Solid)
        {
            var half = width / 2f;
            var rect = Math.Abs(y1 - y2) < 0.01f
                ? new SKRect(Math.Min(x1, x2), y1 - half, Math.Max(x1, x2), y1 + half)   // horizontal
                : new SKRect(x1 - half, Math.Min(y1, y2), x1 + half, Math.Max(y1, y2));  // vertical
            // Snapped to whole device pixels like the background fill above, so a border and an
            // equally-sized background agree pixel for pixel on a fractional boundary.
            using var fill = new SKPaint { Color = color, IsAntialias = false };
            canvas.DrawRect(rect, fill);
            return;
        }

        using var paint = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true };
        ApplyBorderStyle(paint, style);
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void ApplyBorderStyle(SKPaint paint, BorderStyle style)
    {
        switch (style)
        {
            case BorderStyle.Dotted:
                paint.PathEffect = SKPathEffect.CreateDash([paint.StrokeWidth, paint.StrokeWidth * 2], 0);
                break;
            case BorderStyle.Dashed:
                paint.PathEffect = SKPathEffect.CreateDash([paint.StrokeWidth * 3, paint.StrokeWidth * 2], 0);
                break;
        }
    }

    private static SKColor DarkenColor(SKColor c, float factor)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red * factor, 0, 255),
            (byte)Math.Clamp(c.Green * factor, 0, 255),
            (byte)Math.Clamp(c.Blue * factor, 0, 255),
            c.Alpha);
    }

    // CSS 2.1 §8.5.3: 3D border styles ('groove', 'ridge', 'inset', 'outset') derive
    // light and dark bevel shades from the border color. Multiplicative scaling alone leaves
    // black (#000000) unchanged, making default-color groove/ridge borders indistinguishable
    // from solid (groove-default, ridge-default). Lift near-black colors toward white so the
    // two bevel halves stay visually distinct while preserving pure-channel scaling on hues.
    private static SKColor LightenColor(SKColor c, float factor)
    {
        if (c.Red + c.Green + c.Blue < 96)
        {
            const float lift = 0.4f;
            return new SKColor(
                (byte)Math.Clamp(c.Red + (255 - c.Red) * lift, 0, 255),
                (byte)Math.Clamp(c.Green + (255 - c.Green) * lift, 0, 255),
                (byte)Math.Clamp(c.Blue + (255 - c.Blue) * lift, 0, 255),
                c.Alpha);
        }
        return new SKColor(
            (byte)Math.Clamp(c.Red * factor, 0, 255),
            (byte)Math.Clamp(c.Green * factor, 0, 255),
            (byte)Math.Clamp(c.Blue * factor, 0, 255),
            c.Alpha);
    }

    // ---- CSS filter ----

    private static SKPaint? BuildFilterPaint(LayoutNode node)
    {
        var raw = node.GetFilter();
        if (raw == null) return null;

        SKImageFilter? imageFilter = null;
        SKColorFilter? colorFilter = null;

        // Parse each filter function
        var span = raw.AsSpan();
        while (span.Length > 0)
        {
            span = span.TrimStart();
            var parenIdx = span.IndexOf('(');
            if (parenIdx < 0) break;
            var funcName = span[..parenIdx].Trim().ToString().ToLowerInvariant();
            var closeIdx = span.IndexOf(')');
            if (closeIdx < 0) break;
            var argStr = span[(parenIdx + 1)..closeIdx].ToString().Trim();
            span = span[(closeIdx + 1)..];

            switch (funcName)
            {
                case "blur":
                    if (StyleExtensions.TryParseLengthPx(argStr, out var sigma))
                    {
                        var blur = SKImageFilter.CreateBlur(sigma, sigma);
                        imageFilter = imageFilter != null ? SKImageFilter.CreateCompose(imageFilter, blur) : blur;
                    }
                    break;
                case "grayscale":
                    {
                        var amt = ParseFilterAmount(argStr, 1f);
                        amt = Math.Clamp(amt, 0f, 1f);
                        var inv = 1f - amt;
                        var cf = SKColorFilter.CreateColorMatrix([
                            0.2126f + 0.7874f * inv, 0.7152f - 0.7152f * inv, 0.0722f - 0.0722f * inv, 0, 0,
                            0.2126f - 0.2126f * inv, 0.7152f + 0.2848f * inv, 0.0722f - 0.0722f * inv, 0, 0,
                            0.2126f - 0.2126f * inv, 0.7152f - 0.7152f * inv, 0.0722f + 0.9278f * inv, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "sepia":
                    {
                        var amt = ParseFilterAmount(argStr, 1f);
                        amt = Math.Clamp(amt, 0f, 1f);
                        var inv = 1f - amt;
                        var cf = SKColorFilter.CreateColorMatrix([
                            0.393f + 0.607f * inv, 0.769f - 0.769f * inv, 0.189f - 0.189f * inv, 0, 0,
                            0.349f - 0.349f * inv, 0.686f + 0.314f * inv, 0.168f - 0.168f * inv, 0, 0,
                            0.272f - 0.272f * inv, 0.534f - 0.534f * inv, 0.131f + 0.869f * inv, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "brightness":
                    {
                        var b = ParseFilterAmount(argStr, 1f);
                        var cf = SKColorFilter.CreateColorMatrix([
                            b, 0, 0, 0, 0,
                            0, b, 0, 0, 0,
                            0, 0, b, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "contrast":
                    {
                        var c = ParseFilterAmount(argStr, 1f);
                        var t = (1f - c) / 2f;
                        var cf = SKColorFilter.CreateColorMatrix([
                            c, 0, 0, 0, t,
                            0, c, 0, 0, t,
                            0, 0, c, 0, t,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "saturate":
                    {
                        var s = ParseFilterAmount(argStr, 1f);
                        var inv = 1f - s;
                        var cf = SKColorFilter.CreateColorMatrix([
                            0.2126f + 0.7874f * s, 0.7152f - 0.7152f * s, 0.0722f - 0.0722f * s, 0, 0,
                            0.2126f - 0.2126f * s, 0.7152f + 0.2848f * s, 0.0722f - 0.0722f * s, 0, 0,
                            0.2126f - 0.2126f * s, 0.7152f - 0.7152f * s, 0.0722f + 0.9278f * s, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "hue-rotate":
                    {
                        if (StyleExtensions.TryParseAngle(argStr, out var deg2))
                        {
                            var rad = deg2 * MathF.PI / 180f;
                            var cos = MathF.Cos(rad);
                            var sin = MathF.Sin(rad);
                            var cf = SKColorFilter.CreateColorMatrix([
                                0.213f + cos * 0.787f - sin * 0.213f, 0.715f - cos * 0.715f - sin * 0.715f, 0.072f - cos * 0.072f + sin * 0.928f, 0, 0,
                                0.213f - cos * 0.213f + sin * 0.143f, 0.715f + cos * 0.285f + sin * 0.140f, 0.072f - cos * 0.072f - sin * 0.283f, 0, 0,
                                0.213f - cos * 0.213f - sin * 0.787f, 0.715f - cos * 0.715f + sin * 0.715f, 0.072f + cos * 0.928f + sin * 0.072f, 0, 0,
                                0, 0, 0, 1, 0
                            ]);
                            colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                        }
                    }
                    break;
                case "invert":
                    {
                        var amt = ParseFilterAmount(argStr, 1f);
                        amt = Math.Clamp(amt, 0f, 1f);
                        var cf = SKColorFilter.CreateColorMatrix([
                            1f - 2f * amt, 0, 0, 0, amt,
                            0, 1f - 2f * amt, 0, 0, amt,
                            0, 0, 1f - 2f * amt, 0, amt,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "opacity":
                    {
                        var a = ParseFilterAmount(argStr, 1f);
                        a = Math.Clamp(a, 0f, 1f);
                        var cf = SKColorFilter.CreateColorMatrix([
                            1, 0, 0, 0, 0,
                            0, 1, 0, 0, 0,
                            0, 0, 1, 0, 0,
                            0, 0, 0, a, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
            }
        }

        if (imageFilter == null && colorFilter == null) return null;

        var paint = new SKPaint { IsAntialias = true };
        if (colorFilter != null)
        {
            var cfImage = SKImageFilter.CreateColorFilter(colorFilter);
            imageFilter = imageFilter != null ? SKImageFilter.CreateCompose(imageFilter, cfImage) : cfImage;
        }
        paint.ImageFilter = imageFilter;
        return paint;
    }

    private static float ParseFilterAmount(string arg, float defaultIfEmpty)
    {
        arg = arg.Trim();
        if (string.IsNullOrEmpty(arg)) return defaultIfEmpty;
        if (arg.EndsWith('%'))
        {
            if (float.TryParse(arg[..^1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
                return pct / 100f;
        }
        if (float.TryParse(arg, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var val))
            return val;
        return defaultIfEmpty;
    }

    // ---- Outline drawing ----

    private static void DrawOutline(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var style = node.GetOutlineStyle();
        if (style == BorderStyle.None) return;
        var width = node.GetOutlineWidth();
        if (width <= 0) return;
        var color = node.GetOutlineColor();
        var offset = node.GetOutlineOffset();

        // A solid outline is the same filled ring a solid border is (see DrawBorderSide): the
        // outline-width tests compare an outline against an equal border, and a centred stroke
        // rasterises a different fringe at fractional offsets.
        if (style == BorderStyle.Solid)
        {
            var inner = SKRect.Inflate(box.BorderBox, offset, offset);
            var outer = SKRect.Inflate(inner, width, width);
            using var fill = new SKPaint { Color = color, IsAntialias = false };
            canvas.DrawRect(new SKRect(outer.Left, outer.Top, outer.Right, inner.Top), fill);
            canvas.DrawRect(new SKRect(outer.Left, inner.Bottom, outer.Right, outer.Bottom), fill);
            canvas.DrawRect(new SKRect(outer.Left, inner.Top, inner.Left, inner.Bottom), fill);
            canvas.DrawRect(new SKRect(inner.Right, inner.Top, outer.Right, inner.Bottom), fill);
            return;
        }

        var outlineRect = SKRect.Inflate(box.BorderBox, offset + width / 2f, offset + width / 2f);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true,
        };
        ApplyBorderStyle(paint, style);
        canvas.DrawRect(outlineRect, paint);
    }
}
