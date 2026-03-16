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
        var typeface = SKTypeface.FromFamilyName(family, bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                                                 SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        return new SKFont(typeface, size);
    }

    /// <summary>
    /// Wraps text into lines that fit within maxWidth.
    /// Each TextLine carries the text, measured width, line height, and baseline ascent.
    /// </summary>
    public static List<TextLine> WrapText(string text, float maxWidth, SKFont font)
    {
        var lines      = new List<TextLine>();
        var lineHeight = font.Size * 1.4f;
        var ascent     = -font.Metrics.Ascent; // Metrics.Ascent is negative in SkiaSharp

        var words     = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb        = new System.Text.StringBuilder();

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

    /// <summary>
    /// Measures text without wrapping — returns total width and single-line height.
    /// </summary>
    public static (float Width, float Height, float Ascent) MeasureSingleLine(string text, SKFont font)
    {
        var ascent = -font.Metrics.Ascent;
        return (font.MeasureText(text), font.Size * 1.4f, ascent);
    }
}
