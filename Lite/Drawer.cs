using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Rendering;
using SkiaSharp;

namespace Lite;

internal static class Drawer
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

    /// <summary>Bitmap backing the pixel pointer returned by <see cref="Draw"/>; kept alive
    /// until the next Draw call so the Win32 blit never reads freed memory.</summary>
    private static SKBitmap? _lastBitmap;

    public static (IntPtr Pixels, List<HitRegion> HitRegions) Draw(int width, int height, LayoutNode root, Viewport viewport)
    {
        _lastBitmap?.Dispose();
        _lastBitmap = DrawToBitmap(width, height, root, viewport);
        return (_lastBitmap.GetPixels(), _hitRegions);
    }

    /// <summary>Renders the page to a bitmap the caller owns. Used by the conformance
    /// harness for reftest pixel comparison.</summary>
    internal static SKBitmap DrawToBitmap(int width, int height, LayoutNode root, Viewport viewport)
    {
        // Layout pass — compute node.Box for every node
        BoxEngine.Layout(root, width, height);

        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(imageInfo);
        var canvas = new SKCanvas(bitmap);

        // Propagate the root/body background to the viewport canvas (CSS 2.1 §14.2). When neither
        // sets a background the canvas is the UA default — white, as in browsers (not a grey wash).
        var body = root.Children.FirstOrDefault(c => c.TagName == "BODY") ?? root;
        var clearColor = root.GetBackgroundColor();
        if (clearColor == SKColors.Transparent) clearColor = body.GetBackgroundColor();
        if (clearColor == SKColors.Transparent) clearColor = SKColors.White;
        canvas.Clear(clearColor);
        _hitRegions = [];
        SelectOptionMap.Clear();
        _pendingDropdown = null;
        _viewportScrollY = viewport.ScrollY;
        _viewportHeight = height;
        _viewportWidth = width;

        canvas.Save();
        canvas.Translate(0, -viewport.ScrollY);

        PaintNode(canvas, root, width);

        // Draw deferred dropdown overlay on top of all content (but within scroll context)
        if (_pendingDropdown is var (ddNode, ddRect, ddSelected))
            DrawDropdownOverlay(canvas, ddNode, ddRect, ddSelected);

        canvas.Restore();

        // Paint position:fixed nodes after restoring scroll so they stay on screen
        PaintFixedNodes(canvas, root, width);

        viewport.ContentHeight = root.Box.MarginBox.Bottom;
        DrawScrollbar(canvas, viewport, width, height);

        return bitmap;
    }

    // -------------------------------------------------------------------------
    // Scrollbar
    // -------------------------------------------------------------------------

    private static void DrawScrollbar(SKCanvas canvas, Viewport viewport, int width, int height)
    {
        if (viewport.ContentHeight <= viewport.ViewportHeight) return;

        const float barWidth = 6f;
        const float margin = 2f;
        var ratio = viewport.ViewportHeight / viewport.ContentHeight;
        var trackH = viewport.ViewportHeight;
        var thumbH = Math.Max(trackH * ratio, 24f);
        var thumbTop = viewport.ScrollY / viewport.ContentHeight * trackH;
        var x = width - barWidth - margin;

        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true };
        canvas.DrawRoundRect(x, thumbTop + margin, barWidth, thumbH - margin * 2, 3, 3, paint);
    }

    private static void DrawElementScrollbar(SKCanvas canvas, LayoutNode node, ElementScrollState ss)
    {
        var box = node.Box.PaddingBox;
        var barW = ElementScrollState.BarWidth;
        var barM = ElementScrollState.BarMargin;
        var x = box.Right - barW - barM;
        var thumbH = ss.ThumbHeight;
        var thumbTop = ss.ThumbTop(box.Top);

        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true };
        canvas.DrawRoundRect(x, thumbTop + barM, barW, thumbH - barM * 2, 3, 3, paint);
    }

    // -------------------------------------------------------------------------
    // Paint tree
    // -------------------------------------------------------------------------

    private static void PaintFixedNodes(SKCanvas canvas, LayoutNode root, int viewportWidth)
    {
        var stack = new Stack<LayoutNode>();
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        for (int i = root.Children.Count - 1; i >= 0; i--)
            stack.Push(root.Children[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.GetPosition() == PositionType.Fixed)
                PaintNode(canvas, node, viewportWidth);
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

        // position:relative — translate by offset, paint normally, then restore
        var pos = node.GetPosition();
        if (pos == PositionType.Relative)
        {
            var fontSize = node.GetFontSize();
            var t = node.GetOffsetTop(node.Box.ContentBox.Height, fontSize);
            var l = node.GetOffsetLeft(node.Box.ContentBox.Width, fontSize);
            var r = node.GetOffsetRight(node.Box.ContentBox.Width, fontSize);
            var b = node.GetOffsetBottom(node.Box.ContentBox.Height, fontSize);
            var dx = !float.IsNaN(l) ? l : !float.IsNaN(r) ? -r : 0f;
            var dy = !float.IsNaN(t) ? t : !float.IsNaN(b) ? -b : 0f;
            if (dx != 0f || dy != 0f)
            {
                canvas.Save();
                canvas.Translate(dx, dy);
                PaintNodeInner(canvas, node, viewportWidth);
                canvas.Restore();
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
            canvas.Concat(ref m);
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

        // CSS `clip: rect(...)` — clips an absolutely-positioned element to a rectangle
        // relative to its border box (CSS 2.1 §11.1.2).
        var cssClipRect = node.GetClipRect();
        bool cssClipped = cssClipRect is not null;
        if (cssClipped)
        {
            canvas.Save();
            canvas.ClipRect(cssClipRect!.Value);
        }

        // Apply per-element scroll offset
        var scrollState = node.ScrollState;
        if (scrollState != null && scrollState.NeedsScrollbar)
        {
            // Draw background, borders, and shadows at their fixed layout position
            // before the scroll translate so they don't scroll away with the content.
            PaintBlockDecorations(canvas, node);
            canvas.Translate(0, -scrollState.ScrollY);
        }

        PaintNodeContent(canvas, node, viewportWidth);

        // Draw per-element scrollbar (inside the clip, after restoring scroll translate)
        if (scrollState != null && scrollState.NeedsScrollbar)
        {
            canvas.Translate(0, scrollState.ScrollY); // Undo scroll offset for scrollbar
            DrawElementScrollbar(canvas, node, scrollState);
        }

        if (cssClipped) canvas.Restore();
        if (opacity < 1f || clip) canvas.Restore();
        if (filterPaint != null) { canvas.Restore(); filterPaint.Dispose(); }
        if (transform != null) canvas.Restore();
    }

    private static void PaintNodeContent(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var display = node.GetDisplay();

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

        if (display == DisplayType.ListItem)
        {
            PaintListItem(canvas, node, viewportWidth);
            return;
        }

        if (display == DisplayType.Block || display == DisplayType.Flex || display == DisplayType.InlineFlex
            || display == DisplayType.Table || display == DisplayType.InlineTable
            || display == DisplayType.TableRowGroup || display == DisplayType.TableRow || display == DisplayType.TableCell)
        {
            PaintBlock(canvas, node, viewportWidth);
            return;
        }

        // Generic inline: if this node carries text (e.g. <strong>, <em>, <mark>, <code>, #TEXT)
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var bgColor = node.GetBackgroundColor();
            if (bgColor != SKColors.Transparent)
            {
                using var bgPaint = new SKPaint { Color = bgColor };
                canvas.DrawRect(node.Box.ContentBox, bgPaint);
            }
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText,
                node.Box.ContentBox.Left, node.Box.ContentBox.Top,
                node.Box.ContentBox.Width, font, paint);
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
        var box = node.Box;
        DrawBoxShadows(canvas, box, node);
        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else canvas.DrawRect(box.PaddingBox, bgPaint);
        }
        var gradient = node.GetLinearGradient();
        if (gradient != null)
            DrawLinearGradient(canvas, box, node, gradient);
        DrawBackgroundImage(canvas, node, box);
        DrawBorders(canvas, box, node);
        DrawOutline(canvas, box, node);
    }

    /// <summary>CSS 2.1 §17.6.1.1: with <c>empty-cells: hide</c>, a table cell with no in-flow
    /// content paints neither background nor borders.</summary>
    private static bool IsEmptyCellHidden(LayoutNode node)
    {
        if (node.GetDisplay() != DisplayType.TableCell) return false;
        var ec = node.TryResolveStyle("empty-cells", out var v) ? v : node.Style.GetPropertyValueSafe("empty-cells");
        if (ec?.Trim() != "hide") return false;
        bool hasContent = !string.IsNullOrWhiteSpace(node.DisplayText)
            || node.Children.Any(c => c.TagName != "#text" || !string.IsNullOrWhiteSpace(c.DisplayText));
        return !hasContent;
    }

    private static void PaintBlock(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        // Decorations are drawn before the scroll translate for scrollable elements;
        // skip them here to avoid painting them twice. empty-cells:hide also suppresses them.
        if (node.ScrollState?.NeedsScrollbar != true && !IsEmptyCellHidden(node))
        {
            DrawBoxShadows(canvas, box, node);

            // Background
            var bgColor = node.GetBackgroundColor();
            if (bgColor != SKColors.Transparent)
            {
                using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
                var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
                if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
                else canvas.DrawRect(box.PaddingBox, bgPaint);
            }

            // Linear gradient background
            var gradient = node.GetLinearGradient();
            if (gradient != null)
                DrawLinearGradient(canvas, box, node, gradient);

            // Background image
            DrawBackgroundImage(canvas, node, box);

            DrawBorders(canvas, box, node);
            DrawOutline(canvas, box, node);
        }

        if (!node.GetPointerEventsNone())
        {
            var cursor = node.GetCursor();
            _hitRegions.Add(new HitRegion(box.BorderBox, cursor, NodeKey: node.NodeKey));
        }

        // Draw own text content (block elements like <div>Text</div> with no child nodes)
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
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
                var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(box.ContentBox.Width, 1f), font, ws, lh);
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

            DrawWrappedText(canvas, node, node.DisplayText,
                            textX, textY, textMaxW, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    /// <summary>
    /// An element establishes a stacking context (painted atomically, ordered by z-index within
    /// its parent context) when it is positioned (relative/absolute/sticky), has opacity &lt; 1,
    /// or has a transform. position:fixed is excluded — those are painted by PaintFixedNodes.
    ///
    /// Pragmatic model of CSS 2.1 Appendix E: ALL positioned elements are treated as stacking
    /// contexts. This correctly flattens and z-orders positioned descendants across nesting
    /// levels (the main gap of the old per-parent sort). The one unmodeled subtlety is the
    /// z-index:auto case where a positioned-auto element's own positioned descendants are meant
    /// to join the PARENT context rather than be isolated within it.
    /// </summary>
    private static bool EstablishesStackingContext(LayoutNode n)
    {
        var pos = n.GetPosition();
        if (pos is PositionType.Absolute or PositionType.Relative or PositionType.Sticky) return true;
        if (n.GetOpacity() < 1f) return true;
        if (n.GetTransform() != null) return true;
        return false;
    }

    /// <summary>Collects stacking-context descendants of <paramref name="node"/> in document
    /// order, descending through non-stacking, non-fixed boxes but stopping at (and including)
    /// each stacking context — so deep positioned elements flatten into their nearest ancestor
    /// stacking context. position:fixed is skipped (PaintFixedNodes owns it).</summary>
    private static void CollectStackingItems(LayoutNode node, List<LayoutNode> items)
    {
        foreach (var child in node.Children)
        {
            if (child.GetDisplay() == DisplayType.None) continue;
            if (child.GetPosition() == PositionType.Fixed) continue;
            if (EstablishesStackingContext(child)) items.Add(child); // atomic — don't descend
            else CollectStackingItems(child, items);
        }
    }

    /// <summary>An in-flow inline-level atomic box (inline-block / inline-table / inline-flex).
    /// Per CSS 2.1 Appendix E these paint in step 5 — ABOVE in-flow block-level backgrounds
    /// and borders (step 3), unlike Lite's default single doc-order pass.</summary>
    private static bool IsAtomicInlineBox(LayoutNode n)
        => n.GetDisplay() is DisplayType.InlineBlock or DisplayType.InlineTable or DisplayType.InlineFlex;

    /// <summary>Collects the in-flow atomic inline boxes of a stacking context (descending through
    /// non-stacking, non-fixed boxes, stopping at each atomic inline and each nested stacking
    /// context) so the SC root can paint them in Appendix-E step 5.</summary>
    private static void CollectDeferredInlines(LayoutNode node, List<LayoutNode> items)
    {
        foreach (var child in node.Children)
        {
            if (child.GetDisplay() == DisplayType.None) continue;
            if (child.GetPosition() == PositionType.Fixed) continue;
            if (EstablishesStackingContext(child)) continue;   // painted by the SC collection (step 6)
            if (IsAtomicInlineBox(child)) items.Add(child);    // step 5 — don't descend
            else CollectDeferredInlines(child, items);
        }
    }

    /// <summary>
    /// Paints a node's descendants per CSS 2.1 Appendix E. The node's own background/borders are
    /// already painted by the caller, so the order here is: negative-z stacking contexts → in-flow
    /// non-positioned content → zero/positive-z stacking contexts (and positioned descendants).
    /// A stacking-context root flattens & z-orders ALL positioned descendants in its subtree;
    /// non-root nodes only paint their in-flow (non-stacking) children, since their positioned
    /// descendants were already collected by the nearest ancestor stacking-context root.
    /// </summary>
    private static void PaintChildrenSorted(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var isFlex = node.GetDisplay() is DisplayType.Flex or DisplayType.InlineFlex;
        IEnumerable<LayoutNode> ordered = isFlex ? node.Children.OrderBy(c => c.GetOrder()) : node.Children;

        // The root element (no parent) and any stacking-context-establishing box are roots.
        bool isScRoot = node.Parent is null || EstablishesStackingContext(node);

        void PaintInFlow()
        {
            foreach (var child in ordered)
            {
                if (child.GetPosition() == PositionType.Fixed) continue;       // PaintFixedNodes
                if (EstablishesStackingContext(child)) continue;               // handled by SC root
                PaintNode(canvas, child, viewportWidth);
            }
        }

        if (!isScRoot)
        {
            PaintInFlow();
            return;
        }

        var items = new List<LayoutNode>();
        CollectStackingItems(node, items);
        // OrderBy is stable, so equal z-indices keep document order.
        var negativeZ = items.Where(c => c.GetZIndex() < 0).OrderBy(c => c.GetZIndex()).ToList();
        var nonNegativeZ = items.Where(c => c.GetZIndex() >= 0).OrderBy(c => c.GetZIndex()).ToList();

        // Appendix E: in-flow atomic inline boxes (step 5) paint after in-flow block content
        // (step 3/4) but before z-index:auto/positive stacking contexts (step 6).
        var deferredInlines = new List<LayoutNode>();
        CollectDeferredInlines(node, deferredInlines);

        foreach (var c in negativeZ) PaintNode(canvas, c, viewportWidth);

        var savedDeferred = _deferredInlines;
        _deferredInlines = deferredInlines.Count > 0 ? new HashSet<LayoutNode>(deferredInlines) : null;
        PaintInFlow();
        _deferredInlines = savedDeferred;

        foreach (var c in deferredInlines) PaintNode(canvas, c, viewportWidth); // step 5
        foreach (var c in nonNegativeZ) PaintNode(canvas, c, viewportWidth);
    }

    // -------------------------------------------------------------------------
    // Text elements (H1-H6, P, LABEL)
    // -------------------------------------------------------------------------

    private static void PaintTextBlock(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        DrawBorders(canvas, box, node);
        DrawOutline(canvas, box, node);

        var cursor = node.GetCursor();
        _hitRegions.Add(new HitRegion(box.MarginBox, cursor, node.Href, NodeKey: node.NodeKey));

        if (string.IsNullOrEmpty(node.DisplayText)) return;

        using var font = TextMeasure.CreateFont(node);
        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

        DrawWrappedText(canvas, node, node.DisplayText,
                        box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Width, font, paint);
    }

    // -------------------------------------------------------------------------
    // Anchor
    // -------------------------------------------------------------------------

    private static void PaintAnchor(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var cursor = node.GetCursor();
        _hitRegions.Add(new HitRegion(node.Box.BorderBox, cursor, node.Href, NodeKey: node.NodeKey));

        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var box = node.Box;
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText, box.ContentBox.Left, box.ContentBox.Top,
                            box.ContentBox.Width, font, paint);
            return;
        }

        // Anchor wraps child nodes (e.g. #TEXT created by parser for mixed content)
        PaintChildrenSorted(canvas, node, viewportWidth);
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

        // Snapshot the parent's paint statics — the recursive DrawToBitmap clobbers them.
        var savedHit = _hitRegions;
        var savedScrollY = _viewportScrollY;
        var savedH = _viewportHeight;
        var savedW = _viewportWidth;
        var savedSelect = new Dictionary<int, int>(SelectOptionMap);
        var savedDropdown = _pendingDropdown;

        SKBitmap childBmp;
        try { childBmp = DrawToBitmap(cw, ch, page.Root, page.Viewport); }
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
            canvas.DrawBitmap(childBmp, content.Left, content.Top);
            canvas.Restore();
        }
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    private static void PaintInput(SKCanvas canvas, LayoutNode node)
    {
        var rect = node.Box.ContentBox;
        var inputType = node.Attributes.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "text";

        switch (inputType)
        {
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
        var displayText = isPassword && !string.IsNullOrEmpty(text)
            ? new string('\u2022', text.Length)
            : text;

        if (string.IsNullOrEmpty(text) && node.Attributes.TryGetValue("placeholder", out var ph))
        {
            using var phPaint = new SKPaint { Color = new SKColor(170, 170, 170), IsAntialias = true };
            using var phFont = new SKFont { Size = 12 };
            canvas.DrawText(ph, rect.Left + 4, rect.Top + 14, SKTextAlign.Left, phFont, phPaint);
        }
        else if (!string.IsNullOrEmpty(displayText))
        {
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var textFont = new SKFont { Size = 12 };
            canvas.DrawText(displayText, rect.Left + 4, rect.Top + 14, SKTextAlign.Left, textFont, textPaint);
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
            using var caretFont = new SKFont { Size = 12 };
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
            using var phFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 13);
            canvas.DrawText(ph, rect.Left + 4, rect.Top + 16, SKTextAlign.Left, phFont, phPaint);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var textFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 13);
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

        var btnLabel = node.DisplayText;
        if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
        if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";

        DrawBoxShadows(canvas, box, node);

        var bgColor = node.GetBackgroundColor();
        if (bgColor == SKColors.Transparent) bgColor = new SKColor(225, 225, 225);
        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
        var (brx, bry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
        if (brx > 0 || bry > 0) canvas.DrawRoundRect(box.PaddingBox, brx, bry, bgPaint);
        else canvas.DrawRect(box.PaddingBox, bgPaint);

        DrawBorders(canvas, node.Box, node);

        using var btnFont = new SKFont { Size = 13 };
        var textColor = node.GetColor();
        using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };
        canvas.DrawText(btnLabel, rect.Left + FormLayout.ButtonPaddingX, rect.Top + FormLayout.ButtonPaddingY + 13,
                        SKTextAlign.Left, btnFont, textPaint);

        _hitRegions.Add(new HitRegion(rect, CursorType.Pointer, NodeKey: node.NodeKey, InputAction: InputAction.Button));
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

    // -------------------------------------------------------------------------
    // List item
    // -------------------------------------------------------------------------

    private static void PaintListItem(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        // Background
        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        DrawBorders(canvas, box, node);

        var listStyleType = node.GetListStyleType();
        var listStylePos = node.GetListStylePosition();
        float insideMarkerWidth = 0f; // track width consumed by inside marker

        // list-style-image: when set and loadable, the image replaces the bullet/number marker.
        var listImageUrl = node.GetListStyleImage();
        SKBitmap? listImage = null;
        if (listImageUrl is not null)
        {
            if (!_bgImageCache.TryGetValue(listImageUrl, out listImage))
            {
                listImage = ResourceLoader.FetchImage(listImageUrl, Parser.BaseUrl);
                _bgImageCache[listImageUrl] = listImage;
            }
        }

        if (listImage is not null)
        {
            var lineH = node.GetLineHeight(node.GetFontSize());
            float imgW = listImage.Width, imgH = listImage.Height;
            var dest = listStylePos == ListStylePosition.Inside
                ? new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + imgW, box.ContentBox.Top + imgH)
                : new SKRect(box.ContentBox.Left - imgW - 4f, box.ContentBox.Top, box.ContentBox.Left - 4f, box.ContentBox.Top + imgH);
            using var imgPaint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(listImage, dest, imgPaint);
            if (listStylePos == ListStylePosition.Inside) insideMarkerWidth = imgW + 4f;
        }
        else if (listStyleType != ListStyleType.None)
        {
            using var markerFont = TextMeasure.CreateFont(node);
            using var markerPaint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

            var markerText = GetMarkerText(listStyleType, node);
            var ascent = -markerFont.Metrics.Ascent;

            if (listStylePos == ListStylePosition.Inside)
            {
                // Inside: marker is part of content flow — draw it and offset content
                if (!string.IsNullOrEmpty(markerText))
                {
                    var markerStr = markerText + " ";
                    insideMarkerWidth = markerFont.MeasureText(markerStr);
                    canvas.DrawText(markerStr, box.ContentBox.Left, box.ContentBox.Top + ascent,
                                    SKTextAlign.Left, markerFont, markerPaint);
                }
                else
                {
                    var bulletSize = markerFont.Size * 0.2f;
                    insideMarkerWidth = bulletSize * 2 + markerFont.Size * 0.3f + 4f;
                    DrawBulletMarker(canvas, listStyleType, box.ContentBox.Left + markerFont.Size * 0.3f,
                                     box.ContentBox.Top + markerFont.Size * 0.55f, markerFont.Size, markerPaint);
                }
            }
            else
            {
                // Outside: marker is drawn to the left of content box
                var markerX = box.ContentBox.Left - 6f;
                var markerY = box.ContentBox.Top;

                if (!string.IsNullOrEmpty(markerText))
                {
                    canvas.DrawText(markerText, markerX, markerY + ascent,
                                    SKTextAlign.Right, markerFont, markerPaint);
                }
                else
                {
                    DrawBulletMarker(canvas, listStyleType, markerX - markerFont.Size * 0.2f * 2,
                                     markerY + markerFont.Size * 0.55f, markerFont.Size, markerPaint);
                }
            }
        }

        // Paint own text — offset by inside marker width if applicable
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.DisplayText,
                            box.ContentBox.Left + insideMarkerWidth, box.ContentBox.Top,
                            box.ContentBox.Width - insideMarkerWidth, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    private static string? GetMarkerText(ListStyleType type, LayoutNode node)
    {
        var index = GetOrderedIndex(node);
        return type switch
        {
            ListStyleType.Decimal => $"{index}.",
            ListStyleType.DecimalLeadingZero => $"{index:D2}.",
            ListStyleType.LowerAlpha => $"{(char)('a' + (index - 1) % 26)}.",
            ListStyleType.UpperAlpha => $"{(char)('A' + (index - 1) % 26)}.",
            ListStyleType.LowerRoman => $"{ToRoman(index).ToLowerInvariant()}.",
            ListStyleType.UpperRoman => $"{ToRoman(index)}.",
            _ => null, // bullet types return null
        };
    }

    private static void DrawBulletMarker(SKCanvas canvas, ListStyleType type, float cx, float cy, float fontSize, SKPaint paint)
    {
        var radius = fontSize * 0.2f;
        switch (type)
        {
            case ListStyleType.Disc:
                canvas.DrawCircle(cx, cy, radius, paint);
                break;
            case ListStyleType.Circle:
                using (var strokePaint = new SKPaint { Color = paint.Color, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true })
                    canvas.DrawCircle(cx, cy, radius, strokePaint);
                break;
            case ListStyleType.Square:
                canvas.DrawRect(cx - radius, cy - radius, radius * 2, radius * 2, paint);
                break;
        }
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999) return number.ToString();
        var sb = new System.Text.StringBuilder();
        int[] values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] syms = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        for (int i = 0; i < values.Length; i++)
        {
            while (number >= values[i]) { sb.Append(syms[i]); number -= values[i]; }
        }
        return sb.ToString();
    }

    private static bool IsInsideOrderedList(LayoutNode node)
    {
        var p = node.Parent;
        while (p != null)
        {
            if (p.TagName == "OL") return true;
            if (p.TagName == "UL") return false;
            p = p.Parent;
        }
        return false;
    }

    private static int GetOrderedIndex(LayoutNode node)
    {
        if (node.Parent == null) return 1;
        var index = 1;
        foreach (var sibling in node.Parent.Children)
        {
            if (sibling == node) break;
            if (sibling.TagName == "LI") index++;
        }
        return index;
    }

    // -------------------------------------------------------------------------
    // Shadows
    // -------------------------------------------------------------------------

    private static void DrawBoxShadows(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var shadows = node.GetBoxShadows();
        if (shadows.Count == 0) return;

        var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);

        // Paint shadows in reverse order (last layer first, per CSS spec)
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            var s = shadows[i];
            if (s.Inset) continue;   // inset shadows not yet supported

            var sigma = s.Blur / 2f;
            var shadowRect = SKRect.Inflate(box.PaddingBox, s.Spread, s.Spread);
            shadowRect.Offset(s.OffsetX, s.OffsetY);
            var srx = Math.Max(0, rx + s.Spread);
            var sry = Math.Max(0, ry + s.Spread);

            using var paint = new SKPaint
            {
                Color = s.Color,
                IsAntialias = true,
                MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
            };

            if (srx > 0 || sry > 0) canvas.DrawRoundRect(shadowRect, srx, sry, paint);
            else canvas.DrawRect(shadowRect, paint);
        }
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static void DrawBorders(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var bw = box.Border;
        var (rx, ry) = node.GetBorderRadius(box.BorderBox.Width, box.BorderBox.Height);

        if (rx > 0 || ry > 0)
        {
            // With border-radius draw a single stroked rounded rect using top-border values
            var maxWidth = Math.Max(Math.Max(bw.Top, bw.Right), Math.Max(bw.Bottom, bw.Left));
            if (maxWidth <= 0) return;
            var style = node.GetBorderStyleTop();
            if (style == BorderStyle.None || style == BorderStyle.Hidden) return;
            using var p = new SKPaint
            {
                Color = node.GetBorderTopColor(),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = maxWidth,
                IsAntialias = true,
            };
            ApplyBorderStyle(p, style);
            var inset = maxWidth / 2f;
            var r = SKRect.Inflate(box.BorderBox, -inset, -inset);
            if (r.Width <= 0 || r.Height <= 0) return;
            canvas.DrawRoundRect(r, Math.Max(0, rx - inset), Math.Max(0, ry - inset), p);
            return;
        }

        DrawBorderSide(canvas, box.BorderBox.Left, box.BorderBox.Top + bw.Top / 2,
                        box.BorderBox.Right, box.BorderBox.Top + bw.Top / 2,
                        bw.Top, node.GetBorderTopColor(), node.GetBorderStyleTop());
        DrawBorderSide(canvas, box.BorderBox.Right - bw.Right / 2, box.BorderBox.Top,
                        box.BorderBox.Right - bw.Right / 2, box.BorderBox.Bottom,
                        bw.Right, node.GetBorderRightColor(), node.GetBorderStyleRight());
        DrawBorderSide(canvas, box.BorderBox.Left, box.BorderBox.Bottom - bw.Bottom / 2,
                        box.BorderBox.Right, box.BorderBox.Bottom - bw.Bottom / 2,
                        bw.Bottom, node.GetBorderBottomColor(), node.GetBorderStyleBottom());
        DrawBorderSide(canvas, box.BorderBox.Left + bw.Left / 2, box.BorderBox.Top,
                        box.BorderBox.Left + bw.Left / 2, box.BorderBox.Bottom,
                        bw.Left, node.GetBorderLeftColor(), node.GetBorderStyleLeft());
    }

    private static void DrawBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2,
                                        float width, SKColor color, BorderStyle style)
    {
        if (width <= 0 || style == BorderStyle.None || style == BorderStyle.Hidden) return;

        if (style == BorderStyle.Double && width >= 3f)
        {
            // Draw two lines with a gap in between
            var lineW = Math.Max(1f, width / 3f);
            var offset = width / 2f - lineW / 2f;
            var isHorizontal = Math.Abs(y1 - y2) < 0.01f;
            using var p = new SKPaint { Color = color, StrokeWidth = lineW, IsAntialias = true };
            if (isHorizontal)
            {
                canvas.DrawLine(x1, y1 - offset, x2, y2 - offset, p);
                canvas.DrawLine(x1, y1 + offset, x2, y2 + offset, p);
            }
            else
            {
                canvas.DrawLine(x1 - offset, y1, x2 - offset, y2, p);
                canvas.DrawLine(x1 + offset, y1, x2 + offset, y2, p);
            }
            return;
        }

        if (style == BorderStyle.Groove || style == BorderStyle.Ridge)
        {
            var halfW = width / 2f;
            var isHorizontal = Math.Abs(y1 - y2) < 0.01f;
            var dark = DarkenColor(color, 0.6f);
            var light = LightenColor(color, 1.4f);
            var c1 = style == BorderStyle.Groove ? dark : light;
            var c2 = style == BorderStyle.Groove ? light : dark;
            using var p1 = new SKPaint { Color = c1, StrokeWidth = halfW, IsAntialias = true };
            using var p2 = new SKPaint { Color = c2, StrokeWidth = halfW, IsAntialias = true };
            if (isHorizontal)
            {
                canvas.DrawLine(x1, y1 - halfW / 2f, x2, y2 - halfW / 2f, p1);
                canvas.DrawLine(x1, y1 + halfW / 2f, x2, y2 + halfW / 2f, p2);
            }
            else
            {
                canvas.DrawLine(x1 - halfW / 2f, y1, x2 - halfW / 2f, y2, p1);
                canvas.DrawLine(x1 + halfW / 2f, y1, x2 + halfW / 2f, y2, p2);
            }
            return;
        }

        if (style == BorderStyle.Inset || style == BorderStyle.Outset)
        {
            var isTop = Math.Abs(y1 - y2) < 0.01f && y1 < (y1 + y2) / 2f + 1; // approximate
            var isLeft = Math.Abs(x1 - x2) < 0.01f && x1 < (x1 + x2) / 2f + 1;
            bool darken = style == BorderStyle.Inset ? (isTop || isLeft) : !(isTop || isLeft);
            var c = darken ? DarkenColor(color, 0.6f) : LightenColor(color, 1.4f);
            using var p = new SKPaint { Color = c, StrokeWidth = width, IsAntialias = true };
            canvas.DrawLine(x1, y1, x2, y2, p);
            return;
        }

        using var paint = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true };
        ApplyBorderStyle(paint, style);
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void ApplyBorderStyle(SKPaint paint, BorderStyle style)
    {
        switch (style)
        {
            case BorderStyle.Dotted:
                paint.PathEffect = SKPathEffect.CreateDash([paint.StrokeWidth, paint.StrokeWidth * 2], 0);
                break;
            case BorderStyle.Dashed:
                paint.PathEffect = SKPathEffect.CreateDash([paint.StrokeWidth * 3, paint.StrokeWidth * 2], 0);
                break;
        }
    }

    private static SKColor DarkenColor(SKColor c, float factor)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red * factor, 0, 255),
            (byte)Math.Clamp(c.Green * factor, 0, 255),
            (byte)Math.Clamp(c.Blue * factor, 0, 255),
            c.Alpha);
    }

    private static SKColor LightenColor(SKColor c, float factor)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red * factor, 0, 255),
            (byte)Math.Clamp(c.Green * factor, 0, 255),
            (byte)Math.Clamp(c.Blue * factor, 0, 255),
            c.Alpha);
    }

    // ---- CSS filter ----

    private static SKPaint? BuildFilterPaint(LayoutNode node)
    {
        var raw = node.GetFilter();
        if (raw == null) return null;

        SKImageFilter? imageFilter = null;
        SKColorFilter? colorFilter = null;

        // Parse each filter function
        var span = raw.AsSpan();
        while (span.Length > 0)
        {
            span = span.TrimStart();
            var parenIdx = span.IndexOf('(');
            if (parenIdx < 0) break;
            var funcName = span[..parenIdx].Trim().ToString().ToLowerInvariant();
            var closeIdx = span.IndexOf(')');
            if (closeIdx < 0) break;
            var argStr = span[(parenIdx + 1)..closeIdx].ToString().Trim();
            span = span[(closeIdx + 1)..];

            switch (funcName)
            {
                case "blur":
                    if (StyleExtensions.TryParseLengthPx(argStr, out var sigma))
                    {
                        var blur = SKImageFilter.CreateBlur(sigma, sigma);
                        imageFilter = imageFilter != null ? SKImageFilter.CreateCompose(imageFilter, blur) : blur;
                    }
                    break;
                case "grayscale":
                    {
                        var amt = ParseFilterAmount(argStr, 1f);
                        amt = Math.Clamp(amt, 0f, 1f);
                        var inv = 1f - amt;
                        var cf = SKColorFilter.CreateColorMatrix([
                            0.2126f + 0.7874f * inv, 0.7152f - 0.7152f * inv, 0.0722f - 0.0722f * inv, 0, 0,
                            0.2126f - 0.2126f * inv, 0.7152f + 0.2848f * inv, 0.0722f - 0.0722f * inv, 0, 0,
                            0.2126f - 0.2126f * inv, 0.7152f - 0.7152f * inv, 0.0722f + 0.9278f * inv, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "sepia":
                    {
                        var amt = ParseFilterAmount(argStr, 1f);
                        amt = Math.Clamp(amt, 0f, 1f);
                        var inv = 1f - amt;
                        var cf = SKColorFilter.CreateColorMatrix([
                            0.393f + 0.607f * inv, 0.769f - 0.769f * inv, 0.189f - 0.189f * inv, 0, 0,
                            0.349f - 0.349f * inv, 0.686f + 0.314f * inv, 0.168f - 0.168f * inv, 0, 0,
                            0.272f - 0.272f * inv, 0.534f - 0.534f * inv, 0.131f + 0.869f * inv, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "brightness":
                    {
                        var b = ParseFilterAmount(argStr, 1f);
                        var cf = SKColorFilter.CreateColorMatrix([
                            b, 0, 0, 0, 0,
                            0, b, 0, 0, 0,
                            0, 0, b, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "contrast":
                    {
                        var c = ParseFilterAmount(argStr, 1f);
                        var t = (1f - c) / 2f;
                        var cf = SKColorFilter.CreateColorMatrix([
                            c, 0, 0, 0, t,
                            0, c, 0, 0, t,
                            0, 0, c, 0, t,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "saturate":
                    {
                        var s = ParseFilterAmount(argStr, 1f);
                        var inv = 1f - s;
                        var cf = SKColorFilter.CreateColorMatrix([
                            0.2126f + 0.7874f * s, 0.7152f - 0.7152f * s, 0.0722f - 0.0722f * s, 0, 0,
                            0.2126f - 0.2126f * s, 0.7152f + 0.2848f * s, 0.0722f - 0.0722f * s, 0, 0,
                            0.2126f - 0.2126f * s, 0.7152f - 0.7152f * s, 0.0722f + 0.9278f * s, 0, 0,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "hue-rotate":
                    {
                        if (StyleExtensions.TryParseAngle(argStr, out var deg2))
                        {
                            var rad = deg2 * MathF.PI / 180f;
                            var cos = MathF.Cos(rad);
                            var sin = MathF.Sin(rad);
                            var cf = SKColorFilter.CreateColorMatrix([
                                0.213f + cos * 0.787f - sin * 0.213f, 0.715f - cos * 0.715f - sin * 0.715f, 0.072f - cos * 0.072f + sin * 0.928f, 0, 0,
                                0.213f - cos * 0.213f + sin * 0.143f, 0.715f + cos * 0.285f + sin * 0.140f, 0.072f - cos * 0.072f - sin * 0.283f, 0, 0,
                                0.213f - cos * 0.213f - sin * 0.787f, 0.715f - cos * 0.715f + sin * 0.715f, 0.072f + cos * 0.928f + sin * 0.072f, 0, 0,
                                0, 0, 0, 1, 0
                            ]);
                            colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                        }
                    }
                    break;
                case "invert":
                    {
                        var amt = ParseFilterAmount(argStr, 1f);
                        amt = Math.Clamp(amt, 0f, 1f);
                        var cf = SKColorFilter.CreateColorMatrix([
                            1f - 2f * amt, 0, 0, 0, amt,
                            0, 1f - 2f * amt, 0, 0, amt,
                            0, 0, 1f - 2f * amt, 0, amt,
                            0, 0, 0, 1, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
                case "opacity":
                    {
                        var a = ParseFilterAmount(argStr, 1f);
                        a = Math.Clamp(a, 0f, 1f);
                        var cf = SKColorFilter.CreateColorMatrix([
                            1, 0, 0, 0, 0,
                            0, 1, 0, 0, 0,
                            0, 0, 1, 0, 0,
                            0, 0, 0, a, 0
                        ]);
                        colorFilter = colorFilter != null ? SKColorFilter.CreateCompose(cf, colorFilter) : cf;
                    }
                    break;
            }
        }

        if (imageFilter == null && colorFilter == null) return null;

        var paint = new SKPaint { IsAntialias = true };
        if (colorFilter != null)
        {
            var cfImage = SKImageFilter.CreateColorFilter(colorFilter);
            imageFilter = imageFilter != null ? SKImageFilter.CreateCompose(imageFilter, cfImage) : cfImage;
        }
        paint.ImageFilter = imageFilter;
        return paint;
    }

    private static float ParseFilterAmount(string arg, float defaultIfEmpty)
    {
        arg = arg.Trim();
        if (string.IsNullOrEmpty(arg)) return defaultIfEmpty;
        if (arg.EndsWith('%'))
        {
            if (float.TryParse(arg[..^1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
                return pct / 100f;
        }
        if (float.TryParse(arg, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var val))
            return val;
        return defaultIfEmpty;
    }

    // ---- Linear gradient ----

    private static void DrawLinearGradient(SKCanvas canvas, BoxDimensions box, LayoutNode node, LinearGradient gradient)
    {
        var rect = box.PaddingBox;
        var cx = rect.MidX;
        var cy = rect.MidY;
        var w = rect.Width;
        var h = rect.Height;

        // Convert CSS angle to radians (CSS 0deg = to top, clockwise)
        var rad = (float)((gradient.AngleDeg - 90) * Math.PI / 180.0);
        // Gradient line length: project the box diagonal onto the gradient direction
        var diagLen = (float)(Math.Abs(w * Math.Cos(rad)) + Math.Abs(h * Math.Sin(rad))) / 2f;

        var startX = cx - diagLen * (float)Math.Cos(rad);
        var startY = cy - diagLen * (float)Math.Sin(rad);
        var endX = cx + diagLen * (float)Math.Cos(rad);
        var endY = cy + diagLen * (float)Math.Sin(rad);

        // Resolve stop positions (evenly distribute missing positions)
        var count = gradient.Stops.Count;
        var colors = new SKColor[count];
        var positions = new float[count];
        for (var i = 0; i < count; i++)
        {
            colors[i] = gradient.Stops[i].Color;
            positions[i] = gradient.Stops[i].Position ?? (count > 1 ? (float)i / (count - 1) : 0f);
        }

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(startX, startY),
            new SKPoint(endX, endY),
            colors,
            positions,
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        var (rx, ry) = node.GetBorderRadius(w, h);
        if (rx > 0 || ry > 0) canvas.DrawRoundRect(rect, rx, ry, paint);
        else canvas.DrawRect(rect, paint);
    }

    // ---- Background image ----

    private static readonly Dictionary<string, SKBitmap?> _bgImageCache = new();

    private static void DrawBackgroundImage(SKCanvas canvas, LayoutNode node, BoxDimensions box)
    {
        var imageUrl = node.GetBackgroundImage();
        if (imageUrl == null) return;

        if (!_bgImageCache.TryGetValue(imageUrl, out var bitmap))
        {
            bitmap = ResourceLoader.FetchImage(imageUrl, Parser.BaseUrl);
            _bgImageCache[imageUrl] = bitmap;
        }
        if (bitmap == null) return;

        var repeat = node.GetBackgroundRepeat();
        var (posX, posY) = node.GetBackgroundPosition();
        var (sizeW, sizeH) = node.GetBackgroundSize();

        // background-attachment: fixed — the positioning area is the viewport (in document
        // coordinates the canvas is translated by -scrollY, so the viewport's top-left sits at
        // y = scrollY). The painted background is still clipped to the element's padding box, but
        // its placement is independent of where the element scrolled to (CSS 2.1 §14.2.1).
        bool fixedBg = node.IsBackgroundFixed();
        var clipArea = box.PaddingBox;
        var area = fixedBg
            ? new SKRect(0, _viewportScrollY, _viewportWidth, _viewportScrollY + _viewportHeight)
            : box.PaddingBox;

        // Resolve image draw size
        float drawW = bitmap.Width, drawH = bitmap.Height;
        if (sizeW == "cover" || sizeW == "contain")
        {
            var scaleX = area.Width / bitmap.Width;
            var scaleY = area.Height / bitmap.Height;
            var scale = sizeW == "cover" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            drawW = bitmap.Width * scale;
            drawH = bitmap.Height * scale;
        }
        else
        {
            if (sizeW != "auto") drawW = ResolveBgDimension(sizeW, area.Width);
            if (sizeH != "auto") drawH = ResolveBgDimension(sizeH, area.Height);
        }

        // Resolve position
        float offX = ResolveBgPosition(posX, area.Width, drawW);
        float offY = ResolveBgPosition(posY, area.Height, drawH);

        canvas.Save();
        var (rx, ry) = node.GetBorderRadius(clipArea.Width, clipArea.Height);
        if (rx > 0 || ry > 0)
            canvas.ClipRoundRect(new SKRoundRect(clipArea, rx, ry), antialias: true);
        else
            canvas.ClipRect(clipArea);

        bool repeatX = repeat is "repeat" or "repeat-x";
        bool repeatY = repeat is "repeat" or "repeat-y";

        float startX = repeatX ? area.Left + offX - (float)Math.Ceiling((offX) / drawW) * drawW : area.Left + offX;
        float startY = repeatY ? area.Top + offY - (float)Math.Ceiling((offY) / drawH) * drawH : area.Top + offY;
        float endX = repeatX ? area.Right : startX + drawW;
        float endY = repeatY ? area.Bottom : startY + drawH;

        for (float py = startY; py < endY; py += drawH)
        {
            for (float px = startX; px < endX; px += drawW)
            {
                var dest = new SKRect(px, py, px + drawW, py + drawH);
                canvas.DrawBitmap(bitmap, dest);
                if (!repeatX) break;
            }
            if (!repeatY) break;
        }

        canvas.Restore();
    }

    private static float ResolveBgDimension(string val, float total)
    {
        val = val.Trim();
        if (val.EndsWith("px") && float.TryParse(val[..^2], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
        if (val.EndsWith('%') && float.TryParse(val[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pct)) return pct / 100f * total;
        return total;
    }

    private static float ResolveBgPosition(string val, float areaSize, float imageSize)
    {
        val = val.Trim();
        return val switch
        {
            "left" or "top" => 0f,
            "center" => (areaSize - imageSize) / 2f,
            "right" or "bottom" => areaSize - imageSize,
            _ when val.EndsWith("px") && float.TryParse(val[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px) => px,
            _ when val.EndsWith('%') && float.TryParse(val[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct) => (areaSize - imageSize) * pct / 100f,
            _ => 0f,
        };
    }

    // ---- Outline drawing ----

    private static void DrawOutline(SKCanvas canvas, BoxDimensions box, LayoutNode node)
    {
        var style = node.GetOutlineStyle();
        if (style == BorderStyle.None) return;
        var width = node.GetOutlineWidth();
        if (width <= 0) return;
        var color = node.GetOutlineColor();
        var offset = node.GetOutlineOffset();

        var outlineRect = SKRect.Inflate(box.BorderBox, offset + width / 2f, offset + width / 2f);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true,
        };
        ApplyBorderStyle(paint, style);
        canvas.DrawRect(outlineRect, paint);
    }

    private static void DrawWrappedText(SKCanvas canvas, LayoutNode node, string text,
                                        float x, float y, float maxWidth,
                                        SKFont font, SKPaint paint)
    {
        var whiteSpace = node.GetWhiteSpace();
        var textAlign = node.GetTextAlign();
        var underline = node.IsUnderline();
        var lineThrough = node.IsLineThrough();
        var textTransform = node.GetTextTransform();
        var letterSpacing = node.GetLetterSpacing(font.Size);
        var wordSpacing = node.GetWordSpacing(font.Size);
        var textIndent = node.GetTextIndent(maxWidth, font.Size);

        // Resolve ::first-letter and ::first-line styles from node or parent
        var firstLetterStyles = node.FirstLetterStyles ?? node.Parent?.FirstLetterStyles;
        var firstLineStyles = node.FirstLineStyles ?? node.Parent?.FirstLineStyles;

        // Apply text-transform
        text = StyleExtensions.ApplyTextTransform(text, textTransform);

        var lines = TextMeasure.WrapText(text, maxWidth, font, whiteSpace, node.GetLineHeight(node.GetFontSize()));

        // text-overflow: ellipsis — when overflow is clipped and only one line, truncate with "…"
        // Check node itself OR its parent block (text-overflow is non-inherited; inline children
        // get the visual effect from the nearest clipping ancestor).
        static bool HasEllipsisClip(LayoutNode n) =>
            n.IsTextOverflowEllipsis() && n.GetOverflow() != OverflowType.Visible;
        var useEllipsis = HasEllipsisClip(node)
            || (node.Parent != null && HasEllipsisClip(node.Parent));
        if (useEllipsis && lines.Count == 1 && lines[0].Width > maxWidth)
        {
            var (truncText, truncW) = TextMeasure.TruncateWithEllipsis(lines[0].Text, maxWidth, font);
            lines = [new TextLine(truncText, truncW, lines[0].Height, lines[0].Ascent)];
        }

        var lineY = y;
        var textShadow = node.GetTextShadow();
        var isFirstLine = true;
        var firstLetterDrawn = false;

        var lastLineIndex = lines.Count - 1;
        var lineIndex = -1;
        foreach (var line in lines)
        {
            lineIndex++;
            // Measure actual draw width accounting for letter-spacing
            var lineWidth = line.Width;
            if (letterSpacing != 0f || wordSpacing != 0f)
                lineWidth = MeasureWithSpacing(line.Text, font, letterSpacing, wordSpacing);

            // text-align: justify — widen the inter-word gaps so every line except the last fills
            // the available width (CSS 2.1 §16.2). Last lines and single-word lines stay unmodified.
            var lineWordSpacing = wordSpacing;
            var justified = false;
            if (textAlign == TextAlign.Justify && lineIndex != lastLineIndex && lineWidth < maxWidth)
            {
                var gaps = line.Text.Count(c => c == ' ');
                if (gaps > 0)
                {
                    lineWordSpacing = wordSpacing + (maxWidth - lineWidth) / gaps;
                    justified = true;
                }
            }

            // Compute x offset for text-align
            var drawX = textAlign switch
            {
                TextAlign.Center => x + (maxWidth - lineWidth) / 2f,
                TextAlign.Right => x + maxWidth - lineWidth,
                _ => x,
            };

            // A justified line visually spans the full width (used for underline/strike extents).
            var drawnWidth = justified ? maxWidth : lineWidth;

            // Apply text-indent on first line
            if (isFirstLine && textIndent != 0f)
            {
                drawX += textIndent;
            }

            // SkiaSharp draws at baseline; add ascent to convert top→baseline
            var baseline = lineY + line.Ascent;

            // Determine paint/font for this line (::first-line override)
            var linePaint = paint;
            var lineFont = font;
            SKPaint? tempPaint = null;
            SKFont? tempFont = null;

            if (isFirstLine && firstLineStyles != null)
            {
                tempPaint = new SKPaint { Color = paint.Color, IsAntialias = true };
                if (firstLineStyles.TryGetValue("color", out var flColor))
                    tempPaint.Color = Rendering.SvgRenderer.ParseColor(flColor);
                if (firstLineStyles.TryGetValue("font-weight", out var flWeight) && flWeight == "bold")
                {
                    var tf = font.Typeface;
                    var boldTf = SKTypeface.FromFamilyName(tf.FamilyName, (int)SKFontStyleWeight.Bold,
                        (int)tf.FontStyle.Width, tf.FontStyle.Slant) ?? tf;
                    tempFont = new SKFont(boldTf, font.Size);
                    lineFont = tempFont;
                }
                if (firstLineStyles.TryGetValue("font-size", out var flSize))
                {
                    if (flSize.EndsWith("em") && float.TryParse(flSize[..^2],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var em))
                    {
                        tempFont?.Dispose();
                        tempFont = new SKFont(lineFont.Typeface, font.Size * em);
                        lineFont = tempFont;
                    }
                }
                linePaint = tempPaint;
            }

            // text-shadow drawn before (behind) the main text
            if (textShadow.HasValue)
            {
                var ts = textShadow.Value;
                var sigma = ts.Blur / 2f;
                using var sp = new SKPaint
                {
                    Color = ts.Color,
                    IsAntialias = true,
                    MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
                };
                DrawTextWithSpacing(canvas, line.Text, drawX + ts.OffsetX, baseline + ts.OffsetY, lineFont, sp, letterSpacing, lineWordSpacing);
            }

            // Handle ::first-letter on the first character of the first line
            if (isFirstLine && !firstLetterDrawn && firstLetterStyles != null && line.Text.Length > 0)
            {
                firstLetterDrawn = true;
                var firstChar = line.Text[0..1];
                var restText = line.Text[1..];

                // Create first-letter font/paint
                using var flPaint = new SKPaint { Color = linePaint.Color, IsAntialias = true };
                var flFontSize = lineFont.Size;
                var flTypeface = lineFont.Typeface;

                if (firstLetterStyles.TryGetValue("color", out var flcColor))
                    flPaint.Color = Rendering.SvgRenderer.ParseColor(flcColor);
                if (firstLetterStyles.TryGetValue("font-size", out var flcSize))
                {
                    if (flcSize.EndsWith("em") && float.TryParse(flcSize[..^2],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var em))
                        flFontSize = lineFont.Size * em;
                    else if (flcSize.EndsWith("px") && float.TryParse(flcSize[..^2],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var px))
                        flFontSize = px;
                }
                if (firstLetterStyles.TryGetValue("font-weight", out var flcWeight) && flcWeight == "bold")
                {
                    flTypeface = SKTypeface.FromFamilyName(flTypeface.FamilyName, (int)SKFontStyleWeight.Bold,
                        (int)flTypeface.FontStyle.Width, flTypeface.FontStyle.Slant) ?? flTypeface;
                }

                using var flFont = new SKFont(flTypeface, flFontSize);
                var flWidth = flFont.MeasureText(firstChar, out _);

                DrawTextWithSpacing(canvas, firstChar, drawX, baseline, flFont, flPaint, 0, 0);
                if (restText.Length > 0)
                    DrawTextWithSpacing(canvas, restText, drawX + flWidth, baseline, lineFont, linePaint, letterSpacing, lineWordSpacing);
            }
            else
            {
                DrawTextWithSpacing(canvas, line.Text, drawX, baseline, lineFont, linePaint, letterSpacing, lineWordSpacing);
            }

            if (underline)
            {
                var metrics = lineFont.Metrics;
                var uY = baseline + (metrics.UnderlinePosition ?? lineFont.Size * 0.1f);
                var uThick = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = linePaint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, uY, drawX + drawnWidth, uY, lp);
            }

            if (lineThrough)
            {
                var strikY = baseline - lineFont.Size * 0.3f;
                var uThick = Math.Max(lineFont.Metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = linePaint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, strikY, drawX + drawnWidth, strikY, lp);
            }

            isFirstLine = false;
            tempPaint?.Dispose();
            tempFont?.Dispose();
            lineY += line.Height;
        }
    }

    /// <summary>Draws text with custom letter-spacing and word-spacing.</summary>
    private static void DrawTextWithSpacing(SKCanvas canvas, string text, float x, float y,
                                            SKFont font, SKPaint paint, float letterSpacing, float wordSpacing)
    {
        if (letterSpacing == 0f && wordSpacing == 0f)
        {
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
            return;
        }

        var curX = x;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            canvas.DrawText(s, curX, y, SKTextAlign.Left, font, paint);
            curX += font.MeasureText(s) + letterSpacing;
            if (ch == ' ') curX += wordSpacing;
        }
    }

    /// <summary>Measures text width including letter-spacing and word-spacing.</summary>
    private static float MeasureWithSpacing(string text, SKFont font, float letterSpacing, float wordSpacing)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        foreach (var ch in text)
        {
            w += font.MeasureText(ch.ToString()) + letterSpacing;
            if (ch == ' ') w += wordSpacing;
        }
        // Remove trailing letter-spacing
        w -= letterSpacing;
        return Math.Max(0f, w);
    }
}
