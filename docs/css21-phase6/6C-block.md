# 6.C Block flow, margin collapse and sizing

Branch `css21/p6-c`, worktree `C:\Repos\Lite-p6-c`. Case list `Lite.Conformance/artifacts/lanes/C.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Block.cs
- Lite/Layout/BoxDimensions.cs
- Lite/Layout/CssUnits.cs

## Opens with (parked findings)

- Lite sizes a `display: table` box's percentage width as content-box, so a bordered 100%-wide table's cells record boxes past the table's border (`text-align-applies-to-008`). Settle the reading against the spec and browsers with 6.E.
- `inline-block-replaced-height-008`: an `<object>` whose `style` sets `width: 15%` together with a height keeps its intrinsic 60px (replaced sizing lives in `BoxEngine.Block.cs`).

## Coordination

- The `ex` unit resolves in `CssUnits.cs`, which you own, but the measured x-height comes from 6.J. Take 6.J's metric API, and do not measure fonts here.

## Handed in

- `border-dynamic-001`, `border-dynamic-002` (official+WPT, 4): even statically (without JS), a bordered `#test` containing an empty child with `margin: 10em 0` collapses to 0 height instead of including the child's self-collapsed 10em margin inside the bordered block's auto height (from 6.I).

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
