using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal record TextLine(string Text, float Width, float Height, float Ascent);

internal static class TextMeasure
{
    public static SKFont CreateFont(LayoutNode node)
    {
        var size     = node.GetFontSize();
        var family   = node.GetFontFamily();
        var bold     = node.GetFontBold();
        var italic   = node.GetFontItalic();
        var slant    = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var typeface = SKTypeface.FromFamilyName(family,
                           bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal, slant);
        return new SKFont(typeface, size);
    }

    /// <summary>
    /// Wraps text into lines that fit within maxWidth, respecting the node's white-space mode.
    /// Each TextLine carries the text, measured width, line height, and baseline ascent.
    /// </summary>
    public static List<TextLine> WrapText(string text, float maxWidth, SKFont font, WhiteSpace whiteSpace = WhiteSpace.Normal)
    {
        return whiteSpace switch
        {
            WhiteSpace.Pre     => SplitPreserved(text, font, wrap: false),
            WhiteSpace.PreWrap => SplitPreserved(text, font, wrap: true, maxWidth),
            WhiteSpace.PreLine => SplitPreLine(text, maxWidth, font),
            WhiteSpace.NoWrap  => WrapCollapsed(text, float.MaxValue, font),
            _                  => WrapCollapsed(text, maxWidth, font),
        };
    }

    /// <summary>
    /// Measures text without wrapping — returns total width and single-line height.
    /// </summary>
    public static (float Width, float Height, float Ascent) MeasureSingleLine(string text, SKFont font)
    {
        var ascent = -font.Metrics.Ascent;
        return (font.MeasureText(text), font.Size * 1.4f, ascent);
    }

    // -------------------------------------------------------------------------
    // white-space modes
    // -------------------------------------------------------------------------

    /// <summary>Collapse whitespace and word-wrap at maxWidth (normal / nowrap).</summary>
    private static List<TextLine> WrapCollapsed(string text, float maxWidth, SKFont font)
    {
        var lines      = new List<TextLine>();
        var lineHeight = font.Size * 1.4f;
        var ascent     = -font.Metrics.Ascent;

        // Fast path: text already fits — return it verbatim so that leading/trailing
        // spaces (significant in inline runs like " and ") are preserved.
        if (font.MeasureText(text) <= maxWidth)
        {
            lines.Add(new TextLine(text, font.MeasureText(text), lineHeight, ascent));
            return lines;
        }

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb    = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            var candidate = sb.Length == 0 ? word : sb + " " + word;
            if (font.MeasureText(candidate) > maxWidth && sb.Length > 0)
            {
                var lineText = sb.ToString();
                lines.Add(new TextLine(lineText, font.MeasureText(lineText), lineHeight, ascent));
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
    private static List<TextLine> SplitPreserved(string text, SKFont font, bool wrap, float maxWidth = float.MaxValue)
    {
        var lines      = new List<TextLine>();
        var lineHeight = font.Size * 1.4f;
        var ascent     = -font.Metrics.Ascent;

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
    private static List<TextLine> SplitPreLine(string text, float maxWidth, SKFont font)
    {
        var lines = new List<TextLine>();
        foreach (var rawLine in text.Split('\n'))
            lines.AddRange(WrapCollapsed(rawLine, maxWidth, font));
        return lines;
    }
}
