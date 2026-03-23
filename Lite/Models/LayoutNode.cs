using AngleSharp.Css.Dom;
using Jint.Native;
using Lite.Animation;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Models;

/// <summary>A CSS property/value pair that is conditional on a media query.</summary>
public record MediaConditionalStyle(string MediaText, string Property, string Value, string Target);

/// <summary>An event listener entry supporting both capture and bubble phases.</summary>
public record EventListenerEntry(string EventType, JsValue? Handler, Action? LegacyHandler, bool Capture);

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
    /// <summary>CSS custom properties (--*) declared on this element, for var() resolution.</summary>
    public Dictionary<string, string> CustomProperties { get; } = [];
    /// <summary>Current interpolated animation/transition values. Highest priority in style resolution.</summary>
    public Dictionary<string, string> AnimationOverrides { get; } = [];
    /// <summary>Parsed `transition` declarations for this element.</summary>
    public List<TransitionSpec> TransitionSpecs { get; } = [];
    /// <summary>Parsed `animation` declarations for this element.</summary>
    public List<AnimationSpec> AnimationSpecs { get; } = [];
    /// <summary>Styles for ::before pseudo-element. Null if no ::before rule matched.</summary>
    public Dictionary<string, string>? BeforeStyles { get; set; }
    /// <summary>Styles for ::after pseudo-element. Null if no ::after rule matched.</summary>
    public Dictionary<string, string>? AfterStyles { get; set; }

    /// <summary>
    /// Resolves a CSS property considering pseudo-class state and media overrides.
    /// Priority: :active (media > base) > :focus (media > base) > :hover (media > base) > media overrides > style overrides.
    /// </summary>
    public bool TryResolveStyle(string prop, out string val)
    {
        val = null!;
        // Animation/transition overrides have highest priority (live interpolated values)
        if (AnimationOverrides.TryGetValue(prop, out var va)) { val = ResolveVarRefs(va); return true; }
        if (IsActive && MediaActiveStyles.TryGetValue(prop, out var v1m)) { val = ResolveVarRefs(v1m); return true; }
        if (IsActive && ActiveStyles.TryGetValue(prop, out var v1))       { val = ResolveVarRefs(v1);  return true; }
        if (IsFocused && MediaFocusStyles.TryGetValue(prop, out var v2m)) { val = ResolveVarRefs(v2m); return true; }
        if (IsFocused && FocusStyles.TryGetValue(prop, out var v2))       { val = ResolveVarRefs(v2);  return true; }
        if (IsHovered && MediaHoverStyles.TryGetValue(prop, out var v3m)) { val = ResolveVarRefs(v3m); return true; }
        if (IsHovered && HoverStyles.TryGetValue(prop, out var v3))       { val = ResolveVarRefs(v3);  return true; }
        if (MediaOverrides.TryGetValue(prop, out var v4m))                { val = ResolveVarRefs(v4m); return true; }
        if (StyleOverrides.TryGetValue(prop, out var v4))                 { val = ResolveVarRefs(v4);  return true; }
        return false;
    }

    /// <summary>
    /// Resolves all <c>var(--name)</c> and <c>var(--name, fallback)</c> references in a value
    /// by walking up the ancestor chain. Returns the original string if no var() is present.
    /// </summary>
    private string ResolveVarRefs(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("var(", StringComparison.OrdinalIgnoreCase))
            return value;

        var sb = new System.Text.StringBuilder();
        int i  = 0;

        while (i < value.Length)
        {
            int varIdx = value.IndexOf("var(", i, StringComparison.OrdinalIgnoreCase);
            if (varIdx < 0) { sb.Append(value, i, value.Length - i); break; }

            sb.Append(value, i, varIdx - i);

            int start = varIdx + 4;
            int depth = 1, j = start;
            while (j < value.Length && depth > 0)
            {
                if      (value[j] == '(') depth++;
                else if (value[j] == ')') depth--;
                if (depth > 0) j++;
                else break;
            }

            var inner = value[start..j];

            // Split at first top-level comma → name, fallback
            int commaIdx = -1, d = 0;
            for (int k = 0; k < inner.Length; k++)
            {
                if      (inner[k] == '(') d++;
                else if (inner[k] == ')') d--;
                else if (inner[k] == ',' && d == 0) { commaIdx = k; break; }
            }

            var name     = (commaIdx >= 0 ? inner[..commaIdx] : inner).Trim();
            var fallback = commaIdx >= 0 ? inner[(commaIdx + 1)..].Trim() : null;

            string? resolved = null;
            for (var cur = this; cur != null; cur = cur.Parent)
            {
                if (cur.CustomProperties.TryGetValue(name, out var v)) { resolved = v.Trim(); break; }
            }

            sb.Append(resolved != null ? ResolveVarRefs(resolved) : fallback != null ? ResolveVarRefs(fallback) : "");
            i = j + 1;
        }

        return sb.ToString();
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
    public List<EventListenerEntry> EventListeners { get; } = [];
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
