using System.Globalization;
using Lite.Models;
using Lite.Rendering;
using SkiaSharp;

namespace Lite.Scripting.Dom;

/// <summary>CanvasRenderingContext2D exposed to JavaScript.</summary>
public class JsCanvasContext2D
{
    private readonly LayoutNode _canvasNode;
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly Stack<SKMatrix> _savedStates = new();
    private SKPath _currentPath = new();

    // State
    public string fillStyle { get; set; } = "#000000";
    public string strokeStyle { get; set; } = "#000000";
    public double lineWidth { get; set; } = 1;
    public string font { get; set; } = "10px sans-serif";
    public string textAlign { get; set; } = "start";
    public string textBaseline { get; set; } = "alphabetic";
    public double globalAlpha { get; set; } = 1.0;
    public string lineCap { get; set; } = "butt";
    public string lineJoin { get; set; } = "miter";
    public double miterLimit { get; set; } = 10;
    public double shadowBlur { get; set; } = 0;
    public string shadowColor { get; set; } = "transparent";
    public double shadowOffsetX { get; set; } = 0;
    public double shadowOffsetY { get; set; } = 0;

    public JsCanvasContext2D(LayoutNode canvasNode, int width, int height)
    {
        _canvasNode = canvasNode;
        _bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _canvas = new SKCanvas(_bitmap);
        _canvas.Clear(SKColors.Transparent);
        _canvasNode.Image = _bitmap;
    }

    // ---- rect operations ----
    public void fillRect(double x, double y, double w, double h)
    {
        using var paint = MakeFillPaint();
        _canvas.DrawRect((float)x, (float)y, (float)w, (float)h, paint);
    }

    public void strokeRect(double x, double y, double w, double h)
    {
        using var paint = MakeStrokePaint();
        _canvas.DrawRect((float)x, (float)y, (float)w, (float)h, paint);
    }

    public void clearRect(double x, double y, double w, double h)
    {
        using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
        _canvas.DrawRect((float)x, (float)y, (float)w, (float)h, paint);
    }

    // ---- text ----
    public void fillText(string text, double x, double y, double? maxWidth = null)
    {
        using var paint = MakeFillPaint();
        using var skFont = ParseFont(font);
        _canvas.DrawText(text, (float)x, (float)y, skFont, paint);
    }

    public void strokeText(string text, double x, double y, double? maxWidth = null)
    {
        using var paint = MakeStrokePaint();
        using var skFont = ParseFont(font);
        _canvas.DrawText(text, (float)x, (float)y, skFont, paint);
    }

    public JsTextMetrics measureText(string text)
    {
        using var skFont = ParseFont(font);
        return new JsTextMetrics { width = skFont.MeasureText(text) };
    }

    // ---- path operations ----
    public void beginPath() => _currentPath = new SKPath();
    public void closePath() => _currentPath.Close();
    public void moveTo(double x, double y) => _currentPath.MoveTo((float)x, (float)y);
    public void lineTo(double x, double y) => _currentPath.LineTo((float)x, (float)y);

    public void quadraticCurveTo(double cpx, double cpy, double x, double y)
        => _currentPath.QuadTo((float)cpx, (float)cpy, (float)x, (float)y);

