using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Computes BoxDimensions for every LayoutNode in the tree before painting.
/// Block elements stack vertically. Inline/inline-block elements flow horizontally in line boxes.
/// </summary>
internal static class BoxEngine
{
    public static void Layout(LayoutNode root, float viewportWidth, float viewportHeight)
    {
        // Establish the root (html) font-size so `rem` units resolve correctly this pass.
        var html = root.TagName == "HTML" ? root : root.Children.FirstOrDefault(c => c.TagName == "HTML") ?? root;
        CssUnits.RootFontSize = html.GetFontSize();

        NormalizeBlockInInline(root);
        NormalizeInteractive(root);
        NormalizeTableBoxes(root);

        LayoutBlock(root, 0, 0, viewportWidth, viewportWidth, viewportHeight, viewportHeight); // root: margin-box discarded
        // Second pass: lay out all absolute/fixed nodes now that normal-flow boxes are finalised
        LayoutPositioned(root, root.Box, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// Walks the tree and resolves position:absolute and position:fixed boxes.
    /// containingBox is the padding-box of the nearest positioned ancestor.
    /// viewportBox is the full viewport rect (for position:fixed).
    /// </summary>
    private static void LayoutPositioned(LayoutNode node,
        BoxDimensions containingBox,
        float viewportWidth, float viewportHeight)
    {
        var viewportBox = new BoxDimensions
        {
            ContentBox = new SKRect(0, 0, viewportWidth, viewportHeight)
        };

        foreach (var child in node.Children)
        {
            var pos = child.GetPosition();

            if (pos == PositionType.Absolute || pos == PositionType.Fixed)
            {
                var cb = pos == PositionType.Fixed ? viewportBox : containingBox;
                ResolveAbsoluteBox(child, cb, viewportWidth, viewportHeight);
                // Recurse using this child as the new containing block
                LayoutPositioned(child, child.Box, viewportWidth, viewportHeight);
            }
            else
            {
                // Pass down nearest positioned ancestor as containing block
                var nextCb = child.IsPositioned() ? child.Box : containingBox;
                LayoutPositioned(child, nextCb, viewportWidth, viewportHeight);
            }
        }
    }

    /// <summary>
    /// CSS 2.1 §9.2.1.1 (block-in-inline): "When an inline box contains an in-flow block-level
    /// box, the inline box (and its inline ancestors within the same block box) are broken around
    /// the block-level box … splitting the inline box into two boxes (even if either side is
    /// empty), one on each side of the block-level box. The line boxes before the break and after
    /// the break are enclosed in anonymous block boxes, and the block-level box becomes a sibling
    /// of those anonymous boxes."
    /// <para>We implement this by restructuring each <b>block container</b>'s child list: an inline
    /// child that (transitively, through inline boxes) contains an in-flow block is replaced by the
    /// flattened sequence of its pieces — clones of the inline carrying its style around each run of
    /// inline content, with the block-level descendants hoisted out to sibling position. The
    /// existing mixed inline/block flow in <see cref="LayoutChildrenImpl"/> then wraps the inline
    /// pieces in anonymous line boxes and stacks the blocks, exactly as the spec requires. This is
    /// materially more correct than the previous "promote the whole inline to a block" heuristic,
    /// which painted the inline's background/borders behind the nested block (wrong for
    /// backgrounds/empty pieces) and forced a preceding sibling of the inline onto its own line.</para>
    /// <para>Runs before <see cref="NormalizeTableBoxes"/> and is idempotent: the emitted pieces
    /// contain no in-flow block (so they never re-trigger) and the hoisted blocks are block-level
    /// siblings (so they are not inline children to split).</para>
    /// </summary>
    private static void NormalizeBlockInInline(LayoutNode node)
    {
        // Resolve descendants first so a hoisted block's own subtree is already normalized and a
        // deeper block container splits its own inline children before we look at this level.
        foreach (var child in node.Children)
            NormalizeBlockInInline(child);

        if (!IsBlockContainerForSplit(node)) return;
        if (!node.Children.Any(IsBreakableInline)) return;

        var rebuilt = new List<LayoutNode>();
        foreach (var child in node.Children)
        {
            if (IsBreakableInline(child))
                rebuilt.AddRange(SplitInline(child));
            else
                rebuilt.Add(child);
        }
        node.Children.Clear();
        foreach (var c in rebuilt) { c.Parent = node; node.Children.Add(c); }
    }

    /// <summary>True for nodes whose children flow as a block container (mixed block/inline), i.e.
    /// the boxes that can hold the anonymous block boxes §9.2.1.1 generates. Excludes inline boxes
    /// (they bubble the break up to their block-container ancestor), flex containers (children are
    /// blockified flex items) and table boxes (handled by <see cref="NormalizeTableBoxes"/>).</summary>
    private static bool IsBlockContainerForSplit(LayoutNode node)
        => node.GetDisplay() is DisplayType.Block or DisplayType.ListItem
            or DisplayType.TableCell or DisplayType.InlineBlock;

    /// <summary>True when a box participates in its parent's normal flow — i.e. it is neither
    /// floated nor absolutely/fixed-positioned. A #text node never floats even if it inherited a
    /// float from a shared parent style (floats apply to elements only).</summary>
    private static bool IsInFlow(LayoutNode n)
    {
        var pos = n.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return false;
        if (n.TagName != "#text" && n.GetFloat() != FloatType.None) return false;
        return true;
    }

    /// <summary>A non-anonymous element with <c>display:inline</c> — the only box kind broken
    /// around a block (inline-block/-table/-flex are inline-level but are themselves block
    /// containers, so a block inside them stays inside).</summary>
    private static bool IsSplittableInline(LayoutNode n)
        => !n.TagName.StartsWith('#') && n.GetDisplay() == DisplayType.Inline;

    /// <summary>An in-flow inline that contains an in-flow block and so must be broken (the trigger
    /// used at a block container's level and when weaving nested inlines). Excludes floated/abs
    /// inlines: those are blockified/out of flow, and a block they contain stays inside them rather
    /// than breaking any ancestor inline (this is what keeps Acid2's floated
    /// <c>span&gt;em&gt;strong</c> smile intact).</summary>
    private static bool IsBreakableInline(LayoutNode n)
        => IsSplittableInline(n) && IsInFlow(n) && InlineContainsInFlowBlock(n);

    /// <summary>True when <paramref name="n"/> is an in-flow block-level box (the trigger for a
    /// block-in-inline break). Floats and absolutely/fixed-positioned boxes are out of flow and do
    /// not break the inline. Character-data nodes (<c>#text</c>/<c>#comment</c>/<c>#pi</c>) are
    /// always inline-level content — never a block — even if one inherited a non-inherited
    /// <c>display:block</c> from a shared/JS-created style (the same shared-#text-style hazard the
    /// flow already guards against for float/clear). Generated content (<c>#pseudo-*</c>) and
    /// anonymous boxes are NOT excluded — a <c>display:block</c> <c>::after</c> is a real block.</summary>
    private static bool IsInFlowBlockLevel(LayoutNode n)
        => n.TagName is not ("#text" or "#comment" or "#pi") && IsInFlow(n)
            && n.GetDisplay() is DisplayType.Block or DisplayType.ListItem
                or DisplayType.Flex or DisplayType.Table;

    /// <summary>Whether an inline box (transitively, through <b>in-flow</b> nested inline boxes)
    /// contains an in-flow block-level box, so it must be broken. Recursion stops at out-of-flow
    /// nested inlines — a float/abs inline is its own formatting root and any block within it does
    /// not break this inline.</summary>
    private static bool InlineContainsInFlowBlock(LayoutNode inline)
    {
        foreach (var c in inline.Children)
        {
            if (IsInFlowBlockLevel(c)) return true;
            if (IsSplittableInline(c) && IsInFlow(c) && InlineContainsInFlowBlock(c)) return true;
        }
        return false;
    }

    /// <summary>Breaks a single inline box around the in-flow block(s) it contains, returning the
    /// flattened parent-level sequence: clones of the inline wrapping each run of inline content
    /// (empty/whitespace-only runs dropped — §9.2.1.1 "if empty, will not show any background"),
    /// interleaved with the hoisted block-level descendants in document order. Nested inline
    /// ancestors that also contain the block are broken too (their inline pieces nest inside this
    /// inline's piece; their blocks bubble to this level).</summary>
    private static List<LayoutNode> SplitInline(LayoutNode inline)
    {
        var result = new List<LayoutNode>();
        var piece = CloneInlineShell(inline);

        void FlushPiece()
        {
            if (PieceHasVisibleContent(piece)) result.Add(piece);
            piece = CloneInlineShell(inline);
        }

        void EmitBlock(LayoutNode block)
        {
            FlushPiece();
            // §9.2.1.1 last sentence: relative positioning of the inline (and its inline ancestors)
            // also translates the block-level box it was broken around.
            PropagateRelativePosition(inline, block);
            SuppressSharedStyleBorder(inline, block);
            result.Add(block);
        }

        void HoistFloat(LayoutNode f)
        {
            // A float inside a broken inline is laid out in the enclosing block container's
            // formatting context, not inside an inline piece — the inline-run collector has no way
            // to place a float, so one left inside a piece would simply be dropped. Hoist it to
            // sibling position (it does not break the surrounding inline content, so the piece is
            // left open to keep leading/trailing text on one line).
            PropagateRelativePosition(inline, f);
            result.Add(f);
        }

        void AddToPiece(LayoutNode n) { n.Parent = piece; piece.Children.Add(n); }

        foreach (var child in inline.Children)
        {
            if (IsInFlowBlockLevel(child)) EmitBlock(child);
            else if (IsFloated(child)) HoistFloat(child);
            else if (IsBreakableInline(child))
            {
                // A nested in-flow inline that itself contains a block: split it, weaving its inline
                // pieces into the current piece and hoisting its blocks/floats (already
                // relpos-propagated for the nested inline) up to this level, where they also take
                // this inline's relpos.
                foreach (var part in SplitInline(child))
                {
                    if (IsInFlowBlockLevel(part)) EmitBlock(part);
                    else if (IsFloated(part)) HoistFloat(part);
                    else AddToPiece(part);
                }
            }
            else AddToPiece(child);
        }
        FlushPiece();
        return result;
    }

    /// <summary>True for a floated element (never a #text node, which cannot float even if it shares
    /// a floated parent's computed style).</summary>
    private static bool IsFloated(LayoutNode n)
        => n.TagName != "#text" && n.GetFloat() != FloatType.None;

    /// <summary>Generated content (a <c>::before</c>/<c>::after</c> node) shares its originating
    /// element's <see cref="LayoutNode.Style"/> object, which carries that element's non-inherited
    /// border. Once such a pseudo is <c>display:block</c> and hoisted to sibling position by the
    /// block-in-inline break, that inherited border would wrongly paint as a full box. When the
    /// hoisted block literally shares the broken inline's Style (so the border is the inline's, not
    /// the block's own) and declares no border of its own, suppress the border paint. Scoped by
    /// reference-equality so real block children (which have their own Style) are never touched.</summary>
    private static void SuppressSharedStyleBorder(LayoutNode inline, LayoutNode block)
    {
        if (!ReferenceEquals(block.Style, inline.Style)) return;
        if (block.StyleOverrides.Keys.Any(k => k.StartsWith("border", StringComparison.OrdinalIgnoreCase))) return;
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            block.StyleOverrides[$"border-{side}-style"] = "none";
            block.StyleOverrides[$"border-{side}-width"] = "0";
        }
    }

    /// <summary>An empty inline piece (or one holding only collapsible whitespace) generates no
    /// line box and must paint no background, so it is dropped rather than emitted.</summary>
    private static bool PieceHasVisibleContent(LayoutNode piece)
    {
        foreach (var c in piece.Children)
        {
            if (c.GetDisplay() == DisplayType.None) continue;
            if (c.TagName == "#text")
            {
                if (!string.IsNullOrWhiteSpace(c.DisplayText)) return true;
            }
            else return true;
        }
        return false;
    }

    /// <summary>Creates an empty inline box carrying <paramref name="inline"/>'s identity and style
    /// (shared <see cref="LayoutNode.Style"/> plus copies of every override map), so each broken
    /// piece paints the inline's background/color/relpos behind its own content. Children are added
    /// by the caller; attributes (incl. id) are intentionally not copied — the pieces are anonymous.</summary>
    private static LayoutNode CloneInlineShell(LayoutNode inline)
    {
        var clone = new LayoutNode(null, inline.TagName, "", inline.Style);
        CopyDict(inline.StyleOverrides, clone.StyleOverrides);
        CopyDict(inline.HoverStyles, clone.HoverStyles);
        CopyDict(inline.FocusStyles, clone.FocusStyles);
        CopyDict(inline.ActiveStyles, clone.ActiveStyles);
        CopyDict(inline.MediaOverrides, clone.MediaOverrides);
        CopyDict(inline.AnimationOverrides, clone.AnimationOverrides);
        CopyDict(inline.CustomProperties, clone.CustomProperties);
        // display:inline is the whole point of a piece; guarantee it even if the source read its
        // inline display from the shared Style rather than an override.
        clone.StyleOverrides["display"] = "inline";
        return clone;
    }

    private static void CopyDict(Dictionary<string, string> from, Dictionary<string, string> to)
    {
        foreach (var (k, v) in from) to[k] = v;
    }

    /// <summary>Applies a relatively-positioned inline's translation to a block hoisted out of it
    /// (§9.2.1.1). Only length/percentage offsets on an otherwise-static block are handled (the
    /// common corpus case, e.g. an inline <c>left:2em</c> shifting its block child); a block that is
    /// already positioned keeps its own offsets.</summary>
    private static void PropagateRelativePosition(LayoutNode inline, LayoutNode block)
    {
        if (inline.GetPosition() != PositionType.Relative) return;
        if (block.GetPosition() != PositionType.Static) return;

        var copied = false;
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            var v = RawStyle(inline, side);
            if (string.IsNullOrEmpty(v) || v == "auto") continue;
            block.StyleOverrides[side] = v;
            copied = true;
        }
        if (copied) block.StyleOverrides["position"] = "relative";
    }

