# HTML 5.3 compatibility work

Lite targets the [W3C HTML 5.3 Working Draft of 18 October 2018](https://www.w3.org/TR/2018/WD-html53-20181018/). The active contract is [lite-html53-css21-es2020-profile.json](../Lite.Conformance/Profile/lite-html53-css21-es2020-profile.json). Browser chrome, OS accessibility mappings, and HTTP/TLS internals remain excluded. HTML-visible networking behavior remains included. Complete CSS and ECMAScript conformance are separate workstreams; required HTML dependencies must still be mapped and tested.

**The profile is incomplete.** Passing curated gates or `--suite html53` does not establish HTML readiness, and there is no meaningful completed-conformance percentage. Broad profile entries are workstream placeholders, not an exhaustive list of normative obligations.

## What is implemented

- The active profile, schema, CLI, and CI reporting use HTML 5.3. Historical HTML 5.2 contracts are retained under `Lite.Conformance/Profile/history/`.
- The pinned draft's 883 numbered sections are indexed in [html53-sections.json](../Lite.Conformance/Profile/html53-sections.json). The index preserves exact section numbers, titles, and anchors, including required obsolete processing. Every section currently awaits review. Run `python scripts/import-html53-sections.py` to reproduce the index; `--source <downloaded-index.html>` supports an offline copy. Importing headings does not classify obligations.
- [html53-applicability.json](../Lite.Conformance/Wpt/html53-applicability.json), format 2, separates reviewed tests, unreviewed tests, later features, regression-only tests, and explicit exclusions. Mixed-era tests require a complete named assertion inventory: only explicitly classified non-target assertions may fail. Missing, duplicate, or new assertions block evidence. Unlisted tests remain unreviewed.
- WPT tests run in isolated processes. The deadline includes document loading and script execution; ordinary tests get 20 seconds and `timeout=long` tests get 70 seconds. Completion from a child document cannot finish the root test. Reports retain individual subtests and harness status. Empty results, crashes, timeouts, skipped survey tests, and unexplained failures cannot pass readiness.
- The locked upstream version 9 WPT manifest supplies canonical URLs, variants, reference graphs, manual tests, and execution contexts. `html53-inventory` exports the review backlog without automatically classifying tests. Generate the manifest before collecting evidence; its digest becomes part of the execution identity. Window-context `.window.js` and `.any.js` tests support metadata scripts and variants. Worker contexts, test-driver, visual/manual automation, fuzzy comparisons, and DPI metadata remain unsupported and cannot establish readiness.
- Window reftests use the pinned wptrunner graph algorithm and exact pixels with the root test's viewport. The runner handles `reftest-wait` and `TestRendered`, rejects failed document loads, and records reference, actual, and difference images for failed comparisons. Process failures retain bounded stdout/stderr logs. This does not establish complete upstream rendering-harness support.
- Window crash tests use the manifest's classification, including legacy files that also load `testharness.js`. They wait for document loading, two paint callbacks, `TestRendered`/`test-wait`, and a final render under the same isolated process deadline. Their synthetic evidence assertion records successful loading and rendering, not individual JavaScript assertions. Headless animation-frame timestamps remain increasing across successive pump calls.
- Narrow reviewed assertions cover `details.open` boolean reflection, runtime toggle notification coalescing, and a non-null initial iframe document. These establish only the named behavior, not complete details or iframe conformance.
- Initial iframe documents have stable identity. Empty `srcdoc` takes precedence over `src`. Document title and mode use the captured AngleSharp document. Runtime CSS matching, form action resolution, fetch URLs, and fragment parsing use owning-document state. Element wrapper identity and observer delivery are isolated by Jint realm. Nested fragment scripts no longer modify the active parser script queue. These changes do not make the layout-backed DOM authoritative.
- Details attribute changes coalesce toggle notifications at the latest queued task position. Initially open details parsed in documents and fragments queue notifications on the owning document's engine, including disconnected fragments; subsequent mutations coalesce with those notifications. Picture source selection preserves the author's `src` attribute; `src` reflection and image resource resolution use the owning document's base.

## Reproduce evidence

Finish source changes before collecting evidence. Reports contain the source revision and source-tree digest, active profile and suite-lock digests, dependency binary hashes, engine/harness hashes, suite checkout contents, and platform. Editing inputs during a run marks that report incomplete. Conflicting, stale, or incomplete reports block readiness.

```powershell
./scripts/fetch-tests.ps1
./scripts/build-wpt-manifest.ps1
dotnet build Lite.sln -c Release
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html53-inventory
dotnet run --project Lite.Tests -c Release --no-build -- --report Lite.Conformance/artifacts/unit-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite wpt --report Lite.Conformance/artifacts/wpt-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html53 --report Lite.Conformance/artifacts/html53-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite css21 --report Lite.Conformance/artifacts/css21-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite test262 --report Lite.Conformance/artifacts/test262-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite profile --evidence Lite.Conformance/artifacts/unit-results.json --evidence Lite.Conformance/artifacts/wpt-results.json --evidence Lite.Conformance/artifacts/html53-results.json --evidence Lite.Conformance/artifacts/css21-results.json --evidence Lite.Conformance/artifacts/test262-results.json
```

The compatibility report has `reportFormatVersion: 3`, `html53ProfileReady`, `html53Blockers`, published `profileExclusions`, and separate combined-profile `releaseReady` fields. Executed evidence uses `formatVersion: 3`; old reports must be regenerated. It records execution contexts, test kinds, and hashed artifact attachments. Manual evidence requires an operator, procedure, environment, observation time within the report's execution interval, and at least one intact artifact. It is ingested through the same `--evidence` option; a complete manual recording workflow remains to be built.

Add `--require-html-ready` to the profile command to require HTML completion (currently exits 1). `--require-ready` still checks the combined profile. Pull-request, nightly, and release workflows generate the inventory and collect unit, reviewed HTML, curated WPT/CSS, and sparse Test262 evidence. They do not enable the completion gate while obligations remain untested. These workflows do not yet execute the full applicable corpus or configure upstream HTTPS and multiple origins.

Keep output in the ignored `Lite.Conformance/artifacts/` directory. Arbitrary unignored output files become source inputs and invalidate subsequent evidence identities. Reports fingerprint the binaries actually executed; rebuild after source changes before collecting release evidence.

CI uses Python 3.12 for WPT. The manifest helper accepts `-Python <executable>` and keeps separate environments for each interpreter version. To reuse an already provisioned environment without running package setup, pass `-VirtualEnvironment <directory> -SkipEnvironmentSetup`; its installed packages must satisfy the pinned WPT requirements. This option does not skip the checkout checks or manifest rebuild.

## Upstream WPT serving

In a separate terminal, run the pinned upstream server:

```powershell
./scripts/serve-wpt.ps1
```

Follow the pinned WPT checkout's `docs/running-tests/from-local-system.md` for host resolution and HTTPS certificates. Supply `-Config <path>` for an explicit upstream configuration. The helper checks the locked revision and runs `wpt serve`; it does not configure the OS hosts file or trust store.

Point Lite at that server:

```powershell
dotnet run --project Lite.Conformance -c Release --no-build -- --suite wpt --filter Event-type.html --wpt-base-url http://web-platform.test:8000 --report Lite.Conformance/artifacts/upstream-results.json
```

The upstream server provides handlers, substitutions, and multiple origins. HTML evidence for vendor WPT paths requires upstream serving; the built-in static server and regression overrides cannot supply that evidence. `lite/` fixtures continue to use the local server. Authentic XHTML MIME serving is enabled for WPT; the legacy CSS runner still has its XHTML-as-HTML workaround pending genuine XML parsing.

## Remaining milestones

| Milestone | Work still required |
| --- | --- |
| 1. Contract and measurement | Review all sections; inventory individual user-agent obligations, authoring-only rules, optional capabilities, and referenced dependencies. Classify the expanded pinned tests against 2018. Add real input automation, worker contexts, complete HTTPS/multiple-origin CI integration, required dependency mapping, manual recording procedures, and remaining reftest metadata. |
| 2. Document semantics | Make AngleSharp the authoritative DOM and layout a projection. Preserve exact text, head nodes, comments, doctypes, namespaces, disconnected nodes, and inert templates. Finish document-owned form/focus/resource state, live collections, mutations, serialization, prototypes, and Web IDL conversion. Font/animation registries and native input still need isolation. |
| 3. Parsing and scripts | Implement parser insertion points, streaming `document.write`, document replacement, script scheduling, stylesheet blocking, lifecycle ordering, encoding, and true XHTML parsing. Remove unconditional stylesheet entity decoding. |
| 4. Contexts and networking | Implement persistent browsing contexts and cross-document history, complete iframe lifecycle and targeting, origin enforcement and sandboxing, structured cloning and transfer, request policy, and cookie/storage isolation. |
| 5. Forms and interaction | Complete control states, validity rules, ownership, successful controls, submission encodings, focus and keyboard behavior, activation, DOM task-source scheduling, modal dialogs, and shared host/headless input. |
| 6. Embedded content and rendering | Complete responsive images, resource lifecycle, object/embed fallback, Canvas 2D state and pixels, origin cleanliness, deterministic and real media evidence, and HTML rendering requirements. Track unrelated CSS and Acid2 separately. |
| 7. Other included capabilities | Implement custom elements and shadow/slot integration, editing, selection/ranges, drag-and-drop, remaining interfaces, and the [5.2-to-5.3 changes](https://www.w3.org/TR/2018/WD-html53-20181018/changes.html). |

Windows manual evidence is still needed for keyboard navigation, editing, drag-and-drop, file selection, and real media playback. The completion gate must stay false until every applicable requirement has passing evidence and no unexplained failure, timeout, crash, or untested obligation remains. Any eventual completion claim must publish the profile exclusions with the evidence.
