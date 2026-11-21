# 6.G Bidi (wave 2)

Branch `css21/p6-g`, worktree `C:\Repos\Lite-p6-g`. Case list `Lite.Conformance/artifacts/lanes/G.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/Bidi.cs
- Lite/Layout/BidiClassTable.cs
- Lite/Layout/BoxEngine.Inline.cs: `ReorderForBidi` and its helpers (agree any line-assembly change with 6.A)

## Opens with (parked findings)

- Line breaking on the visual order: the reorder engages only when the composed run fits one line band.
- The line's start edge is not mirrored (text-align under rtl): `text-align-bidi-002/003/004` share one 10px centering asymmetry.
- §8.6 multi-fragment wrapper geometry for boxes split by the reordering: `bidi-001..004`, `bidi-005a..010a`
  order correctly now, but the painter unions a split box's fragments into one rect (borders across the gaps).
  Needs the per-line wrapper accumulator to emit one fragment box per contiguous run — 6.A's line-assembly area.
- `bidi-override-003/004` (list-marker placement under override, with 6.F) and `bidi-border-collapse-001..004` (RTL collapsed tables, with 6.E).

## Coordination

- Starts after the first wave-1 merge batch, from the merged head.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

- `css/CSS2/tables/border-conflict-element-029.xht` (WPT, from 6.E): `<table dir/ direction: rtl>`
  with three `<col>`s; the first `<col>` must be the RIGHTMOST column, so its black left/right
  border beats the following columns' red right border in §17.6.2.1's conflict. 6.E has NOT changed
  `TableEngine`'s column X order (`BuildGrid`/`CellX` still place column 0 on the left), so the
  red border survives (935 red pixels). The cell positions and the border-conflict winner both
  follow the table's 'direction'; 6.E can carry the TableEngine side if 6.G lands the direction
  handling, or 6.G can take both. Coordinate before either branch edits `BuildGrid`/`CellX`.
- `css/CSS2/fonts/fonts-011.xht` (WPT, from 6.J): fixed by the X9 explicit-control pass (see Progress, commit 1) —
  the RLO/PDF between the two `X` glyphs no longer reserve an Ahem advance, so the green background covers the
  2em box. Also fixes the WPT `line-breaking-bidi-001..003` red gaps. 6.J can re-run its list.

- `css/CSS2/text/text-align-bidi-008.xht`, `text-align-bidi-009.xht`, `text-align-bidi-010.xht` — the justified RTL
  paragraph's lines do not end on one edge: the inked band ends alternate 641..787 / 631..785 (a 10px spread) though
  the box content box is 628..790. §16.2 requires justified lines to line up on both edges, so this is the known
  10px RTL start-edge asymmetry, now visible because the judge reads inside the content box and mirrors the last
  line's start edge. From 6.M.

## Edits outside owned files

- `Lite/Extensions/DrawCommandExtensions.cs` — one line in `BidiVisualBlockText`: when the block's own text is
  wider than the band (the queued per-line reorder stands down), return `Bidi.WithoutExplicitFormatting(text)`
  instead of the raw string, so X9's controls never paint/reserve width in wrapped block text
  (`fonts-011`, `line-breaking-bidi-001..003`). Commit 2 also widened its content gate to admit a declared
  override on pure-Latin text, and routes `RenderedText` through `BidiReversesContentForLayout`.
- `Lite/Drawer.cs` — one line in `PaintBlock`'s own-text branch: draw
  `node.BidiVisualBlockText(textMaxW, font)` instead of `node.RenderedText()`, so an inline-block's own text
  honours a declared override (`unicode-bidi-applies-to-012`); the other block text sites already did.
- `Lite/Layout/BoxEngine.Inline.cs` outside `ReorderForBidi`/helpers: the collection-time child-sequence
  reversal and the run's top-level sequence reversal now gate on `BidiReversesContentForLayout()` (legacy
  only for edge-bearing scopes).

## Progress

### 2026-11-20 — Commit 1: §16.2/UAX #9 explicit formatting characters (X2-X5, X7, X9)
- **Moved (G list, +12: 6 official, 6 WPT; official 0/56 → 6/56, WPT 0/65 → 6/65):**
  - `bidi-005b`, `bidi-006b`, `bidi-011` (official + WPT)
  - `line-breaking-bidi-001`, `-002`, `-003` (official + WPT)
  - WPT `fonts-011` also passes now (handed to 6.J; not in G).
- **What changed:**
  - `Bidi.Reorder` now runs UAX #9's embedding stack (X1-X7): explicit RLE/LRE/RLO/LRO push the least
    odd/even greater level, PDF pops the innermost explicit frame, and CSS `embed`/`bidi-override` spans enter
    the same stack as span events. X9's removed characters (RLE/LRE/RLO/LRO/PDF/BN) are dropped from the
    visual string after L2 by character class (an index filter drops a rendered neighbour once L2 moves a
    control). New helpers `IsRemovedByX9`, `HasExplicitFormatting`, `WithoutExplicitFormatting`.
  - `ReorderForBidi` (owned helpers): the fragment sequence now preserves item order (non-text items take
    their own slot instead of being appended after every text item); text slices lose X9 controls and are
    re-measured; the run's trailing collapsible space is trimmed before L2 (§16.6.1 — bidi-011's override
    scope used to carry it into the middle of the line); the width gate measures the controls as zero;
    reordered fragments set `ContentW` (the painted rect was the whole unsplit item's width); inline EDGES
    are no longer stream fragments — §8.6 attaches each to its box's first/last visual content fragment.
    Zero-width markers between characters take the neighbouring text's level, so an empty inline no longer
    splits an L2 run (restores `css/CSS2/text/bidi-span-001.html`).
- **Still failing and why:** `bidi-001..004`, `bidi-005a..010a` need the §8.6 **multi-fragment** wrapper box:
  the painter's per-line wrapper accumulator unions all of a split box's content fragments into one rect
  (huge borders across the reordered gaps) and the 005a-family reference paints one bordered piece per
  contiguous run. That is line-assembly/wrapper code owned by 6.A (parked, see below). `direction-unicode-bidi-*`
  still needs the legacy override merge (next commit). `bidi-glyph-mirroring` needs bracket mirroring.
- **Handed to other lanes:** 6.J (`fonts-011`); 6.A coordination note added (below).
- **Checks:** units 737/738 (new `BidiUbaTests`: `Reorder_ExplicitRloPdfIsAnEmbeddingAndRendersNothing`,
  `InlineRun_ExplicitRloSpansItemsAndOmitsControlWidth`, `InlineRun_EmptyInlineBoxDoesNotSplitTheL2Run`; the
  one failure `GeneratedContentTests.EmptyOutsideListItem_HasItsMarkerLineBox` also fails with this batch
  stashed at the base commit — pre-existing, not this change). Curated css21 51/1, wpt 86/2. G list 6/56 +
  6/65. `G-pass.txt` 223/223, `A-pass.txt` 1405/1405, `canary.txt` 960/960.

### 2026-11-20 — Commit 2: §16.2 override scopes merge into the paragraph pass
- **Moved (G list, +8: 4 official, 4 WPT; official 6/56 → 10/56, WPT 6/65 → 10/65):**
  - `direction-unicode-bidi-019`, `-021`, `-023` (official + WPT)
  - `bidi-inline-002` (official + WPT)
- **What changed:** the legacy whole-stream reversal (`BidiReversesContent`) now fires only for scopes that
  carry §8.6 edge boxes (`BidiReversesContentForLayout`). Edge-less override scopes route to the
  paragraph-level pass, which models them as UBA override spans and handles mixed Latin/Arabic content —
  the legacy subset only covered strong-LTR content. Gated consistently at all four reversal sites:
  `RenderedText`, the collection-time child sequence, the run's top-level child sequence, and
  `ReorderForBidi`'s bail. `ReorderForBidi`'s gate also engages on a declared inline embed/override scope
  (no RTL-class char needed, e.g. `dnoceS`). `BidiVisualBlockText` now admits a declared override on
  pure-Latin block text, and `PaintBlock`'s own-text branch uses it (inline-block `SSAP SSAP` → `PASS PASS`).
- **Still failing and why:** `direction-unicode-bidi-026/027/028` are judge-side (two-line needle vs
  single-line candidate) — handed to 6.M. `bidi-001..004`/`005a..010a` need the §8.6 multi-fragment wrapper
  (6.A). `bidi-breaking-001/002`, `bidi-glyph-mirroring`, `bidi-alt-001` remain (paragraph/base-direction and
  glyph-mirroring work); `bidi-override-003/004` with 6.F, border-collapse with 6.E.
- **Restored while widening the gate:** `unicode-bidi-applies-to-007/009/012` (official + WPT) had regressed
  when `RenderedText` stopped reversing edge-less scopes; they pass again through the paragraph pass.
- **Checks:** units 739/740 (new `InlineRun_EdgeLessRtlOverrideSpanOwnsMixedContent`; same pre-existing
  `GeneratedContentTests.EmptyOutsideListItem_HasItsMarkerLineBox`). Curated css21 51/1, wpt 86/2. G list
  10/56 + 10/65. `G-pass.txt` 223/223, `A-pass.txt` 1405/1405, `canary.txt` 960/960.

### 2026-11-20 — Batch 2 close: residues and handoffs (no engine change)
- **`text-align-bidi-002/003/004` are judge-side, handed to 6.M.** The P's line is advance-centred on
  its 784px content box (8..792; `WrapText` gives 375.352px for the 53-char line, draw x 212.3). The
  judge's `AlignCenter` climbs `container = parent.Parent` each iteration and ends on the document's
  0..800 box, so its ink-edge gaps differ by the 8px body margin plus side bearings (reported 223/213).
  No engine change made; details in `6M-judges.md`.
- **`text-align-bidi-008/009/010` and `-011` are the queued per-line visual order.** Their block own
  text wraps before reordering: Hebrew RTL lines (008/009/010) and the whole-paragraph RLO of 011 paint
  in logical order because `BidiVisualBlockText` stands down when the paragraph is wider than the band
  and returns the X9-stripped logical text. The fix belongs with the line-breaking restructure: break
  on the logical order, then run `Bidi.Reorder` per line box (and paint the line's visual string). This
  needs `Drawer.Text.DrawWrappedText`/the line assembler (6.A's file), so it is left parked and agreed
  in `6A-inline.md` rather than half-done here.
- **`bidi-001..004`/`005a..010a`** order correctly but need multiple §8.6 wrapper fragments per line
  (6.A, parked above). **`bidi-breaking-001/002`** need the bidi-break paragraph rules (RLM/block
  boundary); **`bidi-glyph-mirroring-001/002`** need bracket mirroring; **`bidi-alt-001`** is alt-text
  fallback. **`bidi-override-003/004`** remain with 6.F, **`bidi-border-collapse-001..004`** with 6.E.
- **Checks (unchanged engine identity):** units 739/740 (pre-existing `EmptyOutsideListItem` failure);
  curated css21 51/1, wpt 86/2; G list 10/56 + 10/65; `G-pass` 223/223, `A-pass` 1405/1405, canary
  960/960.


