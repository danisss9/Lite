# Conformance status and next steps

This is the consolidated cross-track view of Lite's conformance program: where the
HTML 5.0, CSS 2.1, and ES2020 workstreams stand, and the order of work from here.
Each track's normative detail lives in its own document —
[html5-conformance.md](html5-conformance.md) with the plan in
[html5-conformance-plan.md](html5-conformance-plan.md), the CSS 2.1 plan in
[css21-conformance-plan.md](css21-conformance-plan.md), and
[es2020-conformance.md](es2020-conformance.md) — and nothing here changes a
completion contract or claims new passing coverage.

The current position and the end-to-end path to all three readiness flags are in
[Track status at a glance](#track-status-at-a-glance) and
[Roadmap to full conformance](#roadmap-to-full-conformance-written-11-november-2026)
(written 11 November 2026). The dated snapshot paragraphs below are the history that
led there.

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

Campaign update, 5 November (Wave 0 + four Wave 1 batches): the campaign's opening
baseline ran in an isolated worktree at `3e5fd1af` and banked the never-executed
3 November routing — **official 7,153/9,039 (79.1%), WPT `css/CSS2` 6,461/8,904
(72.6%): start count 13,614, +365 over the 2 November baseline** (official
unsupported 1,027 → 50; manual-pending now a real verdict class; four crashes
enumerated P1). The re-ranked Wave 0 queue (2,589 real fails + 509 unsupported):
WPT normal-flow 148, tables 147, generated-content 93, text 91, borders 86, fonts
73, floats 71, css1 69; official background 63, quotes 33, bidi-text 31,
border-conflict-style 27. Four engine/harness batches then landed cluster-verified
(`da598c36` cleared-`<br>` line-box clearance — border-conflict-style-107 flips;
`91c20484` the text-color-pairs zip bug + fail-closed stated colours — the
user-stylesheet crashes become honest verdicts; `35d5398e` the official userstyle
family's prerequisite sheet loads — official user-stylesheet 2/18 → 12/18;
`716b928a` reference-graph cycles close passing paths — block-in-inline-insert
3/41 → 38/41, normal-flow probe 696/842): roughly +90 verified conversions beyond
the baseline, with per-case guards showing zero regressions. The details are in the
two 5 November checkpoints in the CSS plan; the mid-campaign baseline re-ranks and
banks all of it.

Campaign update, 6 November (mid-campaign validating baseline at `8e433cab`):
**official 7,084/9,039 (78.4%), WPT `css/CSS2` 6,416/8,904 (72.1%): combined
13,500 — −114 net against the 13,614 start count**, with crashes 4 → 0 and
1,216 manual-pending verdicts now recorded inside runs (the HTML5 track's M3
static-review routing activating). The decline is fully attributed: the
campaign's five engine/harness batches banked ~+100 verified conversions
(reference-graph cycles +35 in block-in-inline-insert, the userstyle sheet
route +10, the quotes case-fold +2, the tables dynamic conversions +6, the
cleared-br ripple +7, the honest crash-route verdicts), and one cross-track
regression took ~−215 gross — the HTML5 track's "initial-parse comment
projection" (`6281ee0d`) renders HTML comments as text, and the official
suite is comment-dense (quotes-035's author comment paints over the page;
separated-border-model-007/008/009 the same shape). The owning track's
Parser/BoxEngine fixes were in flight while this baseline ran. The CSS plan's
6 November checkpoint carries the details and the re-ranked queue (tables
163, normal-flow 110 — the insert family's bank visible — generated-content
96, text 92, borders 86; the tables/character-encoding/quotes inflation is
the comment regression, not new engine findings).

Snapshot updated 5 November 2026 (second pass): **Milestone 1 of the HTML 5.0 track
closes — the applicability inventory is complete and `inventoryComplete` flips true
under the runner's guarded omission check.** The 4,641 unclassified catalog sources
(the 11-root remainder; the mechanical recount reproduces the check's own per-root
numbers at 6,631 cases) worked to zero in one session: static sweeps classified the
whole-directory and marker post-target families plus the canvas/print/worker-only
scope buckets with per-file reasons; the propose tool's 81 dependency-usage files got
their individual per-file decisions (infrastructure use returns to measurement — and
one rule correction against the 2014-07-10 DOM4 draft itself: `ParentNode.append`/
`prepend` are defined there at-risk, so those files re-queued for measurement);
289 manual-kind sources closed through the verified-gap/procedure-blocked static
route; and 3,811 unique catalog case paths executed across six sharded surveys on one
build identity — 741 pass, 2,775 reviewed-failing entries recorded with measured
failing-subtest evidence and the owning Milestone 4 package. The manifest now holds
**12,575 entries — 5,848 `included`, 5,281 `post-target`, 1,422 `profile-excluded`,
23 `regression-only`, 1 `mixed`, 0 `unreviewed`** — and `ProfileRunner` reads
0 `html5-unclassified-tests` and 0 `html5-unreviewed-test` blockers across all 16
candidate roots. Units 468/468; curated gates untouched (wpt 86 pass / 2 published
expected-fails, css21 51/1, acid 3/3); `html5ProfileReady` stays false by contract —
the 5,849 standing `html5-missing-applicable-test` blockers are the reviewed-failing
queue the Milestone 4 engine packages drain, with the REC section review (0/762) and
the Milestone 6 evidence pass behind them.

Snapshot updated 12 November 2026: the HTML5 track's first executed gate baseline exists and is
diagnostic, not banked. It reads **1,351/6,538 accepted (20.7%)** over **6,538 of the 7,837
expanded reviewed paths (83.4%)**, with the queue's largest clusters at `html/semantics`
386/2,098, `html/browsers` 56/588, `html/syntax` 112/401, `html/rendering` 196/452 and
`html/webappapis` 114/311. Three reasons keep it off the evidence shelf: the checkout is shared
with the CSS 2.1 track, so identity drifted four times mid-run (`Bidi.cs`, `BidiClassTable.cs`,
`BoxEngine.cs` and `Drawer.cs` written 16:22–17:23) and the `html/` and `selection/` phases that
finished their queues failed final verification; `editing/` dies about five minutes into its
queue under memory contention (5, 4 and 2 shards), and the one attempt that survived on an idle
machine was stopped at 600 of 729 paths to close the baseline; and the `encoding/legacy-mb`
family — 102 entries expanding to 1,196 one-thousand-subtest pages at ~3.9 GB peak each —
exceeds this machine's memory on any pass wide enough to finish it. Probes on the same build are
exact: tokenizer 7,052/7,053 token streams decided, parse gate 1,886/1,891 decided and **failing
by three open `tests19.dat` meta-charset cases** (the pipeline normalizes a declared `utf-8` label
to `ascii`), inventory 762 sections against 15,458 candidate cases. Stage 0's evidence reset
therefore now has exact counts for 83.4% of the reviewed queue and three named blockers instead of
an unmeasured gate; the tooling to re-bank it on a quiet tree is `scripts/run-html5-baseline.ps1`
plus `scripts/aggregate-html5-baseline.py`.

## Track status at a glance

Measured 11 November 2026. The CSS official number comes from that day's validating sweep and
the WPT number from the 9 November closing baseline. The HTML5 counts are a static recount of
`Wpt/html5-applicability.json` and the shared profile; no HTML5 run was executed for this
snapshot.

For this program, "100%" means three readiness flags are true on one identified build:
`es2020ProfileReady`, `css21ProfileReady` (screen scope) and `html5ProfileReady`. Every
applicable obligation must have passing evidence or a reviewed exclusion (permitted choice,
defective test, post-target). Expected failures, manual-pending, unsupported, timeouts and
crashes never count as success. `releaseReady` stays a separate final decision.

| Track | Readiness | Position |
| --- | --- | --- |
| ES2020 | **`es2020ProfileReady` true**, enforced by compatibility CI and NuGet release validation | 67,235/67,235 required Test262 executions, with zero failures, zero expected failures and no dependency exception. Host suite 20/20. All 2,115 ECMA-262 clauses reviewed. Maintenance only. |
| CSS 2.1 | `css21ProfileReady` false (screen-only scope; print removed 9 October) | Official **7,394/9,039 (81.8%)**. WPT `css/CSS2` ~6,552/8,904 (73.6%, 9 November). Obligations 18/213 `implemented`. Non-pass pool: ~2,371 real fails, 511 unsupported, **1,216 manual-pending**, 62 timeouts (the block-in-inline/hit-test hang cluster), 1 crash (`visudet/line-height-201`). The 966 official fails split into 460 twin-walk pixel diffs, 172 red-pixel engine bugs, 152 shape/box judge reads and ~180 mixed. |
| HTML5 | `html5ProfileReady` false | Milestones 1–3 closed (`inventoryComplete` true). The manifest has 12,575 entries and 5,848 are `included`. Of those, **~3,800 carry reviewed-failing reasons** and ~2,000 have a pass state unknown until the next run. Profile `html5.*`: 192 of 202 `untested`. Section review 0/762. Milestone 4 packages and increments landed 5–11 November, but **no cluster has been rerun and no manifest entries flipped** (evidence debt). The 12 November diagnostic baseline prices that debt: **1,351/6,538 accepted (20.7%)** over 83.4% of the expanded queue (`editing/` partial at 600 of 729 paths, `encoding/legacy-mb` blocked by memory, identity drifted by the parallel CSS track), so the flip review has per-cluster counts to work from; it is not banked evidence. |

The HTML5 reviewed-failing queue by area (static recount, 11 November):

| Area | Failing entries |
| --- | --- |
| `html/semantics` | 1,646: embedded-content 548, forms 487, scripting-1 356, document-metadata 86, links 30, tabular-data 28, selectors 28, … |
| `html/editing` | 269 (dnd 228) |
| `html/webappapis` | 251 (dynamic-markup-insertion 149, scripting 74) |
| `fetch` + `cors` + `url` | 287 |
| `html/syntax` | 199 (speculative-parsing 126, parsing 37, charset 22) |
| `html/dom` | 183 (elements 104, partial-updates 34, documents 30) |
| `dom/*` | nodes 156, events 129, ranges 51, collections 11, traversal 10 |
| Other dependency suites | encoding 112, selection 105, editing 66, uievents 63, FileAPI 55, cookies 40 |

## Roadmap to full conformance (written 11 November 2026)

This roadmap supersedes the 5–6 November one. The per-track documents keep their milestone
definitions and campaign checkpoints. This section is the single end-to-end path and its order.

### Stage 0: integration discipline and evidence reset (all tracks, first)

Two problems hold both tracks back:

- **Unbanked work.** The HTML5 packages and the CSS probe-read conversions are not in any baseline yet.
- **Cross-track breakage.** The comment-projection regression cost CSS about −215 cases, and HTML runs keep waiting for a quiet tree.

Steps:

1. **Per-track worktrees by default, plus an integration baseline.** Each track works in its
   own worktree, using the established isolated-worktree evidence route. After every landed
   batch pair, one build identity runs every gate:
   - unit;
   - curated css21 and wpt;
   - `--suite html5`;
   - html5-parse and html5-tokenizer;
   - `run-es2020.py --require-ready`;
   - the sharded CSS full baseline.

   The baseline also diffs every case against the previous integration identity. A
   cross-track regression goes to its owning track and is fixed before more feature work.
2. **Fix the build environment.** The machine lacks MinGW gcc, so `Lite.QuickJs/native`
   cannot recompile. The `LITE_SKIP_QUICKJS_NATIVE` csproj edit is uncommitted plumbing.
   Either restore the toolchain or commit a prebuilt-binary path that verifies its stamp.
   ES2020's guarantee depends on bridge evidence that can be rebuilt.
3. **Get the unit suite fully green.** Three tests fail: the two worker-bin tests
   (`WptWorker_ContainsLoadHangsAndRejectsChildReports`, `Worker_RecoversAfterHardTimeout`)
   and `Css21CoverageTests.VisualChecks_BoxCountsAndThreeBars`.
4. **Run the HTML5 evidence.** Run `--suite html5 --shard k/6` on one identity and merge the
   shards. Then run `scripts/review-m4-dom-authority-flip.py` and the matching flips for every
   landed package. The output is the exact per-entry pass/fail queue.
5. **Run a CSS validating baseline** on both suites to bank the 9–11 November probe reads.
   Put every non-pass in one bucket: engine, judge, reference, manual, timeout or crash.

Exit: both queues are exact, every gate is green on one identity, and the integration cadence is running.

### Cross-cutting keystones

Many clusters wait on these architectural changes, so schedule them early.

- **K1: script execution inside the parser.** Classic scripts currently run after the parse,
  by design. HTML 5.0 needs:
  - the tokenizer pausing at parser-inserted scripts;
  - the rule that style sheets block scripts;
  - async/defer ordering;
  - the insertion point;
  - nested `document.write`;
  - speculative parsing.

  K1 unblocks scripting-1 (356 entries) and speculative-parsing (126). It also unblocks the
  dynamic-markup residual (script_010–013, module writes, MutationObserver) and the two pinned
  `scripted_webkit01` runner-exclusions, which get unpinned once they pass. It delivers the
  parser-blocking work ES2020 scoped out. Primary areas: `Parser.cs`, the fork's
  `HtmlDomBuilder`, and the `JsDocument` write-region code.
- **K2: one DOM authority.** The AngleSharp mirror and the `LayoutNode` projection still
  diverge on the query path, the `querySelectorAll` backfill and frame document identity. The
  authoritative DOM must become the only source, with the projection derived purely from it.
  K2 unblocks dom/nodes, ranges, selection, editing (the execCommand undo journal) and
  MutationObserver.
- **K3: auxiliary browsing contexts.** This means `window.open` and a joint session history
  for frames. It is a prerequisite for much of `html/browsers` (browsing-the-web 211, windows
  56) and for several dynamic-markup and manual-tail residuals.
- **K4: one manual-evidence capture tool for both tracks.** CSS holds 1,216 manual-pending
  cases, and HTML needs native keyboard, editing, file and media evidence. The tool renders
  each case natively on Windows and takes a reviewer's verdict. It hashes the screenshot or
  recording into `ExecutionEvidence` against the build identity. It also accepts reviewed
  equivalent replacement tests. Neither profile can become ready without it.

### CSS 2.1 (screen) to `css21ProfileReady`

The per-batch loop stays the same:

1. Run the cluster.
2. Diagnose.
3. Fix the engine.
4. Add focused `Lite.Tests` pins.
5. Rerun the cluster, the curated gate and the units.
6. Diff every case, requiring 0 regressions.
7. Commit as `fix(...)` plus `docs(css21)`.

**C1. Crashes and hangs (P1).**
- The `line-height-201` NRE inside AngleSharp's `ComputeDeclarations`.
- The 62-page timeout cluster. Measure it before fixing.

**C2. Engine roots, largest first.**
- **Full UBA port (Wave 2).** Port `scripts/gen_css21_bidi_expectations.py` into the
  `RenderedText`/painted-order path. Targets: direction-unicode-bidi 22, bidi 16, WPT
  bidi-text ~31, the 56 char-order cases, and §8.6 split-box placement.
- **§4.4 stylesheet charset chain.** Targets at-charset 15 and character-encoding. Coordinate
  with the HTML encoding work.
- **Worker-vs-harness render divergence.** Targets content-attr 12, font-03x, and the counters
  XML-pipeline zeros (19).
- **Tables (WPT ~130):**
  - eager cell-content layout for column-visibility-004;
  - §9.5.2 clearance on internal table boxes;
  - the row-height metric under a specified table height;
  - the vertical-align: top spans.
- **WPT families:** normal-flow ~139, floats + floats-clear ~125, fonts 72, css1 68, text 68,
  selectors 61, generated-content 59, positioning 53, linebox 48, margin-padding-clear 48,
  borders 54.
- **Official families:**
  - background scroll-ink (24);
  - tile-boundary clipping (6);
  - background-root dynamic recalc;
  - font-family invalid-characters (fork error recovery).
- **The general inline-declaration stamp.**

**C3. Judge and oracle fidelity (Wave 3).**
- border-conflict-style thin double/dotted reads (27).
- The 152 shape/box reads.
- The look-same thresholds and overlapping bands.

Thresholds come from renders, and every judge change ships with a both-ways false-pass fixture.

**C4. The non-engine classes.**
- 511 unsupported (WPT backgrounds 228, css1 26): reviewed references or strict machine templates.
- 1,216 manual-pending: K4 recordings or reviewed replacements.
- Permitted-rendering and defective cases: reviewed `css21-applicability.json`
  classifications with provenance.

**C5. Obligations.** Flip the 195 `untested` obligations to `implemented` under the strict
whole-family rule. Start with the four near-misses (`css21.9.2.2.1`, `css21.10.3.1`,
`css21.11.1`, `css21.5.11.1`). Resolve Acid2.

**C6. Milestone 10 audit.**
1. Run the full screen matrix on one identity.
2. Add the gate-hygiene regressions.
3. Flip `css21ScreenReady` and `css21ProfileReady`.
4. Enforce `--require-css-ready` in CI and release validation.

### HTML 5.0 to `html5ProfileReady`

The Milestone 4 packages run in dependency order, on top of K1–K3. From now on, every package
closes with a cluster rerun and manifest flips, not unit tests alone.

| Order | Package | Queue it drains | Notes |
| --- | --- | --- | --- |
| H1 | DOM completion (on K2) | dom/nodes 156, dom/events 129, ranges 51, collections/traversal 21, html/dom 183, document-metadata 86 | createElementNS still fails 325 subtests and createElement 82. Also MutationObserver and reflection (`html/dom/elements` 104). |
| H2 | Scripting and parser (on K1) | scripting-1 356, speculative-parsing 126, dynamic-markup 149, webappapis/scripting 74, syntax parsing + charset 59 | Retires the parse gate's two runner-exclusions. |
| H3 | Browsing contexts and navigation (on K3) | `html/browsers` (561 included; exact state from Stage 0), focus | window.open, joint frame history, and frame document identity across open/close. |
| H4 | Forms | forms 487 (input-element 103 remaining) | valueAsDate, email IDN, color/number formatting, activation and event order, cloning steps, submission encodings, constraint validation, keygen. |
| H5 | Embedded content and media (2014 semantics) | embedded-content 548, rendering/replaced-elements 87 | img/iframe/object/embed loading and sizing; media ready/network states, MediaController/mediagroup, TextTrack; the video fixture gap. |
| H6 | Editing, selection and DnD | html/editing 269 (dnd 228), editing 263, selection 105 | The undo journal, drag-and-drop through testdriver, and K4 native evidence. |
| H7 | Dependency suites | fetch/cors/url 287, encoding 112, uievents 63, FileAPI 55, cookies 40, mimesniff 5 | Check each entry against the 2014 dependency snapshot first. Bindings to later specs move to `post-target` with reasons. |
| H8 | Rendering | html/rendering 446 (non-replaced 208, widgets 73, details 33), obsolete residue | UA stylesheet completeness. Shares layout and paint with CSS, so it is coordinated through the integration baseline. |
| H9 | Remaining webappapis | timers, animation frames, user prompts, system state, canvas residue | Small; folds into neighbouring packages. |

**H10. Milestone 5.**
- Run the 762-section backlog through `scripts/import-html5-sections.py` into atomic obligations.
- Map each obligation to a requirement plus tests, or give it a named reason.
- The 192 `untested` requirements shrink by whole-family flips.
- This is review work, so it runs in parallel with Milestone 4 from the start. It can surface
  obligations that no test covers.

**H11. Milestone 6.**
1. Collect identity-matched evidence for every requirement, K4 native recordings included.
2. Run the schema audit and check the gate-hygiene invariants.
3. Evaluate `html5ProfileReady`.

### ES2020: maintenance

`python scripts/run-es2020.py --require-ready` runs inside every integration baseline. Watch
K1 (script scheduling), QuickJS bridge changes and AngleSharp fork patches. Intl/ECMA-402,
public Web Workers and `document.write` reentrancy stay outside the contract. K1 delivers
parser-blocking execution for the HTML track.

### Sequencing

1. Stage 0. Nothing downstream is trustworthy until it closes.
2. In parallel, in per-track worktrees:
   - CSS: C1 → C2.
   - HTML: H1 on K2, then K1 → H2.
   - Throughout: K4 tooling and the Milestone 5 section review.
3. K3 → H3, with H4 and H5 in parallel, then H6 → H7.
4. H8 after CSS C2, because they share layout and paint. Then CSS C3 and C4.
5. CSS C5 and C6 → `css21ProfileReady`. HTML H10 and H11 → `html5ProfileReady`. The `releaseReady` decision comes last.

CSS is closest to a flip. HTML is the long pole, dominated by K1, embedded content and media,
and forms.

### Cross-track rules

- `releaseReady` stays a separate final decision, taken once the CSS and HTML gates settle.
- When tracks run in parallel, keep per-track file ownership. Each track edits only its own
  profile prefix (`html5.*`, `css21.*` or `es2020-*`) and never touches another track's
  suites, expected-failure lists or artifacts.
- Rebuild before collecting evidence, and never mix build identities in one evidence set.
- Readiness can be claimed only from full, unfiltered, single-identity evidence. It goes
  through `--require-css-ready`, `--require-es2020-ready` and the html5 profile evaluation.

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
