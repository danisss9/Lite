using AngleSharp.Dom;
using Lite.Network;

namespace Lite.Models;

/// <summary>Document-owned inputs for scripting and runtime style resolution.</summary>
internal sealed class DocumentState(IDocument? document, string address, string baseUrl,
    IReadOnlyList<Parser.CssRule> styleRules)
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<INode, LayoutNode> _layoutByDom = new();
    private LayoutNode? _renderRoot;
    internal IDocument? Document { get; } = document;
    internal Scripting.JsEngine? Engine { get; set; }
    internal LayoutNode? ActiveElement { get; set; }
    internal Parser.ParseState? ParserContext { get; init; }
    internal BrowserSession? Session { get; init; }
    internal string Address { get; } = address;
    internal string Url { get; set; } = address;
    internal string BaseUrl { get; } = baseUrl;
    internal IReadOnlyList<Parser.CssRule> StyleRules { get; } = styleRules;

    internal void Bind(LayoutNode root)
    {
        _renderRoot ??= root;
        var pending = new Stack<LayoutNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            node.DocumentState = this;
            if (node.DomNode is { } domNode)
                _layoutByDom.GetValue(domNode, _ => node);
            foreach (var child in node.Children) pending.Push(child);
            if (node.TemplateContent is { } content) pending.Push(content);
            // A ChildPage owns a separate document and keeps its own state.
        }
    }

    // A DOM node can have no rendering object (head, comments, detached nodes, etc.).
    // Its lightweight projection exists only for legacy rendering/interaction callers;
    // the AngleSharp node retains identity, content and tree ownership.
    internal LayoutNode ForDomNode(INode node)
    {
        return _layoutByDom.GetValue(node, key =>
        {
            var tag = key switch
            {
                IElement element => element.TagName.ToUpperInvariant(),
                IText => "#text",
                IComment => "#comment",
                IDocumentType => "#document-type",
                IDocumentFragment => "#document-fragment",
                IProcessingInstruction => "#pi",
                _ => key.NodeName
            };
            var projection = new LayoutNode(null, tag, key.TextContent ?? string.Empty,
                _renderRoot?.Style ?? throw new InvalidOperationException("Document rendering root is not bound."))
            {
                DocumentState = this
            };
            // Copy the attributes BEFORE binding the DOM node: DomAttributeDictionary mirrors
            // writes back to the element, and enumerating element.Attributes while its own
            // SetAttribute runs would throw "Collection was modified".
            if (key is IElement elementNode)
                foreach (var attribute in elementNode.Attributes)
                    projection.Attributes[attribute.Name] = attribute.Value;
            projection.DomNode = key;
            // Keep disconnected trees disconnected. A non-rendered ancestor is represented
            // lazily but is never inserted into the rendering children list.
            if (key.Parent is { } parent && parent is not IDocument)
                projection.Parent = ForDomNode(parent);
            return projection;
        });
    }
}
