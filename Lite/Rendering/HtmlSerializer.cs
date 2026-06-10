using System.Text;
using Lite.Models;

namespace Lite.Rendering;

/// <summary>
/// Serializes a <see cref="LayoutNode"/> subtree back to HTML for the
/// innerHTML / outerHTML getters. This is a pragmatic serializer — it emits the
/// element tree the engine actually holds, not a byte-perfect round-trip of the source.
/// </summary>
internal static class HtmlSerializer
{
    // HTML void elements never have a closing tag or children.
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "AREA", "BASE", "BR", "COL", "EMBED", "HR", "IMG", "INPUT",
        "LINK", "META", "PARAM", "SOURCE", "TRACK", "WBR",
    };

    // Internal bookkeeping attributes that should never be serialized.
    private static readonly HashSet<string> InternalAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "_options", "_optionValues",
    };

    /// <summary>Serializes the children of <paramref name="node"/> (innerHTML).</summary>
    internal static string SerializeChildren(LayoutNode node)
    {
        // A text-only element keeps its text directly rather than as a child node.
        if (node.Children.Count == 0)
            return Escape(node.DisplayText);

        var sb = new StringBuilder();
        foreach (var child in node.Children)
            SerializeNode(child, sb);
        return sb.ToString();
    }

    /// <summary>Serializes <paramref name="node"/> including its own tag (outerHTML).</summary>
    internal static string SerializeOuter(LayoutNode node)
    {
        var sb = new StringBuilder();
        SerializeNode(node, sb);
        return sb.ToString();
    }

    private static void SerializeNode(LayoutNode node, StringBuilder sb)
    {
        if (node.TagName == "#text")
        {
            sb.Append(Escape(node.DisplayText));
            return;
        }
        if (node.TagName.StartsWith('#'))
        {
            // document-fragment / document — emit children only.
            foreach (var child in node.Children)
                SerializeNode(child, sb);
            return;
        }

        var tag = node.TagName.ToLowerInvariant();
        sb.Append('<').Append(tag);

        // The parser stores the id on Node.Id rather than in Attributes; emit it explicitly.
        if (!string.IsNullOrEmpty(node.Id) && !node.Attributes.ContainsKey("id"))
            sb.Append(" id=\"").Append(EscapeAttribute(node.Id)).Append('"');

        foreach (var (name, value) in node.Attributes)
        {
            if (InternalAttributes.Contains(name)) continue;
            sb.Append(' ').Append(name);
            sb.Append("=\"").Append(EscapeAttribute(value)).Append('"');
        }

        if (VoidElements.Contains(node.TagName))
        {
            sb.Append('>');
            return;
        }

        sb.Append('>');
        sb.Append(SerializeChildren(node));
        sb.Append("</").Append(tag).Append('>');
    }

    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeAttribute(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("&", "&amp;").Replace("\"", "&quot;");
    }
}
