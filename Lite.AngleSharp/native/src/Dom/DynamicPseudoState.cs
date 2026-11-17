namespace AngleSharp.Dom
{
    using System;

    /// <summary>
    /// The live user-interaction state behind the dynamic pseudo-classes (:hover, :active,
    /// :focus). AngleSharp has no renderer, so a host that tracks the pointer and focus
    /// installs the probes; selector matching then answers from that state, which is what lets
    /// a combinator rule such as <c>textarea:focus + span</c> match the span while the textarea
    /// has focus. <see cref="AssumingAll"/> answers true for every dynamic pseudo-class on the
    /// current thread: the host's cascade uses it to find the rules that could ever apply to
    /// an element, then re-matches them against the live state at style resolution.
    /// </summary>
    public static class DynamicPseudoState
    {
        [ThreadStatic] private static Int32 _assumeAll;

        /// <summary>The host's :hover probe (the pointer is over the element or a descendant).</summary>
        public static Func<IElement, Boolean>? Hover { get; set; }

        /// <summary>The host's :active probe (the element or a descendant is being activated).</summary>
        public static Func<IElement, Boolean>? Active { get; set; }

        /// <summary>The host's :focus probe, consulted in addition to the DOM's own focus flag.</summary>
        public static Func<IElement, Boolean>? Focus { get; set; }

        /// <summary>Whether every dynamic pseudo-class currently matches on this thread.</summary>
        public static Boolean AssumeAll => _assumeAll > 0;

        /// <summary>Makes every dynamic pseudo-class match on this thread until disposed.</summary>
        public static IDisposable AssumingAll()
        {
            _assumeAll++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private Boolean _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _assumeAll--;
            }
        }
    }
}
