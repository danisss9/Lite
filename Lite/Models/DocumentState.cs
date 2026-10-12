using AngleSharp.Css.Dom;
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
    /// <summary>The rule set captured at parse time; refreshed when the rule set itself changes
    /// (a style sheet toggled disabled, a host control), so the dynamic resolver follows.</summary>
    internal IReadOnlyList<Parser.CssRule> StyleRules { get; private set; } = styleRules;

    internal void RefreshStyleRules(IReadOnlyList<Parser.CssRule> rules) => StyleRules = rules;

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

    /// <summary>The rendering object already bound to a DOM node, if one exists — without
    /// creating a lazy projection. This is the reuse lookup for mutation re-projection:
    /// first-bind-wins means a parsed element keeps its LayoutNode identity across mutations,
    /// which is what lets re-projection survive the layout normalizers' anonymous wrappers.</summary>
    internal LayoutNode? FindBound(INode node)
        => _layoutByDom.TryGetValue(node, out var bound) ? bound : null;

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
            {
                foreach (var attribute in elementNode.Attributes)
                    projection.Attributes[attribute.Name] = attribute.Value;
                // The element gets its OWN computed cascade, not the borrowed render-root style:
                // the root's computed 'display' (block) used to pass off as the projection's own,
                // so a JS-created <span> laid out block-level. This is the same computation
                // Traverse performs for parsed elements (UA sheet block list, author rules) —
                // through the guarded path: AngleSharp's raw computation throws on declarations
                // it cannot convert (unitless font-shorthand line-height).
                projection.Style = Parser.ComputeCurrentStyleFor(elementNode);
            }
            else if (key is IText)
            {
                // Text projections get the same explicit inline the parser gives #text children
                // (they share their originating element's style object, which may not be inline).
                projection.StyleOverrides["display"] = "inline";
            }
            projection.DomNode = key;
            // Keep disconnected trees disconnected. A non-rendered ancestor is represented
            // lazily but is never inserted into the rendering children list.
            if (key.Parent is { } parent && parent is not IDocument)
                projection.Parent = ForDomNode(parent);
            return projection;
        });
    }
}
