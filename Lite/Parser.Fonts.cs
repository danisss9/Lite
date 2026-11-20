using AngleSharp.Dom;
using Lite.Layout;

namespace Lite;

/// <summary>The font longhands the Parser computes itself (lane 6.J, fonts).</summary>
internal static partial class Parser
{
    /// <summary>The 'font-weight' value that wins the cascade for this element itself (a direct
    /// longhand or the 'font' shorthand's weight component), or null when nothing declares it —
    /// the element then inherits its parent's computed weight (§15.6, an inherited property).
    /// AngleSharp hands descendants the SPECIFIED value ('bolder'), which re-applied the relative
    /// step at every level below the declaring element; reading the element's own declaration is
    /// what lets 'bolder'/'lighter' resolve once, against the parent (font-weight-bolder-001).
    /// Invalid values (font-weight: 9000) are skipped, so the next declaration in cascade order
    /// applies (§4.2 "illegal values ... must be ignored").</summary>
    private static string? OwnFontWeightDeclaration(IElement element)
    {
        string? best = null;
        var bestKey = (Rank: -1, Specificity: -1, Order: -1);

        foreach (var rule in CssRules)
        {
            if (FontWeightOf(rule.Properties) is not { } val) continue;
            try { if (!element.Matches(rule.Selector)) continue; }
            catch { continue; }
            // §6.4.1: origin and importance outrank specificity — an author 'th { font-weight:
            // normal }' beats the UA 'th { font-weight: bold }' whatever their specificities.
            var important = rule.ImportantProps.Contains("font-weight") || rule.ImportantProps.Contains("font");
            var rank = important ? (rule.Origin == RuleOrigin.Author ? 3 : 4) : (int)rule.Origin;
            var key = (rank, rule.Specificity, rule.Order);
            if (best is null || key.CompareTo(bestKey) > 0) { best = val; bestKey = key; }
        }

        // An inline declaration outranks every non-important rule (CSS 2.1 §6.4.3).
        if (element.GetAttribute("style") is { Length: > 0 } inline && bestKey.Rank < 3)
        {
            var (props, _) = ParseDeclarations(inline);
            if (FontWeightOf(props) is { } iv) return iv;
        }
        return best;
    }

    /// <summary>The font-weight a declaration block sets, directly or through the 'font'
    /// shorthand (§15.8: the shorthand resets every font longhand it omits to its initial
    /// value, so <c>font: 1in Ahem</c> sets the weight back to 'normal').</summary>
    private static string? FontWeightOf(Dictionary<string, string> props)
    {
        if (props.TryGetValue("font-weight", out var direct) && !string.IsNullOrWhiteSpace(direct))
            return TextMeasure.IsValidFontWeight(direct) ? direct.Trim() : null;
        if (!props.TryGetValue("font", out var shorthand) || string.IsNullOrWhiteSpace(shorthand))
            return null;
        var trimmed = shorthand.Trim();
        if (trimmed.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return "inherit";
        // <style> <variant> <weight> precede the size: the first token that is a weight keyword
        // or number before the first length is the weight. 'normal' is ambiguous between the
        // three, and resolves to the initial weight either way.
        foreach (var token in trimmed.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TextMeasure.IsValidFontWeight(token) && !token.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return token;
            var t = token.Split('/')[0];
            if (t.Length > 0 && (char.IsAsciiDigit(t[0]) || t[0] == '.' || t.EndsWith('%'))) break;
        }
        return "normal";
    }
}
