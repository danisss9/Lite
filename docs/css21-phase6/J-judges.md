# Lane J: judges and evidence (no engine changes) (batch 3)

Merges 6.M ([6M-judges.md](6M-judges.md)) and 6.X ([6X-evidence.md](6X-evidence.md)). Branch `css21/b3-j`, worktree `C:\Repos\Lite\.claude\worktrees\b3-j`.
Case list `Lite.Conformance/artifacts/lanes/J.txt` (pass list `J-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite.Conformance/Css21/VisualChecks.cs
- Lite.Conformance/Wpt/WptVisualPage.cs
- Lite.Conformance/Css21/SelfTestOracle.cs
- Lite.Conformance/Css21/AnimatedImageCheck.cs
- scripts/gen_css21_pass_conditions.py
- Lite.Conformance/Css21/css21-pass-conditions.json and css21-pass-condition-reviews.json (sole writer)
- Lite.Conformance/Css21/css21-applicability.json (sole writer, and only after the user rules)
- Lite.Conformance/Css21/OfficialCatalog.cs, scripts/restore-css21-*.py, the suite lock and catalog

## Opens with (parked findings)

Your list holds the failing cases whose render matches the sentence but whose check misreads it.
The splitter routes them here by name (`JUDGE_NAMES` in `scripts/split-css21-lanes.py`). The
diagnoses are in [6M-judges.md](6M-judges.md) under "Handed in". By the kind of judge fix:

- **Segmentation:**
  - touching same-colour 1px bars (`border-spacing-*`);
  - groove/ridge shades split in two;
  - nested dashed boxes, and parallel dashed lines merged into one;
  - dropped 1px/1pt lines (`*-width-092/014`);
  - the 50x8 line (`*-width-047`);
  - the `-applies-to-010` bullets beside borders;
  - 1px-tall `double` sub-lines.
- **Shape tests:**
  - the round-dot disc (`list-style-type-001` family): test corner emptiness instead;
  - `GradientLighter`'s hue band (`table-visual-layout-007/008`).
- **Container walks:**
  - `AlignCenter`'s parent walk (`text-align-bidi-002..004`);
  - `text-align-applies-to-007/012/013/015`. **Wait for lane Tb's table-width fix**: a judge-only
    change flips 12 passing siblings.
- **Text reads:**
  - two-line `PaintedOrder` (`direction-unicode-bidi-026..028`);
  - `painted-only` case and quote tokens (`content-142`, `quotes-applies-to-005/006`);
  - the needles of `content-008`, `content-counters-003` and `list-style-type-017`;
  - `hanging-indent` (`list-style-position-020/022`);
  - `text-band` needle targets (`c5518`/`c5520`);
  - the white caption (`table-height-algorithm-031/032`);
  - `outline-style-applies-to-008`;
  - `clipping-017`'s side-by-side bands;
  - `c5525-fltmult-000`, which needs `redScan: false` plus a two-region comparison.
- **Character category:** `first-letter-punctuation-184`. U+166D is `Po` in the suite's tables but
  `So` in .NET 8, so it needs a category override or a reviewed defect.

**Exclusion proposals.**
- A case that survives two batches in its engine lane comes to you with its diagnosis.
- Write a defective/optional proposal, with citation and replacement, for the user's ruling.
- Nothing goes into `css21-applicability.json` until the user rules.
- Already waiting: `s-11-1-1b-002/003/004` (defective upstream references), inline-svg (4 WPT cases)
  and `font-matching-rule-009`.

**Fail closed.**
- A judge change may only move a pass to unsupported or fail. It never manufactures a pass, unless the
  render is shown correct against the sentence.
- Every new check kind needs a unit test.
- `PassConditions_EveryCheckKindHasJudgeAndEveryCaseHasARoute` must hold.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
