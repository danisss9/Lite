using Lite.Models;
using Lite.Scripting.Dom;

namespace Lite.Layout;

/// <summary>
/// Applies the author stylesheet cascade to <see cref="LayoutNode"/>s that were created
/// programmatically (document.createElement) and therefore never went through the
/// AngleSharp-backed cascade in <see cref="Parser"/>.
///
/// The cascade is specificity- and !important-correct (CSS 2.1 §6.4): declarations are
/// ordered by importance, then specificity, then source order. Inline styles already on the
/// node (e.g. element.style.x set before insertion) win over normal author rules but lose to
/// !important author rules. Inherited properties fall back to the parent's resolved value.
/// </summary>
internal static class StyleResolver
{
    // CSS 2.1 inherited properties (the common subset the engine renders).
    private static readonly string[] InheritedProperties =
    {
        "color", "font-family", "font-size", "font-style", "font-weight", "font-variant",
        "line-height", "letter-spacing", "word-spacing", "text-align", "text-indent",
        "text-transform", "white-space", "list-style-type", "list-style-position",
        "visibility", "cursor", "direction",
    };

    /// <summary>
    /// Resolves styles for every node in <paramref name="root"/>'s subtree that still
    /// needs resolution, then clears the flag. Call this when a programmatically-created
    /// subtree is inserted into the live document.
    /// </summary>
    internal static void ApplyTree(LayoutNode root)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.NeedsStyleResolution)
            {
                Apply(node);
                node.NeedsStyleResolution = false;
            }
            foreach (var child in node.Children)
                stack.Push(child);
        }
    }

    /// <summary>Applies matching author rules + inheritance to a single node. Idempotent:
    /// values stamped by a previous resolution are retracted first, so this can re-run
    /// after a class/id change without stale rule values masquerading as inline styles.</summary>
    internal static void Apply(LayoutNode node)
    {
        if (node.TagName.StartsWith('#')) return; // text / fragment / document nodes

        foreach (var prop in node.CascadeAppliedProps)
            node.StyleOverrides.Remove(prop);
        node.CascadeAppliedProps.Clear();

        // Gather matching rules, then order them: importance is decided per-property below,
        // so first sort all matches by (specificity, source order).
        var matches = new List<Parser.CssRule>();
        foreach (var rule in Parser.CssRules)
        {
            bool ok;
            try { ok = SelectorEngine.Matches(node, rule.Selector); }
            catch { continue; }
            if (ok) matches.Add(rule);
        }
        matches.Sort((x, y) =>
        {
            int cmp = x.Specificity.CompareTo(y.Specificity);
            return cmp != 0 ? cmp : x.Order.CompareTo(y.Order);
        });

        // Build the winning normal and important declarations (later in sorted order wins).
        var normal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var important = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in matches)
            foreach (var (prop, val) in rule.Properties)
            {
                if (rule.ImportantProps.Contains(prop)) important[prop] = val;
                else normal[prop] = val;
            }

        // Normal author rules: fill in only where no inline style is already present.
        foreach (var (prop, val) in normal)
            if (node.StyleOverrides.TryAdd(prop, val))
                node.CascadeAppliedProps.Add(prop);

        // !important author rules: override even inline styles.
        foreach (var (prop, val) in important)
        {
            node.StyleOverrides[prop] = val;
            node.CascadeAppliedProps.Add(prop);
        }

        // Inheritance: for inherited properties still unset, take the parent's resolved value.
        if (node.Parent is { } parent)
        {
            foreach (var prop in InheritedProperties)
            {
                if (node.StyleOverrides.ContainsKey(prop)) continue;
                if (parent.TryResolveStyle(prop, out var inherited) && !string.IsNullOrEmpty(inherited))
                {
                    node.StyleOverrides[prop] = inherited;
                    node.CascadeAppliedProps.Add(prop);
                }
            }
        }
    }
}
