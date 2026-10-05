using System.Collections.Concurrent;
using System.Text;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>The Blob interface (File API dependency referenced by HTML 5.0 §2.7.5): immutable
/// byte sequence assembled from string/blob/file parts. Text is stored UTF-8; binary parts
/// beyond blob/file passthrough are not modeled.</summary>
public class JsBlob
{
    private readonly string _content;
    private readonly string _type;

    public JsBlob(JsValue[]? parts = null, JsValue? options = null)
    {
        var sb = new StringBuilder();
        foreach (var part in parts ?? [])
        {
            if (part.IsString()) sb.Append(part.AsString());
            else if (part.ToObject() is JsBlob inner) sb.Append(inner._content);
            else if (part.ToObject() is JsFile file) sb.Append(file.text());
        }
        _content = sb.ToString();
        _type = NormalizeType(options);
    }

    /// <summary>Constructs a blob from text with an explicit media type (used by the URL registry).</summary>
    internal JsBlob(string content, string type) { _content = content; _type = NormalizeType(type); }

    public double size => Encoding.UTF8.GetByteCount(_content);
    public string type => _type;

    /// <summary>Blob.text() — the spec returns a promise; callers only consume the value.</summary>
    public string text() => _content;

    internal string Content => _content;

    private static string NormalizeType(JsValue? options)
    {
        if (options is null || !options.IsObject()) return "";
        var raw = options.AsObject().Get("type").AsString() ?? "";
        // ASCII case-insensitive media type; strip trailing parameter-free whitespace only.
        return raw.Trim().ToLowerInvariant();
    }

    private static string NormalizeType(string type) => type.Trim().ToLowerInvariant();
}

/// <summary>Blob URL store backing URL.createObjectURL/revokeObjectURL. URLs are process-wide
/// and unguessable; session partitioning is not modeled.</summary>
public static class BlobUrlRegistry
{
    private static readonly ConcurrentDictionary<string, JsBlob> Blobs = new(StringComparer.Ordinal);

    public static string Create(JsBlob blob)
    {
        var url = "blob:" + Guid.NewGuid().ToString("N");
        Blobs[url] = blob;
        return url;
    }

    public static bool Revoke(string url) => Blobs.TryRemove(url, out _);

    public static bool TryResolve(string url, out JsBlob? blob)
    {
        if (url.StartsWith("blob:", StringComparison.Ordinal))
            return Blobs.TryGetValue(url, out blob) && blob is not null;
        blob = null;
        return false;
    }
}
