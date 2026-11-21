# 6.E Tables

Branch `css21/p6-e`, worktree `C:\Repos\Lite-p6-e`. Case list `Lite.Conformance/artifacts/lanes/E.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/TableEngine.cs
- Lite/Layout/BoxEngine.Normalize.cs (anonymous table boxes and the block-in-inline split)

## Opens with (parked findings)

- **Resolved in batch 2:** `table-anonymous-objects` 134, 203 and 204 and `column-visibility-002`
  pass (the base at 52f3bb54 plus the batch-2 margin/overflow commits; see Progress).
- Row heights run one line-height metric high when content is taller than a specified table height (`table-height-algorithm-004/009/010`); a `vertical-align: top` cell spans 4px (`012-014`). (004/009/010/012 moved in batch 1; 013/014 are 6.J's ink metrics.)
- `border-conflict-element-029` (RTL column order) is shared with 6.G.
- **caption-vs-table-box-001/002**: a caption is laid out from the table's CONTENT box
  (`LayoutCaptionBlock(caption, contentX, contentY, contentW, …)`), but §17.4 puts it in the
  wrapper: the table's border/padding must not inset or offset it. The two boxes then touch/offset
  and the `same-alignment` judge sees one box. The fix is to place the caption at
  `contentX − pad − border`, width `contentW + pad + border`, and (bottom captions) add the same
  edge below the rows; caption-vs-table-box-002's text alignment follows.
- **fixed-table-layout-025/028-031, table-columns-example-003**: the 50% cell's column is
  `50% × table + padding + border` (correct), so the auto columns get 0, but the engine keeps the
  auto cells' padding in their border box, so the red cells paint 50px wide. Browsers force a
  fixed-layout cell's border box to its column width (padding included). Clamp the cell box
  (or its padding) to the column width when `padding + border > column`.
- The content-box percentage width of `display: table` (with 6.C).

## Coordination

- The block-in-inline split in `BoxEngine.Normalize.cs` is shared territory with 6.A. Tell 6.A before changing it.

## Handed in

- `css/CSS2/css21-errata/s-11-1-1b-001..006, 008, 009` (from 6.K): `overflow` on a `<table>` — the
  §11.1.1 erratum. **Moved: 001, 005, 006, 008, 009.** 002/003/004 remain and are DEFECTIVE
  upstream reftests, not engine failures: Chromium (Edge 142, `--headless=new`) renders
  002-test's square at 50..74 against 002-ref's 94..113, and 003/004's at 35..54 against
  001-ref's 60..79; the refs shift the table (002-ref `margin-top:60px`) or use the captioned
  001-ref for a caption-less test. Handed to 6.X as exclusion candidates (see below).
- `content-172` (official + WPT, from 6.F): the `.normal` row's cell shrink-to-fits to its longest
  forced line ("XXX XXX") instead of the full collapsed pseudo content, so "XXX" wraps while the
  control cell stays on one line. The pre row is now correct. The max-content measure of a cell
  holding a pseudo child with newlines is the remaining cause (6.F did not change IntrinsicSizer
  or TableEngine).

- `top/left/right/bottom/position-applies-to-005/006/015` (both corpora, 30 cases in 6.D's
  list): the table-display variants — 'table-column-group', 'table-column', 'table-caption' —
  offset/position questions land in the table box geometry, not the positioning walk. From 6.D,
  20 November 2026.
_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- To 6.J: table-height-algorithm-013/014 — the engine geometry is right; the failure is the
  default font's (ascent − cap-top) × size ink offset in the selftest judge's ink measurement.
- To 6.M: table-height-algorithm-031/032 — the engine renders the assert (bottoms aligned,
  caption intentionally white-invisible); the judge counts the white caption as "no ink".
- To 6.G (unchanged, parked): border-conflict-element-029 waits on RTL column order.
- `border-applies-to-004`, `border-color-applies-to-004`, `border-{left,right}-applies-to-004`, `border-{left,right}-color-applies-to-004`, `border-{left,right}-width-applies-to-004` (official+WPT, 16): in `border-collapse: collapse`, a `table-row`'s left/right border is resolved onto the interior boundary between adjacent cells in `TableEngine` instead of only the row's outer left/right edges (from 6.I).
- `outline-applies-to-016`, `outline-applies-to-017` (WPT, 2): replaced `<img>` with `display: table-column-group` / `table-column` produces no laid-out box, so nothing paints (from 6.I).
- `c5520-brdr-b-001` (official + WPT, from 6.I): inside `td.skip` the nested table renders
  ABOVE the cell's leading anonymous block "dummy text" — source order is text first, then the
  table. The twin c5518 (same DOM, `border-top` instead of `border-bottom`) lays it out
  correctly (geom: c5518 child TABLE offsetTop=119.3 after the text; c5520 offsetTop=101.4 at
  the cell top). Cell child ordering diverges between the two passes; the judge-side needle
  problem (see 6.M) masks it today.

## Edits outside owned files

- `Lite/Layout/BoxEngine.Block.cs` (commit c62d5c02): LayoutBlockCore zeroes the wrapper
  padding for collapsed tables — §17.6.2 "a table does not have padding". Minimal: 4 lines at
  the padding resolution.
- `Lite/Drawer.cs` + `Lite/Drawer.Backgrounds.cs` (commit b4547ba0): PaintBlock paint areas
  (§14.2 border-box background colour; per-cell row/row-group rects from
  `TableEngine.TryGetCellAreaBackgrounds`; collapsed rows' background layer paints while
  borders stay suppressed) and PaintColumnBackgrounds draws the per-cell rects. 6.H: the
  PaintColumnBackgrounds change is in your file — take a look before editing that method.
- `Lite/Layout/BoxEngine.Inline.cs` (6.A's file; commits 3e222489, b2b4595e and the fix-round
  commit): `LineItemVerticalAlign` (cell text aligns baseline, scoped in the fix round to text
  directly in a non-inline container), the `<br>` item's font and line-height, and the
  `FindFirstBaselineY` caption skip. 6.A: the fix round touches only `LineItemVerticalAlign`.
- `Lite/Extensions/DrawCommandExtensions.cs` (shared style-getter layer; commit 97072a38): split
  `GetOverflow` into `DeclaredOverflow` (the computed value) + propagation/table-box adjustments,
  and added `PropagatesOverflowToViewport`. 6.D/6.F/6.I/6.J: the public `GetOverflow` meaning is
  unchanged except for HTML/BODY and table boxes; their pass lists were run (D/F/I/J, all green).
- `Lite/Layout/BoxEngine.Positioned.cs` (6.D's file; commit 97072a38): `ViewportOverflow` reads
  `DeclaredOverflow` so the propagating element's used 'visible' does not erase the viewport clip.
  3 lines; 6.D's pass list green.
- `Lite/Drawer.cs` (shared; commit 97072a38): the two `overflow` clip sites use
  `TableEngine.OverflowClipPath` for table boxes (padding-box / caption regions). 6.D's pass list
  green; keep the call signature if you edit `PaintNodeInner`/`PaintThroughClippers`.

## Progress

### Batch 1 (5 commits, lane list 168 → 43 passing)

- **`d6bb821a` — column element width floors cells (§17.5.2.2)**: ComputeColumnWidths kept only
  the first fixed cell per column and let it override the column element's width. Moved
  column-width-001/002 (both corpora). Units 648, gates 51/1 + 86/2, canary 300/300.
- **`c62d5c02` — collapsed table padding ignored (§17.6.2)**: the wrapper resolved the table's
  padding in the collapse model, shifting the whole cell band 13px/11px off the reference.
  Moved table-backgrounds-bc-colgroup/column-001 (both corpora), collapsing-border-model-004
  (both), 011/012 (WPT). Units 649, gates green, canary 300/300.
- **`b4547ba0` — table layer background paint areas (§14.2/§17.5.1/§17.6.1)**: background
  colour now paints the border box (transparent borders show it through); rows/groups/columns
  paint the per-cell area rects in the separated model (spacing stays on the table's layer);
  collapsed rows' background layer paints its band again (only borders stay suppressed).
  Moved all 20 table-backgrounds-* cases, table-layer-transparency-006/007,
  table-visual-layout-017. Units 651, gates green, canary 300/300.
- **`3e222489` — cell text line boxes align baseline; br box uses its own font (§17.5.3, §5.5,
  §10.8.1, §15.2)**: the line loop read the CELL's `vertical-align: middle` (the §17.5.3 row
  alignment, a UA default on the shared style object) as every text item's line alignment —
  shifted the text and grew each line to the shifted reach; and a br's inline box measured the
  UA default font and the raw font box instead of the inherited font and its line-height.
  Moved table-height-algorithm-004/009/010 (official). Units 652, gates green, canary 300/300.
- **`b2b4595e` — row baselines cover spanning cells, nested tables and captions (§17.5.3,
  §17.5.2, §17.4)**: rowspanning cells join the row baseline they anchor (and their growth
  measures against the whole span); nested-table children measure through the table model
  (spacing + cell edges, so the shrink-wrap no longer starves them); captions don't set the
  table's baseline in FindFirstBaselineY. Moved table-height-algorithm-012 (both corpora).
  Units 655, gates green, canary 300/300.

Edits outside owned files so far: `BoxEngine.Block.cs` (commit 2), `Drawer.cs` +
`Drawer.Backgrounds.cs` (commit 3), `BoxEngine.Inline.cs` (commits 4 and 5) — all named above.

Still failing, with causes:

- **table-height-algorithm-013/014 (both corpora)**: engine geometry right (the three
  `vertical-align: top` divs share a top); the judge measures text INK tops, whose offset below
  the box scales with font size — default-font metrics, handed to 6.J.
- **table-height-algorithm-031/032 (both corpora)**: engine renders the assert (bottoms aligned,
  caption intentionally white); the selftest judge counts the white caption as missing — handed
  to 6.M.
- **fixed-table-layout-025/028-031, table-columns-example-003, anonymous-table-box-width-001**:
  fixed-layout width distribution cases — next group, not yet diagnosed this batch.
- **border-collapse-dynamic-*** (row/rowgroup/colgroup/column/cell/table 002-003): dynamic
  border re-resolve after DOM/style changes — not yet diagnosed this batch.
- **border-conflict-element-001d/002/021/029/038, border-conflict-example-001,
  border-conflict-resolution-006, collapsing-table-borders-008/010, collapsing-border-model-
  010b/013/014**: §17.6.2.1 conflict-resolution probes — 029 is parked for 6.G (RTL).
- **border-spacing-003/083/084/095, border-spacing-applies-to-014/015**: spacing paint/apply
  probes — not yet diagnosed.
- **table-anonymous-objects-008/211/213, table-anonymous-text-indent,
  table-anonymous-block-015**: anonymous boxes (the parked 134/203/204 stale-row-band findings
  are not on the lane list; 211/213 are queued as the next group).
- **row-visibility-001/002/003, column-visibility-004**: visibility handling.
- **captions**: caption-position-001, caption-side-applies-to-006/007, caption-vs-table-box-001/002.
- **inline-tables**: inline-table-002a/002b/003, inline-table-valign-001, inline-table-zorder-005.
- **floats**: table-among-floats-001, table-sizing-with-adjacent-floats, c5525-fltwidth-003 (×2).
- **rest**: table-visual-layout-002/007/008/012, table-valign-001, tables-102,
  basic-css-table-001, column-horizontal-alignment-003/005, separated-border-model-004c/004d,
  table-header/footer-group-005, table-vertical-align-baseline-009, border-collapse-006,
  border-collapse-empty-row, border-collapse-offset-001/002,
  border-collapse-visibility-collapse-001/002 (tentative).

### Fix round (batch-1 loss)

- **Loss:** `css/CSS2/linebox/vertical-align-nested-top-001.html` (passed at 05228a26), caused by
  3e222489. That commit made every `#text` item align 'baseline', so cell text stops reading the
  cell's §17.5.3 'vertical-align: middle'. But a `#text` inside an INLINE element is that inline
  box's content and is placed with it (§10.8.1): the first 'X' inside the 'vertical-align: top'
  span dropped to the baseline, below the nested top-aligned 'X'.
