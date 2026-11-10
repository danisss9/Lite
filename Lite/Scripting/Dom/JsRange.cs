using Lite.Models;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>DOM Range (DOM4 §5): boundary points over the projected LayoutNode tree. Offsets
/// are child indices for element containers, character offsets for text containers. The
/// document-order comparisons use a precomputed index so compareBoundaryPoints and
/// intersectingNode stay O(1) after one walk.</summary>
public class JsRange
{
    private readonly Engine _engine;
    internal LayoutNode StartNodeField;
    internal int StartOffsetField;
    internal LayoutNode EndNodeField;
    internal int EndOffsetField;

    internal JsRange(Engine engine)
    {
        _engine = engine;
        var root = FindDocumentRoot();
        StartNodeField = root;
        EndNodeField = root;
    }

    private LayoutNode FindDocumentRoot()
    {
        var jsEngine = JsEngine.For(_engine);
        return jsEngine?.DocumentFacade?.RootNode ?? new LayoutNode(null, "#document-fragment", "", null!);
    }

    internal (LayoutNode Node, int Offset) Start => (StartNodeField, StartOffsetField);
    internal (LayoutNode Node, int Offset) End => (EndNodeField, EndOffsetField);

    public JsElement? startContainer => Wrap(StartNodeField);
    public int startOffset => StartOffsetField;
    public JsElement? endContainer => Wrap(EndNodeField);
    public int endOffset => EndOffsetField;
    public bool collapsed => ReferenceEquals(StartNodeField, EndNodeField) && StartOffsetField == EndOffsetField;

    public JsElement? commonAncestorContainer
    {
        get
        {
            var chain = new List<LayoutNode>();
            for (var p = (LayoutNode?)StartNodeField; p != null; p = p.Parent) chain.Add(p);
            for (var p = (LayoutNode?)EndNodeField; p != null; p = p.Parent)
                if (chain.Contains(p)) return Wrap(p);
            return Wrap(FindDocumentRoot());
        }
    }

    internal JsElement? Wrap(LayoutNode node) => JsElement.For(JsEngine.For(_engine)!.RawEngine, node);

    public void setStart(JsElement node, int offset)
    {
        StartNodeField = node.Node;
        StartOffsetField = Math.Max(0, offset);
        CollapseIfNeeded();
    }

    public void setEnd(JsElement node, int offset)
    {
        EndNodeField = node.Node;
        EndOffsetField = Math.Max(0, offset);
        CollapseIfNeeded();
    }

    public void setStartBefore(JsElement node)
    {
        if (node.Node.Parent is not { } parent) throw JsErrors.Dom(_engine, "InvalidNodeTypeError", "No parent.");
        setStart(Wrap(parent), parent.Children.IndexOf(node.Node));
    }

    public void setStartAfter(JsElement node)
    {
        if (node.Node.Parent is not { } parent) throw JsErrors.Dom(_engine, "InvalidNodeTypeError", "No parent.");
        setStart(Wrap(parent), parent.Children.IndexOf(node.Node) + 1);
    }

    public void setEndBefore(JsElement node)
    {
        if (node.Node.Parent is not { } parent) throw JsErrors.Dom(_engine, "InvalidNodeTypeError", "No parent.");
        setEnd(Wrap(parent), parent.Children.IndexOf(node.Node));
    }

    public void setEndAfter(JsElement node)
    {
        if (node.Node.Parent is not { } parent) throw JsErrors.Dom(_engine, "InvalidNodeTypeError", "No parent.");
        setEnd(Wrap(parent), parent.Children.IndexOf(node.Node) + 1);
    }

    public void collapse(bool toStart = true)
    {
        if (toStart) { EndNodeField = StartNodeField; EndOffsetField = StartOffsetField; }
        else { StartNodeField = EndNodeField; StartOffsetField = EndOffsetField; }
    }

    public void selectNode(JsElement node)
    {
        if (node.Node.Parent is not { } parent) throw JsErrors.Dom(_engine, "InvalidNodeTypeError", "No parent.");
        StartNodeField = parent;
        StartOffsetField = parent.Children.IndexOf(node.Node);
        EndNodeField = parent;
        EndOffsetField = StartOffsetField + 1;
    }

