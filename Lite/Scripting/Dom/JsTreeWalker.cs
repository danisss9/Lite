using Lite.Scripting.Runtime;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>DOM TreeWalker per the traversal algorithm of the DOM4 draft referenced by
/// HTML 5.0: FILTER_REJECT cuts the subtree in document-order scans but behaves like
/// FILTER_SKIP for sibling navigation, FILTER_SKIP is transparent, and an unset
/// whatToShow bit always behaves as FILTER_SKIP.</summary>
public class JsTreeWalker
{
    private const uint FilterAccept = 1, FilterReject = 2, FilterSkip = 3;

    private readonly Engine _engine;
    private readonly LayoutNode _root;
    private readonly uint _whatToShow;
    private readonly JsValue? _filter;
    private LayoutNode _current;

    public JsTreeWalker(Engine engine, LayoutNode root, uint whatToShow, JsValue? filter)
    {
        _engine = engine;
        _root = root;
        _whatToShow = whatToShow;
        _filter = filter;
        _current = root;
    }

    public JsElement root => JsElement.For(_engine, _root);
    public double whatToShow => _whatToShow;
    public JsValue? filter => _filter;
    public bool expandEntityReferences => true;

    public JsValue currentNode
    {
        get => JsValue.FromObject(_engine, JsElement.For(_engine, _current));
        set
        {
            // WebIDL Node-typed attribute: anything that is not a Node raises TypeError.
            var element = value.ToObject() as JsElement;
            if (element is null)
                throw JsErrors.Native(_engine, "TypeError",
                    "Failed to set the 'currentNode' property on 'TreeWalker': The provided value is not of type 'Node'.");
            _current = element.Node;
        }
    }

    public JsElement? parentNode()
    {
        var node = _current;
        while (true)
        {
            if (ReferenceEquals(node, _root) || node.Parent is null) return null;
            node = node.Parent;
            if (FilteredNode(node) == FilterAccept)
            {
                _current = node;
                return JsElement.For(_engine, node);
            }
        }
    }

    public JsElement? firstChild() => TraverseChildren(forward: true);
    public JsElement? lastChild() => TraverseChildren(forward: false);

    public JsElement? nextSibling() => TraverseSiblings(forward: true);
    public JsElement? previousSibling() => TraverseSiblings(forward: false);

    public JsElement? nextNode()
    {
        // Children of the current node first, then siblings of each ancestor. The upward
        // walk stops at the root: the root's own siblings are outside the walk.
        var start = ScanChildrenOf(_current, forward: true);
        if (start is not null) { _current = start; return JsElement.For(_engine, start); }

        var node = _current;
        while (!ReferenceEquals(node, _root) && node.Parent is not null)
        {
            var sibling = SiblingOf(node, forward: true, within: node.Parent);
            var parent = node.Parent;
            while (sibling is not null)
            {
                var found = ScanSubtree(sibling, forward: true, boundary: sibling);
                if (found is not null) { _current = found; return JsElement.For(_engine, found); }
                sibling = SiblingOf(sibling, forward: true, within: parent);
            }
            node = parent;
            if (ReferenceEquals(node, _root)) break;
            if (node.Parent is null) break;
        }
        _current = _root;
        return null;
    }

    public JsElement? previousNode()
    {
        var node = _current;
        while (true)
        {
            if (ReferenceEquals(node, _root) || node.Parent is null) { _current = _root; return null; }
            var sibling = SiblingOf(node, forward: false, within: node.Parent);
            var parent = node.Parent;
            while (sibling is not null)
            {
                var found = ScanSubtree(sibling, forward: false, boundary: sibling);
                if (found is not null) { _current = found; return JsElement.For(_engine, found); }
                sibling = SiblingOf(sibling, forward: false, within: parent);
            }
            node = parent;
            if (ReferenceEquals(node, _root)) { _current = _root; return null; }
            if (FilteredNode(node) == FilterAccept)
            {
                _current = node;
                return JsElement.For(_engine, node);
            }
        }
    }

    private JsElement? TraverseChildren(bool forward)
    {
        var children = _current.Children;
        int begin = forward ? 0 : children.Count - 1, end = forward ? children.Count : -1;
        for (int i = begin; i != end; i += forward ? 1 : -1)
        {
            var child = children[i];
            var filtered = FilteredNode(child);
            if (filtered == FilterAccept) { _current = child; return JsElement.For(_engine, child); }
            // SKIP is transparent (descend into the child); REJECT for first/last child means
            // the subtree is off limits and no sibling continuation happens (spec: null).
            if (filtered == FilterSkip)
            {
                var found = ScanSubtree(child, forward, boundary: _current);
                if (found is not null) { _current = found; return JsElement.For(_engine, found); }
            }
        }
        return null;
    }

