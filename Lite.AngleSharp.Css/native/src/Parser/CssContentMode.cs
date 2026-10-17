namespace AngleSharp.Css.Parser
{
    /// <summary>
    /// Lite patch 001: where a raw component-value run read by
    /// <see cref="CssTokenizer.ContentFrom(System.Int32, CssContentMode, out System.Boolean)"/> ends.
    /// </summary>
    public enum CssContentMode
    {
        /// <summary>A declaration value: ends at a top-level ';' or '}'.</summary>
        Value,
        /// <summary>An at-rule prelude: ends at a top-level ';', '{' or '}'.</summary>
        AtRulePrelude,
        /// <summary>A style rule (qualified rule) prelude: ends at a top-level '{' or '}' only.</summary>
        RulePrelude,
    }
}