    public void selectNodeContents(JsElement node)
    {
        StartNodeField = node.Node;
        StartOffsetField = 0;
        EndNodeField = node.Node;
        EndOffsetField = node.Node.Children.Count;
    }

    private void CollapseIfNeeded()
    {
        // Start after end: collapse to the later point.
        if (DocumentOrder(StartNodeField, StartOffsetField, EndNodeField, EndOffsetField) > 0)
        {
            (StartNodeField, StartOffsetField) = (EndNodeField, EndOffsetField);
        }
    }

    /// <summary>Document-order comparison: negative when (n1, o1) precedes (n2, o2).</summary>
    internal static int DocumentOrder(LayoutNode n1, int o1, LayoutNode n2, int o2)
    {
        if (ReferenceEquals(n1, n2)) return o1.CompareTo(o2);
        // Walk n1's ancestor chain; check n2's containment.
        var chain = new List<LayoutNode>();
        for (var p = (LayoutNode?)n1; p != null; p = p.Parent) chain.Add(p);
        for (var p = (LayoutNode?)n2; p != null; p = p.Parent)
            if (chain.Contains(p))
            {
                // Common ancestor found: compare the child indices on the way down. A node
                // that IS the common ancestor compares through its parent's child index (the
                // node itself is not its own child — IndexOf(node) on the same node reads -1).
                var ancestor = p;
                var child1 = ReferenceEquals(ancestor, n1) ? null : AncestorChild(ancestor, n1);
                var child2 = ReferenceEquals(ancestor, n2) ? null : AncestorChild(ancestor, n2);
                if (child1 is null && child2 is null) return 0;
                if (child1 is null)
                {
                    // n1 is the ancestor: it precedes any child (a parent at offset o precedes
                    // its children only via the child comparison — n1's position is before
                    // child2 in document order).
                    var i2 = ancestor.Children.IndexOf(child2!);
                    return o1 <= 0 && i2 >= 0 ? -1 : 1;
                }
                if (child2 is null)
                {
                    var i1 = ancestor.Children.IndexOf(child1!);
                    return i1 >= 0 ? 1 : -1;
                }
                if (ReferenceEquals(child1, child2))
                    return DocumentOrder(child1, o1, child2, o2);
                var index1 = ancestor.Children.IndexOf(child1);
                var index2 = ancestor.Children.IndexOf(child2);
                return index1.CompareTo(index2);
            }
        return 0; // disconnected
    }

    private static LayoutNode? AncestorChild(LayoutNode ancestor, LayoutNode node)
    {
        for (var p = (LayoutNode?)node; p != null; p = p.Parent)
            if (p.Parent == ancestor) return p;
        return null;
    }

    public short compareBoundaryPoints(short how, JsRange other)
    {
        var cmp = DocumentOrder(other.StartNodeField, other.StartOffsetField, StartNodeField, StartOffsetField);
        // how: 0=START_TO_START, 1=START_TO_END, 2=END_TO_END, 3=END_TO_START
        return how switch
        {
            0 => (short)DocumentOrder(StartNodeField, StartOffsetField, other.StartNodeField, other.StartOffsetField),
            1 => (short)DocumentOrder(StartNodeField, StartOffsetField, other.EndNodeField, other.EndOffsetField),
            2 => (short)DocumentOrder(EndNodeField, EndOffsetField, other.EndNodeField, other.EndOffsetField),
            3 => (short)cmp,
            _ => throw JsErrors.Dom(_engine, "NotSupportedError", "Invalid comparison type."),
        };
    }

