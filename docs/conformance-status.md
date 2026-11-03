# Conformance status and next steps

This is the consolidated cross-track view of Lite's conformance program: where the
HTML 5.0, CSS 2.1, and ES2020 workstreams stand, and the order of work from here.
Each track's normative detail lives in its own document —
[html5-conformance.md](html5-conformance.md) with the plan in
[html5-conformance-plan.md](html5-conformance-plan.md), the CSS 2.1 plan in
[css21-conformance-plan.md](css21-conformance-plan.md), and
[es2020-conformance.md](es2020-conformance.md) — and nothing here changes a
completion contract or claims new passing coverage.

Snapshot written 2 November 2026 on build `2b978d0` after the second probe batch
and its same-day engine fixes: the stated pass condition of 4,012 self-describing
CSS 2.1 cases is now machine-checked (fifty-odd sentence templates and their
judges, up from twenty-seven), and the fresh full-screen baseline reads the
official suite at 6,915/9,039 (76.5%, from 75.3% on 1 November) and WPT `css/CSS2`
at 6,334/8,904 (71.1%, from 69.8%). Of the batch's 1,978 formerly-unsupported,
twin-less targets, 618 now carry verdicts — 358 pass, 260 are real engine findings
queued below — and the engine fixed what the judges surfaced: inside list markers
consume first-line width (§12.5.1), the position-first `list-style` shorthand
survives AngleSharp's dropped declaration, column-spanning cells size their
columns (§17.5.2.1), cell sub/super/text-top/text-bottom read as baseline
(§17.5.3), the 28-case outline-overlap family is decided by the corrected
stated-condition judge, and the decoration reads no longer misread merged lines.
Unit suite 428/428; curated gates css21 51 pass / 1 published expected-fail, wpt
86 pass / 2 published expected-fails, html5 46/46, acid 3/3. Obligations stand at
17/213 `implemented` after the 2 November strict flip pass (css21.9.9 newly
clears its full family).

## Track status at a glance

| Track | Readiness | Last recorded position |
| --- | --- | --- |
| ES2020 | **`es2020ProfileReady` true** — enforced by compatibility CI and NuGet release validation | 67,235/67,235 required Test262 executions, zero failures and zero expected failures, no dependency exception; host suite 20/20; all 2,115 ECMA-262 clauses reviewed |
| CSS 2.1 | `css21ProfileReady` false (screen-only scope; print removed 9 October) | 2 November, second probe batch (`745f8b6`) plus two engine-fix batches (`37f05dc`, `2b978d0`) with a fresh full-screen baseline: official 2011 suite 6,915/9,039 (76.5%), 1,097 fail, 1,027 unsupported pending references or manual evidence; WPT `css/CSS2` 6,334/8,904 (71.1%), 1,377 fail, 1,180 unsupported; the stated pass condition of 4,012 self-describing cases is machine-checked (618 formerly-unsupported targets decided in this baseline: 358 pass, 260 real engine findings); applicability review complete (18,654 cases); obligations 17/213 `implemented` |
| HTML5 | `html5ProfileReady` false | Reviewed gate now spans 1,392 tests (153 passing — the applicability review's newly-tracked obligations form the Milestone 4 engine queue), curated wpt 86 pass / 2 published expected-fails, unit 432/432; html5lib parse fidelity **decided 1889/1,891 unique tree-construction cases** (1,746 raw pass + 2 pipeline-fixes artifacts + 141 reviewed ledger-pass corrections, +2 runner-excluded scripted document.write cases) — enforced by the `--suite html5-parse` gate in CI since 2 November (`198c9fc`); applicability manifest 7,432 entries (1,235 `included`, 1 `mixed`, 4,773 `post-target`, 1,291 `profile-excluded`, 23 `regression-only`, 109 `unreviewed` — each naming its pending Milestone 3 capability); REC section review 0/762; tokenizer corpus vendored, harness pending |

## Roadmap

### Lead: CSS 2.1 completion

Closest to a readiness flip. The work runs the established loop per cluster —
run the failing cluster, implement the behavior, add focused regression coverage,
rerun the cluster plus the curated 52-entry gate and the unit suite — never closing
a package from a sample.

1. **Work the official-suite queue cluster by cluster**, re-ranked by the 2 November
   full-screen baseline (worst clusters by real failure count, not pass rate, so
   unsupported singletons do not crowd the queue). Expected heads: official
   border-conflict-style (65 fails — the conflict winner read honestly through the
   sample oracle), bidi-box-model (27), quotes (22), content-counters (18),
   text-transform-bicameral (18), bidi (16), margin-collapse (16), background-root
   (14), floats (14), table-height-algorithm (14 — the cell vertical-align reading);
   WPT tables (184), normal-flow (143), text (116), generated-content (78), borders
   (75), floats (74), css1 (67), fonts (66), bidi-text (65), floats-clear (64). The
   probe batch's own engine findings were fixed same day (spanning-cell column
   widths, cell sub/super/text values as baseline, the outline-overlap judge); the
   still-queued heads are the row-height metric under a specified table height
   (004/009/010), 4px vertical-align: top spans (012-014), two no-ink fillers
   (031/032), and outside markers above bars. The unsupported long tail (official
   background 213, direction-unicode-bidi 28, cursor 23; WPT backgrounds 253, tables
   145, css1 92, ui 79, borders 63) needs reviewed references or manual evidence —
   feed the oracle/reference work alongside the engine queue. The 2 November probe
   pass already machine-checks the stated pass condition of 4,012 self-describing
   cases; extend the templates only where the sentence has a strict machine reading.
