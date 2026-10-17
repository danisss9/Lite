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
        // Second pass: lay out all absolute/fixed nodes now that normal-flow boxes are finalised.
        // With no positioned ancestor, an absolutely positioned box is contained by the initial
        // containing block (CSS 2.1 §10.1), which is the viewport, not the root element's box.
        var initialContainingBlock = new BoxDimensions { ContentBox = new SKRect(0, 0, viewportWidth, viewportHeight) };
        LayoutPositioned(root, root.IsPositioned() ? root.Box : initialContainingBlock, viewportWidth, viewportHeight);
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
        => n.TagName != "A" && IsSplittableInline(n) && IsInFlow(n) && InlineContainsInFlowBlock(n);

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
                or DisplayType.Flex or DisplayType.Table
            && !(IsCssTableCaption(n) && WhitespaceOnlyInterior(n));

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
        // GetBorderWidth ignores StyleOverrides by design (Acid2), so the suppression needs the
        // explicit flag to keep the inherited border out of the hoisted block's layout height.
        block.SharedBorderSuppressed = true;
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
        // Anonymous boxes are a FUNCTION of the current display values, so a pass that only ever
        // adds wrappers cannot track a change to one. `t.style.display = 'none'` made the engine
        // wrap that cell in an anonymous cell + table (a display:none box is not a proper table
        // child), and restoring 'table-cell' left the wrappers in place — the cell stayed nested
        // in a table of its own. Dropping the previously generated boxes first makes each pass
        // rebuild them from the element tree, so normalization tracks the DOM.
        UnwrapGeneratedTableBoxes(node);

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

    /// <summary>Replaces this node's previously generated anonymous table boxes with their own
    /// children, in place, so the next pass regenerates them from the current display values.
    /// Only the boxes this engine synthesized (<c>#anon-…</c>) are dissolved; real elements and
    /// the text nodes migrated into them are kept exactly where they are.</summary>
    private static void UnwrapGeneratedTableBoxes(LayoutNode node)
    {
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (!child.TagName.StartsWith("#anon-", StringComparison.Ordinal)) continue;

            UnwrapGeneratedTableBoxes(child);      // dissolve inside-out
            node.Children.RemoveAt(i);
            for (var j = child.Children.Count - 1; j >= 0; j--)
            {
                var grandChild = child.Children[j];
                grandChild.Parent = node;
                node.Children.Insert(i, grandChild);
            }
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
        // Whitespace-only captions count too — they are dropped in the loop below (§17.2.1),
        // and without them the run would break around the invisible block.
        var anyMisparented = false;
        foreach (var c in node.Children)
            if (IsMisparentedTableBox(c) || (IsCssTableCaption(c) && WhitespaceOnlyInterior(c)))
                { anyMisparented = true; break; }
        if (!anyMisparented) return;

        // §17.2.1: "If the box's parent is an inline box, generate an anonymous INLINE-table box;
        // otherwise, an anonymous table box." A block-level table inside an inline box broke the
        // line in two, so cells written between inline text — `a <span table-cell>b</span>c` —
        // landed on their own line instead of flowing with it.
        var inlineContext = node.GetDisplay() == DisplayType.Inline;

        var newChildren = new List<LayoutNode>();
        var run = new List<LayoutNode>();
        // Render-invisible children (scripts, display:none) neither join a run of misparented
        // content nor split one (§17.2.1): reaching the main loop directly would flush the run
        // at the script, so they are parked here and reinserted after the run they interrupted.
        var deferred = new List<LayoutNode>();

        // True when another misparented table box follows in the child list, skipping
        // whitespace-only text and non-rendered nodes: such a box continues the current run.
        // A <script> (display:none) between two cells is not renderable content (§16.6.1),
        // so the cells still share one anonymous table — treating it as flow content split
        // the run and rendered "b c" where the row reads "bc".
        bool AnotherMisparentedFollows(int fromIndex)
        {
            for (var k = fromIndex + 1; k < node.Children.Count; k++)
            {
                var later = node.Children[k];
                if (later.TagName == "#text" && string.IsNullOrWhiteSpace(later.DisplayText)) continue;
                if (later.GetDisplay() == DisplayType.None) continue;
                return IsMisparentedTableBox(later);
            }
            return false;
        }

        void FlushRun()
        {
            if (run.Count > 0)
            {
                var anon = new LayoutNode(null, "#anon-table", "", node.Style);
                anon.ResetNonInheritedStyles();
                anon.StyleOverrides["display"] = inlineContext ? "inline-table" : "table";
                foreach (var side in new[] { "top", "right", "bottom", "left" })
                {
                    anon.StyleOverrides[$"margin-{side}"] = "0";
                    anon.StyleOverrides[$"padding-{side}"] = "0";
                    anon.StyleOverrides[$"border-{side}-width"] = "0";
                }
                // The generated table has no cellspacing attribute, so the UA sheet's default
                // spacing never applies to it (§17.6.1 initial value): with it, every cell gap
                // in 'a <cells>b c</cells> d' shifted the row ~2px per boundary against the
                // plain-text reference and the white-space reftests showed red fringes.
                anon.StyleOverrides["border-spacing"] = "0";
                anon.Parent = node;
                foreach (var c in run) { c.Parent = anon; anon.Children.Add(c); }
                run.Clear();
                // A bare cell still needs a row between it and the table.
                WrapAnonymousTableBoxes(anon, wrapAsRow: true);
                newChildren.Add(anon);
            }
            newChildren.AddRange(deferred);
            deferred.Clear();
        }

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            // A whitespace-only caption generates nothing (§17.2.1): dropping it here keeps the
            // surrounding inline flow on one line instead of breaking at an invisible block.
            if (IsCssTableCaption(child) && WhitespaceOnlyInterior(child))
                continue;
            if (child.GetDisplay() == DisplayType.None)
            {
                // A display:none TABLE-INTERNAL box (a cell toggled to none and back by script)
                // keeps its DOM-relative position: the onload relayout is a style mutation, which
                // re-runs the normalizers on the existing tree instead of re-projecting DOM order,
                // so a cell parked at the run's end stayed there and rendered "a  dbc" where the
                // row reads "a bc d". Non-table content (scripts, hidden elements) defers — it is
                // not renderable content (§16.6.1), so it neither joins nor splits a run.
                var cascadedDisplay = child.Style.GetPropertyValueSafe("display")?.Trim();
                var tableInternal = cascadedDisplay is "table" or "inline-table" or "table-row"
                    or "table-row-group" or "table-header-group" or "table-footer-group"
                    or "table-column" or "table-column-group" or "table-caption" or "table-cell";
                if (!tableInternal) { deferred.Add(child); continue; }
            }
            if (IsMisparentedTableBox(child)) { run.Add(child); continue; }
            // Whitespace strictly BETWEEN two misparented table boxes is table-internal and is
            // not content (§17.2.1) — it must not split one table into several. Whitespace at the
            // run's END precedes ordinary inline content, so it belongs to the OUTER inline flow
            // (§16.6.1) and must survive as the space between the anonymous table and what
            // follows — dropping it made the red layer of the white-space reftests render
            // "a bcd" where the reference reads "a bc d".
            if (run.Count > 0 && child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText))
            {
                if (AnotherMisparentedFollows(i)) continue;
                FlushRun();
                newChildren.Add(child);
                continue;
            }
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

    /// <summary>True when a box's interior is only collapsible whitespace (or nothing): per
    /// §17.2.1 such a box generates nothing in anonymous-table construction. A whitespace-only
    /// <c>display:table-caption</c> span in inline flow otherwise breaks the run like a block
    /// while painting nothing, splitting 'a <caption></caption> bc d' onto three lines.</summary>
    private static bool WhitespaceOnlyInterior(LayoutNode node)
    {
        if (!string.IsNullOrEmpty(node.Text) && !string.IsNullOrWhiteSpace(node.Text)) return false;
        foreach (var c in node.Children)
        {
            if (c.GetDisplay() == DisplayType.None) continue;
            if (c.TagName == "#text")
            {
                if (!string.IsNullOrWhiteSpace(c.DisplayText)) return false;
            }
            else return false; // an element child counts as content
        }
        return true;
    }

    /// <summary>True for a box styled <c>display:table-caption</c> — GetDisplay maps the
    /// keyword to Block (captions lay out as blocks above/below their table), so the raw
    /// value is what tells a CSS caption from an ordinary block.</summary>
    private static bool IsCssTableCaption(LayoutNode node) =>
        node.TryResolveStyle("display", out var d)
            ? d?.Trim() == "table-caption"
            : node.Style.GetPropertyValueSafe("display")?.Trim() == "table-caption";

    /// <summary>True for a table container (table/inline-table/row-group/row/caption) whose
    /// interior generates nothing — the anonymous-table construction around it is skipped
    /// ("anonymous table objects only exist if the table has content").</summary>
    private static bool IsEmptyTableBox(LayoutNode node)
    {
        var raw = node.TryResolveStyle("display", out var d)
            ? d?.Trim() : node.Style.GetPropertyValueSafe("display")?.Trim();
        return raw is "table" or "inline-table" or "table-row-group" or "table-row"
            or "table-header-group" or "table-footer-group" or "table-caption"
            && WhitespaceOnlyInterior(node);
    }

    /// <summary>
    /// A table/row-group/row whose content is bare text (e.g. a leaf <c>&lt;div
    /// display:inline-table&gt;some text&lt;/div&gt;</c>) holds that text as its OWN
    /// <see cref="LayoutNode.Text"/> rather than a <c>#text</c> child — the parser only splits
    /// text into an ordered child when the element also has renderable element children (see
    /// <c>Parser.Traverse</c>'s <c>hasMixedChildren</c> check, which excludes scripts).
    /// <see cref="WrapAnonymousTableBoxes"/> only wraps <c>Children</c>, so that text would
    /// otherwise be invisible to anonymous-box generation (§17.2.1) and the table would end up
    /// with zero rows. Splitting it into a real <c>#text</c> child makes it "misparented
    /// content" like any other, so the normal wrapping path picks it up. Dynamic DOM mutation
    /// (appendChild of a text node) leaves a node with BOTH own text and #text children — the
    /// own text is the leading content, so it is inserted as the first child. The static parser
    /// path never produces this shape (mixed content empties own text), so no duplication.
    /// </summary>
    private static void MigrateOwnTextToChild(LayoutNode node)
    {
        if (string.IsNullOrEmpty(node.Text) || node.OwnTextMigratedToChild) return;
        node.OwnTextMigratedToChild = true;
        var textChild = new LayoutNode(null, "#text", node.Text, node.Style) { Parent = node };
        // The child's Style object is shared with `node` (the usual #text convention), which
        // leaks node's own NON-inherited "display" (here table/inline-table) onto it — without
        // this override the text would be mistaken for another nested table (CollectInlineItems
        // dispatches purely on GetDisplay()), which is both wrong and pathologically slow.
        textChild.StyleOverrides["display"] = "inline";
        node.Children.Insert(0, textChild);
    }

    /// <summary>Wraps runs of misparented children into anonymous table-rows (<paramref name="wrapAsRow"/>)
    /// or table-cells. Only rebuilds the child list if at least one anonymous box is created, so
    /// proper tables are untouched. Whitespace-only runs between real boxes are dropped, not wrapped.</summary>
    private static void WrapAnonymousTableBoxes(LayoutNode parent, bool wrapAsRow)
    {
        var newChildren = new List<LayoutNode>();
        var run = new List<LayoutNode>();
        var created = false;
        // Render-invisible boxes (scripts, display:none) must not join a run of misparented
        // content nor split one (a script between two bare texts is ONE cell, not two), so
        // they are parked here and reinserted after the run they interrupted flushes.
        var deferred = new List<LayoutNode>();

        void FlushRun()
        {
            if (run.Count == 0) { newChildren.AddRange(deferred); deferred.Clear(); return; }
            // A run whose interior is only collapsible whitespace generates no cell (§17.2.1):
            // trailing whitespace after the last proper child must not fabricate an empty one.
            // Nor does an empty TABLE-ish element ("anonymous table objects only exist if the
            // table has content" — tables-001): a display:table with no rows inside a run must
            // not become a cell for the surrounding spacing to hang on.
            if (run.All(c => (c.TagName == "#text" && string.IsNullOrWhiteSpace(c.DisplayText))
                || (!c.TagName.StartsWith('#') && IsEmptyTableBox(c))))
            { run.Clear(); newChildren.AddRange(deferred); deferred.Clear(); return; }
            var anon = new LayoutNode(null, wrapAsRow ? "#anon-row" : "#anon-cell", "", parent.Style);
            anon.ResetNonInheritedStyles();
            anon.StyleOverrides["display"] = wrapAsRow ? "table-row" : "table-cell";
            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                anon.StyleOverrides[$"margin-{side}"] = "0";
                anon.StyleOverrides[$"padding-{side}"] = "0";
                anon.StyleOverrides[$"border-{side}-width"] = "0";
            }
            anon.Parent = parent;
            foreach (var c in run)
            {
                c.Parent = anon;
                anon.Children.Add(c);
                // A #text child created outside the parser (a JS appendChild into an inline
                // table) shares its parent's Style object, so without an explicit override it
                // can inherit a table-level display and lay out as its own block inside the
                // anonymous cell instead of flowing with the rest of the run.
                if (c.TagName == "#text" && !c.StyleOverrides.ContainsKey("display"))
                    c.StyleOverrides["display"] = "inline";
            }
            run.Clear();
            // Content inside a freshly-made anonymous row still needs an anonymous cell.
            if (wrapAsRow) WrapAnonymousTableBoxes(anon, wrapAsRow: false);
            newChildren.Add(anon);
            newChildren.AddRange(deferred);
            deferred.Clear();
            created = true;
        }

        // True when another proper table child follows at fromIndex, skipping whitespace-only
        // text and non-rendered nodes — the counterpart of lastWasProper for the drop rule.
        bool ProperTableChildFollows(int fromIndex)
        {
            for (var k = fromIndex + 1; k < parent.Children.Count; k++)
            {
                var later = parent.Children[k];
                if (later.TagName == "#text" && string.IsNullOrWhiteSpace(later.DisplayText)) continue;
                if (later.GetDisplay() == DisplayType.None || later.TagName == "SCRIPT") continue;
                if (IsCssTableCaption(later) && WhitespaceOnlyInterior(later)) continue;
                return IsProperTableChild(later, atTableLevel: wrapAsRow);
            }
            return false;
        }

        var lastWasProper = false;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            var child = parent.Children[i];
            // Whitespace-only text is dropped only when it sits BETWEEN proper table children
            // (table-internal, §17.2.1). Whitespace beside non-cell content is that content's
            // interior: consecutive inline spans in a row merge into ONE anonymous cell, and
            // the collapsible spaces between them render inside it ('Row 1, Col 1' must keep
            // its space against the real-table reference — dropping them under-measured and
            // misaligned the infer-cells overlays). Under pre/pre-wrap the space is content
            // anywhere. A run that ends up whitespace-only still generates no cell.
            if (child.TagName == "#text" && string.IsNullOrWhiteSpace(child.DisplayText))
            {
                var wsMode = child.GetWhiteSpace();
                if (wsMode is not (WhiteSpace.Pre or WhiteSpace.PreWrap) &&
                    lastWasProper && ProperTableChildFollows(i))
                    continue;
                run.Add(child);
                continue;
            }

            // §17.2.1 groups rendered content: a display:none box (a script between two bare
            // texts, a hidden element) renders nothing, so it neither joins a run of
            // misparented content nor splits one into separate anonymous cells. A
            // whitespace-only caption generates nothing for the same reason.
            if (child.GetDisplay() == DisplayType.None || child.TagName == "SCRIPT" ||
                IsCssTableCaption(child) && WhitespaceOnlyInterior(child))
            {
                deferred.Add(child);
                continue;
            }

            if (IsProperTableChild(child, atTableLevel: wrapAsRow))
            {
                FlushRun();
                newChildren.Add(child);
                lastWasProper = true;
            }
            else
            {
                run.Add(child);
                lastWasProper = false;
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

        // §10.3.7: when 'left', 'width' and 'right' are all given, an auto margin absorbs the
        // space left over — two of them split it equally, which is how an absolutely positioned
        // box is centred in its containing block. Auto margins used to compute to zero, so such a
        // box sat at its 'left' offset instead.
        var isRtl = node.GetDirection() == "rtl";
        if (!float.IsNaN(left) && !float.IsNaN(right))
        {
            var mlAuto = node.IsAutoMarginLeft();
            var mrAuto = node.IsAutoMarginRight();
            if (mlAuto || mrAuto)
            {
                // Whatever the containing block has left after the offsets, the box and any
                // margin that is NOT auto — that remainder is what the auto margins take.
                var slack = cbRect.Width - left - right - contentW
                            - border.Left - border.Right - padding.Left - padding.Right
                            - (mlAuto ? 0f : margin.Left) - (mrAuto ? 0f : margin.Right);
                if (mlAuto && mrAuto)
                {
                    // A negative remainder is pushed entirely onto the end side (§10.3.7 rule 4).
                    if (slack >= 0f) margin.Left = margin.Right = slack / 2f;
                    else if (isRtl) { margin.Right = 0f; margin.Left = slack; }
                    else { margin.Left = 0f; margin.Right = slack; }
                }
                else if (mlAuto) margin.Left = slack;
                else margin.Right = slack;
            }
        }

        // Resolve X — §10.3.7 rule 1: with 'left' and 'right' both auto the box takes its static
        // position, which under 'direction: rtl' is measured from the RIGHT edge — the hypothetical
        // static box sits at the end of the line, so an RTL containing block puts it flush right.
        float contentX;
        if (!float.IsNaN(left))
            contentX = cbRect.Left + left + margin.Left + border.Left + padding.Left;
        else if (!float.IsNaN(right))
            contentX = cbRect.Right - right - margin.Right - border.Right - padding.Right - contentW;
        else if (isRtl)
            contentX = cbRect.Right - margin.Right - border.Right - padding.Right - contentW;
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

        // Lay out children to get content height. The formatting context is the box's OWN display,
        // exactly as LayoutBlock dispatches it: an absolutely positioned table or flex container
        // used to fall through to plain block-children layout, which flowed its rows and cells as
        // inline content — an abs-pos <table> came out as one long wrapping line of cell text.
        float LayoutAbsChildren(float originY, float knownH) => node.GetDisplay() switch
        {
            DisplayType.Flex or DisplayType.InlineFlex =>
                FlexEngine.LayoutFlex(node, contentX, originY, contentW, knownH, viewportWidth, viewportHeight),
            DisplayType.Table or DisplayType.InlineTable =>
                TableEngine.LayoutTable(node, contentX, originY, contentW, viewportWidth, viewportHeight),
            _ => LayoutChildren(node.Children, contentX, originY, contentW, viewportWidth, viewportHeight, knownH),
        };

        var contentY0 = cbRect.Top; // temp origin for children layout
        var contentH = LayoutAbsChildren(contentY0, selfContentH);

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

        // §10.6.4: the vertical axis works the same way — with 'top', 'height' and 'bottom' all
        // given, auto margins share out what is left, centring the box vertically.
        if (!float.IsNaN(top) && !float.IsNaN(bottom))
        {
            var mtAuto = node.IsAutoMarginTop();
            var mbAuto = node.IsAutoMarginBottom();
            if (mtAuto || mbAuto)
            {
                var slack = cbRect.Height - top - bottom - contentH
                            - border.Top - border.Bottom - padding.Top - padding.Bottom
                            - (mtAuto ? 0f : margin.Top) - (mbAuto ? 0f : margin.Bottom);
                if (mtAuto && mbAuto)
                {
                    if (slack >= 0f) margin.Top = margin.Bottom = slack / 2f;
                    else { margin.Top = 0f; margin.Bottom = slack; }
                }
                else if (mtAuto) margin.Top = slack;
                else margin.Bottom = slack;
            }
        }

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
            LayoutAbsChildren(contentY, selfContentH);

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
    private static (float Height, MarginChain BottomMargin) LayoutBlock(
        LayoutNode node,
        float x, float y,
        float availableWidth,
        float viewportWidth, float viewportHeight,
        float parentContentHeight = 0,
        List<ActiveFloat>? bfcFloats = null)
        => LayoutBlockCore(node, x, y, availableWidth, viewportWidth, viewportHeight, parentContentHeight, bfcFloats);

    private static (float Height, MarginChain BottomMargin) LayoutBlockCore(
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
            return (0f, MarginChain.Empty);
        }

        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(availableWidth, viewportHeight, fontSize);
        // Auto horizontal margins are solved after the used width is known. Treating them as
        // lengths here can shrink an auto-width block to its font size before max-width applies.
        if (node.IsAutoMarginLeft()) margin.Left = 0f;
        if (node.IsAutoMarginRight()) margin.Right = 0f;
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
        // An auto-width table is shrink-wrapped. Resolve that width before auto margins so
        // a centered table can divide the remaining space between its two margins.
        if (!hasExplicitW && node.GetDisplay() == DisplayType.Table)
            clampedW = TableEngine.MeasureTableWidth(node, clampedW, viewportWidth, viewportHeight);
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
        // §10.3.3: with a known width and non-auto margins, the difference between the box's
        // outer sum and the containing-block width goes to the ignored margin — under the
        // CONTAINING BLOCK's 'direction: rtl' the left margin absorbs it, anchoring the box at
        // its right edge (the padding-right reftests pull an inner border over an outer one
        // with a negative right margin, which only lines up when this box anchors right).
        if (widthIsKnown && !node.IsAutoMarginLeft() && !node.IsAutoMarginRight() &&
            (node.Parent ?? node).GetDirection() == "rtl")
        {
            var fillTarget = availableWidth - margin.Left - margin.Right
                             - border.Left - border.Right - padding.Left - padding.Right;
            if (Math.Abs(contentW - fillTarget) > 0.5f)
            {
                contentX = x + availableWidth - margin.Right - border.Right - padding.Right
                             - contentW - padding.Left - border.Left;
            }
        }
        var contentY = y + margin.Top + border.Top + padding.Top;

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
        var trailingChain = MarginChain.Empty; // the trailing chain's (maxPositive, minNegative) form
        var chainHasCommittedContent = true; // whether the trailing chain contains committed boxes
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
            // The root's initial containing block has the viewport height even when the root's
            // own height is auto. Its percentage-height children (notably body) need that size.
            var childContainingH = node.TagName == "HTML" && !hasExplicitH ? viewportHeight : knownContentH;
            // The document root is a block-formatting-context boundary: a top margin on BODY
            // (or any first child) must push the content down the canvas, not collapse through
            // the root and escape above the viewport.
            var childBfc = establishesBfc || node.TagName == "HTML";
            contentH = LayoutChildrenImpl(node.Children, contentX, contentY, contentW, viewportWidth, viewportHeight,
                childContainingH, border.Top + padding.Top, childBfc, floatCtx, ownsFloatContext,
                out trailingMargin, out trailingChain, out chainHasCommittedContent);
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
        var effectiveBottomChain = MarginChain.Of(margin.Bottom);
        if (trailingMargin != 0f && padding.Bottom == 0f && border.Bottom == 0f
            && node.IsAutoHeight() && !establishesBfc)
            effectiveBottomChain += trailingChain;
        else if (chainHasCommittedContent)
            contentH += trailingMargin;
        var effectiveBottomMargin = effectiveBottomChain.Value;
        // else: the whole content self-collapsed into one chain (§8.3.1) — with a bottom
        // border/padding the chain can neither escape nor extend the height: a bordered box
        // with only empty children renders its borders alone (margin-collapse-128).

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
        node.CollapseShiftApplied = 0; // a fresh Box invalidates any prior self-collapse anchoring
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
        return (totalH, effectiveBottomChain);
    }

    /// <summary>Measure a nested flex item's content at its available width before final placement.</summary>
    internal static float MeasureBlockHeight(LayoutNode node, float availableWidth,
        float viewportWidth, float viewportHeight)
        => LayoutBlock(node, 0f, 0f, availableWidth, viewportWidth, viewportHeight).Height;

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
    private record struct ActiveFloat(float Left, float Top, float Right, float Bottom, FloatType Side, float CollapseShift, LayoutNode Node)
    {
        public ActiveFloat(float l, float t, float r, float b, FloatType s, float cs)
            : this(l, t, r, b, s, cs, null) { }
    }

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
    private static MarginChain GetEffectiveTopMarginChain(LayoutNode node, float cbWidth)
    {
        var fontSize = node.GetFontSize();
        var ownTop = MarginChain.Of(node.GetMarginTop(total: cbWidth, size: fontSize));

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
            // A child with 'clear' set never joins the chain (§8.3.1: the top margin collapses
            // with the first child only when "the child has no clearance"): clearance severs the
            // margins above the child, so its top margin stays inside this block. When clearance
            // ends up not being introduced the child still applies its own effective top margin
            // as the gap below (see LayoutChildrenImpl), so the rendered geometry is unchanged.
            if (child.TagName != "#text" && child.GetClear() != ClearType.None) return ownTop;
            // First in-flow block child: its effective top margin chain collapses with this
            // block's. The (maxPositive, minNegative) pair form survives negatives across three
            // and more levels, which the old scalar nesting lost (margin-collapse-162).
            return ownTop + GetEffectiveTopMarginChain(child, cbWidth);
        }
        return ownTop; // no in-flow children
    }

    /// <summary>
    /// The effective top margin of <paramref name="node"/>: its own top margin collapsed with the
    /// first in-flow child chain that collapses through (CSS 2.1 §8.3.1). Stops at a border/
    /// padding/BFC boundary, or when the first in-flow child is inline (non-empty text or
    /// inline-level box) — either of which contains the margin.
    /// <para>Percentage margins resolve against <paramref name="cbWidth"/> (the parent content
    /// width); descendant percentages reuse it as an approximation since the true per-level
    /// containing-block width isn't known before layout.</para>
    /// </summary>
    private static float GetEffectiveTopMargin(LayoutNode node, float cbWidth)
        => GetEffectiveTopMarginChain(node, cbWidth).Value;

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

    /// <summary>Shifts a laid-out subtree vertically so its boxes sit <paramref name="dy"/>
    /// below their laid-out origin: every box's content box, every line fragment, and every
    /// recorded static position. Idempotent per node — a node already shifted to
    /// <paramref name="dy"/> by a nested self-collapser's fold in the same chain is skipped
    /// (each node records what it received in <see cref="LayoutNode.CollapseShiftApplied"/>).</summary>
    private static void TranslateSubtree(LayoutNode node, float dy)
    {
        var additional = dy - node.CollapseShiftApplied;
        node.CollapseShiftApplied = dy;
        if (MathF.Abs(additional) > 0.01f)
        {
            var box = node.Box;
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top + additional,
                box.ContentBox.Right, box.ContentBox.Bottom + additional);
            node.Box = box;
            if (node.InlineFragments is { } fragments)
                for (var i = 0; i < fragments.Count; i++)
                    fragments[i] = (new SKRect(fragments[i].Rect.Left, fragments[i].Rect.Top + additional,
                        fragments[i].Rect.Right, fragments[i].Rect.Bottom + additional), fragments[i].Text);
            if (node.StaticY is { } sy) node.StaticY = sy + additional;
        }
        foreach (var child in node.Children) TranslateSubtree(child, dy);
    }

    /// <summary>Collapsing margins per CSS 2.1 §8.3.1: a chain of adjoining margins collapses
    /// to the sum of its largest positive and its most-negative member — a single running
    /// scalar loses the negative part (collapse(collapse(2,−4),14) = 12, the true chain = 10),
    /// so the chain is carried as (maxPositive, minNegative) while it accumulates.</summary>
    private readonly record struct MarginChain(float Pos, float Neg)
    {
        public static readonly MarginChain Empty = new(0f, 0f);
        public float Value => Pos + Neg;
        public static MarginChain Of(float margin) => margin >= 0f ? new(margin, 0f) : new(0f, margin);
        public static MarginChain operator +(MarginChain chain, float margin)
            => new(MathF.Max(chain.Pos, MathF.Max(margin, 0f)), MathF.Min(chain.Neg, MathF.Min(margin, 0f)));
        public static MarginChain operator +(MarginChain a, MarginChain b)
            => new(MathF.Max(a.Pos, b.Pos), MathF.Min(a.Neg, b.Neg));
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
                case InlineItemKind.OutOfFlow or InlineItemKind.LineBreak or InlineItemKind.InlineEdge:
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
        float openLineY = float.NaN, float openLineUsedW = 0f, float chainOffset = 0f)
    {
        var fontSize = child.GetFontSize();
        var margin = child.GetMargin(contentW, viewportHeight, fontSize);
        var padding = child.GetPadding(contentW, viewportHeight, fontSize);
        var border = child.GetBorderWidth();

        // Shrink-to-fit: use explicit width or half container as heuristic. 'width: 0' is a used
        // width of zero — only an AUTO width shrink-to-fits (floats-153's zero-width float with a
        // negative margin must not grow to its text).
        var hasExplicitW = !child.IsAutoWidth();
        var explicitW = hasExplicitW ? child.GetWidth(contentW) : 0f;
        // A floated replaced box (§9.7 makes it block-level) is sized from the image, not
        // shrink-to-fit over children it does not have.
        var replaced = TryResolveReplacedSize(child, contentW, parentContentHeight, viewportHeight);
        if (replaced.HasValue) { explicitW = replaced.Value.Width; hasExplicitW = true; }
        var maxAvail = contentW - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right;
        float childContentW;
        if (hasExplicitW)
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

        // The float's placement already sits at the running collapsed-margin chain's bottom
        // (placeY = runY + pendingMargin) — recorded so the self-collapsing re-anchoring shift
        // only moves the float by the chain's remaining growth (see the ActiveFloat return).
        child.CollapseShiftApplied = chainOffset;
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

        // The float's placement already sits at the running collapsed-margin chain's bottom
        // (placeY = runY + pendingMargin): record that offset so a self-collapsing ancestor's
        // re-anchoring shift only moves the float by the chain's remaining growth.
        return new ActiveFloat(outerLeft, outerTop, outerRight, outerBottom, side, chainOffset, child);
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
            new List<ActiveFloat>(), ownsFloatContext: true, out var trailing, out _, out _);
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
        out float trailingMargin,
        out MarginChain trailingChain,
        out bool chainHasCommittedContent)
    {
        // Running-margin model (CSS 2.1 §8.3.1): runY is the committed content bottom (the border
        // bottom of the last non-collapsing box) and pendingMargin is the collapsing margin still
        // accumulating below it. The previous single cursor is equivalent to runY + pendingMargin.
        var runY = contentY;
        var pending = MarginChain.Empty;
        var firstBlockSeen = false;
        var lastPlacedWasBlock = false;
        // §8.3.1: when a self-collapsing box with clearance collapses, its collapsed margin
        // chain does NOT collapse with the parent's bottom margin — the chain stays inside the
        // parent's height. True while the LAST adjoining chain contains such a box; a committed
        // box or inline run below it ends the chain (its own trailing margin escapes normally).
        var chainSeveredByClearance = false;
        // The part of the current chain that was already spent ABOVE the flow point: the first
        // self-collapser's effective top margin when it escaped with the parent's own top margin
        // (firstChildCollapse). A sibling following the re-anchored self-collapser joins the same
        // chain and lands at the chain bottom — runY + chain − this escape — rather than
        // re-spending the chain between the siblings (margin-collapse-019/162).
        var chainSpentAbove = false;
        var chainEscapedAbove = 0f;
        // Whether any box committed height or a line box laid out: when the ENTIRE content is
        // self-collapsing (§8.3.1), the one collapsed margin chain neither extends the parent's
        // height nor paints — a bordered box with only empty children renders its borders alone,
        // with no stripe of background where the collapsed margin would sit (margin-collapse-128).
        var hasCommittedContent = false;
        // The floats placed since the last committed box/line box belong to the current
        // self-collapse chain: those that follow real collapsed margins ride the chain's
        // bottom margin edge as later margins join it (margin-collapse-162).
        var chainFloatStart = 0;
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
                var staticY = runY + pending.Value - (chainSpentAbove ? chainEscapedAbove : 0f);
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
            // FLOATS with clear keep the cursor-adjust form (the float's margin box starts at the
            // cleared position). Block-level boxes are handled inside their branch below, where
            // the §9.5.2 clearance computation needs the hypothetical border edge.
            if (clear != ClearType.None && floatSide != FloatType.None)
            {
                var clearedForFloat = ApplyClear(clear, floats, runY + pending.Value);
                RetireFloats(floats, clearedForFloat);
                // Keep runY + pendingMargin == cleared so the running margin still collapses with the
                // next box exactly as before (clearance behaviour unchanged).
                runY = clearedForFloat - pending.Value;
            }

            // Handle floated elements — taken out of normal flow but affect available width
            if (floatSide != FloatType.None)
            {
                var af = LayoutFloat(child, floatSide, floats, contentX, runY + pending.Value, contentW,
                                     viewportWidth, viewportHeight, parentContentHeight,
                                     openLineY, openLineUsedW, pending.Value);
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
                // A cleared box's effective top margin never escapes its parent (the chain stops
                // at it, see GetEffectiveTopMargin), so it applies as the gap below even when the
                // box is its parent's first — the first-child escape shortcut would drop it.
                var gap = firstChildCollapse && clear == ClearType.None
                    ? 0f : (pending + childEffectiveTop).Value;
                firstBlockSeen = true;

                // A sibling following a re-anchored self-collapser joins the chain already spent
                // above it: its border edge is the chain bottom (runY + chain − escape), so the
                // escape part is subtracted from the fresh gap (margin-collapse-019: the 40px
                // chain collapsed with the parent's top margin must not open a second gap below).
                var borderTop = runY + gap - (chainSpentAbove ? chainEscapedAbove : 0f);
                // §9.5.2 clearance for block-level boxes: the hypothetical border edge is where
                // this box would sit with clear:none (runY + gap). If that is not past the
                // relevant floats, clearance is introduced: the border edge goes to the float
                // bottom, and the margins ABOVE are severed — the preceding collapsed margin and
                // this box's own top margin both apply as plain spacing inside the parent
                // (§8.3.1: "clearance inhibits margin collapsing and acts as spacing above the
                // margin-top"). The space runY..borderTop then holds pendingMargin +
                // childEffectiveTop + C, with C possibly negative (down to keeping the edge at
                // the hypothetical position).
                var clearanceSevered = false;
                if (clear != ClearType.None)
                {
                    var clearedBorder = ApplyClear(clear, floats, borderTop);
                    if (clearedBorder > borderTop)
                    {
                        borderTop = clearedBorder;
                        pending = MarginChain.Empty;
                        clearanceSevered = true;
                        chainSeveredByClearance = true;
                        chainSpentAbove = false; // clearance severs the margins above the box
                        chainEscapedAbove = 0f;
                        // The chain below (this box's own collapsed-through margins plus
                        // following self-collapsing siblings) is adjoining from the box's top
                        // margin edge — borderTop − childEffectiveTop — not from runY: the
                        // following siblings' top margins collapse with the chain from there.
                        runY = borderTop - childEffectiveTop;
                    }
                    RetireFloats(floats, borderTop);
                }

                // §9.5: a plain in-flow block's border box OVERLAPS the floats beside it — only
                // its inline (line box) content narrows, and a block that establishes a BFC may
                // not overlap at all. Placing every block in the float's wake painted block
                // backgrounds beside the float instead of under it (floats-008/135: the red
                // block must slide UNDER the green/blue float, not out of its way).
                var (effX, effW) = EstablishesBlockFormattingContext(child)
                    ? AvailableBand(floats, borderTop, 1, contentX, contentW)
                    : (contentX, contentW);
                if (effW != contentW || effX != contentX)
                {
                    // §9.5 rule 6: "implementations should clear the said element by placing it
                    // below any preceding floats" when it cannot fit beside them — the border box
                    // must not overlap, and a BFC's specified width is not narrowed by the band
                    // (floats-bfc-001's 200px BFC drops below the 150px float and grows its
                    // auto-height parent to full coverage).
                    var wantedW = child.IsAutoWidth()
                        ? effW
                        : Math.Max(0f, child.GetWidth(contentW)
                                       + child.GetBorderWidth().Left + child.GetBorderWidth().Right
                                       + (child.GetPadding(contentW, viewportHeight, childFontSize).Left
                                          + child.GetPadding(contentW, viewportHeight, childFontSize).Right));
                    if (wantedW > effW + 0.5f)
                    {
                        var floatBottom = borderTop;
                        foreach (var f in floats) floatBottom = Math.Max(floatBottom, f.Bottom);
                        if (floatBottom > borderTop + 0.5f)
                        {
                            borderTop = floatBottom;
                            (effX, effW) = AvailableBand(floats, borderTop, 1, contentX, contentW);
                            RetireFloats(floats, borderTop);
                        }
                    }
                }
                // The pre-layout state of the spent-chain flag: the ride gate below must know
                // whether the CURRENT chain was opened by a re-anchored self-collapser (the box
                // about to fold would set the flag itself before the ride runs).
                var chainWasSpentAbove = chainSpentAbove;
                var (h, childBottomMargin) = LayoutBlock(child, effX, borderTop - childMarginTop, effW,
                                                         viewportWidth, viewportHeight, parentContentHeight, floats);
                var borderBoxH = h - childMarginTop - childBottomMargin.Value;

                // Self-collapsing block (§8.3.1): no in-flow content / border / padding / height (a
                // zero border-box height already implies height is auto-or-0 with no min-height), and
                // not a BFC → its top and bottom margins are adjoining, fold together into the running
                // margin, and the box contributes no height (so neighbouring margins collapse through).
                if (borderBoxH <= 0.01f && !EstablishesBlockFormattingContext(child))
                {
                    var own = MarginChain.Empty + childEffectiveTop + childBottomMargin;
                    pending = firstChildCollapse && !clearanceSevered ? own : pending + own;
                    // §8.3.1: a self-collapsing box's border box sits at the BOTTOM of its
                    // collapsed margin chain — the chain is spent above the box, so its
                    // containing block for absolutely-positioned descendants (and any paint
                    // origin) lands where the following content will (margin-collapse-137:
                    // the abs child's top:0 must coincide with the following sibling the chain
                    // flows into). The first-child escape part of the chain went ABOVE the
                    // parent instead, so it is subtracted to keep that placement unchanged.
                    // A box carrying its own text contains a line box and is NOT self-
                    // collapsing (its text paints from its own position, margin-collapse-130).
                    if (!clearanceSevered && string.IsNullOrEmpty(child.DisplayText))
                    {
                        // The escaped part of the chain is the first self-collapser's effective
                        // top margin when it collapsed with the parent's own top margin — that
                        // part already sits above the parent, spent by the parent's placement.
                        if (firstChildCollapse) chainEscapedAbove = childEffectiveTop;
                        chainSpentAbove = true;
                        var collapsedBorderTop = runY + pending.Value - chainEscapedAbove;
                        var dy = collapsedBorderTop - borderTop;
                        if (MathF.Abs(dy) > 0.01f)
                        {
                            // The whole subtree laid out at the pre-shift origin (floats place
                            // during the flow, abs static positions were recorded there), so
                            // every descendant box, fragment and static position shifts with
                            // the box — as do the floats the subtree escaped into the BFC list.
                            // Nested self-collapsers in the same chain already shifted their own
                            // subtrees by their own fold; the per-node applied marker keeps the
                            // re-shift a no-op for them (the chain collapses to ONE margin).
                            TranslateSubtree(child, dy);
                        }
                        // Chain floats that follow real collapsed margins ride the chain's
                        // bottom margin edge: later siblings' top margins join the same chain
                        // (a float out of flow does not interrupt the adjoining), and the
                        // float's static position tracks the chain's bottom (margin-collapse-
                        // 162's float lands on the following block). A float placed at a chain's
                        // START (no margins above it, placement offset zero) stays there — the
                        // following margins form a chain below it (margin-collapse-123).
                        var chainBottomOffset = pending.Value - chainEscapedAbove;
                        for (var fi = chainFloatStart; fi < floats.Count; fi++)
                        {
                            var f = floats[fi];
                            // Only a SPENT chain (a re-anchored self-collapser opened it — checked
                            // BEFORE this box's own fold set the flag) moves its floats: the
                            // float's own placement point was mid-chain, so later joins drag it
                            // down to the chain's bottom (margin-collapse-162). A plain internal
                            // chain opened by a committed box leaves the float at its placement
                            // flow point — later siblings' margins form a chain BELOW it
                            // (margin-collapse-163's float stays put; margin-collapse-123).
                            if (!chainWasSpentAbove || f.CollapseShift <= 0f) continue;
                            var additional = chainBottomOffset - f.CollapseShift;
                            if (MathF.Abs(additional) > 0.01f)
                            {
                                f.Top += additional;
                                f.Bottom += additional;
                                f.CollapseShift = chainBottomOffset;
                                if (f.Node is { } floatNode && floatNode.CollapseShiftApplied != dy)
                                {
                                    var fb = floatNode.Box;
                                    fb.ContentBox = new SKRect(fb.ContentBox.Left, fb.ContentBox.Top + additional,
                                        fb.ContentBox.Right, fb.ContentBox.Bottom + additional);
                                    floatNode.Box = fb;
                                }
                                if (f.Node is { } fn) fn.CollapseShiftApplied = chainBottomOffset;
                                floats[fi] = f;
                            }
                        }
                    }
                }
                else
                {
                    // Commit: runY advances to the child's border-box bottom; its (effective, after
                    // its own last-child collapse-through) bottom margin becomes the running margin.
                    runY = borderTop + borderBoxH;
                    pending = childBottomMargin;
                    chainSeveredByClearance = false; // a committed box ends the severed chain
                    chainSpentAbove = false; // its placement consumed the spent chain
                    chainEscapedAbove = 0f;
                    hasCommittedContent = true;
                    chainFloatStart = floats.Count; // a new chain starts after committed content
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
                // Skip runs with no real inline content — whitespace-only #TEXT around
                // out-of-flow boxes. Neither creates a line box (out-of-flow boxes are removed
                // from the line, whitespace-only text builds none), so the pending margin keeps
                // collapsing across them (§8.3.1; margin-collapse-162's absolute .a must not
                // spend the chain). The out-of-flow members take their static position at the
                // flow point, exactly as the direct-child abs branch records it.
                var runHasInlineContent = run.Any(n => n.TagName == "#text"
                    ? n.DisplayText.Trim().Length > 0
                    : n.GetPosition() is not (PositionType.Absolute or PositionType.Fixed));
                if (!runHasInlineContent)
                {
                    var flowPointY = runY + pending.Value - (chainSpentAbove ? chainEscapedAbove : 0f);
                    foreach (var oof in run.Where(n => n.TagName != "#text"))
                    {
                        var (staticBandX, _) = AvailableBand(floats, flowPointY, 1, contentX, contentW);
                        oof.StaticX = staticBandX;
                        oof.StaticY = flowPointY;
                    }
                    continue;
                }

                // Inline content commits the pending block margin (margins don't collapse across a
                // line box) and lays out at the committed position — the chain bottom when a
                // re-anchored self-collapser already spent the chain's escaped part above.
                runY += pending.Value - (chainSpentAbove ? chainEscapedAbove : 0f);
                pending = MarginChain.Empty;
                chainSeveredByClearance = false; // a line box ends the severed chain
                chainSpentAbove = false;
                chainEscapedAbove = 0f;
                hasCommittedContent = true;
                chainFloatStart = floats.Count; // a new chain starts after committed content
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
        // in-flow block child's bottom margin can collapse through only if no float extends past
        // it, and (§8.3.1) not at all when the chain was severed by clearance: the resulting
        // collapsed margin does not collapse with the parent block's bottom margin — it stays
        // inside the parent's height (runY was set to the chain's origin when the severed box
        // folded, so runY + pendingMargin already spans the chain's full extent).
        var contentBottom = runY + pending.Value;
        var flowBottom = contentBottom;
        if (ownsFloatContext)
            foreach (var f in floats)
                contentBottom = Math.Max(contentBottom, f.Bottom);

        trailingChain = (lastPlacedWasBlock && contentBottom <= flowBottom && !chainSeveredByClearance)
            ? pending : MarginChain.Empty;
        trailingMargin = trailingChain.Value;
        chainHasCommittedContent = hasCommittedContent;
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
        // Whether anything on the current line aligns against the shared baseline. Only then does
        // maxBelow hold a real measurement rather than its 0 starting value.
        var hasBaselineItem = false;
        var lineEndsWithBreak = false;

        // §10.8.1's STRUT: every line box begins with a zero-width inline box carrying the block
        // container's own font and line-height, so a line holding nothing but a 96px image is
        // still at least as tall as the text that would have sat on its baseline. Measured once —
        // the strut's metrics come from the block, not from whatever lands on the line.
        var strutAbove = 0f;
        var strutBelow = 0f;
        if (container is not null)
        {
            using var strutFont = TextMeasure.CreateFont(container);
            var strutLh = container.GetLineHeight(container.GetFontSize());
            // With 'line-height: normal' the strut is exactly the font's own box — no leading to
            // split. Our 'normal' is a flat 1.4em, which is generous enough that a strut using it
            // would poke out above a 16px image on an otherwise-empty line and nudge it down.
            if (container.IsNormalLineHeight())
                strutLh = -strutFont.Metrics.Ascent + strutFont.Metrics.Descent;
            strutAbove = TextMeasure.ComputeAscent(strutFont, strutLh);
            strutBelow = strutLh - strutAbove;
        }

        var placed = new List<(InlineItem item, float relX, float relY)>();
        var lineStart = 0;

        // Locals rather than the out parameters directly: C# forbids touching an 'out' parameter
        // from inside a local function.
        var committedLineTop = 0f;
        var committedLineWidth = 0f;

        // §9.4.2's test for a line box that "exists": any in-flow content at all — text that is
        // not purely collapsible whitespace, a replaced box, an inline-block/flex/table, or a
        // forced break.
        bool LineHasContent()
        {
            if (lineEndsWithBreak) return true;
            for (var k = lineStart; k < placed.Count; k++)
            {
                var it = placed[k].item;
                if (it.Kind == InlineItemKind.OutOfFlow) continue;
                if (it.Kind == InlineItemKind.Text && string.IsNullOrWhiteSpace(it.Text)) continue;
                return true;
            }
            return false;
        }

        void CommitLine()
        {
            // Resolve the final line-box height: start from the baseline-aligned items' reach,
            // then grow outward (below for 'top', above for 'bottom') if those need more room.
            var above = maxAbove;
            var below = maxBelow;

            // The strut only counts on a line that actually has content. §9.4.2: a line box with
            // no text, no in-flow content and no inline box with non-zero edges is a ZERO-height
            // line box, so seeding the strut unconditionally would give every stray whitespace
            // node a line of its own.
            if (LineHasContent())
            {
                above = Math.Max(above, strutAbove);
                // The strut's own reach below the baseline can be NEGATIVE (a line-height smaller
                // than the font's ascent+descent gives negative half-leading), so it only counts
                // when nothing else has claimed room down there — clamping it to 0 would stretch a
                // `line-height: 10px` line back out to the full baseline.
                below = hasBaselineItem ? Math.Max(below, strutBelow) : strutBelow;
            }
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
            hasBaselineItem = false;
            lineEndsWithBreak = false;
            // The next line sits lower, so it may clear a float the previous line was beside.
            var (nx, nw) = AvailableBand(floats, originY + lineY, 1, originX, maxWidth);
            bandLeft = nx - originX;
            bandRight = bandLeft + nw;
            lineX = bandLeft;
            lineStart = placed.Count;
        }

        // CSS 2.1 16.6 / UAX #14: a line may only break at a break opportunity. Each placed entry
        // remembers the item it came from so the line can be cut back to its last opportunity.
        var placedSource = new List<int>();
        var sourceIndex = 0;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            while (placedSource.Count < placed.Count) placedSource.Add(sourceIndex);
            sourceIndex = itemIndex;
            if (item.Kind == InlineItemKind.LineBreak)
            {
                // An otherwise-empty line still needs the <br>'s own font metrics for its height.
                if (maxAbove + maxBelow + maxTopH + maxBottomH <= 0f)
                {
                    maxAbove = item.Ascent;
                    maxBelow = item.Height - item.Ascent;
                    hasBaselineItem = true;
                }
                // §9.4.2 exempts a line that ends with a forced break from the zero-height rule,
                // so an empty <br> line still gets the strut.
                lineEndsWithBreak = true;
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

            // A whitespace-only text item that would cross the line's end collapses there
            // (§16.6.1): it must neither wrap onto a fabricated line box nor commit one — the
            // green overlay in the white-space band grew a ~45px phantom line from exactly this.
            // The space is itself the break opportunity, so the line ends at it: what follows
            // starts the next line instead of looking glued to the word before the space.
            if (item.Kind == InlineItemKind.Text && item.Text != null && item.Text.Trim().Length == 0 &&
                lineX + item.Width > bandRight && WrapsLines(item.Node))
            {
                if (lineX > bandLeft) CommitLine();
                continue;
            }

            if (lineX > bandLeft && lineX + item.Width > bandRight)
            {
                if (BreakAllowedBefore(placed, lineStart, item))
                    CommitLine();
                else if (SplitAtFirstOpportunity(items, itemIndex) )
                {
                    // The glued head stays with the content before it; the tail, which now
                    // starts after a break opportunity, is placed on its own turn.
                    itemIndex--;
                    continue;
                }
                else if (LastOpportunity(placed, lineStart) is { } cut && cut > lineStart)
                {
                    // No opportunity here: the unbreakable run moves down whole, from the line's
                    // last opportunity (a word split across inline boxes is still one word).
                    var resume = placedSource[cut];
                    placed.RemoveRange(cut, placed.Count - cut);
                    placedSource.RemoveRange(cut, placedSource.Count - cut);
                    (maxAbove, maxBelow, maxTopH, maxBottomH, hasBaselineItem) = LineMetrics(placed, lineStart);
                    CommitLine();
                    itemIndex = resume - 1;
                    continue;
                }
                // Otherwise nothing on this line can break: the content overflows it.
            }

            // Skip whitespace-only text at the start of a line — a COLLAPSING one (§16.6.1):
            // under white-space:pre/pre-wrap a preserved space at a line's start renders, and
            // a cell whose content is exactly that space ("a <cellws> </cellws> d") lost it.
            if (lineX <= bandLeft && item.Kind == InlineItemKind.Text &&
                item.Text != null && item.Text.Trim().Length == 0)
            {
                var wsMode = item.Node?.GetWhiteSpace() ?? WhiteSpace.Normal;
                if (wsMode is not (WhiteSpace.Pre or WhiteSpace.PreWrap)) continue;
            }

            // §16.6.1: collapsible spaces at the START of a line are removed — including the
            // spaces LEADING a text node that also carries words (" a " opens at the 'a'): the
            // parser collapses each source text node to " x ", and a bare-text node landing at
            // a line boundary painted one space too far right (white-space-12 reftests).
            // Preserved spaces (pre/pre-wrap) are content and stay.
            if (lineX <= bandLeft && item.Kind == InlineItemKind.Text && item.Text is { } leading &&
                leading.Length > 0 && char.IsWhiteSpace(leading[0]))
            {
                var wsMode = item.Node?.GetWhiteSpace() ?? WhiteSpace.Normal;
                if (wsMode is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
                {
                    var stripped = leading.TrimStart();
                    if (stripped.Length == 0) continue;
                    using (var stripFont = TextMeasure.CreateFont(item.Node))
                        item = item with { Text = stripped, Width = stripFont.MeasureText(stripped),
                                       ContentW = stripFont.MeasureText(stripped) };
                }
            }

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

            // §10.8: the vertical edges of a non-replaced inline box do not enter the line box's
            // height calculation, so an edge spacer only advances the pen.
            if (effectiveItem.Kind == InlineItemKind.InlineEdge)
            {
                placed.Add((effectiveItem, lineX, lineY));
                lineX += effectiveItem.Width;
                continue;
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
                    hasBaselineItem = true;
                    break;
            }

            placed.Add((effectiveItem, lineX, lineY));
            lineX += effectiveItem.Width;
        }
        if (placed.Count > lineStart) CommitLine();

        // A node can end up with several fragments (a forced break inside it, or a run that wrapped);
        // start each layout pass from a clean list so a re-layout does not accumulate stale ones.
        foreach (var (item, _, _) in placed) item.Node.InlineFragments = null;

        foreach (var (item, relX, relY) in placed)
        {
            ApplyInlineItem(item, originX + relX, originY + relY, viewportWidth, viewportHeight);
        }

        lastLineTop = committedLineTop;
        lastLineWidth = committedLineWidth;
        return lineY;
    }

    /// <summary>
    /// For an item aligned baseline/sub/super/middle/length/percentage, the distance from ITS
    /// OWN top down to wherever it aligns against the line's shared baseline (CSS 2.1 §10.8.1).
    /// 'baseline' uses the item's intrinsic ascent directly; 'sub'/'super' shift it down/up by
    /// ~0.15em; 'middle' aligns the item's vertical centre with the baseline plus half an
    /// x-height (approximated as a quarter of the font size); a &lt;length&gt;/&lt;percentage&gt;
    /// raises (positive) or lowers (negative) the box's baseline by that distance.
    /// </summary>
    private static float AboveBaselineComponent(InlineItem item, VerticalAlignType vAlign)
    {
        var fontSize = item.Node.GetFontSize();
        var above = vAlign switch
        {
            VerticalAlignType.Middle => item.Height / 2f + fontSize * 0.25f,
            VerticalAlignType.Sub => item.Ascent - fontSize * 0.15f,
            VerticalAlignType.Super => item.Ascent + fontSize * 0.15f,
            _ => item.Ascent, // baseline
        };
        if (vAlign is VerticalAlignType.Length or VerticalAlignType.Percentage)
            above += item.Node.GetVerticalAlignOffset();
        return above;
    }

    /// <summary>White-space modes that allow soft wrapping (CSS 2.1 16.6): 'nowrap' and 'pre' never do.</summary>
    private static bool WrapsLines(LayoutNode node) =>
        node.GetWhiteSpace() is WhiteSpace.Normal or WhiteSpace.PreWrap or WhiteSpace.PreLine;

    /// <summary>A collapsible or preserved space that offers a break: NOT a no-break space.</summary>
    private static bool IsBreakSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsAtomicInline(InlineItem item) =>
        item.Kind is InlineItemKind.Image or InlineItemKind.InlineBlock or InlineItemKind.InlineFlex or InlineItemKind.InlineTable;

    private static bool IsAncestorOrSelf(LayoutNode ancestor, LayoutNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// Whether a line may break between the content already on the line (from
    /// <paramref name="lineStart"/>) and <paramref name="next"/>. A space at the boundary under a
    /// wrapping white-space mode is an opportunity, so is an atomic inline beside it (treated as
    /// an ideograph, CSS Text) and a &lt;wbr&gt;; text meeting text with no space is not: the
    /// boundary of an inline box is no break opportunity on its own.
    /// </summary>
    private static bool BreakAllowedBefore(List<(InlineItem item, float relX, float relY)> placed, int lineStart, InlineItem next)
    {
        InlineItem? prev = null;
        for (var k = placed.Count - 1; k >= lineStart; k--)
        {
            var it = placed[k].item;
            if (it.Kind == InlineItemKind.InlineEdge)
            {
                if (it.Node.TagName == "WBR") return true;
                continue;
            }
            if (it.Kind == InlineItemKind.OutOfFlow) continue;
            prev = it;
            break;
        }
        if (prev is null) return true;
        return BreakBetween(prev, next);
    }

    private static bool BreakBetween(InlineItem prev, InlineItem next)
    {
        if (next.Kind == InlineItemKind.Text && next.Text is { Length: > 0 } nt && IsBreakSpace(nt[0]))
            return WrapsLines(next.Node);
        if (prev.Kind == InlineItemKind.Text && prev.Text is { Length: > 0 } pt && IsBreakSpace(pt[^1]))
            return WrapsLines(prev.Node);
        if (IsAtomicInline(next)) return WrapsLines(next.Node.Parent ?? next.Node) && (prev.Kind != InlineItemKind.Text || WrapsLines(prev.Node));
        if (IsAtomicInline(prev)) return WrapsLines(prev.Node.Parent ?? prev.Node) && (next.Kind != InlineItemKind.Text || WrapsLines(next.Node));
        return false;
    }

    /// <summary>
    /// The placed index at which the current line can last be cut: just after the content item
    /// preceding the latest break opportunity, keeping the closing edges of the inline boxes that
    /// content belongs to (a span's end padding stays with its last word). Null when the line has
    /// no opportunity after its first item.
    /// </summary>
    private static int? LastOpportunity(List<(InlineItem item, float relX, float relY)> placed, int lineStart)
    {
        for (var k = placed.Count - 1; k > lineStart; k--)
        {
            var it = placed[k].item;
            if (it.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow) continue;
            // Find the content before entry k and decide whether the boundary breaks.
            var j = k - 1;
            var wbr = false;
            while (j >= lineStart && placed[j].item.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow)
            {
                if (placed[j].item.Node.TagName == "WBR") wbr = true;
                j--;
            }
            if (j < lineStart) continue;
            if (!wbr && !BreakBetween(placed[j].item, it)) continue;
            var cut = j + 1;
            while (cut < k && placed[cut].item.Kind == InlineItemKind.InlineEdge &&
                   IsAncestorOrSelf(placed[cut].item.Node, placed[j].item.Node))
                cut++;
            return cut;
        }
        return null;
    }

    /// <summary>
    /// Splits text item <paramref name="index"/> after its first run of break spaces when its
    /// white-space wraps, so the part glued to the preceding content stays on the line while the
    /// rest starts after a real break opportunity. False when there is nothing to split off.
    /// </summary>
    private static bool SplitAtFirstOpportunity(List<InlineItem> items, int index)
    {
        var item = items[index];
        if (item.Kind != InlineItemKind.Text || item.Text is not { Length: > 1 } text || !WrapsLines(item.Node))
            return false;
        var space = 0;
        while (space < text.Length && !IsBreakSpace(text[space])) space++;
        if (space == 0 || space >= text.Length) return false;
        var end = space;
        while (end < text.Length && IsBreakSpace(text[end])) end++;
        if (end >= text.Length) return false;

        using var font = TextMeasure.CreateFont(item.Node);
        var lh = item.Node.GetLineHeight(item.Node.GetFontSize());
        var (ls, wsp) = TextMeasure.SpacingOf(item.Node);
        var head = text[..end];
        var tail = text[end..];
        var (hw, hh, ha) = TextMeasure.MeasureSingleLine(head, font, lh, ls, wsp);
        var (tw, th, ta) = TextMeasure.MeasureSingleLine(tail, font, lh, ls, wsp);
        items[index] = item with { Text = head, Width = hw, Height = hh, ContentW = hw, ContentH = hh, Ascent = ha };
        items.Insert(index + 1, item with { Text = tail, Width = tw, Height = th, ContentW = tw, ContentH = th, Ascent = ta });
        return true;
    }

    /// <summary>Recomputes the line's baseline accumulators from what is placed on it — needed
    /// after the line is cut back to an earlier break opportunity.</summary>
    private static (float Above, float Below, float TopH, float BottomH, bool HasBaseline) LineMetrics(
        List<(InlineItem item, float relX, float relY)> placed, int lineStart)
    {
        float above = 0f, below = 0f, topH = 0f, bottomH = 0f;
        var hasBaseline = false;
        for (var k = lineStart; k < placed.Count; k++)
        {
            var it = placed[k].item;
            if (it.Kind is InlineItemKind.InlineEdge or InlineItemKind.OutOfFlow) continue;
            var va = it.Node.GetVerticalAlign();
            switch (va)
            {
                case VerticalAlignType.Top or VerticalAlignType.TextTop:
                    topH = Math.Max(topH, it.Height);
                    break;
                case VerticalAlignType.Bottom or VerticalAlignType.TextBottom:
                    bottomH = Math.Max(bottomH, it.Height);
                    break;
                default:
                    above = Math.Max(above, AboveBaselineComponent(it, va));
                    below = Math.Max(below, it.Height - AboveBaselineComponent(it, va));
                    hasBaseline = true;
                    break;
            }
        }
        return (above, below, topH, bottomH, hasBaseline);
    }

    private static InlineItem EdgeItem(LayoutNode node, float width) =>
        new(InlineItemKind.InlineEdge, node, null, width, 0, default, default, default, width, 0, 0);

    /// <summary>
    /// CSS 2.1 §8.4: the HORIZONTAL margins and padding of a non-replaced inline box are laid out
    /// — they push whatever follows along the line — while its vertical ones do not affect the
    /// line box's height. Returns the widths to reserve before and after the box's own content.
    /// </summary>
    private static (float Leading, float Trailing) InlineBoxEdges(
        LayoutNode node, float maxWidth, float viewportHeight)
    {
        // Anonymous boxes (#text and friends) resolve their styles from the element they came
        // from, so asking them for a margin would apply the parent's a second time.
        if (node.TagName.StartsWith('#')) return (0f, 0f);

        if (node.IsBrokenAroundBlock()) return (0f, 0f);

        // §9.4.2's bidi box model puts the box's edges on its START and END fragments, which under
        // 'direction: rtl' are its right and left sides. Lines here are always laid out
        // left-to-right, so there is nowhere correct to put them; reserving the LTR pair would
        // move the content the wrong way rather than leave it where it already is.
        if (node.GetDirection() == "rtl") return (0f, 0f);

        var fontSize = node.GetFontSize();
        var margin = node.GetMargin(maxWidth, viewportHeight, fontSize);
        var padding = node.GetPadding(maxWidth, viewportHeight, fontSize);
        // 'auto' is 0 on an inline box (§10.3.1), not the block-level centring rule.
        var left = (node.IsAutoMarginLeft() ? 0f : margin.Left) + padding.Left;
        var right = (node.IsAutoMarginRight() ? 0f : margin.Right) + padding.Right;
        return (left, right);
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
                // 'normal' line-height is the font's natural box, consistent with GetLineHeight.
                var brH = -brFont.Metrics.Ascent + brFont.Metrics.Descent;
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
                var explicitW = node.IsAutoWidth() ? 0f : node.GetWidth(maxWidth);
                var explicitH = node.GetHeight(viewportHeight);

                // Form controls are replaced elements even when an author styles them
                // as inline-flex. They have no flex children, so measuring only their
                // children collapses their label to zero width.
                var intrinsicW = FormLayout.IntrinsicWidth(node);
                var w = explicitW > 0
                    ? explicitW
                    : intrinsicW ?? FlexEngine.MeasureMaxContentMain(node, 0, 0, viewportWidth, viewportHeight);
                var horizontalEdges = padding.Left + padding.Right + border.Left + border.Right;
                var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
                if (isBorderBox && explicitW > 0) w = Math.Max(0, w - horizontalEdges);
                var minW = node.GetMinWidth(maxWidth, fontSize);
                var maxW = node.GetMaxWidth(maxWidth, fontSize);
                if (isBorderBox)
                {
                    minW = Math.Max(0, minW - horizontalEdges);
                    if (!float.IsPositiveInfinity(maxW)) maxW = Math.Max(0, maxW - horizontalEdges);
                }
                w = Math.Max(minW, Math.Min(maxW, w));

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
                var explicitW = node.IsAutoWidth() ? 0f : node.GetWidth(maxWidth);
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
                else if (node.TagName == "INPUT" && inputType is "submit" or "reset" or "button")
                {
                    defaultW = FormLayout.IntrinsicWidth(node) ?? FormLayout.TextInputWidth;
                    defaultH = FormLayout.TextInputHeight;
                }
                else if (node.TagName == "TEXTAREA") { defaultW = FormLayout.TextareaWidth; defaultH = FormLayout.TextareaHeight; }
                else if (node.TagName == "SELECT") { defaultW = FormLayout.SelectWidth; defaultH = FormLayout.SelectHeight; }
                else if (node.TagName == "PROGRESS") { defaultW = FormLayout.ProgressWidth; defaultH = FormLayout.ProgressHeight; }
                else if (node.TagName == "METER") { defaultW = FormLayout.MeterWidth; defaultH = FormLayout.MeterHeight; }
                else
                {
                    defaultW = IntrinsicSizer.ShrinkToFit(node, Math.Max(maxWidth, 0f), viewportHeight);
                    defaultH = FormLayout.TextInputHeight;
                    isFormControl = false;
                }

                var w = explicitW > 0 ? explicitW : node.TagName == "INPUT"
                    ? FormLayout.IntrinsicWidth(node) ?? defaultW : defaultW;
                w = Math.Max(node.GetMinWidth(maxWidth, fontSize),
                    Math.Min(w, node.GetMaxWidth(maxWidth, fontSize)));
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

                // A content-bearing inline-block's height is its laid-out content height at its
                // used width (§10.6.6) — the form-control fallback above is for controls. The
                // pre-measure mirrors the flex-item path; the placement case re-lays the subtree
                // at its resolved position, so boxes written here are provisional. The inner
                // layout is what makes an inline-block child's vertical margins count toward the
                // line box: its own line encloses the child's margin box (§10.8.1,
                // margin-collapse-015).
                if (!isFormControl && explicitH <= 0 && node.Children.Count > 0)
                {
                    var marginBoxH = MeasureBlockHeight(node, Math.Max(w, 0f), viewportWidth, viewportHeight);
                    h = Math.Max(0f, marginBoxH - margin.Top - margin.Bottom
                                     - border.Top - border.Bottom - padding.Top - padding.Bottom);
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
                // §10.3.2 / §10.6.2: a CSS 'width'/'height' on a replaced element overrides its
                // intrinsic size, and one specified dimension derives the other from the intrinsic
                // ratio. The inline path used to read the intrinsic size alone, so `width: 100%`
                // (or the HTML width="100%" hint) left the image at its natural pixel width.
                var sized = TryResolveReplacedSize(node, maxWidth, viewportHeight, viewportHeight);
                var w = sized is { Width: > 0 } ? sized.Value.Width
                        : node.IntrinsicWidth > 0 ? (float)node.IntrinsicWidth : node.Image?.Width ?? 100f;
                var h = sized is { Height: > 0 } ? sized.Value.Height
                        : node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 100f;
                // §8.4: an inline REPLACED box's margins, borders and padding all take part in
                // layout horizontally *and* vertically — unlike a non-replaced inline box, whose
                // vertical edges do not affect line height. Reserving only the content size here
                // meant `img { padding-right: 20px }` advanced the line by nothing at all.
                var imgFontSize = node.GetFontSize();
                var imgMargin = node.GetMargin(maxWidth, viewportHeight, imgFontSize);
                // §10.3.2 / §10.6.2: on an INLINE replaced box every 'auto' margin has a used
                // value of 0. GetMargin's 'auto' means "centre in the containing block", which is
                // the block-level rule — applying it here pushed the image into mid-air.
                if (node.IsAutoMarginTop()) imgMargin.Top = 0f;
                if (node.IsAutoMarginBottom()) imgMargin.Bottom = 0f;
                if (node.IsAutoMarginLeft()) imgMargin.Left = 0f;
                if (node.IsAutoMarginRight()) imgMargin.Right = 0f;
                var imgPadding = node.GetPadding(maxWidth, viewportHeight, imgFontSize);
                var imgBorder = node.GetBorderWidth();
                var imgW = imgMargin.Left + imgBorder.Left + imgPadding.Left + w
                         + imgPadding.Right + imgBorder.Right + imgMargin.Right;
                var imgH = imgMargin.Top + imgBorder.Top + imgPadding.Top + h
                         + imgPadding.Bottom + imgBorder.Bottom + imgMargin.Bottom;
                // Replaced elements have no baseline of their own — CSS 2.1 §10.8's fallback rule
                // aligns their bottom margin edge with the line's baseline (Ascent = full height).
                items.Add(new InlineItem(InlineItemKind.Image, node, null, imgW, imgH,
                           imgMargin, imgPadding, imgBorder, w, h, imgH));
            }
            else if (node.TagName == "WBR")
            {
                // HTML: <wbr> represents a line break opportunity and nothing else.
                items.Add(EdgeItem(node, 0f));
            }
            else if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                var lh = node.GetLineHeight(node.GetFontSize());
                // 'letter-spacing' / 'word-spacing' widen the text the painter draws, so they have
                // to widen the box layout reserves for it too — measuring without them put every
                // following inline box in the wrong place.
                var (ls, wsp) = TextMeasure.SpacingOf(node);

                // §16.6: under pre / pre-wrap / pre-line a newline is a FORCED line break. An
                // inline run measured the whole string as one unbroken item, so a preformatted
                // <span> holding two lines drew them on top of each other.
                var ws = node.GetWhiteSpace();
                var segments = ws is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine
                               && node.DisplayText.Contains('\n')
                    ? node.DisplayText.Split('\n')
                    : [node.DisplayText];

                var (leading, trailing) = InlineBoxEdges(node, maxWidth, viewportHeight);
                if (leading != 0f) items.Add(EdgeItem(node, leading));
                for (var si = 0; si < segments.Length; si++)
                {
                    if (si > 0)
                        items.Add(new InlineItem(InlineItemKind.LineBreak, node, null, 0, lh,
                                   default, default, default, 0, lh, TextMeasure.ComputeAscent(font, lh)));
                    var (w, h, ascent) = TextMeasure.MeasureSingleLine(segments[si], font, lh, ls, wsp);
                    items.Add(new InlineItem(InlineItemKind.Text, node, segments[si], w, h,
                               default, default, default, w, h, ascent));
                }
                if (trailing != 0f) items.Add(EdgeItem(node, trailing));
            }
            else if (node.Children.Count > 0)
            {
                var (leading, trailing) = InlineBoxEdges(node, maxWidth, viewportHeight);
                if (leading != 0f) items.Add(EdgeItem(node, leading));
                CollectInlineItems(node.Children, items, maxWidth, viewportWidth, viewportHeight);
                if (trailing != 0f) items.Add(EdgeItem(node, trailing));
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
                    if (node.Children.Count > 0)
                        LayoutChildrenImpl(node.Children, contentX, contentY, item.ContentW,
                            viewportWidth, viewportHeight, item.ContentH, 0f, true,
                            new List<ActiveFloat>(), true, out _, out _, out _);
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
            case InlineItemKind.InlineEdge:
                // Pure advance. The inline box's own painted geometry comes from the fragments its
                // text children record, so writing a box here would overwrite it with a sliver.
                break;
            case InlineItemKind.Image:
                {
                    // The item spans the margin box; the node's own box starts inside the
                    // margin/border/padding edges so the bitmap lands on the content box and
                    // the decorations get a padding/border box to paint against.
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
            case InlineItemKind.Text:
            case InlineItemKind.LineBreak:
                {
                    var rect = new SKRect(absX, absY, absX + item.ContentW, absY + item.ContentH);
                    // Record every fragment, and let Box span them all: an inline box broken over
                    // two lines has one rect per line, and painting only the last one lost the
                    // background and borders of every line above it.
                    if (item.Kind == InlineItemKind.Text)
                    {
                        (node.InlineFragments ??= []).Add((rect, item.Text ?? ""));
                        if (node.InlineFragments.Count > 1) rect = SKRect.Union(node.Box.ContentBox, rect);
                    }
                    // The line already reserved this box's horizontal padding (§8.4), so record it
                    // here too — the painter fills the PADDING box, and without it the background
                    // would stop short of the space the line gave the box. Anonymous boxes (#text)
                    // resolve their styles from the element that produced them, so taking padding
                    // there would apply the parent's a second time.
                    var noEdges = node.TagName.StartsWith('#') || node.IsBrokenAroundBlock();
                    node.Box = new BoxDimensions
                    {
                        ContentBox = rect,
                        Padding = noEdges ? default : node.GetPadding(0, 0, node.GetFontSize()),
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
    /// <summary><see cref="InlineEdge"/> is the leading or trailing margin+border+padding of a
    /// non-replaced inline box: it advances the line but has no height of its own (§8.4).</summary>
    private enum InlineItemKind { Text, Image, InlineBlock, InlineFlex, InlineTable, LineBreak, OutOfFlow, InlineEdge }

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