    private JsElement? TraverseSiblings(bool forward)
    {
        if (ReferenceEquals(_current, _root) || _current.Parent is null) return null;
        var sibling = SiblingOf(_current, forward, within: _current.Parent);
        while (sibling is not null)
        {
            var found = ScanSubtree(sibling, forward, boundary: sibling);
            if (found is not null) { _current = found; return JsElement.For(_engine, found); }
            sibling = SiblingOf(sibling, forward, within: _current.Parent);
        }
        return null;
    }

    /// <summary>Scans the subtree rooted at <paramref name="start"/> in document order (or
    /// reverse), starting at <paramref name="start"/> itself, without leaving the subtree
    /// rooted at <paramref name="boundary"/>.</summary>
    private LayoutNode? ScanSubtree(LayoutNode start, bool forward, LayoutNode? boundary)
    {
        var node = start;
        while (node is not null)
        {
            var filtered = FilteredNode(node);
            if (filtered == FilterAccept) return node;
            // SKIP descends transparently; REJECT cuts the subtree and continues with the
            // next sibling at this level. Both stay inside the boundary subtree.
            node = filtered == FilterSkip
                ? AdvanceWithin(node, forward, boundary, descendFirst: true)
                : AdvanceWithin(node, forward, boundary, descendFirst: false);
        }
        return null;
    }

    private LayoutNode? ScanChildrenOf(LayoutNode node, bool forward)
    {
        if (node.Children.Count == 0) return null;
        var child = forward ? node.Children[0] : node.Children[^1];
        return ScanSubtree(child, forward, boundary: node);
    }

    /// <summary>Next node in (reverse) document order from <paramref name="node"/>, never
    /// leaving the subtree rooted at <paramref name="boundary"/>. With
    /// <paramref name="descendFirst"/> false the node's own subtree is skipped entirely.</summary>
    private static LayoutNode? AdvanceWithin(LayoutNode node, bool forward, LayoutNode? boundary, bool descendFirst)
    {
        if (descendFirst && node.Children.Count > 0)
            return forward ? node.Children[0] : node.Children[^1];
        var current = node;
        while (current.Parent is not null)
        {
            var parent = current.Parent;
            var index = parent.Children.IndexOf(current);
            var siblingIndex = forward ? index + 1 : index - 1;
            if (siblingIndex >= 0 && siblingIndex < parent.Children.Count)
                return parent.Children[siblingIndex];
            if (ReferenceEquals(parent, boundary)) return null;
            current = parent;
        }
        return null;
    }

    private static LayoutNode? SiblingOf(LayoutNode node, bool forward, LayoutNode within)
    {
        var index = within.Children.IndexOf(node);
        if (index < 0) return null;
        var siblingIndex = forward ? index + 1 : index - 1;
        return siblingIndex >= 0 && siblingIndex < within.Children.Count ? within.Children[siblingIndex] : null;
    }

    /// <summary>The filtered-node verdict: an unset whatToShow bit is FILTER_SKIP, and filter
    /// results map to REJECT/SKIP with anything else (including no filter) ACCEPT.</summary>
    private uint FilteredNode(LayoutNode node)
    {
        if ((NodeBits(node) & _whatToShow) == 0) return FilterSkip;
        if (_filter is null) return FilterAccept;
        var verdict = InvokeFilter(node);
        return verdict == 2d ? FilterReject : verdict == 3d ? FilterSkip : FilterAccept;
    }

    private double InvokeFilter(LayoutNode node)
    {
        JsValue callback;
        JsValue? thisValue = null;
        if (_filter.IsCallable()) callback = _filter;
        else
        {
            callback = _filter.Get("acceptNode");
            if (!callback.IsCallable())
                throw JsErrors.Native(_engine, "TypeError",
                    "Failed to execute 'acceptNode' on 'NodeFilter': The provided value is not of type 'NodeFilter'.");
            thisValue = _filter;
        }
        var argument = JsValue.FromObject(_engine, new JsElement(_engine, node));
        var receiver = thisValue ?? JsValue.Undefined;
        var result = _engine.Call(callback, receiver, argument);
        return result.IsNumber() ? result.AsNumber() : 0;
    }

    internal static uint NodeBits(LayoutNode node) => node.TagName switch
    {
        "#text" => 0x4u,
        "#cdata-section" => 0x8u,
        "#pi" => 0x40u,
        "#comment" => 0x80u,
        "#document" => 0x100u,
        "#document-type" => 0x200u,
        "#document-fragment" => 0x400u,
        _ => 0x1u,
    };
}

/// <summary>DOM NodeIterator per the traversal algorithm: a live filtered view of the nodes
/// between the root and the document end, positioned by a reference node plus a before-flag.</summary>
public class JsNodeIterator
{
    private const uint FilterAccept = 1, FilterReject = 2, FilterSkip = 3;

    private readonly Engine _engine;
    private readonly LayoutNode _root;
    private readonly uint _whatToShow;
    private readonly JsValue? _filter;
    private LayoutNode _reference;
    private bool _beforeReference = true;

