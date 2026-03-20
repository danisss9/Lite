using AngleSharp.Css.Dom;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Models;

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
    public bool IsHovered { get; set; }
    public bool IsFocused { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Resolves a CSS property considering pseudo-class state.
    /// Priority: :active > :focus > :hover > StyleOverrides.
    /// </summary>
    public bool TryResolveStyle(string prop, out string val)
    {
        val = null!;
        if (IsActive && ActiveStyles.TryGetValue(prop, out var v1)) { val = v1; return true; }
        if (IsFocused && FocusStyles.TryGetValue(prop, out var v2)) { val = v2; return true; }
        if (IsHovered && HoverStyles.TryGetValue(prop, out var v3)) { val = v3; return true; }
        if (StyleOverrides.TryGetValue(prop, out var v4)) { val = v4; return true; }
        return false;
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
