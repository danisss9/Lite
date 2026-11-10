using Lite.Models;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>The document's Selection (CSSOM View / DOM4 §5.2): a live facade over one anchor
/// and one focus boundary point, exposed through window.getSelection(). The engine holds one
/// selection per document; edits through execCommand read and update it.</summary>
public class JsSelection
{
    private readonly Engine _engine;
    internal LayoutNode? AnchorNodeField;
    internal int AnchorOffsetField;
    internal LayoutNode? FocusNodeField;
    internal int FocusOffsetField;
    internal bool DirectionBackward;

    internal JsSelection(Engine engine)
    {
        _engine = engine;
        var root = RootNode();
        AnchorNodeField = root;
        FocusNodeField = root;
    }

    private LayoutNode RootNode() => JsEngine.For(_engine)?.DocumentFacade?.RootNode
        ?? new LayoutNode(null, "#document-fragment", "", null!);

    public JsElement? anchorNode => Wrap(AnchorNodeField);
    public int anchorOffset => AnchorOffsetField;
    public JsElement? focusNode => Wrap(FocusNodeField);
    public int focusOffset => FocusOffsetField;
    public bool isCollapsed => ReferenceEquals(AnchorNodeField, FocusNodeField) && AnchorOffsetField == FocusOffsetField;
    public int rangeCount => isCollapsed && RootIsAnchor() ? 0 : 1;

    private bool RootIsAnchor()
    {
        var root = RootNode();
        return ReferenceEquals(AnchorNodeField, root) && ReferenceEquals(FocusNodeField, root);
    }

    private JsElement? Wrap(LayoutNode? node) => node is null ? null : JsElement.For(JsEngine.For(_engine)!.RawEngine, node);

    public JsRange getRangeAt(int index)
    {
        if (index != 0) throw JsErrors.Dom(_engine, "IndexSizeError", "Only one range is supported.");
        // The fields are set directly: going through setStart would trigger the boundary
        // collapse against the constructor's stale document-root end before setEnd lands.
        var range = new JsRange(_engine);
        if (DirectionBackward)
        {
            range.StartNodeField = FocusNodeField!;
            range.StartOffsetField = FocusOffsetField;
            range.EndNodeField = AnchorNodeField!;
            range.EndOffsetField = AnchorOffsetField;
        }
        else
        {
            range.StartNodeField = AnchorNodeField!;
            range.StartOffsetField = AnchorOffsetField;
            range.EndNodeField = FocusNodeField!;
            range.EndOffsetField = FocusOffsetField;
        }
        return range;
    }

    public void removeAllRanges()
    {
        var root = RootNode();
        AnchorNodeField = root;
        FocusNodeField = root;
        AnchorOffsetField = 0;
        FocusOffsetField = 0;
        DirectionBackward = false;
    }

    public void addRange(JsRange range)
    {
        // The DOM4 shape: the range's start becomes the anchor, the end the focus, and the
        // selection keeps the range's direction (a backward range sets the focus first).
        AnchorNodeField = range.StartNodeField;
        AnchorOffsetField = range.StartOffsetField;
        FocusNodeField = range.EndNodeField;
        FocusOffsetField = range.EndOffsetField;
        DirectionBackward = false;
    }

    public void collapse(JsElement? node, int offset = 0)
    {
        AnchorNodeField = node?.Node ?? RootNode();
        AnchorOffsetField = Math.Max(0, offset);
        FocusNodeField = AnchorNodeField;
        FocusOffsetField = AnchorOffsetField;
        DirectionBackward = false;
    }

    public void setPosition(JsElement? node, int offset = 0) => collapse(node, offset);

    public void selectAllChildren(JsElement node)
    {
        AnchorNodeField = node.Node;
        AnchorOffsetField = 0;
        FocusNodeField = node.Node;
        FocusOffsetField = node.Node.Children.Count;
        DirectionBackward = false;
    }

    public void extend(JsElement node, int offset)
    {
        // extend() moves only the focus: the direction flips when the new focus precedes the
        // anchor (DOM4 §5.2 extend steps).
        FocusNodeField = node.Node;
        FocusOffsetField = Math.Max(0, offset);
        DirectionBackward = JsRange.DocumentOrder(AnchorNodeField!, AnchorOffsetField, FocusNodeField!, FocusOffsetField) > 0;
    }

    public string toString()
    {
        var range = getRangeAt(rangeCount == 0 ? 0 : 0);
        return isCollapsed ? "" : range.toString();
    }

    /// <summary>The editing commands read the selection's single range (collapsed → an insertion
    /// point at the focus).</summary>
    internal JsRange EditingRange()
    {
        if (isCollapsed)
        {
            var r = new JsRange(_engine);
            r.setStart(Wrap(FocusNodeField)!, FocusOffsetField);
            r.collapse(true);
            return r;
        }
        return getRangeAt(0);
    }
}
