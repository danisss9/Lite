using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;

namespace Lite.Media;

/// <summary>
/// A fetched SVG document used as an image (a CSS background image). SKCodec has no SVG codec,
/// so a vector image is kept as its parsed document and rasterized at the concrete size the
/// painter resolves.
///
/// Sizing follows CSS 2.1 §14.2.1 together with CSS Images 3 §5.3 (the "default sizing
/// algorithm", which css3-background's <c>background-size: auto</c> uses): the image's
/// intrinsic width, height and ratio come from the root <c>&lt;svg&gt;</c>'s <c>width</c>,
/// <c>height</c> and <c>viewBox</c>; a missing dimension comes from the ratio, else from the
/// default object size (the background positioning area). A percentage width or height is
/// resolved against that default object size, as browsers do (background-intrinsic-006).
/// </summary>
internal sealed class SvgImage
{
    private readonly XElement _root;
    private readonly string? _width, _height;
    private readonly SKRect? _viewBox;
    private readonly Dictionary<(int, int), SKBitmap> _rasters = new();

    private SvgImage(XElement root)
    {
        _root = root;
        _width = (string?)root.Attribute("width");
        _height = (string?)root.Attribute("height");
        _viewBox = ParseViewBox((string?)root.Attribute("viewBox"));
    }

    /// <summary>The image's intrinsic ratio (width / height) from its viewBox, else from two
    /// absolute intrinsic dimensions; null when it has none.</summary>
    internal float? Ratio
    {
        get
        {
            if (_viewBox is { } vb) return vb.Width / vb.Height;
            if (Absolute(_width) is { } w && Absolute(_height) is { } h && w > 0 && h > 0) return w / h;
            return null;
        }
    }