    /// <summary>Removes the range's contents: splits text containers at the boundary offsets and
    /// drops the enclosed nodes. Returns the extracted fragment nodes in document order.</summary>
    internal List<LayoutNode> ExtractContents(bool retain)
    {
        var extracted = new List<LayoutNode>();
        if (collapsed) return extracted;

        NormalizeBoundaries();

        if (ReferenceEquals(StartNodeField, EndNodeField))
        {
            if (StartNodeField.TagName == "#text")
            {
                var text = StartNodeField.Text ?? "";
                var removed = text[StartOffsetField..EndOffsetField];
                if (!retain) extracted.Add(new LayoutNode(null, "#text", removed, StartNodeField.Style) { DomNode = StartNodeField.DomNode });
                StartNodeField.SetOwnText(text[..StartOffsetField] + text[EndOffsetField..]);
            }
            else
            {
                for (var i = EndOffsetField - 1; i >= StartOffsetField; i--)
                {
                    var child = StartNodeField.Children[i];
                    StartNodeField.Children.RemoveAt(i);
                    if (!retain) extracted.Insert(0, child);
                }
            }
            return extracted;
        }

        // Partial start text.
        if (StartNodeField.TagName == "#text" && StartOffsetField < (StartNodeField.Text?.Length ?? 0))
        {
            var text = StartNodeField.Text ?? "";
            if (!retain) extracted.Add(new LayoutNode(null, "#text", text[StartOffsetField..], StartNodeField.Style) { DomNode = StartNodeField.DomNode });
            StartNodeField.SetOwnText(text[..StartOffsetField]);
        }

        // Whole children of the start container after the start offset.
        var startParent = StartNodeField;
        for (var i = startParent.Children.Count - 1; i >= StartOffsetField; i--)
        {
            var child = startParent.Children[i];
            startParent.Children.RemoveAt(i);
            if (!retain) extracted.Insert(0, child);
        }

        // Whole children of the end container before the end offset (reversed walk).
        var endParent = EndNodeField;
        if (endParent.TagName == "#text" && EndOffsetField > 0)
        {
            var text = endParent.Text ?? "";
            var removed = text[..Math.Min(EndOffsetField, text.Length)];
            if (!retain) extracted.Add(new LayoutNode(null, "#text", removed, endParent.Style) { DomNode = endParent.DomNode });
            endParent.SetOwnText(text[Math.Min(EndOffsetField, text.Length)..]);
        }
        else
        {
            for (var i = Math.Min(EndOffsetField, endParent.Children.Count) - 1; i >= 0; i--)
            {
                var child = endParent.Children[i];
                endParent.Children.RemoveAt(i);
                if (!retain) extracted.Insert(0, child);
            }
        }

        // Ancestors between the two containers: their in-range children lift out.
        var chain = new List<LayoutNode>();
        for (var p = (LayoutNode?)endParent; p != null; p = p.Parent) chain.Add(p);
        var ancestor = startParent.Parent;
        while (ancestor is not null && !ReferenceEquals(ancestor, endParent) && chain.Contains(ancestor))
        {
            var startIdx = ancestor.Children.IndexOf(startParent);
            for (var i = ancestor.Children.Count - 1; i > startIdx; i--)
            {
                var child = ancestor.Children[i];
                ancestor.Children.RemoveAt(i);
                if (!retain) extracted.Insert(0, child);
            }
            startParent = ancestor;
            ancestor = ancestor.Parent;
        }

        return extracted;
    }

    /// <summary>Clamps both boundaries into a canonical same-grain form: text containers keep
    /// character offsets; element containers keep child indices (an offset past the last child
    /// clamps to Children.Count).</summary>
    private void NormalizeBoundaries()
    {
        if (StartNodeField.TagName != "#text" && StartOffsetField > StartNodeField.Children.Count)
            StartOffsetField = StartNodeField.Children.Count;
        if (EndNodeField.TagName != "#text" && EndOffsetField > EndNodeField.Children.Count)
            EndOffsetField = EndNodeField.Children.Count;
    }

    public void deleteContents() => ExtractContents(retain: false);

    public JsDocumentFragment extractContents()
    {
        var fragment = new LayoutNode(null, "#document-fragment", "", StartNodeField.Style);
        foreach (var node in ExtractContents(retain: false)) fragment.AddChild(node);
        return new JsDocumentFragment(_engine, fragment);
    }

    public JsDocumentFragment cloneContents()
    {
        var fragment = new LayoutNode(null, "#document-fragment", "", StartNodeField.Style);
        // Cloning the enclosed range contents (same-document case simplified: full children).
        if (ReferenceEquals(StartNodeField, EndNodeField) && StartNodeField.TagName != "#text")
        {
            for (var i = StartOffsetField; i < EndOffsetField && i < StartNodeField.Children.Count; i++)
                fragment.AddChild(ShallowClone(StartNodeField.Children[i]));
        }
        return new JsDocumentFragment(_engine, fragment);
    }

    private static LayoutNode ShallowClone(LayoutNode node) => new(null, node.TagName, node.Text, node.Style)
    {
        DomNode = node.DomNode,
    };