    /// <summary>
    /// Interactive-element layout collapse, re-run each layout so toggling <c>open</c> (via click or
    /// JS) re-flows:
    ///   • &lt;details&gt; — when not <c>open</c>, only the first &lt;summary&gt; is shown; the rest collapse.
    ///   • &lt;dialog&gt; — collapsed entirely unless <c>open</c> (the UA <c>dialog:not([open])</c> rule).
    /// The pre-hide display is saved (<see cref="LayoutNode.DetailsSavedDisplay"/>) so it can be
    /// restored on re-open without clobbering an author-specified display.
    /// </summary>
    private static void NormalizeInteractive(LayoutNode node)
    {
        foreach (var child in node.Children)
            NormalizeInteractive(child);

        if (node.TagName == "DIALOG")
            SetHiddenByUa(node, hide: !node.Attributes.ContainsKey("open"));

        if (node.TagName == "DETAILS")
        {
            var open = node.Attributes.ContainsKey("open");
            var summarySeen = false;
            foreach (var child in node.Children)
            {
                if (child.TagName is "#text") continue;
                if (!summarySeen && child.TagName == "SUMMARY") { summarySeen = true; continue; }
                SetHiddenByUa(child, hide: !open);
            }
        }
    }

    /// <summary>Hides/unhides a node via display:none, saving and restoring its prior display value.</summary>
    private static void SetHiddenByUa(LayoutNode n, bool hide)
    {
        if (hide)
        {
            if (n.DetailsSavedDisplay is null)
            {
                n.DetailsSavedDisplay = n.StyleOverrides.GetValueOrDefault("display") ?? "";
                n.StyleOverrides["display"] = "none";
            }
        }
        else if (n.DetailsSavedDisplay is not null)
        {
            if (n.DetailsSavedDisplay.Length == 0) n.StyleOverrides.Remove("display");
            else n.StyleOverrides["display"] = n.DetailsSavedDisplay;
            n.DetailsSavedDisplay = null;
        }
    }

    /// <summary>
    /// CSS 2.1 §17.2.1 (anonymous table objects): a table / inline-table (or a row group) whose
    /// children include non-row content wraps consecutive misparented children in an anonymous
    /// table-row; a table-row whose children include non-cell content wraps them in an anonymous
    /// table-cell. This lets bare text/blocks inside a table lay out (and paint) as real cells —
    /// the common shape of the inline-table tests. Idempotent: generated boxes carry
    /// table-row/table-cell display, so a re-run treats them as proper children and does not re-wrap.
    /// A well-formed TABLE&gt;TR&gt;TD is left byte-for-byte untouched (no anonymous box is created).
    /// </summary>
    private static void NormalizeTableBoxes(LayoutNode node)
    {
        foreach (var child in node.Children)
            NormalizeTableBoxes(child);

        var display = node.GetDisplay();
        var isRowGroup = display == DisplayType.TableRowGroup || node.TagName is "TBODY" or "THEAD" or "TFOOT";
        if (display is DisplayType.Table or DisplayType.InlineTable || isRowGroup)
        {
            MigrateOwnTextToChild(node);
            WrapAnonymousTableBoxes(node, wrapAsRow: true);
        }
        else if (display == DisplayType.TableRow)
        {
            MigrateOwnTextToChild(node);
            WrapAnonymousTableBoxes(node, wrapAsRow: false);
        }
        else
        {
            // Everything else is not a table container, so any internal table box among its
            // children is missing its ancestors (§17.2.1 "generate missing parents").
            WrapMisparentedTableBoxes(node);
        }
    }

    /// <summary>
    /// CSS 2.1 §17.2.1: an internal table box (row-group / row / cell) whose parent is not a table
    /// container must have the missing ancestor boxes generated AROUND it — the "upward" half of
    /// anonymous table generation, as opposed to <see cref="WrapAnonymousTableBoxes"/>, which only
    /// fills in missing children. Consecutive misparented siblings share one generated table, so
    /// e.g. two adjacent <c>display:table-row</c> boxes become two rows of the SAME table.
    /// Once the anonymous table exists, the normal downward pass fills in any rows/cells it needs.
    /// Idempotent: the generated box has display:table, so on a re-run its children are no longer
    /// misparented and nothing new is created.
    /// </summary>
    private static void WrapMisparentedTableBoxes(LayoutNode node)
    {
        // Cheap pre-check: the overwhelming majority of boxes have no table children at all.
        var anyMisparented = false;
        foreach (var c in node.Children)
            if (IsMisparentedTableBox(c)) { anyMisparented = true; break; }
        if (!anyMisparented) return;

        var newChildren = new List<LayoutNode>();
        var run = new List<LayoutNode>();

        void FlushRun()
        {
            if (run.Count == 0) return;
            var anon = new LayoutNode(null, "#anon-table", "", node.Style);
            anon.StyleOverrides["display"] = "table";
            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                anon.StyleOverrides[$"margin-{side}"] = "0";
                anon.StyleOverrides[$"padding-{side}"] = "0";
                anon.StyleOverrides[$"border-{side}-width"] = "0";
            }
            anon.Parent = node;
            foreach (var c in run) { c.Parent = anon; anon.Children.Add(c); }
            run.Clear();
            // A bare cell still needs a row between it and the table.
            WrapAnonymousTableBoxes(anon, wrapAsRow: true);
            newChildren.Add(anon);
        }

