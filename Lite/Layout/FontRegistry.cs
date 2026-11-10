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
            // The name table's LOCALIZED family names (nameID 1/16 on every platform record)
            // alias to the same face: the official suite's localized-name tests address the
            // CSSTest fonts by their Japanese fullwidth names, which Skia's primary
            // FamilyName never exposes (font-family-name-010).
            foreach (var alias in LocalizedFamilyNames(path))
            {
                if (alias.Equals(family, StringComparison.OrdinalIgnoreCase)) continue;
                var aliasKey = MakeKey(alias, bold, italic);
                if (_typefaces.TryAdd(aliasKey, tf)) _persistent.Add(aliasKey);
            }
            Console.WriteLine($"[FontRegistry] Provisioned '{family}' bold={bold} italic={italic}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FontRegistry] Failed to provision {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    /// <summary>Every family name (nameID 1 and 16) the font's 'name' table declares — all
    /// platforms and encodings. A minimal big-endian sfnt walk: the table holds length-prefixed
    /// records whose string values are UTF-16BE (Windows) or the Mac ASCII form.</summary>
    private static IEnumerable<string> LocalizedFamilyNames(string path)
    {
        var names = new List<string>();
        try
        {
            var data = File.ReadAllBytes(path);
            static ushort U16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
            static uint U32(byte[] b, int o) => ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
            if (data.Length < 12) return names;
            var sfnt = U32(data, 0);
            if (sfnt != 0x00010000 && sfnt != 0x74727565 && sfnt != 0x4F54544F) return names;
            var numTables = U16(data, 4);
            uint tableOffset = 0;
            for (var i = 0; i < numTables && 12 + (i + 1) * 16 <= data.Length; i++)
            {
                var o = 12 + i * 16;
                var tag = U32(data, o);
                tableOffset = U32(data, o + 8);
                if (tag == 0x6E616D65) break; // 'name'
                tableOffset = 0;
            }
            if (tableOffset == 0 || tableOffset + 6 > data.Length) return names;
            var count = U16(data, (int)tableOffset + 2);
            var stringOffset = U16(data, (int)tableOffset + 4);
            var records = new List<(int Platform, ushort Offset, ushort Length)>();
            for (var i = 0; i < count; i++)
            {
                var rec = (int)tableOffset + 6 + i * 12;
                if (rec + 12 > data.Length) break;
                var platform = U16(data, rec);
                var nameId = U16(data, rec + 6);
                if (nameId is not (1 or 16)) continue;
                // The 'name' record carries LENGTH then OFFSET (the "name" table spec).
                var length = U16(data, rec + 8);
                var offset = U16(data, rec + 10);
                records.Add((platform, offset, length));
            }
            foreach (var (platform, offset, length) in records)
            {
                var str = (int)(tableOffset + stringOffset + offset);
                if (length == 0 || str + length > data.Length) continue;
                // Windows/Unicode records are UTF-16BE; Mac records are the ASCII form.
                string name;
                if (platform is 0 or 3 && length % 2 == 0)
                {
                    var chars = new char[length / 2];
                    for (var c = 0; c < chars.Length; c++)
                        chars[c] = (char)((data[str + c * 2] << 8) | data[str + c * 2 + 1]);
                    name = new string(chars);
                }
                else name = System.Text.Encoding.ASCII.GetString(data, str, length);
                name = name.Trim('\0', ' ');
                if (name.Length > 0) names.Add(name);
            }
        }
        catch
        {
            // A malformed name table only costs the aliases; the primary name still works.
        }
        return names;
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
