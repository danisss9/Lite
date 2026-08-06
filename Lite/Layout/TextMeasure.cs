using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal record TextLine(string Text, float Width, float Height, float Ascent);

internal static class TextMeasure
{
    public static SKFont CreateFont(LayoutNode node)
    {
        var size = Math.Max(1f, node.GetFontSize());
        var family = node.GetFontFamily();
        var bold = node.GetFontBold();
        var italic = node.GetFontItalic();
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

        // Check @font-face registry before falling back to system fonts
        var customTypeface = FontRegistry.Resolve(family, bold, italic);
        var typeface = customTypeface
                       ?? SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant)
                       ?? SKTypeface.Default;

        // If the node has short text (pseudo-element content), check for missing glyphs
        // and fall back to a typeface with broader Unicode coverage.
        var text = node.DisplayText;
        if (!string.IsNullOrEmpty(text) && text.Length <= 20 && !ContainsAllGlyphs(typeface, text))
        {
            foreach (var fallback in _symbolFallbacks)
            {
                var fb = SKTypeface.FromFamilyName(fallback, weight, SKFontStyleWidth.Normal, slant);
                if (fb != null && ContainsAllGlyphs(fb, text))
                {
                    typeface = fb;
                    break;
                }
                fb?.Dispose();
            }
        }

