# Lane F: flow: block, margins, floats and clear (batch 3)

Merges 6.B ([6B-floats.md](6B-floats.md)) and 6.C ([6C-block.md](6C-block.md)). Branch `css21/b3-f`, worktree `C:\Repos\Lite\.claude\worktrees\b3-f`.
Case list `Lite.Conformance/artifacts/lanes/F.txt` (pass list `F-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Layout/BoxEngine.Block.cs
- Lite/Layout/BoxEngine.Floats.cs
- Lite/Layout/BoxDimensions.cs
- Lite/Layout/CssUnits.cs

## Opens with (parked findings)

- Clusters: `normal-flow` (43 WPT), `margin-padding-clear` (33), `floats` + `floats-clear` (43),
  `margin-collapse` + `margin-collapse-clear` (20), `css/CSS2/visudet` (10).
- `margin-collapse-162/163`: the scalar running collapse loses negative margins in chains of three or
  more. Carry the collapsed margin as (maxPositive, minNegative) through `LayoutBlock`'s returned
  bottom margin.
- `margin-collapse-145..148`: a float inside a collapsed-through chain must track the chain bottom (6C).
- `list-style-*-applies-to-017` (WPT): an empty list item beside a float keeps its full-width band (6B).
- `border-dynamic-001/002`: inside a bordered auto-height block, an empty child's self-collapsed margin
  is lost (6C).
- `margin-percentage-inherit-001` still fails on the abs-pos overlay geometry (6C).
- The float behaviour behind the reverted `ch` fix (6D): a right float inside nowrap text paints over
  the line instead of shortening it (`float-nowrap-3/4/8/9`). Fix this first, then lane T lands `ch`.
- `c5525-fltinln-000`, `c5525-fltmrgn-000`: an anonymous `#text` paints its block parent's background
  colour over the background image (`Drawer.cs` `PaintInlineBoxOwnContent`, 6D). This touches lane V's
  painting, so coordinate with V.
- `hit-test-floats-003/005`: `elementFromPoint` over floats returns "error" (6M). This is hit testing:
  check it before assuming a layout cause.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
