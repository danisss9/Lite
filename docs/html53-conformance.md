# HTML 5.3 compatibility work

Lite targets the [W3C HTML 5.3 Working Draft of 18 October 2018](https://www.w3.org/TR/2018/WD-html53-20181018/). The active contract is [lite-html53-css21-es2020-profile.json](../Lite.Conformance/Profile/lite-html53-css21-es2020-profile.json). Browser chrome, OS accessibility mappings, and HTTP/TLS internals remain excluded. HTML-visible networking behavior remains included. Complete CSS and ECMAScript conformance are separate workstreams; required HTML dependencies must still be mapped and tested.

**The profile is incomplete.** Passing curated gates or `--suite html53` does not establish HTML readiness, and there is no meaningful completed-conformance percentage. Broad profile entries are workstream placeholders, not an exhaustive list of normative obligations.

## What is implemented

- The active profile, schema, CLI, and CI reporting use HTML 5.3. Historical HTML 5.2 contracts are retained under `Lite.Conformance/Profile/history/`.
- The pinned draft's 883 numbered sections are indexed in [html53-sections.json](../Lite.Conformance/Profile/html53-sections.json). The index preserves exact section numbers, titles, and anchors, including required obsolete processing. Every section currently awaits review. Run `python scripts/import-html53-sections.py` to reproduce the index; `--source <downloaded-index.html>` supports an offline copy. Importing headings does not classify obligations.
- [html53-applicability.json](../Lite.Conformance/Wpt/html53-applicability.json) separates reviewed tests, unreviewed tests, later features, regression-only tests, and explicit exclusions. Unlisted tests remain unreviewed. The expanded WPT checkout is pinned by `test-suites.lock.json`.
- WPT tests run in isolated processes. The deadline includes document loading and script execution; ordinary tests get 20 seconds and `timeout=long` tests get 70 seconds. Completion from a child document cannot finish the root test. Reports retain individual subtests and harness status. Empty results, crashes, timeouts, skipped survey tests, and unexplained failures cannot pass readiness.
- Window-context `.window.js` and `.any.js` tests support metadata scripts and variants. Every declared variant needs evidence. Worker-only tests are not executable yet and cannot establish readiness. Local fixtures and overrides remain separate from upstream WPT serving.
- Narrow reviewed assertions cover `details.open` boolean reflection and a non-null initial iframe document. These establish only the named behavior, not complete details or iframe conformance.
- Initial iframe documents have stable identity. Empty `srcdoc` takes precedence over `src`. Document title and mode use the captured AngleSharp document. Runtime CSS matching, form action resolution, and fetch base URLs use owning-document state. Element wrapper identity is cached per node and Jint realm.

## Reproduce evidence

Finish source changes before collecting evidence. Reports contain the source revision and source-tree digest, active profile and suite-lock digests, dependency binary hashes, engine/harness hashes, suite checkout contents, and platform. Editing inputs during a run marks that report incomplete. Conflicting, stale, or incomplete reports block readiness.

```powershell
./scripts/fetch-tests.ps1
dotnet build Lite.sln -c Release
dotnet run --project Lite.Tests -c Release --no-build -- --report Lite.Conformance/artifacts/unit-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite wpt --report Lite.Conformance/artifacts/wpt-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html53 --report Lite.Conformance/artifacts/html53-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite profile --evidence Lite.Conformance/artifacts/unit-results.json --evidence Lite.Conformance/artifacts/wpt-results.json --evidence Lite.Conformance/artifacts/html53-results.json
```

The compatibility report has `reportFormatVersion: 2`, `html53ProfileReady`, `html53Blockers`, and separate combined-profile `releaseReady` fields. Executed evidence uses `formatVersion: 2`. Add `--require-html-ready` to the profile command to require HTML completion (currently exits 1). `--require-ready` still checks the combined profile. CI records HTML results and reports but does not enable the completion gate while obligations remain untested.

Keep output in the ignored `Lite.Conformance/artifacts/` directory. Arbitrary unignored output files become source inputs and invalidate subsequent evidence identities. Reports fingerprint the binaries actually executed; rebuild after source changes before collecting release evidence.

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
| 1. Contract and measurement | Review all sections; inventory individual user-agent obligations, authoring-only rules, optional capabilities, and referenced dependencies. Classify the expanded pinned tests against 2018. Add real input automation, worker contexts, complete HTTPS/multiple-origin CI integration, required dependency evidence, manual evidence ingestion, and visual failure artifacts. |
| 2. Document semantics | Make AngleSharp the authoritative DOM and layout a projection. Preserve exact text, head nodes, comments, doctypes, namespaces, disconnected nodes, and inert templates. Finish per-document parser/form/focus/observer state, live collections, mutations, serialization, prototypes, and Web IDL conversion. Fragment parsing and several registries still rely on shared state. |
| 3. Parsing and scripts | Implement parser insertion points, streaming `document.write`, document replacement, script scheduling, stylesheet blocking, lifecycle ordering, encoding, and true XHTML parsing. Remove unconditional stylesheet entity decoding. |
| 4. Contexts and networking | Implement persistent browsing contexts and cross-document history, complete iframe lifecycle and targeting, origin enforcement and sandboxing, structured cloning and transfer, request policy, and cookie/storage isolation. |
| 5. Forms and interaction | Complete control states, validity rules, ownership, successful controls, submission encodings, focus and keyboard behavior, activation, details coalescing, modal dialogs, and shared host/headless input. |
| 6. Embedded content and rendering | Complete responsive images, resource lifecycle, object/embed fallback, Canvas 2D state and pixels, origin cleanliness, deterministic and real media evidence, and HTML rendering requirements. Track unrelated CSS and Acid2 separately. |
| 7. Other included capabilities | Implement custom elements and shadow/slot integration, editing, selection/ranges, drag-and-drop, remaining interfaces, and the [5.2-to-5.3 changes](https://www.w3.org/TR/2018/WD-html53-20181018/changes.html). |

Windows manual evidence is still needed for keyboard navigation, editing, drag-and-drop, file selection, and real media playback. The completion gate must stay false until every applicable requirement has passing evidence and no unexplained failure, timeout, crash, or untested obligation remains. Any eventual completion claim must publish the profile exclusions with the evidence.
