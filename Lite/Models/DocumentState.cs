using AngleSharp.Dom;

namespace Lite.Models;

/// <summary>Document-owned inputs for scripting and runtime style resolution.</summary>
internal sealed class DocumentState(IDocument? document, string address, string baseUrl,
    IReadOnlyList<Parser.CssRule> styleRules)
{
    internal IDocument? Document { get; } = document;
    internal string Address { get; } = address;
    internal string Url { get; set; } = address;
    internal string BaseUrl { get; } = baseUrl;
    internal IReadOnlyList<Parser.CssRule> StyleRules { get; } = styleRules;

    internal void Bind(LayoutNode root)
    {
        var pending = new Stack<LayoutNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            node.DocumentState = this;
            foreach (var child in node.Children) pending.Push(child);
            if (node.TemplateContent is { } content) pending.Push(content);
            // A ChildPage owns a separate document and keeps its own state.
        }
    }
}
