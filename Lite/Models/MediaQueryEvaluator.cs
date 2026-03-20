using System.Text.RegularExpressions;

namespace Lite.Models;

/// <summary>
/// Evaluates CSS media query strings (e.g. "screen and (max-width: 768px)") against
/// a given viewport size. Supports the most common media features used in responsive design.
/// </summary>
internal static class MediaQueryEvaluator
{
    // Matches a single feature condition like "(max-width: 768px)"
    private static readonly Regex FeatureRegex =
        new(@"\(\s*([\w-]+)\s*:\s*([^)]+?)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns true when the media query text matches the given viewport dimensions.
    /// Handles comma-separated OR queries, "and" combinators, "not", and media types.
    /// </summary>
    public static bool Matches(string mediaText, int viewportWidth, int viewportHeight)
    {
        if (string.IsNullOrWhiteSpace(mediaText)) return true;

        // Comma-separated queries = OR
        foreach (var segment in mediaText.Split(','))
        {
            if (EvaluateSingle(segment.Trim(), viewportWidth, viewportHeight))
                return true;
        }
        return false;
    }

    private static bool EvaluateSingle(string query, int vw, int vh)
    {
        query = query.Trim();
        var negated = false;

        if (query.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            negated = true;
            query = query[4..].TrimStart();
        }

        // Split on "and" (whole word, case-insensitive) keeping feature groups intact.
        // We only split on "and" that sits outside parentheses.
        var parts = SplitOnAnd(query);
        var result = true;

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Length == 0) continue;

            // Media type
            if (!p.StartsWith('('))
            {
                var type = p.ToLowerInvariant();
                if (type == "print")             { result = false; break; }
                if (type is "screen" or "all")   continue;
                // Unknown media type — treat as mismatch
                result = false;
                break;
            }

            // Feature condition
            var match = FeatureRegex.Match(p);
            if (!match.Success) continue; // unknown feature — pass through

            var feature = match.Groups[1].Value.ToLowerInvariant();
            var value   = match.Groups[2].Value.Trim();

            if (!EvaluateFeature(feature, value, vw, vh))
            {
                result = false;
                break;
            }
        }

        return negated ? !result : result;
    }

    private static bool EvaluateFeature(string feature, string value, int vw, int vh)
    {
        switch (feature)
        {
            case "min-width":
                return TryParsePx(value, out var minW) && vw >= minW;
            case "max-width":
                return TryParsePx(value, out var maxW) && vw <= maxW;
            case "width":
                return TryParsePx(value, out var w) && vw == w;
            case "min-height":
                return TryParsePx(value, out var minH) && vh >= minH;
            case "max-height":
                return TryParsePx(value, out var maxH) && vh <= maxH;
            case "height":
                return TryParsePx(value, out var h) && vh == h;
            case "orientation":
                var orientation = value.Trim().ToLowerInvariant();
                return orientation == "portrait"  ? vh >= vw
                     : orientation == "landscape" ? vw > vh
                     : true;
            // Treat other features (color, resolution, etc.) as always matching
            default:
                return true;
        }
    }

    /// <summary>
    /// Parses a CSS length value that uses px, em (treated as 16px), or vw/vh units.
    /// Returns false for unknown or unparseable values.
    /// </summary>
    private static bool TryParsePx(string value, out float px)
    {
        px = 0;
        value = value.Trim();

        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return float.TryParse(value[..^2].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out px);

        if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var em))
        {
            px = em * 16f;
            return true;
        }

        if (value.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^3].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var rem))
        {
            px = rem * 16f;
            return true;
        }

        // Bare number — assume px
        return float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out px);
    }

    /// <summary>
    /// Splits a media query on the "and" keyword while respecting parentheses grouping.
    /// e.g. "screen and (min-width: 480px) and (max-width: 768px)" → ["screen", "(min-width: 480px)", "(max-width: 768px)"]
    /// </summary>
    private static IEnumerable<string> SplitOnAnd(string query)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < query.Length; i++)
        {
            if (query[i] == '(') { depth++; continue; }
            if (query[i] == ')') { depth--; continue; }

            if (depth == 0 && i + 4 <= query.Length &&
                string.Equals(query.Substring(i, 4), " and", StringComparison.OrdinalIgnoreCase) &&
                (i == 0 || query[i] == ' '))
            {
                // Check if this is " and " (surrounded by spaces or start/end)
                if (i + 4 < query.Length && query[i + 4] == ' ')
                {
                    parts.Add(query[start..i].Trim());
                    i += 4; // skip " and"
                    start = i + 1;
                }
            }
        }

        if (start < query.Length)
            parts.Add(query[start..].Trim());

        return parts.Count > 0 ? parts : [query];
    }
}
