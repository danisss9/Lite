using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Registry of custom typefaces: @font-face registrations loaded from URLs, plus persistent
/// test-font provisioning. Maps a family to its faces, matched by weight and style.
/// </summary>
internal static class FontRegistry
{
    /// <summary>One face of a family: its typeface, OS/2 weight class and slant.</summary>
    private sealed record Face(SKTypeface Typeface, int Weight, bool Italic, bool Persistent);

    // Family name → its faces. Matching picks among them by style, then weight (CSS 2.1 §15.5,
    // refined by CSS Fonts 3 §5.2): one family can carry nine weights (the CSSTest Weights Full
    // fonts), which a (bold, italic) key could only hold two of.
    private static readonly Dictionary<string, List<Face>> _families = new(StringComparer.OrdinalIgnoreCase);

    internal static void Clear()
    {
        // Provisioned test fonts survive the per-document Clear() (FontRegistry is wiped during
        // every page load because @font-face rules must be re-collected); the official CSS 2.1
        // suite references its test fonts by family name without @font-face, so they must persist.
        foreach (var faces in _families.Values)
        {
            foreach (var face in faces)
                if (!face.Persistent) face.Typeface.Dispose();
            faces.RemoveAll(f => !f.Persistent);
        }
        foreach (var empty in _families.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
            _families.Remove(empty);
    }

    internal static void Register(string family, bool bold, bool italic, byte[] fontData) =>
        Register(family, bold ? TextMeasure.BoldWeight : TextMeasure.NormalWeight, italic, fontData);

    /// <summary>Registers an @font-face source under its descriptor weight and style.</summary>
    internal static void Register(string family, int weight, bool italic, byte[] fontData)
    {
        var faces = FacesOf(family);
        if (faces.Any(f => f.Weight == weight && f.Italic == italic)) return;
        var data = SKData.Create(new MemoryStream(fontData));
        var tf = SKTypeface.FromData(data);
        if (tf != null)
        {
            faces.Add(new Face(tf, weight, italic, Persistent: false));
            Console.WriteLine($"[FontRegistry] Registered '{family}' weight={weight} italic={italic}");
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
            var weight = tf.FontWeight;
            var italic = tf.FontStyle.Slant != SKFontStyleSlant.Upright;
            if (!TryAdd(family, tf, weight, italic)) { tf.Dispose(); return; }
            // The name table's LOCALIZED and typographic family names (nameID 1/16 on every
            // platform record) alias to the same face: the official suite's localized-name tests
            // address the CSSTest fonts by their Japanese fullwidth names, which Skia's primary
            // FamilyName never exposes (font-family-name-010), and the CSSTest Weights fonts
            // share one typographic family ("CSSTest Weights Full") across nine weight faces.
            foreach (var alias in LocalizedFamilyNames(path))
                if (!alias.Equals(family, StringComparison.OrdinalIgnoreCase))
                    TryAdd(alias, tf, weight, italic);
            Console.WriteLine($"[FontRegistry] Provisioned '{family}' weight={weight} italic={italic}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FontRegistry] Failed to provision {Path.GetFileName(path)}: {ex.Message}");
        }

        static bool TryAdd(string family, SKTypeface tf, int weight, bool italic)
        {
            var faces = FacesOf(family);
            if (faces.Any(f => f.Weight == weight && f.Italic == italic)) return false;
            faces.Add(new Face(tf, weight, italic, Persistent: true));
            return true;
        }
    }

    private static List<Face> FacesOf(string family)
    {
        var key = Normalize(family);
        if (!_families.TryGetValue(key, out var faces)) _families[key] = faces = [];
        return faces;
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

    /// <summary>The registered face of <paramref name="family"/> that best matches the desired
    /// weight and style, or null when the family is not registered. CSS 2.1 §15.5 step 3 matches
    /// 'font-style' first (an italic request takes an italic face, else the upright one, which
    /// the painter then slants), then 'font-weight' (§15.6) by the CSS Fonts 3 §5.2 order:
    /// <list type="bullet">
    /// <item>a desired weight from 400 to 500 tries the weights up to 500 ascending, then the
    /// lighter ones descending, then the heavier ones ascending (400 picks 500 before 300);</item>
    /// <item>below 400, the lighter weights descending, then the heavier ascending;</item>
    /// <item>above 500, the heavier weights ascending, then the lighter descending.</item>
    /// </list></summary>
    internal static SKTypeface? Resolve(string family, int weight, bool italic)
    {
        if (!_families.TryGetValue(Normalize(family), out var faces) || faces.Count == 0) return null;
        var styled = faces.Where(f => f.Italic == italic).ToList();
        if (styled.Count == 0) styled = faces;
        return MatchWeight(styled, weight)?.Typeface;
    }

    private static Face? MatchWeight(IReadOnlyList<Face> faces, int desired)
    {
        if (faces.FirstOrDefault(f => f.Weight == desired) is { } exact) return exact;
        Face? Lightest(Func<Face, bool> range) => faces.Where(range).OrderBy(f => f.Weight).FirstOrDefault();
        Face? Heaviest(Func<Face, bool> range) => faces.Where(range).OrderByDescending(f => f.Weight).FirstOrDefault();
        if (desired is >= 400 and <= 500)
            return Lightest(f => f.Weight > desired && f.Weight <= 500)
                   ?? Heaviest(f => f.Weight < desired)
                   ?? Lightest(f => f.Weight > 500);
        if (desired < 400)
            return Heaviest(f => f.Weight < desired) ?? Lightest(f => f.Weight > desired);
        return Lightest(f => f.Weight > desired) ?? Heaviest(f => f.Weight < desired);
    }

    /// <summary>Whether a provisioned family exists under this exact name (case-insensitive).
    /// Used by font-family list resolution: a provisioned name wins over Skia's fuzzy
    /// FromFamilyName matching, which would e.g. let "CSSTest   FamilyName" (three spaces)
    /// wrongly resolve to "CSSTest FamilyName".</summary>
    internal static bool HasFamily(string family) =>
        _families.TryGetValue(Normalize(family), out var faces) && faces.Count > 0;

    private static string Normalize(string family) => family.Trim().Trim('"', '\'');
}
