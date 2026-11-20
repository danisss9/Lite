using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Rendering;
using SkiaSharp;

namespace Lite;

internal static partial class Drawer
{
    private static List<HitRegion> _hitRegions = [];
    /// <summary>In-flow atomic inline boxes (inline-block/table/flex) deferred by the current
    /// stacking-context root so they paint in Appendix-E step 5 (above in-flow block backgrounds).
    /// While non-null, <see cref="PaintNode"/> skips these during the block pass; the SC root paints
    /// them afterward. Saved/restored around each SC root's block pass so nesting is isolated.</summary>
    private static HashSet<LayoutNode>? _deferredInlines;
    /// <summary>Maps HitRegion index → select option index for dropdown options.</summary>
    internal static Dictionary<int, int> SelectOptionMap { get; } = [];
    /// <summary>Deferred dropdown to draw on top of everything.</summary>
    private static (LayoutNode node, SKRect rect, string selectedVal)? _pendingDropdown;
    /// <summary>Current viewport scroll Y — used for position:sticky calculations.</summary>
    private static float _viewportScrollY;
    /// <summary>Current viewport height — used for position:sticky calculations.</summary>
    private static float _viewportHeight;
    /// <summary>Current viewport width — used for fixed background-attachment positioning.</summary>
    private static float _viewportWidth;
    /// <summary>The accumulated SCROLLABLE-BOX scroll offsets of the paint context (the sum of
    /// every enclosing overflow:scroll element's scroll translate). A fixed background attaches
    /// to the VIEWPORT (§14.2.1) — its placement compensates this sum so scrolling an ancestor
    /// box does not drag the image (background-027's cat moved with the box's scroll).</summary>
    private static float _bgFixedScrollX;
    private static float _bgFixedScrollY;
    /// <summary>The BODY whose background propagated to the canvas (§14.2) — it paints no
    /// background of its own this frame. Null when the root painted its own background.</summary>
    private static LayoutNode? _propagatedBackgroundNode;

    /// <summary>Bitmap backing the pixel pointer returned by <see cref="Draw"/>; kept alive
    /// until the next Draw call so the Win32 blit never reads freed memory.</summary>
    private static SKBitmap? _lastBitmap;

