using AngleSharp.Dom;
using Lite.Models;

namespace Lite.Interaction;

/// <summary>
/// Tracks :hover, :focus, and :active pseudo-class state across the layout tree.
/// Returns whether a re-render is needed after each state change.
/// </summary>
/// <remarks>
/// The state is kept twice: on the layout nodes (<see cref="LayoutNode.IsHovered"/> and
/// friends, read by the media-conditional pseudo styles and transitions) and as DOM element
/// sets that AngleSharp's selector matching reads through <see cref="DynamicPseudoState"/>
/// (CSS 2.1 §5.11.3: :hover and :active also apply to the ancestors in the document tree).
/// Every change bumps <see cref="Epoch"/>, which invalidates the per-node dynamic-rule cache.
/// </remarks>
internal static class PseudoClassState
{
    private static readonly List<LayoutNode> _hoveredChain = [];
    private static LayoutNode? _activeNode;
    private static LayoutNode? _focusedNode;
    private static readonly HashSet<INode> _hoveredDom = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<INode> _activeDom = new(ReferenceEqualityComparer.Instance);
    private static INode? _focusedDom;
    private static int _dynamicRules;

    /// <summary>Bumped on every interaction-state change (and by <see cref="Invalidate"/>),
    /// so dynamic-rule matches are recomputed instead of read from a stale cache.</summary>
    public static int Epoch { get; private set; }

    /// <summary>Installs the live-state probes AngleSharp's :hover/:active/:focus matching
    /// reads, and records that the page has dynamic rules (any state change re-renders).</summary>
    public static void Install()
    {
        _dynamicRules++;
        if (DynamicPseudoState.Hover is not null) return;
        DynamicPseudoState.Hover = element => _hoveredDom.Contains(element);
        DynamicPseudoState.Active = element => _activeDom.Contains(element);
        DynamicPseudoState.Focus = element => ReferenceEquals(_focusedDom, element);
    }

    /// <summary>Drops cached dynamic-rule matches (the DOM may have changed under them).</summary>
    public static void Invalidate() => Epoch++;

    private static bool AnyDynamicRules => _dynamicRules > 0;

    /// <summary>The DOM node a layout node renders; an inline piece renders its source's.</summary>
    private static INode? DomOf(LayoutNode node) => node.DomNode ?? node.SplitFrom?.DomNode;

    private static void FillDomChain(HashSet<INode> set, LayoutNode? target)
    {
        set.Clear();
        for (var dom = target is null ? null : DomOf(target) ?? FirstDomAncestor(target); dom is not null; dom = dom.Parent)
            set.Add(dom);
    }

    private static INode? FirstDomAncestor(LayoutNode node)
    {
        for (var n = node.Parent; n is not null; n = n.Parent)
            if (DomOf(n) is { } dom) return dom;
        return null;
    }

    /// <summary>
    /// Updates hover state based on the mouse position (in content-space coordinates).
    /// Returns true if any node with hover styles changed state.
    /// </summary>
    public static bool UpdateHover(LayoutNode? root, float x, float contentY)
    {
        if (root == null) { var had = _hoveredChain.Count > 0; ClearHover(); return had && AnyDynamicRules; }

        var target = FindNodeAt(root, x, contentY);

        // Build the new chain from target up to root
        var newChain = new List<LayoutNode>();
        var n = target;
        while (n != null)
        {
            newChain.Add(n);
            n = n.Parent;
        }

        // Quick check: same deepest node means same chain
        if (newChain.Count > 0 && _hoveredChain.Count > 0 && newChain[0] == _hoveredChain[0])
            return false;

        // Check if any affected node has hover styles (old or new chain)
        var needsRender = AnyDynamicRules;
        foreach (var node in _hoveredChain)
        {
            if (node.MediaHoverStyles.Count > 0) needsRender = true;
            node.IsHovered = false;
        }
        foreach (var node in newChain)
        {
            if (node.MediaHoverStyles.Count > 0) needsRender = true;
            node.IsHovered = true;
        }

        _hoveredChain.Clear();
        _hoveredChain.AddRange(newChain);
        FillDomChain(_hoveredDom, target);
        Epoch++;

        return needsRender;
    }

