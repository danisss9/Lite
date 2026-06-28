using AngleSharp.Dom;
using Lite.Layout;
using Lite.Scripting;

namespace Lite.Models;

/// <summary>
/// A browsing context's rendered document: its layout tree, JS engine, and the per-page parse
/// state captured at load time. The top-level window owns one Page; each same-origin
/// <c>&lt;iframe&gt;</c> owns a nested child Page (stored on its <see cref="LayoutNode.ChildPage"/>).
///
/// Bundling this state is the first step toward replacing the Parser/Drawer/JsEngine static
/// singletons (which only allow one page at a time). For now child pages are produced by
/// <see cref="Parser"/> via save/restore of those statics, so a child's *initial* render is fully
/// correct (its cascade ran against its own stylesheets during parse); only runtime restyle that
/// reads the static cascade (StyleResolver class changes) falls back to the active page's rules.
/// </summary>
internal sealed class Page
{
    public required LayoutNode Root { get; init; }
    public required JsEngine Engine { get; init; }
    public IDocument? Document { get; init; }
    public string? BaseUrl { get; init; }
    public int ViewportWidth { get; init; }
    public int ViewportHeight { get; init; }

    /// <summary>This page's own scroll/viewport state (independent of the parent's).</summary>
    public Viewport Viewport { get; } = new();
}