- **Fix:** `LineItemVerticalAlign` aligns a `#text` 'baseline' only when its parent is not an
  inline box (a table cell, a block: text in the root inline box). Text inside an inline element
  keeps that element's alignment, as before 3e222489.
- **Moved:** the loss passes again. No other change on the lane list.
- **Gains kept:** all 43 lane passes hold (E.txt 43 pass / 124 fail / 1 manualpending, same as
  after b2b4595e), including 3e222489's `table-height-algorithm-004/009/010`. No gain had to go.
- **Checks:** units 656/0 (new `LayoutTests.TextInsideTopAlignedInline_KeepsItsBoxAlignment`,
  which fails without the fix at y=34); css21 51/1 expected-fail; wpt 86/2 expected-fail;
  `fix-losses.txt` 1/1; `E-pass.txt` 1729/1729; `canary.txt` 960/960.

### Batch 2 (3 commits, E.txt 0 → 6 passing; 5 handed-in cases + 2 parked findings)

- **`1fc0ca79` — table cells and rows ignore margins (§17.5)**: the cell box was computed from
  `GetMargin()`, so a UA 8px BODY margin inside a cell shrank s-11-1-1b-005's 20px cell to 4px and
  s-11-1-1b-006's -15px cell margin lifted its square. Cells and rows now use zero margins.
  Moved `s-11-1-1b-006`, `table-visual-layout-002` (official+WPT). Units 736/0
  (`TableCellAndRow_IgnoreMargins`), gates 51/1 + 86/2, E.txt 2 passing, E-pass 1777/1777,
  canary 960/960.
