using Lite.Models;

namespace Lite.Interaction;

/// <summary>
/// Tracks :hover, :focus, and :active pseudo-class state across the layout tree.
/// Returns whether a re-render is needed after each state change.
/// </summary>
internal static class PseudoClassState
{
    private static readonly List<LayoutNode> _hoveredChain = [];
    private static LayoutNode? _activeNode;
    private static LayoutNode? _focusedNode;

    /// <summary>
    /// Updates hover state based on the mouse position (in content-space coordinates).
    /// Returns true if any node with hover styles changed state.
    /// </summary>
    public static bool UpdateHover(LayoutNode? root, float x, float contentY)
    {
        if (root == null) { ClearHover(); return _hoveredChain.Count > 0; }

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
        var needsRender = false;
        foreach (var node in _hoveredChain)
        {
            if (node.HoverStyles.Count > 0) needsRender = true;
            node.IsHovered = false;
        }
        foreach (var node in newChain)
        {
            if (node.HoverStyles.Count > 0) needsRender = true;
            node.IsHovered = true;
        }

        _hoveredChain.Clear();
        _hoveredChain.AddRange(newChain);

        return needsRender;
    }

    /// <summary>Clears all hover state.</summary>
    public static void ClearHover()
    {
        foreach (var node in _hoveredChain)
            node.IsHovered = false;
        _hoveredChain.Clear();
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

        if (target == null) return false;
        _activeNode = target;

        // Set :active on target and ancestors
        var needsRender = false;
        var n = target;
        while (n != null)
        {
            n.IsActive = true;
            if (n.ActiveStyles.Count > 0) needsRender = true;
            n = n.Parent;
        }
        return needsRender;
    }

    /// <summary>Clears all :active state. Returns true if re-render needed.</summary>
    public static bool ClearActive()
    {
        if (_activeNode == null) return false;
        var needsRender = false;
        var n = _activeNode;
        while (n != null)
        {
            if (n.ActiveStyles.Count > 0) needsRender = true;
            n.IsActive = false;
            n = n.Parent;
        }
        _activeNode = null;
        return needsRender;
    }

    private static void ClearActiveInternal()
    {
        if (_activeNode == null) return;
        var n = _activeNode;
        while (n != null) { n.IsActive = false; n = n.Parent; }
        _activeNode = null;
    }

    /// <summary>
    /// Updates :focus state to the node with the given key.
    /// Pass null to clear focus. Returns true if re-render needed.
    /// </summary>
    public static bool UpdateFocus(LayoutNode? root, Guid? nodeKey)
    {
        var needsRender = false;

        // Clear old focus
        if (_focusedNode != null)
        {
            if (_focusedNode.FocusStyles.Count > 0) needsRender = true;
            _focusedNode.IsFocused = false;
            _focusedNode = null;
        }

        if (root == null || nodeKey == null) return needsRender;

        // Find and set new focus
        var target = FindNodeByKey(root, nodeKey.Value);
        if (target != null)
        {
            target.IsFocused = true;
            _focusedNode = target;
            if (target.FocusStyles.Count > 0) needsRender = true;
        }

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

    /// <summary>Finds a node by its NodeKey (breadth-first).</summary>
    private static LayoutNode? FindNodeByKey(LayoutNode root, Guid key)
    {
        if (root.NodeKey == key) return root;
        foreach (var child in root.Children)
        {
            var found = FindNodeByKey(child, key);
            if (found != null) return found;
        }
        return null;
    }
}