2. **Keep the obligation inventory honest as clusters close**: the 31 October pass
   flipped 15 obligations under the strict rule — every applicable test mapped to
   the obligation passes, official and WPT alike, mapped by their official paths in
   the evidence-record form (`css21-official/` prefix) because `css21-wpt` evidence
   requires the upstream-wpt serving environment. Four near-misses stayed `untested`
   because their WPT twins still fail (`css21.9.2.2.1`, `css21.10.3.1`,
   `css21.11.1`, `css21.5.11.1`). Flip only whole families; never from a sample.
3. **Grow the WPT `css/CSS2` evidence**: `visual` tests now execute through the
   suite's self-describing no-failure-red route, so the parked 44.9% corpus can be
   re-run and the remaining real failures and crashes re-classified
   (later-feature, defective, or reviewed-failing engine gaps feeding the queue).
4. **Phase 6 final audit**: run the complete screen matrix against one identified
   build, merge shards, publish obligation coverage separately from suite pass
   rates, add the CSS gate-hygiene regressions, and only then evaluate
   `css21ScreenReady`/`css21ProfileReady` and enforce `--require-css-ready` in CI
   and release validation. Print stays out of scope (`css21PrintReady` false).

### HTML5 follows

In the delivery order of [html5-conformance-plan.md](html5-conformance-plan.md):

1. **Classify the harness-blocked applicability tail (109 entries)**: the 2026-11-02
   root reviews closed the `dom` stragglers (20 reviewed-failing obligations now
   feed the Milestone 4 engine queue as `included` entries with standing evidence
   blockers) and classified the `editing` and `html` roots (directory-coherent
   feature analysis with representative spot-checks). The remaining 109
   `unreviewed` entries each carry a reason naming the Milestone 3 capability
   their classification waits on — testdriver input automation (66 editing, 30
   html), HTTPS upstream serving (13), or the manual-evidence flow — and the
   dependency roots the scope decision already covers (`uievents`, `url`,
   `encoding`, `webstorage`, `webmessaging`, `cookies`, `fetch`/`cors`,
   `FileAPI`, `selection`, `mimesniff`) were bulk-classified in earlier sweeps.
   The tail closes when Milestone 3 lands and the files execute.
