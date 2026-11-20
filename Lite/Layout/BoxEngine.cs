using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Computes BoxDimensions for every LayoutNode in the tree before painting.
/// Block elements stack vertically. Inline/inline-block elements flow horizontally in line boxes.
/// </summary>
internal static partial class BoxEngine
{
    /// <summary>Bumped once per layout pass: per-pass caches (the collapsed table borders)
    /// recompute after the tree or its styles may have changed.</summary>
    internal static int Generation { get; private set; }

    public static void Layout(LayoutNode root, float viewportWidth, float viewportHeight)
    {
        Generation++;
        // Establish the root (html) font-size so `rem` units resolve correctly this pass.
        var html = root.TagName == "HTML" ? root : root.Children.FirstOrDefault(c => c.TagName == "HTML") ?? root;
        CssUnits.RootFontSize = html.GetFontSize();

        StyleResolver.ResolvePendingCssWideKeywords(root);
        NormalizeBlockInInline(root);
        NormalizeInteractive(root);
        NormalizeTableBoxes(root);

        LayoutBlock(root, 0, 0, viewportWidth, viewportWidth, viewportHeight, viewportHeight); // root: margin-box discarded
        // Second pass: lay out all absolute/fixed nodes now that normal-flow boxes are finalised.
        // With no positioned ancestor, an absolutely positioned box is contained by the initial
        // containing block (CSS 2.1 §10.1), which is the viewport, not the root element's box.
        var initialContainingBlock = new BoxDimensions { ContentBox = new SKRect(0, 0, viewportWidth, viewportHeight) };
        // The ROOT element can itself be positioned (html style="position:absolute") — §10.1: its
        // containing block is the initial containing block. The positioned pass below only
        // re-resolves absolute/fixed DESCENDANTS, so the root resolves here.
        if (root.GetPosition() is PositionType.Absolute or PositionType.Fixed)
            ResolveAbsoluteBox(root, initialContainingBlock, viewportWidth, viewportHeight);
        LayoutPositioned(root, root.IsPositioned() ? root.Box : initialContainingBlock, viewportWidth, viewportHeight);
    }
}
