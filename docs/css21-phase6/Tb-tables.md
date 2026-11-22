# Lane Tb: tables (batch 3)

Merges 6.E ([6E-tables.md](6E-tables.md)). Branch `css21/b3-tb`, worktree `C:\Repos\Lite\.claude\worktrees\b3-tb`.
Case list `Lite.Conformance/artifacts/lanes/Tb.txt` (pass list `Tb-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Layout/TableEngine.cs
- Lite/Layout/BoxEngine.Normalize.cs (anonymous table boxes and the block-in-inline split)

## Opens with (parked findings)

- Clusters: `css/CSS2/tables` (64 WPT), `table-height-algorithm`, `table-visual-layout`,
  `row-visibility`.
- **First: the table percentage width.** `width: 100%` on a bordered `display: table` box is sized as a
  content box, so the table overflows its container by the border.
  - It blocks lane J's judge fix for `text-align-applies-to-007/012/013/015`.
  - The 12 passing siblings `…-006/008/009/010/011/014` must keep passing.
  - Tell lane J when it lands.
- `margin-*-applies-to-007/012..015`: §8.3, margin does not apply to table-internal boxes (6C). The
  `min/max-width-applies-to` and `padding-*-applies-to` table variants likely share the cause.
- `border-*-applies-to-004` (16): in the collapsing model, a row's left/right border lands on interior
  cell boundaries (6I).
- `outline-applies-to-016/017`: an `<img>` with `display: table-column(-group)` produces no box (6I).
- `content-172`: a cell's max-content when a pseudo child holds newlines (6F).
- `c5520-brdr-b-001`: the nested table lays out above the cell's leading anonymous block (6I).
- `border-conflict-element-029`: rtl column order. Coordinate with lane Bd before editing
  `BuildGrid`/`CellX`.
- `s-11-1-1b-002/003/004` are proposed defective; lane J holds the proposal.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
