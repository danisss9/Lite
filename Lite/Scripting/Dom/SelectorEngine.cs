using Jint;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>CSS Selectors Level 3 engine for querySelector/querySelectorAll.</summary>
internal static class SelectorEngine
{
    internal static JsElement? QuerySelector(LayoutNode root, string selector, Engine engine)
    {
        var node = FindFirst(root, n => Matches(n, selector));
        return node is null ? null : new JsElement(engine, node);
    }

    internal static JsElement[] QuerySelectorAll(LayoutNode root, string selector, Engine engine)
    {
        return FindAll(root, n => Matches(n, selector))
            .Select(n => new JsElement(engine, n)).ToArray();
    }

    /// <summary>Tests whether a node matches a full selector string (with commas, combinators).</summary>
    internal static bool Matches(LayoutNode node, string selector)
    {
        // Handle comma-separated selectors
        var selectors = SplitOnCommas(selector);
        return selectors.Any(s => MatchesSingle(node, s.Trim()));
    }

    private static bool MatchesSingle(LayoutNode node, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;

        // Parse into a sequence of (combinator, simpleSelector) pairs
        var parts = ParseCombinatorSequence(selector);
        if (parts.Count == 0) return false;

        // Match right-to-left
        return MatchSequence(node, parts, parts.Count - 1);
    }

    private static bool MatchSequence(LayoutNode node, List<(char Combinator, string Selector)> parts, int index)
    {
        if (!MatchCompound(node, parts[index].Selector)) return false;
        if (index == 0) return true;

        var combinator = parts[index].Combinator;
        switch (combinator)
        {
            case ' ': // descendant
                for (var p = node.Parent; p != null; p = p.Parent)
                    if (MatchSequence(p, parts, index - 1)) return true;
                return false;

            case '>': // child
                return node.Parent != null && MatchSequence(node.Parent, parts, index - 1);

            case '+': // adjacent sibling
                var prev = GetPreviousElementSibling(node);
                return prev != null && MatchSequence(prev, parts, index - 1);

            case '~': // general sibling
                if (node.Parent == null) return false;
                var siblings = node.Parent.Children;
                var myIdx = siblings.IndexOf(node);
                for (int i = myIdx - 1; i >= 0; i--)
                    if (siblings[i].TagName != "#text" && MatchSequence(siblings[i], parts, index - 1)) return true;
                return false;

            default:
                return false;
        }
    }

    /// <summary>Matches a compound selector (e.g. "div.foo#bar[attr=val]:first-child").</summary>
    private static bool MatchCompound(LayoutNode node, string compound)
    {
        if (node.TagName == "#text" || node.TagName == "#document-fragment") return false;
        var parts = TokenizeCompound(compound);
        return parts.All(part => MatchSimple(node, part));
    }

