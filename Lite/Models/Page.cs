using AngleSharp.Dom;
using Lite.Layout;
using Lite.Scripting;

namespace Lite.Models;

/// <summary>
/// A browsing context's rendered document: its layout tree, JS engine, and the per-page parse
/// state captured at load time. The top-level window owns one Page; each same-origin
/// <c>&lt;iframe&gt;</c> owns a nested child Page (stored on its <see cref="LayoutNode.ChildPage"/>).
///
/// Child pages are still parsed using save/restore of Parser's construction state. Runtime
/// stylesheet matching and URL resolution use the owning engine's DocumentState.
/// </summary>
internal sealed class Page
{
    public required LayoutNode Root { get; init; }
    public required JsEngine Engine { get; init; }
    public IDocument? Document { get; init; }
    internal bool IsInitialAboutBlank { get; set; }
    public string? BaseUrl { get; init; }
    public int ViewportWidth { get; init; }
    public int ViewportHeight { get; init; }

    /// <summary>This page's own scroll/viewport state (independent of the parent's).</summary>
    public Viewport Viewport { get; } = new();
}
