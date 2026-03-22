using System.Globalization;
using Lite.Models;
using SkiaSharp;

namespace Lite.Rendering;

/// <summary>Renders SVG elements using SkiaSharp.</summary>
internal static class SvgRenderer
{
    internal static void Render(SKCanvas canvas, LayoutNode svgNode)
    {
        var box = svgNode.Box;
        canvas.Save();
        canvas.Translate(box.ContentBox.Left, box.ContentBox.Top);

        // Parse viewBox if present
        if (svgNode.Attributes.TryGetValue("viewBox", out var viewBox))
        {
            var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 &&
                TryParseF(parts[0], out var vx) && TryParseF(parts[1], out var vy) &&
                TryParseF(parts[2], out var vw) && TryParseF(parts[3], out var vh) &&
                vw > 0 && vh > 0)
            {
                var scaleX = box.ContentBox.Width / vw;
                var scaleY = box.ContentBox.Height / vh;
                canvas.Scale(scaleX, scaleY);
                canvas.Translate(-vx, -vy);
            }
        }

        foreach (var child in svgNode.Children)
            RenderElement(canvas, child);

        canvas.Restore();
    }

    private static void RenderElement(SKCanvas canvas, LayoutNode node)
    {
        // Handle transform
        canvas.Save();
        if (node.Attributes.TryGetValue("transform", out var transform))
            ApplyTransform(canvas, transform);

        switch (node.TagName)
        {
            case "RECT": RenderRect(canvas, node); break;
            case "CIRCLE": RenderCircle(canvas, node); break;
            case "ELLIPSE": RenderEllipse(canvas, node); break;
            case "LINE": RenderLine(canvas, node); break;
            case "POLYLINE": RenderPolyline(canvas, node, false); break;
            case "POLYGON": RenderPolyline(canvas, node, true); break;
            case "PATH": RenderPath(canvas, node); break;
            case "TEXT": RenderText(canvas, node); break;
            case "G":
                foreach (var child in node.Children)
                    RenderElement(canvas, child);
                break;
            case "SVG": // nested SVG
                foreach (var child in node.Children)
                    RenderElement(canvas, child);
                break;
        }

        canvas.Restore();
    }

    private static void RenderRect(SKCanvas canvas, LayoutNode node)
    {
        var x = GetAttrF(node, "x");
        var y = GetAttrF(node, "y");
        var w = GetAttrF(node, "width");
        var h = GetAttrF(node, "height");
        if (w <= 0 || h <= 0) return;
        var rx = GetAttrF(node, "rx");
        var ry = GetAttrF(node, "ry");
        if (ry == 0) ry = rx;
        if (rx == 0) rx = ry;

        var rect = SKRect.Create(x, y, w, h);
        FillAndStroke(canvas, node, p =>
        {
            if (rx > 0 || ry > 0)
                canvas.DrawRoundRect(rect, rx, ry, p);
            else
                canvas.DrawRect(rect, p);
        });
    }

    private static void RenderCircle(SKCanvas canvas, LayoutNode node)
    {
        var cx = GetAttrF(node, "cx");
        var cy = GetAttrF(node, "cy");
        var r = GetAttrF(node, "r");
        if (r <= 0) return;
        FillAndStroke(canvas, node, p => canvas.DrawCircle(cx, cy, r, p));
    }

    private static void RenderEllipse(SKCanvas canvas, LayoutNode node)
    {
        var cx = GetAttrF(node, "cx");
        var cy = GetAttrF(node, "cy");
        var rx = GetAttrF(node, "rx");
        var ry = GetAttrF(node, "ry");
        if (rx <= 0 || ry <= 0) return;
        FillAndStroke(canvas, node, p => canvas.DrawOval(cx, cy, rx, ry, p));
    }

    private static void RenderLine(SKCanvas canvas, LayoutNode node)
    {
        var x1 = GetAttrF(node, "x1");
        var y1 = GetAttrF(node, "y1");
        var x2 = GetAttrF(node, "x2");
        var y2 = GetAttrF(node, "y2");
        using var paint = CreateStrokePaint(node);
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void RenderPolyline(SKCanvas canvas, LayoutNode node, bool close)
    {
        if (!node.Attributes.TryGetValue("points", out var pointsStr)) return;
        var points = ParsePoints(pointsStr);
        if (points.Length < 2) return;

        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++)
            path.LineTo(points[i]);
        if (close) path.Close();

        FillAndStroke(canvas, node, p => canvas.DrawPath(path, p));
    }

    private static void RenderPath(SKCanvas canvas, LayoutNode node)
    {
        if (!node.Attributes.TryGetValue("d", out var d)) return;
        var path = SKPath.ParseSvgPathData(d);
        if (path == null) return;

        FillAndStroke(canvas, node, p => canvas.DrawPath(path, p));
        path.Dispose();
    }

    private static void RenderText(SKCanvas canvas, LayoutNode node)
    {
        var x = GetAttrF(node, "x");
        var y = GetAttrF(node, "y");
        var text = node.DisplayText;
        if (string.IsNullOrEmpty(text)) text = string.Concat(node.Children.Select(c => c.DisplayText));

        var fontSize = 16f;
        if (node.Attributes.TryGetValue("font-size", out var fs) && TryParseF(fs.Replace("px", ""), out var parsedFs))
            fontSize = parsedFs;
        fontSize = Math.Max(1f, fontSize);

        using var font = new SKFont(SKTypeface.Default, fontSize);
        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = ParseColor(GetAttr(node, "fill", "black"));
        canvas.DrawText(text, x, y, font, paint);
    }

    // ---- Helpers ----

    private static void FillAndStroke(SKCanvas canvas, LayoutNode node, Action<SKPaint> draw)
    {
        var fillStr = GetAttr(node, "fill", "black");
        var strokeStr = GetAttr(node, "stroke", null);
        var fillOpacity = GetAttrF(node, "fill-opacity", 1);
        var strokeOpacity = GetAttrF(node, "stroke-opacity", 1);
        var opacity = GetAttrF(node, "opacity", 1);

        if (fillStr != "none")
        {
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ParseColor(fillStr).WithAlpha((byte)(fillOpacity * opacity * 255)),
                IsAntialias = true
            };
            draw(paint);
        }

        if (strokeStr != null && strokeStr != "none")
        {
            using var paint = CreateStrokePaint(node);
            paint.Color = ParseColor(strokeStr).WithAlpha((byte)(strokeOpacity * opacity * 255));
            draw(paint);
        }
    }

    private static SKPaint CreateStrokePaint(LayoutNode node)
    {
        var strokeWidth = GetAttrF(node, "stroke-width", 1);
        var strokeColor = GetAttr(node, "stroke", "black");
        return new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            Color = ParseColor(strokeColor),
            IsAntialias = true,
            StrokeCap = GetAttr(node, "stroke-linecap", "butt") switch
            {
                "round" => SKStrokeCap.Round,
                "square" => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt
            },
            StrokeJoin = GetAttr(node, "stroke-linejoin", "miter") switch
            {
                "round" => SKStrokeJoin.Round,
                "bevel" => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Miter
            }
        };
    }

    private static void ApplyTransform(SKCanvas canvas, string transform)
    {
        var idx = 0;
        while (idx < transform.Length)
        {
            // Find function name
            var fnStart = idx;
            while (idx < transform.Length && transform[idx] != '(') idx++;
            if (idx >= transform.Length) break;
            var fn = transform[fnStart..idx].Trim();
            idx++; // skip '('
            var argStart = idx;
            while (idx < transform.Length && transform[idx] != ')') idx++;
            var args = transform[argStart..idx].Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            idx++; // skip ')'

            switch (fn)
            {
                case "translate":
                    if (args.Length >= 1 && TryParseF(args[0], out var tx))
                    {
                        TryParseF(args.Length > 1 ? args[1] : "0", out var ty);
                        canvas.Translate(tx, ty);
                    }
                    break;
                case "scale":
                    if (args.Length >= 1 && TryParseF(args[0], out var sx))
                    {
                        TryParseF(args.Length > 1 ? args[1] : args[0], out var sy);
                        canvas.Scale(sx, sy);
                    }
                    break;
                case "rotate":
                    if (args.Length >= 1 && TryParseF(args[0], out var angle))
                    {
                        if (args.Length >= 3 && TryParseF(args[1], out var rcx) && TryParseF(args[2], out var rcy))
                        {
                            canvas.Translate(rcx, rcy);
                            canvas.RotateDegrees(angle);
                            canvas.Translate(-rcx, -rcy);
                        }
                        else
                        {
                            canvas.RotateDegrees(angle);
                        }
                    }
                    break;
                case "skewX":
                    if (TryParseF(args[0], out var skx))
                        canvas.Skew((float)Math.Tan(skx * Math.PI / 180), 0);
                    break;
                case "skewY":
                    if (TryParseF(args[0], out var sky))
                        canvas.Skew(0, (float)Math.Tan(sky * Math.PI / 180));
                    break;
                case "matrix":
                    if (args.Length == 6 &&
                        TryParseF(args[0], out var a) && TryParseF(args[1], out var b) &&
                        TryParseF(args[2], out var c) && TryParseF(args[3], out var dd) &&
                        TryParseF(args[4], out var e) && TryParseF(args[5], out var f))
                    {
                        var matrix = new SKMatrix(a, c, e, b, dd, f, 0, 0, 1);
                        canvas.Concat(in matrix);
                    }
                    break;
            }
        }
    }

    internal static SKColor ParseColor(string? color)
    {
        if (string.IsNullOrEmpty(color) || color == "none") return SKColors.Transparent;

        // Named colors
        if (SKColor.TryParse(color, out var parsed)) return parsed;

        // rgb(r, g, b) / rgba(r, g, b, a)
        if (color.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var inner = color[(color.IndexOf('(') + 1)..color.IndexOf(')')];
            var parts = inner.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                var r = ParseColorComponent(parts[0]);
                var g = ParseColorComponent(parts[1]);
                var bl = ParseColorComponent(parts[2]);
                var a = parts.Length >= 4 && float.TryParse(parts[3], CultureInfo.InvariantCulture, out var av) ? (byte)(av * 255) : (byte)255;
                return new SKColor(r, g, bl, a);
            }
        }

        // hsl(h, s%, l%) / hsla(h, s%, l%, a)
        if (color.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            return ParseHsl(color);
        }

        return SKColors.Black;
    }

    internal static SKColor ParseHsl(string hsl)
    {
        var inner = hsl[(hsl.IndexOf('(') + 1)..hsl.IndexOf(')')];
        var parts = inner.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return SKColors.Black;

        if (!TryParseF(parts[0].Replace("deg", ""), out var h)) return SKColors.Black;
        if (!TryParseF(parts[1].Replace("%", ""), out var s)) return SKColors.Black;
        if (!TryParseF(parts[2].Replace("%", ""), out var l)) return SKColors.Black;
        float a = 1f;
        if (parts.Length >= 4) TryParseF(parts[3].Replace("%", ""), out a);
        if (a > 1) a /= 100f; // percent alpha

        h = ((h % 360) + 360) % 360;
        s /= 100f; l /= 100f;

        var c = (1f - Math.Abs(2f * l - 1f)) * s;
        var x = c * (1f - Math.Abs(h / 60f % 2 - 1f));
        var m = l - c / 2f;

        float r1, g1, b1;
        if (h < 60)       { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else              { r1 = c; g1 = 0; b1 = x; }

        return new SKColor(
            (byte)((r1 + m) * 255),
            (byte)((g1 + m) * 255),
            (byte)((b1 + m) * 255),
            (byte)(a * 255));
    }

    private static byte ParseColorComponent(string s)
    {
        s = s.Trim();
        if (s.EndsWith('%') && float.TryParse(s[..^1], CultureInfo.InvariantCulture, out var pct))
            return (byte)(pct / 100f * 255);
        return byte.TryParse(s, out var v) ? v : (byte)0;
    }

    private static SKPoint[] ParsePoints(string pointsStr)
    {
        var nums = pointsStr.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var points = new List<SKPoint>();
        for (int i = 0; i + 1 < nums.Length; i += 2)
        {
            if (TryParseF(nums[i], out var x) && TryParseF(nums[i + 1], out var y))
                points.Add(new SKPoint(x, y));
        }
        return points.ToArray();
    }

    private static string GetAttr(LayoutNode node, string name, string? defaultValue)
    {
        return node.Attributes.TryGetValue(name, out var v) ? v : defaultValue ?? "";
    }

    private static float GetAttrF(LayoutNode node, string name, float defaultValue = 0)
    {
        if (!node.Attributes.TryGetValue(name, out var v)) return defaultValue;
        v = v.Replace("px", "").Trim();
        return TryParseF(v, out var f) ? f : defaultValue;
    }

    private static bool TryParseF(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