    private static bool MatchSimple(LayoutNode node, string simple)
    {
        if (simple == "*") return true;

        // :not(...)
        if (simple.StartsWith(":not(") && simple.EndsWith(")"))
        {
            var inner = simple[5..^1];
            return !MatchCompound(node, inner);
        }

        // Pseudo-classes
        if (simple.StartsWith(':'))
        {
            return MatchPseudoClass(node, simple);
        }

        // Attribute selectors
        if (simple.StartsWith('[') && simple.EndsWith(']'))
        {
            return MatchAttribute(node, simple[1..^1]);
        }

        // Class selector
        if (simple.StartsWith('.'))
        {
            var cls = simple[1..];
            var nodeClasses = node.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return nodeClasses.Contains(cls);
        }

        // ID selector
        if (simple.StartsWith('#'))
        {
            return node.Id == simple[1..];
        }

        // Tag selector
        return node.TagName.Equals(simple, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchPseudoClass(LayoutNode node, string pseudo)
    {
        if (node.Parent == null) return false;
        var siblings = node.Parent.Children.Where(c => c.TagName != "#text").ToList();
        var sameType = siblings.Where(c => c.TagName == node.TagName).ToList();
        var index = siblings.IndexOf(node);
        var typeIndex = sameType.IndexOf(node);

        return pseudo switch
        {
            ":first-child" => index == 0,
            ":last-child" => index == siblings.Count - 1,
            ":only-child" => siblings.Count == 1,
            ":first-of-type" => typeIndex == 0,
            ":last-of-type" => typeIndex == sameType.Count - 1,
            ":only-of-type" => sameType.Count == 1,
            ":empty" => node.Children.Count == 0 && string.IsNullOrEmpty(node.Text),
            ":checked" => node.Attributes.ContainsKey("checked"),
            ":disabled" => node.Attributes.ContainsKey("disabled"),
            ":enabled" => !node.Attributes.ContainsKey("disabled") &&
                          (node.TagName is "INPUT" or "BUTTON" or "SELECT" or "TEXTAREA"),
            ":hover" => node.IsHovered,
            ":focus" => node.IsFocused,
            ":active" => node.IsActive,
            _ when pseudo.StartsWith(":nth-child(") => MatchNth(pseudo[11..^1], index + 1),
            _ when pseudo.StartsWith(":nth-last-child(") => MatchNth(pseudo[16..^1], siblings.Count - index),
            _ when pseudo.StartsWith(":nth-of-type(") => MatchNth(pseudo[13..^1], typeIndex + 1),
            _ when pseudo.StartsWith(":nth-last-of-type(") => MatchNth(pseudo[18..^1], sameType.Count - typeIndex),
            _ => false,
        };
    }

    private static bool MatchNth(string expr, int position)
    {
        expr = expr.Trim();
        if (expr == "odd") return position % 2 == 1;
        if (expr == "even") return position % 2 == 0;
        if (int.TryParse(expr, out var num)) return position == num;

        // Parse An+B
        var nIdx = expr.IndexOf('n');
        if (nIdx < 0) return false;
        var aStr = expr[..nIdx].Trim();
        var bStr = expr[(nIdx + 1)..].Trim().Replace("+", "").Trim();
        int a = aStr switch { "" or "+" => 1, "-" => -1, _ => int.TryParse(aStr, out var av) ? av : 0 };
        int b = string.IsNullOrEmpty(bStr) ? 0 : int.TryParse(bStr, out var bv) ? bv : 0;
        if (a == 0) return position == b;
        return (position - b) % a == 0 && (position - b) / a >= 0;
    }

    private static bool MatchAttribute(LayoutNode node, string attrSelector)
    {
        // [attr], [attr=val], [attr^=val], [attr$=val], [attr*=val], [attr~=val], [attr|=val]
        int opIdx = -1;
        string op = "exists";
        for (int i = 0; i < attrSelector.Length; i++)
        {
            if (attrSelector[i] == '=')
            {
                if (i > 0 && attrSelector[i - 1] is '^' or '$' or '*' or '~' or '|')
                {
                    opIdx = i - 1;
                    op = attrSelector[(i - 1)..(i + 1)];
                }
                else
                {
                    opIdx = i;
                    op = "=";
                }
                break;
            }
        }

        if (op == "exists")
        {
            return node.Attributes.ContainsKey(attrSelector.Trim());
        }

        var attrName = attrSelector[..opIdx].Trim();
        var attrValue = attrSelector[(opIdx + op.Length)..].Trim().Trim('"').Trim('\'');

        if (!node.Attributes.TryGetValue(attrName, out var nodeVal)) return false;

        return op switch
        {
            "=" => nodeVal == attrValue,
            "^=" => nodeVal.StartsWith(attrValue, StringComparison.Ordinal),
            "$=" => nodeVal.EndsWith(attrValue, StringComparison.Ordinal),
            "*=" => nodeVal.Contains(attrValue, StringComparison.Ordinal),
            "~=" => nodeVal.Split(' ').Contains(attrValue),
            "|=" => nodeVal == attrValue || nodeVal.StartsWith(attrValue + "-", StringComparison.Ordinal),
            _ => false,
        };
    }

    // ---- Parsing helpers ----

    private static List<string> SplitOnCommas(string selector)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < selector.Length; i++)
        {
            if (selector[i] == '(') depth++;
            else if (selector[i] == ')') depth--;
            else if (selector[i] == ',' && depth == 0)
            {
                result.Add(selector[start..i]);
                start = i + 1;
            }
        }
        result.Add(selector[start..]);
        return result;
    }

