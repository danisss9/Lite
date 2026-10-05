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
    private static readonly string[] InheritedProperties = PropertyTable.InheritedProperties.ToArray();

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
            if (node.Parent?.OwningDocument is { } document) node.DocumentState = document;
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

        // Gather matching rules, then determine each declaration's cascade tier: CSS 2.1 §6.4.1
        // orders (ascending) UA-normal < user-normal < author-normal < author-important <
        // user-important, and only within one tier do specificity and source order decide.
        var matches = new List<Parser.CssRule>();
        foreach (var rule in node.OwningDocument?.StyleRules ?? Parser.CssRules)
        {
            bool ok;
            try { ok = SelectorEngine.Matches(node, rule.Selector); }
            catch { continue; }
            if (ok) matches.Add(rule);
        }

        // Build the winning declaration per property across all five tiers (later tier wins;
        // within a tier the later rule in (specificity, order) wins).
        var winners = new Dictionary<string, (int Tier, int Specificity, int Order, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in matches)
            foreach (var (prop, val) in rule.Properties)
            {
                var important = rule.ImportantProps.Contains(prop);
                var tier = rule.Origin switch
                {
                    Parser.RuleOrigin.Ua => 0,
                    Parser.RuleOrigin.User => important ? 4 : 1,
                    _ => important ? 3 : 2,
                };
                if (winners.TryGetValue(prop, out var current) &&
                    (current.Tier > tier || current.Tier == tier &&
                        (current.Specificity > rule.Specificity ||
                         current.Specificity == rule.Specificity && current.Order > rule.Order)))
                    continue;
                winners[prop] = (tier, rule.Specificity, rule.Order, val);
            }

        // Tiers 0-2 (UA/user/author normal) lose to an inline style; the important tiers (3-4)
        // override even inline styles (§6.4.3).
        foreach (var (prop, win) in winners)
        {
            if (win.Tier < 3 && node.StyleOverrides.ContainsKey(prop)) continue;
            node.StyleOverrides[prop] = win.Value;
            node.CascadeAppliedProps.Add(prop);
        }

        // Resolve the cascade-wide keywords initial / inherit / unset against PropertyTable.
        foreach (var prop in node.StyleOverrides.Keys.ToList())
            ResolveCssWideKeyword(node, prop);

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

    /// <summary>Resolves an <c>initial</c> / <c>inherit</c> / <c>unset</c> value in
    /// <see cref="LayoutNode.StyleOverrides"/> to a concrete value (CSS 2.1 / Cascade §7.3).</summary>
    internal static void ResolveCssWideKeyword(LayoutNode node, string prop)
    {
        if (!node.StyleOverrides.TryGetValue(prop, out var raw)) return;
        var keyword = raw.Trim().ToLowerInvariant();
        if (keyword is not ("initial" or "inherit" or "unset")) return;

        string? resolved = keyword switch
        {
            "inherit" => InheritedOrNull(node, prop) ?? PropertyTable.InitialValue(prop),
            "initial" => PropertyTable.InitialValue(prop),
            "unset" => PropertyTable.IsInherited(prop)
                ? (InheritedOrNull(node, prop) ?? PropertyTable.InitialValue(prop))
                : PropertyTable.InitialValue(prop),
            _ => null,
        };

        if (resolved is not null) node.StyleOverrides[prop] = resolved;
        else node.StyleOverrides.Remove(prop); // unknown property → drop the unusable keyword
    }

    private static string? InheritedOrNull(LayoutNode node, string prop) =>
        node.Parent is { } p && p.TryResolveStyle(prop, out var v) && !string.IsNullOrEmpty(v) ? v : null;
}
