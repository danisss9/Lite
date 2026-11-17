namespace AngleSharp.Dom
{
    using System;

    /// <summary>
    /// Represents a CDATA section node (XML documents only).
    /// </summary>
    sealed class CDataSection : TextNode, ICDataSection
    {
        #region ctor

        internal CDataSection(Document owner)
            : this(owner, String.Empty)
        {
        }

        internal CDataSection(Document owner, String text)
            : base(owner, "#cdata-section", NodeType.CharacterData, text)
        {
        }

        #endregion
    }
}
