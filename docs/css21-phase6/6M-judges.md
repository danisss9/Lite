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

## Edits outside owned files

- `Lite.Conformance/Wpt/WptRunner.cs`: the manual-pending dispatch (`IsManualPendingRoute`, two route checks and `EvidenceKind`). The route cannot be expressed from the owned files (the kind switch lives here); it is 13 lines and one concern.
- `Lite.Tests/Css21CoverageTests.cs`: the unit tests for every new route/check (the lane rules require them).
- `docs/css21-phase6/6D-positioning.md`, `6H-backgrounds.md`, `6K-cascade.md`: findings handed in, one section each.

## Progress

### 1. Reviewed WPT manual copies get a fail-closed route (commit `route manual copies`)

- **Moved:** the 36 WPT `manual`-kind copies that the catalog could not execute now run their official-case route. 31 move `unsupported` → `manualpending` (reviewed, awaiting native evidence):
  `background-attachment-004/005/008`, `background-position-201`, `background-root-014a`, `block-in-inline-004/005/006`,
  `c414-flt-ln-002`, `c543-txt-decor-000`, `floats-110`, `floats-137`, `content-178`, `inline-formatting-context-010b`,
  `inline-block-004/005`, `abspos-inline-001/002`, `active-selector-002`, `dom-hover-001/002`, `first-letter-selector-029`,
  `first-line-selector-017`, `link-visited-selector-002`, `white-space-mixed-004`, `outline-019`,
  `outline-no-relayout-001`, `overflow-007/008/009`, `visibility-collapse-001`.
- **Moved to fail (5, all audited by eye — the render is the verdict):** `background-attachment-006`/`007` (red
  background/border visible where the inner box/image should cover it), `containing-block-017`/`018` (large red box
  visible; the page's own condition is "no red"), `html-precedence-003` (`<font color=green>` paints red; the
  presentational attribute loses). Handed to 6.H, 6.D and 6.K respectively.
- **Mechanism:** `WptRunner.IsManualPendingRoute` sends a `manual`-kind `css/CSS2` case whose reviewed entry is
  `manualPending` and has no machine check through `SelfTestOracle`, which can only report `manualpending` (clean
  render) or `fail` (failure red) — never a pass. `dom-hover-001`'s at-rest red is by design (`#green { color: red }`,
  the condition is the hover-then-removal state), so its reviewed entry sets `redScan: false`; the at-rest scan is off
  and the case stays manual-pending.
- **Left:** the other 76 unsupported are judge reads (c527 font ~20, decoration 16, marker/alignment 10, same-color
  without an embedded pair 10, …); they are the next batch.
- **Readings:** units 648/648; css21 51 passed + 1 expected-fail; wpt 86 passed + 2 expected-fail; lane list
  1078 manualpending / 5 fail / 76 unsupported, 0 passes (before: 1047 / 0 / 112); canary 300/300.

