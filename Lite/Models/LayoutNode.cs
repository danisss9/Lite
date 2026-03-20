using AngleSharp.Css.Dom;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Models;

/// <summary>A CSS property/value pair that is conditional on a media query.</summary>
public record MediaConditionalStyle(string MediaText, string Property, string Value, string Target);

public class LayoutNode
{
    public Guid NodeKey { get; } = Guid.NewGuid();
    public string? Id { get; }
    public string TagName { get; }
    public string Text { get; }
    public ICssStyleDeclaration Style { get; }
    public string? Href { get; }
    public LayoutNode? Parent { get; set; }
    public List<LayoutNode> Children { get; } = [];
    public BoxDimensions Box { get; set; }
    public SKBitmap? Image { get; set; }
    public int IntrinsicWidth { get; set; }
    public int IntrinsicHeight { get; set; }
    public string? Alt { get; set; }
    public Dictionary<string, string> Attributes { get; } = [];
    public Dictionary<string, string> StyleOverrides { get; } = [];
    public Dictionary<string, string> HoverStyles { get; } = [];
    public Dictionary<string, string> FocusStyles { get; } = [];
    public Dictionary<string, string> ActiveStyles { get; } = [];
    /// <summary>Styles applied from currently-matching @media rules. Rebuilt on each viewport resize.</summary>
    public Dictionary<string, string> MediaOverrides { get; } = [];
    public Dictionary<string, string> MediaHoverStyles { get; } = [];
    public Dictionary<string, string> MediaFocusStyles { get; } = [];
    public Dictionary<string, string> MediaActiveStyles { get; } = [];
    /// <summary>All media-conditional styles found during parse (used to re-evaluate on resize).</summary>
    public List<MediaConditionalStyle> MediaConditionalStyles { get; } = [];
    public bool IsHovered { get; set; }
    public bool IsFocused { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Resolves a CSS property considering pseudo-class state and media overrides.
    /// Priority: :active (media > base) > :focus (media > base) > :hover (media > base) > media overrides > style overrides.
    /// </summary>
    public bool TryResolveStyle(string prop, out string val)
    {
        val = null!;
        if (IsActive && MediaActiveStyles.TryGetValue(prop, out var v1m)) { val = v1m; return true; }
        if (IsActive && ActiveStyles.TryGetValue(prop, out var v1))       { val = v1;  return true; }
        if (IsFocused && MediaFocusStyles.TryGetValue(prop, out var v2m)) { val = v2m; return true; }
        if (IsFocused && FocusStyles.TryGetValue(prop, out var v2))       { val = v2;  return true; }
        if (IsHovered && MediaHoverStyles.TryGetValue(prop, out var v3m)) { val = v3m; return true; }
        if (IsHovered && HoverStyles.TryGetValue(prop, out var v3))       { val = v3;  return true; }
        if (MediaOverrides.TryGetValue(prop, out var v4m))                { val = v4m; return true; }
        if (StyleOverrides.TryGetValue(prop, out var v4))                 { val = v4;  return true; }
        return false;
    }

    /// <summary>
    /// Re-evaluates all stored media-conditional styles against the given viewport dimensions
    /// and rebuilds the Media* dictionaries. Call this on every viewport resize.
    /// </summary>
    public void ReapplyMediaStyles(int viewportWidth, int viewportHeight)
    {
        MediaOverrides.Clear();
        MediaHoverStyles.Clear();
        MediaFocusStyles.Clear();
        MediaActiveStyles.Clear();

        foreach (var ms in MediaConditionalStyles)
        {
            if (!MediaQueryEvaluator.Matches(ms.MediaText, viewportWidth, viewportHeight)) continue;

            var dict = ms.Target switch
            {
                "hover"  => MediaHoverStyles,
                "focus"  => MediaFocusStyles,
                "active" => MediaActiveStyles,
                _        => MediaOverrides,
            };
            dict[ms.Property] = ms.Value;
        }

        foreach (var child in Children)
            child.ReapplyMediaStyles(viewportWidth, viewportHeight);
    }

    public string? TextOverride { get; set; }
    public string DisplayText => TextOverride ?? Text;
    public List<(string EventType, Action Handler)> EventListeners { get; } = [];
    /// <summary>
    /// Static position within a flex container, set by FlexEngine for abs-pos children.
    /// Used by BoxEngine.ResolveAbsoluteBox when top/left are auto.
    /// </summary>
    public float? FlexStaticX { get; set; }
    public float? FlexStaticY { get; set; }

    public LayoutNode(string? id, string tagName, string text, ICssStyleDeclaration style, string? href = null)
    {
        Id = id;
        TagName = tagName;
        Text = text;
        Style = style;
        Href = href;
    }

    public void AddChild(LayoutNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}
