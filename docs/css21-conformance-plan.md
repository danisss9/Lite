# Full CSS 2.1 coverage plan

Planning baseline: 17 September 2026. Scope: Windows x64, screen and print, retaining Lite's C#/AngleSharp/Jint/Skia architecture. This document is an implementation plan; it does not change the active compatibility profile or claim that any new coverage has passed.

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
