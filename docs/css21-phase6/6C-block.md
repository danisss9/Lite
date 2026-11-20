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

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

- `css21-official/html4/margin-collapse-clear-000.htm`, `-001`, `-004`, `-006`, `-007`, `-010` and
  the WPT twins under `css/CSS2/margin-padding-clear/`: these contain no `float` and no `clear` at
  all — the failing bar is the collapsed-through parent's background ("each coloured bar matches a
  same-coloured box", the #c0c0c0 bar at 19,151). Pure §8.3.1 margin collapsing, BoxEngine.Block.cs.
  From 6.B (they landed on B by test-name stem).
- `floats-wrap-bfc-with-margin-001..010` (WPT): shared — the §10.3.3 negative-margin "prop-up" of an
  auto-width BFC beside a float is the Block-side width resolution in the
  `EstablishesBlockFormattingContext` branch; 6.B owns the float side of the same cases. From 6.B.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
