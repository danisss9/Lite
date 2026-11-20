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
        var weight = ComputedFontWeight(node);
        var italic = node.GetFontItalic();
        var families = node.GetFontFamilies();
        var typeface = ResolveTypeface(families[0], weight, italic);

        // CSS 2.1 §15.2 / §15.3: when the primary family lacks glyphs for the text, try the
        // remaining families in the node's 'font-family' list in order (font-family-013,
        // fonts-013, font-family-015) before falling back to system symbol fonts.
        var text = node.DisplayText;
        if (!string.IsNullOrEmpty(text) && (families.Length > 1 || text.Length <= 20) && !ContainsAllGlyphs(typeface, text))
        {
            var matched = false;
            for (var i = 1; i < families.Length; i++)
            {
                var fb = ResolveTypeface(families[i], weight, italic);
                if (ContainsAllGlyphs(fb, text))
                {
                    typeface = fb;
                    matched = true;
                    break;
                }
            }
            if (!matched && text.Length <= 20)
            {
                foreach (var fallback in PlatformFonts.SymbolFallbacks)
                {
                    var fb = SystemTypeface(fallback, (SKFontStyleWeight)weight, Slant(italic));
                    if (fb != null && ContainsAllGlyphs(fb, text))
                    {
                        typeface = fb;
                        break;
                    }
                }
            }
        }

        var font = new SKFont(typeface, size);
        // §15.6 synthesis: a bold weight (600 and up) asked of a family whose best face is
        // lighter than 600 (Ahem and most CSSTest Weights families carry one or two weights)
        // is drawn emboldened, as every browser does. Without it the bold and normal Ahem boxes
        // of font-weight-002 painted identically.
        if (NeedsSyntheticBold(weight, typeface)) font.Embolden = true;
        // Ahem is the CSS test font: every glyph is a box on the em square, and browsers
        // grid-fit those edges to whole pixels. It carries no hinting instructions and the
        // platform rasterizer does not autohint it, so at most sizes its 0.8em ascent lands on
        // a fractional row and antialiases (font-family-applies-to-008's 16px square read
        // rows 50.2-66.2 against the reference's 50-65). Aliased edging fills exactly the
        // pixels whose centres the box covers — what grid-fitting produces.
        if (typeface.FamilyName.Equals("Ahem", StringComparison.OrdinalIgnoreCase))
            font.Edging = SKFontEdging.Alias;
        return font;
    }

    /// <summary>CSS 2.1 §15.6 'normal' and 'bold'.</summary>
    internal const int NormalWeight = 400, BoldWeight = 700;

    /// <summary>Whether a face needs emboldening to render the desired weight: the request is
    /// bold (600 and up) and the matched face is not.</summary>
    internal static bool NeedsSyntheticBold(int desiredWeight, SKTypeface typeface) =>
        desiredWeight >= 600 && typeface.FontWeight < 600;

    /// <summary>Whether a 'font-weight' value is valid: the §15.6 keywords, or a number from 1 to
    /// 1000 (CSS 2.1 lists the hundreds; CSS Fonts 4 admits every number in that range).</summary>
    internal static bool IsValidFontWeight(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        if (v is "normal" or "bold" or "bolder" or "lighter" or "inherit" or "initial" or "unset") return true;
        return int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
               && n is >= 1 and <= 1000;
    }

    /// <summary>The computed 'font-weight' (§15.6) of a declared value against the parent's
    /// computed weight. Null (nothing declared) and 'inherit' take the parent's. 'bolder' and
    /// 'lighter' step relative to it by the CSS Fonts 4 table (CSS 2.1 leaves the step to the
    /// family's faces; the suite's font-weight-bolder/lighter references follow the table):
    /// <list type="bullet">
    /// <item>bolder: below 350 → 400, below 550 → 700, otherwise 900;</item>
    /// <item>lighter: below 550 → 100, below 750 → 400, otherwise 700.</item>
    /// </list></summary>
    internal static int ResolveFontWeight(string? declared, int parentWeight)
    {
        var v = declared?.Trim().ToLowerInvariant();
        switch (v)
        {
            case null or "" or "inherit" or "unset": return parentWeight;
            case "normal" or "initial": return NormalWeight;
            case "bold": return BoldWeight;
            case "bolder": return parentWeight < 350 ? 400 : parentWeight < 550 ? 700 : Math.Max(900, parentWeight);
            case "lighter": return parentWeight < 100 ? parentWeight : parentWeight < 550 ? 100 : parentWeight < 750 ? 400 : 700;
        }
        return int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
               && n is >= 1 and <= 1000 ? n : parentWeight;
    }

    /// <summary>A node's computed 'font-weight'. The Parser resolves it for the elements it
    /// builds; a value set on the node itself (a pseudo-element's or script's declaration)
    /// resolves here against the parent. Boxes that share their element's style (#text,
    /// anonymous boxes) inherit: re-reading a shared specified 'bolder' would step twice.</summary>
    internal static int ComputedFontWeight(LayoutNode node)
    {
        var parent = node.Parent;
        if (node.TryResolveStyle("font-weight", out var own) && IsValidFontWeight(own))
            return ResolveFontWeight(own, parent is null ? NormalWeight : ComputedFontWeight(parent));
        if (node.ComputedFontWeight is { } computed) return computed;
        var raw = node.Style.GetPropertyValueSafe("font-weight")?.Trim().ToLowerInvariant();
        if (parent is null) return ResolveFontWeight(IsValidFontWeight(raw) ? raw : null, NormalWeight);
        if (node.TagName.StartsWith('#') || raw is "bolder" or "lighter" || !IsValidFontWeight(raw))
            return ComputedFontWeight(parent);
        return ResolveFontWeight(raw, ComputedFontWeight(parent));
    }

    /// <summary>The face a family, weight and style resolve to: a registered (@font-face or
    /// provisioned) family first, then the installed family, then Skia's default.</summary>
    internal static SKTypeface ResolveTypeface(string family, int weight, bool italic) =>
        FontRegistry.Resolve(family, weight, italic)
        ?? SystemTypeface(family, (SKFontStyleWeight)weight, Slant(italic))
        ?? SKTypeface.Default;

    private static SKFontStyleSlant Slant(bool italic) => italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<SKTypeface, float> s_xHeightRatios = new();

    /// <summary>The x-height of the node's first available font as a fraction of the em —
    /// the 'ex' unit (CSS 2.1 §4.3.2: "equal to the font's x-height"). Ahem's is 0.8, so the
    /// old fixed 0.5em approximation sized every ex length in the suite's Ahem tests wrong
    /// (font-size-083: '6ex' at 20px is 96px, not 60). 0.5 when the font reports none.</summary>
    internal static float XHeightRatio(LayoutNode node)
    {
        var typeface = ResolveTypeface(node.GetFontFamily(), ComputedFontWeight(node), node.GetFontItalic());
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

    /// <summary>Unicode Default_Ignorable / bidi format characters carry no visible glyph and
    /// must not trigger font fallback (CSS Fonts 3 §5.2; fonts-011).</summary>
    private static bool IsDefaultIgnorable(char c) =>
        c is '\u00AD' or '\u034F' or '\u061C' or '\uFEFF'
          or (>= '\u200B' and <= '\u200F')
          or (>= '\u202A' and <= '\u202E')
          or (>= '\u2060' and <= '\u206F');

    private static bool ContainsAllGlyphs(SKTypeface typeface, string text)
    {
        foreach (var c in text)
        {
            if (c == ' ' || IsDefaultIgnorable(c)) continue;
            if (typeface.GetGlyphs(c.ToString()) is ushort[] glyphs
                && glyphs.Length > 0 && glyphs[0] == 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// <see cref="WrapText"/> with a ::first-line 'text-transform' (§5.12.1): the first line is
    /// broken with the transformed text (uppercase glyphs are wider, so fewer words may fit),
    /// and the rest of the text wraps untransformed from the word after it. Preserved white
    /// space and length-changing transforms (ß → SS) keep the plain wrap with the first line
    /// transformed in place.
    /// </summary>
    internal static List<TextLine> WrapTextWithFirstLine(string text, TextTransform firstLine, string? lang,
        float maxWidth, SKFont font, WhiteSpace whiteSpace, float lineHeight, IReadOnlyList<float>? lineWidths = null)
    {
        if (firstLine == TextTransform.None)
            return WrapText(text, maxWidth, font, whiteSpace, lineHeight, lineWidths);
        var transformed = StyleExtensions.ApplyTextTransform(text, firstLine, lang);
        if (whiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine || transformed.Length != text.Length)
        {
            var plain = WrapText(text, maxWidth, font, whiteSpace, lineHeight, lineWidths);
            if (plain.Count > 0)
            {
                var head = StyleExtensions.ApplyTextTransform(plain[0].Text, firstLine, lang);
                plain[0] = plain[0] with { Text = head, Width = font.MeasureText(head) };
            }
            return plain;
        }
        var firstPass = WrapText(transformed, maxWidth, font, whiteSpace, lineHeight, lineWidths);
        if (firstPass.Count <= 1) return firstPass;
        // The first line holds the transformed text's first k words; the rest is the plain
        // text after the same k words.
        var k = firstPass[0].Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (k <= 0 || k >= words.Length) return firstPass;
        var rest = string.Join(' ', words.Skip(k));
        var tail = WrapText(rest, maxWidth, font, whiteSpace, lineHeight, lineWidths?.Skip(1).ToList());
        return [firstPass[0], .. tail];
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
            // §16.4: word spacing applies to every space and no-break space.
            if (ch is ' ' or '\u00A0') w += wordSpacing;
        }
        // §16.4: the spacing follows EVERY character, the run's last one included, as browsers
        // apply it — dropping the last one closed the gap where a word continues into the next
        // inline ('T<span>his</span>' under letter-spacing, text-transform-cap-002).
        return Math.Max(0f, w);
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

        var words = text.TrimCss().Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
                // pre-wrap breaks only after spaces (§16.6), never inside a word, and the spaces
                // at the break hang off the line end: they never push the next word down. Breaking
                // per character split "XX  XX" mid-word (white-space-processing-052).
                var line = new System.Text.StringBuilder();
                var i = 0;
                while (i < raw.Length)
                {
                    var wordEnd = i;
                    while (wordEnd < raw.Length && raw[wordEnd] != ' ') wordEnd++;
                    var spaceEnd = wordEnd;
                    while (spaceEnd < raw.Length && raw[spaceEnd] == ' ') spaceEnd++;
                    var word = raw[i..wordEnd];
                    if (line.Length > 0 && word.Length > 0 && font.MeasureText(line + word) > maxWidth)
                    {
                        var seg = line.ToString();
                        lines.Add(new TextLine(seg, font.MeasureText(seg.TrimEnd(' ')), lineHeight, ascent));
                        line.Clear();
                    }
                    line.Append(raw, i, spaceEnd - i);
                    i = spaceEnd;
                }
                var last = line.ToString();
                lines.Add(new TextLine(last, font.MeasureText(last.TrimEnd(' ')), lineHeight, ascent));
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
