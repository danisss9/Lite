using AngleSharp;
using AngleSharp.Dom;
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

    public Task<IDocument> CreateAsync(IBrowsingContext context, CreateDocumentOptions options, CancellationToken cancel)
    {
        var type = options.ContentType.ToString().Split(';')[0].Trim().ToLowerInvariant();
        var isXml = type is "application/xhtml+xml" or "text/xml" or "application/xml";
        if (!isXml)
            return _fallback.CreateAsync(context, options, cancel);
        var parser = context.GetService<IXmlParser>()
            ?? throw new InvalidOperationException("XML document dispatch requires the AngleSharp.Xml IXmlParser service.");
        return parser.ParseDocumentAsync(options.Response.Content, cancel).ContinueWith(
            static task => (IDocument)task.Result, cancel, TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}