    /// <summary>The device scale of the DrawToBitmap render in progress: nested iframes inherit
    /// it so every browsing context rasterizes at the same device resolution. Saved/restored
    /// around the recursive call like the other paint statics (see PaintIframe).</summary>
    private static float _renderScale = 1f;

    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, LayoutNode root, Viewport viewport)
    {
        _lastBitmap?.Dispose();
        _lastBitmap = DrawToBitmap(width, height, root, viewport);
        return (_lastBitmap.GetPixels(), _hitRegions);
    }

    /// <summary>The DOM body in layout order. The table normalizers can wrap BODY in anonymous
    /// boxes (html display:table), so the propagation follows the DOM past layout wrappers —
    /// §14.2 propagates the first HTML BODY child of the root element, whatever holds it.</summary>
    private static LayoutNode? FindBody(LayoutNode root)
    {
        foreach (var child in root.Children)
        {
            if (child.TagName == "BODY") return child;
            if (child.TagName == "HEAD") continue;
            var found = FindBody(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Renders the page to a bitmap the caller owns. Used by the conformance
    /// harness for reftest pixel comparison. <paramref name="scale"/> is the device pixel
    /// ratio: the layout runs in CSS pixels at <paramref name="width"/>×<paramref name="height"/>
    /// and the bitmap rasterizes at scale× that size (a dpr option asks the harness for a
    /// device-scaled render; 1 is the live-window default).</summary>
    internal static SKBitmap DrawToBitmap(int width, int height, LayoutNode root, Viewport viewport, float scale = 1f)
    {
        // Dynamic pseudo-class rules re-match once per frame: the DOM may have changed under
        // a cached match (a class toggled by script) even when the interaction state did not.
        Interaction.PseudoClassState.Invalidate();
        // Layout pass — compute node.Box for every node
        BoxEngine.Layout(root, width, height);
        // Nested iframes inherit the device scale (read by PaintIframe during PaintNode);
        // every DrawToBitmap entry rewrites it, so no restore is needed on the way out.
        _renderScale = scale;

        var imageInfo = new SKImageInfo(Math.Max(1, (int)MathF.Ceiling(width * scale)),
            Math.Max(1, (int)MathF.Ceiling(height * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);

        // Transparency is alpha 0 whatever the RGB: "transparent" computes to rgba(0, 0, 0, 0),
        // which never equalled SKColors.Transparent (#00FFFFFF) — so a transparent root never
        // propagated the body background and the canvas was cleared to nothing (background-root-*).
        // Propagate the root/body background to the viewport canvas (CSS 2.1 §14.2). When neither
        // sets a background the canvas is the UA default — white, as in browsers (not a grey wash).
        // When the root is fully transparent, the BODY's background — color AND image — becomes
        // the canvas background, positioned relative to the ROOT's padding box, and the body
        // paints no background of its own (its own box would place the image wrong).
        var body = FindBody(root) ?? root;
        _propagatedBackgroundNode = null;
        var clearColor = root.GetBackgroundColor();
        if (clearColor.Alpha == 0 && root.GetBackgroundImage() == null)
        {
            clearColor = body.GetBackgroundColor();
            _propagatedBackgroundNode = body != root ? body : null;
        }
        if (clearColor.Alpha == 0) clearColor = SKColors.White;
        canvas.Clear(clearColor);
        // Device scale applies to every paint below — content, dropdown overlay, fixed nodes
        // and the scrollbar all rasterize at the scaled resolution (CSS coordinates unchanged).
        canvas.Scale(scale, scale);
        _hitRegions = [];
        // §18.1 with §14.2: the canvas beyond the root box belongs to the root element, so it
        // shows the root's cursor. Bottom-most and cursor-only (clicks there hit nothing, as
        // before), bounded to this frame's own canvas.
        _hitRegions.Add(CursorRegion(new SKRect(0, 0, Math.Max(width + viewport.ScrollX, root.Box.MarginBox.Right),
            Math.Max(height + viewport.ScrollY, root.Box.MarginBox.Bottom)), root) with { CursorOnly = true });
        SelectOptionMap.Clear();
        _pendingDropdown = null;
        _viewportScrollY = viewport.ScrollY;
        _viewportHeight = height;
        _viewportWidth = width;
        // A paint that throws mid-node (or any future early return) must not leak the previous
        // render's scrollable-box offsets into this one: the fixed-background placement would
        // land in mid-air (background-027-class cases render per test in a shared worker).
        _bgFixedScrollX = 0f;
        _bgFixedScrollY = 0f;

        canvas.Save();
        canvas.Translate(-viewport.ScrollX, -viewport.ScrollY);

        // The propagated background image paints at the root's padding box (its positioning area
        // is the canvas's origin), scrolling with the document, under all content.
        // §14.2: the canvas background "covers the entire canvas" — positioned against the root
        // box, painted (and repeated) over the whole page, not clipped to the root's box
        // (abspos-zero-width-001's 2000px image under a short root).
        var (canvasRight, canvasBottom) = BoxEngine.PageExtent(root);
        _canvasRoot = root;
        _canvasArea = new SKRect(MathF.Min(0f, root.Box.BorderBox.Left), MathF.Min(0f, root.Box.BorderBox.Top),
            MathF.Max(canvasRight, width + viewport.ScrollX), MathF.Max(canvasBottom, height + viewport.ScrollY));
        if (_propagatedBackgroundNode is { } bgNode && bgNode.GetBackgroundImage() != null)
            DrawBackgroundImage(canvas, bgNode, root.Box, _canvasArea);

        PaintNode(canvas, root, width);

        // Draw deferred dropdown overlay on top of all content (but within scroll context)
        if (_pendingDropdown is var (ddNode, ddRect, ddSelected))
            DrawDropdownOverlay(canvas, ddNode, ddRect, ddSelected);

        canvas.Restore();

        // Paint position:fixed nodes after restoring scroll so they stay on screen
        PaintFixedNodes(canvas, root, width);
        AddWrappedAnchorRegions(root);

        // §11.1.1: 'overflow: hidden' propagated to the viewport clips the canvas there — the
        // page offers no scrolling mechanism, so it has no scroll extent and draws no bar.
        // The page scrolls over everything it paints, absolutely positioned content included,
        // on both axes (BoxEngine.PageExtent).
        if (BoxEngine.ViewportOverflow(root) == OverflowType.Hidden)
        {
            viewport.ContentHeight = Math.Min(root.Box.MarginBox.Bottom, height);
            viewport.ContentWidth = Math.Min(root.Box.MarginBox.Right, width);
        }
        else
        {
            var (pageRight, pageBottom) = BoxEngine.PageExtent(root);
            viewport.ContentHeight = pageBottom;
            viewport.ContentWidth = pageRight;
        }
        DrawScrollbar(canvas, viewport, width, height);

        return bitmap;
    }

    /// <summary>Block children of an inline anchor can be hoisted into anonymous layout boxes.
    /// The anchor then has no box of its own, so use the rendered descendants as its hit area.</summary>
    private static void AddWrappedAnchorRegions(LayoutNode root)
    {
        var pending = new Stack<LayoutNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node.Href is not null && node.Box.BorderBox.IsEmpty && node.GetDisplay() != DisplayType.None)
            {
                var descendants = new Stack<LayoutNode>();
                foreach (var child in node.Children) descendants.Push(child);
                while (descendants.TryPop(out var child))
                {
                    if (child.GetDisplay() == DisplayType.None) continue;
                    if (!child.Box.BorderBox.IsEmpty)
                        _hitRegions.Add(CursorRegion(child.Box.BorderBox, node, node.Href, node.NodeKey));
                    foreach (var grandchild in child.Children) descendants.Push(grandchild);
                }
            }
            foreach (var child in node.Children) pending.Push(child);
        }
    }

    // -------------------------------------------------------------------------
    // Scrollbar
    // -------------------------------------------------------------------------

    private static void DrawScrollbar(SKCanvas canvas, Viewport viewport, int width, int height)
    {
        if (viewport.HideScrollbars) return;
        const float barWidth = 6f;
        const float margin = 2f;
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true };
        if (viewport.ContentHeight > viewport.ViewportHeight)
        {
            var ratio = viewport.ViewportHeight / viewport.ContentHeight;
            var trackH = viewport.ViewportHeight;
            var thumbH = Math.Max(trackH * ratio, 24f);
            var thumbTop = viewport.ScrollY / viewport.ContentHeight * trackH;
            var x = width - barWidth - margin;
            canvas.DrawRoundRect(x, thumbTop + margin, barWidth, thumbH - margin * 2, 3, 3, paint);
        }
        // §11.1.1: content overflowing the viewport sideways gets a horizontal scrolling
        // mechanism too (c5505-mrgn-002: "This page should have a horizontal scroll bar").
        if (viewport.ContentWidth > width + 0.5f)
        {
            var ratio = width / viewport.ContentWidth;
            var thumbW = Math.Max(width * ratio, 24f);
            var thumbLeft = viewport.ScrollX / viewport.ContentWidth * width;
            var y = height - barWidth - margin;
            canvas.DrawRoundRect(thumbLeft + margin, y, thumbW - margin * 2, barWidth, 3, 3, paint);
        }
    }

    /// <summary>Whether the box always shows its scrolling mechanism: 'overflow: scroll' on an
    /// element box (the root's and body's 'overflow' belongs to the viewport, §11.1.1).</summary>
    private static bool HasScrollMechanism(LayoutNode node) =>
        node.TagName is not ("HTML" or "BODY") && !node.TagName.StartsWith('#') &&
        node.GetOverflow() == OverflowType.Scroll && node.Box.PaddingBox.Width > 0 && node.Box.PaddingBox.Height > 0;

    /// <summary>The opaque trough a scroll box paints in each shown scrollbar's gutter.</summary>
    internal static readonly SKColor ScrollTrough = new(0xF0, 0xF0, 0xF0);

    private static void DrawElementScrollbar(SKCanvas canvas, LayoutNode node, ElementScrollState ss, bool always = false)
    {
        // Each shown bar (BoxEngine.ShownScrollbars: 'scroll' both, 'auto' an overflowing
        // axis's — a horizontal-only overflow grows no phantom vertical bar) owns its gutter
        // (§11.1.1: its space is taken out of the box's containing block, BoxEngine.ScrollGutter):
        // an opaque trough with a track, which the content (clipped to the scrollport) never
        // covers. The thumb marks an axis that can actually scroll, so a track without one is
        // an inactive scrollbar.
        var box = node.Box.PaddingBox;
        var barW = ElementScrollState.BarWidth;
        var barM = ElementScrollState.BarMargin;
        var (vertical, horizontal) = BoxEngine.ShownScrollbars(node);
        if (!vertical && !horizontal && !always) return;
        var gutter = BoxEngine.ScrollGutter;
        using (var trough = new SKPaint { Color = ScrollTrough, IsAntialias = false })
        {
            if (vertical)
                canvas.DrawRect(SKRect.Create(box.Right - gutter, box.Top, gutter, box.Height), trough);
            if (horizontal)
                canvas.DrawRect(SKRect.Create(box.Left, box.Bottom - gutter,
                    Math.Max(0f, box.Width - (vertical ? gutter : 0f)), gutter), trough);
        }
        using (var track = new SKPaint { Color = new SKColor(0, 0, 0, 28), IsAntialias = true })
        {
            if (vertical)
                canvas.DrawRoundRect(box.Right - barW - barM, box.Top + barM, barW, Math.Max(0f, box.Height - barW - barM * 3), 3, 3, track);
            if (horizontal)
                canvas.DrawRoundRect(box.Left + barM, box.Bottom - barW - barM, Math.Max(0f, box.Width - barW - barM * 3), barW, 3, 3, track);
        }
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true };
        if (ss.NeedsScrollbar)
        {
            var thumbH = ss.ThumbHeight;
            var thumbTop = ss.ThumbTop(box.Top);
            canvas.DrawRoundRect(box.Right - barW - barM, thumbTop + barM, barW, thumbH - barM * 2, 3, 3, paint);
        }
        if (ss.NeedsHorizontalScrollbar)
        {
            var thumbW = ss.ContentWidth > 0
                ? Math.Max(20f, box.Width * (box.Width / ss.ContentWidth))
                : 0f;
            var trackW = Math.Max(0f, box.Width - thumbW);
            var thumbLeft = ss.MaxScrollX > 0 ? box.Left + trackW * (ss.ScrollX / ss.MaxScrollX) : box.Left;
            canvas.DrawRoundRect(thumbLeft, box.Bottom - barW - barM, Math.Max(thumbW - barM * 2, barW), barW, 3, 3, paint);
        }
    }

    // -------------------------------------------------------------------------
    // Paint tree
    // -------------------------------------------------------------------------

    private static void PaintFixedNodes(SKCanvas canvas, LayoutNode root, int viewportWidth)
    {
        // The ROOT can itself be position:fixed (abspos-containing-block-initial-004b): the normal
        // tree pass skipped it, so it must be painted here too.
        if (root.GetPosition() == PositionType.Fixed)
        {
            PaintNodeInner(canvas, root, viewportWidth);
            return;
        }
        var stack = new Stack<LayoutNode>();
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        for (int i = root.Children.Count - 1; i >= 0; i--)
            stack.Push(root.Children[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.GetPosition() == PositionType.Fixed)
                // PaintNodeInner, not PaintNode: PaintNode skips every fixed box so the normal
                // tree pass leaves them for this one. Routing back through it made this pass a
                // no-op, so a position:fixed box was laid out correctly and then never painted.
                PaintNodeInner(canvas, node, viewportWidth);
            else
                for (int i = node.Children.Count - 1; i >= 0; i--)
                    stack.Push(node.Children[i]);
        }
    }

    private static void PaintNode(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display = node.GetDisplay();
        if (display == DisplayType.None) return;

        // Deferred to Appendix-E step 5 by an ancestor stacking-context root — painted later, above
        // in-flow block backgrounds. (Skipped only during the block pass, when the set is active.)
        if (_deferredInlines != null && _deferredInlines.Contains(node)) return;

        // Skip fixed nodes in the normal tree pass — painted separately after scroll restore
        if (node.GetPosition() == PositionType.Fixed) return;

        // A block hoisted out of a link broken around it (§9.2.1.1) is still link content.
        if (node.TagName != "A" && SplitLink(node) is { } link)
            _hitRegions.Add(CursorRegion(node.Box.BorderBox, link, link.Href, link.NodeKey));

        // position:relative — translate by offset, paint normally, then restore
        var pos = node.GetPosition();
        if (pos == PositionType.Relative)
        {
            var (dx, dy) = BoxEngine.RelativeOffset(node);
            if (dx != 0f || dy != 0f)
            {
                var hitStart = _hitRegions.Count;
                canvas.Save();
                canvas.Translate(dx, dy);
                PaintNodeInner(canvas, node, viewportWidth);
                canvas.Restore();
                // PaintNodeInner adds hit regions in layout coordinates. Move every region
                // created by this subtree to its visible position, including nested links.
                for (var i = hitStart; i < _hitRegions.Count; i++)
                {
                    var region = _hitRegions[i];
                    var box = region.Bounds;
                    _hitRegions[i] = region with
                    {
                        Bounds = new SKRect(box.Left + dx, box.Top + dy, box.Right + dx, box.Bottom + dy)
                    };
                }
                return;
            }
        }

        // position:sticky — behaves like relative but offset is clamped to viewport scroll
        if (pos == PositionType.Sticky)
        {
            var fontSize = node.GetFontSize();
            var stickyTop = node.GetOffsetTop(node.Box.ContentBox.Height, fontSize);
            if (!float.IsNaN(stickyTop))
            {
                // Node's layout position in content coordinates
                var nodeY = node.Box.MarginBox.Top;
                // Target position in viewport: viewportScrollY + stickyTop
                var targetY = _viewportScrollY + stickyTop;
                var dy = 0f;
                if (nodeY < targetY)
                {
                    // Node has scrolled above its sticky threshold — push it down
                    dy = targetY - nodeY;
                    // Clamp to parent's bottom edge so sticky doesn't escape its container
                    if (node.Parent != null)
                    {
                        var parentBottom = node.Parent.Box.ContentBox.Bottom - node.Box.MarginBox.Height;
                        if (nodeY + dy > parentBottom) dy = Math.Max(0, parentBottom - nodeY);
                    }
                }
                if (dy > 0)
                {
                    canvas.Save();
                    canvas.Translate(0, dy);
                    PaintNodeInner(canvas, node, viewportWidth);
                    canvas.Restore();
                    return;
                }
            }
        }

        PaintNodeInner(canvas, node, viewportWidth);
    }

    private static void PaintNodeInner(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var opacity = node.GetOpacity();
        var overflow = node.GetOverflow();
        var clip = overflow == OverflowType.Hidden || overflow == OverflowType.Scroll || overflow == OverflowType.Auto;

        // CSS transform — apply around the node's transform-origin (default: center)
        var transform = node.GetTransform();
        if (transform != null)
        {
            canvas.Save();
            var box = node.Box.BorderBox;
            var ox = box.Left + box.Width / 2f;
            var oy = box.Top + box.Height / 2f;
            var m = SKMatrix.CreateTranslation(ox, oy);
            m = m.PostConcat(transform.Value);
            m = m.PostConcat(SKMatrix.CreateTranslation(-ox, -oy));
            canvas.Concat(in m);
        }

        // CSS filter — applied as a layer effect
        var filterPaint = BuildFilterPaint(node);
        if (filterPaint != null)
            canvas.SaveLayer(filterPaint);

        var (clipRx, clipRy) = clip
            ? node.GetBorderRadius(node.Box.PaddingBox.Width, node.Box.PaddingBox.Height)
            : (0f, 0f);

        // opacity < 1 — composite the subtree into a temporary layer at reduced alpha
        if (opacity < 1f)
        {
            using var alphaPaint = new SKPaint { Color = SKColors.Transparent.WithAlpha((byte)(opacity * 255)) };
            canvas.SaveLayer(alphaPaint);
        }
        else if (clip)
        {
            canvas.Save();
        }

        // CSS `clip: rect(...)` — clips an absolutely-positioned element to a rectangle
        // relative to its border box (CSS 2.1 §11.1.2), the element's own border included, so
        // it applies before the box's decorations; 'overflow' clips only what is inside.
        var cssClipRect = node.GetClipRect();
        bool cssClipped = cssClipRect is not null;
        if (cssClipped)
        {
            canvas.Save();
            canvas.ClipRect(cssClipRect!.Value);
        }

        // §11.2: an invisible box paints nothing of its own, but still scrolls/clips its
        // descendants (which may be visibility:visible). Keep the translate, drop the paint.
        var invisible = node.IsInvisible();
        // §11.1.1: 'overflow' clips the box's CONTENT to its padding box; the box's own
        // background, border and outline sit outside that clip. They used to paint inside
        // it, so a clipping box never showed its border (overflow-scrollbar-001's blue border).
        var predecorated = clip && !invisible && PaintsAsBlock(node);
        if (predecorated)
        {
            PaintBlockDecorations(canvas, node);
            _predecorated.Add(node);
        }

        if (clip)
        {
            if (clipRx > 0 || clipRy > 0)
            {
                var rr = new SKRoundRect(node.Box.PaddingBox, clipRx, clipRy);
                canvas.ClipRoundRect(rr, antialias: true);
            }
            else
            {
                canvas.ClipRect(node.Box.PaddingBox);
            }
        }

        // Apply per-element scroll offset
        var scrollState = node.ScrollState;
        if (scrollState != null && (scrollState.NeedsScrollbar || scrollState.NeedsHorizontalScrollbar))
        {
            // Draw background, borders, and shadows at their fixed layout position
            // before the scroll translate so they don't scroll away with the content.
            if (!invisible && !predecorated) PaintBlockDecorations(canvas, node);
            // The content scrolls within the scrollport: the padding box less the bars'
            // gutters (§11.1.1), so it never paints under its own scrollbars.
            canvas.Save();
            canvas.ClipRect(BoxEngine.ScrollportRect(node));
            canvas.Translate(-scrollState.ScrollX, -scrollState.ScrollY);
            // The content painted inside this translate (including fixed backgrounds of
            // descendants) compensates the box's scroll in its own placement (§14.2.1).
            _bgFixedScrollX += scrollState.ScrollX;
            _bgFixedScrollY += scrollState.ScrollY;
            PaintNodeContent(canvas, node, viewportWidth);
            _bgFixedScrollX -= scrollState.ScrollX;
            _bgFixedScrollY -= scrollState.ScrollY;
            canvas.Restore(); // undo the scroll offset and the scrollport clip for the bars
            if (!invisible) DrawElementScrollbar(canvas, node, scrollState, always: HasScrollMechanism(node));
        }
        else if (HasScrollMechanism(node))
        {
            canvas.Save();
            canvas.ClipRect(BoxEngine.ScrollportRect(node));
            PaintNodeContent(canvas, node, viewportWidth);
            canvas.Restore();
            // §11.1.1 'scroll': "if a scrolling mechanism is visible on the screen, it should be
            // displayed whether or not any of its content is clipped" — the bars of a box with
            // nothing to scroll are shown inactive (a track without a thumb).
            if (!invisible)
                DrawElementScrollbar(canvas, node, scrollState ?? new ElementScrollState(), always: true);
        }
        else
        {
            PaintNodeContent(canvas, node, viewportWidth);
        }

        if (predecorated) _predecorated.Remove(node);
        if (cssClipped) canvas.Restore();
        if (opacity < 1f || clip) canvas.Restore();
        if (filterPaint != null) { canvas.Restore(); filterPaint.Dispose(); }
        if (transform != null) canvas.Restore();
    }

    /// <summary>Boxes whose own decorations PaintNodeInner painted before their overflow clip;
    /// PaintBlock skips those decorations for them.</summary>
    private static readonly HashSet<LayoutNode> _predecorated = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether the box's content paint goes through <see cref="PaintBlock"/> (whose
    /// decorations are <see cref="PaintBlockDecorations"/>): a block container that is not a
    /// tag-dispatched replaced/form/embedded element, a table, a table-internal box or a
    /// list item (those paint their own decorations).</summary>
    private static bool PaintsAsBlock(LayoutNode node) =>
        !node.TagName.StartsWith('#') && node.TagName is not ("HTML" or "BODY" or "A" or "IMG" or "OBJECT" or "INPUT" or "BUTTON"
            or "TEXTAREA" or "SELECT" or "PROGRESS" or "METER" or "HR" or "IFRAME" or "VIDEO" or "AUDIO" or "SVG" or "CANVAS")
        && !node.CollapsedByColumn && node.CollapsedColumnClip is not { Count: > 0 }
        && node.GetDisplay() is DisplayType.Block or DisplayType.InlineBlock or DisplayType.Flex or DisplayType.InlineFlex;

    private static void PaintNodeContent(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display = node.GetDisplay();

        // §17.5.1: a cell whose every column is collapsed ('visibility: collapse' on a
        // column/column-group) is removed from the rendering — its background, borders, text
        // and descendants paint nothing, while the grid keeps its place.
        if (node.CollapsedByColumn) return;

        // §17.5.1 dynamic effects: a cell spanning a collapsed column paints only the visible
        // columns' slices — the intersecting slice is clipped out (the cell's own background
        // included). The full-collapsed case skips the paint entirely.
        var columnClip = node.CollapsedColumnClip;
        if (columnClip is { Count: > 0 })
        {
            canvas.Save();
            var borderLeft = node.Box.BorderBox.Left;
            var top = node.Box.BorderBox.Top;
            var bottom = node.Box.BorderBox.Bottom;
            canvas.ClipRect(new SKRect(borderLeft, top, node.Box.BorderBox.Right, bottom));
            foreach (var (start, end) in columnClip)
                canvas.ClipRect(new SKRect(borderLeft + start, top, borderLeft + end, bottom), SKClipOperation.Difference);
            try
            {
                PaintNodeContentBody(canvas, node, viewportWidth);
            }
            finally
            {
                canvas.Restore();
            }
            return;
        }

        PaintNodeContentBody(canvas, node, viewportWidth);
    }

    private static void PaintNodeContentBody(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display = node.GetDisplay();

        // CSS 2.1 §11.2: 'visibility: hidden' suppresses this box's own rendering — background,
        // borders, text, replaced content and list marker — while the box keeps its place in the
        // layout. Descendants are NOT skipped: visibility is inherited, so a child normally
        // inherits 'hidden', but one that declares visibility:visible must still be painted.
        // Going straight to PaintChildrenSorted also drops the hit regions the per-control paint
        // helpers register, which is correct: an invisible box is not a hit-test target.
        if (node.IsInvisible())
        {
            PaintChildrenSorted(canvas, node, viewportWidth);
            return;
        }

        switch (node.TagName)
        {
            case { } h when h.StartsWith('H') && h.Length == 2 && char.IsDigit(h[1]):
            case "P":
            case "LABEL":
                PaintTextBlock(canvas, node, viewportWidth);
                PaintChildrenSorted(canvas, node, viewportWidth);
                return;

            case "A":
                PaintAnchor(canvas, node, viewportWidth);
                return;

            case "IMG":
                // An image is a replaced box like any other: its own background, border and
                // outline paint under/around the bitmap. The inline path reserves room for those
                // edges, so skipping them here would leave a gap with nothing drawn in it.
                PaintBlockDecorations(canvas, node);
                PaintImage(canvas, node);
                return;

            case "OBJECT":
                // A loaded <object> is a replaced element (paint its background/border, then the
                // image); an unloaded one renders its fallback child content.
                if (node.Image != null)
                {
                    PaintBlockDecorations(canvas, node);
                    PaintImage(canvas, node);
                }
                else PaintChildrenSorted(canvas, node, viewportWidth);
                return;

            case "INPUT":
                PaintInput(canvas, node);
                return;

            case "BUTTON":
                PaintButton(canvas, node);
                return;

            case "TEXTAREA":
                PaintTextarea(canvas, node);
                return;

            case "SELECT":
                PaintSelect(canvas, node);
                return;

            case "PROGRESS":
                PaintProgress(canvas, node);
                return;

            case "METER":
                PaintMeter(canvas, node);
                return;

            case "HR":
                PaintHorizontalRule(canvas, node);
                return;

            case "IFRAME":
                PaintIframe(canvas, node);
                return;

            case "VIDEO":
            case "AUDIO":
                PaintMedia(canvas, node);
                return;

            case "SVG":
                SvgRenderer.Render(canvas, node);
                return;

            case "CANVAS":
                CanvasRenderer.Render(canvas, node);
                return;
        }

        // Generated 'content: url(...)' (§12.2): the pseudo box is a replaced element — its own
        // background/border/outline paint under/around the bitmap, exactly like an IMG.
        if (Layout.BoxEngine.IsReplacedImage(node) && node.TagName.StartsWith("#pseudo"))
        {
            PaintBlockDecorations(canvas, node);
            PaintImage(canvas, node);
            return;
        }

        if (display == DisplayType.ListItem)
        {
            PaintListItem(canvas, node, viewportWidth);
            return;
        }

        // inline-block belongs here with the other atomic inline-level boxes: it is a block
        // CONTAINER, so it paints a background, borders and a background-image over its padding
        // box. Leaving it out sent an inline-block with no text straight to the child recursion,
        // which paints nothing at all — an empty sized inline-block was simply invisible — and
        // one with text to the generic-inline branch, which fills only the content box and draws
        // no border. (Form controls never reach here; they are dispatched by tag name above.)
        if (display == DisplayType.Block || display == DisplayType.Flex || display == DisplayType.InlineFlex
            || display == DisplayType.Table || display == DisplayType.InlineTable
            || display == DisplayType.InlineBlock
            || display == DisplayType.TableRowGroup || display == DisplayType.TableRow || display == DisplayType.TableCell)
        {
            PaintBlock(canvas, node, viewportWidth);
            return;
        }

        // Generic inline: if this node carries text (e.g. <strong>, <em>, <mark>, <code>, #TEXT)
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var bgColor = node.GetBackgroundColor();
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

            // An inline box broken over several line boxes has one fragment per line, each with
            // the slice of text drawn there. Painting node.Box alone covered only the last line,
            // so a preformatted <span> spanning two lines showed one background, not two.
            if (node.InlineFragments is { Count: > 1 } fragments)
            {
                foreach (var (rect, text) in fragments)
                {
                    if (bgColor != SKColors.Transparent)
                    {
                        using var fragBg = new SKPaint { Color = bgColor };
                        canvas.DrawRect(BoxEngine.InlineContentArea(node, rect), fragBg);
                    }
                    DrawWrappedText(canvas, node, text, rect.Left, rect.Top, SliceWidth(node, text, font, rect.Width), font, paint, lineAligned: true);
                }
                return;
            }

            if (bgColor != SKColors.Transparent)
            {
                // §14.2: a background paints over the PADDING box. For an inline box that is the
                // text plus whatever horizontal padding the line reserved for it (§8.4).
                using var bgPaint = new SKPaint { Color = bgColor };
                canvas.DrawRect(BoxEngine.InlineContentArea(node, node.Box).PaddingBox, bgPaint);
            }
            // NOTE: an inline box's BORDERS are still not drawn. Reserving room for them in the
            // line and stroking them here was measured against the CSS 2.1 suite and came out
            // flat: it fixes the tests that expect a visible inline border, and breaks the same
            // number of block-in-inline ones, where an inline box broken around a block needs one
            // fragment box per piece before its edges can land in the right places.
            // The fragment rect is LINE-POSITIONED: the line assembler already shifted the whole
            // line for text-align over every item's width. Re-applying the alignment inside the
            // rect would shift the ink again whenever the item's width (which counts a trailing
            // collapsible space — it advances the pen, §16.6.1) differs from the wrapped ink
            // width, landing right/center-aligned text one space off (bidi-box-model-019).
            // The painted string is the FRAGMENT text — the laid-out visual slice, which also
            // carries the §16.6.1 line-start strip the raw RenderedText has not been through
            // (an XML-served document keeps " First " raw; layout laid out "First ").
            var paintedText = node.InlineFragments is { Count: > 0 } fs ? fs[^1].Text : node.RenderedText();
            DrawWrappedText(canvas, node, paintedText,
                node.Box.ContentBox.Left, node.Box.ContentBox.Top,
                node.InlineFragments is { Count: > 0 } ? SliceWidth(node, paintedText, font, node.Box.ContentBox.Width) : node.Box.ContentBox.Width,
                font, paint, lineAligned: true);
            return;
        }

        // Inline container with children (e.g. <span>, <a> wrappers) — recurse
        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    // -------------------------------------------------------------------------
    // Block containers
    // -------------------------------------------------------------------------

    private static void PaintBlockDecorations(SKCanvas canvas, LayoutNode node)
    {
        var box = node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable
            ? TableEngine.ExcludeCaptionBands(node, node.Box)
            : node.Box;
        DrawBoxShadows(canvas, box, node);
        // §14.2: a body whose background propagated to the canvas paints no background here.
        var bgColor = node != _propagatedBackgroundNode ? node.GetBackgroundColor() : SKColors.Transparent;
        if (bgColor != SKColors.Transparent)
        {
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            // A square background is snapped to whole device pixels (no anti-aliasing), which is
            // what a browser does and what replaced content already does here. Anti-aliasing a
            // box that lands on a fractional y left a half-covered fringe row, so a background
            // and an image of the same size in the same place did not paint the same pixels.
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = rx > 0 || ry > 0 };
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else canvas.DrawRect(box.PaddingBox, bgPaint);
        }
        var gradient = node.GetLinearGradient();
        if (gradient != null && node != _propagatedBackgroundNode)
            DrawLinearGradient(canvas, box, node, gradient);
        if (node != _propagatedBackgroundNode)
            DrawBackgroundImage(canvas, node, box);
        DrawBorders(canvas, box, node);
        DrawOutline(canvas, box, node);
    }

    /// <summary>CSS 2.1 §17.6.1.1: with <c>empty-cells: hide</c>, a table cell with no in-flow
    /// content paints neither background nor borders. Only the separated-borders model honours
    /// the property; in the collapsing model every cell paints.</summary>
    private static bool IsEmptyCellHidden(LayoutNode node)
    {
        if (node.GetDisplay() != DisplayType.TableCell) return false;
        var ec = node.TryResolveStyle("empty-cells", out var v) ? v : node.Style.GetPropertyValueSafe("empty-cells");
        if (ec?.Trim() != "hide") return false;
        var collapse = false;
        for (var a = node.Parent; a is not null; a = a.Parent)
            if (a.GetDisplay() is DisplayType.Table or DisplayType.InlineTable)
                { collapse = TableEngine.IsBorderCollapse(a); break; }
        if (collapse) return false;
        bool content = TextRenders(node.DisplayText, node)
            || node.Children.Any(c => c.TagName != "#text" || TextRenders(c.DisplayText, c));
        return !content;
    }

    /// <summary>Whether a text run renders anything under <paramref name="node"/>'s collapsing:
    /// CSS white space is only space/tab/LF/CR/FF (§4.1.1), so an NBSP run is content even
    /// though string.IsNullOrWhiteSpace answers yes (empty-cells-005); under pre/pre-wrap even
    /// white-space-only runs render (empty-cells-006, empty-cells-exceptions-004).</summary>
    internal static bool TextRenders(string? text, LayoutNode node)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (node.GetWhiteSpace() is WhiteSpace.Pre or WhiteSpace.PreWrap) return true;
        foreach (var ch in text)
            if (ch is not (' ' or '\t' or '\n' or '\r' or '\f')) return true;
        return false;
    }

    private static void PaintBlock(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable
            ? TableEngine.ExcludeCaptionBands(node, node.Box)
            : node.Box;

        // Decorations are drawn before the scroll translate for scrollable elements;
        // skip them here to avoid painting them twice. empty-cells:hide also suppresses them.
        // In a collapsed table the row/group/column boxes paint NO borders of their own —
        // the cells carry the §17.6.2.1 resolved winners, and a row border painted at the
        // row's box would draw an inner duplicate line (border-conflict-element-003/004/012).
        // Only the row's own decorations are suppressed: its children (the cells) still paint.
        var noDecorations = false;
        if (!_predecorated.Contains(node) && node.ScrollState?.NeedsScrollbar != true && !IsEmptyCellHidden(node))
        {
            var propagated = node == _propagatedBackgroundNode;
            if (node.GetDisplay() is DisplayType.TableRow or DisplayType.TableRowGroup
                || TableEngine.IsColumn(node) || TableEngine.IsColumnGroup(node))
            {
                for (var a = node.Parent; a is not null; a = a.Parent)
                {
                    if (a.GetDisplay() != DisplayType.Table) continue;
                    noDecorations = TableEngine.IsBorderCollapse(a);
                    break;
                }
            }
            if (!noDecorations)
            {
                if (!propagated) DrawBoxShadows(canvas, box, node);

                // Background — a body whose background propagated to the canvas (§14.2) paints
                // no background of its own; borders and outline still draw.
                if (!propagated)
                {
                    var bgColor = node.GetBackgroundColor();
                    if (bgColor != SKColors.Transparent)
                    {
                        var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
                        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = rx > 0 || ry > 0 };
                        if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
                        else canvas.DrawRect(box.PaddingBox, bgPaint);
                    }

                    // Linear gradient background
                    var gradient = node.GetLinearGradient();
                    if (gradient != null)
                        DrawLinearGradient(canvas, box, node, gradient);

                    // Background image
                    DrawBackgroundImage(canvas, node, box);

                    if (node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable)
                        PaintColumnBackgrounds(canvas, node);
                }

                // In a collapsed table the TABLE's own border does not paint either: its
                // §17.6.2 contribution is already one of the resolved winners the cells draw
                // in the outer band, and painting it separately drew the losing border over
                // the winner at the frame (border-conflict-element-039's red sides).
                var suppressTableBorder = node.GetDisplay() is DisplayType.Table or DisplayType.InlineTable
                                          && TableEngine.IsBorderCollapse(node);
                if (!suppressTableBorder) DrawBorders(canvas, box, node);
                DrawOutline(canvas, box, node);            }
        }

        if (!node.GetPointerEventsNone())
        {
            _hitRegions.Add(CursorRegion(box.BorderBox, node, nodeKey: node.NodeKey));
        }

        // Draw own text content (block elements like <div>Text</div> with no child nodes).
        // Children that render nothing (display:none, e.g. a layout-probing <script> inside a
        // float) don't count: layout's own-text height path (BoxEngine) measures the folded text
        // whenever no child contributes height, so a script child must not mute the paint.
        // During the Appendix-E step-3 descent the text belongs to step 5: collect the node so
        // PaintInlineContent draws the run after the SC's floats.
        if (!string.IsNullOrEmpty(node.DisplayText) &&
            (node.Children.Count == 0 || node.Children.All(c => c.GetDisplay() == DisplayType.None)))
        {
            if (_stepFloats != null && node.GetDisplay() is not (DisplayType.Flex or DisplayType.InlineFlex))
            {
                Defer(_stepInlines!, canvas, node);
                return;
            }
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

            var textX = box.ContentBox.Left;
            var textY = box.ContentBox.Top;

            // Flex containers with direct text: apply justify-content / align-items
            var nodeDisplay = node.GetDisplay();
            var isFlex = nodeDisplay == DisplayType.Flex || nodeDisplay == DisplayType.InlineFlex;
            var textMaxW = box.ContentBox.Width;
            if (isFlex)
            {
                var ws = node.GetWhiteSpace();
                var lh = node.GetLineHeight(node.GetFontSize());
                var lines = TextMeasure.WrapText(node.RenderedText(), Math.Max(box.ContentBox.Width, 1f), font, ws, lh);
                var textH = lines.Sum(l => l.Height);
                var textW = lines.Count > 0 ? lines.Max(l => l.Width) : 0f;

                var dir = node.GetFlexDirection();
                var isRow = dir == FlexDirection.Row || dir == FlexDirection.RowReverse;

                if (isRow)
                {
                    // justify-content controls horizontal, align-items controls vertical
                    textX += node.GetJustifyContent() switch
                    {
                        JustifyContent.Center => (box.ContentBox.Width - textW) / 2f,
                        JustifyContent.FlexEnd => box.ContentBox.Width - textW,
                        JustifyContent.SpaceAround => (box.ContentBox.Width - textW) / 2f,
                        JustifyContent.SpaceEvenly => (box.ContentBox.Width - textW) / 2f,
                        _ => 0f,
                    };
                    textY += node.GetAlignItems() switch
                    {
                        AlignItems.Center => (box.ContentBox.Height - textH) / 2f,
                        AlignItems.FlexEnd => box.ContentBox.Height - textH,
                        _ => 0f,
                    };
                }
                else
                {
                    // Column: justify-content controls vertical, align-items controls horizontal
                    textY += node.GetJustifyContent() switch
                    {
                        JustifyContent.Center => (box.ContentBox.Height - textH) / 2f,
                        JustifyContent.FlexEnd => box.ContentBox.Height - textH,
                        JustifyContent.SpaceAround => (box.ContentBox.Height - textH) / 2f,
                        JustifyContent.SpaceEvenly => (box.ContentBox.Height - textH) / 2f,
                        _ => 0f,
                    };
                    textX += node.GetAlignItems() switch
                    {
                        AlignItems.Center => (box.ContentBox.Width - textW) / 2f,
                        AlignItems.FlexEnd => box.ContentBox.Width - textW,
                        _ => 0f,
                    };
                }
                // Use measured text width so DrawWrappedText doesn't add its own alignment offset
                textMaxW = textW;
            }

            DrawWrappedText(canvas, node, node.RenderedText(),
                            textX, textY, textMaxW, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    /// <summary>
    /// An element establishes a stacking context (painted atomically, ordered by z-index within
    /// its parent context) when it is a relatively or absolutely positioned box with an integer
    /// 'z-index' (CSS 2.1 §9.9.1: "'auto' … The box does not establish a new local stacking
    /// context"), a sticky box, or has opacity &lt; 1 or a transform. position:fixed is excluded —
    /// those are painted by PaintFixedNodes.
    /// <para>A positioned box with 'z-index: auto' is a stacking ITEM without being a context
    /// (<see cref="IsStackingItem"/>): Appendix E step 8 paints it "as if it generated a new
    /// stacking context, but any positioned descendants and descendants which actually create a
    /// new stacking context should be considered part of the parent stacking context, not this
    /// new one". Treating it as a context sealed its z-index:-1 children above its own
    /// background (z-index-abspos-001's red square over the green one).</para>
    /// </summary>
    private static bool EstablishesStackingContext(LayoutNode n)
    {
        var pos = OwnPosition(n);
        if (pos is PositionType.Absolute or PositionType.Relative && !IsAutoZIndex(n)) return true;
        if (pos is PositionType.Sticky) return true;
        if (!IsSynthesized(n) || n.TryResolveStyle("opacity", out _))
            if (n.GetOpacity() < 1f) return true;
        if (!IsSynthesized(n) || n.TryResolveStyle("transform", out _))
            if (n.GetTransform() != null) return true;
        return false;
    }

    /// <summary>A box the stacking-context walk paints in its own layer (Appendix E steps 2, 8
    /// and 9): every stacking context plus every relatively/absolutely positioned box, whatever
    /// its 'z-index'. The in-flow phases skip these.</summary>
    private static bool IsStackingItem(LayoutNode n) =>
        OwnPosition(n) is PositionType.Absolute or PositionType.Relative or PositionType.Sticky
        || EstablishesStackingContext(n);

    /// <summary>Synthesized nodes (<c>#text</c>, <c>#pseudo-*</c>, anonymous boxes) share their
    /// host element's <see cref="LayoutNode.Style"/> object, so the host's non-inherited
    /// 'position', 'opacity' and 'transform' read back as their own unless they carry their own
    /// override (a generated box's declared 'position').</summary>
    private static bool IsSynthesized(LayoutNode n) => n.TagName.Length > 0 && n.TagName[0] == '#';

    private static PositionType OwnPosition(LayoutNode n) =>
        IsSynthesized(n) && !n.TryResolveStyle("position", out _)
            ? PositionType.Static
            : n.GetPosition();

    /// <summary>§9.9.1: 'z-index' is 'auto' unless it is an integer.</summary>
    private static bool IsAutoZIndex(LayoutNode n)
    {
        var raw = n.TryResolveStyle("z-index", out var ov)
            ? ov : n.Style.GetPropertyValueSafe("z-index");
        return !int.TryParse(raw?.Trim(), out _);
    }

    /// <summary>Collects the stacking items of <paramref name="node"/>'s stacking context in
    /// document order (Appendix E). The walk descends through in-flow boxes and through
    /// positioned 'z-index: auto' boxes — whose positioned descendants belong to this context,
    /// not to theirs — but stops at (and includes) each real stacking context. position:fixed is
    /// skipped (PaintFixedNodes owns it).</summary>
    private static void CollectStackingItems(LayoutNode node, List<LayoutNode> items)
    {
        foreach (var child in node.Children)
        {
            if (child.GetDisplay() == DisplayType.None) continue;
            if (OwnPosition(child) == PositionType.Fixed) continue;
            if (EstablishesStackingContext(child)) items.Add(child); // atomic — don't descend
            else
            {
                if (IsStackingItem(child)) items.Add(child);         // z-index:auto, step 8
                CollectStackingItems(child, items);
            }
        }
    }

    /// <summary>An in-flow inline-level atomic box (inline-block / inline-table / inline-flex).
    /// Per CSS 2.1 Appendix E these paint in step 5 — ABOVE in-flow block-level backgrounds
    /// and borders (step 3), unlike Lite's default single doc-order pass.</summary>
    private static bool IsAtomicInlineBox(LayoutNode n)
        => n.GetDisplay() is DisplayType.InlineBlock or DisplayType.InlineTable or DisplayType.InlineFlex;

    /// <summary>
    /// Paints a node's descendants per CSS 2.1 Appendix E. The node's own background/borders are
    /// already painted by the caller, so the order here is: negative-z stacking contexts → in-flow
    /// non-positioned content → zero/positive-z stacking contexts (and positioned descendants).
    /// <para>The in-flow content itself splits into the Appendix E steps of the NEAREST stacking
    /// context: step 3 paints in-flow block-level descendants (backgrounds, borders, replaced
    /// content), step 4 paints every float subtree, and step 5 paints the inline content — each
    /// phase over the whole stacking-context subtree, so a float covers later in-flow block
    /// backgrounds but sits under inline text (stack-floats-*, floats-008). Blocks reached during
    /// the step-3 descent collect their own text into the step-5 list instead of drawing it.</para>
    /// </summary>
    private static void PaintChildrenSorted(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var isFlex = node.GetDisplay() is DisplayType.Flex or DisplayType.InlineFlex;
        IEnumerable<LayoutNode> ordered = isFlex ? node.Children.OrderBy(c => c.GetOrder()) : node.Children;

        // The root element (no parent), any stacking-context-establishing box, and any
        // position:fixed box are roots: PaintFixedNodes is the only thing that paints a fixed
        // box's subtree, so its positioned descendants have no enclosing context to be
        // collected by — the context walk stops at fixed boxes (EstablishesStackingContext
        // excludes them so they never join an ancestor's z lists) and PaintNodeInner's child
        // pass skips stacking items (containing-block-010's abspos square under a fixed div
        // was laid out but orphaned, painted by nobody).
        bool isScRoot = node.Parent is null || EstablishesStackingContext(node)
                        || OwnPosition(node) == PositionType.Fixed;

        void SplitInFlowNode(LayoutNode child)
        {
            if (OwnPosition(child) == PositionType.Fixed) return;              // PaintFixedNodes
            if (IsStackingItem(child)) return;                                 // handled by SC root
            if (_stepFloats == null)
            {
                // No step-3 descent active (a full-paint context: float subtree, z pass,
                // atomic inline) — document order, exactly as before the phase split.
                PaintNode(canvas, child, viewportWidth);
                return;
            }
            if (child.TagName != "#text" && child.GetFloat() != FloatType.None)
            {
                Defer(_stepFloats, canvas, child);                             // step 4
                return;
            }
            if (IsInlineLevelContent(child))
            {
                Defer(_stepInlines!, canvas, child);                           // step 5
                // An inline box's BLOCK descendants are step-3 content: descend through the
                // inline box (stack-floats-001's red block inside the inline paints under the
                // float). Its inline-level descendants are NOT collected — they paint through
                // this box's own step-5 recursion; collecting them here would paint them twice.
                if (!IsAtomicInlineBox(child) && child.TagName != "IMG")
                    foreach (var gc in child.Children)
                        DescendForBlocks(gc);
                return;
            }
            PaintNode(canvas, child, viewportWidth);                           // step 3
        }

        void DescendForBlocks(LayoutNode child)
        {
            if (OwnPosition(child) == PositionType.Fixed) return;              // PaintFixedNodes
            if (IsStackingItem(child)) return;                                 // handled by SC root
            if (child.TagName != "#text" && child.GetFloat() != FloatType.None)
            {
                Defer(_stepFloats!, canvas, child);                            // step 4
                return;
            }
            if (IsInlineLevelContent(child))
            {
                if (!IsAtomicInlineBox(child) && child.TagName != "IMG")
                    foreach (var gc in child.Children)
                        DescendForBlocks(gc);
                return;
            }
            PaintNode(canvas, child, viewportWidth);                           // step 3
        }

        void SplitInFlow()
        {
            foreach (var child in ordered)
                SplitInFlowNode(child);
        }

        // Appendix E step 8: a positioned 'z-index: auto' box paints "as if it generated a new
        // stacking context" — its own in-flow content in the step 3/4/5 order — while its
        // positioned descendants were collected by the enclosing context, so it has no z lists.
        bool isAutoZRoot = !isScRoot && IsStackingItem(node);
        if (!isScRoot && !isAutoZRoot)
        {
            SplitInFlow();
            return;
        }

        var items = new List<LayoutNode>();
        if (isScRoot) CollectStackingItems(node, items);
        // OrderBy is stable, so equal z-indices keep document order.
        var negativeZ = items.Where(c => c.GetZIndex() < 0).OrderBy(c => c.GetZIndex()).ToList();
        var nonNegativeZ = items.Where(c => c.GetZIndex() >= 0).OrderBy(c => c.GetZIndex()).ToList();
        foreach (var c in negativeZ) PaintThroughClippers(canvas, node, c, viewportWidth);      // step 2

        var savedFloats = _stepFloats;
        var savedInlines = _stepInlines;
        var savedDeferred = _deferredInlines;
        _deferredInlines = null; // subsumed by the step-5 list
        _stepFloats = [];
        _stepInlines = [];
        SplitInFlow();                                                         // step 3
        // Deactivate the collectors BEFORE flushing: painting a float or an inline item runs
        // full-paint paths whose text blocks must draw directly, not re-enqueue mid-iteration.
        var step4Floats = _stepFloats;
        var step5Items = _stepInlines;
        _stepFloats = savedFloats;
        _stepInlines = savedInlines;
        _deferredInlines = savedDeferred;
        foreach (var f in step4Floats) PaintDeferred(canvas, f, () => PaintNode(canvas, f.Node, viewportWidth));                    // step 4
        foreach (var i in step5Items) PaintDeferred(canvas, i, () => PaintInlineContent(canvas, i.Node, viewportWidth));  // step 5

        foreach (var c in nonNegativeZ) PaintThroughClippers(canvas, node, c, viewportWidth);   // steps 6/7
    }


    /// <summary>Paints a stacking item from its stacking-context root under the clip and scroll
    /// offset of every non-'visible' overflow box between the root and the item — the state
    /// the in-flow pass applied inside those boxes, which a root-level paint would otherwise
    /// skip (overflow-003: an abspos square inside a scrolled 'overflow: scroll' box painted
    /// outside it, unclipped and unscrolled). §11.1.1: the clip covers descendants "except
    /// those whose containing block is the viewport or an ancestor of the element", so an
    /// absolutely positioned item passes only through boxes that contain its containing block.
    /// <para>A 'z-index: auto' positioned ancestor between the root and the item (whose
    /// positioned descendants join the root's context, Appendix E) still moves its subtree:
    /// its relative offset (§9.4.3) is replayed here as well, outermost first, interleaved with
    /// the clips in tree order.</para></summary>
    private static void PaintThroughClippers(SKCanvas canvas, LayoutNode root, LayoutNode item, int viewportWidth)
    {
        List<(LayoutNode Box, bool Clip, float Dx, float Dy)>? chain = null;
        var absolute = OwnPosition(item) == PositionType.Absolute;
        LayoutNode? containingBlock = null;
        if (absolute)
            for (var a = item.Parent; a is not null && !ReferenceEquals(a, root); a = a.Parent)
                if (OwnPosition(a) != PositionType.Static) { containingBlock = a; break; }
        float shiftX = 0f, shiftY = 0f;
        for (var a = item.Parent; a is not null && !ReferenceEquals(a, root); a = a.Parent)
        {
            var clips = a.GetOverflow() != OverflowType.Visible
                        && (!absolute || IsSelfOrDescendant(containingBlock, a));
            var (dx, dy) = OwnPosition(a) == PositionType.Relative ? BoxEngine.RelativeOffset(a) : (0f, 0f);
            if (!clips && dx == 0f && dy == 0f) continue;
            (chain ??= []).Add((a, clips, dx, dy));
            shiftX += dx;
            shiftY += dy;
        }
        if (chain is null)
        {
            PaintNode(canvas, item, viewportWidth);
            return;
        }
        var hitStart = _hitRegions.Count;
        canvas.Save();
        for (var i = chain.Count - 1; i >= 0; i--)  // outermost first
        {
            var (a, clips, dx, dy) = chain[i];
            if (dx != 0f || dy != 0f) canvas.Translate(dx, dy);
            if (!clips) continue;
            canvas.ClipRect(BoxEngine.ScrollportRect(a));
            if (a.ScrollState is { } ss && (ss.NeedsScrollbar || ss.NeedsHorizontalScrollbar))
                canvas.Translate(-ss.ScrollX, -ss.ScrollY);
        }
        PaintNode(canvas, item, viewportWidth);
        canvas.Restore();
        // Hit regions are recorded in layout coordinates; move the ones this subtree made by
        // the relative offsets replayed above (PaintNode does the same for the item's own).
        if (shiftX != 0f || shiftY != 0f)
            for (var i = hitStart; i < _hitRegions.Count; i++)
            {
                var region = _hitRegions[i];
                var box = region.Bounds;
                _hitRegions[i] = region with
                {
                    Bounds = new SKRect(box.Left + shiftX, box.Top + shiftY, box.Right + shiftX, box.Bottom + shiftY)
                };
            }
    }

    private static bool IsSelfOrDescendant(LayoutNode? node, LayoutNode ancestor)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    private static List<DeferredPaint>? _stepFloats;
    private static List<DeferredPaint>? _stepInlines;

    /// <summary>A step-4/5 item with the canvas state it was collected under. The phases paint
    /// from the stacking-context root, outside the clips and translations of the boxes between
    /// it and the item — an 'overflow: hidden' table cell's clip (overflow-applies-to-*), a
    /// scrolled container's offset — so the item replays that state when it finally paints.</summary>
    private readonly record struct DeferredPaint(LayoutNode Node, SKMatrix Matrix, SKRectI DeviceClip);

    private static void Defer(List<DeferredPaint> list, SKCanvas canvas, LayoutNode node)
    {
        foreach (var d in list)
            if (ReferenceEquals(d.Node, node)) return;
        list.Add(new DeferredPaint(node, canvas.TotalMatrix, canvas.DeviceClipBounds));
    }

    private static void PaintDeferred(SKCanvas canvas, DeferredPaint item, Action paint)
    {
        canvas.Save();
        canvas.ResetMatrix();
        canvas.ClipRect(SKRect.Create(item.DeviceClip.Left, item.DeviceClip.Top,
            item.DeviceClip.Width, item.DeviceClip.Height));
        canvas.SetMatrix(item.Matrix);
        paint();
        canvas.Restore();
    }

    /// <summary>An in-flow, inline-level box or text run: Appendix-E step-5 content.
    /// Atomic inlines (inline-block/-table/-flex, images) and text are inline-level; so is
    /// display:inline. Everything in-flow that is not inline-level, a float, or positioned is
    /// a step-3 block.</summary>
    private static bool IsInlineLevelContent(LayoutNode n)
        => n.TagName == "#text"
           || n.GetDisplay() is DisplayType.Inline or DisplayType.InlineBlock
               or DisplayType.InlineTable or DisplayType.InlineFlex;

    /// <summary>Appendix-E step 5: paints one collected piece of inline content.
    /// Atomic inlines paint whole; #text and blocks collected for their own text draw their
    /// text runs; an inline box paints its own background/borders over its fragments and then
    /// recurses into inline-level children only — its block children painted at step 3.</summary>
    private static void PaintInlineContent(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        if (node.GetDisplay() == DisplayType.None) return;
        if (IsAtomicInlineBox(node) || node.TagName is "IMG" or "BR"
            || node.GetPosition() is PositionType.Absolute or PositionType.Fixed
            || node.TagName is "A" or "LABEL" or "BUTTON" or "INPUT" or "SELECT" or "TEXTAREA"
                or "OBJECT" or "IFRAME" or "VIDEO" or "AUDIO" or "SVG" or "CANVAS" or "PROGRESS" or "METER")
        {
            // Elements with dedicated paint paths — hit regions, controls, replaced content —
            // paint whole. A block-in-inline inside an anchor then paints at step 5 rather than
            // step 3: an accepted deviation that keeps links clickable.
            PaintNode(canvas, node, viewportWidth);
            return;
        }

        bool isBlockWithOwnText = !IsInlineLevelContent(node) && node.TagName != "#text";
        if (isBlockWithOwnText)
        {
            // A step-3 block collected for its own text: the background/borders painted in the
            // block pass; only the text runs belong here.
            DrawNodeText(canvas, node);
            return;
        }

        // An inline box: own background/borders/text, then inline-level children only.
        if (PaintInlineBoxOwnContent(canvas, node))
            return; // the per-line fragments carried the whole subtree's text (old early-return)
        foreach (var child in node.Children)
        {
            if (child.GetDisplay() == DisplayType.None) continue;
            if (child.GetPosition() == PositionType.Fixed) continue;
            if (IsStackingItem(child)) continue;               // z passes own it
            if (child.TagName != "#text" && child.GetFloat() != FloatType.None) continue; // step 4
            if (!IsInlineLevelContent(child)) continue;        // step 3 painted it
            PaintInlineContent(canvas, child, viewportWidth);
        }
    }

    /// <summary>An inline box's own painting: background over the padding box (or per-line
    /// fragments), borders where the fragments carry them, and its own text run. Returns true
    /// when per-line fragments were drawn — they carry the text of the whole inline subtree,
    /// so the caller must not recurse into the (differently-boxed) text children.</summary>
    private static bool PaintInlineBoxOwnContent(SKCanvas canvas, LayoutNode node)
    {
        var bgColor = node.GetBackgroundColor();
        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
        using var font = TextMeasure.CreateFont(node);
        // Anonymous boxes (:before/:after pseudos, synthesized text) resolve their styles from
        // the element that produced them — painting "their" borders would paint the HOST's
        // border as an inner ring (content-white-space-001's bordered div around a :before).
        var paintsOwnBorders = !node.TagName.StartsWith('#');
        // §14.2: a boxless inline wrapper's BACKGROUND paints over each line fragment's padding
        // box too — it was never painted, so an inline whose content is another inline showed
        // no background at all, not even under its child's margin (inline-formatting-context-002:
        // the black stripe left of 'Filler Text').
        var wrapperBackground = paintsOwnBorders && bgColor != SKColors.Transparent &&
                                node.InlineFragmentBoxes is { Count: > 0 };
        if (wrapperBackground)
        {
            using var wrapperBg = new SKPaint { Color = bgColor };
            foreach (var wrapperBox in node.InlineFragmentBoxes!)
                canvas.DrawRect(wrapperBox.PaddingBox, wrapperBg);
        }
        // §8.4/§8.6: a boxless inline wrapper's borders paint per line from the fragments line
        // layout assembled (side masks included) — this is the only border geometry a wrapper
        // has, since its painted geometry lives in its content rather than its own Box.
        if (paintsOwnBorders)
            foreach (var wrapperBox in node.InlineFragmentBoxes ?? [])
                DrawBorders(canvas, wrapperBox, node);
        // An inline element is a hit target over its own fragments (its cursor, §18.1, and
        // the innermost element for pointer events); anonymous boxes belong to their host.
        var hitTarget = paintsOwnBorders && !node.GetPointerEventsNone();
        if (hitTarget)
            foreach (var wrapperBox in node.InlineFragmentBoxes ?? [])
                _hitRegions.Add(CursorRegion(wrapperBox.BorderBox, node, nodeKey: node.NodeKey));
        if (node.InlineFragments is { Count: > 1 } fragments)
        {
            // §8.4: an inline box split over lines paints its left border at the FIRST fragment's
            // left, the right border at the LAST fragment's right, and the top/bottom borders on
            // EVERY fragment (a fragment's join sides stay empty). Zero-width sides are skipped
            // inside DrawBorderSide, so masking through the Border edges is the whole rule.
            // Anonymous boxes (#text) resolve styles from their parent element: they have no
            // padding or border of their own (taking the parent's 3em padding spread each line's
            // background over the line above it, background-root-017-ref).
            var fragPadding = paintsOwnBorders ? node.GetPadding(0, 0, node.GetFontSize()) : default;
            var fragBorder = paintsOwnBorders ? node.GetBorderWidth() : default;
            // The join sides carry no PADDING either: line 1 of a split padded span ends at its
            // text and line 2 starts at its text (inline-formatting-context-022 drew both join
            // sides 1em too wide). The background covers each fragment's padding box (§14.2).
            BoxDimensions FragmentBox(int fi, SKRect area) => new()
            {
                ContentBox = area,
                Padding = new EdgeSizes
                {
                    Top = fragPadding.Top,
                    Bottom = fragPadding.Bottom,
                    Left = fi == 0 ? fragPadding.Left : 0f,
                    Right = fi == fragments.Count - 1 ? fragPadding.Right : 0f,
                },
                Border = new EdgeSizes
                {
                    Top = fragBorder.Top,
                    Bottom = fragBorder.Bottom,
                    Left = fi == 0 ? fragBorder.Left : 0f,
                    Right = fi == fragments.Count - 1 ? fragBorder.Right : 0f,
                },
            };
            for (var fi = 0; fi < fragments.Count; fi++)
            {
                var (rect, text) = fragments[fi];
                if (hitTarget) _hitRegions.Add(CursorRegion(rect, node, nodeKey: node.NodeKey));
                var box = FragmentBox(fi, BoxEngine.InlineContentArea(node, rect));
                if (bgColor != SKColors.Transparent)
                {
                    using var fragBg = new SKPaint { Color = bgColor };
                    canvas.DrawRect(box.PaddingBox, fragBg);
                }
                DrawBorders(canvas, box, node);
                DrawWrappedText(canvas, node, text, rect.Left, rect.Top, SliceWidth(node, text, font, rect.Width), font, paint, lineAligned: true);
            }
            // §18.4: an inline element's outline is drawn around each line fragment (the
            // UA may also draw the union; per-fragment is the common rendering).
            if (paintsOwnBorders)
                for (var fi = 0; fi < fragments.Count; fi++)
                    DrawOutline(canvas, FragmentBox(fi, BoxEngine.InlineContentArea(node, fragments[fi].Rect)), node);
            return true;
        }
        var decorated = string.IsNullOrEmpty(node.DisplayText) ? node.Box : BoxEngine.InlineContentArea(node, node.Box);
        if (bgColor != SKColors.Transparent && !wrapperBackground)
        {
            using var bgPaint = new SKPaint { Color = bgColor };
            canvas.DrawRect(decorated.PaddingBox, bgPaint);
        }
        if (hitTarget && !node.Box.BorderBox.IsEmpty)
            _hitRegions.Add(CursorRegion(node.Box.BorderBox, node, nodeKey: node.NodeKey));
        // §8.4: the borders of an unsplit inline box paint around its border box — the box the
        // line layout recorded (borders were laid out but never painted: a 90px border on
        // display:inline painted nothing, border-width-applies-to-008). A boxless inline wrapper
        // (its geometry lives in its fragments) carries zero Border edges and paints nothing here.
        DrawBorders(canvas, decorated, node);
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            // Line-positioned rect (see the generic inline path): no re-alignment, and the
            // FRAGMENT text — the laid-out slice with the §16.6.1 line-start strip applied —
            // not the raw RenderedText an XML-served document still carries.
            var paintedText = node.InlineFragments is { Count: > 0 } fs ? fs[^1].Text : node.RenderedText();
            DrawWrappedText(canvas, node, paintedText,
                node.Box.ContentBox.Left, node.Box.ContentBox.Top,
                node.InlineFragments is { Count: > 0 } ? SliceWidth(node, paintedText, font, node.Box.ContentBox.Width) : node.Box.ContentBox.Width,
                font, paint, lineAligned: true);
        }
        // §18.4 outlines apply to inline elements too (outline-pseudo-selector-002's hovered
        // span): around the unsplit border box, or each fragment a boxless wrapper assembled.
        if (paintsOwnBorders)
        {
            if (node.InlineFragmentBoxes is { Count: > 0 } wrapperBoxes)
                foreach (var wrapperBox in wrapperBoxes) DrawOutline(canvas, wrapperBox, node);
            else if (!node.Box.BorderBox.IsEmpty)
                DrawOutline(canvas, decorated, node);
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Anchor
    // -------------------------------------------------------------------------

    private static void PaintAnchor(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        // A piece of an anchor broken around a block (§9.2.1.1) is a clone without attributes:
        // it links through the anchor it was split from.
        var link = SplitLink(node) ?? node;
        _hitRegions.Add(CursorRegion(node.Box.BorderBox, node, link.Href, link.NodeKey));

        // An inline link is an ordinary inline box (§8.4, §14.2): its background and borders
        // paint under its text — per line fragment when it was split over lines. The dedicated
        // link path used to skip them, so 'a:hover { background-color: green }' painted
        // nothing (dynamic-pseudo-classes-000). Block-level links got theirs in the block pass.
        if (node.GetDisplay() == DisplayType.Inline && !node.IsInvisible())
        {
            var background = node.GetBackgroundColor();
            if (background != SKColors.Transparent)
            {
                using var backgroundPaint = new SKPaint { Color = background };
                if (node.InlineFragments is { Count: > 1 } fragments)
                    foreach (var (rect, _) in fragments) canvas.DrawRect(rect, backgroundPaint);
                else if (!node.Box.PaddingBox.IsEmpty)
                    canvas.DrawRect(node.Box.PaddingBox, backgroundPaint);
            }
            DrawBorders(canvas, node.Box, node);
        }

        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var box = node.Box;
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            // A one-line inline anchor has already been measured by layout. Allow a small
            // floating-point margin so the painter does not wrap its final word into the next
            // table row when both measurements sit on the same pixel boundary.
            var lineHeight = node.GetLineHeight(node.GetFontSize());
            var paintWidth = box.ContentBox.Height <= lineHeight + 0.5f
                ? SliceWidth(node, node.RenderedText(), font, box.ContentBox.Width) + 1f : box.ContentBox.Width;
            DrawWrappedText(canvas, node, node.RenderedText(), box.ContentBox.Left, box.ContentBox.Top,
                            paintWidth, font, paint, lineAligned: true);
            if (!box.BorderBox.IsEmpty) DrawOutline(canvas, box, node);
            return;
        }

        // Anchor wraps child nodes (e.g. #TEXT created by parser for mixed content)
        PaintChildrenSorted(canvas, node, viewportWidth);
        // §18.4: a focused link's 'a:focus { outline: ... }' (outline-pseudo-selector-001).
        if (!node.Box.BorderBox.IsEmpty) DrawOutline(canvas, node.Box, node);
    }

    /// <summary>The link a split piece or hoisted block belongs to: the nearest anchor with an
    /// href among the inlines it was broken out of and their ancestors, or null.</summary>
    private static LayoutNode? SplitLink(LayoutNode node)
    {
        for (var n = node.SplitFrom; n is not null; n = n.SplitFrom ?? n.Parent)
            if (n.TagName == "A" && n.Href is not null) return n;
        return null;
    }

    // -------------------------------------------------------------------------
    // Image
    // -------------------------------------------------------------------------

    private static void PaintImage(SKCanvas canvas, LayoutNode node)
    {
        var destRect = node.Box.ContentBox;

        if (node.Image != null)
        {
            canvas.DrawBitmap(node.Image, destRect);
        }
        else
        {
            using var borderPaint = new SKPaint { Color = SKColors.Gray, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRect(destRect, borderPaint);

            if (!string.IsNullOrEmpty(node.Alt))
            {
                using var altPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
                using var altFont = new SKFont { Size = 12 };
                canvas.DrawText(node.Alt, destRect.Left + 4, destRect.Top + 14, SKTextAlign.Left, altFont, altPaint);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Media (audio / video)
    // -------------------------------------------------------------------------

    /// <summary>Paints an &lt;audio&gt;/&lt;video&gt; element: a video frame/poster (or a black box),
    /// then a controls overlay (play/pause glyph + progress bar + time) when <c>controls</c> is set.
    /// The progress reflects the backend's live currentTime/duration.</summary>
    private static void PaintMedia(SKCanvas canvas, LayoutNode node)
    {
        var box = node.Box.ContentBox;
        bool isVideo = node.TagName == "VIDEO";

        if (isVideo)
        {
            // Decoded frame (real backend) → poster image → black letterbox.
            var frame = node.Media?.CurrentFrame ?? node.Image;
            if (frame is not null)
                canvas.DrawBitmap(frame, box);
            else
            {
                using var bg = new SKPaint { Color = new SKColor(20, 20, 20) };
                canvas.DrawRect(box, bg);
            }
        }

        if (!node.Attributes.ContainsKey("controls")) return;

        // ---- controls bar ----
        float barH = Math.Min(40f, box.Height);
        var bar = new SKRect(box.Left, box.Bottom - barH, box.Right, box.Bottom);
        using (var barPaint = new SKPaint { Color = new SKColor(0, 0, 0, isVideo ? (byte)150 : (byte)235) })
            canvas.DrawRect(bar, barPaint);

        var cur = node.Media?.CurrentTime ?? 0;
        var dur = node.Media?.Duration ?? 0;
        bool paused = node.Media?.Paused ?? true;

        // Play/pause glyph at the left.
        float pad = 10f, glyph = 14f;
        float gx = bar.Left + pad, gy = bar.MidY;
        using (var fg = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            if (paused)
            {
                using var path = new SKPath();
                path.MoveTo(gx, gy - glyph / 2);
                path.LineTo(gx, gy + glyph / 2);
                path.LineTo(gx + glyph * 0.85f, gy);
                path.Close();
                canvas.DrawPath(path, fg);
            }
            else
            {
                canvas.DrawRect(gx, gy - glyph / 2, glyph * 0.32f, glyph, fg);
                canvas.DrawRect(gx + glyph * 0.55f, gy - glyph / 2, glyph * 0.32f, glyph, fg);
            }
        }

        // Time label at the right.
        using var timeFont = new SKFont { Size = 11 };
        using var timePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var label = $"{FormatTime(cur)} / {FormatTime(dur)}";
        float labelW = timeFont.MeasureText(label);
        canvas.DrawText(label, bar.Right - pad - labelW, bar.MidY + 4, SKTextAlign.Left, timeFont, timePaint);

        // Progress track + fill between the glyph and the time label.
        float trackLeft = gx + glyph + pad;
        float trackRight = bar.Right - pad - labelW - pad;
        if (trackRight > trackLeft)
        {
            float ty = bar.MidY, th = 4f;
            using var track = new SKPaint { Color = new SKColor(255, 255, 255, 70), IsAntialias = true };
            canvas.DrawRoundRect(trackLeft, ty - th / 2, trackRight - trackLeft, th, 2, 2, track);
            float ratio = dur > 0 ? (float)Math.Clamp(cur / dur, 0, 1) : 0f;
            using var fill = new SKPaint { Color = new SKColor(0, 150, 255), IsAntialias = true };
            canvas.DrawRoundRect(trackLeft, ty - th / 2, (trackRight - trackLeft) * ratio, th, 2, 2, fill);
        }

        // Hit region toggles play/pause (the whole bar) so the controls work in the live window.
        _hitRegions.Add(new HitRegion(bar, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.MediaToggle));
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        int total = (int)Math.Floor(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    // -------------------------------------------------------------------------
    // Iframe (nested browsing context)
    // -------------------------------------------------------------------------

    /// <summary>Paints an &lt;iframe&gt;: its own background/border, then the child Page rendered
    /// into a sub-bitmap sized to the content box and clipped into place. The nested DrawToBitmap
    /// resets the shared paint statics, so they're snapshotted and restored around it.</summary>
    private static void PaintIframe(SKCanvas canvas, LayoutNode node)
    {
        PaintBlockDecorations(canvas, node);

        var page = node.ChildPage;
        if (page is null) return;
        var content = node.Box.ContentBox;
        int cw = Math.Max(1, (int)Math.Round(content.Width));
        int ch = Math.Max(1, (int)Math.Round(content.Height));
        // The child page's viewport state must match the frame box it paints into: left at its
        // default 0, DrawScrollbar saw ContentHeight (8px of collapsed body margins) exceed the
        // "viewport" and painted a phantom thumb into every frame (absolute-replaced-height-*).
        page.Viewport.ViewportHeight = ch;

        // Snapshot the parent's paint statics — the recursive DrawToBitmap clobbers them.
        var savedHit = _hitRegions;
        var savedScrollY = _viewportScrollY;
        var savedH = _viewportHeight;
        var savedW = _viewportWidth;
        var savedSelect = new Dictionary<int, int>(SelectOptionMap);
        var savedDropdown = _pendingDropdown;

        SKBitmap childBmp;
        List<HitRegion> childRegions;
        try
        {
            childBmp = DrawToBitmap(cw, ch, page.Root, page.Viewport, _renderScale);
            childRegions = _hitRegions.ToList();
        }
        finally
        {
            _hitRegions = savedHit;
            _viewportScrollY = savedScrollY;
            _viewportHeight = savedH;
            _viewportWidth = savedW;
            _pendingDropdown = savedDropdown;
            SelectOptionMap.Clear();
            foreach (var (k, v) in savedSelect) SelectOptionMap[k] = v;
        }

        using (childBmp)
        {
            canvas.Save();
            canvas.ClipRect(content);
            // The child rasterizes at the same device scale as this canvas, so its bitmap holds
            // scale× cw×ch device pixels: draw it into the frame's CSS box and the scaled canvas
            // maps it 1:1 onto device pixels (at scale 1 this is the natural-size draw).
            canvas.DrawBitmap(childBmp, new SKRect(content.Left, content.Top, content.Left + cw, content.Top + ch));
            canvas.Restore();
        }
        foreach (var region in childRegions)
        {
            var left = Math.Max(content.Left, content.Left + region.Bounds.Left);
            var top = Math.Max(content.Top, content.Top + region.Bounds.Top - page.Viewport.ScrollY);
            var right = Math.Min(content.Right, content.Left + region.Bounds.Right);
            var bottom = Math.Min(content.Bottom, content.Top + region.Bounds.Bottom - page.Viewport.ScrollY);
            if (right <= left || bottom <= top) continue;
            _hitRegions.Add(region with
            {
                Bounds = new SKRect(left, top, right, bottom),
                OwnerPage = region.OwnerPage ?? page,
                OwnerOffsetX = content.Left + region.OwnerOffsetX,
                OwnerOffsetY = content.Top - page.Viewport.ScrollY + region.OwnerOffsetY,
            });
        }
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    /// <summary>A text control's text colour: the 'color' declared on the control itself (an author,
    /// user or dynamic rule — focus-selector-001's 'textarea:focus { color: green }'), else the
    /// UA's field text black; a control does not take the page's inherited text colour.</summary>
    private static SKColor ControlTextColor(LayoutNode node) =>
        node.TryResolveStyle("color", out var own) && own.Trim() is { Length: > 0 } value &&
        !value.Equals("inherit", StringComparison.OrdinalIgnoreCase) ? node.GetColor() : SKColors.Black;

    private static void PaintInput(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;
        var inputType = node.Attributes.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "text";

        switch (inputType)
        {
            case "submit":
            case "reset":
            case "button":
            case "image":
                PaintButton(canvas, node);
                return;
            case "checkbox":
                PaintCheckbox(canvas, node, rect);
                return;
            case "radio":
                PaintRadio(canvas, node, rect);
                return;
            case "range":
                PaintRange(canvas, node, rect);
                return;
            case "file":
                PaintFileInput(canvas, node, rect);
                return;
        }

        // Text-like inputs: text, password, number, email, etc.
        var isFocused = FormState.FocusedInput == node.NodeKey;
        var isPassword = inputType == "password";

        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        var authorBorder = node.GetBorderWidth().Top > 0f;
        using var borderPaint = new SKPaint
        {
            Color = isFocused ? new SKColor(0, 120, 215) : new SKColor(150, 150, 150),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isFocused ? 2f : 1f,
            IsAntialias = true,
        };
        if (authorBorder && !isFocused) DrawBorders(canvas, node.Box, node);
        else canvas.DrawRect(authorBorder ? node.Box.BorderBox : rect, borderPaint);

        node.Attributes.TryGetValue("value", out var defaultVal);
        var text = FormState.GetTextValue(node.NodeKey, defaultVal);
        var displayText = isPassword && !string.IsNullOrEmpty(text)
            ? new string('\u2022', text.Length)
            : text;

        if (string.IsNullOrEmpty(text) && node.Attributes.TryGetValue("placeholder", out var ph))
        {
            using var phPaint = new SKPaint { Color = new SKColor(170, 170, 170), IsAntialias = true };
            using var phFont = TextMeasure.CreateFont(node);
            canvas.DrawText(ph, rect.Left + 4, rect.MidY + phFont.Size * 0.35f, SKTextAlign.Left, phFont, phPaint);
        }
        else if (!string.IsNullOrEmpty(displayText))
        {
            using var textPaint = new SKPaint { Color = ControlTextColor(node), IsAntialias = true };
            using var textFont = TextMeasure.CreateFont(node);
            canvas.DrawText(displayText, rect.Left + 4, rect.MidY + textFont.Size * 0.35f, SKTextAlign.Left, textFont, textPaint);
        }

        // Number input: draw up/down arrows with separate hit regions
        if (inputType == "number")
        {
            var arrowW = 16f;
            var arrowX = rect.Right - arrowW;

            // Divider line
            using var divPaint = new SKPaint { Color = new SKColor(200, 200, 200), StrokeWidth = 1 };
            canvas.DrawLine(arrowX, rect.Top, arrowX, rect.Bottom, divPaint);
            canvas.DrawLine(arrowX, rect.MidY, rect.Right, rect.MidY, divPaint);

            using var arrowPaint = new SKPaint { Color = new SKColor(100, 100, 100), IsAntialias = true, Style = SKPaintStyle.Fill };
            // Up arrow
            var upPath = new SKPath();
            upPath.MoveTo(arrowX + 4, rect.MidY - 2);
            upPath.LineTo(arrowX + arrowW - 4, rect.MidY - 2);
            upPath.LineTo(arrowX + arrowW / 2f, rect.Top + 3);
            upPath.Close();
            canvas.DrawPath(upPath, arrowPaint);
            // Down arrow
            var downPath = new SKPath();
            downPath.MoveTo(arrowX + 4, rect.MidY + 2);
            downPath.LineTo(arrowX + arrowW - 4, rect.MidY + 2);
            downPath.LineTo(arrowX + arrowW / 2f, rect.Bottom - 3);
            downPath.Close();
            canvas.DrawPath(downPath, arrowPaint);

            // Hit regions: up arrow, down arrow (added before the text input region so they take priority)
            var upRect = new SKRect(arrowX, rect.Top, rect.Right, rect.MidY);
            var downRect = new SKRect(arrowX, rect.MidY, rect.Right, rect.Bottom);
            _hitRegions.Add(new HitRegion(upRect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.NumberUp));
            _hitRegions.Add(new HitRegion(downRect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.NumberDown));
        }

        if (isFocused)
        {
            using var caretFont = TextMeasure.CreateFont(node);
            var caretX = rect.Left + 4 + caretFont.MeasureText(displayText ?? "");
            using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
            canvas.DrawLine(caretX, rect.Top + 3, caretX, rect.Bottom - 3, caretPaint);
        }

        var textHitRect = inputType == "number"
            ? new SKRect(rect.Left, rect.Top, rect.Right - 16f, rect.Bottom)
            : rect;
        _hitRegions.Add(new HitRegion(textHitRect, CursorType.Text, NodeKey: node.NodeKey, InputAction: InputAction.TextInput));
    }

    private static void PaintCheckbox(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint { Color = new SKColor(150, 150, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(rect, borderPaint);

        var defaultChecked = node.Attributes.ContainsKey("checked");
        if (FormState.IsChecked(node.NodeKey, defaultChecked))
        {
            using var checkPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(rect.Left + 2, rect.MidY, rect.MidX - 1, rect.Bottom - 3, checkPaint);
            canvas.DrawLine(rect.MidX - 1, rect.Bottom - 3, rect.Right - 2, rect.Top + 3, checkPaint);
        }

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Checkbox));
    }

    private static void PaintRadio(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        // Register in radio group
        var groupName = node.Attributes.GetValueOrDefault("name", "default");
        FormState.RegisterRadio(node.NodeKey, groupName);

        var cx = rect.MidX;
        var cy = rect.MidY;
        var radius = Math.Min(rect.Width, rect.Height) / 2f;

        using var bgPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, bgPaint);

        using var borderPaint = new SKPaint { Color = new SKColor(150, 150, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, borderPaint);

        var defaultChecked = node.Attributes.ContainsKey("checked");
        if (FormState.IsChecked(node.NodeKey, defaultChecked))
        {
            using var dotPaint = new SKPaint { Color = new SKColor(0, 120, 215), IsAntialias = true };
            canvas.DrawCircle(cx, cy, radius * 0.45f, dotPaint);
        }

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Radio));
    }

    private static void PaintRange(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        // Track
        var trackY = rect.MidY;
        var trackH = 4f;
        using var trackPaint = new SKPaint { Color = new SKColor(200, 200, 200), IsAntialias = true };
        canvas.DrawRoundRect(rect.Left, trackY - trackH / 2, rect.Width, trackH, 2, 2, trackPaint);

        // Thumb position based on value (use FormState if available, else attribute)
        node.Attributes.TryGetValue("min", out var minStr);
        node.Attributes.TryGetValue("max", out var maxStr);
        node.Attributes.TryGetValue("value", out var valStr);
        float.TryParse(minStr ?? "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min);
        float.TryParse(maxStr ?? "100", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max);

        var currentVal = FormState.GetTextValue(node.NodeKey, valStr ?? "50");
        float.TryParse(currentVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val);
        val = Math.Clamp(val, min, max);
        var ratio = max > min ? (val - min) / (max - min) : 0.5f;
        var thumbX = rect.Left + ratio * rect.Width;

        // Filled portion
        using var fillPaint = new SKPaint { Color = new SKColor(0, 120, 215), IsAntialias = true };
        canvas.DrawRoundRect(rect.Left, trackY - trackH / 2, thumbX - rect.Left, trackH, 2, 2, fillPaint);

        // Thumb
        using var thumbPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var thumbBorder = new SKPaint { Color = new SKColor(0, 120, 215), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawCircle(thumbX, trackY, 7, thumbPaint);
        canvas.DrawCircle(thumbX, trackY, 7, thumbBorder);

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Range));
    }

    /// <summary>&lt;input type=file&gt;: a "Choose File" button plus the chosen filename(s) summary.</summary>
    private static void PaintFileInput(SKCanvas canvas, LayoutNode node, SKRect rect)
    {
        var btnW = 90f;
        var btnRect = new SKRect(rect.Left, rect.Top, rect.Left + Math.Min(btnW, rect.Width), rect.Bottom);

        using var btnBg = new SKPaint { Color = new SKColor(0xE1, 0xE1, 0xE1), IsAntialias = true };
        canvas.DrawRect(btnRect, btnBg);
        using var btnBorder = new SKPaint { Color = new SKColor(0x9A, 0x9A, 0x9A), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(btnRect, btnBorder);

        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont { Size = 12 };
        canvas.DrawText("Choose File", btnRect.Left + 6, btnRect.MidY + 4, SKTextAlign.Left, font, textPaint);

        var files = FormState.GetFiles(node.NodeKey);
        var label = files.Count == 0 ? "No file chosen"
            : files.Count == 1 ? files[0].Name
            : $"{files.Count} files";
        using var labelPaint = new SKPaint { Color = new SKColor(0x40, 0x40, 0x40), IsAntialias = true };
        canvas.DrawText(label, btnRect.Right + 8, rect.MidY + 4, SKTextAlign.Left, font, labelPaint);

        _hitRegions.Add(new HitRegion(btnRect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.FileOpen));
    }

    private static float ParseAttrFloat(LayoutNode node, string name, float fallback)
    {
        if (node.Attributes.TryGetValue(name, out var s) &&
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return fallback;
    }

    /// <summary>HTMLProgressElement: a track with a determinate filled portion (value/max).
    /// A progress with no <c>value</c> attribute is indeterminate and paints only the track.</summary>
    private static void PaintProgress(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;
        var track = new SKColor(0xC8, 0xC8, 0xC8);
        var fill = new SKColor(0x00, 0x78, 0xD7);

        using var trackPaint = new SKPaint { Color = track, IsAntialias = false };
        canvas.DrawRect(rect, trackPaint);

        // Indeterminate when there is no value attribute (the bar position is unknown).
        if (!node.Attributes.ContainsKey("value")) return;

        var max = ParseAttrFloat(node, "max", 1f);
        if (max <= 0) max = 1f;
        var value = Math.Clamp(ParseAttrFloat(node, "value", 0f), 0f, max);
        var ratio = value / max;

        using var fillPaint = new SKPaint { Color = fill, IsAntialias = false };
        canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + rect.Width * ratio, rect.Bottom), fillPaint);
    }

    /// <summary>HTMLMeterElement: a gauge whose filled portion is value within [min,max], coloured
    /// green/yellow/red by how far the value sits from the optimum region (HTML §4.10.14).</summary>
    private static void PaintMeter(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;
        using var trackPaint = new SKPaint { Color = new SKColor(0xE6, 0xE6, 0xE6), IsAntialias = false };
        canvas.DrawRect(rect, trackPaint);

        var min = ParseAttrFloat(node, "min", 0f);
        var max = ParseAttrFloat(node, "max", 1f);
        if (max < min) max = min;
        var value = Math.Clamp(ParseAttrFloat(node, "value", 0f), min, max);
        var low = Math.Clamp(ParseAttrFloat(node, "low", min), min, max);
        var high = Math.Clamp(ParseAttrFloat(node, "high", max), low, max);
        var optimum = Math.Clamp(ParseAttrFloat(node, "optimum", (min + max) / 2f), min, max);

        var fill = MeterColor(value, low, high, optimum);
        var span = max - min;
        var ratio = span > 0 ? (value - min) / span : 0f;

        using var fillPaint = new SKPaint { Color = fill, IsAntialias = false };
        canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + rect.Width * ratio, rect.Bottom), fillPaint);
    }

    /// <summary>Picks the meter bar colour: green when the value is in the same band as the optimum,
    /// yellow one band away, red two bands away (the bands are [min,low], (low,high], (high,max]).</summary>
    private static SKColor MeterColor(float value, float low, float high, float optimum)
    {
        var green = new SKColor(0x4C, 0xAF, 0x50);
        var yellow = new SKColor(0xE6, 0xC2, 0x29);
        var red = new SKColor(0xD9, 0x3A, 0x2B);

        int Band(float v) => v <= low ? 0 : v <= high ? 1 : 2;
        var optBand = Band(optimum);
        var valBand = Band(value);
        var dist = Math.Abs(valBand - optBand);
        return dist == 0 ? green : dist == 1 ? yellow : red;
    }

    private static void PaintTextarea(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;
        var isFocused = FormState.FocusedInput == node.NodeKey;

        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = isFocused ? new SKColor(0, 120, 215) : new SKColor(150, 150, 150),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isFocused ? 2f : 1f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, borderPaint);

        node.Attributes.TryGetValue("value", out var defaultVal);
        var text = FormState.GetTextValue(node.NodeKey, defaultVal);

        if (string.IsNullOrEmpty(text) && node.Attributes.TryGetValue("placeholder", out var ph))
        {
            using var phPaint = new SKPaint { Color = new SKColor(170, 170, 170), IsAntialias = true };
            using var phFont = new SKFont(TextMeasure.SystemTypeface(PlatformFonts.Monospace), 13);
            canvas.DrawText(ph, rect.Left + 4, rect.Top + 16, SKTextAlign.Left, phFont, phPaint);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            using var textPaint = new SKPaint { Color = ControlTextColor(node), IsAntialias = true };
            using var textFont = new SKFont(TextMeasure.SystemTypeface(PlatformFonts.Monospace), 13);
            var lines = text.Split('\n');
            var lineY = rect.Top + 16;
            foreach (var line in lines)
            {
                if (lineY > rect.Bottom) break;
                canvas.DrawText(line, rect.Left + 4, lineY, SKTextAlign.Left, textFont, textPaint);
                lineY += 18;
            }
        }

        _hitRegions.Add(new HitRegion(rect, CursorType.Text, NodeKey: node.NodeKey, InputAction: InputAction.TextInput));
    }

    private static void PaintSelect(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;

        using var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(rect, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(150, 150, 150),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, borderPaint);

        // Display selected option text
        node.Attributes.TryGetValue("value", out var defaultVal);
        node.Attributes.TryGetValue("_options", out var optionsStr);
        node.Attributes.TryGetValue("_optionValues", out var optValStr);

        var selectedVal = FormState.GetTextValue(node.NodeKey, defaultVal);
        var displayText = selectedVal;
        if (optionsStr != null && optValStr != null)
        {
            var opts = optionsStr.Split('|');
            var vals = optValStr.Split('|');
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i] == selectedVal && i < opts.Length) { displayText = opts[i]; break; }
            }
        }

        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var textFont = new SKFont { Size = 13 };
        canvas.DrawText(displayText, rect.Left + 4, rect.MidY + 5, SKTextAlign.Left, textFont, textPaint);

        // Drop-down arrow
        var arrowX = rect.Right - 16;
        using var arrowPaint = new SKPaint { Color = new SKColor(100, 100, 100), IsAntialias = true, Style = SKPaintStyle.Fill };
        var path = new SKPath();
        path.MoveTo(arrowX, rect.MidY - 3);
        path.LineTo(arrowX + 10, rect.MidY - 3);
        path.LineTo(arrowX + 5, rect.MidY + 4);
        path.Close();
        canvas.DrawPath(path, arrowPaint);

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.SelectDropdown));

        // Defer dropdown overlay to draw on top of everything
        if (FormState.OpenDropdown == node.NodeKey)
            _pendingDropdown = (node, rect, selectedVal);
    }

    private static void DrawDropdownOverlay(SKCanvas canvas, LayoutNode node, SKRect rect, string selectedVal)
    {
        node.Attributes.TryGetValue("_options", out var optionsStr);
        node.Attributes.TryGetValue("_optionValues", out var optValStr);
        if (optionsStr == null || optValStr == null) return;

        var opts = optionsStr.Split('|');
        var vals = optValStr.Split('|');
        var itemH = 24f;
        var dropW = rect.Width;
        var dropH = opts.Length * itemH;
        var dropRect = new SKRect(rect.Left, rect.Bottom, rect.Left + dropW, rect.Bottom + dropH);

        // Shadow
        using var shadowPaint = new SKPaint { Color = new SKColor(0, 0, 0, 40), IsAntialias = true };
        canvas.DrawRect(new SKRect(dropRect.Left + 2, dropRect.Top + 2, dropRect.Right + 2, dropRect.Bottom + 2), shadowPaint);

        // Background
        using var dropBg = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(dropRect, dropBg);
        using var dropBorder = new SKPaint { Color = new SKColor(150, 150, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(dropRect, dropBorder);

        using var optFont = new SKFont { Size = 13 };
        for (int i = 0; i < opts.Length; i++)
        {
            var optY = dropRect.Top + i * itemH;
            var optRect = new SKRect(dropRect.Left, optY, dropRect.Right, optY + itemH);

            // Highlight selected
            if (vals[i] == selectedVal)
            {
                using var hlPaint = new SKPaint { Color = new SKColor(0, 120, 215, 30) };
                canvas.DrawRect(optRect, hlPaint);
            }

            using var optPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            canvas.DrawText(opts[i], optRect.Left + 4, optRect.Top + 17, SKTextAlign.Left, optFont, optPaint);

            // Each option is a clickable hit region — store index in SelectOptionMap
            _hitRegions.Add(new HitRegion(optRect, CursorType.Pointer, NodeKey: node.NodeKey,
                InputAction: InputAction.SelectDropdown));
            SelectOptionMap[_hitRegions.Count - 1] = i;
        }
    }

    // -------------------------------------------------------------------------
    // Button
    // -------------------------------------------------------------------------

    private static void PaintButton(SKCanvas canvas, LayoutNode node)
    {
        var box = node.Box;
        var rect = box.ContentBox;

        // RenderedText keeps the label consistent with layout (text-transform, and the §16.2
        // subset reversal inside an RTL override scope); the value attribute falls through.
        var btnLabel = node.RenderedText();
        if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
        if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";

        DrawBoxShadows(canvas, box, node);

        var bgColor = node.GetBackgroundColor();
        var hasAuthorBackground = node.TryResolveStyle("background-color", out var authoredBackground)
            ? !string.IsNullOrWhiteSpace(authoredBackground)
            : !string.IsNullOrWhiteSpace(node.Style.GetPropertyValueSafe("background-color"));
        if (bgColor.Alpha == 0 && !hasAuthorBackground)
            bgColor = new SKColor(225, 225, 225);
        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
        var (brx, bry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
        if (brx > 0 || bry > 0) canvas.DrawRoundRect(box.PaddingBox, brx, bry, bgPaint);
        else canvas.DrawRect(box.PaddingBox, bgPaint);

        DrawBorders(canvas, node.Box, node);

        using var btnFont = TextMeasure.CreateFont(node);
        var textColor = node.GetColor();
        using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };
        canvas.DrawText(btnLabel, rect.Left + FormLayout.ButtonPaddingX, rect.MidY + btnFont.Size * 0.35f,
                        SKTextAlign.Left, btnFont, textPaint);

        _hitRegions.Add(new HitRegion(box.BorderBox, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Button));
    }

    // -------------------------------------------------------------------------
    // Horizontal rule
    // -------------------------------------------------------------------------

    private static void PaintHorizontalRule(SKCanvas canvas, LayoutNode node)
    {
        var box = node.Box;
        var y = box.BorderBox.Top + box.Border.Top / 2f;
        var color = node.GetBorderTopColor();
        var thick = box.Border.Top > 0 ? box.Border.Top : 1f;

        using var paint = new SKPaint { Color = color, StrokeWidth = thick, IsAntialias = false };
        canvas.DrawLine(box.BorderBox.Left, y, box.BorderBox.Right, y, paint);
    }
}
