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

Snapshot updated 3 November 2026: the CSS 2.1 track's unsupported long tail is fully
routed — every cataloged case has a verdict route (twin reftest, machine check of the stated
pass condition, sufficient no-red scan/swatch, reviewed manual-pending, or a published
screen exclusion), enforced by a static audit in the unit suite. No conformance run was
executed for this checkpoint by decision; the 2 November baseline stands until the next
full-screen run converts the routes into verdicts. The HTML5 track's encoding-sniffer and
html5lib tokenizer work was in flight in the same checkout and has since landed — the
HTML5 row and the plan's 3 November checkpoint record the closed position — and the
track's remaining-work plan to 100% is now written (no run executed for it either);
per-track file ownership was respected throughout.

Snapshot updated 4 November 2026: the CSS 2.1 queue's #2 head is closed — the
bidi-box-model cluster runs 45/45 official and 45/45 WPT twins (from 18/45) on a
cluster-scoped run over work in progress on top of `3280b413`, and `css21.8.6` flipped
`untested` → `implemented` under the strict whole-family rule (18/213 total). Behind the
family: the §8.6 physical-side edge reservation for unsplit rtl inline boxes (the old drop
vanished their margins/padding from the line), the §8.6 mirrored join sides for
direction-rtl boxes split across lines (caught by the curated gate's rtl-span-only
regression, green again), and the §16.2/UAX #9 subset rendering an RTL `bidi-override`
scope reversed — six of the 28 `direction-unicode-bidi` cases now pass the painted-order
judge on the same capability. Suite numbers still stand at the 2 November baseline until
the next full-screen run; the 4 November checkpoint in the CSS plan carries the details.

Same-day follow-up (commit `8c3c9307`): the border-conflict-style family's 65
"unclassified" fails were a judge artifact — the ring verifier compared the center crop
against a whole-grid component bbox (collapsed same-color borders form one connected
component), excluding every side from the style vote while the engine's ring read the
stated style on all four sides. The judge now reads the ring's own extent: the family
converts 30 cases to real passes (71/106 official), the remainder carrying honest
per-case verdicts (thin double/dotted sample-swatch reads under the 40% band-row
threshold — queued oracle work — plus one real float-packing finding feeding the WPT
tables cluster from border-conflict-style-107). The wider `bidi-` space is surveyed and
measurable for the next session (full-UBA port from the char-order ledger's Python
reference leading).

## Track status at a glance

| Track | Readiness | Last recorded position |
| --- | --- | --- |
| ES2020 | **`es2020ProfileReady` true** — enforced by compatibility CI and NuGet release validation | 67,235/67,235 required Test262 executions, zero failures and zero expected failures, no dependency exception; host suite 20/20; all 2,115 ECMA-262 clauses reviewed |
| CSS 2.1 | `css21ProfileReady` false (screen-only scope; print removed 9 October) | 4 November, the bidi-box-model cluster closes and the border-conflict-style judge artifact clears (cluster-scoped runs on work in progress over `3280b413` + `8c3c9307`; full-suite numbers stand at the 2 November baseline: official 6,915/9,039, WPT 6,334/8,904): bidi-box-model 45/45 official and 45/45 WPT twins (from 18/45) — `css21.8.6` flipped under the strict rule, obligations 18/213 `implemented`; border-conflict-style 65 → 35 official fails as the ring judge reads the ring's own extent (30 honest conversions; residue = thin double/dotted swatch reads under the 40% band threshold + one float-packing engine finding for the tables cluster); behind it the §8.6 physical-side edge reservation, the §8.6 mirrored split-box join sides, and the §16.2/UAX #9 subset for RTL `bidi-override` scopes (6 of 28 `direction-unicode-bidi` now pass the painted-order judge); the 3 November routing invariant stands (every one of the 11,934 cataloged cases carries exactly one verdict route, unit-audited); unit 465/465, curated css21 51 pass / 1 published expected-fail, wpt 86 pass / 2 published expected-fails, html5 gate green over the 2,440-case reviewed corpus |
| HTML5 | `html5ProfileReady` false | 3 November: the harness-blocked tail is **closed — 0 unreviewed** — and Milestone 1's remaining work is quantified. All 109 tail entries decided (101 executed under the landed testdriver capability and its visual-runner route — 2 pass, 99 reviewed-failing; 8 manual-kind files by static reviewed-failing classification: no applicationCache, fixed `navigator.onLine`, no `window.open`). The propose tool's `needs/testdriver` inventory batch (481 sources) was then surveyed and added with measured outcomes (35 pass, 446 reviewed-failing) plus 9 cited post-target entries — the manifest now holds 7,923 entries with 1,826 `included`, and the runner's guarded omission check reports the inventory-completion remainder precisely: **6,691 catalog cases across 11 roots with no manifest entry** (html/semantics dominating at 2,162; encoding 1,265; fetch 856; dom 378) — `inventoryComplete` stays false by the mechanical condition, and the unproposed sources find their classifications through the section review. Engine fixes landed with the testdriver capability: text inputs size 150×20, trusted form-control clicks, interaction focus mirroring `element.focus()`, `elementsFromPoint`/`getClientRects`/`scrollIntoView`/`document.contains`. Units 458/458; curated wpt 86/2 and css21 51/1 unchanged; parse fidelity 1,889/1,891, tokenizer 7,052/7,052; REC section review 0/762 |

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
   **Closed 3 November: the 109-entry tail is 0.** The checkpoint in
   [html5-conformance-plan.md](html5-conformance-plan.md) records the mechanism
   and the batches: 101 entries executed under the landed capabilities (2 pass,
   99 reviewed-failing on the Milestone 4 queue) and the 8 manual-kind files
   classified by static reviewed-failing review (the manual-evidence recordings
   remain the Milestone 6 evidence step). **Milestone 1's remaining work is
   inventory completion**: the propose tool's testdriver inventory batch (481
   sources, 35 pass / 446 reviewed-failing) and the cited post-target batches
   are in the manifest, and the runner's guarded omission check quantifies the
   remainder at **6,691 unclassified catalog cases across 11 roots** — worked to
   zero through the propose batches and the section review before
   `inventoryComplete` flips.
2. **Milestone 2's measured corpus work is complete** (3 November): the tree-construction corpus is
   **closed under the contract** — every case is a raw pass, a pipeline-fixes
   artifact, a reviewed ledger-pass correction (`Wpt/html5-parse-expectations.json`,
   regenerated by `scripts/gen_html5_parse_expectations.py`, reviewed entries never
   overwritten), or a pinned runner-exclusion (the two scripted_webkit01 streaming
   document.write cases; the pipeline executes classic scripts post-parse by design,
   and the streaming semantics live on the frame-level write path). Fork patches
   012–014 (in-select anything-else, the in-cell HTML-cell pop, after-frameset
   whitespace) carried 20 cases to the letter; the .dat loader now decodes the
   format's `\uXXXX` escapes. The tokenizer corpus is vendored and gated —
   `--suite html5-tokenizer` decides **7,052/7,052** case/state runs (+1 pinned
   U+FFFF runner-exclusion) through fork patches 015–019 and a 30-entry reviewed
   ledger; encoding determination (§8.2.2.1 — BOM, transport charset, prescan,
   default, late restart) is wired into the load path; the serialization
   residuals are closed (innerHTML/outerHTML omit the injected origin sheets at
   every depth). What remains of Milestone 2 is the **serving integration** the
   WPT encoding suite needs — Milestone 3 work, sequenced with the rest of the
   remaining plan in [html5-conformance-plan.md](html5-conformance-plan.md).
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
