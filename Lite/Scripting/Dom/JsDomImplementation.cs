using AngleSharp.Dom;
using Lite.Models;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>DOM §4.5 DOMImplementation: createDocumentType/createDocument/createHTMLDocument and
/// hasFeature. The created documents are real AngleSharp documents, each exposed through its own
/// JsDocument facade over that document's root element; their nodes are ordinary wrappers, so
/// element creation, selectors and mutations behave as they do on the page document.</summary>
public class JsDomImplementation
{
    private readonly Engine _engine;
    private readonly IDocument? _document;

    internal JsDomImplementation(Engine engine, IDocument? document)
    {
        _engine = engine;
        _document = document;
    }

    private DocumentState? State => JsEngine.For(_engine)?.DocumentState;

    /// <summary>hasFeature is the legacy compatibility alias: every query answers true
    /// (DOM §4.5 keeps the method and always returns true).</summary>
    public bool hasFeature(string feature, string version) => true;

    /// <summary>DOM §4.5 createDocumentType: a DocumentType node owned by this document.</summary>
    public object? createDocumentType(string qualifiedName, string publicId, string systemId)
    {
        if (_document is not { } document) return null;
        return Wrap(document.Implementation.CreateDocumentType(qualifiedName, publicId, systemId));
    }

    /// <summary>DOM §4.5 createDocument: a new document in the given namespace whose document
    /// element is <paramref name="qualifiedName"/> (an empty name creates no root).</summary>
    public object? createDocument(string? namespaceUri, string qualifiedName, object? doctype)
    {
        if (_document is not { } document) return null;
        var created = document.Implementation.CreateDocument(namespaceUri, qualifiedName, doctype as IDocumentType);
        return Facade(created);
    }

    /// <summary>DOM §4.5 createHTMLDocument: an HTML document with a title, head and body.</summary>
    public object? createHTMLDocument(string? title)
    {
        if (_document is not { } document) return null;
        return Facade(document.Implementation.CreateHtmlDocument(title));
    }

    private JsDocument Facade(IDocument created)
    {
        if (State is not { } state)
            throw new InvalidOperationException("The implementation is not bound to a document state.");
        // createDocument may create a document with no document element (an empty qualified
        // name): the facade still needs a rendering root, so a detached one over the created
        // document's style is used — its documentElement reads null.
        var root = created.DocumentElement is { } element
            ? state.ForDomNode(element)
            : new LayoutNode(null, "HTML", string.Empty, JsEngine.For(_engine)!.DocumentFacade.RootNode.Style);
        return new JsDocument(_engine, root, created);
    }

    private object? Wrap(INode node) => State is { } state ? JsElement.For(_engine, state.ForDomNode(node)) : null;
}
