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

    /// <summary>Whether a 'font' shorthand declaration is syntactically valid for the parts
    /// Lite extracts (§15.8 / §6.2.1 / §10.8.1 / §4.2): rejects 'inherit' mixed with other
    /// subproperty values (<c>font: 40px inherit</c>, font-045) and negative <c>/&lt;line-height&gt;</c>
    /// components (<c>font: 4em/-2em serif</c>, font-146), so the whole shorthand is ignored.</summary>
    private static bool IsValidFontShorthand(string shorthand)
    {
        var trimmed = shorthand.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("initial", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("unset", StringComparison.OrdinalIgnoreCase))
            return true;

        // Scan unquoted tokens (preserving quoted family names like "inherit" or "a/b").
        var tokens = new List<(string Text, bool Quoted)>();
        var i = 0;
        while (i < trimmed.Length)
        {
            while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i])) i++;
            if (i >= trimmed.Length) break;
            if (trimmed[i] is '"' or '\'')
            {
                var q = trimmed[i++];
                var start = i;
                while (i < trimmed.Length && trimmed[i] != q)
                {
                    if (trimmed[i] == '\\' && i + 1 < trimmed.Length) i++;
                    i++;
                }
                tokens.Add((trimmed[start..i], true));
                if (i < trimmed.Length) i++;
            }
            else
            {
                var start = i;
                while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i])) i++;
                tokens.Add((trimmed[start..i], false));
            }
        }

        for (var t = 0; t < tokens.Count; t++)
        {
            var (tok, quoted) = tokens[t];
            if (quoted) continue;
            // §6.2.1 / §15.8: 'inherit' cannot mix with other values in the shorthand.
            var bare = tok.TrimEnd(',');
            if (tokens.Count > 1 && bare.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                return false;
            // §10.8.1: negative line-height after '/' is invalid and voids the entire shorthand.
            var slash = tok.IndexOf('/');
            if (slash >= 0)
            {
                var lh = slash + 1 < tok.Length ? tok[(slash + 1)..] : (t + 1 < tokens.Count ? tokens[t + 1].Text : "");
                if (lh.StartsWith('-')) return false;
            }
        }
        return true;
    }

    /// <summary>True when the element matched an invalid 'font' shorthand (e.g. negative
    /// line-height in <c>font: 4em/-2em serif</c>, font-146) that AngleSharp expanded into
    /// 'font-family' anyway, and no valid 'font'/'font-family' declaration applies on the
    /// element itself. The caller then resets 'font-family' to 'inherit' so the bogus
    /// longhand from the rejected shorthand does not leak.</summary>
    private static bool HasOnlyInvalidFontShorthand(IElement element)
    {
        var sawInvalid = false;
        foreach (var rule in CssRules)
        {
            var hasFamily = rule.Properties.TryGetValue("font-family", out var ff) && !string.IsNullOrWhiteSpace(ff);
            var hasFont = rule.Properties.TryGetValue("font", out var f) && !string.IsNullOrWhiteSpace(f);
            if (!hasFamily && !hasFont) continue;
            try { if (!element.Matches(rule.Selector)) continue; }
            catch { continue; }
            if (hasFamily || (hasFont && IsValidFontShorthand(f!))) return false;
            sawInvalid = true;
        }
        if (element.GetAttribute("style") is { Length: > 0 } inline)
        {
            var (props, _) = ParseDeclarations(inline);
            if (props.TryGetValue("font-family", out var ff) && !string.IsNullOrWhiteSpace(ff)) return false;
            if (props.TryGetValue("font", out var f) && !string.IsNullOrWhiteSpace(f))
                return !IsValidFontShorthand(f);
        }
        return sawInvalid;
    }

    /// <summary>The font-weight a declaration block sets, directly or through the 'font'
    /// shorthand (§15.8: the shorthand resets every font longhand it omits to its initial
    /// value, so <c>font: 1in Ahem</c> sets the weight back to 'normal').</summary>
    private static string? FontWeightOf(Dictionary<string, string> props)
    {
        if (props.TryGetValue("font-weight", out var direct) && !string.IsNullOrWhiteSpace(direct))
            return TextMeasure.IsValidFontWeight(direct) ? direct.Trim() : null;
        if (!props.TryGetValue("font", out var shorthand) || string.IsNullOrWhiteSpace(shorthand) ||
            !IsValidFontShorthand(shorthand))
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
            if (t.Length > 0 && (AsciiChar.IsDigit(t[0]) || t[0] == '.' || t.EndsWith('%'))) break;
        }
        return "normal";
    }
}