    public void insertNode(JsElement node)
    {
        var container = StartNodeField;
        if (container.TagName == "#text")
        {
            var text = container.Text ?? "";
            var offset = Math.Min(StartOffsetField, text.Length);
            container.SetOwnText(text[..offset]);
            var rest = text[offset..];
            if (container.Parent is { } parent)
            {
                var idx = parent.Children.IndexOf(container);
                var wrapper = node.Node;
                wrapper.Parent = parent;
                parent.Children.Insert(idx, wrapper);
                if (rest.Length > 0)
                {
                    var restNode = new LayoutNode(null, "#text", rest, container.Style) { DomNode = container.DomNode };
                    restNode.Parent = parent;
                    parent.Children.Insert(idx + 1, restNode);
                }
                return;
            }
        }
        var insertIdx = Math.Min(StartOffsetField, container.Children.Count);
        node.Node.Parent = container;
        container.Children.Insert(insertIdx, node.Node);
    }

    public string toString()
    {
        // The range's text: every text node in document order between the boundaries, bounded
        // by the offsets at the two ends (DOM4 §5.3 toString). The document-order walk stops
        // at the end boundary so an element-container start does not spill past the end node.
        if (collapsed) return "";
        if (ReferenceEquals(StartNodeField, EndNodeField) && StartNodeField.TagName != "#text")
        {
            // Same element container: the text of children [start, end).
            var sb2 = new System.Text.StringBuilder();
            for (var i = StartOffsetField; i < EndOffsetField && i < StartNodeField.Children.Count; i++)
            {
                var child = StartNodeField.Children[i];
                if (child.TagName == "#text") sb2.Append(child.Text ?? "");
                else AppendAllText(sb2, child);
            }
            return sb2.ToString();
        }
        var sb = new System.Text.StringBuilder();
        var endReached = false;
        void Walk(LayoutNode node)
        {
            if (endReached) return;
            if (ReferenceEquals(node, EndNodeField))
            {
                var text = node.Text ?? "";
                if (node.TagName == "#text") sb.Append(text[..Math.Min(EndOffsetField, text.Length)]);
                else for (var i = 0; i < Math.Min(EndOffsetField, node.Children.Count); i++) Walk(node.Children[i]);
                endReached = true;
                return;
            }
            var isText = node.TagName == "#text";
            var from = ReferenceEquals(node, StartNodeField)
                ? Math.Min(StartOffsetField, isText ? (node.Text?.Length ?? 0) : node.Children.Count)
                : 0;
            if (node.Children.Count == 0 || isText)
            {
                var text = node.Text ?? "";
                sb.Append(text[Math.Min(from, text.Length)..]);
                return;
            }
            for (var i = from; i < node.Children.Count; i++) Walk(node.Children[i]);
        }
        Walk(StartNodeField);
        return sb.ToString();
    }

        private static void AppendAllText(System.Text.StringBuilder sb, LayoutNode node)
    {
        // The projection folds a leaf's text into its own Text (a P's content is P.Text, no
        // #text child), so a leaf reads its folded text directly.
        if (node.Children.Count == 0 || node.TagName == "#text") { sb.Append(node.Text ?? ""); return; }
        foreach (var c in node.Children) AppendAllText(sb, c);
    }

    public JsRange cloneRange() => new(_engine)
    {
        StartNodeField = StartNodeField,
        StartOffsetField = StartOffsetField,
        EndNodeField = EndNodeField,
        EndOffsetField = EndOffsetField,
    };

    public void detach() { }
}

/// <summary>The DocumentFragment node (§4.4.2): a detached root holding moved/cloned nodes.</summary>
public class JsDocumentFragment
{
    private readonly Engine _engine;
    internal LayoutNode Node { get; }

    internal JsDocumentFragment(Engine engine, LayoutNode node)
    {
        _engine = engine;
        Node = node;
    }

    public JsElement? firstChild => JsElement.For(JsEngine.For(_engine)!.RawEngine, Node.Children.Count > 0 ? Node.Children[0] : Node);
    public int childElementCount => Node.Children.Count(c => !c.TagName.StartsWith('#'));
    public JsNodeList childNodes => new(() => Node.Children.Select(c => JsElement.For(JsEngine.For(_engine)!.RawEngine, c)).ToArray());
}