        return new SKFont(typeface, size);
    }

    private static readonly string[] _symbolFallbacks =
        ["Segoe UI Symbol", "Segoe UI Emoji", "Arial Unicode MS", "Lucida Sans Unicode"];

    private static bool ContainsAllGlyphs(SKTypeface typeface, string text)
    {
        foreach (var c in text)
        {
            if (c == ' ') continue;
            if (typeface.GetGlyphs(c.ToString()) is ushort[] glyphs
                && glyphs.Length > 0 && glyphs[0] == 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Wraps text into lines that fit within maxWidth, respecting the node's white-space mode.
    /// Each TextLine carries the text, measured width, line height, and baseline ascent.
    /// </summary>
    public static List<TextLine> WrapText(string text, float maxWidth, SKFont font, WhiteSpace whiteSpace = WhiteSpace.Normal, float lineHeight = -1f,
        IReadOnlyList<float>? lineWidths = null)
    {
        // Negative means "caller did not say"; a line-height of exactly 0 is a real value
        // ('line-height: 0' collapses the line box) and must not be replaced.
        if (lineHeight < 0f) lineHeight = font.Size * 1.4f;
        return whiteSpace switch
        {
            WhiteSpace.Pre => SplitPreserved(text, font, wrap: false, lineHeight: lineHeight),
            WhiteSpace.PreWrap => SplitPreserved(text, font, wrap: true, maxWidth, lineHeight),
            WhiteSpace.PreLine => SplitPreLine(text, maxWidth, font, lineHeight),
            WhiteSpace.NoWrap => WrapCollapsed(text, float.MaxValue, font, lineHeight),
            _ => WrapCollapsed(text, maxWidth, font, lineHeight, lineWidths),
        };
    }

    /// <summary>The width available to line <paramref name="index"/>: <paramref name="widths"/>
    /// when the caller supplied a per-line band (text beside a float, CSS 2.1 §9.5), otherwise the
    /// single <paramref name="fallback"/>. Past the end of the list the last band repeats — below
    /// the lowest float the band no longer changes.</summary>
    internal static float LineWidthAt(IReadOnlyList<float>? widths, int index, float fallback)
        => widths is null or { Count: 0 } ? fallback : widths[Math.Min(index, widths.Count - 1)];

    /// <summary>
    /// Measures text with 'letter-spacing' and 'word-spacing' applied — the width the painter
    /// will actually draw. Spacing goes after each character (and after each space for
    /// word-spacing); the trailing letter-space is dropped, as it is when painting.
    /// </summary>
    public static float MeasureWithSpacing(string text, SKFont font, float letterSpacing, float wordSpacing)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        if (letterSpacing == 0f && wordSpacing == 0f) return font.MeasureText(text);

        float w = 0f;
        foreach (var ch in text)
        {
            w += font.MeasureText(ch.ToString()) + letterSpacing;
            if (ch == ' ') w += wordSpacing;
        }
        return Math.Max(0f, w - letterSpacing);
    }

    /// <summary>The spacing a node's text is laid out with, so measurement and painting agree.</summary>
    public static (float Letter, float Word) SpacingOf(LayoutNode node)
    {
        var fs = node.GetFontSize();
        return (node.GetLetterSpacing(fs), node.GetWordSpacing(fs));
    }

    /// <summary>
    /// Measures text without wrapping — returns total width and single-line height.
    /// </summary>
    public static (float Width, float Height, float Ascent) MeasureSingleLine(string text, SKFont font, float lineHeight = -1f,
        float letterSpacing = 0f, float wordSpacing = 0f)
    {
        // Negative means "caller did not say"; a line-height of exactly 0 is a real value
        // ('line-height: 0' collapses the line box) and must not be replaced.
        if (lineHeight < 0f) lineHeight = font.Size * 1.4f;
        return (MeasureWithSpacing(text, font, letterSpacing, wordSpacing), lineHeight, ComputeAscent(font, lineHeight));
    }

    /// <summary>
    /// CSS 2.1 §10.8.1 half-leading: within a text box of the given line-height, the font's own
    /// ascent+descent is centred, splitting the extra "leading" evenly above and below. Returns
    /// the distance from the top of that box down to the baseline — i.e. the value inline layout
    /// needs to align this box against a shared line baseline.
    /// </summary>
    public static float ComputeAscent(SKFont font, float lineHeight)
    {
        var ascent = -font.Metrics.Ascent;
        var descent = font.Metrics.Descent;
        var halfLeading = (lineHeight - (ascent + descent)) / 2f;
        return ascent + halfLeading;
    }

    /// <summary>
    /// Truncates text to fit within maxWidth, appending "…" if truncated.
    /// Returns the truncated string and its measured width.
    /// </summary>
    public static (string Text, float Width) TruncateWithEllipsis(string text, float maxWidth, SKFont font)
    {
        var fullWidth = font.MeasureText(text);
        if (fullWidth <= maxWidth) return (text, fullWidth);

        var ellipsis = "\u2026"; // …
        var ellipsisW = font.MeasureText(ellipsis);
        var available = maxWidth - ellipsisW;
        if (available <= 0) return (ellipsis, ellipsisW);

        // Binary search for the longest prefix that fits
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (font.MeasureText(text[..mid]) <= available) lo = mid;
            else hi = mid - 1;
        }
        var truncated = text[..lo] + ellipsis;
        return (truncated, font.MeasureText(truncated));
    }

    // -------------------------------------------------------------------------
    // white-space modes
    // -------------------------------------------------------------------------

    /// <summary>Collapse whitespace and word-wrap at maxWidth (normal / nowrap).</summary>
    private static List<TextLine> WrapCollapsed(string text, float maxWidth, SKFont font, float lineHeight,
        IReadOnlyList<float>? lineWidths = null)
    {
        var lines = new List<TextLine>();
        var ascent = ComputeAscent(font, lineHeight);

        // Fast path: text already fits — return it verbatim so that leading/trailing
        // spaces (significant in inline runs like " and ") are preserved.
        if (font.MeasureText(text) <= LineWidthAt(lineWidths, 0, maxWidth))
        {
            lines.Add(new TextLine(text, font.MeasureText(text), lineHeight, ascent));
            return lines;
        }

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        // Each line is measured against ITS OWN width: beside a float the available width changes
        // from line to line, so a single maxWidth would wrap the whole paragraph to the narrow
        // band even below the float's bottom edge.
        var limit = LineWidthAt(lineWidths, 0, maxWidth);

        foreach (var word in words)
        {
            var candidate = sb.Length == 0 ? word : sb + " " + word;
            if (font.MeasureText(candidate) > limit && sb.Length > 0)
            {
                var lineText = sb.ToString();
                lines.Add(new TextLine(lineText, font.MeasureText(lineText), lineHeight, ascent));
                limit = LineWidthAt(lineWidths, lines.Count, maxWidth);
                sb.Clear();
                sb.Append(word);
            }
            else
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(word);
            }
        }

        if (sb.Length > 0)
        {
            var lineText = sb.ToString();
            lines.Add(new TextLine(lineText, font.MeasureText(lineText), lineHeight, ascent));
        }

        return lines;
    }

    /// <summary>Preserve whitespace and newlines; optionally wrap long lines (pre / pre-wrap).</summary>
    private static List<TextLine> SplitPreserved(string text, SKFont font, bool wrap, float maxWidth = float.MaxValue, float lineHeight = -1f)
    {
        var lines = new List<TextLine>();
        // Negative means "caller did not say"; a line-height of exactly 0 is a real value
        // ('line-height: 0' collapses the line box) and must not be replaced.
        if (lineHeight < 0f) lineHeight = font.Size * 1.4f;
        var ascent = ComputeAscent(font, lineHeight);

        var rawLines = text.Split('\n');
        foreach (var raw in rawLines)
        {
            if (!wrap || font.MeasureText(raw) <= maxWidth)
            {
                lines.Add(new TextLine(raw, font.MeasureText(raw), lineHeight, ascent));
            }
            else
            {
                // Break at character level when a single line overflows
                var sb = new System.Text.StringBuilder();
                foreach (var ch in raw)
                {
                    var candidate = sb.ToString() + ch;
                    if (font.MeasureText(candidate) > maxWidth && sb.Length > 0)
                    {
                        var seg = sb.ToString();
                        lines.Add(new TextLine(seg, font.MeasureText(seg), lineHeight, ascent));
                        sb.Clear();
                    }
                    sb.Append(ch);
                }
                if (sb.Length > 0)
                {
                    var seg = sb.ToString();
                    lines.Add(new TextLine(seg, font.MeasureText(seg), lineHeight, ascent));
                }
            }
        }

        return lines;
    }

    /// <summary>Collapse spaces but preserve newlines; word-wrap at maxWidth (pre-line).</summary>
    private static List<TextLine> SplitPreLine(string text, float maxWidth, SKFont font, float lineHeight)
    {
        var lines = new List<TextLine>();
        foreach (var rawLine in text.Split('\n'))
            lines.AddRange(WrapCollapsed(rawLine, maxWidth, font, lineHeight));
        return lines;
    }
}
