using System.Globalization;
using AngleSharp.Css.Values;

namespace Lite.Layout;

/// <summary>
/// Central CSS length-unit resolution. Converts every CSS 2.1 / common CSS3 length unit to
/// pixels: absolute (px, pt, pc, in, cm, mm, q), font-relative (em, rem, ex, ch), and
/// viewport (vw, vh, vmin, vmax), plus percentages. Replaces the per-call-site switch
/// statements that previously only knew px/em/%/vw/vh.
/// </summary>
internal static class CssUnits
{
    // CSS reference pixel: 96px == 1in.
    private const float PxPerPt = 96f / 72f;       // 1pt = 1/72 in
    private const float PxPerPc = 16f;             // 1pc = 12pt
    private const float PxPerIn = 96f;
    private const float PxPerCm = 96f / 2.54f;     // 1in = 2.54cm
    private const float PxPerMm = 96f / 25.4f;
    private const float PxPerQ = 96f / 101.6f;     // 1q = 1/40 cm

    /// <summary>Root element (html) font-size in px, used for rem. Set once per layout pass;
    /// defaults to the CSS initial 16px.</summary>
    public static float RootFontSize { get; set; } = 16f;

    /// <summary>Converts an AngleSharp <see cref="Length"/> to pixels.</summary>
    /// <param name="fontSize">The element's font-size (for em/ex/ch).</param>
    /// <param name="pctBasis">The basis for percentage values.</param>
    /// <param name="vw">Viewport width; <param name="vh">Viewport height (default to vw when unknown).</param></param>
    public static float ToPx(Length length, float fontSize, float pctBasis, float vw, float vh) =>
        ToPx(length.Type, length.Value, fontSize, pctBasis, vw, vh);

    public static float ToPx(Length.Unit unit, double value, float fontSize, float pctBasis, float vw, float vh)
    {
        var v = (float)value;
        return unit switch
        {
            Length.Unit.Px => v,
            Length.Unit.Em => v * fontSize,
            Length.Unit.Rem => v * RootFontSize,
            Length.Unit.Ex => v * fontSize * 0.5f,   // approximation: ex ≈ 0.5em
            Length.Unit.Ch => v * fontSize * 0.5f,    // approximation: ch ≈ 0.5em
            Length.Unit.Percent => v / 100f * pctBasis,
            Length.Unit.Vw => v / 100f * vw,
            Length.Unit.Vh => v / 100f * vh,
            Length.Unit.Vmin => v / 100f * Math.Min(vw, vh),
            Length.Unit.Vmax => v / 100f * Math.Max(vw, vh),
            Length.Unit.Pt => v * PxPerPt,
            Length.Unit.Pc => v * PxPerPc,
            Length.Unit.In => v * PxPerIn,
            Length.Unit.Cm => v * PxPerCm,
            Length.Unit.Mm => v * PxPerMm,
            _ => v, // unknown unit: treat the number as px
        };
    }

    /// <summary>Converts a unit suffix (as found in a calc() token or raw string) + number to px.</summary>
    public static float UnitStringToPx(string unit, float value, float fontSize, float pctBasis, float vw, float vh)
        => unit.ToLowerInvariant() switch
        {
            "px" or "" => value,
            "em" => value * fontSize,
            "rem" => value * RootFontSize,
            "ex" => value * fontSize * 0.5f,
            "ch" => value * fontSize * 0.5f,
            "%" => value / 100f * pctBasis,
            "vw" => value / 100f * vw,
            "vh" => value / 100f * vh,
            "vmin" => value / 100f * Math.Min(vw, vh),
            "vmax" => value / 100f * Math.Max(vw, vh),
            "pt" => value * PxPerPt,
            "pc" => value * PxPerPc,
            "in" => value * PxPerIn,
            "cm" => value * PxPerCm,
            "mm" => value * PxPerMm,
            "q" => value * PxPerQ,
            _ => value,
        };

    private static readonly string[] KnownUnits =
        ["px", "rem", "em", "ex", "ch", "vw", "vh", "vmin", "vmax", "pt", "pc", "in", "cm", "mm", "q", "%"];

    /// <summary>Parses a single CSS length token (e.g. "12pt", "1.5rem", "50%") to pixels.
    /// Returns false for non-length tokens (auto, none, keywords, calc()).</summary>
    public static bool TryParse(string token, float fontSize, float pctBasis, float vw, float vh, out float px)
    {
        px = 0f;
        token = token.Trim();
        if (token.Length == 0) return false;

        // Unitless 0 is a valid length.
        if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare))
        {
            px = bare;
            return bare == 0f; // only unitless 0 is a valid length per CSS
        }

        foreach (var unit in KnownUnits)
        {
            if (!token.EndsWith(unit, StringComparison.OrdinalIgnoreCase)) continue;
            var numPart = token[..^unit.Length];
            if (float.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                px = UnitStringToPx(unit, num, fontSize, pctBasis, vw, vh);
                return true;
            }
        }
        return false;
    }
}
