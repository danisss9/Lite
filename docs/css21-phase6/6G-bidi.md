# 6.G Bidi (wave 2)

Branch `css21/p6-g`, worktree `C:\Repos\Lite-p6-g`. Case list `Lite.Conformance/artifacts/lanes/G.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/Bidi.cs
- Lite/Layout/BidiClassTable.cs
- Lite/Layout/BoxEngine.Inline.cs: `ReorderForBidi` and its helpers (agree any line-assembly change with 6.A)

## Opens with (parked findings)

- Line breaking on the visual order: the reorder engages only when the composed run fits one line band.
- The line's start edge is not mirrored (text-align under rtl): `text-align-bidi-002/003/004` share one 10px centering asymmetry.
- Merge the legacy `BidiReversesContent` override path into `Bidi.cs`.
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

- `css/CSS2/text/text-align-bidi-008.xht`, `text-align-bidi-009.xht`, `text-align-bidi-010.xht` — the justified RTL
  paragraph's lines do not end on one edge: the inked band ends alternate 641..787 / 631..785 (a 10px spread) though
  the box content box is 628..790. §16.2 requires justified lines to line up on both edges, so this is the known
  10px RTL start-edge asymmetry, now visible because the judge reads inside the content box and mirrors the last
  line's start edge. From 6.M.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
