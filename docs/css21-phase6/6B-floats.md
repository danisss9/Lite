# 6.B Floats and clear

Branch `css21/p6-b`, worktree `C:\Repos\Lite-p6-b`. Case list `Lite.Conformance/artifacts/lanes/B.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Floats.cs

## Opens with (parked findings)

- `border-conflict-style-107` (WPT): five floated 100x100 bordered tables pack into overlapping bars instead of a 200x200 square. This is float sizing and placement of bordered tables, shared with 6.E.
- `floats-137` is a timed render (6.M owns the protocol). The engine side is yours once 6.M routes it.

## Coordination

- Float placement inside inline runs (`PlaceInRunFloat`) lives in your file, but its caller is in `BoxEngine.Inline.cs` (6.A).
- Margin collapsing and clearance interact: `ApplyClear` is yours, and the margin chain (`BoxEngine.Block.cs`) is 6.C's.

## Handed in

- `list-style-applies-to-017`, `list-style-type-applies-to-017`,
  `list-style-image-applies-to-017`, `list-style-position-applies-to-017` (all WPT, from 6.F):
  an empty `display: list-item` block that is a sibling of a block-level float keeps its
  full-width content box instead of taking the float's shifted line band; its outside marker
  therefore paints off the left edge (expected: beside the float, where the reference puts it).
  The pseudo/position work in 6.F is done; only the float band is missing.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
