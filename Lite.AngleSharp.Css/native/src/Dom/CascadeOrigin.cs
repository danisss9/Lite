#nullable enable
namespace AngleSharp.Css.Dom
{
    using AngleSharp.Dom;
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Lite patch 004: CSS 2.1 §6.4.1 cascade origins. Upstream sorts every matching rule by
    /// specificity alone, so the default style sheet and a host's user-agent or user sheet
    /// (style elements the host inserts) competed with author rules as if they were author
    /// rules. A host marks the style elements carrying its UA and user sheets; rules then sort
    /// by origin before specificity: default sheet, user agent, user, author.
    /// </summary>
    public static class CascadeOrigin
    {
        /// <summary>The built-in default style sheet (no owner node).</summary>
        public const Int32 Default = 0;
        /// <summary>A host user-agent style sheet.</summary>
        public const Int32 UserAgent = 1;
        /// <summary>A host user style sheet.</summary>
        public const Int32 User = 2;
        /// <summary>Every other sheet.</summary>
        public const Int32 Author = 3;

        private static readonly ConditionalWeakTable<IElement, object> _marked = new();

        /// <summary>Marks the style element whose sheet belongs to <paramref name="origin"/>.</summary>
        public static void Mark(IElement ownerNode, Int32 origin) => _marked.AddOrUpdate(ownerNode, origin);

        internal static Int32 Of(ICssRule rule)
        {
            var sheet = rule.Owner;
            while (sheet?.Parent is { } parent) sheet = parent;
            if (sheet is null) return Author;
            if (sheet.OwnerNode is not { } node) return sheet.OwnerRule is null ? Default : Author;
            return _marked.TryGetValue(node, out var origin) ? (Int32)origin : Author;
        }
    }
}