    /// <summary>Clears all hover state.</summary>
    public static void ClearHover()
    {
        foreach (var node in _hoveredChain)
            node.IsHovered = false;
        _hoveredChain.Clear();
        _hoveredDom.Clear();
        Epoch++;
    }

    /// <summary>
    /// Sets :active on the node at the given position and its ancestors.
    /// Returns true if a re-render is needed.
    /// </summary>
    public static bool SetActive(LayoutNode? root, float x, float contentY)
    {
        if (root == null) return false;
        var target = FindNodeAt(root, x, contentY);
        if (target == _activeNode) return false;

        // Clear previous
        ClearActiveInternal();

        if (target == null) return AnyDynamicRules;
        _activeNode = target;

        // Set :active on target and ancestors
        var needsRender = AnyDynamicRules;
        var n = target;
        while (n != null)
        {
            n.IsActive = true;
            if (n.MediaActiveStyles.Count > 0) needsRender = true;
            n = n.Parent;
        }
        FillDomChain(_activeDom, target);
        Epoch++;
        return needsRender;
    }

    /// <summary>Clears all :active state. Returns true if re-render needed.</summary>
    public static bool ClearActive()
    {
        if (_activeNode == null) return false;
        var needsRender = AnyDynamicRules;
        var n = _activeNode;
        while (n != null)
        {
            if (n.MediaActiveStyles.Count > 0) needsRender = true;
            n.IsActive = false;
            n = n.Parent;
        }
        _activeNode = null;
        _activeDom.Clear();
        Epoch++;
        return needsRender;
    }

    private static void ClearActiveInternal()
    {
        if (_activeNode == null) return;
        var n = _activeNode;
        while (n != null) { n.IsActive = false; n = n.Parent; }
        _activeNode = null;
        _activeDom.Clear();
        Epoch++;
    }

    /// <summary>
    /// Updates :focus state to the node with the given key.
    /// Pass null to clear focus. Returns true if re-render needed.
    /// </summary>
    public static bool UpdateFocus(LayoutNode? root, Guid? nodeKey)
    {
        var target = root == null || nodeKey == null ? null : FindNodeByKey(root, nodeKey.Value);
        return SetFocus(target);
    }

    /// <summary>Moves :focus to <paramref name="target"/> (null clears it) — the single
    /// writer behind interaction focus, script focus()/blur() and sequential navigation.
    /// Returns true if a re-render is needed.</summary>
    public static bool SetFocus(LayoutNode? target)
    {
        if (ReferenceEquals(target, _focusedNode)) return false;
        var needsRender = AnyDynamicRules;

        // Clear old focus
        if (_focusedNode != null)
        {
            if (_focusedNode.MediaFocusStyles.Count > 0) needsRender = true;
            _focusedNode.IsFocused = false;
            _focusedNode = null;
        }

        _focusedDom = target is null ? null : DomOf(target);
        if (target != null)
        {
            target.IsFocused = true;
            _focusedNode = target;
            if (target.MediaFocusStyles.Count > 0) needsRender = true;
        }
        Epoch++;

        return needsRender;
    }

    /// <summary>Finds the deepest node whose border box contains the point.</summary>
    private static LayoutNode? FindNodeAt(LayoutNode root, float x, float y)
    {
        // Walk children in reverse (last painted = on top)
        for (int i = root.Children.Count - 1; i >= 0; i--)
        {
            var result = FindNodeAt(root.Children[i], x, y);
            if (result != null) return result;
        }

        var box = root.Box.BorderBox;
        if (box.Width > 0 && box.Height > 0 && box.Contains(x, y))
            return root;
        return null;
    }

    /// <summary>Finds a node by its NodeKey (iterative, cycle-safe).</summary>
    private static LayoutNode? FindNodeByKey(LayoutNode root, Guid key)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.NodeKey == key) return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
        return null;
    }
}
