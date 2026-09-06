using AngleSharp.Css.Dom;
using Jint.Native;
using Lite.Animation;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Models;

/// <summary>A CSS property/value pair that is conditional on a media query.</summary>
public record MediaConditionalStyle(string MediaText, string Property, string Value, string Target);

/// <summary>An event listener entry supporting both capture and bubble phases. <c>Once</c> listeners
/// are removed from the node before they are invoked (DOM §2.9), so re-entrant dispatch can't re-run them.</summary>
public record EventListenerEntry(string EventType, JsValue? Handler, Action? LegacyHandler, bool Capture, bool Once = false);

public class LayoutNode
{
    public Guid NodeKey { get; } = Guid.NewGuid();
    internal DocumentState? DocumentState { get; set; }
    internal DocumentState? OwningDocument => Parent?.OwningDocument ?? DocumentState;
    /// <summary>The element's id. Backed by <see cref="Attributes"/> so parser-built and
    /// JS-mutated ids stay in sync (selector matching reads one source of truth).</summary>
    public string? Id => Attributes.GetValueOrDefault("id");
    public string TagName { get; }
    public string Text { get; }
    public ICssStyleDeclaration Style { get; }
    /// <summary>The element's href. Backed by <see cref="Attributes"/> (see <see cref="Id"/>).</summary>
    public string? Href => Attributes.GetValueOrDefault("href");
    public LayoutNode? Parent { get; set; }
    public List<LayoutNode> Children { get; } = [];
    public BoxDimensions Box { get; set; }
    public SKBitmap? Image { get; set; }
    /// <summary>For an &lt;iframe&gt;: the nested browsing context (child Page) it hosts. The
    /// iframe is laid out as a replaced box and the child Page is painted clipped into it.</summary>
    internal Page? ChildPage { get; set; }
    /// <summary>For an &lt;audio&gt;/&lt;video&gt;: the media backend driving its timeline. Created
    /// lazily when the element's media API is first used (or on autoplay).</summary>
    internal Media.IMediaBackend? Media { get; set; }
    public int IntrinsicWidth { get; set; }
    public int IntrinsicHeight { get; set; }
    public string? Alt { get; set; }
    public Dictionary<string, string> Attributes { get; } = [];
    public Dictionary<string, string> StyleOverrides { get; } = [];
    /// <summary>Keys in <see cref="StyleOverrides"/> that were written by the stylesheet
    /// cascade (StyleResolver) rather than inline styles. Cleared and re-stamped on each
    /// re-resolution so class/id changes can retract stale rule-applied values.</summary>
    public HashSet<string> CascadeAppliedProps { get; } = new(StringComparer.OrdinalIgnoreCase);
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
    /// <summary>Styles for ::first-letter pseudo-element.</summary>
    public Dictionary<string, string>? FirstLetterStyles { get; set; }
    /// <summary>Styles for ::first-line pseudo-element.</summary>
    public Dictionary<string, string>? FirstLineStyles { get; set; }
    /// <summary>Snapshot of CSS counter values (top of each scope stack) at this node's position
    /// in the tree (for counter() resolution).</summary>
    public Dictionary<string, int>? CounterValues { get; set; }
    /// <summary>Snapshot of the full nested counter scope stacks at this node (for counters(name, sep)).
    /// Each list is outermost-first; the last element is the innermost/current value.</summary>
    public Dictionary<string, List<int>>? CounterStacks { get; set; }
    /// <summary>Per-element scroll state for overflow:scroll/auto elements.</summary>
    public ElementScrollState? ScrollState { get; set; }

    /// <summary>For a child collapsed by a closed &lt;details&gt;: the display value before it was
    /// hidden (null = not hidden by details; "" = no explicit display to restore).</summary>
    public string? DetailsSavedDisplay { get; set; }

    /// <summary>For a &lt;template&gt; element: the inert <c>#document-fragment</c> holding its parsed
    /// content (exposed to JS as <c>template.content</c>). The content is NOT part of the rendered tree
    /// — the template element itself has no rendered children.</summary>
    public LayoutNode? TemplateContent { get; set; }

    /// <summary>
    /// True for nodes created via document.createElement that have not yet had the
    /// stylesheet cascade applied. <see cref="Lite.Layout.StyleResolver"/> resolves and
    /// clears this when the node is inserted into the live tree. Nodes produced by HTML
    /// parsing (page load or innerHTML) are already fully cascaded and leave this false.
    /// </summary>
    public bool NeedsStyleResolution { get; set; }

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
        if (IsActive && ActiveStyles.TryGetValue(prop, out var v1)) { val = ResolveVarRefs(v1); return true; }
        if (IsFocused && MediaFocusStyles.TryGetValue(prop, out var v2m)) { val = ResolveVarRefs(v2m); return true; }
        if (IsFocused && FocusStyles.TryGetValue(prop, out var v2)) { val = ResolveVarRefs(v2); return true; }
        if (IsHovered && MediaHoverStyles.TryGetValue(prop, out var v3m)) { val = ResolveVarRefs(v3m); return true; }
        if (IsHovered && HoverStyles.TryGetValue(prop, out var v3)) { val = ResolveVarRefs(v3); return true; }
        if (MediaOverrides.TryGetValue(prop, out var v4m)) { val = ResolveVarRefs(v4m); return true; }
        if (StyleOverrides.TryGetValue(prop, out var v4)) { val = ResolveVarRefs(v4); return true; }
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
        int i = 0;