    /// <summary>Bytes that are an SVG document (an XML document whose root is <c>svg</c>), or null.</summary>
    internal static SvgImage? TryParse(byte[] bytes)
    {
        if (!LooksLikeSvg(bytes)) return null;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var root = XDocument.Load(reader).Root;
            return root is not null && root.Name.LocalName == "svg" ? new SvgImage(root) : null;
        }
        catch (XmlException) { return null; }
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        return head.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('<') &&
               head.Contains("<svg", StringComparison.Ordinal);
    }

    /// <summary>The concrete object size (CSS Images 3 §5.3) for a specified size of
    /// <paramref name="specifiedW"/>/<paramref name="specifiedH"/> (null = auto) and a default
    /// object size of <paramref name="defaultW"/> × <paramref name="defaultH"/>.</summary>
    internal (float Width, float Height) ConcreteSize(float? specifiedW, float? specifiedH, float defaultW, float defaultH)
    {
        if (specifiedW is { } sw && specifiedH is { } sh) return (sw, sh);
        var ratio = Ratio;
        if (specifiedW is { } w0) return (w0, ratio is { } r0 ? w0 / r0 : Length(_height, defaultH) ?? defaultH);
        if (specifiedH is { } h0) return (ratio is { } r1 ? h0 * r1 : Length(_width, defaultW) ?? defaultW, h0);

        var iw = Length(_width, defaultW);
        var ih = Length(_height, defaultH);
        if (iw is { } w && ih is { } h) return (w, h);
        if (iw is { } w1) return (w1, ratio is { } r2 ? w1 / r2 : defaultH);
        if (ih is { } h1) return (ratio is { } r3 ? h1 * r3 : defaultW, h1);
        if (ratio is { } r)
        {
            // Only a ratio: the largest rectangle at that ratio that fits the default size ("contain").
            return defaultW / defaultH > r ? (defaultH * r, defaultH) : (defaultW, defaultW / r);
        }
        return (defaultW, defaultH);
    }

    /// <summary>The image rasterized at <paramref name="width"/> × <paramref name="height"/>
    /// device pixels (rounded, at least 1). Rasters are cached per size.</summary>
    internal SKBitmap Rasterize(float width, float height)
    {
        int w = Math.Max(1, (int)MathF.Round(width)), h = Math.Max(1, (int)MathF.Round(height));
        lock (_rasters)
        {
            if (_rasters.TryGetValue((w, h), out var cached)) return cached;
            var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                var viewport = new SKSize(w, h);
                if (_viewBox is { } vb)
                {
                    // preserveAspectRatio's initial value, xMidYMid meet (SVG 1.1 §7.8).
                    var scale = Math.Min(w / vb.Width, h / vb.Height);
                    canvas.Translate((w - vb.Width * scale) / 2, (h - vb.Height * scale) / 2);
                    canvas.Scale(scale);
                    canvas.Translate(-vb.Left, -vb.Top);
                    viewport = vb.Size;
                }
                var style = new Paint(SKColors.Black, null, 1f, 1f);
                foreach (var child in _root.Elements()) Draw(canvas, child, viewport, style);
            }
            _rasters[(w, h)] = bitmap;
            return bitmap;
        }
    }

    // ---- painting ----

    private readonly record struct Paint(SKColor Fill, SKColor? Stroke, float StrokeWidth, float Opacity);

    private static void Draw(SKCanvas canvas, XElement element, SKSize viewport, Paint inherited)
    {
        if (Attr(element, "display") == "none") return;
        var paint = Inherit(element, inherited, viewport);
        canvas.Save();
        if (Attr(element, "transform") is { } transform) ApplyTransform(canvas, transform);
        float X(string name) => Length(Attr(element, name), viewport.Width) ?? 0;
        float Y(string name) => Length(Attr(element, name), viewport.Height) ?? 0;
        switch (element.Name.LocalName)
        {
            case "g":
            case "svg":
            case "a":
                foreach (var child in element.Elements()) Draw(canvas, child, viewport, paint);
                break;
            case "rect":
                {
                    float w = X("width"), h = Y("height");
                    if (w > 0 && h > 0) Shape(canvas, paint, p => p.AddRect(SKRect.Create(X("x"), Y("y"), w, h)));
                    break;
                }
            case "circle":
                {
                    var diagonal = MathF.Sqrt(viewport.Width * viewport.Width + viewport.Height * viewport.Height) / MathF.Sqrt(2);
                    var r = Length(Attr(element, "r"), diagonal) ?? 0;
                    if (r > 0) Shape(canvas, paint, p => p.AddCircle(X("cx"), Y("cy"), r));
                    break;
                }
            case "ellipse":
                {
                    float rx = X("rx"), ry = Y("ry");
                    if (rx > 0 && ry > 0) Shape(canvas, paint, p => p.AddOval(new SKRect(X("cx") - rx, Y("cy") - ry, X("cx") + rx, Y("cy") + ry)));
                    break;
                }
            case "line":
                Shape(canvas, paint with { Fill = SKColors.Transparent }, p =>
                {
                    p.MoveTo(X("x1"), Y("y1"));
                    p.LineTo(X("x2"), Y("y2"));
                });
                break;
            case "polyline":
            case "polygon":
                {
                    var points = Numbers(Attr(element, "points"));
                    if (points.Count < 4) break;
                    var closed = element.Name.LocalName == "polygon";
                    Shape(canvas, paint, p =>
                    {
                        p.MoveTo(points[0], points[1]);
                        for (var i = 2; i + 1 < points.Count; i += 2) p.LineTo(points[i], points[i + 1]);
                        if (closed) p.Close();
                    });
                    break;
                }
            case "path":
                if (Attr(element, "d") is { } d && SKPath.ParseSvgPathData(d) is { } path)
                {
                    using (path) Shape(canvas, paint, p => p.AddPath(path));
                }
                break;
        }
        canvas.Restore();
    }

    private static void Shape(SKCanvas canvas, Paint paint, Action<SKPath> build)
    {
        using var path = new SKPath();
        build(path);
        using var p = new SKPaint { IsAntialias = true };
        if (paint.Fill.Alpha > 0)
        {
            p.Style = SKPaintStyle.Fill;
            p.Color = Faded(paint.Fill, paint.Opacity);
            canvas.DrawPath(path, p);
        }
        if (paint.Stroke is { Alpha: > 0 } stroke && paint.StrokeWidth > 0)
        {
            p.Style = SKPaintStyle.Stroke;
            p.StrokeWidth = paint.StrokeWidth;
            p.Color = Faded(stroke, paint.Opacity);
            canvas.DrawPath(path, p);
        }
    }

    private static SKColor Faded(SKColor color, float opacity) =>
        opacity >= 1 ? color : color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));

    private static Paint Inherit(XElement element, Paint inherited, SKSize viewport)
    {
        var paint = inherited;
        if (Attr(element, "fill") is { } fill) paint = paint with { Fill = Color(fill) };
        if (Attr(element, "stroke") is { } stroke) paint = paint with { Stroke = stroke == "none" ? null : Color(stroke) };
        if (Attr(element, "stroke-width") is { } sw && Length(sw, viewport.Width) is { } width) paint = paint with { StrokeWidth = width };
        if (Attr(element, "fill-opacity") is { } fo && float.TryParse(fo, NumberStyles.Float, CultureInfo.InvariantCulture, out var fov))
            paint = paint with { Fill = Faded(paint.Fill, fov) };
        // Group opacity is approximated per shape (exact for non-overlapping content).
        if (Attr(element, "opacity") is { } op && float.TryParse(op, NumberStyles.Float, CultureInfo.InvariantCulture, out var opv))
            paint = paint with { Opacity = paint.Opacity * opv };
        return paint;
    }

    private static SKColor Color(string value) => Rendering.SvgRenderer.ParseColor(value.Trim());

    /// <summary>A presentation attribute, or the same property in the element's <c>style</c>
    /// attribute (which wins, SVG 1.1 §6.4).</summary>
    private static string? Attr(XElement element, string name)
    {
        if ((string?)element.Attribute("style") is { } style)
        {
            foreach (var declaration in style.Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon > 0 && declaration[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return declaration[(colon + 1)..].Trim();
            }
        }
        return ((string?)element.Attribute(name))?.Trim();
    }

    // ---- values ----

    /// <summary>An SVG length: a number, <c>px</c>, or a percentage of <paramref name="basis"/>.</summary>
    private static float? Length(string? value, float basis)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.EndsWith('%'))
            return TryNumber(value[..^1], out var pct) ? pct / 100f * basis : null;
        return Absolute(value);
    }

    private static float? Absolute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.EndsWith('%')) return null;
        foreach (var (unit, factor) in Units)
            if (value.EndsWith(unit, StringComparison.OrdinalIgnoreCase) &&
                TryNumber(value[..^unit.Length], out var n)) return n * factor;
        return TryNumber(value, out var plain) ? plain : null;
    }

    private static readonly (string Unit, float Factor)[] Units =
        [("px", 1f), ("pt", 4f / 3f), ("pc", 16f), ("in", 96f), ("cm", 96f / 2.54f), ("mm", 96f / 25.4f)];

    private static bool TryNumber(string s, out float value) =>
        float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static List<float> Numbers(string? s)
    {
        var list = new List<float>();
        if (s is null) return list;
        foreach (var part in s.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            if (TryNumber(part, out var n)) list.Add(n);
        return list;
    }

    private static SKRect? ParseViewBox(string? viewBox)
    {
        var n = Numbers(viewBox);
        return n.Count == 4 && n[2] > 0 && n[3] > 0 ? SKRect.Create(n[0], n[1], n[2], n[3]) : null;
    }

    private static void ApplyTransform(SKCanvas canvas, string transform)
    {
        var i = 0;
        while (i < transform.Length)
        {
            var open = transform.IndexOf('(', i);
            if (open < 0) return;
            var close = transform.IndexOf(')', open);
            if (close < 0) return;
            var name = transform[i..open].Trim(' ', ',', '\t', '\r', '\n');
            var a = Numbers(transform[(open + 1)..close]);
            switch (name)
            {
                case "translate" when a.Count >= 1: canvas.Translate(a[0], a.Count > 1 ? a[1] : 0); break;
                case "scale" when a.Count >= 1: canvas.Scale(a[0], a.Count > 1 ? a[1] : a[0]); break;
                case "rotate" when a.Count >= 3: canvas.RotateDegrees(a[0], a[1], a[2]); break;
                case "rotate" when a.Count >= 1: canvas.RotateDegrees(a[0]); break;
                case "skewX" when a.Count >= 1: canvas.Skew(MathF.Tan(a[0] * MathF.PI / 180), 0); break;
                case "skewY" when a.Count >= 1: canvas.Skew(0, MathF.Tan(a[0] * MathF.PI / 180)); break;
                case "matrix" when a.Count == 6:
                    var m = new SKMatrix(a[0], a[2], a[4], a[1], a[3], a[5], 0, 0, 1);
                    canvas.Concat(in m);
                    break;
            }
            i = close + 1;
        }
    }
}
