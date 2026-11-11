namespace AngleSharp.Dom
{
    using AngleSharp.Html.Dom;
    using AngleSharp.Text;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provides a number of methods for performing operations that are
    /// independent of any particular instance of the DOM.
    /// </summary>
    sealed class DomImplementation : IImplementation
    {
        #region Features

        static readonly Dictionary<String, String[]> features = new(StringComparer.OrdinalIgnoreCase)
        {
            { "XML", new[] { "1.0", "2.0" } },
            { "HTML", new[] { "1.0", "2.0" } },
            { "Core", new[] { "2.0" } },
            { "Views", new[] { "2.0" } },
            { "StyleSheets", new[] { "2.0" } },
            { "CSS", new[] { "2.0" } },
            { "CSS2", new[] { "2.0" } },
            { "Traversal", new[] { "2.0" } },
            { "Events", new[] { "2.0" } },
            { "UIEvents", new[] { "2.0" } },
            { "HTMLEvents", new[] { "2.0" } },
            { "Range", new[] { "2.0" } },
            { "MutationEvents", new[] { "2.0" } },
        };

        #endregion

        #region Fields

        readonly Document _owner;

        #endregion

        #region ctor

        public DomImplementation(Document owner)
        {
            _owner = owner;
        }

        #endregion

        #region Methods

        public IDocumentType CreateDocumentType(String qualifiedName, String publicId, String systemId)
        {
            if (qualifiedName is null)
            {
                throw new ArgumentNullException(nameof(qualifiedName));
            }

            if (!qualifiedName.IsXmlName())
            {
                throw new DomException(DomError.InvalidCharacter);
            }

            if (!qualifiedName.IsQualifiedName())
            {
                throw new DomException(DomError.Namespace);
            }

            return new DocumentType(_owner, qualifiedName) 
            { 
                PublicIdentifier = publicId, 
                SystemIdentifier = systemId 
            };
        }

        public IDocument CreateHtmlDocument(String title)
        {
            var document = new HtmlDocument();
            document.AppendChild(new DocumentType(document, TagNames.Html));
            document.AppendChild(document.CreateElement(TagNames.Html));
            document.DocumentElement.AppendChild(document.CreateElement(TagNames.Head));

            if (!String.IsNullOrEmpty(title))
            {
                var titleElement = document.CreateElement(TagNames.Title);
                titleElement.AppendChild(document.CreateTextNode(title));
                document.Head!.AppendChild(titleElement);
            }

            document.DocumentElement.AppendChild(document.CreateElement(TagNames.Body));
            document.BaseUrl = _owner.BaseUrl;
            return document;
        }

        public IDocument CreateDocument(String? namespaceUri, String qualifiedName, IDocumentType? doctype)
        {
            // DOM §4.5 createDocument: a new document holding the doctype (if any) and a document
            // element created by createElement in the requested namespace. An empty qualified name
            // creates no document element. The document is an XML document (case-sensitive names,
            // no HTML whitespace/quirks rules), which is what the createElement namespace rules
            // observe.
            var document = new HtmlDocument();
            // DOM §4.5: the created document's content type follows the requested namespace —
            // application/xhtml+xml for the HTML namespace, image/svg+xml for SVG, and
            // application/xml otherwise.
            document.SetContentType(namespaceUri switch
            {
                "http://www.w3.org/1999/xhtml" => "application/xhtml+xml",
                "http://www.w3.org/2000/svg" => "image/svg+xml",
                _ => "application/xml",
            });
            if (doctype is not null)
            {
                document.AppendChild((DocumentType)doctype);
            }
            if (!String.IsNullOrEmpty(qualifiedName))
            {
                document.AppendChild(document.CreateElement(namespaceUri, qualifiedName));
            }
            document.BaseUrl = _owner.BaseUrl;
            return document;
        }

        public Boolean HasFeature(String feature, String? version = null)        {
            if (feature is null)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            if (features.TryGetValue(feature, out var versions))
            {
                return versions.Contains(version ?? String.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        #endregion
    }
}