        foreach (var child in node.Children)
        {
            if (IsMisparentedTableBox(child)) { run.Add(child); continue; }
            // Whitespace between misparented table boxes is not content and must not break the run
            // (or it would split one table into several).
            if (run.Count > 0 && child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText))
                continue;
            FlushRun();
            newChildren.Add(child);
        }
        FlushRun();

        node.Children.Clear();
        node.Children.AddRange(newChildren);
    }

    /// <summary>True for an in-flow internal table box sitting outside any table container. Floated
    /// and absolutely-positioned boxes are blockified by CSS and never participate in table
    /// structure, so they are left alone.</summary>
    private static bool IsMisparentedTableBox(LayoutNode child)
    {
        var d = child.GetDisplay();
        if (d is not (DisplayType.TableRowGroup or DisplayType.TableRow or DisplayType.TableCell))
            return false;
        if (child.GetFloat() != FloatType.None) return false;
        var pos = child.GetPosition();
        return pos != PositionType.Absolute && pos != PositionType.Fixed;
    }

    /// <summary>
    /// A table/row-group/row whose only content is bare text (e.g. a leaf <c>&lt;div
    /// display:inline-table&gt;some text&lt;/div&gt;</c>) holds that text as its OWN
    /// <see cref="LayoutNode.Text"/> rather than a <c>#text</c> child — the parser only splits
    /// text into an ordered child when the element also has element children (see
    /// <c>Parser.Traverse</c>'s <c>hasMixedChildren</c> check). <see cref="WrapAnonymousTableBoxes"/>
    /// only wraps <c>Children</c>, so that text would otherwise be invisible to anonymous-box
    /// generation (§17.2.1) and the table would end up with zero rows. Splitting it into a real
    /// <c>#text</c> child first makes it "misparented content" like any other, so the normal
    /// wrapping path picks it up. Scoped to nodes with NO existing children, so a node that
    /// already has its text ordered among element children (the common, already-correct case) is
    /// never touched — avoids duplicating content.
    /// </summary>
    private static void MigrateOwnTextToChild(LayoutNode node)
    {
        if (node.Children.Count > 0 || string.IsNullOrEmpty(node.Text)) return;
        var textChild = new LayoutNode(null, "#text", node.Text, node.Style) { Parent = node };
        // The child's Style object is shared with `node` (the usual #text convention), which
        // leaks node's own NON-inherited "display" (here table/inline-table) onto it — without
        // this override the text would be mistaken for another nested table (CollectInlineItems
        // dispatches purely on GetDisplay()), which is both wrong and pathologically slow.
        textChild.StyleOverrides["display"] = "inline";
        node.Children.Add(textChild);
    }

    /// <summary>Wraps runs of misparented children into anonymous table-rows (<paramref name="wrapAsRow"/>)
    /// or table-cells. Only rebuilds the child list if at least one anonymous box is created, so
    /// proper tables are untouched. Whitespace-only runs between real boxes are dropped, not wrapped.</summary>
    private static void WrapAnonymousTableBoxes(LayoutNode parent, bool wrapAsRow)
    {
        var newChildren = new List<LayoutNode>();
        var run = new List<LayoutNode>();
        var created = false;

        void FlushRun()
        {
            if (run.Count == 0) return;
            var anon = new LayoutNode(null, wrapAsRow ? "#anon-row" : "#anon-cell", "", parent.Style);
            anon.StyleOverrides["display"] = wrapAsRow ? "table-row" : "table-cell";
            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                anon.StyleOverrides[$"margin-{side}"] = "0";
                anon.StyleOverrides[$"padding-{side}"] = "0";
                anon.StyleOverrides[$"border-{side}-width"] = "0";
            }
            anon.Parent = parent;
            foreach (var c in run) { c.Parent = anon; anon.Children.Add(c); }
            run.Clear();
            // Content inside a freshly-made anonymous row still needs an anonymous cell.
            if (wrapAsRow) WrapAnonymousTableBoxes(anon, wrapAsRow: false);
            newChildren.Add(anon);
            created = true;
        }

        foreach (var child in parent.Children)
        {
            // Whitespace-only text between table boxes is not content — discard it (CSS 2.1
            // white-space handling) so it never becomes (or pads) an anonymous cell.
            if (child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText))
                continue;

            if (IsProperTableChild(child, atTableLevel: wrapAsRow))
            {
                FlushRun();
                newChildren.Add(child);
            }
            else
            {
                run.Add(child);
            }
        }
        FlushRun();

        if (!created) return; // nothing wrapped — leave the original list (and its whitespace) intact
        parent.Children.Clear();
        parent.Children.AddRange(newChildren);
    }

    /// <summary>True when a child already occupies a proper slot in its table parent: a row / row
    /// group / caption / column(-group) at the table level, or a cell at the row level.</summary>
    private static bool IsProperTableChild(LayoutNode child, bool atTableLevel)
    {
        if (atTableLevel)
        {
            if (child.GetDisplay() is DisplayType.TableRow or DisplayType.TableRowGroup) return true;
            return child.TagName is "TBODY" or "THEAD" or "TFOOT" or "CAPTION" or "COL" or "COLGROUP";
        }
        return child.GetDisplay() == DisplayType.TableCell;
    }

    private static void ResolveAbsoluteBox(LayoutNode node, BoxDimensions cb,
        float viewportWidth, float viewportHeight)
    {
        var cbRect = cb.PaddingBox;
        var fontSize = node.GetFontSize();
        var padding = node.GetPadding(cbRect.Width, cbRect.Height, fontSize);
        var border = node.GetBorderWidth();
        var margin = node.GetMargin(cbRect.Width, cbRect.Height, fontSize);

        var top = node.GetOffsetTop(cbRect.Height, fontSize);
        var right = node.GetOffsetRight(cbRect.Width, fontSize);
        var bottom = node.GetOffsetBottom(cbRect.Height, fontSize);
        var left = node.GetOffsetLeft(cbRect.Width, fontSize);

        // Resolve width. An absolutely positioned replaced box takes its size from the image
        // (§10.3.7's shrink-to-fit and the children-derived height below never see it).
        var replaced = TryResolveReplacedSize(node, cbRect.Width, cbRect.Height, viewportHeight);
        var explicitW = replaced?.Width ?? node.GetWidth(cbRect.Width);
        float contentW;
        if (explicitW > 0)
            contentW = explicitW;
        else if (!float.IsNaN(left) && !float.IsNaN(right))
            contentW = Math.Max(0, cbRect.Width - left - right - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right);
        else
        {
            // Shrink-to-fit (§10.3.7): min(max(min-content, available), max-content), where the
            // available content width is what remains of the containing block after this box's own
            // margins, border, padding and any set left/right offset are removed.
            var availW = cbRect.Width - margin.Left - margin.Right
                       - border.Left - border.Right - padding.Left - padding.Right;
            if (!float.IsNaN(left)) availW -= left;
            else if (!float.IsNaN(right)) availW -= right;
            // Both offsets auto: the box starts at its static position, so only what is left of
            // the containing block from there is available to it (§10.3.7 rule 3).
            else if (node.StaticX is { } sx) availW -= Math.Max(0f, sx - cbRect.Left);
            contentW = IntrinsicSizer.ShrinkToFit(node, Math.Max(0f, availW), viewportHeight);
        }

        // Clamp to min/max-width (§10.4) — min wins over max. (Acid2's fixed scalp uses
        // width:140%; max-width:4em to pin the head's top line to a fixed size.)
        contentW = Math.Min(contentW, node.GetMaxWidth(cbRect.Width, fontSize));
        contentW = Math.Max(contentW, node.GetMinWidth(cbRect.Width, fontSize));

        // Resolve X — §4.1: use StaticX as static position when left/right are both auto
        float contentX;
        if (!float.IsNaN(left))
            contentX = cbRect.Left + left + margin.Left + border.Left + padding.Left;
        else if (!float.IsNaN(right))
            contentX = cbRect.Right - right - margin.Right - border.Right - padding.Right - contentW;
        else if (node.StaticX.HasValue)
            contentX = node.StaticX.Value + margin.Left + border.Left + padding.Left;
        else
            contentX = cbRect.Left + margin.Left + border.Left + padding.Left;

        // Determine this node's explicit content height so children can resolve % heights.
        // height:auto is content-based — GetHeight returns the CB height for auto, so ignore it.
        var autoHeight = node.IsAutoHeight();
        var explicitHEarly = autoHeight ? 0f : node.GetHeight(cbRect.Height, 0, viewportHeight);
        float selfContentH;
        if (explicitHEarly > 0)
        {
            var isBBEarly = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            selfContentH = isBBEarly
                ? Math.Max(0f, explicitHEarly - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitHEarly;
        }
        else if (!float.IsNaN(top) && !float.IsNaN(bottom))
            selfContentH = Math.Max(0, cbRect.Height - top - bottom - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom);
        else
            selfContentH = 0f;

        // Lay out children to get content height
        var contentY0 = cbRect.Top; // temp origin for children layout
        var contentH = LayoutChildren(node.Children,
            contentX, contentY0,
            contentW, viewportWidth, viewportHeight, selfContentH);

        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var lh = node.GetLineHeight(node.GetFontSize());
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, node.GetWhiteSpace(), lh);
            contentH = lines.Sum(l => l.Height);
        }

        var explicitH = autoHeight ? 0f : node.GetHeight(cbRect.Height, 0, viewportHeight);
        if (explicitH > 0)
        {
            var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            contentH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        }
        else if (autoHeight && !float.IsNaN(top) && !float.IsNaN(bottom))
            contentH = Math.Max(0, cbRect.Height - top - bottom - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom);
        if (replaced.HasValue) contentH = replaced.Value.Height;

        // Clamp to min/max-height (§10.7) — min wins over max (Acid2's scalp: min-height:1em
        // overrides max-height:2mm).
        contentH = Math.Min(contentH, node.GetMaxHeight(cbRect.Height, fontSize));
        contentH = Math.Max(contentH, node.GetMinHeight(cbRect.Height, fontSize));

        // Resolve Y — §4.1: use StaticY as static position when top/bottom are both auto
        float contentY;
        if (!float.IsNaN(top))
            contentY = cbRect.Top + top + margin.Top + border.Top + padding.Top;
        else if (!float.IsNaN(bottom))
            contentY = cbRect.Bottom - bottom - margin.Bottom - border.Bottom - padding.Bottom - contentH;
        else if (node.StaticY.HasValue)
            contentY = node.StaticY.Value + margin.Top + border.Top + padding.Top;
        else
            contentY = cbRect.Top + margin.Top + border.Top + padding.Top;

        // Re-layout children at the correct absolute Y
        if (contentY != contentY0)
            LayoutChildren(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight);

        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(contentX, contentY, contentX + contentW, contentY + contentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };
    }

    // -------------------------------------------------------------------------
    // Block layout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a block-level node at the given position.
    /// Returns the total margin-box height consumed (for the parent's y-cursor).
    /// </summary>
    /// <summary>Lays out a block-level node. Returns its total margin-box height and its
    /// effective bottom margin (after any last-child collapse-through).</summary>
    private static (float Height, float BottomMargin) LayoutBlock(
        LayoutNode node,
        float x, float y,
        float availableWidth,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0,
        List<ActiveFloat>? bfcFloats = null)
    {
        if (node.GetDisplay() == DisplayType.None)
        {
            node.Box = default;
            return (0f, 0f);
        }

        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(availableWidth, viewportHeight, fontSize);
        var padding = node.GetPadding(availableWidth, viewportHeight, fontSize);
        var border = node.GetBorderWidth();

        // Explicit width or fill available (pass size=0 so unset width returns 0, not fontSize).
        // "Specified" is IsAutoWidth, not "> 0": 'width: 0' is a used width of zero, and reading it
        // as auto made a zero-width box fill its container instead.
        var hasExplicitW = !node.IsAutoWidth();
        var explicitW = hasExplicitW ? node.GetWidth(availableWidth) : 0f;
        // A block-level replaced box is sized from the image (§10.3.4 defers to §10.3.2's rules),
        // so its used width behaves like an explicit one for centering and box-sizing purposes.
        var replaced = TryResolveReplacedSize(node, availableWidth, parentContentHeight, viewportHeight);
        var hasUsedW = replaced.HasValue || hasExplicitW;
        var usedW = replaced?.Width ?? explicitW;
        var boxWidth = hasUsedW ? usedW : availableWidth - margin.Left - margin.Right;

        // box-sizing: with content-box (the default), an explicit `width` IS the content width —
        // padding/border are added outside it. Only border-box (and the auto/fill case) subtracts
        // padding+border from the box width. (Height already honors this below.)
        var isBorderBoxW = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var contentW = (hasUsedW && !isBorderBoxW)
            ? Math.Max(0f, usedW)
            : Math.Max(0f, boxWidth - border.Left - border.Right - padding.Left - padding.Right);

        // CSS 2.1 §10.4: clamp the tentative used width to max-width, then min-width (min wins),
        // and re-run the width rules with the clamped value. Only the absolutely-positioned path
        // did this, so 'min-width' and 'max-width' had no effect at all on an in-flow block.
        // A percentage that cannot be resolved (no containing-block width) computes to 'none'/0.
        var clampedW = contentW;
        var maxW = node.GetMaxWidth(availableWidth, fontSize);
        if (maxW < float.PositiveInfinity && !(IsPercentValue(node, "max-width") && availableWidth <= 0f))
        {
            var maxContent = isBorderBoxW
                ? Math.Max(0f, maxW - border.Left - border.Right - padding.Left - padding.Right) : maxW;
            if (clampedW > maxContent) clampedW = maxContent;
        }
        var minW = node.GetMinWidth(availableWidth, fontSize);
        if (minW > 0f && !(IsPercentValue(node, "min-width") && availableWidth <= 0f))
        {
            var minContent = isBorderBoxW
                ? Math.Max(0f, minW - border.Left - border.Right - padding.Left - padding.Right) : minW;
            if (clampedW < minContent) clampedW = minContent;
        }
        // A clamped box has a known width, so auto margins centre it like an explicit one does.
        var widthIsKnown = hasUsedW || clampedW != contentW;
        contentW = clampedW;

        // margin: auto centering — when the used width is known and one or both horizontal margins are auto
        if (widthIsKnown)
        {
            var leftAuto = node.IsAutoMarginLeft();
            var rightAuto = node.IsAutoMarginRight();
            if (leftAuto || rightAuto)
            {
                var remaining = availableWidth - contentW - border.Left - border.Right - padding.Left - padding.Right;
                if (leftAuto && rightAuto) { margin.Left = margin.Right = MathF.Max(0, remaining / 2f); }
                else if (leftAuto) { margin.Left = MathF.Max(0, remaining); }
                else { margin.Right = MathF.Max(0, remaining); }
            }
        }

        var contentX = x + margin.Left + border.Left + padding.Left;
        var contentY = y + margin.Top + border.Top + padding.Top;

        // A block-level table with auto width shrink-wraps to its content (CSS 2.1 §17.5.2), unlike
        // a normal block which fills its container. An explicit width already flowed into contentW.
        if (!hasExplicitW && node.GetDisplay() == DisplayType.Table)
            contentW = TableEngine.MeasureTableWidth(node, contentW, viewportWidth, viewportHeight);

        // Resolve this node's explicit height using parentContentHeight for % and viewportHeight for vh/vw.
        // height:auto is content-based — GetHeight returns the containing-block height for auto (the
        // width-style "fill" behaviour of GetSize), which must NOT be treated as an explicit height.
        var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var hasExplicitH = !node.IsAutoHeight();
        var explicitH = hasExplicitH ? node.GetHeight(parentContentHeight, 0, viewportHeight) : 0f;
        var knownContentH = hasExplicitH
            ? (isBorderBox ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom) : explicitH)
            : 0f;

        var nodeDisplay = node.GetDisplay();
        var establishesBfc = EstablishesBlockFormattingContext(node);
        float contentH;
        float trailingMargin = 0f; // last in-flow block child's bottom margin (collapse-through candidate)
        if (nodeDisplay is DisplayType.Flex or DisplayType.InlineFlex)
            contentH = FlexEngine.LayoutFlex(node, contentX, contentY, contentW, knownContentH, viewportWidth, viewportHeight);
        else if (nodeDisplay == DisplayType.Table)
            contentH = TableEngine.LayoutTable(node, contentX, contentY, contentW, viewportWidth, viewportHeight);
        else
        {
            // Float context: a BFC-establishing block (or the root, when no ambient context was
            // passed) owns a fresh list; a normal block threads its ancestor BFC's list so floats
            // declared inside it escape into that context rather than being contained here.
            var ownsFloatContext = establishesBfc || bfcFloats is null;
            var floatCtx = ownsFloatContext ? new List<ActiveFloat>() : bfcFloats!;
            contentH = LayoutChildrenImpl(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight,
                knownContentH, border.Top + padding.Top, establishesBfc, floatCtx, ownsFloatContext, out trailingMargin);
        }

        // Block elements with no children but own text (e.g. <label>, <p>, <h1>):
        if (contentH == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var ws = node.GetWhiteSpace();
            var lh = node.GetLineHeight(node.GetFontSize());
            var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentW, 1f), font, ws, lh);
            contentH = lines.Sum(l => l.Height);
        }

        // Parent–last-child margin collapse-through (CSS 2.1 §8.3.1): when this block has no
        // bottom border/padding, an auto height, and is not a block formatting context, the last
        // in-flow child's bottom margin collapses with this block's own bottom margin (propagating
        // out) rather than adding to the content height. Otherwise it stays inside the content.
        var effectiveBottomMargin = margin.Bottom;
        if (trailingMargin != 0f && padding.Bottom == 0f && border.Bottom == 0f
            && node.IsAutoHeight() && !establishesBfc)
            effectiveBottomMargin = CollapseMargins(margin.Bottom, trailingMargin);
        else
            contentH += trailingMargin;

        // Explicit height overrides — respect box-sizing: border-box
        if (hasExplicitH)
        {
            var clampedH = isBorderBox
                ? Math.Max(0f, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;

            // Per-element overflow scroll: track natural content vs. constrained height
            var overflow = node.GetOverflow();
            if ((overflow == OverflowType.Scroll || overflow == OverflowType.Auto) && contentH > clampedH)
            {
                var ss = node.ScrollState ?? new ElementScrollState();
                ss.ContentHeight = contentH;
                ss.ContainerHeight = clampedH;
                node.ScrollState = ss;
            }
            else
            {
                node.ScrollState = null;
            }

            contentH = clampedH;
        }
        else
        {
            // aspect-ratio: derive height from width when no explicit height
            var ar = node.GetAspectRatio();
            if (ar > 0)
                contentH = contentW / ar;

            node.ScrollState = null;
        }

        // A replaced box's height is the image's (or the ratio-derived) one — it has no in-flow
        // content to measure, so the child/text-derived height above does not apply to it.
        if (replaced.HasValue) contentH = replaced.Value.Height;

        // CSS 2.1 §10.7: clamp the resolved height to min-height/max-height. Percentages resolve
        // against the containing block height; when that is auto (parentContentHeight == 0) an
        // unresolvable percentage max-height computes to 'none' and min-height to 0.
        var maxH = node.GetMaxHeight(parentContentHeight, fontSize);
        if (maxH < float.PositiveInfinity && !(IsPercentValue(node, "max-height") && parentContentHeight <= 0f))
        {
            var maxContent = isBorderBox
                ? Math.Max(0f, maxH - border.Top - border.Bottom - padding.Top - padding.Bottom) : maxH;
            if (contentH > maxContent) contentH = maxContent;
        }
        var minH = node.GetMinHeight(parentContentHeight, fontSize);
        if (minH > 0f && !(IsPercentValue(node, "min-height") && parentContentHeight <= 0f))
        {
            var minContent = isBorderBox
                ? Math.Max(0f, minH - border.Top - border.Bottom - padding.Top - padding.Bottom) : minH;
            if (contentH < minContent) contentH = minContent;
        }

        margin.Bottom = effectiveBottomMargin;
        node.Box = new BoxDimensions
        {
            ContentBox = new SKRect(contentX, contentY, contentX + contentW, contentY + contentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        var totalH = margin.Top + border.Top + padding.Top
                   + contentH
                   + padding.Bottom + border.Bottom + effectiveBottomMargin;
        return (totalH, effectiveBottomMargin);
    }

    /// <summary>
    /// Lays out children of a block container.
    /// Consecutive inline/inline-block children are grouped into inline formatting contexts.
    /// Vertical margins between adjacent block children are collapsed (CSS 2.1 §8.3.1).
    /// Returns total content height consumed.
    /// </summary>
    /// <summary>
    /// CSS 2.1 §10.3.2 / §10.6.2 — the used content size of a REPLACED box (an <c>&lt;img&gt;</c>,
    /// or an <c>&lt;object&gt;</c> that decoded an image). An 'auto' dimension takes the intrinsic
    /// one, and when only one of width/height is specified the other is derived from the intrinsic
    /// ratio. Returns null for a non-replaced box, whose size comes from its content instead.
    /// <para>Only the inline path used to consult the intrinsic size, so a replaced box that was
    /// block-level, floated or absolutely positioned filled its container horizontally (or
    /// shrink-to-fit) and collapsed to zero height — it has no children to measure. §9.7 makes a
    /// floated or absolutely positioned image block-level, so all three paths need this.</para>
    /// </summary>
    internal static (float Width, float Height)? TryResolveReplacedSize(
        LayoutNode node, float cbWidth, float cbHeight, float viewportHeight)
    {
        if (node.TagName != "IMG" && !(node.TagName == "OBJECT" && node.Image != null)) return null;

        // The width/height content attributes are captured as the intrinsic size (Parser), so this
        // covers both a real bitmap and an attribute-declared size; CSS width/height still wins.
        float iw = node.IntrinsicWidth > 0 ? node.IntrinsicWidth : node.Image?.Width ?? 0f;
        float ih = node.IntrinsicHeight > 0 ? node.IntrinsicHeight : node.Image?.Height ?? 0f;

        var specW = node.GetWidth(cbWidth);
        var specH = node.IsAutoHeight() ? 0f : node.GetHeight(cbHeight, 0, viewportHeight);

        if (specW > 0 && specH > 0) return (specW, specH);
        if (specW > 0) return (specW, iw > 0 && ih > 0 ? specW * ih / iw : ih);
        if (specH > 0) return (iw > 0 && ih > 0 ? specH * iw / ih : iw, specH);
        return (iw, ih);
    }

    /// <summary>Exposed for FlexEngine to lay out flex item children.</summary>
    internal static float LayoutChildrenPublic(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0)
        => LayoutChildren(children, contentX, contentY, contentW, viewportWidth, viewportHeight, parentContentHeight);

    // -------------------------------------------------------------------------
    // Float tracking
    // -------------------------------------------------------------------------

    /// <summary>Represents an active float whose occupied area affects subsequent layout.</summary>
    private record struct ActiveFloat(float Left, float Top, float Right, float Bottom, FloatType Side);

    /// <summary>
    /// True when the node establishes a new block formatting context (CSS 2.1 §9.4.1): floats,
    /// absolutely-positioned boxes, non-block block containers (inline-block, table-cell, flex),
    /// and elements with overflow other than visible. A BFC's contents (including child margins)
    /// do not collapse with margins outside it.
    /// </summary>
    private static bool EstablishesBlockFormattingContext(LayoutNode node)
    {
        if (node.GetFloat() != FloatType.None) return true;
        var pos = node.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return true;
        if (node.GetOverflow() != OverflowType.Visible) return true;
        var display = node.GetDisplay();
        if (display is DisplayType.InlineBlock or DisplayType.Flex or DisplayType.InlineFlex
            or DisplayType.TableCell or DisplayType.Table or DisplayType.InlineTable) return true;
        // display: flow-root explicitly establishes a BFC (it maps to Block in GetDisplay).
        if (RawStyle(node, "display") == "flow-root") return true;
        return false;
    }

    /// <summary>Reads a raw style value (override first, then declared) for a property.</summary>
    private static string? RawStyle(LayoutNode node, string prop)
        => node.TryResolveStyle(prop, out var ov) ? ov : node.Style.GetPropertyValueSafe(prop);

    /// <summary>True when the property's value is a percentage (e.g. "100%").</summary>
    private static bool IsPercentValue(LayoutNode node, string prop)
        => RawStyle(node, prop)?.TrimEnd().EndsWith("%") == true;

    /// <summary>
    /// The margin that collapses outward at a block's TOP edge (CSS 2.1 §8.3.1): the block's own
    /// margin-top collapsed with the effective top margins of its first in-flow block children,
    /// following the chain as long as each block has no top border/padding and establishes no block
    /// formatting context. Stops at a border/padding/BFC boundary, or when the first in-flow child
    /// is inline (non-empty text or inline-level box) — either of which contains the margin.
    /// <para>Percentage margins resolve against <paramref name="cbWidth"/> (the parent content
    /// width); descendant percentages reuse it as an approximation since the true per-level
    /// containing-block width isn't known before layout.</para>
    /// </summary>
    private static float GetEffectiveTopMargin(LayoutNode node, float cbWidth)
    {
        var fontSize = node.GetFontSize();
        var ownTop = node.GetMarginTop(total: cbWidth, size: fontSize);

        if (node.GetDisplay() is not (DisplayType.Block or DisplayType.ListItem)) return ownTop;
        if (EstablishesBlockFormattingContext(node)) return ownTop;
        var padding = node.GetPadding(cbWidth, 0, fontSize);
        var border = node.GetBorderWidth();
        if (padding.Top != 0f || border.Top != 0f) return ownTop;

        foreach (var child in node.Children)
        {
            var d = child.GetDisplay();
            if (d == DisplayType.None) continue;
            var pos = child.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed) continue;
            if (child.TagName == "#text")
            {
                // Whitespace-only text is skipped by the flow (inter-block artifact); real text is
                // inline content that contains the margin.
                if (string.IsNullOrWhiteSpace(child.DisplayText)) continue;
                return ownTop;
            }
            if (child.GetFloat() != FloatType.None) continue; // floats don't collapse margins
            var isBlock = d is DisplayType.Block or DisplayType.ListItem or DisplayType.Flex or DisplayType.Table;
            if (!isBlock) return ownTop; // inline-level first child contains the margin
            // First in-flow block child: its effective top margin collapses with this block's.
            return CollapseMargins(ownTop, GetEffectiveTopMargin(child, cbWidth));
        }
        return ownTop; // no in-flow children
    }

    /// <summary>
    /// Collapses two adjoining vertical margins per CSS 2.1 §8.3.1: the result is the sum of
    /// the largest positive and the most-negative margin (max(positives) + min(negatives)).
    /// </summary>
    internal static float CollapseMargins(float a, float b)
    {
        var posMax = Math.Max(Math.Max(a, 0f), Math.Max(b, 0f));
        var negMin = Math.Min(Math.Min(a, 0f), Math.Min(b, 0f));
        return posMax + negMin;
    }

    /// <summary>
    /// Returns the Y at which an element with the given <paramref name="clear"/> value
    /// should start, ensuring it is below any relevant active floats.
    /// </summary>
    private static float ApplyClear(ClearType clear, List<ActiveFloat> floats, float cursorY)
    {
        if (clear == ClearType.None || floats.Count == 0) return cursorY;
        var y = cursorY;
        foreach (var f in floats)
        {
            if (clear == ClearType.Both ||
                (clear == ClearType.Left && f.Side == FloatType.Left) ||
                (clear == ClearType.Right && f.Side == FloatType.Right))
            {
                y = Math.Max(y, f.Bottom);
            }
        }
        return y;
    }

    /// <summary>
    /// Computes the available horizontal band at a given Y position,
    /// narrowed by any active floats that overlap vertically.
    /// Returns (effectiveX, effectiveWidth).
    /// </summary>
    private static (float x, float w) AvailableBand(
        List<ActiveFloat> floats, float y, float height,
        float contentX, float contentW)
    {
        var left = contentX;
        var right = contentX + contentW;
        foreach (var f in floats)
        {
            // Float overlaps vertically with the band [y, y+height)?
            if (f.Bottom <= y || f.Top >= y + height) continue;
            if (f.Side == FloatType.Left)
                left = Math.Max(left, f.Right);
            else
                right = Math.Min(right, f.Left);
        }
        return (left, Math.Max(0, right - left));
    }

    /// <summary>The nearest float bottom strictly below <paramref name="y"/>, or null when no
    /// float reaches past it — i.e. the next Y at which the available band can only get wider.</summary>
    private static float? NextFloatBottom(List<ActiveFloat> floats, float y)
    {
        float? best = null;
        foreach (var f in floats)
            if (f.Bottom > y + 0.5f && (best is null || f.Bottom < best))
                best = f.Bottom;
        return best;
    }

    /// <summary>Width of the narrowest thing that must fit on the first line before it may be
    /// shortened any further: the first atomic inline box, or the first word of the first text
    /// item. Whitespace-only items are skipped — they are dropped at the start of a line.</summary>
    private static float FirstUnbreakableWidth(List<InlineItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case InlineItemKind.OutOfFlow or InlineItemKind.LineBreak:
                    continue;
                case InlineItemKind.Text when item.Text is { } t:
                    var trimmed = t.TrimStart();
                    if (trimmed.Length == 0) continue;
                    if (item.Node.GetWhiteSpace() is WhiteSpace.NoWrap or WhiteSpace.Pre) return item.Width;
                    var end = trimmed.IndexOf(' ');
                    var word = end < 0 ? trimmed : trimmed[..end];
                    using (var font = TextMeasure.CreateFont(item.Node))
                        return font.MeasureText(word);
                default:
                    return item.Width;
            }
        }
        return 0f;
    }

    /// <summary>
    /// The band (absolute left edge and width) of each successive line box of a wrapped text run
    /// starting at <paramref name="startY"/>, ending once no float reaches any lower — from there
    /// on every line has the containing block's full width, which the last entry carries.
    /// Returns null when no float shortens any of them.
    /// </summary>
    private static List<(float X, float Width)>? LineBandsFor(
        List<ActiveFloat> floats, float startX, float startY, float lineHeight,
        float contentX, float contentW, float firstLineWidth)
    {
        if (floats.Count == 0 || lineHeight <= 0f) return null;
        var lowest = floats.Max(f => f.Bottom);
        if (lowest <= startY + 0.5f) return null;

        var bands = new List<(float X, float Width)> { (startX, firstLineWidth) };
        var y = startY + lineHeight;
        // Bounded by the lowest float, so this cannot run away on a tall block.
        while (y < lowest && bands.Count < 512)
        {
            var (bx, bw) = AvailableBand(floats, y, lineHeight, contentX, contentW);
            bands.Add((bx, bw));
            y += lineHeight;
        }
        bands.Add((contentX, contentW));   // below every float: the full band
        return bands;
    }

    /// <summary>Remove floats whose bottom edge is at or above <paramref name="y"/>.</summary>
    private static void RetireFloats(List<ActiveFloat> floats, float y)
    {
        floats.RemoveAll(f => f.Bottom <= y);
    }

    /// <summary>
    /// Lays out a single floated child (shrink-to-fit width) and returns the ActiveFloat descriptor.
    /// </summary>
    /// <param name="openLineY">Top of the line box the float would join (NaN when none is open),
    /// with <paramref name="openLineUsedW"/> the width its content already occupies.</param>
    private static ActiveFloat LayoutFloat(
        LayoutNode child, FloatType side,
        List<ActiveFloat> floats,
        float contentX, float cursorY, float contentW,
        float viewportWidth, float viewportHeight, float parentContentHeight,
        float openLineY = float.NaN, float openLineUsedW = 0f)
    {
        var fontSize = child.GetFontSize();
        var margin = child.GetMargin(contentW, viewportHeight, fontSize);
        var padding = child.GetPadding(contentW, viewportHeight, fontSize);
        var border = child.GetBorderWidth();

        // Shrink-to-fit: use explicit width or half container as heuristic
        var explicitW = child.GetWidth(contentW);
        // A floated replaced box (§9.7 makes it block-level) is sized from the image, not
        // shrink-to-fit over children it does not have.
        var replaced = TryResolveReplacedSize(child, contentW, parentContentHeight, viewportHeight);
        if (replaced.HasValue) explicitW = replaced.Value.Width;
        var maxAvail = contentW - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right;
        float childContentW;
        if (explicitW > 0)
        {
            var isBB = child.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            childContentW = isBB
                ? Math.Max(0, explicitW - border.Left - border.Right - padding.Left - padding.Right)
                : explicitW;
        }
        else
        {
            // Shrink-to-fit (§10.3.5): min(max(min-content, available), max-content).
            childContentW = IntrinsicSizer.ShrinkToFit(child, Math.Max(0f, maxAvail), viewportHeight);
        }
        childContentW = Math.Max(0, childContentW);

        var outerW = margin.Left + border.Left + padding.Left + childContentW + padding.Right + border.Right + margin.Right;

        // Find placement Y — must not overlap existing floats on the same side
        var placeY = cursorY;

        // CSS 2.1 §9.5.1 (rules 5-6): the top of a float is aligned with the top of the CURRENT
        // line box — a float written after inline content sits beside that content, and only drops
        // below the line when it no longer fits there (rule 7). Without this, `text <img
        // style="float:right">` pushed the image onto its own line under the text.
        if (!float.IsNaN(openLineY) && openLineY < placeY)
        {
            var (lbx, lbw) = AvailableBand(floats, openLineY, 1, contentX, contentW);
            if (outerW <= lbw - openLineUsedW + 0.5f) placeY = openLineY;
        }
        // Determine X based on side, respecting existing floats
        float placeContentX;
        // Try to place; if it doesn't fit, slide down
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var (bx, bw) = AvailableBand(floats, placeY, 1, contentX, contentW);
            if (outerW <= bw + 0.5f)
            {
                if (side == FloatType.Left)
                    placeContentX = bx + margin.Left + border.Left + padding.Left;
                else
                    placeContentX = bx + bw - margin.Right - border.Right - padding.Right - childContentW;
                goto placed;
            }
            // Slide down past the nearest float bottom
            var nearest = float.MaxValue;
            foreach (var f in floats)
                if (f.Bottom > placeY) nearest = Math.Min(nearest, f.Bottom);
            if (nearest == float.MaxValue) break;
            placeY = nearest;
        }
        // Fallback: place at current position
        placeContentX = side == FloatType.Left
            ? contentX + margin.Left + border.Left + padding.Left
            : contentX + contentW - margin.Right - border.Right - padding.Right - childContentW;

    placed:
        var placeContentY = placeY + margin.Top + border.Top + padding.Top;

        // Lay out children inside the float
        var isBorderBox = child.Style.GetPropertyValueSafe("box-sizing") == "border-box";
        var explicitH = child.GetHeight(parentContentHeight, 0, viewportHeight);
        var knownH = explicitH > 0
            ? (isBorderBox ? Math.Max(0, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom) : explicitH)
            : 0f;

        var nodeDisplay = child.GetDisplay();
        var childContentH = (nodeDisplay == DisplayType.Flex || nodeDisplay == DisplayType.InlineFlex)
            ? FlexEngine.LayoutFlex(child, placeContentX, placeContentY, childContentW, knownH, viewportWidth, viewportHeight)
            : nodeDisplay == DisplayType.Table
                ? TableEngine.LayoutTable(child, placeContentX, placeContentY, childContentW, viewportWidth, viewportHeight)
                : LayoutChildren(child.Children, placeContentX, placeContentY, childContentW, viewportWidth, viewportHeight, knownH);

        if (childContentH == 0 && !string.IsNullOrEmpty(child.DisplayText))
        {
            using var font = TextMeasure.CreateFont(child);
            var lh = child.GetLineHeight(child.GetFontSize());
            var lines = TextMeasure.WrapText(child.DisplayText, Math.Max(childContentW, 1f), font, child.GetWhiteSpace(), lh);
            childContentH = lines.Sum(l => l.Height);
        }
        if (explicitH > 0)
            childContentH = isBorderBox
                ? Math.Max(0, explicitH - border.Top - border.Bottom - padding.Top - padding.Bottom)
                : explicitH;
        if (replaced.HasValue) childContentH = replaced.Value.Height;

        child.Box = new BoxDimensions
        {
            ContentBox = new SKRect(placeContentX, placeContentY,
                                    placeContentX + childContentW, placeContentY + childContentH),
            Padding = padding,
            Border = border,
            Margin = margin,
        };

        var outerTop = placeY;
        var outerBottom = placeY + margin.Top + border.Top + padding.Top + childContentH + padding.Bottom + border.Bottom + margin.Bottom;
        var outerLeft = placeContentX - padding.Left - border.Left - margin.Left;
        var outerRight = placeContentX + childContentW + padding.Right + border.Right + margin.Right;

        return new ActiveFloat(outerLeft, outerTop, outerRight, outerBottom, side);
    }

    /// <summary>Back-compat wrapper: returns content height INCLUDING any trailing child margin
    /// (used by callers that don't participate in parent–last-child margin collapsing). These
    /// callers (a float's content, an abs-pos box, a flex item) all establish a new block
    /// formatting context, so they own a fresh float context.</summary>
    private static float LayoutChildren(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0,
        float parentBorderPaddingTop = -1f,
        bool parentEstablishesBfc = false)
    {
        var h = LayoutChildrenImpl(children, contentX, contentY, contentW, viewportWidth, viewportHeight,
            parentContentHeight, parentBorderPaddingTop, parentEstablishesBfc,
            new List<ActiveFloat>(), ownsFloatContext: true, out var trailing);
        return h + trailing;
    }

    /// <summary>
    /// Lays out block-container children. Returns content height EXCLUDING the trailing margin
    /// of the last in-flow block child (reported via <paramref name="trailingMargin"/>), so the
    /// parent can collapse that margin through itself per CSS 2.1 §8.3.1.
    /// <para><paramref name="floats"/> is the active-float list of the containing block formatting
    /// context. A block that establishes a BFC owns its list; a normal (non-BFC) block reuses its
    /// ancestor BFC's list so its floats escape into that context (CSS 2.1 §9.5) — floats are not
    /// contained by a non-BFC parent and interact with the BFC's other floats. Only the BFC root
    /// (<paramref name="ownsFloatContext"/>) grows its height to contain the floats.</para>
    /// </summary>
    private static float LayoutChildrenImpl(
        List<LayoutNode> children,
        float contentX, float contentY,
        float contentW,
        float viewportWidth, float viewportHeight,
        float parentContentHeight,
        float parentBorderPaddingTop,
        bool parentEstablishesBfc,
        List<ActiveFloat> floats,
        bool ownsFloatContext,
        out float trailingMargin)
    {
        // Running-margin model (CSS 2.1 §8.3.1): runY is the committed content bottom (the border
        // bottom of the last non-collapsing box) and pendingMargin is the collapsing margin still
        // accumulating below it. The previous single cursor is equivalent to runY + pendingMargin.
        var runY = contentY;
        var pendingMargin = 0f;
        var firstBlockSeen = false;
        var lastPlacedWasBlock = false;
        // The line box a following float may join (§9.5.1): NaN once a block-level box has been
        // placed, since there is then no open line.
        var openLineY = float.NaN;
        var openLineUsedW = 0f;
        var i = 0;

        while (i < children.Count)
        {
            var child = children[i];
            var display = child.GetDisplay();

            if (display == DisplayType.None)
            {
                child.Box = default;
                i++;
                continue;
            }

            // Absolute/fixed elements are removed from normal flow, but the flow position they
            // WOULD have taken is their static position (§10.3.7 / §10.6.4) — the used value of
            // an 'auto' left/top. It is the current flow point: after the preceding in-flow
            // content (including the margin still collapsing below it), at the left edge of the
            // band left free by any floats there.
            var pos = child.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed)
            {
                var staticY = runY + pendingMargin;
                var (staticBandX, _) = AvailableBand(floats, staticY, 1, contentX, contentW);
                child.StaticX = staticBandX;
                child.StaticY = staticY;
                i++;
                continue;
            }

            // 'float' applies to elements, never to text runs. A #text node shares its parent's
            // computed style, so it would otherwise inherit a (non-inherited) float/clear from a
            // floated parent and spawn a phantom full-width float that collapses the flow band.
            var floatSide = child.TagName == "#text" ? FloatType.None : child.GetFloat();

            // 'clear' applies only to block-level boxes and floats (CSS 2.1 §9.5.2); inline-level
            // boxes ignore it. (Text nodes can carry a non-inherited 'clear' from the parent's
            // computed style — honoring it on an inline run would also stall the run collector.)
            var clear = child.GetClear();
            var isBlockLevel = display == DisplayType.Block || display == DisplayType.ListItem
                            || display == DisplayType.Flex || display == DisplayType.Table;
            if (clear != ClearType.None && (floatSide != FloatType.None || isBlockLevel))
            {
                var cleared = ApplyClear(clear, floats, runY + pendingMargin);
                RetireFloats(floats, cleared);
                // Keep runY + pendingMargin == cleared so the running margin still collapses with the
                // next box exactly as before (clearance behaviour unchanged).
                runY = cleared - pendingMargin;
            }

            // Handle floated elements — taken out of normal flow but affect available width
            if (floatSide != FloatType.None)
            {
                var af = LayoutFloat(child, floatSide, floats, contentX, runY + pendingMargin, contentW,
                                     viewportWidth, viewportHeight, parentContentHeight,
                                     openLineY, openLineUsedW);
                floats.Add(af);
                i++;
                continue;
            }

            if (isBlockLevel)
            {
                var childFontSize = child.GetFontSize();
                // Percentage margins resolve against the containing-block WIDTH (§8.3), so the
                // collapse math must use contentW — matching what LayoutBlock's GetMargin uses.
                var childMarginTop = child.GetMarginTop(total: contentW, size: childFontSize);
                // §8.3.1: when the child has no top border/padding and isn't a BFC, its own first
                // in-flow block child's top margin collapses THROUGH and propagates outward. The
                // effective top margin — the whole collapsed first-child chain — is what collapses
                // with the running margin, so it materialises as space ABOVE the child rather than
                // being absorbed inside it. (Positioning below still uses the child's OWN margin,
                // since the propagated grandchild margins are placed by the child's own recursion.)
                var childEffectiveTop = GetEffectiveTopMargin(child, contentW);

                // First in-flow child: its top margin collapses with the parent's (the child is
                // placed at the parent's content-box top) when the parent has no border/padding top
                // and does not establish a BFC (§8.3.1). Otherwise it collapses with the running
                // margin — max(positives)+min(negatives) so negative margins pull boxes together.
                var firstChildCollapse = !firstBlockSeen && parentBorderPaddingTop == 0f && !parentEstablishesBfc;
                var gap = firstChildCollapse ? 0f : CollapseMargins(pendingMargin, childEffectiveTop);
                firstBlockSeen = true;

                var borderTop = runY + gap;
                var (effX, effW) = AvailableBand(floats, borderTop, 1, contentX, contentW);
                var (h, childBottomMargin) = LayoutBlock(child, effX, borderTop - childMarginTop, effW,
                                                         viewportWidth, viewportHeight, parentContentHeight, floats);
                var borderBoxH = h - childMarginTop - childBottomMargin;

                // Self-collapsing block (§8.3.1): no in-flow content / border / padding / height (a
                // zero border-box height already implies height is auto-or-0 with no min-height), and
                // not a BFC → its top and bottom margins are adjoining, fold together into the running
                // margin, and the box contributes no height (so neighbouring margins collapse through).
                if (borderBoxH <= 0.01f && !EstablishesBlockFormattingContext(child))
                {
                    var own = CollapseMargins(childEffectiveTop, childBottomMargin);
                    pendingMargin = firstChildCollapse ? own : CollapseMargins(pendingMargin, own);
                }
                else
                {
                    // Commit: runY advances to the child's border-box bottom; its (effective, after
                    // its own last-child collapse-through) bottom margin becomes the running margin.
                    runY = borderTop + borderBoxH;
                    pendingMargin = childBottomMargin;
                }
                lastPlacedWasBlock = true;
                openLineY = float.NaN; // a block-level box closes any open line box
                i++;
            }
            else
            {
                // Collect consecutive inline / inline-block / BR children into a run. The current
                // child is always taken first (it is inline — blocks/floats are handled above), so
                // the run is never empty and i always advances. Subsequent block/float children end
                // the run for their own handling; 'clear' is ignored here (inline boxes don't clear).
                var run = new List<LayoutNode> { child };
                i++;
                while (i < children.Count)
                {
                    var d = children[i].GetDisplay();
                    // An out-of-flow box mid-run neither ends the line nor takes part in it: it
                    // stays in the run so the line it sits on is its static position, and so the
                    // inline content around it is not split onto two lines. (§9.7 blockifies it,
                    // so this test must precede the block-level one.)
                    var childPos = children[i].GetPosition();
                    var outOfFlow = childPos == PositionType.Absolute || childPos == PositionType.Fixed;
                    if (!outOfFlow)
                    {
                        if (d == DisplayType.Block || d == DisplayType.ListItem || d == DisplayType.Flex || d == DisplayType.Table) break;
                        if (children[i].TagName != "#text" && children[i].GetFloat() != FloatType.None) break;
                    }
                    run.Add(children[i]);
                    i++;
                }
                // Skip runs that are solely whitespace-only #TEXT nodes — these are
                // inter-block whitespace artifacts (e.g. newlines between <div>s).
                if (run.All(n => n.TagName == "#text" && n.DisplayText.Trim().Length == 0))
                    continue;

                // Inline content commits the pending block margin (margins don't collapse across a
                // line box) and lays out at the committed position.
                runY += pendingMargin;
                pendingMargin = 0f;
                var runH = LayoutInlineRun(run, contentX, runY, contentW, viewportWidth, viewportHeight,
                                           floats, out var lastLineTop, out var lastLineWidth);
                // Remember where the run's last line box sits: a float that comes right after it
                // belongs ON that line, not below it (§9.5.1).
                openLineY = runY + lastLineTop;
                openLineUsedW = lastLineWidth;
                runY += runH;
                lastPlacedWasBlock = false;
            }
        }

        // A block formatting context grows to contain its floats (CSS 2.1 §10.6.7); a non-BFC
        // block does not — its floats belong to an ancestor BFC and overflow this box. The last
        // in-flow block child's bottom margin can collapse through only if no float extends past it.
        var contentBottom = runY + pendingMargin;
        var flowBottom = contentBottom;
        if (ownsFloatContext)
            foreach (var f in floats)
                contentBottom = Math.Max(contentBottom, f.Bottom);

        trailingMargin = (lastPlacedWasBlock && contentBottom <= flowBottom) ? pendingMargin : 0f;
        return (contentBottom - contentY) - trailingMargin;
    }

    // -------------------------------------------------------------------------
    // Inline formatting context
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lays out a run of inline/inline-block nodes within a line box.
    /// Returns total height consumed by all line boxes.
    /// <para>CSS 2.1 §9.5: floats shorten the line boxes they are beside, and each line box gets
    /// its own band — a float that ends partway down the run stops narrowing the lines below it,
    /// which is how text flows around a float. <paramref name="floats"/> is the block formatting
    /// context's active-float list and <paramref name="originX"/>/<paramref name="maxWidth"/>
    /// the containing block's full content band; the band actually available is recomputed for
    /// every line.</para>
    /// </summary>
    /// <param name="lastLineTop">Offset from <paramref name="originY"/> to the top of the LAST line
    /// box this run produced, and <paramref name="lastLineWidth"/> the width its content used.
    /// A float that follows this run belongs on that line if it still fits (CSS 2.1 §9.5.1).</param>
    private static float LayoutInlineRun(
        List<LayoutNode> nodes,
        float originX, float originY,
        float maxWidth,
        float viewportWidth, float viewportHeight,
        List<ActiveFloat> floats,
        out float lastLineTop, out float lastLineWidth)
    {
        lastLineTop = 0f;
        lastLineWidth = 0f;
        var items = new List<InlineItem>();
        // Items are measured against the first line's band; a line that turns out wider re-measures
        // its text below.
        var (firstX, firstW) = AvailableBand(floats, originY, 1, originX, maxWidth);
        CollectInlineItems(nodes, items, firstW, viewportWidth, viewportHeight);

        if (items.Count == 0) return 0f;

        // CSS 2.1 §16.2: 'text-align' aligns the inline-level content of each LINE BOX, not just
        // text — an image, inline-block or inline-table on the line moves with it. It applies to
        // block containers and is inherited, so the value that governs this run is the containing
        // block's (an inline child may declare its own, which has no effect on the line box).
        // 'justify' is handled at paint time by Drawer.DrawWrappedText and left alone here.
        var container = nodes.Count > 0 ? nodes[0].Parent : null;
        var lineAlign = (container ?? nodes[0]).GetTextAlign();

        // The band available to the line under construction, as offsets from originX. Recomputed
        // for every line: a float only shortens the lines it is actually beside (§9.5).
        var lineY = 0f;

        // §9.5 rule 7: "if a shortened line box is too small to contain any content, it is shifted
        // downward until either it fits or there are no more floats present". Without this an
        // unbreakable word simply overflowed the narrow band beside the float.
        var firstContentW = FirstUnbreakableWidth(items);
        while (firstContentW > firstW + 0.5f)
        {
            var drop = NextFloatBottom(floats, originY + lineY);
            if (drop is not { } nextY) break;
            lineY = nextY - originY;
            (firstX, firstW) = AvailableBand(floats, originY + lineY, 1, originX, maxWidth);
        }

        var bandLeft = firstX - originX;
        var bandRight = bandLeft + firstW;
        var lineX = bandLeft;

        // Running CSS 2.1 §10.8.1 baseline-alignment accumulators for the line under construction:
        // maxAbove/maxBelow track the tallest reach above/below the shared baseline among items
        // aligned baseline/sub/super/middle; maxTopH/maxBottomH track the tallest 'top'/'bottom'
        // aligned items, which anchor to the line box's own edges instead of the baseline.
        var maxAbove = 0f;
        var maxBelow = 0f;
        var maxTopH = 0f;
        var maxBottomH = 0f;

        var placed = new List<(InlineItem item, float relX, float relY)>();
        var lineStart = 0;

        // Locals rather than the out parameters directly: C# forbids touching an 'out' parameter
        // from inside a local function.
        var committedLineTop = 0f;
        var committedLineWidth = 0f;

        void CommitLine()
        {
            // Resolve the final line-box height: start from the baseline-aligned items' reach,
            // then grow outward (below for 'top', above for 'bottom') if those need more room.
            var above = maxAbove;
            var below = maxBelow;
            if (maxTopH > above + below) below = maxTopH - above;
            if (maxBottomH > above + below) above = maxBottomH - below;
            var thisLineHeight = above + below > 0f ? above + below : Math.Max(maxTopH, maxBottomH);

            // The line's used width, ignoring trailing collapsible whitespace — it is removed at
            // the end of a line (§16.6.1), so counting it would land a right-aligned line a space
            // short and would overstate how much room a following float needs.
            var lineRight = 0f;
            for (var k = placed.Count - 1; k >= lineStart; k--)
            {
                var (it, rx, _) = placed[k];
                if (it.Kind == InlineItemKind.OutOfFlow) continue;
                if (it.Kind is InlineItemKind.Text or InlineItemKind.LineBreak &&
                    string.IsNullOrWhiteSpace(it.Text)) continue;
                lineRight = rx + it.Width;
                break;
            }
            committedLineTop = lineY;
            // Reported to the caller as the width used WITHIN the line's band, so it can decide
            // whether a following float still fits on that line.
            committedLineWidth = Math.Max(0f, lineRight - bandLeft);

            // §16.2 horizontal alignment: shift the whole line by the space left unused in ITS
            // band (which floats may have narrowed), not in the containing block.
            var dx = 0f;
            if (lineAlign is TextAlign.Center or TextAlign.Right)
            {
                var slack = bandRight - lineRight;
                if (slack > 0f) dx = lineAlign == TextAlign.Center ? slack / 2f : slack;
            }

            for (var k = lineStart; k < placed.Count; k++)
            {
                var (it, rx, _) = placed[k];
                var vAlign = it.Node.GetVerticalAlign();
                float yOffset = it.Kind == InlineItemKind.OutOfFlow ? 0f : vAlign switch
                {
                    VerticalAlignType.Top or VerticalAlignType.TextTop => 0f,
                    VerticalAlignType.Bottom or VerticalAlignType.TextBottom => thisLineHeight - it.Height,
                    _ => above - AboveBaselineComponent(it, vAlign),
                };
                placed[k] = (it, rx + dx, lineY + yOffset);
            }
            lineY += thisLineHeight;
            maxAbove = maxBelow = maxTopH = maxBottomH = 0f;
            // The next line sits lower, so it may clear a float the previous line was beside.
            var (nx, nw) = AvailableBand(floats, originY + lineY, 1, originX, maxWidth);
            bandLeft = nx - originX;
            bandRight = bandLeft + nw;
            lineX = bandLeft;
            lineStart = placed.Count;
        }

        foreach (var item in items)
        {
            if (item.Kind == InlineItemKind.LineBreak)
            {
                // An otherwise-empty line still needs the <br>'s own font metrics for its height.
                if (maxAbove + maxBelow + maxTopH + maxBottomH <= 0f)
                {
                    maxAbove = item.Ascent;
                    maxBelow = item.Height - item.Ascent;
                }
                CommitLine();
                continue;
            }

            // An out-of-flow marker takes no space and must not contribute to the line's height
            // or its used width: it only records where the line had got to.
            if (item.Kind == InlineItemKind.OutOfFlow)
            {
                placed.Add((item, lineX, lineY));
                continue;
            }

            if (lineX > bandLeft && lineX + item.Width > bandRight)
                CommitLine();

            // Skip whitespace-only text at the start of a line
            if (lineX <= bandLeft && item.Kind == InlineItemKind.Text &&
                item.Text != null && item.Text.Trim().Length == 0)
                continue;

            // For text items wider than the available space, re-measure with wrapping
            var effectiveItem = item;
            if (item.Kind == InlineItemKind.Text && item.Text != null)
            {
                var availW = bandRight - lineX;
                if (availW > 0 && item.Width > availW)
                {
                    using var font = TextMeasure.CreateFont(item.Node);
                    var ws = item.Node.GetWhiteSpace();
                    var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
                    // The paragraph wraps line by line against the band each line actually has:
                    // beside a float the first lines are narrow and the ones below it are not.
                    var bands = LineBandsFor(floats, originX + lineX, originY + lineY, lh,
                                             originX, maxWidth, availW);
                    var wrapLines = TextMeasure.WrapText(item.Text, Math.Max(availW, 1f), font, ws, lh,
                                                         bands?.Select(b => b.Width).ToList());
                    var wrappedH = wrapLines.Sum(l => l.Height);
                    var usedW = wrapLines.Count > 0 ? wrapLines.Max(l => l.Width) : availW;
                    item.Node.LineBands = bands;
                    effectiveItem = item with { Width = Math.Max(availW, usedW), Height = wrappedH,
                                                ContentW = availW, ContentH = wrappedH };
                }
                else item.Node.LineBands = null;
            }

            var va = effectiveItem.Node.GetVerticalAlign();
            switch (va)
            {
                case VerticalAlignType.Top or VerticalAlignType.TextTop:
                    maxTopH = Math.Max(maxTopH, effectiveItem.Height);
                    break;
                case VerticalAlignType.Bottom or VerticalAlignType.TextBottom:
                    maxBottomH = Math.Max(maxBottomH, effectiveItem.Height);
                    break;
                default:
                    maxAbove = Math.Max(maxAbove, AboveBaselineComponent(effectiveItem, va));
                    maxBelow = Math.Max(maxBelow, effectiveItem.Height - AboveBaselineComponent(effectiveItem, va));
                    break;
            }

            placed.Add((effectiveItem, lineX, lineY));
            lineX += effectiveItem.Width;
        }
        if (placed.Count > lineStart) CommitLine();

        foreach (var (item, relX, relY) in placed)
        {
            ApplyInlineItem(item, originX + relX, originY + relY, viewportWidth, viewportHeight);
        }

        lastLineTop = committedLineTop;
        lastLineWidth = committedLineWidth;
        return lineY;
    }

    /// <summary>
    /// For an item aligned baseline/sub/super/middle, the distance from ITS OWN top down to
    /// wherever it aligns against the line's shared baseline (CSS 2.1 §10.8.1). 'baseline' uses
    /// the item's intrinsic ascent directly; 'sub'/'super' shift it down/up by ~0.15em (matching
    /// the ratio Drawer/the old code already used); 'middle' aligns the item's vertical centre
    /// with the baseline plus half an x-height (approximated as a quarter of the font size).
    /// </summary>
    private static float AboveBaselineComponent(InlineItem item, VerticalAlignType vAlign)
    {
        var fontSize = item.Node.GetFontSize();
        return vAlign switch
        {
            VerticalAlignType.Middle => item.Height / 2f + fontSize * 0.25f,
            VerticalAlignType.Sub => item.Ascent - fontSize * 0.15f,
            VerticalAlignType.Super => item.Ascent + fontSize * 0.15f,
            _ => item.Ascent, // baseline
        };
    }

    /// <summary>
    /// Recursively extracts a flat list of InlineItems from inline nodes.
    /// </summary>
    private static void CollectInlineItems(
        IEnumerable<LayoutNode> nodes,
        List<InlineItem> items,
        float maxWidth,
        float viewportWidth, float viewportHeight)
    {
        foreach (var node in nodes)
        {
            var display = node.GetDisplay();
            if (display == DisplayType.None) continue;

            // Absolute/fixed are out of flow: they add nothing to the line, but a zero-sized
            // marker rides along so the line box position they would have occupied is recorded
            // as their static position (§10.3.7 / §10.6.4).
            var nodePos = node.GetPosition();
            if (nodePos == PositionType.Absolute || nodePos == PositionType.Fixed)
            {
                items.Add(new InlineItem(InlineItemKind.OutOfFlow, node, null, 0, 0,
                           default, default, default, 0, 0, 0));
                continue;
            }

            // <br> → forced line break item
            if (node.TagName == "BR")
            {
                using var brFont = TextMeasure.CreateFont(node);
                var brH = brFont.Size * 1.4f;
                items.Add(new InlineItem(InlineItemKind.LineBreak, node, null, 0, brH,
                           default, default, default, 0, brH, TextMeasure.ComputeAscent(brFont, brH)));
                continue;
            }

            if (display == DisplayType.InlineTable)
            {
                // §17.2 / §9.2.2: an inline-table participates in the line box like inline-block,
                // laying out internally as a table shrink-wrapped to its content (clamped to the
                // available line width).
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();

                var avail = maxWidth > 0 ? maxWidth : viewportWidth;
                var availContent = Math.Max(0f, avail - margin.Left - margin.Right
                    - border.Left - border.Right - padding.Left - padding.Right);
                var w = Math.Max(0f, TableEngine.MeasureTableWidth(node, availContent, viewportWidth, viewportHeight));

                var contentX2 = margin.Left + border.Left + padding.Left;
                var contentY2 = margin.Top + border.Top + padding.Top;
                var h = Math.Max(0f, TableEngine.LayoutTable(node, contentX2, contentY2, w, viewportWidth, viewportHeight));
                // An explicit height on the table acts as a minimum for the table box (§17.5.3).
                if (!node.IsAutoHeight())
                    h = Math.Max(h, node.GetHeight(viewportHeight, 0, viewportHeight));

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                // §17.5.3: the baseline of an inline-table is the baseline of its first row. The
                // preliminary LayoutTable call above already positioned rows/cells in this item's
                // own (0,0)-anchored frame, so the offset it finds is directly usable as Ascent.
                // No baseline content (e.g. an empty table) falls back to the bottom margin edge.
                var tableAscent = TableEngine.GetFirstRowBaseline(node) ?? totalH;

                items.Add(new InlineItem(InlineItemKind.InlineTable, node, null, totalW, totalH,
                           margin, padding, border, w, h, tableAscent));
                continue;
            }

            if (display == DisplayType.InlineFlex)
            {
                // §5: inline-flex acts as inline-block in the parent, uses flex layout internally.
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                // Intrinsic width: max-content of flex items (or explicit width)
                var w = explicitW > 0
                    ? explicitW
                    : FlexEngine.MeasureMaxContentMain(node, 0, 0, viewportWidth, viewportHeight);
                w = Math.Max(w, 0);

                // Intrinsic height: lay out children to compute
                var contentX2 = margin.Left + border.Left + padding.Left;
                var contentY2 = margin.Top + border.Top + padding.Top;
                var h = explicitH > 0
                    ? explicitH
                    : FlexEngine.LayoutFlex(node, contentX2, contentY2, w, 0, viewportWidth, viewportHeight);
                h = Math.Max(h, 0);

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                items.Add(new InlineItem(InlineItemKind.InlineFlex, node, null, totalW, totalH,
                           margin, padding, border, w, h, totalH));
                continue;
            }

            if (display == DisplayType.InlineBlock)
            {
                var fontSize = node.GetFontSize();
                var margin = node.GetMargin(0, viewportHeight, fontSize);
                var padding = node.GetPadding(0, viewportHeight, fontSize);
                var border = node.GetBorderWidth();
                var explicitW = node.GetWidth(0);
                var explicitH = node.GetHeight(viewportHeight);

                node.Attributes.TryGetValue("type", out var iType);
                var inputType = iType?.ToLowerInvariant() ?? "text";
                var isCheckbox = node.TagName == "INPUT" && inputType == "checkbox";
                var isRadio = node.TagName == "INPUT" && inputType == "radio";
                var isRange = node.TagName == "INPUT" && inputType == "range";
                float defaultW, defaultH;
                // Form controls are sized (and baseline-aligned) as replaced boxes; everything else
                // is a real inline-block whose baseline comes from its own content.
                bool isFormControl = true;
                if (isCheckbox) { defaultW = FormLayout.CheckboxSize; defaultH = FormLayout.CheckboxSize; }
                else if (isRadio) { defaultW = FormLayout.RadioSize; defaultH = FormLayout.RadioSize; }
                else if (isRange) { defaultW = FormLayout.RangeWidth; defaultH = FormLayout.RangeHeight; }
                else if (node.TagName == "BUTTON") { defaultW = 0f; defaultH = FormLayout.TextInputHeight; }
                else if (node.TagName == "TEXTAREA") { defaultW = FormLayout.TextareaWidth; defaultH = FormLayout.TextareaHeight; }
                else if (node.TagName == "SELECT") { defaultW = FormLayout.SelectWidth; defaultH = FormLayout.SelectHeight; }
                else if (node.TagName == "PROGRESS") { defaultW = FormLayout.ProgressWidth; defaultH = FormLayout.ProgressHeight; }
                else if (node.TagName == "METER") { defaultW = FormLayout.MeterWidth; defaultH = FormLayout.MeterHeight; }
                else { defaultW = FormLayout.TextInputWidth; defaultH = FormLayout.TextInputHeight; isFormControl = false; }

                var w = explicitW > 0 ? explicitW : defaultW;
                var h = explicitH > 0 ? explicitH : defaultH;

                if (node.TagName == "BUTTON" && w <= 0)
                {
                    var btnLabel = node.DisplayText;
                    if (string.IsNullOrEmpty(btnLabel)) node.Attributes.TryGetValue("value", out btnLabel);
                    if (string.IsNullOrEmpty(btnLabel)) btnLabel = "Button";
                    using var btnFont = new SKFont { Size = 13 };
                    w = btnFont.MeasureText(btnLabel) + FormLayout.ButtonPaddingX * 2;
                    h = 13f + FormLayout.ButtonPaddingY * 2;
                }

                var totalW = margin.Left + border.Left + padding.Left + w + padding.Right + border.Right + margin.Right;
                var totalH = margin.Top + border.Top + padding.Top + h + padding.Bottom + border.Bottom + margin.Bottom;

                // CSS 2.1 §10.8.1: the baseline of an inline-block is the baseline of its LAST
                // in-flow line box — NOT its bottom margin edge. The bottom edge is only correct
                // when the box has no in-flow line boxes or its 'overflow' computes to something
                // other than 'visible' (and for replaced boxes, handled elsewhere). Without this a
                // text-bearing inline-block rides above the surrounding text's baseline by roughly
                // its leading. Only the leaf case (the box's content is its own text, e.g. a
                // generated-content ::after) is resolved here: a box with element children has not
                // had them laid out at this point, so it keeps the bottom-edge fallback.
                var ascent = totalH;
                if (!isFormControl
                    && node.Children.Count == 0
                    && !string.IsNullOrEmpty(node.DisplayText)
                    && node.GetOverflow() == OverflowType.Visible)
                {
                    using var ibFont = TextMeasure.CreateFont(node);
                    ascent = margin.Top + border.Top + padding.Top
                             + TextMeasure.ComputeAscent(ibFont, node.GetLineHeight(fontSize));
                }

                items.Add(new InlineItem(InlineItemKind.InlineBlock, node, null, totalW, totalH,
                           margin, padding, border, w, h, ascent));
            }
            else if (node.TagName == "IMG" || (node.TagName == "OBJECT" && node.Image != null))
            {
                var w = node.IntrinsicWidth > 0 ? (float)node.IntrinsicWidth : node.Image?.Width ?? 100f;
                var h = node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 100f;
                // Replaced elements have no baseline of their own — CSS 2.1 §10.8's fallback rule
                // aligns their bottom margin edge with the line's baseline (Ascent = full height).
                items.Add(new InlineItem(InlineItemKind.Image, node, null, w, h,
                           default, default, default, w, h, h));
            }
            else if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                var lh = node.GetLineHeight(node.GetFontSize());
                var (w, h, ascent) = TextMeasure.MeasureSingleLine(node.DisplayText, font, lh);
                items.Add(new InlineItem(InlineItemKind.Text, node, node.DisplayText, w, h,
                           default, default, default, w, h, ascent));
            }
            else if (node.Children.Count > 0)
            {
                CollectInlineItems(node.Children, items, maxWidth, viewportWidth, viewportHeight);
            }
        }
    }

    /// <summary>
    /// Finds the baseline of the first in-flow line box under <paramref name="node"/>, as an
    /// absolute Y in whatever coordinate space <c>node</c>'s subtree was just laid out in (the
    /// caller subtracts its own reference point). Used for §17.5.3 ("the baseline of an
    /// inline-table is the baseline of the first row"), so this only looks at the FIRST
    /// baseline-contributing descendant in document order — display:none and out-of-flow nodes
    /// are skipped, but visibility:hidden ones still occupy space and so still count (visibility
    /// doesn't affect layout). Returns null if the subtree has no baseline content at all (e.g.
    /// empty cells), so callers can fall back to a bottom-edge alignment.
    /// </summary>
    internal static float? FindFirstBaselineY(LayoutNode node)
    {
        if (node.GetDisplay() == DisplayType.None) return null;
        var pos = node.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return null;

        // A leaf with its own text (a bare-text element, or a synthesized #text node) got its own
        // Box set directly by ApplyInlineItem's Text case — its content-box top is the line top.
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            if (node.DisplayText.Trim().Length == 0) return null; // whitespace-only: no line box
            using var font = TextMeasure.CreateFont(node);
            var lh = node.GetLineHeight(node.GetFontSize());
            return node.Box.ContentBox.Top + TextMeasure.ComputeAscent(font, lh);
        }
        // Replaced elements have no baseline of their own — bottom margin edge (§10.8 fallback).
        if (node.TagName == "IMG" || (node.TagName == "OBJECT" && node.Image != null))
            return node.Box.ContentBox.Bottom;

        foreach (var child in node.Children)
        {
            if (child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText)) continue;
            var y = FindFirstBaselineY(child);
            if (y.HasValue) return y;
        }
        return null;
    }

    private static void ApplyInlineItem(InlineItem item, float absX, float absY,
        float viewportWidth, float viewportHeight)
    {
        var node = item.Node;
        switch (item.Kind)
        {
            case InlineItemKind.InlineTable:
                {
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    // Re-run table layout at the resolved position so cells get their final boxes.
                    TableEngine.LayoutTable(node, contentX, contentY, item.ContentW, viewportWidth, viewportHeight);
                    break;
                }
            case InlineItemKind.InlineBlock:
                {
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    break;
                }
            case InlineItemKind.InlineFlex:
                {
                    var m = item.Margin;
                    var p = item.Padding;
                    var b = item.Border;
                    var contentX = absX + m.Left + b.Left + p.Left;
                    var contentY = absY + m.Top + b.Top + p.Top;
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(contentX, contentY,
                                                contentX + item.ContentW, contentY + item.ContentH),
                        Margin = m,
                        Padding = p,
                        Border = b,
                    };
                    // Re-invoke flex layout at the resolved position so children get correct boxes
                    FlexEngine.LayoutFlex(node, contentX, contentY, item.ContentW, item.ContentH, 0, 0);
                    break;
                }
            case InlineItemKind.OutOfFlow:
                {
                    // Records only — the box itself is laid out later by the positioned pass.
                    node.StaticX = absX;
                    node.StaticY = absY;
                    break;
                }
            case InlineItemKind.Image:
            case InlineItemKind.Text:
            case InlineItemKind.LineBreak:
                {
                    node.Box = new BoxDimensions
                    {
                        ContentBox = new SKRect(absX, absY, absX + item.ContentW, absY + item.ContentH),
                    };
                    break;
                }
        }
    }

    // -------------------------------------------------------------------------
    // Inline item model
    // -------------------------------------------------------------------------

    /// <summary><see cref="OutOfFlow"/> is a zero-sized marker for an absolutely positioned box
    /// met inside an inline run: it occupies no space on the line but rides along with it, so the
    /// line position it lands on is the box's static position (§10.3.7 / §10.6.4).</summary>
    private enum InlineItemKind { Text, Image, InlineBlock, InlineFlex, InlineTable, LineBreak, OutOfFlow }

    /// <summary><see cref="Ascent"/> is the distance from this item's own top (margin edge) down
    /// to the baseline it should align on (CSS 2.1 §10.8). Text uses the font's half-leading
    /// ascent; a replaced box (image) or one with no baseline of its own (inline-block,
    /// inline-flex) uses its full height, so 'baseline' alignment reduces to aligning its bottom
    /// margin edge with the line's baseline — the spec's fallback rule. inline-table uses the
    /// baseline of its first row (§17.5.3).</summary>
    private record InlineItem(
        InlineItemKind Kind,
        LayoutNode Node,
        string? Text,
        float Width,
        float Height,
        EdgeSizes Margin,
        EdgeSizes Padding,
        EdgeSizes Border,
        float ContentW,
        float ContentH,
        float Ascent
    );
}
