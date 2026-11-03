using SkiaSharp;

namespace Lite.Rendering;

/// <summary>Thread-static collector of the text the painter actually draws. The stated-
/// condition judges read it as glyph-order evidence a bitmap cannot provide: the visual
/// order of a bidi line, a list marker's text, the typeface and size a run uses. Collection
/// is enabled only around the harness's renders; the live window never collects.</summary>
public static class PaintLog
{
    public sealed record Entry(string Text, float X, float BaselineY, string Typeface, float Size, string Align, bool Bold, bool Italic);

    [ThreadStatic] private static List<Entry>? _entries;

    /// <summary>Starts collecting on this thread; <see cref="End"/> returns the entries.</summary>
    public static void Begin() => _entries = new List<Entry>();

    public static List<Entry> End()
    {
        var list = _entries ?? new List<Entry>();
        _entries = null;
        return list;
    }

    internal static void Record(string text, float x, float baselineY, SKFont font, SKTextAlign align)
    {
        if (_entries is not { } list) return;
        var typeface = font.Typeface;
        list.Add(new Entry(text, x, baselineY, typeface.FamilyName ?? "", font.Size, align.ToString(),
            typeface.FontWeight >= 600, typeface.FontStyle.Slant != SkiaSharp.SKFontStyleSlant.Upright));
    }
}