- **`97072a38` — table overflow clips to the table box; root/body values propagate (§11.1.1)**:
  the clip is the table box between the captions (captions join the clip region, they sit outside
  the table box); `scroll`/`auto` on a table box mean `visible` (erratum s.11.1.1b); the HTML/BODY
  element a value propagates from gets a used `visible` (the viewport still reads the declared
  value). Moved `s-11-1-1b-001/005/008/009`. Units 738/0 (`OverflowOnTable_ClipsTheTableBoxNotTheWrapper`,
  `TableOverflowOnRootOrBody_PropagatesToTheViewport`), gates green, E.txt unchanged,
  E-pass 1777/1777, canary 960/960, and the neighbour pass lists for the files touched:
  D 1203/1203 (`BoxEngine.Positioned.cs`, `Drawer.cs`), F 1789/1789, I 2475/2475, J 519/519
  (`DrawCommandExtensions.cs`).
- **`8b782617` — a caption inherits 'caption-side' from its table (§17.4)**: the table's value
  lives in its StyleOverrides channel, which the caption's style object does not see; the lookup
  now walks the caption's ancestors (its own declaration still wins). Moved
  `caption-side-applies-to-006/007` (official+WPT). Units 739/0
  (`CaptionSide_IsInheritedFromTheTable`), gates green, E.txt 6 passing, E-pass 1777/1777,
  canary 960/960.

