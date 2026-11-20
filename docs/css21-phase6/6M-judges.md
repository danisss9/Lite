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

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
