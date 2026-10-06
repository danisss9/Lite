# Full CSS 2.1 coverage plan

Planning baseline: 17 September 2026. Scope: Windows x64, screen and print, retaining Lite's C#/AngleSharp/Jint/Skia architecture. This document is an implementation plan; it does not change the active compatibility profile or claim that any new coverage has passed.

Execution status update, 5 October 2026: the Milestone 1 and 2 scaffolding described below now exists — `Profile/css21-sections.json` (841 sections), `Profile/css21-properties.json` (115 properties), `Profile/css21-requirements.json`, `Css21/css21-applicability.json`, the vendored and hash-pinned 2011-03-23 official suite (9,364 cataloged cases), the `--suite css21-inventory` and `--suite css21-full --media` commands, the `--require-css-ready` gate flag, and `scripts/run-css21-baseline.ps1` with `scripts/aggregate-css21-baseline.py`. The phased remaining-work breakdown at the end of this document sequences the execution; the milestones above remain the normative definition of each package.

Execution progress, 5 October 2026 (Phase 4 screen packages under way): Phase 3 foundations landed — XHTML MIME dispatch (documents served as XML parse with the XML parser, `.xht` reftests execute; the known gap is AngleSharp's case-insensitive cascade type matching), §4.4 BOM-less UTF-16 `@charset` decoding, the spaced `! important` bang normalization (AngleSharp's declaration parser drops the whitespace form entirely; with declarations surviving, AngleSharp's own cascade handles the §6.4.1/§6.4.3 precedence correctly), the missing-input guard (812 archive-gap cases reported as honest `Missing suite input` blockers), and the stylesheet origins batch: origin-tagged rules ordered by the five §6.4.1 tiers in the dynamic resolver, UA and user sheets written into the document source (dynamic insertion appends after parse-time sheets and inverted the computed cascade), the user sheet's !important half injected after the author sheets (§6.4.1 puts user !important above author !important), sheet suspension for `style.disabled`, host author-disable and preferred alternate set selection (the four `css21.ua.*` conformance controls), plus origin injection that never precedes a doctype. Known limit, published as the failing `css21.dynamic-recascade` profile requirement: the AngleSharp style store keeps parse-time computed declarations per element with no invalidation path, so a suspended author-!important rule still wins on existing elements until a store-aware recascade lands (waived in the curated manifest with the same reason). The refreshed screen baseline at source `13e3b2d` reports the official suite at 82.4% (7,452/9,039, 775 real failures — led by table anonymous objects 66, vertical-align 49, encoding/charset support-file cases 27, padding-right 25, floats 24, font-family-name 22 — and 812 missing-input blockers) and the WPT corpus at 41.4% passing (3,706 cases; 2,214 real failures, 387 crashes, 2,635 unsupported: the visual-kind harness gap and missing inputs). The border-conflict clusters (~195) and the spaced-bang color families moved out of the failing set entirely.

Second evidence checkpoint, 6 October late (source `477b973`): the isolated render workers were found rendering without the vendored test fonts (they reuse the parent's server URL and never run Start), so every worker-rendered selftest compared fallback-font glyphs against Ahem-based expectations. Test-font provisioning moved to an idempotent entry point that both Start and the worker entry call. The refreshed baseline reports the official suite at 83.7% (7,569/9,039; 658 real failures, down from 760): vertical-align 49 -> 31 (the shifted-glyph fringes were the missing worker fonts), floats left the top-failing list entirely (the 1xx family 29 -> 42 passing), margin-collapse 18 -> 15. WPT holds at 41.9% with 124 crashes. The remaining queue: table anonymous objects (65: the infer-cells and dynamic families), vertical-align keyword fringes (31), at-charset support-file recoveries (27), RTL layout (padding-right 25 + the bidi corpus), block-in-inline insertion (19), color/border-color swatch families (~70 across the four border-*-color clusters plus color, mostly missing swatch inputs), font-family-name pixel triage (14, fonts provisioned).

Diagnosis note for the table-anonymous-objects white-space band (~35 of the 65, Gecko-derived dynamic-removal tests 167-206): after onload removes the display:table-cell spans, the whitespace #text nodes around the removal sites lay out as ~45px block-like lines inside the absolutely-positioned green overlay (its box grows 32 -> 92 tall and its text lands ~46px below the red static text it must cover). The text nodes carry display:inline overrides and the block-in-inline normalizer's character-data guard is correct, so the defect sits in the abs-pos children path's whitespace/anonymous handling after dynamic removal - the next investigation entry point.

Evidence checkpoint, 6 October (source `ffd4d6b`, 24 commits over the 5 October baseline): the official suite stands at 82.6% passing (7,467/9,039; 760 real failures, down from 974, zero crashes), the WPT corpus at 42.0% (3,760 passing; crashes down from 387 to 124; the reference-target guard reclassified a further 264 citation cases as honest missing-input blockers). The day's engine batches — collapsed-border conflicts, anonymous-table runs and intrinsic inline-run summing, spaced-bang normalization, stylesheet origins with the five cascade tiers and the four user controls, the store-aware recascade, vertical-align length/percentage offsets, the root-BFC body-margin fix, §17.5.3 cell vertical alignment, the :first-line float exclusion, test-font provisioning, and the reference-target guard — are all reflected. The remaining queue: table anonymous objects (65: the infer-cells and dynamic families), vertical-align keyword fringes and the strut rounding behind the shifted-line cases (49), at-charset support-file recoveries (27), RTL layout (the padding-right family, 25, which also gates the bidi corpus), floats variants (24), font-family-name pixel triage (22, fonts now provisioned), absolute-non-replaced widths (19), block-in-inline insertion (19), and margin-collapse (18).

Later the same day (source `9086c8c`): the store-aware recascade landed — suspending a sheet snapshots its rules and the recascade retracts exactly the properties they set through the node override channel, bypassing the frozen style store, so `css21.dynamic-recascade` is now `implemented` and table-anonymous-objects-015 passes unwaived (curated gate 52/52 with zero waivers). Vertical-align <length>/<percentage> offsets (§10.8.1) parse and shift boxes in both the line measurement and placement phases (the length cases' pixel deltas halved; the remaining ~30px fringes are half-leading/strut rounding, distinct from the offsets). And the document root is now a BFC boundary — BODY top margins position content down the canvas instead of collapsing through the root and escaping above the viewport (padding-applies-to-016/017 pass). Remaining diagnosed clusters: RTL box painting (the padding-right family needs genuine RTL layout, which also gates the bidi-text corpus), the anonymous-objects infer-cells and dynamic families, inline vertical padding paint, vertical-align sub-pixel fringes and the table-cell baseline reftests, floats, font matching, abspos widths, and margin-collapse.

## Target and completion contract

Keep the existing [CSS 2.1 Recommendation of 7 June 2011](https://www.w3.org/TR/2011/REC-CSS2-20110607/) as the normative target. Store checksummed copies of its source and a dated copy of the [errata](https://www.w3.org/Style/css2-updates/REC-CSS2-20110607-errata.html). The errata page has Working Draft status: review corrections individually, record their applicability and test impact, and do not silently replace the target with CSS 2.2 or later modules. Where an adopted correction changes the base Recommendation, report that compatibility decision explicitly.

The requested scope includes screen and print rendering, supported HTML and XHTML document modes, dynamic restyling, and CSS user-agent controls. CSS conformance is media-specific; report screen completion separately while print is under development. The [UA conformance requirements](https://www.w3.org/TR/2011/REC-CSS2-20110607/conform.html#conformance) include alternate stylesheet selection, disabling author styles, and allowing a user stylesheet file. These remain required even though the existing HTML profile excludes browser chrome.

[Appendix A](https://www.w3.org/TR/2011/REC-CSS2-20110607/aural.html) is informative and does not require a speech renderer. Classify every section and appendix by its actual normative status, including the normative stacking rules in Appendix E. The grammar appendix is informative; parsing obligations come from the normative syntax, selector, and property definitions. Keep modern extensions such as flexbox, custom properties, `rem`, and `initial`/`unset` in their existing regression coverage without counting them as CSS 2.1 requirements. Record cases where newer specifications intentionally differ from the pinned target.

Completion means all applicable obligations are inventoried, implemented, and supported by passing evidence. It does not mean a selected test manifest is green or that every possible input has been tested. Document specification-permitted choices and undefined behavior; do not invent a single required rendering where the specification allows alternatives.

## Verified starting point

This baseline comes from source inspection, not a fresh execution of the test suites:

- `Css21/css21-manifest.txt` contains 52 curated entries, mixing local regressions and upstream tests.
- The active profile marks broad CSS sections untested, excludes paged media, and declares `cssMedia: screen`; its schema also fixes that value. Acid2 is recorded as failing.
- The official 2011 test snapshot is listed in `test-suites.lock.json` but is not present in `Lite.Conformance/vendor`. The fetch script currently fetches WPT, Test262, and Acid inputs, not that snapshot. The lock's historical test totals must be reconciled with an imported catalog.
- The WPT CSS2 checkout is available. Its file count includes references and resources and is not a conformance denominator.
- The legacy CSS runner selects one reference, uses a fixed 600x600 viewport and a global pixel tolerance, and has an informational survey that cannot establish readiness. The newer `WptCatalog`, `WptRefTestRunner`, process isolation, and execution-evidence infrastructure are reusable foundations; fuzzy comparison, DPI metadata, and input automation still need support.
- `Parser.cs` skips alternate stylesheet links, injects UA CSS as an ordinary style element, and unconditionally decodes entities/removes CDATA markers from style text to accommodate XHTML parsed as HTML.
- `PropertyTable` and the dynamic `StyleResolver` inheritance list cover a subset of properties. Initial parsing and later style resolution have different paths.
- `MediaQueryEvaluator` rejects print. `TableEngine` currently takes only the first `border-spacing` component. `FontRegistry` is static and cleared during document loading. These are concrete areas to address, not a complete defect inventory.
- Block, inline, float, positioning, intrinsic-size, table, generated-content, and painting implementations already exist and have focused regressions. Extend them from measured failures instead of restarting the engine.

There are existing uncommitted HTML/conformance changes in the checkout. Integrate with those changes and preserve unrelated work. Rebuild and collect a new baseline before reporting pass rates.

## Milestone 1: complete inventories and independent CSS gates

Add a CSS requirement inventory patterned after the HTML inventory, with one record per testable obligation rather than one record per chapter. Proposed files:

- `Lite.Conformance/Profile/css21-requirements.json` and schema: stable ID, spec anchor, requirement summary, normative strength, media/document applicability, dependencies, implementation locations, reviewed status, and assertion mappings.
- `Lite.Conformance/Css21/css21-applicability.json` and schema: suite/revision, source path, executable variants, reference relations, assertion mapping, required resources/interaction, classification, and review rationale.
- `scripts/import-css21-inventory.py`: reproducible section/property indexes and candidate-test imports. Generated candidates remain unreviewed until checked against the specification.

Inventory every property and shorthand, legal value family, initial value, inheritance rule, percentage basis, computed-value rule, application restriction, and important cross-property interaction. Also cover non-property obligations: stylesheet retrieval, error recovery, selectors, document modes, anonymous boxes, user controls, and media handling. Map mandatory external dependencies, including relevant document-language and Unicode behavior, to evidence.

Classify candidate tests as applicable, mixed-version, later-feature-only, informative/optional, duplicate, or defective, with a precise justification. A missing engine feature or unsupported harness capability remains an applicable blocker. A duplicate is an alias to a canonical test while distinct document/media variants remain separately accounted for. Replacement tests for defects must cover the original obligation and retain provenance.

Extend the profile and schema without changing historical profiles. Add `css21ScreenReady`, `css21PrintReady`, `css21ProfileReady`, per-media blocker lists, and computed obligation/test coverage counts. Add proposed `--suite css21-inventory` and `--require-css-ready` commands. Keep HTML, ECMAScript, CSS, and combined `releaseReady` results distinct. Preserve `--suite css21` as the existing fast regression gate.

**Exit:** every section is reviewed; every applicable obligation has a stable record; every candidate case/variant is classified; omissions are detected from the pinned catalogs rather than trusted completion flags. Readiness stays false until execution evidence is complete.

## Milestone 2: trustworthy full-suite execution

Import and checksum the [official 23 March 2011 suite](https://www.w3.org/Style/CSS/Test/CSS2.1/20110323/), including HTML, XHTML, other-format cases, references, metadata, fonts, images, and licenses. Catalog print applicability as well as screen applicability. The printer conversion is noncanonical and may introduce conversion errors; review it separately. Retain the pinned WPT CSS2 corpus as complementary evidence and classify tests that depend on later CSS specifications.

Add proposed `--suite css21-full --media screen|print`, with filtering for diagnosis and complete sharding for evidence collection. Share catalog, serving, rendering, and evidence code with the newer WPT runner where practical. A filtered run can contribute individual results but cannot declare a complete suite by itself.

Required runner work:

- Serve each document with its real MIME type, encoding, response headers, source URL, and relative resource base. Use upstream WPT serving where tests depend on handlers, origins, or substitutions. Do not use regression overrides as upstream evidence.
- Execute reftests, scripted assertions, and reviewed manual/interactive cases. Honor the pinned runner's full reference graph semantics, match/mismatch relations, variants, metadata, viewport, device scale, and timeouts.
- Use exact comparison by default; allow only reviewed, test-specific fuzzy limits with recorded provenance. Remove the global pixel budget from conformance evidence.
- Wait for document, stylesheet, image, and font readiness and `reftest-wait` completion. Treat unexpected missing inputs, invalid MIME handling, hangs, crashes, and unsupported execution requirements as blockers.
- Provision and fingerprint Ahem and special test fonts. Make generic/system font choices, DPI, locale, page dimensions, device color settings, and UA defaults reproducible. Respect tests requiring a viewport wider than the legacy 600px configuration.
- Isolate cases and reference documents. Audit shared font/resource/style state so one page cannot change another page's result.
- Add harness regression fixtures for reference alternatives/chains, equality and inequality, resource failures, blank-page false positives, delayed resources, viewport/DPI metadata, page count, and stale/missing evidence. Some legitimate tests are blank, so use known sanity fixtures and load assertions rather than a blanket nonblank rule.
- Record source/binary/input identities, document mode, medium, render settings, dependencies, and artifacts. Produce expected/actual/diff images, geometry/style diagnostics, and per-page print output. Extend existing manual evidence validation for native controls and printer checks.

**Exit:** every applicable case has an execution route, and a complete baseline reports pass/fail/unsupported/manual-pending/timeout/crash separately. No silent skip can become a pass. Runtime and failure clusters are measured before estimating the remaining work.

## Milestone 3: stylesheet, cascade, selector, and value foundations

Primary areas: `Parser.cs`, `DocumentState.cs`, `StyleResolver.cs`, `PropertyTable.cs`, `SelectorEngine.cs`, `MediaQueryEvaluator.cs`, stylesheet loading, and native host configuration.

Introduce document-owned stylesheet records preserving origin, URL/base URL, order, media, title/set, disabled state, and import relationships. Represent UA, user, and author origins explicitly; injecting the UA stylesheet into author CSS is insufficient. Expose user stylesheet file selection, author-style enable/disable, and alternate-set selection in the public host options and a usable native/example UI.

Unify initial parsing, inserted nodes, attribute/class changes, inline edits, stylesheet changes, state selectors, and media changes through the same cascade and computed-style rules. Complete property metadata and keep specified, computed, used, and actual values distinguishable. Preserve existing later-CSS features while testing the pinned CSS 2.1 behavior separately.

Complete CSS tokenization/error recovery, escapes, comments, strings, URLs, numeric ranges, units, invalid declarations/selectors, shorthand expansion/reset, `inherit`, source order, specificity, all origin/importance combinations, and presentational hints. Audit encoding precedence against section 4.4 with conflicting HTTP/BOM/`@charset`/link/document inputs; existing tests describe the current implementation and are not sufficient authority. Fix import ordering, media restrictions, cycles, retrieval failures, and imported-resource URL resolution.

Finish CSS 2.1 selector and pseudo-element behavior across real HTML and XML trees, including language, case sensitivity, structural selectors, interactive states, and specification-permitted visited-link restrictions. Implement genuine XHTML parsing/MIME dispatch as a dependency of valid XHTML evidence, coordinating with the HTML workstream. Remove unconditional HTML style-text entity decoding only once the corresponding document-mode tests exist.

**Exit:** all reviewed obligations for syntax, selectors, cascade, and media pass in both applicable document modes; initial-load and equivalent dynamic changes agree; user and author controls work through the actual host.

## Milestones 4-8: complete screen rendering

Each work package starts with its failing applicable test cluster, implements the underlying behavior, adds focused regression coverage, then reruns the cluster plus shared layout regressions. A chapter cannot be marked complete from a sample of its tests.

| Milestone | Required work | Primary code and exit evidence |
| --- | --- | --- |
| 4. Box generation and sizing | Anonymous block/inline/table boxes; inline splitting around blocks; `display` transformations and the pinned target's run-in behavior; containing blocks; percentage bases; all width/height and auto-margin equations; min/max constraints; replaced intrinsic sizes/ratios; shrink-to-fit; adjoining and negative margins, clearance, empty boxes, and BFC boundaries. | `BoxEngine`, `BoxDimensions`, `CssUnits`, `IntrinsicSizer`, `LayoutNode`. Geometry assertions plus full applicable box-model/normal-flow/replaced-element reftests. |
| 5. Floats and positioning | All float placement constraints, line exclusion bands, clear and clearance, nested BFCs, float containment, relative offsets, absolute/fixed static positions, over-constrained equations, RTL cases, inline containing blocks, scrolling and resize invalidation. | `BoxEngine`, `Viewport`, scroll state. Geometry and pixels before/after resize, scroll, and mutation; complete applicable float/positioning catalogs. |
| 6. Inline text and fonts | Persistent line/inline fragments shared by layout and painting; struts, baselines, leading, vertical alignment, inline replaced boxes, wrapping, all whitespace modes, text indent/alignment/justification, spacing, transformations, decoration propagation, first-line and first-letter layout, punctuation, and floated first letters. Complete font family/fallback/matching, size/weight/style/variant/system-font behavior, measured ex units, Unicode bidirectional processing and shaping, including `direction` and `unicode-bidi`. | `TextMeasure`, `FontRegistry`, `BoxEngine`, `Drawer`. Ahem geometry, real-font cases, mixed RTL/LTR and combining text, and pseudo-element reftests. Measurement and drawing must use the same shaped runs. |
| 7. Tables | Anonymous table repair; wrapper/caption sizing; row/column groups and spans; fixed layout and all constraints on permitted automatic layout; percentage widths/heights; baselines and vertical alignment; separate horizontal/vertical border spacing; empty cells; collapsed-border conflict resolution; table background layers; visibility collapse and dynamic changes. | `TableEngine`, `IntrinsicSizer`, `Drawer`. Full applicable table catalog plus targeted independent geometry for span/border conflicts. Test allowed outcomes where automatic layout is underspecified. |
| 8. Generated content, painting, and UI | Counter scope and reset/increment, nested counters, quotes, attr/string/image content, pseudo-element inheritance, all list styles and marker placement; colors/system colors; borders, backgrounds, canvas/root/body propagation and fixed backgrounds; overflow/clip/visibility; complete Appendix E paint order and stacking; outlines, focus, cursor images/fallbacks, and applicable user preferences. | `Parser`, style resolution, `Drawer`, `Interaction`, native host. Applicable reftests plus script/input assertions and manual evidence where pixels alone cannot prove behavior. |

Use logical formatting boxes and paint fragments separate from source DOM identity so anonymous-box generation, reflow, and pagination do not mutate the authoritative document. Preserve source text for whitespace/bidi processing. Scope the necessary model changes to rendering dependencies while coordinating with the existing DOM work.

**Screen exit:** all applicable screen obligations and test variants pass, including interactive user-agent requirements, with no incomplete mappings or unsupported cases. `css21ScreenReady` can become true while the combined CSS gate remains false pending print.

## Milestone 9: print and paged media

Add an explicit rendering context shared by style resolution, layout, and painting: media type, CSS viewport, device scale, font environment, physical page dimensions, and margins. Host-selected paper size belongs in render options; do not require later-CSS page-size features to satisfy CSS 2.1.

Implement a pagination layer over the formatting/fragment model, with page-specific layout and painting rather than slicing a tall screen screenshot. Proposed code: `Lite/Layout/PagedLayoutEngine.cs`, page/fragment result models, and print support in `Drawer` and the native host.

Cover `@media print`, stylesheet media selection, `@page` margins and first/left/right page selectors, page-context cascade, page boxes and content outside them, page-break-before/after/inside, allowed/forced/avoided breaks, widows/orphans, oversized/unbreakable content, and page-relative fixed positioning. Audit interactions with margins, floats, tables, backgrounds, clipping, counters, and generated content. Implement or document permitted choices such as repeated table headers according to the pinned specification. The normative basis is [chapter 13](https://www.w3.org/TR/2011/REC-CSS2-20110607/page.html), supplemented by print-specific rules elsewhere in CSS 2.1.

Provide a deterministic paginated output API and PDF export, then connect the same results to Windows print/preview. Keep print layout separate from the live screen document's state so printing and changing paper settings cannot alter the on-screen page. Use the same page geometry for exported artifacts and native output.

Print tests compare page count, page dimensions, per-page geometry, and per-page images; they also verify content is neither lost nor duplicated. Exercise page parity, forced blank pages, repeated fixed content, nested avoidance, widow/orphan constraints, long tables, oversized content, and differing paper sizes. Add native preview/print-to-file checks and recorded manual physical-printer checks for output scaling and device behavior.

**Print exit:** all applicable print obligations and variants pass through the paginated pipeline, supported native output is verified, and screen/print switching is isolated. `css21PrintReady` and then `css21ProfileReady` become eligible.

## Milestone 10: final audit and permanent gates

Run the complete classified official and supplemental WPT suites against one identified build and merge all required shards. Validate exact inventory coverage, identity consistency, required artifacts/manual evidence, and media/document variants. Publish obligation coverage separately from suite execution coverage and property implementation summaries. Report exclusions, permitted choices, errata decisions, and test defects with their reasons.

Add CSS-specific gate regressions: missing requirement/test/variant, edited completion flags, failed or absent shard, stale binaries, wrong MIME, wrong medium, missing font, unsupported test, invalid manual evidence, and mixed-version assertions must not yield readiness.

CI progression:

1. Pull requests run inventory validation, focused unit tests, the existing curated CSS gate, changed feature clusters, and a small print smoke set once available.
2. Scheduled jobs run the complete screen and print matrix with deterministic inputs and retained diagnostics. During implementation publish every failure; promote completed feature groups into required gates.
3. A CSS completion/release claim requires fresh full evidence and `--require-css-ready`. Package publishing and overall HTML/CSS/ES readiness remain separate decisions; CSS completion alone does not imply `releaseReady`.

Investigate Acid2 and keep its regression visible throughout. Map its CSS failures to obligations, diagnose other HTML/image dependencies separately, and verify against an independent reference. Never regenerate Lite's stored baseline to turn a failure into a pass, and never use Acid2 as a substitute for the requirement inventory.

The final CSS gate requires all applicable obligations and cases to be accounted for with passing evidence, including any reviewed equivalent replacements for defective tests. Expected failures, crashes, timeouts, missing artifacts, missing shards, unreviewed entries, and manual-pending cases cannot be counted as success. Optional or undefined behavior needs a reviewed classification, not a fabricated mandatory assertion.

## Delivery order and validation

Implement inventory/gates first, then full-suite execution. Start the render-context and fragment-model work during the foundations phase so print does not require a second layout redesign. Complete the screen work packages in dependency order: box/sizing, floats/positioning, inline/font behavior, tables, and paint/content/UI; implement print once these shared foundations are stable. Close with the full audit and claim gate.

Each change should deliver a reviewed obligation mapping, a reproducer or applicable upstream case, the implementation, relevant regression evidence, and an updated blocker report. Runtime baselines from milestone 2 determine practical shard counts and estimates; a reliable completion date cannot be inferred from the 52-entry curated suite.

Use `dotnet build Lite.sln -c Release` and the custom executable runner `dotnet run --project Lite.Tests -c Release --no-build` for code validation. Run the affected conformance groups, then the full media matrix for milestone closure. Store generated reports and render artifacts under ignored `Lite.Conformance/artifacts/`. Rebuild after source changes and finish edits before collecting evidence; do not combine evidence from different source/binary identities. All new CLI names and report fields described above are proposed work, not currently available commands.

## Phased remaining-work breakdown

Assessed 5 October 2026 from the committed trackers (`compatibility-report.json`, `Lite.Conformance/artifacts/css21-inventory.json`), not a fresh execution:

- Done: the curated 52-entry gate (green, CI-enforced); official-suite vendoring with a verified tree hash and catalog; the readiness and inventory machinery (`Css21Inventory` fail-closed gates, schemas, blocker computation); screen behavior with focused regressions for box model, margin collapsing, block-in-inline, anonymous tables, float basics, absolute-position equations, replaced sizing, inline struts, generated content, counters, and Appendix E paint-order basics; three profile obligations marked `implemented`.
- At zero: section, property, obligation, and test reviews (841 sections, 115 properties, 5 seed obligations, 0 of 9,364 official plus 9,290 WPT cases classified); the full-suite baseline artifact; Acid2 still recorded as `failing`.
- Known engine gaps: no document-owned stylesheet origins (UA CSS injected as an ordinary style element); no user controls (alternate sets, author-style disable, user stylesheet) — the four `css21.ua.*` hard blockers; print rejected by `MediaQueryEvaluator` and no pagination layer; `FontRegistry` static and cleared per load.

### Phase 1 — full screen baseline (closes Milestone 2's execution exit)

1. Rebuild Release, rerun the unit suite, and regenerate the official catalog artifact to clear `css21-official-catalog-stale`.
2. Run `scripts/run-css21-baseline.ps1` for `--media screen` over the official catalog; measure one shard's wall time before sizing the parallel run.
3. Aggregate with `scripts/aggregate-css21-baseline.py` into `artifacts/css21-full-screen-baseline.md` / `.csv` with per-case outcomes (pass/fail/unsupported/manual-pending/timeout/crash) and the failure-cluster report.
4. Triage clusters into engine defects, harness gaps (MIME handling, reviewed fuzzy comparison, font provisioning, viewport), and suite-classification issues.

Exit: a complete baseline on one build identity and a cluster report that prioritizes all later engine work.

### Phase 2 — inventory review closure (closes Milestone 1's content)

Record all reviews through committed generator scripts, in batches that keep `--suite css21`, `--suite css21-inventory`, and `--suite profile` green:

1. Sections (841): a `scripts/gen_css21_section_review.py` generator; chapter-by-chapter classification (normative-included, informative, permitted-choice, decomposed to obligations) with rationale; `reviewComplete` flips only from the generator.
2. Properties (115): verify values, initial, appliesTo, inherited, and percentage bases against the pinned Recommendation; map `requirementIds`.
3. Obligations: grow `css21-requirements.json` from the 5 seeds to one record per testable obligation with per-media status, decomposed from the section review; replace the `css21.13.pagination` placeholder with real chapter-13 obligations.
4. Official suite (9,364): validate the generated proposals in `css21-official-proposals.json` in batches; write reviewed entries to `css21-applicability.json` (classification, media, requirementIds, dated rationale). Applicable-but-failing cases are recorded reviewed-failing with clause and gap reasons; they feed the Phase 3–4 engine queue and are never included or silently excluded.
5. WPT CSS2 (9,290): classify as applicable, later-feature, informative, optional, defective, duplicate-of-official, or regression-only to clear `css21-unclassified-wpt-cases`.

Exit: the five review blockers (`css21-sections-review-incomplete`, `css21-properties-review-incomplete`, `css21-obligation-inventory-incomplete`, `css21-test-review-incomplete`, `css21-unclassified-wpt-cases`) are gone.

### Phase 3 — foundations (Milestone 3), driven by reviewed-failing clusters

Document-owned stylesheet records with real UA, user, and author origins replacing the style-element injection; public host options plus example UI for alternate sets, author-style disable, and a user stylesheet file (closing the four `css21.ua.*` blockers); one cascade path for initial load and dynamic change; complete PropertyTable metadata; tokenization, error recovery, escapes, shorthand reset, `inherit`; the section 4.4 encoding-precedence audit; `@import` ordering, media restrictions, and cycles; complete selector behavior and genuine XHTML MIME dispatch (coordinated with the HTML workstream; remove unconditional entity decoding only once document-mode tests exist); enable `print` in `MediaQueryEvaluator` as the prerequisite for print execution.

Exit: syntax, selector, cascade, and media obligations pass in both document modes; static and dynamic changes agree; user controls work through the host.

### Phase 4 — screen rendering packages (Milestones 4–8)

Each package: run its failing cluster, implement the behavior, add focused regressions, rerun the cluster plus the curated gate, then flip the obligation `untested` to `implemented` with exact mapped passing tests. Order: box generation and sizing; floats and positioning; inline text and fonts (persistent `FontRegistry`, Ahem provisioning, bidi and shaping, shared shaped runs for measuring and drawing); tables (both border-spacing axes, collapsed-border conflicts); generated content, painting, and UI with the complete Appendix E order. Keep Acid2 visible throughout: map failures to obligations, verify against an independent reference, never regenerate the stored baseline.

Exit: all applicable screen obligations pass; the computed `css21ScreenReady` verdict is published as evidence (not yet enforced); scheduled CI gains the screen matrix as diagnostics.

### Phase 5 — print and paged media (Milestone 9)

Introduce the shared render context (media, viewport, device scale, page geometry); implement `Lite/Layout/PagedLayoutEngine.cs` as real pagination, not screenshot slicing; cover `@page` margins and first/left/right selectors, page-break properties, widows and orphans, page-relative fixed positioning; expose a deterministic paginated output API, PDF export, and Windows print/preview. Then run the print baseline, finish the print applicability review, and close the chapter-13 obligations.

Exit: `css21PrintReady` and then `css21ProfileReady` become eligible.

### Phase 6 — final audit and enforcement (Milestone 10)

Run the full screen and print matrix on one identified build; publish obligation coverage separately from suite pass rates; add the CSS gate-hygiene regressions to `Css21CoverageTests`; rewrite `docs/css21-conformance.md` as the completed-program record; add `--require-css-ready` to CI and release only once the verdict is true. The overall profile claim stays `development-non-conforming` until the HTML and ES2020 tracks' remaining gates are settled.

### Standing constraints

- Edit only the `css21.*` profile prefix and the `Lite.Conformance/Css21/*` plus `Profile/css21-*` artifacts; keep the html5 and es2020 gates green (coordination rules in `docs/html5-conformance-plan.md`).
- One build identity per evidence set: rebuild after source changes and never mix identities; expected-fail waivers are published in the profile and lapse automatically when a waived test passes.
- The long poles are the 18,654-case applicability reviews (mitigated by generator scripts and batch commits) and the print pipeline (largest new engine component); the Phase 1 baseline wall time calibrates all later estimates.
