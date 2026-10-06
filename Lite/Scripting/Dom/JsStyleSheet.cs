using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>CSSOM surface for one style sheet: cssRules (style and media rules), disabled,
/// and ownerNode. A rule's style mutation refreshes the engine's collected rule view and
/// recascades the document, so dynamic rule edits restyle matching elements immediately.</summary>
public class JsStyleSheet(Engine engine, ICssStyleSheet sheet)
{
    private ICssStyleSheet Sheet => sheet;

    public bool disabled
    {
        get => Sheet.IsDisabled;
        set
        {
            if (Sheet.IsDisabled == value) return;
            Sheet.IsDisabled = value;
            var owner = Sheet.OwnerNode as IElement;
            if (owner is not null) Parser.OnStyleSheetToggled(owner);
            JsEngine.For(engine)?.RecascadeAll();
        }
    }

    public JsElement? ownerNode => Sheet.OwnerNode is IElement owner && JsEngine.For(engine)?.DocumentState is { } state
        ? JsElement.For(engine, state.ForDomNode(owner))
        : null;

    public JsCssRuleList cssRules => new(engine, Sheet.Rules);

    public int insertRule(string rule, int index) => 0; // parsing new rules dynamically is out of scope
    public void deleteRule(int index) { }
}

/// <summary>A StyleSheetList over the document's sheets.</summary>
public class JsStyleSheetList(Engine engine, Func<IEnumerable<ICssStyleSheet>> sheets)
{
    private IReadOnlyList<JsStyleSheet> Snapshot() =>
        sheets().Select(s => new JsStyleSheet(engine, s)).ToList();

    public int length => Snapshot().Count;
    public JsStyleSheet? this[int index] => index >= 0 && index < Snapshot().Count ? Snapshot()[index] : null;
    public JsStyleSheet? item(int index) => this[index];
}

/// <summary>A CSSRuleList over one sheet's rules.</summary>
public class JsCssRuleList(Engine engine, ICssRuleList rules)
{
    private IReadOnlyList<JsCssRule> Snapshot() =>
        rules.OfType<ICssRule>().Select(r => JsCssRule.Wrap(engine, r)).ToList();

    public int length => Snapshot().Count;
    public JsCssRule? this[int index] => index >= 0 && index < Snapshot().Count ? Snapshot()[index] : null;
    public JsCssRule? item(int index) => this[index];
}

/// <summary>A CSSStyleRule (or media rule) wrapper. Writing through style refreshes the rule
/// view and recascades, so the mutation restyles every matching element.</summary>
public class JsCssRule
{
    private readonly ICssRule _rule;
    private readonly Engine _engine;
    private readonly JsRuleStyle? _style;

    private JsCssRule(Engine engine, ICssRule rule)
    {
        _rule = rule;
        _engine = engine;
        if (rule is ICssStyleRule styleRule)
            _style = new JsRuleStyle(styleRule.Style, () => OnMutated());
    }

    internal static JsCssRule Wrap(Engine engine, ICssRule rule) => new(engine, rule);

    public string selectorText => _rule is ICssStyleRule style ? style.SelectorText : "";
    public string cssText => _rule.CssText;

    public JsRuleStyle? style => _rule is ICssStyleRule ? _style : null;

    public JsCssRuleList? cssRules => _rule is ICssMediaRule media ? new JsCssRuleList(_engine, media.Rules) : null;

    private void OnMutated()
    {
        if (_rule.Owner?.OwnerNode is IElement owner)
            Parser.OnStyleSheetToggled(owner);
        JsEngine.For(_engine)?.RecascadeAll();
    }
}

/// <summary>A rule's declaration block: the same named properties as JsStyle, writing into the
/// CSSOM declaration and notifying the owning stylesheet on every mutation.</summary>
public class JsRuleStyle(ICssStyleDeclaration declaration, Action onMutate)
{
    private string Get(string prop) => declaration.GetPropertyValue(prop) ?? "";
    private void Set(string prop, string value)
    {
        declaration.SetProperty(prop, value);
        onMutate();
    }

    public string cssText
    {
        get => declaration.CssText;
        set { declaration.CssText = value; onMutate(); }
    }

    public string display { get => Get("display"); set => Set("display", value); }
    public string color { get => Get("color"); set => Set("color", value); }
    public string backgroundColor { get => Get("background-color"); set => Set("background-color", value); }
    public string visibility { get => Get("visibility"); set => Set("visibility", value); }
    public string position { get => Get("position"); set => Set("position", value); }
    public string margin { get => Get("margin"); set => Set("margin", value); }
    public string padding { get => Get("padding"); set => Set("padding", value); }
    public string width { get => Get("width"); set => Set("width", value); }
    public string height { get => Get("height"); set => Set("height", value); }
    public string fontSize { get => Get("font-size"); set => Set("font-size", value); }
    public string fontWeight { get => Get("font-weight"); set => Set("font-weight", value); }
    public string fontFamily { get => Get("font-family"); set => Set("font-family", value); }
    public string textAlign { get => Get("text-align"); set => Set("text-align", value); }
    public string lineHeight { get => Get("line-height"); set => Set("line-height", value); }
    public string border { get => Get("border"); set => Set("border", value); }
    public string overflow { get => Get("overflow"); set => Set("overflow", value); }
}
