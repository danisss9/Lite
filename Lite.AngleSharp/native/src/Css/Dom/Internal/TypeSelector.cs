namespace AngleSharp.Css.Dom
{
    using AngleSharp.Dom;
    using AngleSharp.Text;
    using System;

    sealed class TypeSelector : ISelector
    {
        private readonly String _type;

        public TypeSelector(String type)
        {
            _type = type;
        }

        /// <summary>
        /// Gets the raw type name value
        /// </summary>
        internal String TypeName => _type;

        public Priority Specificity => Priority.OneTag;

        public String Text => CssUtilities.Escape(_type);

        public void Accept(ISelectorVisitor visitor) => visitor.Type(_type);

        public Boolean Match(IElement element, IElement? scope)
        {
            // CSS 2.1 §4.1.3 (via the Selectors type-selector rule): element selectors are
            // case-insensitive only for HTML elements in HTML documents; in XML documents
            // (XHTML served as application/xhtml+xml) the document language is case-sensitive
            // and 'DIV' must not match <div> (case-sensitive-007).
            var ignoreCase = element is AngleSharp.Html.Dom.IHtmlElement;
            return ignoreCase ? _type.Isi(element.LocalName) : _type.Is(element.LocalName);
        }
    }
}
