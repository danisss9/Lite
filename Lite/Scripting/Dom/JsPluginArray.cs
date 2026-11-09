using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>The PDF viewer plugin entries (HTML §7.5.2): navigator.plugins presents the two
/// viewer entries back and forward — PDF Viewer first, then the engine-branded one — each
/// holding the two PDF MIME types. A PluginArray with the legacy indexed + named access and
/// refresh(); namedItem identity matches indexed access (plugins-and-mimetypes).</summary>
public class JsPluginArray
{
    private readonly List<JsPlugin> _plugins;

    public JsPluginArray()
    {
        var pdfTypes = new[] { "application/pdf", "text/pdf" };
        _plugins =
        [
            new JsPlugin("PDF Viewer", "Portable Document Format", "pdf", pdfTypes),
            new JsPlugin("Lite PDF Viewer", "Portable Document Format", "pdf", pdfTypes),
        ];
    }

    public int length => _plugins.Count;

    public JsPlugin? item(int index) => index >= 0 && index < _plugins.Count ? _plugins[index] : null;

    public JsPlugin? namedItem(string name) => _plugins.FirstOrDefault(p => p.name == name);

    public void refresh() { }

    public JsPlugin? this[int index] => item(index);
}

/// <summary>One navigator.plugins entry: the viewer name, its description, filename suffix and
/// the MIME types it handles — each type an entry of navigator.mimeTypes back-referencing the
/// plugin (enabledPlugin).</summary>
public class JsPlugin
{
    public string name { get; }
    public string description { get; }
    public string filename { get; }
    public int length => 2;
    private readonly JsMimeType[] _types;

    internal JsPlugin(string name, string description, string filename, string[] types)
    {
        this.name = name;
        this.description = description;
        this.filename = filename;
        _types = types.Select(t => new JsMimeType(t, description, filename, this)).ToArray();
    }

    public JsMimeType? item(int index) => index >= 0 && index < _types.Length ? _types[index] : null;

    public JsMimeType? namedItem(string name) => _types.FirstOrDefault(m => m.type == name);

    public JsMimeType? this[int index] => item(index);
}

/// <summary>One navigator.mimeTypes entry.</summary>
public class JsMimeType
{
    public string type { get; }
    public string description { get; }
    public string suffixes { get; }
    public JsPlugin enabledPlugin { get; }

    internal JsMimeType(string type, string description, string suffixes, JsPlugin plugin)
    {
        this.type = type;
        this.description = description;
        this.suffixes = suffixes;
        enabledPlugin = plugin;
    }
}

/// <summary>navigator.mimeTypes: the two PDF types with legacy indexed + named access, built
/// over the plugin array's own entries so enabledPlugin back-references the viewer plugin.</summary>
public class JsMimeTypeArray
{
    private readonly JsMimeType[] _types;

    internal JsMimeTypeArray(JsPluginArray plugins)
    {
        // One entry per unique MIME type: a second viewer for the same type does not add
        // another mimeTypes row (plugins-and-mimetypes expects length 2).
        var types = new List<JsMimeType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var p = 0; p < plugins.length; p++)
            for (var i = 0; i < plugins.item(p)!.length; i++)
            {
                var t = plugins.item(p)!.item(i)!;
                if (seen.Add(t.type)) types.Add(t);
            }
        _types = types.ToArray();
    }

    public int length => _types.Length;

    public JsMimeType? item(int index) => index >= 0 && index < _types.Length ? _types[index] : null;

    public JsMimeType? namedItem(string name) => _types.FirstOrDefault(m => m.type == name);

    public JsMimeType? this[int index] => item(index);
}