Handed off this batch:

- **To 6.K (cascade):** `background` shorthand vs `background-color` longhand. Minimal repro:
  `div{background-color:orange} #x{background-color:blue}` → blue, but
  `#x{background-color:blue} div{background:orange}` → orange, even though the ID wins the
  cascade. The same happens on a plain `div` (not table-specific). This is what keeps
  `table-visual-layout-012` orange instead of blue (`#cell{background-color:blue}` vs
  `td{background:orange}`), and it likely affects 6.H's background cases.
- **To 6.M (judges):** `table-visual-layout-007/008` — the render is navy/blue/aqua in order, but
  `GradientLighter`'s `SameHue` (±30°) drops aqua (hue 180 vs blue 240) and needs 3. The judge
  should read the "lighter blue" sentence as lightness over a wider hue band. `border-spacing-003/083/084/095`
  and `border-spacing-applies-to-014/015` — the renders match the sentence (a 1px bar in the 1px
  gap, etc.), but `Components` merges the touching bar with the cells, so `BarGap`/`no-horizontal-gap`
  see one shape instead of the expected count.
- **To 6.G:** `border-conflict-element-029` (RTL column order) — unchanged; the red border needs
  the first `<col>` on the right. 6.E has not touched `TableEngine`'s column X order, so whichever
  lane implements RTL tables can use `CellX`/`BuildGrid` directly.
- **To 6.X:** `s-11-1-1b-002/003/004` are defective upstream reftests (Chromium fails them the same
  way; see Handed in) — exclusion candidates if the evidence lane accepts browser evidence.

Still failing after batch 2, with causes:

- The `background` shorthand cascade bug (6.K) blocks `table-visual-layout-012` (both corpora).
- `caption-vs-table-box-001/002`: the caption is laid out from the table's content box instead of
  the wrapper (parked finding above).
- `fixed-table-layout-025/028-031`, `table-columns-example-003`: zero-width auto columns keep
  their cells' padding in the box (parked finding above).
- `border-collapse-dynamic-*` (8): dynamic re-resolve after DOM/style changes — not yet diagnosed.
- `row-visibility-001/002/003`, `column-visibility-004`: `visibility: collapse` handling.
- `border-conflict-element-001d/002/021/029/038`, `border-conflict-example-001`,
  `border-conflict-resolution-006`, `collapsing-table-borders-008/010`,
  `collapsing-border-model-010b/013/014`: §17.6.2.1 conflict resolution probes.
- `inline-table-002a/002b/003`, `inline-table-valign-001`, `inline-table-zorder-005`.
- `table-among-floats-001`, `table-sizing-with-adjacent-floats`, `c5525-fltwidth-003` (×2) (6.B's
  float layout interacts; next batch).
- `table-height-algorithm-013/014` (6.J ink metrics) and `031/032` (6.M white-caption judge).
- `table-visual-layout-007/008` (6.M hue tolerance), `border-spacing-003/083/084/095` and
  `border-spacing-applies-to-014/015` (6.M shape segmentation).
- `table-anonymous-objects-008/211/213`, `table-anonymous-text-indent`, `table-anonymous-block-015`.
- `table-valign-001`, `table-visual-layout-002` (official+WPT pass; 007/008/012 above),
  `tables-102` (worker-state flake), `basic-css-table-001`, `column-horizontal-alignment-003/005`,
  `separated-border-model-004c/004d`, `table-header/footer-group-005`,
  `table-vertical-align-baseline-009`, `border-collapse-006`, `border-collapse-offset-001/002`,
  `border-collapse-visibility-collapse-001/002` (tentative), `caption-position-001`.