    public void bezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y)
        => _currentPath.CubicTo((float)cp1x, (float)cp1y, (float)cp2x, (float)cp2y, (float)x, (float)y);

    public void arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise = false)
    {
        var sweepAngle = (float)((endAngle - startAngle) * 180 / Math.PI);
        if (counterclockwise && sweepAngle > 0) sweepAngle -= 360;
        if (!counterclockwise && sweepAngle < 0) sweepAngle += 360;
        var startDeg = (float)(startAngle * 180 / Math.PI);
        var oval = SKRect.Create((float)(x - radius), (float)(y - radius), (float)(radius * 2), (float)(radius * 2));
        _currentPath.ArcTo(oval, startDeg, sweepAngle, false);
    }

    public void arcTo(double x1, double y1, double x2, double y2, double radius)
        => _currentPath.ArcTo(new SKPoint((float)x1, (float)y1), new SKPoint((float)x2, (float)y2), (float)radius);

    public void rect(double x, double y, double w, double h)
        => _currentPath.AddRect(SKRect.Create((float)x, (float)y, (float)w, (float)h));

    public void ellipse(double x, double y, double rx, double ry, double rotation, double startAngle, double endAngle, bool counterclockwise = false)
    {
        _canvas.Save();
        _canvas.Translate((float)x, (float)y);
        _canvas.RotateDegrees((float)(rotation * 180 / Math.PI));
        var oval = SKRect.Create(-(float)rx, -(float)ry, (float)(rx * 2), (float)(ry * 2));
        var sweepAngle = (float)((endAngle - startAngle) * 180 / Math.PI);
        if (counterclockwise && sweepAngle > 0) sweepAngle -= 360;
        if (!counterclockwise && sweepAngle < 0) sweepAngle += 360;
        _currentPath.ArcTo(oval, (float)(startAngle * 180 / Math.PI), sweepAngle, false);
        _canvas.Restore();
    }

    public void fill() { using var p = MakeFillPaint(); _canvas.DrawPath(_currentPath, p); }
    public void stroke() { using var p = MakeStrokePaint(); _canvas.DrawPath(_currentPath, p); }
    public void clip() => _canvas.ClipPath(_currentPath, antialias: true);

    // ---- transforms ----
    public void save() { _savedStates.Push(_canvas.TotalMatrix); _canvas.Save(); }
    public void restore() { _canvas.Restore(); if (_savedStates.Count > 0) _savedStates.Pop(); }
    public void translate(double x, double y) => _canvas.Translate((float)x, (float)y);
    public void rotate(double angle) => _canvas.RotateDegrees((float)(angle * 180 / Math.PI));
    public void scale(double x, double y) => _canvas.Scale((float)x, (float)y);

    public void setTransform(double a, double b, double c, double d, double e, double f)
    {
        _canvas.ResetMatrix();
        var matrix = new SKMatrix((float)a, (float)c, (float)e, (float)b, (float)d, (float)f, 0, 0, 1);
        _canvas.Concat(in matrix);
    }

    public void resetTransform() => _canvas.ResetMatrix();

    // ---- image ----
    public void drawImage(JsElement image, double dx, double dy)
    {
        if (image.Node.Image is { } bmp)
            _canvas.DrawBitmap(bmp, (float)dx, (float)dy);
    }

    // ---- helpers ----
    private SKPaint MakeFillPaint() => new()
    {
        Style = SKPaintStyle.Fill,
        Color = SvgRenderer.ParseColor(fillStyle).WithAlpha((byte)(globalAlpha * 255)),
        IsAntialias = true
    };

    private SKPaint MakeStrokePaint() => new()
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = (float)lineWidth,
        Color = SvgRenderer.ParseColor(strokeStyle).WithAlpha((byte)(globalAlpha * 255)),
        IsAntialias = true,
        StrokeCap = lineCap switch
        {
            "round" => SKStrokeCap.Round,
            "square" => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt
        },
        StrokeJoin = lineJoin switch
        {
            "round" => SKStrokeJoin.Round,
            "bevel" => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        },
        StrokeMiter = (float)miterLimit
    };

    private static SKFont ParseFont(string fontStr)
    {
        // Simple parsing: "16px Arial" or "bold 14px sans-serif"
        var parts = fontStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float size = 10;
        foreach (var part in parts)
        {
            var trimmed = part.Replace("px", "").Replace("pt", "");
            if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            { size = s; break; }
        }
        return new SKFont(SKTypeface.Default, size);
    }
}

public class JsTextMetrics
{
    public double width { get; set; }
}

/// <summary>Wraps the canvas element for JS access to getContext().</summary>
public class JsCanvas
{
    private readonly Jint.Engine _engine;
    private readonly LayoutNode _node;
    private JsCanvasContext2D? _ctx;

    public JsCanvas(Jint.Engine engine, LayoutNode node)
    {
        _engine = engine;
        _node = node;
    }

    public object? getContext(string contextType)
    {
        if (contextType == "2d")
        {
            if (_ctx == null)
            {
                var w = int.TryParse(_node.Attributes.GetValueOrDefault("width", "300"), out var wv) ? wv : 300;
                var h = int.TryParse(_node.Attributes.GetValueOrDefault("height", "150"), out var hv) ? hv : 150;
                _ctx = new JsCanvasContext2D(_node, w, h);
            }
            return _ctx;
        }
        return null;
    }
}
