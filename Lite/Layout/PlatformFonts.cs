using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// The system families Lite substitutes for CSS UI/monospace keywords and uses as glyph
/// fallbacks. Windows names its core fonts directly; elsewhere the fontconfig aliases for the
/// generic families pick whatever the distribution installed (e.g. DejaVu, Noto, Liberation).
/// </summary>
internal static class PlatformFonts
{
    /// <summary>The family for <c>system-ui</c> and friends.</summary>
    public static string SystemUi { get; } = OperatingSystem.IsWindows() ? "Segoe UI" : Resolve("sans-serif", "DejaVu Sans");

    /// <summary>The family for the <c>serif</c> generic keyword. Skia's manager has no family
    /// literally named "serif", so asking it directly returns the DEFAULT face (Segoe UI on
    /// Windows) — a sans face whose ascent+descent (1.33em) overflows a 1.2 line-height and
    /// paints descenders outside their boxes. Browsers ship Times New Roman here (1.107em
    /// fits), and the CSS 2.1 suite's references assume a serif face of that class.</summary>
    public static string Serif { get; } = OperatingSystem.IsWindows() ? "Times New Roman" : Resolve("serif", "DejaVu Serif");

    /// <summary>The family for <c>monospace</c>, <c>Courier</c> and text-entry controls.</summary>
    public static string Monospace { get; } = OperatingSystem.IsWindows() ? "Consolas" : Resolve("monospace", "DejaVu Sans Mono");

    /// <summary>Families with broad symbol coverage, tried when the chosen font lacks a glyph.</summary>
    public static string[] SymbolFallbacks { get; } = OperatingSystem.IsWindows()
        ? ["Segoe UI Symbol", "Segoe UI Emoji", "Arial Unicode MS", "Lucida Sans Unicode"]
        : ["Noto Sans Symbols", "Noto Sans Symbols2", "Noto Color Emoji", "DejaVu Sans", "Symbola"];

    private static string Resolve(string alias, string fallback)
    {
        using var typeface = SKTypeface.FromFamilyName(alias);
        return typeface?.FamilyName is { Length: > 0 } name ? name : fallback;
    }
}
