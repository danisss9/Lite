using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using AngleSharp.Io;
using AngleSharp.Xml;
using AngleSharp.Xml.Parser;

namespace Lite;

/// <summary>Routes document opening by MIME type. The stock pipeline parses every response
/// as HTML; documents served as XML — XHTML 1.0 tests serve <c>application/xhtml+xml</c> —
/// must go through the XML parser, whose case sensitivity, CDATA style text, self-closing
/// syntax, and well-formedness handling all differ. AngleSharp.Xml's WithXml() registers
/// XML handlers only for text/xml and application/xml, not XHTML, so XHTML dispatch lands
/// here. Everything else delegates to the stock HTML factory.</summary>
internal sealed class XmlDispatchDocumentFactory : IDocumentFactory
{
    private readonly DefaultDocumentFactory _fallback = new();

    public async Task<IDocument> CreateAsync(IBrowsingContext context, CreateDocumentOptions options, CancellationToken cancel)
    {
        var type = options.ContentType.ToString().Split(';')[0].Trim().ToLowerInvariant();
        var isXml = type is "application/xhtml+xml" or "text/xml" or "application/xml";
        if (!isXml)
            return await _fallback.CreateAsync(context, options, cancel).ConfigureAwait(false);
        var parser = context.GetService<IXmlParser>()
            ?? throw new InvalidOperationException("XML document dispatch requires the AngleSharp.Xml IXmlParser service.");
        using var buffer = new MemoryStream();
        await options.Response.Content.CopyToAsync(buffer, cancel).ConfigureAwait(false);
        var source = XhtmlNamedEntities.Apply(buffer.ToArray());
        try
        {
            using var content = new MemoryStream(source, writable: false);
            return await parser.ParseDocumentAsync(content, cancel).ConfigureAwait(false);
        }
        catch (XmlParseException ex)
        {
            // An XML well-formedness error is fatal to the document (XML 1.0 §1.2); a browser
            // shows the error instead of the content, so the page renders that, not a crash.
            var message = System.Net.WebUtility.HtmlEncode(ex.Message);
            var page = $"<!DOCTYPE html><html><head><title>XML Parsing Error</title></head><body>" +
                $"<h1>XML Parsing Error</h1><p>{message}</p></body></html>";
            return await new HtmlParser(new HtmlParserOptions(), context).ParseDocumentAsync(page, cancel).ConfigureAwait(false);
        }
    }
}

/// <summary>HTML5 REC §9.2 (parsing XHTML documents): when an XML document's DOCTYPE names
/// one of the listed public identifiers, the user agent acts as if the DTD declared the named
/// character references, so <c>&amp;nbsp;</c> and friends resolve instead of being a fatal
/// undefined-entity error. The XML parser has no such table, so the references are rewritten
/// to the equivalent numeric references before parsing — outside comments, CDATA sections,
/// processing instructions and the DOCTYPE, where they are not references at all. Bytes pass
/// through Latin-1 untouched, so the document's own encoding declaration still applies.</summary>
internal static class XhtmlNamedEntities
{
    private static readonly HashSet<string> PublicIdentifiers = new(StringComparer.Ordinal)
    {
        "-//W3C//DTD XHTML 1.0 Transitional//EN",
        "-//W3C//DTD XHTML 1.1//EN",
        "-//W3C//DTD XHTML 1.0 Strict//EN",
        "-//W3C//DTD XHTML 1.0 Frameset//EN",
        "-//W3C//DTD XHTML Basic 1.0//EN",
        "-//W3C//DTD XHTML 1.1 plus MathML 2.0//EN",
        "-//W3C//DTD XHTML 1.1 plus MathML 2.0 plus SVG 1.1//EN",
        "-//W3C//DTD MathML 2.0//EN",
        "-//WAPFORUM//DTD XHTML Mobile 1.0//EN",
    };

    private static readonly Regex Doctype = new("<!DOCTYPE\\s+[^\\s>\\[]+\\s+PUBLIC\\s+(?:\"([^\"]*)\"|'([^']*)')",
        RegexOptions.Compiled);
    private static readonly Regex Reference = new("&([A-Za-z][A-Za-z0-9]*);", RegexOptions.Compiled);

    internal static byte[] Apply(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        if (!UsesHtmlEntityTable(text)) return bytes;
        var output = new StringBuilder(text.Length);
        var changed = false;
        var i = 0;
        while (i < text.Length)
        {
            var skipTo = SkipEnd(text, i);
            if (skipTo > i)
            {
                output.Append(text, i, skipTo - i);
                i = skipTo;
                continue;
            }
            if (text[i] == '&')
            {
                var match = Reference.Match(text, i);
                if (match.Success && match.Index == i && Resolve(match.Groups[1].Value) is { } symbol)
                {
                    for (var c = 0; c < symbol.Length; c++)
                    {
                        var codePoint = char.IsSurrogatePair(symbol, c) ? char.ConvertToUtf32(symbol, c++) : symbol[c];
                        output.Append("&#x").Append(codePoint.ToString("X")).Append(';');
                    }
                    i += match.Length;
                    changed = true;
                    continue;
                }
            }
            output.Append(text[i++]);
        }
        return changed ? Encoding.Latin1.GetBytes(output.ToString()) : bytes;
    }

    private static bool UsesHtmlEntityTable(string text)
    {
        var match = Doctype.Match(text);
        if (!match.Success) return false;
        var root = text.IndexOf('<', 0);
        // The DOCTYPE must precede the root element: skip the XML declaration, comments and PIs.
        while (root >= 0 && root < match.Index)
        {
            var end = SkipEnd(text, root);
            if (end <= root) return false;
            root = text.IndexOf('<', end);
        }
        var id = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return PublicIdentifiers.Contains(id);
    }

    /// <summary>The end of a construct starting at <paramref name="i"/> in which '&amp;' is not a
    /// reference (comment, CDATA section, processing instruction, DOCTYPE), or i itself.</summary>
    private static int SkipEnd(string text, int i)
    {
        if (text[i] != '<') return i;
        static int After(string text, int from, string terminator)
        {
            var end = text.IndexOf(terminator, from, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + terminator.Length;
        }
        if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0) return After(text, i + 4, "-->");
        if (string.CompareOrdinal(text, i, "<![CDATA[", 0, 9) == 0) return After(text, i + 9, "]]>");
        if (string.CompareOrdinal(text, i, "<?", 0, 2) == 0) return After(text, i + 2, "?>");
        if (string.CompareOrdinal(text, i, "<!DOCTYPE", 0, 9) == 0)
        {
            // A DOCTYPE may carry an internal subset in brackets; quoted ids may hold '>'.
            var depth = 0;
            char quote = '\0';
            for (var j = i + 9; j < text.Length; j++)
            {
                var c = text[j];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '"' or '\'') quote = c;
                else if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == '>' && depth <= 0) return j + 1;
            }
            return text.Length;
        }
        return i;
    }

    private static string? Resolve(string name) => name is "amp" or "lt" or "gt" or "quot" or "apos"
        ? null
        : HtmlEntityProvider.Resolver.GetSymbol(name + ";");
}
