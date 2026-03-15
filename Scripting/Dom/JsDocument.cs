using Jint;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>Minimal document proxy exposed to JavaScript.</summary>
public class JsDocument
{
    private readonly Engine     _engine;
    private readonly LayoutNode _root;

    public JsDocument(Engine engine, LayoutNode root)
    {
        _engine = engine;
        _root   = root;
    }

    // ---- selectors ----
    public JsElement? getElementById(string id)
    {
        var node = FindById(_root, id);
        return node is null ? null : new JsElement(_engine, node);
    }

    public JsElement? querySelector(string selector)
    {
        // Support: "#id", "tag", "tag#id", ".class" (class ignored in tree currently)
        selector = selector.Trim();
        LayoutNode? node = null;

        if (selector.StartsWith('#'))
        {
            node = FindById(_root, selector[1..]);
        }
        else if (selector.Contains('#'))
        {
            var parts = selector.Split('#', 2);
            var tag   = parts[0].ToUpperInvariant();
            var id    = parts[1];
            node = FindFirst(_root, n => n.TagName == tag && n.Id == id);
        }
        else
        {
            var tag = selector.ToUpperInvariant();
            node = FindFirst(_root, n => n.TagName == tag);
        }

        return node is null ? null : new JsElement(_engine, node);
    }

    public JsElement[] querySelectorAll(string selector)
    {
        selector = selector.Trim();
        IEnumerable<LayoutNode> matches;

        if (selector.StartsWith('#'))
        {
            var id = selector[1..];
            matches = FindAll(_root, n => n.Id == id);
        }
        else
        {
            var tag = selector.ToUpperInvariant();
            matches = FindAll(_root, n => n.TagName == tag);
        }

        return matches.Select(n => new JsElement(_engine, n)).ToArray();
    }

    // ---- creation ----
    public JsElement createElement(string tagName)
    {
        // Create a detached node — minimal styling
        var style = _root.Style; // borrow parent style; adequate for bootstrap scripts
        var node  = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style);
        return new JsElement(_engine, node);
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
