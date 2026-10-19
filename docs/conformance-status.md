# Conformance status and next steps

This is the consolidated cross-track view of Lite's conformance program: where the
HTML 5.0, CSS 2.1, and ES2020 workstreams stand, and the order of work from here.
Each track's normative detail lives in its own document —
[html5-conformance.md](html5-conformance.md) with the plan in
[html5-conformance-plan.md](html5-conformance-plan.md), the CSS 2.1 plan in
[css21-conformance-plan.md](css21-conformance-plan.md), and
[es2020-conformance.md](es2020-conformance.md) — and nothing here changes a
completion contract or claims new passing coverage.

Snapshot written 18 October 2026 and refreshed the same day after the first CSS 2.1
implementation pass of the roadmap below (commits `18e883d` + `78f8be0`): the official
margin-collapse family is closed at 110/110 (the `css21.8.3.1` obligation is now
`implemented`), table-anonymous-objects recovered its six regressed white-space cases to
198/204, border-conflict rose to 350/353 with the col/colgroup conflict package, and
character-encoding closed at 37/37. Unit suite 360/360; curated gates css21 52/52, wpt
86 pass / 2 published expected-fails, html5 46/46. The refreshed full-screen baseline on
this build identity supersedes the Oct 12 numbers below once it aggregates into
`Lite.Conformance/artifacts/css21-full-screen-baseline.md`.

## Track status at a glance

| Track | Readiness | Last recorded position |
| --- | --- | --- |
| ES2020 | **`es2020ProfileReady` true** — enforced by compatibility CI and NuGet release validation | 67,235/67,235 required Test262 executions, zero failures and zero expected failures, no dependency exception; host suite 20/20; all 2,115 ECMA-262 clauses reviewed |
| CSS 2.1 | `css21ProfileReady` false (screen-only scope; print removed 9 October) | Official 2011 suite 8,543/9,039 (94.6%), 494 real failures, 2 unsupported, 0 crashes; WPT `css/CSS2` 44.9% and parked, with `visual` tests now executing through the self-describing route; applicability review complete (18,654 cases); the 213-record obligation inventory still reads `untested` |
| HTML5 | `html5ProfileReady` false | Reviewed gate 46/46, curated wpt 86 pass / 2 published expected-fails, unit 341/341; html5lib parse fidelity at 92.0% engine-pass (1,740/1,891 unique cases); applicability manifest 3,984 entries (51 `included`, 1 `mixed`, 3,885 `post-target`, 23 `regression-only`, 24 `unreviewed`); REC section review 0/762 |

## Roadmap

### Lead: CSS 2.1 completion

Closest to a readiness flip. The work runs the established loop per cluster —
run the failing cluster, implement the behavior, add focused regression coverage,
rerun the cluster plus the curated 52-entry gate and the unit suite — never closing
a package from a sample.

1. **Work the official-suite queue cluster by cluster**, re-ranked against a fresh
   full-screen baseline (the Oct 12 queue is stale by ~7 engine commits). Expected
   heads from that baseline: the table-anonymous-objects remainder (053/054 — the
   2px anonymous-inline-table baseline fringe; 134 — caption hoisting; 204), the
   border-conflict-element/style families, character-encoding/at-charset (now real
   encoding gaps since the support-subtree restore), background-intrinsic (SVG
   intrinsic sizes), font-family-name, the margin-collapse negative-chain case 162,
   floats, overflow-applies-to, and white-space-normal, then the long tail.
2. **Keep the obligation inventory honest as clusters close**: flip the matching
   `css21-requirements.json` records `untested` → `implemented` with their mapped
   passing tests. These flips currently lag the engine work; Milestone 2's
   bookkeeping must catch up before any readiness computation means anything.
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

1. **Finish the applicability review's small roots**: close the 24 `unreviewed` dom
   stragglers (20 reviewed-failing entries feeding the Milestone 4 engine queue,
   4 reviewed-deferred whose mixed-era member reads cannot be split at subtest
   level), then the dependency roots in batch order (`uievents`, `url`, `encoding`,
   `webstorage`, `webmessaging`, `cookies`, `fetch`/`cors`, `FileAPI`,
   `selection`, `mimesniff`), then the large `html` root by subdirectory. The
   manifest's 3,885 `post-target` classifications show the catalog import ran; the
   review protocol has been demonstrated end to end on `dom` and must now scale.
2. **Close Milestone 2's parse residuals**: the next AngleSharp fork patches
   (the math/svg tr-context stray-`<tr>` insertion trace, the menuitem formatting
   residual, per-case triage of the remaining genuine deviations), then encoding
   sniff/restart and the serialization residuals (innerHTML/outerHTML still
   surfacing the injected origin sheets).
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
- Hygiene: regenerate the committed root `compatibility-report.json` from a current
  profile run — the checked-in copy still predates the 9 October print scope
  decision and contradicts the docs.

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
