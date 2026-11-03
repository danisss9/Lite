using AngleSharp.Text;
using System.Text;
using System.Text.RegularExpressions;

namespace Lite;

/// <summary>
/// REC §8.2.2.1 (determining the character encoding) over a fetched byte stream: BOM first
/// (a willful override of the transport layer), then the HTTP Content-Type charset, then a
/// prescan of the first 1024 bytes for a <c>&lt;meta charset&gt;</c> / <c>http-equiv</c>
/// declaration, then the implementation-defined default. Lite's default is UTF-8 — the 2014
/// Recommendation leaves the final fallback implementation-defined (windows-1252 is only the
/// suggested locale default), and a UTF-8 default keeps undeclared ASCII documents
/// byte-faithful.
///
/// When the confidence is tentative (prescan or default), the decoded text is also scanned
/// for a declaration beyond the prescan window: a browser's parser restarts on a late meta
/// (§8.2.5.4's meta handling re-decodes the raw bytes and re-parses the document), and the
/// load-time equivalent is re-decoding before the string pipeline starts — the observable
/// end state (the document text) is what a browser's restart converges to. A meta-declared
/// UTF-16 is normalized to UTF-8 per the encoding-change rules: bytes that reached the meta
/// tokenized as the tentative encoding, so they cannot be UTF-16.
/// </summary>
internal static class EncodingSniffer
{
    private const int PrescanLimit = 1024;

