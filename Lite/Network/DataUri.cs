namespace Lite.Network;

/// <summary>Decodes <c>data:[&lt;mediatype&gt;][;base64],&lt;data&gt;</c> URIs.</summary>
internal static class DataUri
{
    public static bool IsDataUri(string url) =>
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Decodes a data: URI to its text payload. Returns false if malformed.</summary>
    public static bool TryDecodeText(string dataUri, out string text, out string mediaType)
    {
        text = "";
        mediaType = "text/plain";
        if (!IsDataUri(dataUri)) return false;

        var comma = dataUri.IndexOf(',');
        if (comma < 0) return false;

        var meta = dataUri[5..comma];
        var data = dataUri[(comma + 1)..];
        if (meta.Length > 0)
        {
            var semi = meta.IndexOf(';');
            mediaType = semi < 0 ? meta : meta[..semi];
            if (mediaType.Length == 0) mediaType = "text/plain";
        }

        text = meta.Contains("base64", StringComparison.OrdinalIgnoreCase)
            ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data))
            : Uri.UnescapeDataString(data);
        return true;
    }

    /// <summary>Decodes a data: URI to raw bytes (for images). Returns false if malformed.</summary>
    public static bool TryDecodeBytes(string dataUri, out byte[] bytes, out string mediaType)
    {
        bytes = [];
        mediaType = "application/octet-stream";
        if (!IsDataUri(dataUri)) return false;

        var comma = dataUri.IndexOf(',');
        if (comma < 0) return false;

        var meta = dataUri[5..comma];
        var data = dataUri[(comma + 1)..];
        if (meta.Length > 0)
        {
            var semi = meta.IndexOf(';');
            mediaType = semi < 0 ? meta : meta[..semi];
        }

        bytes = meta.Contains("base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(data)
            : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
        return true;
    }
}
