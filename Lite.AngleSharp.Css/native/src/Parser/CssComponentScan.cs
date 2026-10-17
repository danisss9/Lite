namespace AngleSharp.Css.Parser
{
    using AngleSharp.Text;
    using System;

    /// <summary>
    /// Lite patch 001: exposes the CSS 2.1 section 4.2 component scan to hosts that read
    /// declarations from stylesheet text themselves, so they reject exactly what the parser
    /// rejects.
    /// </summary>
    public static class CssComponentScan
    {
        /// <summary>
        /// True when the text is one well-formed declaration value: no BAD_STRING or BAD_URI,
        /// and no top-level ';' or '}' cutting it short.
        /// </summary>
        public static Boolean IsWellFormedValue(String value)
        {
            var trimmed = value.Trim();

            if (trimmed.Length == 0)
            {
                return false;
            }

            var tokenizer = new CssTokenizer(new TextSource(trimmed));
            var first = tokenizer.Get();
            var content = tokenizer.ContentFrom(first.Position.Position, CssContentMode.Value, out var bad);
            return !bad && content.Length == trimmed.Length;
        }
    }
}