2. **Close Milestone 2's parse residuals**: the tree-construction corpus is
   **closed under the contract** — every case is a raw pass, a pipeline-fixes
   artifact, a reviewed ledger-pass correction (`Wpt/html5-parse-expectations.json`,
   regenerated by `scripts/gen_html5_parse_expectations.py`, reviewed entries never
   overwritten), or a pinned runner-exclusion (the two scripted_webkit01 streaming
   document.write cases; the pipeline executes classic scripts post-parse by design,
   and the streaming semantics live on the frame-level write path). Fork patches
   012–014 (in-select anything-else, the in-cell HTML-cell pop, after-frameset
   whitespace) carried 20 cases to the letter; the .dat loader now decodes the
   format's `\uXXXX` escapes. Remaining: the html5lib-tests **tokenizer corpus**
   (15 `.test` JSON files upstream — expected token streams plus ~80 spec error
   codes mapped onto the engine's `HtmlParseError` set, with initial-state dispatch;
   needs a new `html5lib-tests` locked-suite entry), then encoding sniff/restart
   and the serialization residuals (innerHTML/outerHTML still surfacing the
   injected origin sheets).
3. **Milestone 3 harness automation before the deep `html` root**: testdriver input
   automation over the native/headless hosts, `.any.js`/`.worker.js` worker
   contexts, upstream serving integration, reftest fuzzy/DPI metadata, recorded
   manual evidence, and historical fixtures for removed 2014 features
   (application cache, keygen). After this lands, every non-pass on a reviewed
   applicable test is a genuine engine failure.
4. **Milestone 4 engine packages in dependency order**: DOM authority first (it
   unblocks live collections, serialization fidelity and the editing package),
   then events and messaging (postMessage targetOrigin/transfer, structured
   clone), origins and sandbox, cross-document history, top-level dynamic markup
   replacement and the streaming insertion point, cookies, navigator/capabilities,
   Canvas 2D scope, forms (keygen and per-state coverage), 2014 media semantics,
   editing and selection, obsolete-element processing and rendering.
5. **Milestones 5–6 last**: work the 762-section backlog into atomic obligations
   until `inventoryComplete`, collect identity-matched evidence for every
   applicable requirement (including native Windows keyboard/editing/file/media
   evidence), and only then re-evaluate `html5ProfileReady`.

### ES2020: maintenance only

`es2020ProfileReady` is true and enforced. No conformance work is planned; the
obligation is to keep `python scripts/run-es2020.py --require-ready` green through
engine churn (AngleSharp fork work, QuickJS bridge changes) — any execution
regression flips readiness false and fails those gates automatically. Intl/ECMA-402,
public Web Workers, parser-blocking execution, and `document.write` reentrancy
remain separate workstreams outside the ES2020 contract.

### Cross-track

- `releaseReady` stays a separate final decision taken once the CSS and HTML gates
  settle; a CSS completion alone does not imply it.
- Respect the per-track file ownership when tracks run in parallel (only
  `html5.*` / `css21.*` / `es2020-*` profile prefixes per track; never touch
  another track's suites, expected-failure lists, or artifacts), rebuild before
  collecting evidence, and never mix build identities in one evidence set.
- Hygiene: the root `compatibility-report.json` (git-ignored, regenerated per
  snapshot) was refreshed from the 31 October flip-identity profile run — the
  on-disk copy had predated the 9 October print scope decision and contradicted
  the docs.

## Refreshing the numbers

```powershell
./scripts/fetch-tests.ps1 -IncludeCss21Official
./scripts/build-wpt-manifest.ps1
dotnet build Lite.sln -c Release

# Fast gates
dotnet run --project Lite.Tests -c Release --no-build -- --report Lite.Conformance/artifacts/unit-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite css21
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html5 --report Lite.Conformance/artifacts/html5-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite wpt --report Lite.Conformance/artifacts/wpt-results.json

# CSS 2.1 full screen baseline (calibrate shard wall time first, then aggregate)
./scripts/run-css21-baseline.ps1
python scripts/aggregate-css21-baseline.py

# Combined verdict
dotnet run --project Lite.Conformance -c Release --no-build -- --suite profile --evidence Lite.Conformance/artifacts/unit-results.json --evidence Lite.Conformance/artifacts/html5-results.json --evidence Lite.Conformance/artifacts/wpt-results.json

# ES2020 enforcement (also runs as compatibility CI)
python scripts/run-es2020.py --require-ready
```

Evidence collection requires a quiet tree on one build identity; keep reports in
ignored `Lite.Conformance/artifacts/`. A CSS baseline needs an isolated worktree
when other tracks hold in-flight edits, because case identity hashes every
git-visible file.
