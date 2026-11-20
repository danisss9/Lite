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

- `list-style-type-001`, `list-style-applies-to-005/006`, `list-style-type-applies-to-005/006`
  (official + WPT, from 6.F): `round-dot` rejects a 16px disc. The render is a filled circle 7-8px
  across including antialiasing, but `Components`' 5x5 ink bbox has every bbox edge inked, so
  `Rectangular` is true. A larger disc would pass here but break the `<=24px` bullet limit of
  `list-style-position-003/008/009` at 85px, so the two checks have no common marker size.
  Suggest testing corner emptiness (a circle's top row is narrower than its bbox) instead.
- `content-008` (from 6.F): the pass-condition needle is `"1."` but the sentence and test ask for
  the square bullet (`counter(test, square)`); the generated check does not match the sentence.
- `content-142` (from 6.F): `painted-only` compares the painted "PASS PASS" case-sensitively
  against the lowercase needles `["pass","pass"]` and reports the correct text as non-allowed.
- `quotes-applies-to-005/006` (from 6.F): `painted-only` with no needles reports the rendered
  quote glyphs as non-allowed tokens although the sentence only asks for quotes.
- `content-counters-003` (from 6.F): the two lines are pixel-identical (small hollow-circle
  counters) but `look-same` finds 0 ink bands below the instructions.
- `list-style-type-017` (official only, from 6.F): the twin reftest passes but the self-describing
  check reports "8 ink bands below the instructions".
- `list-style-position-020/022` (official + WPT, from 6.F): `hanging-indent` finds one text band
  instead of two because the blue border of the box encloses both text lines and its component
  spans them; the render itself has the second line shifted left under the bullet (the intended
  result). Suggest ignoring a band whose component is the box border, or grouping text
  components only.

- `text-align-applies-to-007`, `012`, `013`, `015` (official + WPT) — filler text is flush with the table/body content box (`x=8` left / `x=792` right with 8px body margin), but the judge compares against viewport edges `0` / `800`. — from 6.A
_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- table-height-algorithm-031/032 (from 6.E): the engine now renders the assert correctly (the two
  visible "Filler Text" bottoms share the row baseline; the caption is intentionally invisible
  via `color: white`), but the selftest judge still reports "a filler text painted no ink" — it
  appears to count the intentionally-white caption as a missing text. Needs a verdict route that
  accepts an explicitly-white filler text.
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
