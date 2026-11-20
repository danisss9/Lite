# 6.E Tables

Branch `css21/p6-e`, worktree `C:\Repos\Lite-p6-e`. Case list `Lite.Conformance/artifacts/lanes/E.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/TableEngine.cs
- Lite/Layout/BoxEngine.Normalize.cs (anonymous table boxes and the block-in-inline split)

## Opens with (parked findings)

- `table-anonymous-objects` 134 (reused loose cells keep their previous-pass row band after re-projection), 203 and 204 (cell-metrics fringe).
- Row heights run one line-height metric high when content is taller than a specified table height (`table-height-algorithm-004/009/010`); a `vertical-align: top` cell spans 4px (`012-014`).
- `column-visibility-002` paints red.
- `border-conflict-element-029` (RTL column order) is shared with 6.G.
- The content-box percentage width of `display: table` (with 6.C).

## Coordination

- The block-in-inline split in `BoxEngine.Normalize.cs` is shared territory with 6.A. Tell 6.A before changing it.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- To 6.J: table-height-algorithm-013/014 — the engine geometry is right; the failure is the
  default font's (ascent − cap-top) × size ink offset in the selftest judge's ink measurement.
- To 6.M: table-height-algorithm-031/032 — the engine renders the assert (bottoms aligned,
  caption intentionally white-invisible); the judge counts the white caption as "no ink".
- To 6.G (unchanged, parked): border-conflict-element-029 waits on RTL column order.

## Edits outside owned files

- `Lite/Layout/BoxEngine.Block.cs` (commit c62d5c02): LayoutBlockCore zeroes the wrapper
  padding for collapsed tables — §17.6.2 "a table does not have padding". Minimal: 4 lines at
  the padding resolution.
- `Lite/Drawer.cs` + `Lite/Drawer.Backgrounds.cs` (commit b4547ba0): PaintBlock paint areas
  (§14.2 border-box background colour; per-cell row/row-group rects from
  `TableEngine.TryGetCellAreaBackgrounds`; collapsed rows' background layer paints while
  borders stay suppressed) and PaintColumnBackgrounds draws the per-cell rects. 6.H: the
  PaintColumnBackgrounds change is in your file — take a look before editing that method.

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
