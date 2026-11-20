using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

internal static partial class BoxEngine
{
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
                or DisplayType.Flex or DisplayType.Table
            && !(IsCssTableCaption(n) && WhitespaceOnlyInterior(n) && RendersNoBoxOfItsOwn(n));

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
        // §9.2.1.1 with §8.6: the box's start edges (margin, border, padding) belong to its FIRST
        // piece and its end edges to its LAST one; a piece between two blocks has neither. Every
        // piece used to paint as a whole box, closing the first piece with a right border and
        // opening the last with a left one (block-in-inline-insert-001a: 'OneTwoThree' boxed on
        // all four sides, 'Six' between blocks boxed too).
        var afterBlock = false;
        var emitted = new List<(LayoutNode Piece, bool AfterBlock)>();
        // An empty piece is still an inline box when it carries a horizontal edge (the last
        // piece's 'padding-right: 10px', block-in-inline-empty-001): it is kept tentatively and
        // dropped below once its sides are known to be zero.
        var emptyPieces = new List<LayoutNode>();

        void FlushPiece()
        {
            var visible = PieceHasVisibleContent(piece);
            if (visible || IsSplittableInline(piece))
            {
                result.Add(piece);
                emitted.Add((piece, afterBlock));
                if (!visible) emptyPieces.Add(piece);
            }
            piece = CloneInlineShell(inline);
        }

        void EmitBlock(LayoutNode block)
        {
            FlushPiece();
            afterBlock = true;
            // §9.2.1.1 last sentence: relative positioning of the inline (and its inline ancestors)
            // also translates the block-level box it was broken around.
            PropagateRelativePosition(inline, block);
            SuppressSharedStyleBorder(inline, block);
            block.SplitFrom ??= inline;
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
        var beforeAnyBlock = !afterBlock;
        FlushPiece();
        if (!beforeAnyBlock)
        {
            // Pieces flushed before the last hoisted block do not close the box.
            var lastBlockIndex = result.FindLastIndex(IsInFlowBlockLevel);
            var rtl = inline.GetDirection() == "rtl";
            foreach (var (p, opensAfterBlock) in emitted)
            {
                var closesBeforeBlock = result.IndexOf(p) < lastBlockIndex;
                if (opensAfterBlock) DropInlineSide(p, rtl ? "right" : "left");
                if (closesBeforeBlock) DropInlineSide(p, rtl ? "left" : "right");
            }
        }
        // §9.4.2 as browsers apply it: an empty piece with no horizontal margin, border or
        // padding generates no line box worth keeping (a piece between two blocks never has one).
        foreach (var empty in emptyPieces)
        {
            var (lead, trail) = InlineBoxEdges(empty, 0f, 0f);
            if (lead == 0f && trail == 0f) result.Remove(empty);
        }
        return result;
    }

    /// <summary>Removes one side's margin, padding and border from a broken inline's piece.</summary>
    private static void DropInlineSide(LayoutNode piece, string side)
    {
        piece.StyleOverrides["margin-" + side] = "0";
        piece.StyleOverrides["padding-" + side] = "0";
        piece.StyleOverrides["border-" + side + "-style"] = "none";
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
        clone.SplitFrom = inline;
        CopyDict(inline.StyleOverrides, clone.StyleOverrides);
        clone.DynamicRules = inline.DynamicRules;
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
            if (IsCssTableCaption(child) && WhitespaceOnlyInterior(child) && RendersNoBoxOfItsOwn(child))
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

    /// <summary>True when a content-less box would still paint nothing and take no space: no
    /// height or min-height, no border, padding or background. Only such a caption may be
    /// skipped; a caption element always generates its box, so an empty one with
    /// 'height: 1in; background: black' must still paint (*-applies-to-015).</summary>
    private static bool RendersNoBoxOfItsOwn(LayoutNode node)
    {
        if (node.GetHeight() > 0 || node.GetMinHeight() > 0) return false;
        var border = node.GetBorderWidth();
        if (border.Top + border.Right + border.Bottom + border.Left > 0) return false;
        var padding = node.GetPadding(0, 0, node.GetFontSize());
        if (padding.Top + padding.Right + padding.Bottom + padding.Left > 0) return false;
        return node.GetBackgroundColor().Alpha == 0 && node.GetBackgroundImage() is null;
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
            // not become a cell for the surrounding spacing to hang on. An empty box that would
            // still PAINT (background, border, height — empty-cells-applies-to-006/007/015's
            // 1em black table inside a row) is not empty for this purpose.
            if (run.All(c => (c.TagName == "#text" && string.IsNullOrWhiteSpace(c.DisplayText))
                || (!c.TagName.StartsWith('#') && IsEmptyTableBox(c) && RendersNoBoxOfItsOwn(c))))
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
                if (IsCssTableCaption(later) && WhitespaceOnlyInterior(later) && RendersNoBoxOfItsOwn(later)) continue;
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
                IsCssTableCaption(child) && WhitespaceOnlyInterior(child) && RendersNoBoxOfItsOwn(child))
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
            if (child.TagName is "TBODY" or "THEAD" or "TFOOT" or "CAPTION" or "COL" or "COLGROUP") return true;
            // The CSS display values are proper too: a div styled 'display: table-caption' or
            // 'table-column' is the table's caption/column, not content to wrap in a cell.
            var raw = child.TryResolveStyle("display", out var d) ? d?.Trim() : child.Style.GetPropertyValueSafe("display")?.Trim();
            return raw is "table-caption" or "table-column" or "table-column-group";
        }
        return child.GetDisplay() == DisplayType.TableCell;
    }
}
