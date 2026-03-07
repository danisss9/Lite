using AngleSharp.Css.Dom;
using Lite.Layout;

namespace Lite.Models;

public class LayoutNode
{
    public string? Id { get; }
    public string TagName { get; }
    public string Text { get; }
    public ICssStyleDeclaration Style { get; }
    public string? Href { get; }
    public LayoutNode? Parent { get; set; }
    public List<LayoutNode> Children { get; } = [];
    public BoxDimensions Box { get; set; }

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
