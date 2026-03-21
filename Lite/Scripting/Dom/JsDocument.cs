using Jint;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>Full document proxy exposed to JavaScript.</summary>
public class JsDocument
{
    private readonly Engine     _engine;
    private readonly LayoutNode _root;

    public JsDocument(Engine engine, LayoutNode root)
    {
        _engine = engine;
        _root   = root;
    }

    // ---- identity ----
    public string nodeName => "#document";
    public int nodeType => 9;
    public JsElement? documentElement => _root.Children.Count > 0 ? new JsElement(_engine, _root) : null;

    public JsElement? body =>
        FindFirst(_root, n => n.TagName == "BODY") is { } b ? new JsElement(_engine, b) : null;

    public JsElement? head =>
        FindFirst(_root, n => n.TagName == "HEAD") is { } h ? new JsElement(_engine, h) : null;

    // ---- selectors ----
    public JsElement? getElementById(string id)
    {
        var node = FindById(_root, id);
        return node is null ? null : new JsElement(_engine, node);
    }

    public JsElement? querySelector(string selector) =>
        SelectorEngine.QuerySelector(_root, selector, _engine);

    public JsElement[] querySelectorAll(string selector) =>
        SelectorEngine.QuerySelectorAll(_root, selector, _engine);

    public JsElement[] getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return FindAll(_root, n => tag == "*" || n.TagName == tag)
            .Select(n => new JsElement(_engine, n)).ToArray();
    }

    public JsElement[] getElementsByClassName(string classNames)
    {
        var classes = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return FindAll(_root, n =>
        {
            var nodeClasses = n.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => new JsElement(_engine, n)).ToArray();
    }

    // ---- creation ----
    public JsElement createElement(string tagName)
    {
        var style = _root.Style;
        var node  = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style);
        return new JsElement(_engine, node);
    }

    public JsElement createElementNS(string ns, string tagName)
    {
        var style = _root.Style;
        var node = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style);
        node.Attributes["xmlns"] = ns;
        return new JsElement(_engine, node);
    }

    public JsElement createTextNode(string text)
    {
        var style = _root.Style;
        var node = new LayoutNode(null, "#text", text, style);
        return new JsElement(_engine, node);
    }

    public JsElement createDocumentFragment()
    {
        var style = _root.Style;
        var node = new LayoutNode(null, "#document-fragment", string.Empty, style);
        return new JsElement(_engine, node);
    }

    public JsEvent createEvent(string type)
    {
        return new JsEvent();
    }

    // ---- DOM Traversal Level 2 (Phase 9) ----
    public JsTreeWalker createTreeWalker(JsElement root, int whatToShow = -1, object? filter = null)
    {
        return new JsTreeWalker(_engine, root.Node, whatToShow);
    }

    public JsNodeIterator createNodeIterator(JsElement root, int whatToShow = -1, object? filter = null)
    {
        return new JsNodeIterator(_engine, root.Node, whatToShow);
    }

    // ---- tree helpers ----
    private static LayoutNode? FindById(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var result = FindById(child, id);
            if (result is not null) return result;
        }
        return null;
    }

    private static LayoutNode? FindFirst(LayoutNode node, Func<LayoutNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (var child in node.Children)
        {
            var result = FindFirst(child, predicate);
            if (result is not null) return result;
        }
        return null;
    }

    private static IEnumerable<LayoutNode> FindAll(LayoutNode node, Func<LayoutNode, bool> predicate)
    {
        if (predicate(node)) yield return node;
        foreach (var child in node.Children)
        foreach (var match in FindAll(child, predicate))
            yield return match;
    }
}