        while (i < value.Length)
        {
            int varIdx = value.IndexOf("var(", i, StringComparison.OrdinalIgnoreCase);
            if (varIdx < 0) { sb.Append(value, i, value.Length - i); break; }

            sb.Append(value, i, varIdx - i);

            int start = varIdx + 4;
            int depth = 1, j = start;
            while (j < value.Length && depth > 0)
            {
                if (value[j] == '(') depth++;
                else if (value[j] == ')') depth--;
                if (depth > 0) j++;
                else break;
            }

            var inner = value[start..j];

            // Split at first top-level comma → name, fallback
            int commaIdx = -1, d = 0;
            for (int k = 0; k < inner.Length; k++)
            {
                if (inner[k] == '(') d++;
                else if (inner[k] == ')') d--;
                else if (inner[k] == ',' && d == 0) { commaIdx = k; break; }
            }

            var name = (commaIdx >= 0 ? inner[..commaIdx] : inner).Trim();
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
                "hover" => MediaHoverStyles,
                "focus" => MediaFocusStyles,
                "active" => MediaActiveStyles,
                _ => MediaOverrides,
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
    /// Neutralises the NON-INHERITED properties a synthesized box (an anonymous table box, a
    /// generated-content box, a #text child split off an element) would otherwise pick up from
    /// the style object it borrows from its originating element. Such a box shares that object,
    /// so a parent's 'position: absolute' or 'float' would read back as its own and take it out
    /// of flow — an anonymous table inside an abs-pos box was skipped by its own parent's layout
    /// for exactly that reason. Inherited properties (colour, font, text-align, …) are left alone:
    /// a synthesized box is supposed to inherit those.
    /// <para>Only the properties that decide where a box goes and how big it is are reset.
    /// 'background' deliberately is not: an inline box's background is painted by the #text child
    /// that carries its content, so clearing it there would lose the background entirely.</para>
    /// </summary>
    public void ResetNonInheritedStyles()
    {
        foreach (var prop in NonInheritedResets)
            StyleOverrides[prop.Key] = prop.Value;
    }

    private static readonly (string Key, string Value)[] NonInheritedResets =
    [
        ("position", "static"), ("top", "auto"), ("right", "auto"), ("bottom", "auto"), ("left", "auto"),
        ("float", "none"), ("clear", "none"), ("z-index", "auto"), ("overflow", "visible"),
        ("width", "auto"), ("height", "auto"),
        ("min-width", "0"), ("min-height", "0"), ("max-width", "none"), ("max-height", "none"),
    ];

    /// <summary>
    /// The line-box fragments this inline node was broken into, when it produced more than one:
    /// each carries the rect it occupies and the slice of text drawn there. An inline box that
    /// spans several lines has one box per line, so its background and borders paint on each —
    /// with a single Box only the last fragment was painted.
    /// </summary>
    public List<(SKRect Rect, string Text)>? InlineFragments { get; set; }

    /// <summary>
    /// Per-line-box band for this text node when floats make the available width vary down the
    /// paragraph (CSS 2.1 §9.5): the absolute left edge and width of each successive line. Layout
    /// wraps against these, and the painter draws each line at its own X, so both agree. Null when
    /// no float shortens the text.
    /// </summary>
    public List<(float X, float Width)>? LineBands { get; set; }

    /// <summary>
    /// The element's 'font-size' as a computed length in px (CSS 2.1 §15.7), resolved once by the
    /// Parser against the parent's computed size. Descendants inherit this number, so a relative
    /// unit is never applied twice. Null on nodes the Parser did not build (JS-created elements),
    /// which fall back to resolving the style value.
    /// </summary>
    public float? ComputedFontSize { get; set; }

    /// <summary>
    /// CSS 2.1 §10.3.7 / §10.6.4 static position: the left/top MARGIN edge of the hypothetical
    /// box this element would have generated if its 'position' were 'static', in absolute layout
    /// coordinates. Recorded by the normal-flow pass (BoxEngine) and by FlexEngine as each
    /// out-of-flow child is passed over, and used by BoxEngine.ResolveAbsoluteBox when the
    /// corresponding offsets are 'auto'.
    /// </summary>
    public float? StaticX { get; set; }
    public float? StaticY { get; set; }

    public LayoutNode(string? id, string tagName, string text, ICssStyleDeclaration style, string? href = null)
    {
        if (!string.IsNullOrEmpty(id)) Attributes["id"] = id;
        TagName = tagName;
        Text = text;
        Style = style;
        if (href is not null) Attributes["href"] = href;
    }

    public void AddChild(LayoutNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}
