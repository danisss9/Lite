using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal record TextLine(string Text, float Width, float Height, float Ascent);

internal static class TextMeasure
{
    public static SKFont CreateFont(LayoutNode node)
    {
        // A zero font size is legal (§15.7) and must render and measure nothing (font-size-002's
        // "font-size: 0px" text painted, and took space, at the old 1px floor).
        var size = Math.Max(0f, node.GetFontSize());
        var family = node.GetFontFamily();
        var bold = node.GetFontBold();
        var italic = node.GetFontItalic();
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

        // Check @font-face registry before falling back to system fonts
        var customTypeface = FontRegistry.Resolve(family, bold, italic);
        var typeface = customTypeface
                       ?? SystemTypeface(family, weight, slant)
                       ?? SKTypeface.Default;

        // If the node has short text (pseudo-element content), check for missing glyphs
        // and fall back to a typeface with broader Unicode coverage.
        var text = node.DisplayText;
        if (!string.IsNullOrEmpty(text) && text.Length <= 20 && !ContainsAllGlyphs(typeface, text))
        {
            foreach (var fallback in PlatformFonts.SymbolFallbacks)
            {
                var fb = SystemTypeface(fallback, weight, slant);
                if (fb != null && ContainsAllGlyphs(fb, text))
                {
                    typeface = fb;
                    break;
                }
            }
        }

        return new SKFont(typeface, size);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<SKTypeface, float> s_xHeightRatios = new();

    /// <summary>The x-height of the node's first available font as a fraction of the em —
    /// the 'ex' unit (CSS 2.1 §4.3.2: "equal to the font's x-height"). Ahem's is 0.8, so the
    /// old fixed 0.5em approximation sized every ex length in the suite's Ahem tests wrong
    /// (font-size-083: '6ex' at 20px is 96px, not 60). 0.5 when the font reports none.</summary>
    internal static float XHeightRatio(LayoutNode node)
    {
        var bold = node.GetFontBold();
        var italic = node.GetFontItalic();
        var family = node.GetFontFamily();
        var typeface = FontRegistry.Resolve(family, bold, italic)
                       ?? SystemTypeface(family, bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                           italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
                       ?? SKTypeface.Default;
        return s_xHeightRatios.GetOrAdd(typeface, static t =>
        {
            using var font = new SKFont(t, 100f);
            var xHeight = font.Metrics.XHeight;
            return xHeight > 0f ? xHeight / 100f : 0.5f;
        });
    }

    // Installed-font lookups, shared for the process. Matching is cheap with DirectWrite but
    // goes through a full fontconfig query on Linux, and layout asks once per text node per
    // frame — uncached, a single keystroke could cost a second of re-layout. The typefaces are
    // never disposed: SKFont does not own its typeface.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant), SKTypeface?> s_systemTypefaces = new();

    /// <summary>The installed typeface Skia matches for a family and style (cached).</summary>
    internal static SKTypeface? SystemTypeface(string family, SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        SKFontStyleSlant slant = SKFontStyleSlant.Upright) =>
        s_systemTypefaces.GetOrAdd((family, weight, slant),
            static key => SKTypeface.FromFamilyName(key.Family, key.Weight, SKFontStyleWidth.Normal, key.Slant));

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
