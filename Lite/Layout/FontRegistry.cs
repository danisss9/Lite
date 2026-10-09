using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Registry of custom typefaces: @font-face registrations loaded from URLs, plus persistent
/// test-font provisioning. Maps (family, bold, italic) to an SKTypeface.
/// </summary>
internal static class FontRegistry
{
    private static readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.OrdinalIgnoreCase);
    // Provisioned test fonts survive the per-document Clear() (FontRegistry is wiped during
    // every page load because @font-face rules must be re-collected); the official CSS 2.1
    // suite references its test fonts by family name without @font-face, so they must persist.
    private static readonly HashSet<string> _persistent = new(StringComparer.OrdinalIgnoreCase);

    internal static void Clear()
    {
        foreach (var (key, tf) in _typefaces)
            if (!_persistent.Contains(key)) tf.Dispose();
        var kept = _typefaces.Where(kv => _persistent.Contains(kv.Key)).ToList();
        _typefaces.Clear();
        foreach (var (key, tf) in kept) _typefaces[key] = tf;
    }

    internal static void Register(string family, bool bold, bool italic, byte[] fontData)
    {
        var key = MakeKey(family, bold, italic);
        if (_typefaces.ContainsKey(key)) return;
        var data = SKData.Create(new MemoryStream(fontData));
        var tf = SKTypeface.FromData(data);
        if (tf != null)
        {
            _typefaces[key] = tf;
            Console.WriteLine($"[FontRegistry] Registered '{family}' bold={bold} italic={italic}");
        }
    }

    /// <summary>Registers a font file under the family name its name table declares, marked
    /// persistent (survives Clear()). Used to provision the CSS 2.1 / WPT test fonts.</summary>
    internal static void RegisterFile(string path)
    {
        try
        {
            var tf = SKTypeface.FromFile(path);
            if (tf is null) return;
            var family = tf.FamilyName;
            var bold = tf.FontWeight >= 600;
            var italic = tf.FontStyle.Slant != SKFontStyleSlant.Upright;
            var key = MakeKey(family, bold, italic);
            if (_typefaces.ContainsKey(key)) { tf.Dispose(); return; }
            _typefaces[key] = tf;
            _persistent.Add(key);
            Console.WriteLine($"[FontRegistry] Provisioned '{family}' bold={bold} italic={italic}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FontRegistry] Failed to provision {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    internal static SKTypeface? Resolve(string family, bool bold, bool italic)
    {
        // Try exact match first
        if (_typefaces.TryGetValue(MakeKey(family, bold, italic), out var tf))
            return tf;
        // Fallback: try normal weight/style for this family
        if (_typefaces.TryGetValue(MakeKey(family, false, false), out tf))
            return tf;
        return null;
    }

    /// <summary>Whether a provisioned family exists under this exact name (case-insensitive).
    /// Used by font-family list resolution: a provisioned name wins over Skia's fuzzy
    /// FromFamilyName matching, which would e.g. let "CSSTest   FamilyName" (three spaces)
    /// wrongly resolve to "CSSTest FamilyName".</summary>
    internal static bool HasFamily(string family)
    {
        var prefix = family.Trim().Trim('"', '\'') + "|";
        return _typefaces.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string MakeKey(string family, bool bold, bool italic) =>
        $"{family.Trim().Trim('\"', '\'')}|{(bold ? "b" : "n")}|{(italic ? "i" : "n")}";
}
