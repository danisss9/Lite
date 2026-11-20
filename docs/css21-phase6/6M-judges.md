# 6.M Judges and verdict routes (no engine changes)

Branch `css21/p6-m`, worktree `C:\Repos\Lite-p6-m`. Case list `Lite.Conformance/artifacts/lanes/judges.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite.Conformance/Css21/VisualChecks.cs
- Lite.Conformance/Wpt/WptVisualPage.cs
- Lite.Conformance/Css21/SelfTestOracle.cs
- Lite.Conformance/Css21/AnimatedImageCheck.cs
- scripts/gen_css21_pass_conditions.py
- Lite.Conformance/Css21/css21-pass-conditions.json and css21-pass-condition-reviews.json (sole writer)

## Opens with (parked findings)

- About 64 cases have a judge that cannot read the render, and about 48 have no route: WPT `manual`-kind copies, same-color pairs, an external reference origin.
- Timed renders: `floats-137`, `background-position-201` and `background-bg-pos-205`.
- `background-root-014`/`014a` need a resize protocol at a normal window height.
- Strict templates for manual-pending sentences that are machine-readable. The person-read residue goes to the approved-render ledger, which only a person approves.

## Coordination

- **Fail closed:** a judge change may only move a pass to unsupported or fail, never manufacture a pass. Every new check kind needs a unit test, and `PassConditions_EveryCheckKindHasJudgeAndEveryCaseHasARoute` must hold. Your case list is `judges.txt`.

## Handed in

- `border-{top,right,bottom,left}-style-007/008` (official+WPT, 16) and `border-style-rendering-001/002` (official+WPT, 4): engine renders groove/ridge green lines and boxes cleanly; judge splits the two touching shades of green into `2 top-level shapes below the instructions` (from 6.I).
- `border-style-003/004/007/008` (official) and `border-style-shorthand-002/003/004`
  (official+WPT): the dotted side of these was a painter bug, fixed in 6.I on 20 November —
  those sides now read `dotted`. What remains is thin `double` at 3px: 6.I now paints the two
  sub-lines as crisp 1px filled rows, but `Components` drops every component whose bbox is
  1px tall (`right == left || maxY == minY`), so the sub-lines vanish and the side reads
  `none`. Official `border-style-004/008` also have the pass-condition word `single` for the
  solid left side where the judge spells `solid` (from 6.I).
- `border-016/017/018` (official+WPT, 6), `border-{left,right}-016/017` (official+WPT, 8), `border-bottom-017` (official+WPT, 2), `border-top-017` (official, 1): engine renders nested dashed boxes and parallel pairs of dashed lines cleanly; judge expects a single box or merges parallel dashed lines into one non-straight cluster (from 6.I).
- `border-{top,right,bottom,left}-width-092` (official+WPT, 6) and `border-{top,bottom}-width-014` (official, 2): engine renders 1px/1pt thin solid lines cleanly; judge misses 1px-thin lines (`0 top-level shapes`) (from 6.I).
- `border-{top,right,bottom,left}-width-047` (WPT, 4): engine renders the short 50x8 green line (top/bottom) and 2 green lines (left/right) with no red; judge rejects `50x8` as not a line and expects 1 line instead of 2 on left/right (from 6.I).
- `border-{top,bottom}-applies-to-010`, `border-{top,bottom}-color-applies-to-010`, `border-{top,right,bottom,left}-style-applies-to-010` (official + WPT `border-bottom-style-applies-to-010`), `border-left-width-applies-to-010` (official+WPT) (11 total): engine renders both the border line and the list-item marker bullet cleanly; judge misses the bullet when vertically offset from a top/bottom border or fails to detect dashed lines (from 6.I).
- `outline-style-applies-to-008` (official+WPT, 2): engine renders the blue dotted outline around the inline box; judge treats the inner "Filler Text" glyphs as shapes not on the box perimeter (from 6.I).
- `clipping-017` (official+WPT, 2): `#test-outer` (`left:60px;top:60px`) and `#control-outer` (`margin:20px 0 0 200px`) sit side-by-side horizontally on the same band; judge expects 2 vertically stacked ink bands (`the two lines render identically: 1 ink bands`) (from 6.I).

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