    /// <summary>Parses "div > p.foo + span" into [(nul, "div"), ('>', "p.foo"), ('+', "span")].</summary>
    private static List<(char Combinator, string Selector)> ParseCombinatorSequence(string selector)
    {
        var result = new List<(char, string)>();
        var tokens = new List<string>();
        int i = 0;
        while (i < selector.Length)
        {
            if (char.IsWhiteSpace(selector[i])) { i++; continue; }
            if (selector[i] is '>' or '+' or '~')
            {
                tokens.Add(selector[i].ToString());
                i++;
                continue;
            }
            // Read compound selector
            int start = i;
            while (i < selector.Length && !char.IsWhiteSpace(selector[i]) && selector[i] is not ('>' or '+' or '~'))
            {
                if (selector[i] == '(')
                {
                    int depth = 1;
                    i++;
                    while (i < selector.Length && depth > 0)
                    {
                        if (selector[i] == '(') depth++;
                        else if (selector[i] == ')') depth--;
                        i++;
                    }
                }
                else if (selector[i] == '[')
                {
                    while (i < selector.Length && selector[i] != ']') i++;
                    if (i < selector.Length) i++;
                }
                else i++;
            }
            tokens.Add(selector[start..i]);
        }

        // Convert token list: selector [combinator selector]*
        if (tokens.Count == 0) return result;
        result.Add(('\0', tokens[0]));
        for (int t = 1; t < tokens.Count; t++)
        {
            if (tokens[t] is ">" or "+" or "~")
            {
                if (t + 1 < tokens.Count)
                {
                    result.Add((tokens[t][0], tokens[t + 1]));
                    t++;
                }
            }
            else
            {
                result.Add((' ', tokens[t])); // descendant combinator
            }
        }
        return result;
    }

    /// <summary>Tokenizes "div.foo#bar[attr]:pseudo" into ["div", ".foo", "#bar", "[attr]", ":pseudo"].</summary>
    private static List<string> TokenizeCompound(string compound)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < compound.Length)
        {
            if (compound[i] == '.')
            {
                int start = i; i++;
                while (i < compound.Length && IsIdentChar(compound[i])) i++;
                parts.Add(compound[start..i]);
            }
            else if (compound[i] == '#')
            {
                int start = i; i++;
                while (i < compound.Length && IsIdentChar(compound[i])) i++;
                parts.Add(compound[start..i]);
            }
            else if (compound[i] == '[')
            {
                int start = i;
                while (i < compound.Length && compound[i] != ']') i++;
                if (i < compound.Length) i++;
                parts.Add(compound[start..i]);
            }
            else if (compound[i] == ':')
            {
                int start = i; i++;
                if (i < compound.Length && compound[i] == ':') i++; // skip ::
                while (i < compound.Length && IsIdentChar(compound[i])) i++;
                // Handle functional pseudo like :nth-child(...)
                if (i < compound.Length && compound[i] == '(')
                {
                    int depth = 1; i++;
                    while (i < compound.Length && depth > 0)
                    {
                        if (compound[i] == '(') depth++;
                        else if (compound[i] == ')') depth--;
                        i++;
                    }
                }
                parts.Add(compound[start..i]);
            }
            else if (compound[i] == '*')
            {
                parts.Add("*");
                i++;
            }
            else if (IsIdentChar(compound[i]))
            {
                int start = i;
                while (i < compound.Length && IsIdentChar(compound[i])) i++;
                parts.Add(compound[start..i]);
            }
            else i++;
        }
        return parts;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

    private static LayoutNode? GetPreviousElementSibling(LayoutNode node)
    {
        if (node.Parent == null) return null;
        var siblings = node.Parent.Children;
        var idx = siblings.IndexOf(node);
        for (int i = idx - 1; i >= 0; i--)
            if (siblings[i].TagName != "#text") return siblings[i];
        return null;
    }

    private static LayoutNode? FindFirst(LayoutNode node, Func<LayoutNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (var child in node.Children)
        {
            var result = FindFirst(child, predicate);
            if (result is not null) return result;
        }
        return null;
    }

    private static IEnumerable<LayoutNode> FindAll(LayoutNode node, Func<LayoutNode, bool> predicate)
    {
        if (predicate(node)) yield return node;
        foreach (var child in node.Children)
        foreach (var match in FindAll(child, predicate))
            yield return match;
    }
}