    static EncodingSniffer()
    {
        // The code-pages encodings (windows-125x, shift_jis, ...) are not part of the Linux
        // default encoding set; without the provider, TextEncoding's static fields silently
        // bake in UTF-8 fallbacks for every one of those labels. Registering here (before
        // the first TextEncoding touch through this class) keeps the sniff identical on the
        // Linux CI job.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Extracts a charset label from a Content-Type-style content attribute value.</summary>
    private static readonly Regex CharsetInContentType =
        new(@"charset\s*=\s*[""']?([A-Za-z0-9_.:\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string DecodeDocument(byte[] bytes, string? transportCharset)
    {
        // BOM (certain): UTF-8, UTF-16 LE/BE. The mark is consumed — input preprocessing
        // drops it from the stream.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return TextEncoding.Utf8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return TextEncoding.Utf16Le.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return TextEncoding.Utf16Be.GetString(bytes, 2, bytes.Length - 2);

        // Transport layer (certain), when the label resolves.
        if (ResolveTransport(transportCharset) is { } transport)
            return transport.GetString(bytes);

        // Prescan of the first 1024 bytes (tentative).
        if (PrescanCharset(bytes, PrescanLimit) is { } sniffed)
            return ResolveMeta(sniffed)!.GetString(bytes);

        // Implementation-defined default (tentative): UTF-8, with the late-declaration
        // restart applied over the full byte walk (the same comment-aware walker, so a
        // declaration inside <!-- ... --> never applies — the same discrimination a
        // browser's tokenizer makes before its encoding change).
        var html = TextEncoding.Utf8.GetString(bytes);
        if (PrescanCharset(bytes, bytes.Length) is { } declared &&
            ResolveMeta(declared) is { } declaredEncoding &&
            !declaredEncoding.Equals(TextEncoding.Utf8))
        {
            return declaredEncoding.GetString(bytes);
        }

        return html;
    }

    /// <summary>Resolves a transport-layer label exactly as declared (a "utf-16" Content-Type
    /// decodes as UTF-16LE per its label; the UTF-16→UTF-8 remap is a meta-declaration rule).</summary>
    private static Encoding? ResolveTransport(string? label) =>
        label is not null && TextEncoding.IsSupported(label.Trim()) ? TextEncoding.Resolve(label.Trim()) : null;

    /// <summary>Resolves a meta-declared label through AngleSharp's HTML label table (the
    /// case-insensitive dictionary covering the WHATWG aliases). Unknown labels yield null;
    /// a declared UTF-16 becomes UTF-8 (the encoding-change rule).</summary>
    private static Encoding? ResolveMeta(string? label)
    {
        if (label is null || !TextEncoding.IsSupported(label.Trim()))
            return null;
        var encoding = TextEncoding.Resolve(label.Trim());
        return encoding.IsUnicode() ? TextEncoding.Utf8 : encoding;
    }

    // ----- the prescan: a byte-level walk that honors comments, bogus comments, quoted
    // ----- attribute values and non-meta tags, returning the first resolvable charset
    // ----- declaration within <paramref name="limit"/> bytes. Known simplification vs the
    // ----- parser's own meta discovery: markup-looking text inside script/textarea content
    // ----- is not discriminated (the full-document restart pass walks the same way).

    private static string? PrescanCharset(byte[] bytes, int limit)
    {
        limit = Math.Min(bytes.Length, limit);
        var position = 0;
        while (position < limit)
        {
            if (bytes[position] != (byte)'<')
            {
                position++;
                continue;
            }

            if (position + 1 >= limit) return null;
            var next = bytes[position + 1];
            if (next == (byte)'!')
            {
                if (ContinuesWith(bytes, limit, position + 2, "--"))
                {
                    var end = IndexOfMarker(bytes, limit, position + 4, "-->");
                    if (end < 0) return null;
                    position = end + 3;
                }
                else
                {
                    var end = IndexOfMarker(bytes, limit, position + 2, ">");
                    if (end < 0) return null;
                    position = end + 1;
                }
            }
            else if (next == (byte)'?' || next == (byte)'/')
            {
                // <? and </ both run to the next > (end tags carry no attribute values the
                // prescan reads; a quoted > inside one only delays the walk).
                var end = IndexOfMarker(bytes, limit, position + 1, ">");
                if (end < 0) return null;
                position = end + 1;
            }
            else if (IsAsciiAlpha(next))
            {
                var tag = ReadTagName(bytes, ref position, limit);
                if (tag is null) return null;
                var isMeta = tag.Equals("meta", StringComparison.OrdinalIgnoreCase);
                string? content = null, httpEquiv = null;
                while (true)
                {
                    SkipAttributeSeparators(bytes, ref position, limit);
                    if (position >= limit) return null;
                    if (bytes[position] == (byte)'>') { position++; break; }
                    if (bytes[position] == (byte)'=') position++; // stray equals before a name
                    var name = ReadAttributeName(bytes, ref position, limit);
                    if (name is null) return null;
                    SkipAttributeSeparators(bytes, ref position, limit);
                    var value = "";
                    if (position < limit && bytes[position] == (byte)'=')
                    {
                        position++;
                        SkipAttributeSeparators(bytes, ref position, limit);
                        if (position >= limit) return null;
                        value = ReadAttributeValue(bytes, ref position, limit);
                    }
                    if (!isMeta) continue;
                    if (name.Equals("charset", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ResolveMeta(value) is not null) return value;
                    }
                    else if (name.Equals("content", StringComparison.OrdinalIgnoreCase)) content = value;
                    else if (name.Equals("http-equiv", StringComparison.OrdinalIgnoreCase)) httpEquiv = value;
                }
                if (isMeta && content is not null &&
                    httpEquiv?.Trim().Equals("content-type", StringComparison.OrdinalIgnoreCase) == true &&
                    CharsetInContentType.Match(content) is { Success: true } byContent &&
                    ResolveMeta(byContent.Groups[1].Value) is not null)
                {
                    return byContent.Groups[1].Value;
                }
            }
            else
            {
                // < followed by anything else is text: one byte forward.
                position++;
            }
        }

        return null;
    }

    private static bool IsAsciiAlpha(byte b) => b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static string? ReadTagName(byte[] bytes, ref int position, int limit)
    {
        position++; // past '<'
        var start = position;
        while (position < limit)
        {
            var b = bytes[position];
            if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'/' or (byte)'>')
                return Encoding.ASCII.GetString(bytes, start, position - start);
            position++;
        }
        return null;
    }

    private static string? ReadAttributeName(byte[] bytes, ref int position, int limit)
    {
        var start = position;
        while (position < limit)
        {
            var b = bytes[position];
            if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'/' or (byte)'=' or (byte)'>')
                break;
            position++;
        }
        return position == start ? null : Encoding.ASCII.GetString(bytes, start, position - start);
    }

    private static string ReadAttributeValue(byte[] bytes, ref int position, int limit)
    {
        var quote = bytes[position] is (byte)'"' or (byte)'\'' ? bytes[position] : (byte)0;
        if (quote != 0) position++;
        var start = position;
        while (position < limit)
        {
            var b = bytes[position];
            if (quote != 0 ? b == quote : b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'>')
                break;
            position++;
        }
        var value = Encoding.ASCII.GetString(bytes, start, position - start);
        if (quote != 0 && position < limit) position++; // past the closing quote
        return value;
    }

    private static void SkipAttributeSeparators(byte[] bytes, ref int position, int limit)
    {
        while (position < limit && bytes[position] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'/')
            position++;
    }

    private static bool ContinuesWith(byte[] bytes, int limit, int at, string marker)
    {
        if (at < 0 || at + marker.Length > limit) return false;
        for (var i = 0; i < marker.Length; i++)
            if (bytes[at + i] != (byte)marker[i]) return false;
        return true;
    }

    private static int IndexOfMarker(byte[] bytes, int limit, int from, string marker)
    {
        for (var i = Math.Max(from, 0); i + marker.Length <= limit; i++)
        {
            var matches = true;
            for (var j = 0; j < marker.Length && matches; j++)
                matches = bytes[i + j] == (byte)marker[j];
            if (matches) return i;
        }
        return -1;
    }
}
