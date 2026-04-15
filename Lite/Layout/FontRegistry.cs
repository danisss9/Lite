using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Registry of custom @font-face typefaces loaded from URLs.
/// Maps (family, bold, italic) to an SKTypeface created from downloaded font data.
/// </summary>
internal static class FontRegistry
{
    private static readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.OrdinalIgnoreCase);

    internal static void Clear()
    {
        foreach (var tf in _typefaces.Values)
            tf.Dispose();
        _typefaces.Clear();
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

    private static string MakeKey(string family, bool bold, bool italic) =>
        $"{family.Trim().Trim('\"', '\'')}|{(bold ? "b" : "n")}|{(italic ? "i" : "n")}";
}
