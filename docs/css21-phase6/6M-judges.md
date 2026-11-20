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

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- table-height-algorithm-031/032 (from 6.E): the engine now renders the assert correctly (the two
  visible "Filler Text" bottoms share the row baseline; the caption is intentionally invisible
  via `color: white`), but the selftest judge still reports "a filler text painted no ink" — it
  appears to count the intentionally-white caption as a missing text. Needs a verdict route that
  accepts an explicitly-white filler text.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
