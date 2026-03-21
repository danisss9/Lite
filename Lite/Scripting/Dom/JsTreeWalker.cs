using Jint;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>DOM TreeWalker implementation.</summary>
public class JsTreeWalker
{
    private readonly Engine _engine;
    private readonly LayoutNode _root;
    private readonly int _whatToShow;
    private LayoutNode _current;

    // NodeFilter constants
    public static int SHOW_ALL { get; } = unchecked((int)0xFFFFFFFF);
    public static int SHOW_ELEMENT { get; } = 0x1;
    public static int SHOW_TEXT { get; } = 0x4;
    public static int SHOW_DOCUMENT { get; } = 0x100;
    public static int SHOW_DOCUMENT_FRAGMENT { get; } = 0x400;

    public JsTreeWalker(Engine engine, LayoutNode root, int whatToShow)
    {
        _engine = engine;
        _root = root;
        _whatToShow = whatToShow;
        _current = root;
    }

    public JsElement currentNode
    {
        get => new(_engine, _current);
        set => _current = value.Node;
    }

    public JsElement? parentNode()
    {
        if (_current == _root || _current.Parent == null) return null;
        _current = _current.Parent;
        return AcceptsNode(_current) ? new JsElement(_engine, _current) : parentNode();
    }

    public JsElement? firstChild()
    {
        if (_current.Children.Count == 0) return null;
        foreach (var child in _current.Children)
        {
            if (AcceptsNode(child)) { _current = child; return new JsElement(_engine, _current); }
        }
        return null;
    }

    public JsElement? lastChild()
    {
        if (_current.Children.Count == 0) return null;
        for (int i = _current.Children.Count - 1; i >= 0; i--)
        {
            if (AcceptsNode(_current.Children[i])) { _current = _current.Children[i]; return new JsElement(_engine, _current); }
        }
        return null;
    }

    public JsElement? nextNode()
    {
        var node = _current;
        // Try children first
        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                if (AcceptsNode(child)) { _current = child; return new JsElement(_engine, _current); }
                // Check child's subtree
                _current = child;
                var result = nextNode();
                if (result != null) return result;
            }
        }
        // Try siblings and ancestors' siblings
        while (node != _root)
        {
            if (node.Parent == null) break;
            var siblings = node.Parent.Children;
            var idx = siblings.IndexOf(node);
            for (int i = idx + 1; i < siblings.Count; i++)
            {
                if (AcceptsNode(siblings[i])) { _current = siblings[i]; return new JsElement(_engine, _current); }
                _current = siblings[i];
                var result = nextNode();
                if (result != null) return result;
            }
            node = node.Parent;
        }
        _current = _root; // reset if exhausted
        return null;
    }

    public JsElement? previousNode()
    {
        if (_current == _root) return null;
        if (_current.Parent == null) return null;
        var siblings = _current.Parent.Children;
        var idx = siblings.IndexOf(_current);
        for (int i = idx - 1; i >= 0; i--)
        {
            var last = GetLastDescendant(siblings[i]);
            if (last != null && AcceptsNode(last)) { _current = last; return new JsElement(_engine, _current); }
            if (AcceptsNode(siblings[i])) { _current = siblings[i]; return new JsElement(_engine, _current); }
        }
        if (_current.Parent != _root && AcceptsNode(_current.Parent))
        {
            _current = _current.Parent;
            return new JsElement(_engine, _current);
        }
        return null;
    }

    private LayoutNode? GetLastDescendant(LayoutNode node)
    {
        if (node.Children.Count == 0) return node;
        return GetLastDescendant(node.Children[^1]);
    }

    private bool AcceptsNode(LayoutNode node)
    {
        if (_whatToShow == unchecked((int)0xFFFFFFFF)) return true;
        var nodeType = node.TagName switch
        {
            "#text" => 0x4,
            "#document" => 0x100,
            "#document-fragment" => 0x400,
            _ => 0x1 // ELEMENT
        };
        return (_whatToShow & nodeType) != 0;
    }
}

/// <summary>DOM NodeIterator implementation.</summary>
public class JsNodeIterator
{
    private readonly Engine _engine;
    private readonly LayoutNode _root;
    private readonly int _whatToShow;
    private readonly List<LayoutNode> _flatList;
    private int _index = -1;

    public JsNodeIterator(Engine engine, LayoutNode root, int whatToShow)
    {
        _engine = engine;
        _root = root;
        _whatToShow = whatToShow;
        _flatList = Flatten(root, whatToShow);
    }

    public JsElement? nextNode()
    {
        _index++;
        return _index < _flatList.Count ? new JsElement(_engine, _flatList[_index]) : null;
    }

    public JsElement? previousNode()
    {
        _index--;
        return _index >= 0 ? new JsElement(_engine, _flatList[_index]) : null;
    }

    private static List<LayoutNode> Flatten(LayoutNode node, int whatToShow)
    {
        var list = new List<LayoutNode>();
        if (AcceptsNode(node, whatToShow)) list.Add(node);
        foreach (var child in node.Children)
            list.AddRange(Flatten(child, whatToShow));
        return list;
    }

    private static bool AcceptsNode(LayoutNode node, int whatToShow)
    {
        if (whatToShow == unchecked((int)0xFFFFFFFF)) return true;
        var nodeType = node.TagName switch
        {
            "#text" => 0x4,
            "#document" => 0x100,
            "#document-fragment" => 0x400,
            _ => 0x1
        };
        return (whatToShow & nodeType) != 0;
    }
}

/// <summary>NodeFilter constants exposed to JS.</summary>
public static class JsNodeFilter
{
    public static int FILTER_ACCEPT { get; } = 1;
    public static int FILTER_REJECT { get; } = 2;
    public static int FILTER_SKIP { get; } = 3;
    public static int SHOW_ALL { get; } = unchecked((int)0xFFFFFFFF);
    public static int SHOW_ELEMENT { get; } = 0x1;
    public static int SHOW_TEXT { get; } = 0x4;
    public static int SHOW_DOCUMENT { get; } = 0x100;
    public static int SHOW_DOCUMENT_FRAGMENT { get; } = 0x400;
}
