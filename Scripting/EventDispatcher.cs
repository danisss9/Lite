using Lite.Models;

namespace Lite.Scripting;

internal static class EventDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="eventType"/> to the node identified by
    /// <paramref name="nodeKey"/>. Also executes inline on* attribute code
    /// (e.g. onclick="...") through the active <see cref="JsEngine"/>.
    /// Returns <c>true</c> if at least one handler ran (caller should redraw).
    /// </summary>
    internal static bool Dispatch(Guid nodeKey, string eventType, LayoutNode? root)
    {
        var node = Find(root, nodeKey);
        if (node is null) return false;

        var handled = false;
        var attrKey = "on" + eventType.ToLowerInvariant();

        // Inline attribute handler (e.g. onclick="...")
        if (node.Attributes.TryGetValue(attrKey, out var inlineCode) &&
            JsEngine.Instance is { } engine)
        {
            engine.Execute(inlineCode);
            handled = true;
        }

        // addEventListener listeners
        foreach (var (type, handler) in node.EventListeners)
        {
            if (type != eventType.ToLowerInvariant()) continue;
            handler();
            handled = true;
        }

        return handled;
    }

    private static LayoutNode? Find(LayoutNode? node, Guid key)
    {
        if (node is null) return null;
        if (node.NodeKey == key) return node;
        foreach (var child in node.Children)
        {
            var result = Find(child, key);
            if (result is not null) return result;
        }
        return null;
    }
}