    public JsNodeIterator(Engine engine, LayoutNode root, uint whatToShow, JsValue? filter)
    {
        _engine = engine;
        _root = root;
        _whatToShow = whatToShow;
        _filter = filter;
        _reference = root;
    }

    public JsElement root => JsElement.For(_engine, _root);
    public double whatToShow => _whatToShow;
    public JsValue? filter => _filter;
    public bool expandEntityReferences => true;
    public JsElement referenceNode => JsElement.For(_engine, _reference);

    public JsElement? nextNode()
    {
        while (true)
        {
            LayoutNode? candidate;
            if (_beforeReference)
            {
                candidate = _reference;
                _beforeReference = false;
            }
            else
            {
                candidate = Successor(_reference);
                if (candidate is null) return null;
                _reference = candidate;
            }
            var filtered = FilteredNode(candidate);
            if (filtered == FilterAccept) return JsElement.For(_engine, candidate);
        }
    }

    public JsElement? previousNode()
    {
        while (true)
        {
            LayoutNode? candidate;
            if (_beforeReference)
            {
                candidate = Predecessor(_reference);
                if (candidate is null) return null;
                _reference = candidate;
                _beforeReference = false;
            }
            else
            {
                candidate = _reference;
                _beforeReference = true;
            }
            var filtered = FilteredNode(candidate);
            if (filtered == FilterAccept) return JsElement.For(_engine, candidate);
        }
    }

    private LayoutNode? Successor(LayoutNode node)
    {
        if (ReferenceEquals(node, _root)) return FirstWithin(_root);
        // Document-order successor within the root's subtree; detached nodes have none.
        if (!IsWithin(node, _root)) return null;
        var current = node;
        while (current.Parent is not null)
        {
            var parent = current.Parent;
            var index = parent.Children.IndexOf(current);
            if (index + 1 < parent.Children.Count) return parent.Children[index + 1];
            current = parent;
            if (ReferenceEquals(current, _root)) return null;
        }
        return null;
    }

    private LayoutNode? Predecessor(LayoutNode node)
    {
        if (ReferenceEquals(node, _root)) return null;
        if (!IsWithin(node, _root)) return null;
        var current = node;
        while (current.Parent is not null)
        {
            var parent = current.Parent;
            var index = parent.Children.IndexOf(current);
            if (index > 0)
            {
                var previous = parent.Children[index - 1];
                var last = previous;
                while (last.Children.Count > 0) last = last.Children[^1];
                return last;
            }
            current = parent;
            if (ReferenceEquals(current, _root)) return null;
        }
        return null;
    }

    private static LayoutNode? FirstWithin(LayoutNode root)
    {
        var node = root;
        while (node.Children.Count > 0) node = node.Children[0];
        return ReferenceEquals(node, root) ? null : node;
    }

    private bool IsWithin(LayoutNode node, LayoutNode root)
    {
        var current = node;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (ReferenceEquals(current, root)) return true;
        }
        return false;
    }

    private uint FilteredNode(LayoutNode node)
    {
        if ((JsTreeWalker.NodeBits(node) & _whatToShow) == 0) return FilterSkip;
        if (_filter is null) return FilterAccept;
        var verdict = InvokeFilter(node);
        return verdict == 2d ? FilterReject : verdict == 3d ? FilterSkip : FilterAccept;
    }

    private double InvokeFilter(LayoutNode node)
    {
        JsValue callback;
        JsValue? thisValue = null;
        if (_filter.IsCallable()) callback = _filter;
        else
        {
            callback = _filter.Get("acceptNode");
            if (!callback.IsCallable())
                throw JsErrors.Native(_engine, "TypeError",
                    "Failed to execute 'acceptNode' on 'NodeFilter': The provided value is not of type 'NodeFilter'.");
            thisValue = _filter;
        }
        var argument = JsValue.FromObject(_engine, new JsElement(_engine, node));
        var receiver = thisValue ?? JsValue.Undefined;
        var result = _engine.Call(callback, receiver, argument);
        return result.IsNumber() ? result.AsNumber() : 0;
    }
}

/// <summary>NodeFilter constants exposed to JS (the global comes from the interface bootstrap;
/// these statics remain for C# callers).</summary>
public static class JsNodeFilter
{
    public static int FILTER_ACCEPT { get; } = 1;
    public static int FILTER_REJECT { get; } = 2;
    public static int FILTER_SKIP { get; } = 3;
    public static int SHOW_ALL { get; } = unchecked((int)0xFFFFFFFF);
    public static int SHOW_ELEMENT { get; } = 0x1;
    public static int SHOW_TEXT { get; } = 0x4;
    public static int SHOW_COMMENT { get; } = 0x80;
    public static int SHOW_DOCUMENT { get; } = 0x100;
    public static int SHOW_DOCUMENT_TYPE { get; } = 0x200;
    public static int SHOW_DOCUMENT_FRAGMENT { get; } = 0x400;
}
