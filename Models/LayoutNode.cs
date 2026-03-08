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
