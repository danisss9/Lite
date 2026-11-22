# Lane Bd: bidi (batch 3)

Merges 6.G ([6G-bidi.md](6G-bidi.md)). Branch `css21/b3-bd`, worktree `C:\Repos\Lite\.claude\worktrees\b3-bd`.
Case list `Lite.Conformance/artifacts/lanes/Bd.txt` (pass list `Bd-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Layout/Bidi.cs
- Lite/Layout/BidiClassTable.cs
- Lite/Layout/BoxEngine.Inline.cs: `ReorderForBidi` and its helpers. Agree any line-assembly change
  with lane T, which merges before you.

## Opens with (parked findings)

- Clusters: `bidi-text` (19), the bidi cases of `css/CSS2/text` (12), `bidi`, `text-align-bidi`,
  `bidi-list`, `bidi-override`, `bidi-border-collapse`.
- `text-align-bidi-008..010`: justified RTL lines end 10px apart. This is the known RTL start-edge
  asymmetry (6M → 6G).
- `border-conflict-element-029`: rtl column order and the conflict winner. Coordinate with lane Tb on
  `BuildGrid`/`CellX`.
- Keep the UAX #9 §5.2 rule: strip only RLE/LRE/RLO/LRO/PDF from rendered text, never BN. Breaking it
  caused a batch-2 regression.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
