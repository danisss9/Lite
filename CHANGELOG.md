# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

- Add Linux support. `BrowserWindow` no longer talks to Win32 directly: its message handlers become platform-neutral callbacks driven by a window host — `Win32WindowHost` (the previous WndProc, unchanged in behavior) on Windows and a new `X11WindowHost` on Linux, which binds `libX11` directly, blits Skia's BGRA frame with `XPutImage`, wakes from background threads through a self-pipe in a `poll()` loop that also drives the animation timer, maps KeySyms onto the existing virtual-key tables, and takes text through XIM (`Xutf8LookupString`, with a keysym fallback for locales that cannot encode the key). `alert()` and `<input type=file>` use `zenity`/`kdialog` on Linux. The QuickJS bridge gains `build-quickjs.sh`, which builds `liblitequickjs.so` from the same pinned archive and patches; the loader, conformance evidence and `run-es2020.py` resolve the per-OS asset; and `danisss9.Lite.QuickJs` ships `runtimes/linux-x64` beside `runtimes/win-x64`. Lite references `SkiaSharp.NativeAssets.Linux`, maps `system-ui`/`monospace` and the symbol-fallback list onto fontconfig's families off Windows, caches installed-typeface lookups (uncached fontconfig matching cost about a second of re-layout per keystroke), and reports a Linux user agent and `navigator.platform` there. `fetch-tests.ps1` runs under PowerShell 7 on Linux, and CI adds an Ubuntu job that builds the solution and runs the unit suite.
- Run Test262's multi-agent tests natively and retire the last ES2020 dependency exception: the QuickJS bridge implements `$262.agent` (start, broadcast, getReport, report, receiveBroadcast, leaving, sleep, monotonicNow) with each agent as its own QuickJS runtime on its own thread, SharedArrayBuffer memory reference-counted outside any one heap so main and agent runtimes share it, and a teardown that detaches an agent a failing test leaves blocked instead of hanging. The adapter main agent can block unless a test is flagged `CanBlockIsFalse`, as INTERPRETING.md allows (the browser window agent stays non-blocking). All 59 formerly excluded `Atomics.wait`/`Atomics.notify` tests pass in both modes; the full ES2020 run reads 67,235/67,235 required executions with no dependency exception and `es2020ProfileReady` true under `--require-ready`.
- Run WPT `visual` tests under `css/CSS2` through the CSS 2.1 suite's self-describing route (the no-failure-red scan the official suite already uses) instead of reporting them unsupported; visual tests elsewhere stay unsupported because they promise no such convention.

## [0.1.2] - 2026-10-13

This release closes the HTML5 parse-fidelity pipeline — a new attribution diagnostic splits every corpus failure into engine, pipeline and expectation-invalid classes, the scripting-flag package and DOM-level origin-sheet injection eliminate the entire pipeline-defect bucket, and a vendored AngleSharp fork carries ten 2014-REC tree-construction patches that lift the engine-pass to 92.0% of the corpus — while the CSS 2.1 track's table batches close the infer-cells band and lift the official suite to 8,543/9,039 (94.6%). The curated gates hold at 52/52 (css21), 46/46 (html5), 86 pass / 2 published expected-fails (wpt), and the unit suite grows to 341/341.

- Add the html5 parse attribution diagnostic (`--suite html5-attribution`, `Lite.Conformance/Wpt/ParseAttribution.cs`): every tree-construction case executes twice — a plain AngleSharp parse of the raw input versus the real `ParseChildPage` pipeline — and each failure is classified. At introduction 1,652 of 1,891 unique cases pass (87.4%) with zero crashes, and the headline finding is that the largest failure cluster is not an engine gap: all 90 processing-instructions.dat failures expect `<?...>` to parse as a processing-instruction node where REC §8.2.4.42 requires a bogus comment (verified against real Chromium), the 4 isindex and ~19 select-mode expectations are post-target, and the genuine failure set is 149 unique cases split between AngleSharp deviations (~72) and pipeline defects (~52, dominated by text-level origin-sheet injection).
- Parse with the scripting flag on: the parser registers a scripting service so AngleSharp parses with scripting enabled (REC §8.2.5 — noscript content parses as raw text and the in-head-noscript rules apply) while the service's evaluation entry point no-ops, keeping script execution entirely with Lite's loader. The whole noscript cluster (8 unique cases) closes and engine-pass moves 1,654 → 1,662 of 1,891 (87.9%).
- Inject origin sheets at the DOM level: the UA and user origin sheets enter the document as elements (UA sheet as head's first child, user sheet after it, the user !important half appended after every author sheet) instead of being spliced into the fetched source, closing the entire pipeline-defect bucket the attribution had measured (39 → 0) — text-level injection re-anchored leading comments and whitespace into head, its `<head>` synthesis flipped before-head to after-head so a following `<template>` landed in body, and its regexes could match inside raw-text style content. Engine-pass moves 1,662 → 1,701 (90.0%); comments01, doctype01, domjs-unsafe, tests16, tests6, tricky01 and webkit01 pass whole; and the cascade-origin contract (UA < user < author, user !important last) is verified against the CSS track's own gate.
- Give the survey's page budget room for the heaviest real page: the isolated-worker kill budget becomes one flat 300s, so the named-character-references page (~2,231 entity subtests, unreported since the corpus was wired up) now reports and passes whole — the .dat entity corpora pass 100%.
- Vendor AngleSharp 1.7.3 for conformance patches (`Lite.AngleSharp/`): the pinned upstream source (tag `v1.7.3`, commit `0e6979a`, `src/AngleSharp/**` plus upstream's committed `Key.snk`) is vendored pristine and built into the exact binary identity of the NuGet package — assembly `AngleSharp` 1.7.3.0, token `e83494dcdc6d31ea` — so NuGet AngleSharp.Css 1.0.2 keeps binding while Lite's compile and runtime switch to the project. Ten numbered patches land, each listed in the fork README with its html5lib/WPT evidence: 001 menuitem parses as a normal element (menuitem-element.dat 9→18/20); 002 foreign-content breakout applies in fragment parsing (foreign-fragment.dat 48→64/66); 003 fragment-context tokenizer states are HTML-namespace only (SVG title fragments parsed as RCDATA text); 004 input/keygen/textarea in select follow the 2014 close-select+reprocess rule with the fragment context counting for the scope check; 005 the data state emits U+0000 and HTML text insertion drops it, so foreign NUL text keeps its replacement characters; 006 template in after-head processes with the in-head rules; 007 frameset mode keeps per-character whitespace; 008 the reset's html case follows the head pointer — before-head only when the document has no head, otherwise after-head whose EOF synthesizes the body (template.dat 108→111/112); 009 NUL replacement in foreign text spares frameset-ok while other non-whitespace characters clear it, per-character and Chromium-verified (plain-text-unsafe #18/#19/#20); and 010 tokens at HTML/MathML text integration points process with the in-body rules, with in-body-ignored start tags falling through to the table modes while a table is in scope (math.dat/svg.dat #1-4 and tests9/tests10 all pass). Measured movement across the fork: engine-pass 1,701 → 1,740 of 1,891 unique cases (92.0%); anglesharp deviations 189 → 150.
- Review the 27 outstanding dom applicability entries against the referenced 2014 DOM4 draft (WD-dom-20140710) and current engine outcomes: three TreeWalker entries classify post-target (cross-realm filter files and realm-bind semantics absent from the draft), the four dom/events entries stay reviewed-deferred (each embeds `srcElement`, `cancelBubble` or `returnValue` reads — all absent from the draft — inside otherwise-target subtests), and the remaining 20 carry reviewed-failing reasons naming clause, failing assertion and engine gap (HTMLCollection legacy-platform-object semantics, missing getElementsByTagNameNS, DOMStringMap and NamedNodeMap supported-property-name enumeration, DOMTokenList verbatim stringifier, TreeWalker/NodeIterator residuals). The manifest reaches 162/186 entries classified (45 included, 1 mixed, 93 post-target, 23 regression-only), and the tests11 foreign-attribute question resolves as post-target — the corpus follows the modern adjustment table, Chromium-verified, so its expectations are not 2014 obligations.
- Close the CSS 2.1 table batches: presentational-hint and DOM-projection styling read the cascade through the guarded computed-style path (AngleSharp's unguarded `ComputeCurrentStyle` threw on the unitless line-height a `font: 10px/1` shorthand computes, crashing ten newly-executing support-content cases — nine now pass); table runs survive non-rendered siblings so a display:none child no longer splits anonymous tables; intrinsic max-content counts the §16.6.1 boundary space a text leaf carries toward adjacent rendered content; the line assembler's band-start whitespace respects white-space:pre and generated anonymous tables zero border-spacing (§17.6.1 initial 0); a whitespace-only display:table-caption generates nothing (§17.2.1); whitespace inside anonymous cells is content — it drops only between proper table children — and a run that is entirely whitespace or empty table boxes generates no cell (tables-001's anonymous table objects only exist if the table has content); and table intrinsic measurement adopts IntrinsicSizer's boundary-space model (whitespace-only leaves arm a run-boundary flag without emitting their own token), closing the entire infer-cells band 085–092 plus 008 and 203. The table-anonymous-objects family reaches 198/204 and the official suite reads 8,543 pass / 494 fail / 2 unsupported / 0 crashes (94.6%, +41 over the 0.1.1 baseline, zero regressions).
- Add the av-huge override page — a stress page whose script declares 70,000 sequential variables — to the harness override set, and align fetch-tests.ps1's vendored-tree hash sort exactly with the C# verifier: OrdinalIgnoreCase with an Ordinal tiebreak over the relative path alone (the previous key mixed the full path in, so the `|` separator reordered prefix pairs such as at-charset-001.css and its .headers sidecar against the verifier's order), and hidden/system files are included to match `Directory.EnumerateFiles`.

## [0.1.1] - 2026-10-12

This release hardens the layout engine's CSS 2.1 box semantics — collapsed-margin chaining with mixed signs, clearance, self-collapsing boxes, and floats — re-projects DOM mutations onto the layout tree in DOM order, restores the official CSS 2.1 suite's missing inputs to a 94.1% validating baseline, and resolves the HTML5 track's deferred event and insertion applicability entries alongside engine fixes for doctype identity, dynamic markup streams, and ChildNode anchors. The curated gates hold at 52/52 (css21), 46/46 (html5), 86 pass / 2 published expected-fails (wpt), and the unit suite grows to 331/331.

- Implement CSS 2.1 clearance semantics (§9.5.2/§8.3.1): a block-level box with `clear` computes clearance against its hypothetical border edge (run position plus collapsed margin), moving the border edge to the relevant float bottom and severing the margins above when the float extends past it, with clearance possibly negative; a self-collapsing cleared box's resulting chain collapses with following siblings without escaping the parent's bottom margin. Cleared boxes never join the parent's top-margin chain, and always apply their effective top margin as their own gap below to keep the no-clearance case geometrically identical. margin-collapse-clear moves to 4/4.
- Carry collapsed-margin chains as a (maxPositive, minNegative) pair instead of a running scalar, so three-or-more adjoining margins with mixed signs keep their negative member (`collapse(collapse(2,-4),14)` now yields the §8.3.1 value 10), and LayoutBlock reports its effective bottom margin as the chain so a parent's fold consumes the negatives. A self-collapsing box's collapsed chain is spent above its border box — its containing block for absolutely-positioned descendants and any paint origin lands where the following content will — while boxes carrying their own text contain a line box and are not self-collapsing, and fully self-collapsing content no longer extends a bordered box's height. Floats escaped into the BFC list re-anchor with the chain's bottom, shifting each box and mid-chain float exactly once by the chain offset it already sits at.
- Re-project DOM mutations onto the layout tree in DOM order: mutation APIs mirror into the AngleSharp DOM first and rebuild the parent's LayoutNode children (reusing each bound node, letting normalizers regenerate anonymous boxes), so inserts before nodes wrapped in block-in-inline or anonymous-table pieces land at the right reference instead of appending at the end. Projections carry their own computed cascade, hoisted-out-of-broken-inline blocks suppress inherited borders via an explicit flag, generated-content pseudos are harvested back to direct-child position before re-projection, and orphaned CharacterData anchors resolve through the authoritative DOM with mode-aware sibling semantics (ChildNode-before/after/replaceWith 123/123 subtests). block-in-inline-insert moves 0/38 → 38/38.
- Fix table intrinsic-width parity by delegating table and row-group measurement to the same per-column max-width walk layout uses, so shrink-to-fit equals the width layout gives the table; let a float paint its own text when it has no height-contributing children (a `display:none` script no longer mutes it), and copy the computed font size through `cloneNode` so cloned rows resolve font lengths against the cloned base. table-anonymous-objects moves 171 → 178/204.
- Restore the official CSS 2.1 suite's missing inputs and close the resulting families. Recover the 2011 snapshot's absent `html4/support/` subtree (363 files from `csswg-test@fa448700877`, the last commit before the snapshot, gold-validated against Wayback/CDX captures with per-file provenance), map at-charset sheets to encoding headers derived from the suite's recovered `.htaccess` rules, and restore 198 of the 200 missing `.xht` reference documents, each pinned by a recovered build transform whose XHTML→HTML conversion must equal the vendored `.htm` twin byte for byte. Apply §4.2 parsing errors — a declaration with a negative width/height/padding/border/font-size/line-height/border-spacing value is dropped from the sheet text so an earlier valid declaration survives — and scan `SuiteInputs` like the browser (recursive style blocks and linked sheets, comments stripped, references percent-decoded) instead of raw document text, moving prose `url(...)` mentions out of the blocker set. The full screen baseline reports the official suite at 94.1% (8,502/9,039; 525 real failures, honest unsupported down 877 → 2).
- Arbitrate selftests containing failure red through embedded references: when the no-red scan detects `#ff0000` and the suite's own `#test`/`#reference` pair exists, compare the test's dominant color over the swatch's paint region against the reference's, closing 72 swatch-family cases across the color, border-color and outline-color families with zero regressions. Clamp `rgb()`/`rgba()` components to [0,100%]/[0,255] per §4.2 instead of dropping them, exclude the suite's own Ahem font-requirement notice from the no-red scan, and update the acid1 pixel baseline.
- Resolve the HTML5 track's deferred `dom/events` and `dom/nodes` applicability entries against the 2014 REC's own text: reclassify the script-removal and cancelBubble entries post-target (`isConnected` and removal-cancels-execution are absent from the referenced 2014 DOM4 draft and §4.11.1 prepare-a-script), complete the first mixed-entry assertion inventory (Event-defaultPrevented: six 2014-target subtests gated, two `returnValue` subtests inventoried post-target), promote five traversal/NodeFilter tests to included, and hold out the indivisible mixed Event pages with per-subtest split proofs. The reviewed gate grows from 40 to 46 tests, all passing.
- Add the engine pieces the HTML5 gate demanded: expose DocumentType identity (`name`/`publicId`/`systemId`) so tree dumps print `<!DOCTYPE html>`; make the `option.value` getter apply the text fallback IDL-only while the parser mirrors a DOM-present attribute, so bare options no longer serialize `value=""`; marshal void-returning host operations to `undefined` instead of `null` so testharness cleanup callbacks pass; support frame `document.open`/`write`/`close` streams that replace the frame's child page and fire load; and preserve comments and processing instructions through mutation re-projection. The parse-fidelity url runner rises to 12.2% (165/1,354), the write corpus stays hard-blocked on streaming dynamic markup, and the survey runs crash-free.

## [0.1.0] - 2026-10-10

This work completes the ES2020 compatibility program: the compatibility profile now reports `es2020ProfileReady` true with zero blockers, and compatibility CI and NuGet release validation enforce it.

- **Full ECMA-262 11th edition support is established.** The required language corpus grew to 67,117 Test262 executions across eight deterministic Windows shards — all passing with zero failures, zero expected failures, and zero waivers — and the browser JavaScript host suite grew to 20 passing cases covering module fetch options, identity, realms, error and rejection reporting, jobs and readiness, `document.all`, and the shared language/agent configuration.
- Close the eight ES2020 host obligations in `es2020-host-obligations.json`, each reviewed and mapped to host cases: module fetch options (credentials mode from the `crossorigin` attribute, document-URL referrer with origin-only cross-origin Referer, exact-origin ACAO under credentials, per-hop redirect taint), script source ownership (response-URL bases for redirected classic scripts, `<base>`-aware inline module bases, callback-origin import bases), module resolution and identity (canonical absolute-URL identity, namespace identity and immutability, module-map failure caching), error and rejection reporting (trusted cancelable ErrorEvents with stack-parsed locations, legacy `onerror` arguments, unified cancelable default reporting, PromiseRejectionEvent cancellation), document and iframe realms (per-realm module maps and error reporting, parent cancellation), jobs and readiness (readiness after module failures, checkpoint ordering), `document.all`, and host-language configuration.
- Formalize the multi-agent `Atomics.wait`/`Atomics.notify` exclusion as the reviewed `atomics-multi-agent` dependency exception in `es2020-applicability.json`, published as the `es2020.shared-memory-agent-clusters` dependency exception naming all 59 exact tests; agent-cluster coverage is deferred to the Web Workers workstream and every agent-free Atomics case remains required. `Test262Catalog` validates the new `dependencyExceptions` block fail-closed.
- Complete the staging edition review with executable evidence: a new `--test262-set staging` diagnostic executes the 1,226 upstream staging tests; 1,179 pass in all modes and join the required corpus, and the 47 failures each carry a specific review reason (legacy SpiderMonkey behaviors, proposal features such as `RegExp.escape` and bound-function subclassing, ES2021 logical assignment operators, ULP-level Math precision, and named esid:pending engine deviations).
- Complete the normative section review of all 2,115 ECMA-262 clauses: 848 map directly to included tests, 26 sections whose only direct tests are post-target gain reviewed `additionalTests` topic mappings, and the rest carry reviewed non-executable reasons. Setting `semanticReviewComplete` clears the 3,873 post-target mix-in flags executably, with uncovered sections still failing closed.
- Enforce readiness: `scripts/run-es2020.py --require-ready` gates compatibility CI and the NuGet release gate on `es2020ProfileReady`, replacing the publish-only contract.
- Continue the HTML 5.0 conformance track with the second applicability batch and the engine packages it called for. Grow the reviewed `--suite html5` gate from 7 to 40 tests by reviewing the curated `dom/nodes`, `dom/events` and `html/semantics` regressions against the DOM4 draft the 2014 Recommendation references (that edition defines `remove()`, `initCustomEvent` and the `Event` constructor, and does not define `returnValue`, `srcElement`, `cancelBubble`, constructible `EventTarget`, or `before`/`after`/`replaceWith` — files mixing those keep dated deferred reasons instead of being absorbed). Expose Web IDL interface objects (`Node` with node-type constants, `Element`, per-element `HTMLElement` interfaces, `NodeFilter`, …) through a realm bootstrap that builds the prototype hierarchy, `Symbol.toStringTag` tags and illegal-constructor behavior, and brand every host wrapper at exposure time so `instanceof`, `constructor.name` and `Object.prototype.toString` behave per spec. Give `element.dataset` real DOMStringMap named-property semantics (property get/set, `delete`, `in`, and enumeration through `getOwnPropertyNames`) via a dedicated proxy. Add the `Blob` interface with `URL.createObjectURL`/`revokeObjectURL` and blob: iframe navigation, on* IDL attribute properties (`element.onload = fn`) that replace previously registered handlers per HTML §8.1.5.2, `normalize()` on documents and elements with adjacent-text merging, and a spec-order TreeWalker/NodeIterator rewrite with filter support (function and object filters, REJECT cutting subtrees, SKIP staying transparent, live reference tracking, and TypeError on non-Node `currentNode` assignment). Project the formerly skipped metadata nodes (`style`, `noscript`, `meta`, `link`, `title`) into the DOM as `display:none` elements so parse-tree assertions see the real tree while rendering is unchanged. Unblocked by these, the pinned WPT html5lib tree-construction corpus now executes its ~5,500 cases through the engine, and `dom/traversal` moved from interface-object crashes to real algorithm results.

## [0.0.17] - 2026-10-03

This release replaces the JavaScript engine: Jint is removed and Lite now runs a bundled, pinned native QuickJS through a Windows x64 bridge, with DOM bindings, modules, dynamic import, and Test262 execution moved onto it. It adds the iframe and messaging features reCAPTCHA requires, vendors the official CSS 2.1 suite behind a verified catalog, and fixes stylesheet charset resolution.

- Replace Jint with bundled QuickJS as Lite's JavaScript runtime and NuGet dependency. The new `Lite.QuickJs` project pins the QuickJS 2026-06-04 source archive by SHA-256, applies numbered patches through `build-quickjs.ps1`, and embeds the resulting `litequickjs.dll` in the package under `runtimes/win-x64/native` with a build-fingerprint record beside it. Move DOM bindings, modules, dynamic import, and Test262 execution to the new bridge. The unit and browser host suites pass; broader ES2020 conformance remains open, including full proper tail calls.
- Bind a live, callable `document.all` HTMLAllCollection with Annex B `[[IsHTMLDDA]]` behavior — falsy conversion, loose equality, `typeof`, and callability — on QuickJS host-object support. The focused `annex-b-document-all` host case now passes and leaves the expected-failure list; the complete legacy property semantics and normative obligation review remain open.
- Release the current bytecode frame before entering direct strict tail calls through the new `0001-strict-tail-call-trampoline.patch` engine build patch, covering the direct and mutual calls QuickJS already emits as tail-call opcodes. Conditional expression tail positions, bound/proxy forwarding, and full Test262 tail-call evidence still need implementation.
- Add native browser features needed by reCAPTCHA: load dynamically inserted iframes with the page session, route clicks into their child documents, expose named frames and stable message sources, transfer `MessagePort` objects between iframes and workers, and support element focus and explicit XHR content types. Add focused iframe and Web API regression tests.
- Vendor the official 23 March 2011 CSS 2.1 suite behind `OfficialCatalog`: test/reference structure is derived from the suite's own `reftest.list`, per-test metadata (flags, assertions, spec anchors) from each test's head, and the vendored tree is pinned with a whole-tree SHA-256 that the suite lock must publish. Execution uses the tree-verified catalog; readiness additionally requires the lock's pinned tree hash.
- Fix stylesheet charset resolution to try the raw label before the underscore fallback, so UTF-8 stylesheets decode through the label .NET recognizes instead of raising first-chance exceptions on every request, while legacy labels like `shift-JIS` still fall back to `shift_jis`. Skip style rules whose selector text is empty, and register the code pages provider in the tests that assert this.
- Pin QuickJS's native stack budget to 768 KB per runtime instead of the engine's 1 MB default. The stack-overflow watermark is anchored on the thread that creates (or adopts) the runtime, so with the default the guard could fire below the host thread's real stack end and let deep JS recursion die as an uncatchable `AccessViolationException`/`StackOverflowException` — a crash that only appeared on some hosts, depending on binary layout, and that the WPT survey already had to skip whole tests over. The pinned budget keeps every recursion depth that passes on 1.5 MB-reserve hosts while staying safe on 1 MB threads, and turns the overrun into a catchable `InternalError: stack overflow`. Add bridge regression tests for catchable deep recursion and for heavy engine creation with large scripts.
- Bind reflection host methods the way WebIDL operations behave: arguments beyond an operation's declared parameters are ignored instead of raising `MissingMethodException` — Google's reCAPTCHA client calls `appendChild(node, reference)` with two arguments — and a matched overload that cannot convert an argument (a string passed where a node is required) falls through to the next overload, as browsers do. Calls with too few arguments still raise. Add DOM regression tests for both paths.
- Open the HTML 5.0 conformance track: `docs/html5-conformance-plan.md` defines the milestone order (applicability review and gate growth, parse fidelity, harness automation, engine packages, section closure, evidence and readiness) and the coordination rules with the parallel CSS 2.1 and ES2020 workstreams. Grow the `--suite html5` gate from 1 to 7 reviewed tests by reviewing the six Lite fixtures against the pinned Recommendation (script execution timing, DOMContentLoaded, session history/location, media element, iframe nesting, harness sanity — 29 subtests, all passing), classify the `dom/abort` and tentative `dom/observable` roots as post-target, convert the `dom/collections`, `dom/lists` and `dom/traversal` survey failures into reviewed-failing applicability entries with per-test reasons (snapshot collections, missing Web IDL interface objects, traversal filter semantics) that feed the DOM authority engine package, and record the fresh 14,610-case upstream inventory baseline.

## [0.0.16] - 2026-09-26

This release proves the engine against two real search services — DuckDuckGo Lite and signed-out Google Search — exercised end to end from form submission through rendered, clickable results, and adds the scripting and loading features those pages required.

- Add a DuckDuckGo Lite suite (`DuckDuckGoLiteTests`) that replays the site's GET/POST search shape against a local fixture server: URL-encoded form bodies, region and time filter resubmission, pager pagination, 303 redirects, and an `iso-8859-1` response decoded to the correct title. Submit-event cancellation is covered for `preventDefault`, `requestSubmit`, and scripted `form.submit()`, and the homepage asserts desktop control widths and relative hit regions.
- Add a Google Search suite (`GoogleSearchTests`) that deterministically replays the signed-out Search and consent journey captured on 2026-09-25: home form submit, the consent interstitial's accept/reject forms, HTTP-only consent cookies kept invisible to script, and result links that remain clickable at desktop and 800×600 viewports. Reference renders are captured against headless Edge, and an explicit live smoke test (`--google-live`) never runs in the deterministic suite.
- Execute scripts inserted after parse: a `<script>` created with `createElement('script')` and inserted into the document runs through the engine's event loop, with external sources fetched through the page session (cookies, user agent) and module support. `HTMLScriptElement.text` is settable before insertion, and `document.currentScript` reports the currently executing script element per HTML §4.11.1.
- Render server error bodies instead of failing the load — search engines rate-limit with HTML interstitials — and surface the status as a session diagnostic. Keep `HEAD` and `SCRIPT` as real DOM nodes so script-element bootstraps like `getElementsByTagName('script')[0].parentNode.insertBefore` work, and never collapse `<script>` text content.
- Add `navigator.sendBeacon` (fire-and-forget POST carrying session cookies), the byte-oriented `atob`/`btoa` globals with catchable `InvalidCharacterError`, `document.defaultView`, a `document.cookie` setter that writes session cookies, and numeric `code` values on DOM exceptions.

## [0.0.15] - 2026-09-21

This release moves the active compatibility contract to the fixed HTML 5.3 draft of 18 October 2018, hardens WPT execution and document isolation, fixes event handler content attributes, and separates execution results from the outstanding review backlog in the ES2020 gates. Readiness flags remain false: `es2020ProfileReady` is reported rather than enforced in compatibility CI and NuGet release validation, which publish it as evidence alongside the package. This candidate does not claim full conformance.

- Migrate the active compatibility contract to the fixed HTML 5.3 draft of 18 October 2018; retain historical HTML 5.2 contracts. Add a reproducible section index, test applicability review, HTML-specific blockers, and executed evidence reports. HTML readiness remains false.
- Isolate WPT tests in child processes with load/execution deadlines and document-specific reporting. Preserve individual subtests, metadata dependencies, variants, `.window.js`/`.any.js` support, authentic WPT MIME types, and an upstream WPT server option.
- Create stable initial documents for empty iframes and honor empty `srcdoc`. Keep document metadata, runtime stylesheet matching, form URL resolution, and fetch base URLs with their owning document. Cache element wrappers per node and realm.
- Add regression tests for evidence rejection, omitted sections, metadata variants, worker containment, and independent documents. Document the remaining HTML 5.3 milestones; this work does not complete the profile.
- Run event handler content attributes (`onclick="…"`, `onwheel="…"`) as a function body whose single parameter is named `event` and whose `this` is the element, per HTML §8.1.5.1. They previously ran as bare script, so every handler that referenced `event` threw a `ReferenceError` onto `window.onerror`.
- Report the testharness status message alongside the status code in WPT failure detail, so a page whose subtests all pass but whose harness errors names the cause.
- Record `dom/nodes/moveBefore/preserve-render-blocking-script.html` and `…/iframe-loading-lazy-in-scroller-horizontal-far.html` as published expected failures: `document.head`, `Node.moveBefore`, `blocking=render` and `loading="lazy"` distance deferral are unimplemented. Both are now `failing` entries in the compatibility profile rather than silent gate failures.
- Report, rather than enforce, `es2020ProfileReady` in compatibility CI. Readiness additionally requires the unfinished normative and edition review (2,115 unreviewed sections, 5,099 unresolved test classifications, eight unreviewed host obligations), which execution cannot supply. `scripts/run-es2020.py` still runs the readiness check and records its verdict, but its exit status now covers execution only. NuGet release validation likewise publishes the verdict as release evidence instead of enforcing it: on the Jint 4.16.3 pin the two published expected failures keep readiness false by contract even after a complete review, so enforcement would have blocked every tagged release regardless of engineering progress.
- Separate execution results from review backlog in the Test262 shards: unreviewed classifications are reported and kept in the evidence but no longer fail a shard, which previously reported 1,226 staging tests awaiting edition review as failures.
- Give the Test262 and ES2020 host suites the curated WPT manifest's expected-failure contract, through `Test262/es2020-expected-failures.txt` and `Test262/es2020-host-expected-failures.txt`. Both entries still execute, are still recorded as failures, and still keep `es2020ProfileReady` false; they are published in the profile as `es2020.annexb.block-decl-func-arguments` (a stock Jint 4.16.3 defect) and `es2020.annex-b-document-all` (`document.all` needs Jint's internal `[[IsHTMLDDA]]`). A listed test that starts passing fails the suite as an unexpected pass, so a waiver cannot outlive its cause.

## [0.0.14] - 2026-09-04

This entry consolidates the previously staged, unreleased 0.0.14 and 0.0.15 work with the compatibility-program foundation for the **Lite HTML 5.2/CSS 2.1/ES2020 compatibility profile**. Before the dependency-foundation upgrade, the two CSS 2.1 passes moved the upstream `css/CSS2` survey from **2554/6266 (40.8%)** to **4042/6266 (64.5%)**, a net gain of **1488 reftests**. This candidate makes the standards target and its evidence auditable; it does not claim full conformance while the published report remains incomplete or failing.

### Compatibility-program foundation

#### Added

- **Machine-readable compatibility contract** — `lite-html52-css21-es2020-profile.json` maps the current HTML 5.2, CSS 2.1, and ES2020 clause inventory to `implemented`, `failing`, `untested`, `profile-excluded`, or `dependency-exception`, with a schema, exact evidence paths, reasons, and upstream-issue requirements. Unmapped applicable clauses default to `untested` rather than silently passing (`ProfileRunner`)
- **Pinned conformance inputs** — `test-suites.lock.json` records the exact WPT and Test262 commits, sparse-checkout scope, ES2020 applicability manifest, and the official 23 March 2011 CSS 2.1 suite target. `scripts/fetch-tests.ps1` now consumes that lock and verifies checkout revisions (`test-suites.lock`, `fetch-tests`)
- **Deterministic compatibility report** — `--suite profile --report <path>` validates the contract and emits included, excluded, failing, untested, and dependency-exception totals. `--require-ready` fails while any release blocker remains (`ProfileRunner`, `Program`)
- **Shardable conformance runs** — CSS, WPT, Test262, and Acid gates accept `--shard INDEX/COUNT`; the harness uses a dynamically allocated loopback port so shards can execute concurrently (`ShardSpec`, `ConformanceServer`)
- **Windows compatibility CI** — every change restores, performs a transitive NuGet vulnerability audit, builds, runs the real executable unit suite, validates the profile, and exercises the curated CSS/WPT/Test262 and Acid1 gates. Tagged publication repeats those supported gates and publishes the compatibility report alongside the package (`ci.yml`, `publish-nuget.yml`)

#### Changed

- **Dependency foundation** — package versions are centrally pinned; AngleSharp is upgraded to 1.7.3, AngleSharp.Css to 1.0.2, Jint to 4.16.1, SkiaSharp to 3.119.0, and the optional LibVLC packages to their compatible current pins. Lite's CSS integration now uses the current typed-value API and preserves relative values for its own layout context (`Directory.Packages.props`, `Parser`, `CssUnits`, `DrawCommandExtensions`)
- **ES2020 host coverage** — Test262 execution classifies post-ES2020 features explicitly, supports the upgraded Jint host hooks for realms and buffer detachment, resolves module imports within the pinned test root, and permits only exact published dependency exceptions (`Test262Runner`, `Test262ModuleLoader`)
- **Exact test classification** — WPT survey exclusions and Test262 dependency exceptions are exact, case-sensitive test paths; broad substring skips are rejected by the profile validator and cannot be reported as passes (`WptRunner`, `ProfileRunner`)
- **Release metadata** — the NuGet package version is now source-controlled as `0.0.14`, and the publish workflow rejects any `lite_*` tag whose version does not match the project.

#### Fixed

- **Regression gate restored** — fixed-position painting, floats, float/absolute blockification, inline-block painting, replaced sizing, and line-box text alignment are again covered by the executable unit and curated CSS gates.
- **AngleSharp.Css upgrade compatibility** — unitless `line-height` no longer crashes computed-style conversion, relative CSS lengths remain available to Lite's layout engine, and authored empty `class` attributes remain distinguishable from absent attributes (`Parser`)
- **Test262 module containment** — relative imports may traverse sibling fixture directories inside the pinned Test262 root, but cannot escape that root (`Test262ModuleLoader`)
- **Warning-free clean build** — nullable handling now matches the upgraded CSS API and Win32 interop contract; table-cell initialization, Skia matrix passing, and nullable test assertions no longer produce compiler diagnostics.

#### Release-candidate evidence

- Unit suite: **190/190 passed**.
- Curated CSS 2.1 gate: **52/52 passed**.
- Curated WPT gate: **86/86 passed**.
- Applicable sparse Test262 gate: **1304/1304 passed**, with **747** post-ES2020 tests explicitly excluded and no dependency exceptions.
- NuGet audit: no known vulnerable direct or transitive packages; Release build: zero warnings and zero errors.
- Acid1 passes. Acid2 remains a published failing profile entry: the clean 0.0.14 build differs from the existing Lite baseline by 41,678 pixels, and its scrolled variant by 41,289 pixels.

#### Known limitations

- The generated compatibility report is intentionally `releaseReady: false`: the normative-clause inventory is incomplete, 17 mapped entries remain untested, one entry is failing, five are profile-excluded, WPT and Test262 are sparse, and the official CSS 2.1 suite is not yet vendored. Tagged releases publish this report as explicit compatibility evidence; they do not represent a full-conformance claim.
- Acid baselines are Lite-created regression artifacts, not independent standards-conformance evidence. The Acid2 baseline was not changed to conceal the current mismatch.

### CSS 2.1 inline-formatting pass

Second CSS 2.1 conformance pass, on inline formatting: **+125 upstream reftests**. The WPT
`css/CSS2` suite goes from **3917/6266 (62.5%)** to **4042/6266 (64.5%)**. `normal-flow`
479 → 537, `backgrounds` 184 → 215, `borders` 370 → 379, `css1` 46 → 54, `text` 254 → 260,
`floats-clear` 72 → 76, `linebox` 117 → 120 and `positioning` 314 → 317. Two directories give
ground, both for the same reason: nothing paints a border on an inline box yet, so
`margin-padding-clear` 528 → 526 and `box` 9 → 8 lose the tests that draw one.

#### Added

- **Line boxes have a strut (§10.8.1)** — every line box now begins with the zero-width inline box its block container implies, so a line holding only a 96px image is still at least as tall as the text that would have sat on its baseline. Three details the naive version gets wrong are handled: the strut's reach _below_ the baseline is negative when `line-height` is smaller than the font's ascent+descent (clamping it to zero stretched a `line-height: 10px` line back out); §9.4.2 keeps a line with no content, no forced break and no in-flow boxes at zero height, so a stray run of collapsible whitespace is not resurrected; and under `line-height: normal` the strut uses the font's own ascent+descent rather than the engine's more generous 1.4em, which would otherwise poke out above a 16px image (`BoxEngine`, `DrawCommandExtensions`)
- **Conformance guards** — the curated CSS 2.1 gate grows from 40 to 52 entries, promoting an upstream reftest for each fix below; `--geom` also reports the resolved background properties (`css21-manifest`, `RefTestRunner`)

#### Fixed

- **Inline replaced boxes reserve their edges (§8.4)** — an `<img>` on a line was measured at the bitmap's used size alone, so `img { padding-right: 20px }` advanced the line by nothing and the next image sat flush against it. The item is now built from the margin box, the node's own box starts inside its edges, and the image paints its background/border/outline like any other replaced box. This lands on two shared reftest references that the whole `height` / `min-height` / `max-height` family matches against, which is most of `normal-flow`'s gain (`BoxEngine`, `Drawer`)
- **`auto` margins on inline boxes (§10.3.1 / §10.3.2 / §10.6.2)** — `auto` has a used value of 0 on an inline box, replaced or not. The engine read it as the block-level "take the free space in the containing block" rule, so once inline margins were honoured an `img { margin-top: auto; margin-bottom: auto }` grew a 96px line box and left the bitmap floating in the middle of it (`BoxEngine`, `DrawCommandExtensions`)
- **Inline boxes reserve their horizontal margins and padding (§8.4)** — a non-replaced inline box's horizontal margins and padding take part in layout; only its vertical edges are ignored, and those only for the line box's height. The background is painted over the padding box so it covers what the line reserved. An inline box broken around a block child (§9.2.1.1) gets no edges rather than a duplicate set on every fragment, and neither does one under `direction: rtl`, whose edges belong to fragments a strictly left-to-right line cannot place (`BoxEngine`, `Drawer`, `DrawCommandExtensions`)
- **`letter-spacing` / `word-spacing` units (§16.4 / §16.5)** — both take any length, but only `px` and `em` were parsed by hand; `72pt`, `6pc` and `2.54cm` computed to 0, so every non-px spacing test drew its text unspaced (`DrawCommandExtensions`)
- **CSS colour keywords reaching the painter** — `SKColor.TryParse` understands hex only, so a keyword arriving through the engine's own cascade — which is how every colour pulled out of a shorthand looks — silently fell through to whatever AngleSharp had computed, normally a lower-specificity rule's colour. Keywords now resolve through AngleSharp's colour table (`DrawCommandExtensions`)
- **`background` shorthands AngleSharp drops (§14.2)** — AngleSharp.Css refuses a `background` whose position is a bare keyword and discards the whole declaration, so `background: bottom green` and `background: red url(x.png) right repeat-y` left the element with a weaker rule's background. The shorthand is read back out of the stylesheet source for rules where AngleSharp kept nothing of the background family, so a longhand written after the shorthand still wins. The source scan follows §4.2 error handling: backslash escapes and strings are not block delimiters, a declaration containing a block is malformed and dropped whole, and a selector that declares a background more than once is left alone because source order between them is not recoverable from a CSSOM rule (`Parser`)

#### Known limitations

- An inline box's **borders** are still not painted. Reserving room for them in the line and stroking them was measured against the suite and came out flat: it fixes the tests that expect a visible inline border and breaks the same number of block-in-inline ones, which need one fragment box per piece before the edges can land in the right places.

### CSS 2.1 broad conformance pass

CSS 2.1 conformance pass: **+1363 upstream reftests**. The WPT `css/CSS2` suite goes from
**2554/6266 (40.8%)** to **3917/6266 (62.5%)**. The largest movers are `selectors` 93 → 495,
`normal-flow` 183 → 479, `margin-padding-clear` 388 → 528, `positioning` 203 → 314,
`tables` 39 → 145, `borders` 296 → 370, `backgrounds` 136 → 184, `linebox` 82 → 117,
`generated-content` 99 → 130, `syntax` 180 → 210 and `floats-clear` 42 → 72. One directory
regressed: `abspos` 7 → 5, both cases styling the root element itself as a fixed-position
table.

#### Added

- **Stylesheet character encoding (§4.4)** — a stylesheet is decoded by its byte-order mark, then an `@charset` rule, then the HTTP `Content-Type` charset, then the linking element's `charset` attribute, then the referring document's or importing sheet's encoding, then UTF-8; the encoding a sheet resolves to becomes the referrer charset for what it imports. Legacy code pages (Shift_JIS, windows-125x, koi8-r, iso-8859-x) are registered via `System.Text.Encoding.CodePages`, which .NET Core does not ship in the shared framework (`Parser`)
- **`::first-letter` as a real inline box (§5.12.1)** — the pseudo-element is generated during parsing instead of being re-drawn at paint time, so it contributes its font metrics, `line-height` and width to the line box and paints every property (background, border, margins, …) through the ordinary inline path. The old paint-time version supported only `color` / `font-size` / `font-weight` and never affected layout. Upstream `css/CSS2/selectors`: 93/545 → 495/545 (`Parser`, `Drawer`)
- **Static position for out-of-flow boxes (§10.3.7 / §10.6.4)** — with `left` / `top` auto, an absolutely positioned box is placed where it would have been in normal flow: the flow pass records the position each out-of-flow child passes over, and one inside an inline run rides along on the line as a zero-sized marker (so it also no longer splits that line in two). Previously such a box was pinned to its containing block's origin (`BoxEngine`, `LayoutNode`)
- **Text flows around floats line by line (§9.5)** — each line box gets the band the floats leave at its own vertical position, wrapping is measured per line, and the bands are recorded on the text node so painting breaks the lines exactly where layout did. Rule 7 is implemented too: a line too narrow for its first word or atomic box shifts down past the float instead of overflowing (`BoxEngine`, `TextMeasure`, `Drawer`)
- **Conformance guards** — the curated CSS 2.1 reftest gate grows from 22 to 29 entries, promoting upstream tests for each fix below (`css21-manifest`)

#### Fixed

- **`font-size` compounding (§15.7)** — `font-size` computes to a length and descendants inherit _that_; the engine re-resolved the inherited specified value (`2em`) at every level, doubling the size again each time. A four-deep subtree under one such rule reached 512px. Each element's own cascaded declaration (inline style, then author/UA rules by `!important`, specificity and source order, including the `font` shorthand) is now resolved once against the parent's computed size (`Parser`, `DrawCommandExtensions`, `LayoutNode`)
- **Pixel snapping for square backgrounds and borders** — a box landing on a fractional coordinate painted a half-covered anti-aliased fringe row, while replaced content in the same place paints whole pixels. Square fills now snap like a browser; rounded corners keep anti-aliasing (`Drawer`)
- **Explicit zero sizes** — `width: 0` / `height: 0` were read as "no size specified" (layout tested `GetWidth() > 0`), so a zero-width box filled its container and a zero-height box grew to its content (`BoxEngine`, `DrawCommandExtensions`)
- **Invalid negative lengths** — a negative `width`, `height`, `min-`/`max-` pair or `border-width` is invalid, so the declaration is dropped and the property keeps its initial value; `border-top-width: -1px` beside a visible `border-top-style` now paints the initial `medium` (`DrawCommandExtensions`)
- **Root-relative URL resolution** — `Uri.TryCreate(…, UriKind.Absolute)` accepts `/fonts/ahem.css` as an implicit `file:` URI on Unix, so every root-relative stylesheet, image and form action was handed to the HTTP client unresolved. All resolution goes through a shared `UrlUtils` that treats only a real scheme as absolute (`UrlUtils` and all callers)
- **Auto margins on absolutely positioned boxes (§10.3.7 / §10.6.4)** — with the offsets and the size all given, an auto margin takes the space the containing block has left over and two of them split it, which is how such a box is centred; they used to compute to zero. Rule 1's static position also follows `direction`, so an RTL containing block puts a box with both offsets auto flush right. Auto-margin detection reads the node's own resolved value, so a margin set by script or by the engine's cascade counts (`BoxEngine`, `DrawCommandExtensions`)
- **`background` shorthand and its longhands (§14.2)** — the shorthand is expanded from the rule's declared text (tokens classified by shape, so order does not matter) and resets the components it omits; `background-repeat` and `background-position` no longer pass AngleSharp's literal `"initial"` and tuple form `"(initial, 50%)"` through to the painter, which read as "no repeat" and "position 0" respectively (`Parser`, `DrawCommandExtensions`)
- **`min-width` / `max-width` on in-flow blocks (§10.4)** — only the absolutely-positioned path clamped, so neither property had any effect on a normal block; a box the clamp gives a known width to now centres under auto margins like an explicitly-sized one (`BoxEngine`)
- **Replaced-element sizing (§10.3.2 / §10.6.2)** — a CSS `width`/`height` on an inline `<img>` is honoured (the inline path read only the intrinsic pixel size), and HTML's `width`/`height` content attributes are treated as presentational hints for those properties rather than as the intrinsic size — reading them as intrinsic dropped percentages outright and mixed axes, so `width="100%" height="50"` on a 1×1 image derived a height of 39200px (`BoxEngine`, `Parser`)
- **Anonymous table boxes track the DOM** — the normalization pass only ever ADDED wrappers, so it could not follow a change to a display value: setting a cell to `display: none` wrapped it in an anonymous cell + table (a `display: none` box is not a proper table child) and restoring `table-cell` left the wrappers behind. Each pass now dissolves the boxes it generated before regenerating them. The box generated around cells misparented inside an _inline_ box is an `inline-table` per §17.2.1, so cells written between inline text no longer break the line (`BoxEngine`)
- **Absolutely positioned tables and flex containers** — an out-of-flow box laid its children out as plain blocks whatever its own display was, so an abs-pos `<table>` flowed its rows and cells as inline content. Child layout now dispatches on the box's own display, as the in-flow path does (`BoxEngine`)
- **`cellspacing` / `cellpadding`** — the HTML 4 §11.2.1 presentational hints were ignored, so the UA defaults (2px `border-spacing`, 1px cell padding) stayed in place and a `cellpadding="0" cellspacing="0"` table sat 3px lower and wider than its CSS equivalent (`Parser`)
- **`! important` with whitespace after the bang** — the declaration parser matched only the literal token, so the spaced form was read as part of the value (`Parser`)
- **Anonymous table boxes** — a synthesized box borrows its originating element's style object, so a parent's non-inherited `position: absolute` or `float` read back as its own and took it out of flow; a table's intrinsic width came out zero because its rows are neither block-level nor inline. Both are fixed, so an anonymous table inside a float or an abs-pos box now shrink-wraps and lays its cells out side by side (`LayoutNode`, `IntrinsicSizer`, `BoxEngine`)
- **Attributes** — every authored attribute reaches the layout node, so attribute selectors, `getAttribute` and `attr()` in `content` see what the markup declared; only `data-*` and a per-tag whitelist used to survive (`Parser`)
- **`line-height: 0`** — text measurement used 0 as the sentinel for "unspecified" and silently substituted the 1.4em default (`TextMeasure`)
- **`letter-spacing` / `word-spacing` in layout** — measured with the plain font metrics while being painted with the spacing applied, so the box reserved for the text was too narrow and following inline boxes sat in the wrong place (`TextMeasure`, `BoxEngine`)
- **Preformatted newlines in an inline run (§16.6)** — a newline under `pre` / `pre-wrap` / `pre-line` is a forced break; an inline run measured the whole string as one item, so a preformatted `<span>` drew its lines on top of each other (`BoxEngine`)
- **Inline fragments** — an inline box broken over several line boxes now has one fragment per line, so its background and text paint on each rather than only the last (`LayoutNode`, `BoxEngine`, `Drawer`)
- **Whitespace under `pre`** — trimming an element's text implements §16.6.1's "remove the spaces at the start and end of a line", so it must not apply where white-space is preserved: a cell holding a single space is a space wide, not zero (`Parser`)
- **CDATA-wrapped inline scripts** — XHTML wraps inline scripts in `<![CDATA[ … ]]>`; those characters are not JavaScript, so the script failed to parse on its first token (`Parser`)
- **Crash parsing a lone quote in `content`** — the value both starts and ends with a quote, and stripping one off each end asked for a negative-length slice (`Parser`)
- **Restored §9.7 / §10.3.2 / §9.5.1 layout work** that had been reverted while its unit tests stayed in the tree: float and abs-pos blockification, replaced-box intrinsic sizing, `position: fixed` painting, inline-block background/border painting, and a float joining the current line box

## [0.0.13] - 2026-08-05

### Added

- **`::first-letter` / `::first-line` pseudo-elements** — pseudo-element rules (`::before` / `::after` / `::first-letter` / `::first-line`) are lifted out of the cascade so they no longer style the originating element, and apply only to the generated/partial boxes; `::first-letter` matches the first letter plus surrounding punctuation (Unicode Ps/Pe/Pi/Pf/Po) per CSS 2.1 §5.12.1, with punctuation-only text yielding no styled run; iframe documents keep the parent document's pseudo-element rules (`Parser`, `Drawer`)
- **Whitespace collapsing (CSS 2.1 §16.6)** — newlines and tabs in source text collapse to a single space under normal `white-space` processing, while NBSP and `pre` / `pre-line` line breaks are preserved (`Parser`)
- **Conformance CLI `--render`** — `Lite.Conformance --render <url> [name]` dumps a headless page render to `artifacts/<name>.png`; new CSS 2.1 paint reftests cover border-vs-background crisp edges, `text-align` line-box alignment, intrinsic sizing of block-level replaced elements, and `position: fixed` painting; the survey mode gains pass/fail listing controls via `LITE_SURVEY_FAIL_LIMIT` / `LITE_SURVEY_LIST_PASSES` (`Program`, `RefTestRunner`, `css21-manifest`)

### Fixed

- **Block-in-inline splitting (§9.2.1.1)** — in-flow blocks are now hoisted out of inline boxes to a sibling position (proper anonymous-block wrapping) instead of promoting the inline box to a block; handles multiple split blocks and margin collapsing; Acid2 baselines re-approved (`BoxEngine`)
- **Inline-block baseline & painting (§10.8.1)** — inline-blocks align on the baseline of their last line box rather than their bottom edge; an empty sized inline-block now paints its background/borders (it was previously invisible); `display: inherit` on pseudo-elements resolves to the originating element's display; boundary spaces before generated content are preserved (`BoxEngine`, `Drawer`, `Parser`)
- **`visibility: hidden` painting (§11.2)** — an invisible box no longer paints its own background, borders, text or replaced content, but still paints descendants that declare `visibility: visible`, and registers no hit regions (`Drawer`, `DrawCommandExtensions`)
- **`position: fixed` painting** — fixed boxes were laid out correctly but never painted (the fixed pass re-entered the code path that skips them); they now render (`Drawer`)
- **Float/abspos blockification (§9.7)** — floated or absolutely-positioned boxes blockify their specified `display`: `inline-table` → `table`, `inline` / `inline-block` / internal table types → `block` (`DrawCommandExtensions`)
- **`border` shorthand default width (§8.5.1)** — an omitted border width now computes to `medium` instead of zero, so `border: solid red` shows a border (`DrawCommandExtensions`, `Parser`)

## [0.0.12] - 2026-07-21

### Added

- **True intrinsic sizing** — new `IntrinsicSizer` computes real `min-content` / `max-content` widths (longest unbreakable unit vs. never-wrapped line, with `<br>` forcing fresh lines), so floats, absolutely-positioned boxes and other shrink-to-fit contexts size per CSS 2.1 §10.3.5/§10.3.7 instead of the old "widest explicit child" approximation; percentages resolve against an indefinite basis during the pass (`IntrinsicSizer`, `BoxEngine`)
- **Anonymous table boxes & `inline-table`** — bare text, rows or cells inside table display types are wrapped in generated anonymous table boxes per CSS 2.1 §17.2.1; `display: inline-table` lays out as an atomic inline-level box; `display: table-row-group` works on non-`TBODY` elements (`TableEngine`, `BoxEngine`)
- **Constructible `EventTarget`** — `new EventTarget()` with `addEventListener` / `removeEventListener` / `dispatchEvent`; `{ once: true }` listeners are removed before invocation; `dispatchEvent` rejects non-Event arguments (TypeError) and uninitialized events (InvalidStateError); a listener removed mid-dispatch no longer runs (DOM "inner invoke") (`JsEventTarget`, `EventDispatcher`)
- **Legacy event APIs** — `Event.initEvent(type, bubbles, cancelable)`, `event.returnValue`, `srcElement`, `cancelBubble`, and `isTrusted`; `document.createEvent()` accepts the legacy alias table and throws NotSupportedError otherwise (`JsEvent`, `JsDocument`)
- **JS-catchable host errors** — `JsErrors` builds `DOMException` / `TypeError` objects (with `name` / `message` / `code` / `constructor`) from host code without re-entering the engine, so WPT testharness `assert_throws_dom` / `assert_throws_js` checks pass (`JsErrors`)
- **CharacterData API** — `data` / `length` / `nodeValue` and `appendData` / `insertData` / `deleteData` / `replaceData` / `substringData` on text, comment and processing-instruction nodes; `document.createComment()` / `createProcessingInstruction()`; constructible `new Comment(data)` / `new Text(data)` globals; comments and PIs serialize in `innerHTML`; CharacterData nodes reject children with HierarchyRequestError (`JsElement`, `JsDocument`, `HtmlSerializer`)
- **Document lifecycle events** — `document.readyState` (`loading` → `interactive` → `complete`), with `DOMContentLoaded` fired after parsing and deferred scripts, before async scripts and the `load` event; the document itself is an EventTarget whose listeners join the normal capture/bubble path (`JsEngine`, `Parser`, `JsDocument`)
- **Spec-correct DOM insertion** — `before()` / `after()` / `replaceWith()` anchor at the "viable previous/next sibling" (DOM §4.2.8), so the target node itself may be one of the arguments; non-node arguments now stringify per WebIDL (`null` → `"null"`) (`JsElement`)
- **WPT survey mode** — the conformance runner can survey WPT pages against baselines to measure pass rates (`WptRunner`)

### Fixed

- **First-child margin collapse chains (§8.3.1)** — a block's effective top margin now collapses with the whole chain of first in-flow block children (stopping at border/padding, BFC boundaries, or inline content), so nested margins materialize as space above the outermost block (`BoxEngine`)
- **Paint order (Appendix E)** — in-flow atomic inline-level boxes (images, inline-blocks) now paint above the backgrounds of later block boxes, as the CSS 2.1 painting order requires (`Drawer`)
- **Acid2 baselines** re-approved for the intrinsic-sizing corrections

## [0.0.11] - 2026-06-28

### Added

- **`<audio>` / `<video>` (HTMLMediaElement)** — `play()` (returns a Promise) / `pause()` / `load()`, `currentTime`, `duration`, `paused`, `ended`, `readyState`, `volume`, `muted`, `autoplay`, `loop`, `controls`, `preload`, `poster`, `src` / `currentSrc`, `networkState`, and `canPlayType()`; media events fire in spec order (loadedmetadata → canplay → play → playing → timeupdate → ended), with `<source>` selection, `poster`, and `autoplay` (`JsElement`, `Parser`)
- **Pluggable media backend** — new `IMediaBackend` with a default decoder-free `SimulatedMediaBackend` that drives a deterministic timeline on the page's task queue (so the API, event order, and controls work and are testable without native codecs), selected via a `MediaBackends` factory (`Lite/Media`)
- **Real LibVLC backend (`Lite.Media`)** — a new optional project with `VlcMediaBackend` (LibVLCSharp + bundled native codecs) that decodes real audio/video: audio plays through the system device and video frames are decoded to RGBA → `SKBitmap` composited into the element's box. Call `Lite.Media.Vlc.VlcMedia.Register()` at startup to enable it (the Example app does); it falls back to the simulated backend if the native libraries are unavailable
- **Media controls UI** — `<video>` paints its poster/frame and, when `controls` is set, a controls bar (play/pause glyph + progress + time); `<audio controls>` renders a compact strip. Clicking the bar toggles play/pause in the live window (`Drawer`, `BrowserWindow`)
- **Tests + demo** — new `MediaTests` (canPlayType, attribute reflection, `<source>` selection, play→ended event order, pause, autoplay, render), a WPT-style `lite/media.html` gate, and an **Audio & Video** Example demo page
- **Known limitation** — the conformance suite/tests run against the simulated backend (deterministic, no native codecs); the LibVLC backend builds and deploys its native libs but its playback was not verified in the headless CI environment. `<track>`/WebVTT cues are not yet parsed

## [0.0.10] - 2026-06-28

### Added

- **`<iframe>` / nested browsing contexts** — an iframe parses its child document (from `srcdoc` or a same-origin `src`) into an independent `Page` with its own layout tree and JS engine, rendered clipped into the frame box (default 300×150). The host event loop pumps the whole page tree so child timers, observers, and messages run (`Parser`, `Drawer`, `BrowserWindow`)
- **Cross-context JS wiring** — `iframe.contentWindow` (a WindowProxy) and `contentDocument` (same-origin); a child's `window.parent` / `top` / `frameElement`; `window.postMessage` round-trips between parent and child, delivering a `message` event with `data` / `origin` / `source` (`JsWindowProxy`, `JsElement`, `JsEngine`)
- **iframe `load` event** — fired on the iframe element after its child document finishes loading (`Parser`, `EventDispatcher`)
- **`Page` abstraction** — bundles a browsing context's root layout tree, JS engine, document, base URL, and viewport; the first step in replacing the Parser/Drawer/JsEngine static singletons. DOM proxies now resolve their owning engine via `JsEngine.For(rawEngine)` instead of the global `Instance`, so multiple pages coexist (`Page`, `JsEngine`)
- **Acid2 (partial) + gate** — the Acid2 test and its `position:fixed` scroll variant render deterministically and are gated against approved baselines (`baselines/acid2.png`, `baselines/acid2-scrolled.png`). The render is a recognizable smiley (head, eyes, scalp, chin); the mouth/nose detail awaits the deferred CSS 2.1 anonymous-box / margin-collapse work. The harness scrolls to `#top` (as following the in-page link would) so the face comes into view (`AcidRunner`, `RefTestRunner`)
- **`<object>` nested fallback** — an `<object>` renders its `data` resource as a replaced image; when the resource can't be displayed it falls through to its child content, which may be a nested `<object>` (Acid2's eyes are a 3-deep chain) (`Parser`, `Drawer`, `BoxEngine`)
- **`background-attachment: fixed`** — fixed backgrounds are positioned relative to the viewport and clipped to the element box, so they stay put as the element scrolls (`Drawer`, `DrawCommandExtensions`)
- **Appendix/alternate stylesheets** — `<link>` elements whose `rel` token set contains `stylesheet` (e.g. `rel="appendix stylesheet"`) are loaded, including `data:` CSS hrefs; `alternate` stylesheets are skipped (`Parser`)
- **min/max-width & min/max-height clamping for absolute/fixed boxes** — `ResolveAbsoluteBox` clamps the resolved width/height to the min/max box (min wins over max, CSS 2.1 §10.4/§10.7), and approximates shrink-to-fit width from the widest explicit child width instead of defaulting to half the containing block (`BoxEngine`)
- **Tests** — new `IframeTests` (srcdoc child page, default sizing, child rendering, contentDocument, postMessage round-trip, parent/frameElement) with a WPT-style `lite/iframe.html` gate and an **Iframes** Example demo page; new `AcidPrereqTests` covering percent-encoded `data:` images, straight-alpha PNG decode, `<object>` nested fallback, `background-attachment: fixed`, and max-width clamping

### Fixed

- **AngleSharp periodic-value crash** — reading certain periodic/invalid declarations (e.g. `border-color: red yellow black yellow`) threw a `NullReferenceException` deep in AngleSharp.Css; all property reads now go through `GetPropertyValueSafe`, which swallows the failure and ignores the declaration (CSS error recovery) (`DrawCommandExtensions` and all callers)
- **Percent-encoded base64 `data:` images** — `data:image/...;base64,` payloads whose `/` and `=` are percent-encoded (`%2F`, `%3D`, as Acid2 encodes them) now decode correctly (`DataUri`, `ResourceLoader`)
- **Alpha PNG compositing** — images decode with straight (un-premultiplied) alpha so partly-transparent PNGs composite correctly with source-over (Acid2's eyes are two offset transparent PNGs that must overlap into solid yellow) (`ResourceLoader`)
- **Debug logging removed** — the parser's per-element and per-property `[CSS]` console spam (previously always on) is gone (`Parser`)

### Known limitations

- iframe hit-testing does not yet route clicks into child frames (parent-level UI works); cross-document navigation does not dispose child pages; a child's _runtime_ class-based restyle reads the active page's cascade (its initial render is fully correct); nested-frame `top` is approximated as `parent`

## [0.0.9] - 2026-06-25

### Added

- **Script execution timing** — classic scripts now run in the correct order: in-position (inline + external without `defer`/`async`) scripts execute in document order during parse, `defer` scripts run after parsing in document order, and `async` scripts are queued on the event loop in any order; ES modules remain deferred per spec (`Parser`)
- **`document.write()`** — `document.write` / `writeln` / `open` / `close` parse markup and append the resulting nodes to `<body>` (`JsDocument`)
- **`<template>` element** — parsed as an inert fragment; `template.content` exposes a `DocumentFragment` holding the parsed children, which are not rendered or laid out (`Parser`, `JsElement`)
- **`<dialog>` element** — `show()`, `showModal()`, `close(returnValue)`, `returnValue`, `validationMessage`, and `setCustomValidity()`; the `open` attribute drives visibility (`JsElement`, `BoxEngine`)
- **`FormData` API** — `new FormData(form)` enumerates a form's successful controls; entries, `get`, `getAll`, `append`, `set`, `delete`, `has` (`JsFormData`)
- **`<progress>` / `<meter>` / `<output>`** — rendered form elements with `value`, `min`, `max`, `low`, `high`, `optimum`, and `for` attributes; `<output>` reflects its referenced controls (`Drawer`, `FormLayout`, `JsElement`)
- **`<details>` open/close collapse** — toggling the `open` attribute re-flows the disclosure content in/out of the layout tree (`BoxEngine`, `JsElement`)
- **`ResizeObserver`** — `new ResizeObserver(cb)`, `observe`/`unobserve`/`disconnect`, `contentRect` entries delivered on the event loop (`JsResizeObserver`)
- **`IntersectionObserver`** — `new IntersectionObserver(cb, options)`, `observe`/`unobserve`/`disconnect`, `isIntersecting` / `intersectionRatio` entries (`JsIntersectionObserver`)
- **`WheelEvent` / `PointerEvent`** — `wheel` dispatched with `deltaX/Y/Z`; pointer events (`pointerdown`/`up`/`move`) dispatched alongside mouse events with `pointerId`, `pointerType`, `pressure` (`JsEvent`, `BrowserWindow`)
- **`<input type="file">`** — native Win32 file open dialog (`Comdlg32`); `input.files` (`FileList` with `name`, `size`, `type`) and `input.value` reflect the selection (`JsFileList`)
- **`multipart/form-data` submission** — `enctype="multipart/form-data"` builds an RFC 7578 body with file parts (filename + content-type); `application/x-www-form-urlencoded` and GET query encoding retained (`FormSubmitter`)
- **`Attr.value` mutation** — setting `attr.value` updates the owning element's attribute and triggers mutation observers (`JsNamedNodeMap`)
- **Tests** — expanded `DomTests`, `FormTests`, and `LayoutTests` suites covering template, dialog, FormData, progress/meter/output, details, observers, and file inputs

### Fixed

- **Self-collapsing margins** — the margin-collapse model now follows CSS 2.1 §8.3.1, including the self-collapsing case (margins of a block whose top/bottom margins adjoin collapse through) (`BoxEngine`)
- **Block-in-inline margin suppression** — margins on block boxes split by inline content are suppressed per §9.2.1.1, preventing spurious vertical spacing in split-inline formatting (`BoxEngine`)

## [0.0.8] - 2026-06-23

### Added

- **Conformance test harness** — new `Lite.Conformance` project with runners for CSS 2.1 reftests, Web Platform Tests, test262, and the Acid tests; headless page rendering, pixel-diff comparison against approved baselines, mismatch-reference support, and a survey mode (`AcidRunner`, `RefTestRunner`, `WptRunner`, `Test262Runner`, `HeadlessPage`, `PixelDiff`, `ConformanceServer`)
- **`window.location`** — `href`, `protocol`, `host`, `hostname`, `port`, `pathname`, `search`, `hash`, `origin`, and `assign` / `replace` / `reload` setters; same-document (fragment-only) changes scroll to the fragment and fire `hashchange` without reloading (`JsLocation`)
- **`window.history`** — `length`, `state`, `back` / `forward` / `go`, `pushState` / `replaceState`; `popstate` events dispatched to window listeners (`JsHistory`)
- **URL APIs** — `URL` (parsing, `href`/`origin`/`pathname`/`search`/`hash` components, `searchParams`) and `URLSearchParams` (`get`, `getAll`, `append`, `set`, `delete`, `has`, `entries`, `toString`) (`JsUrl`, `JsUrlSearchParams`)
- **`navigator`** — `userAgent`, `platform`, `language` (`JsNavigator`)
- **All CSS length units** — centralized `CssUnits` resolver supporting `px`, `em`, `rem`, `%`, `vw`, `vh`, `vmin`, `vmax`, `ex`, `ch`, `pt`, `pc`, `in`, `cm`, `mm`, and `q` across all length-resolving properties (`CssUnits`)
- **`currentcolor` keyword** — `color: currentcolor` resolves against the inherited `color` value (`DrawCommandExtensions`)
- **`MutationObserver`** — `observe(target, options)`, `disconnect`, `takeRecords`; records for `childList`, `attributes`, and `characterData` mutations with the spec's microtask delivery (`JsMutationObserver`)
- **Attribute API** — `element.attributes` as a live `NamedNodeMap`, `getAttributeNode` / `setAttributeNode` / `removeAttributeNode`, and `Attr` nodes with mutable `value` / `name` (`JsNamedNodeMap`, `JsElement`)
- **Geometry probing & DOM layout hooks** — layout nodes carry geometry used by `getBoundingClientRect` and the conformance reftest harness; `JsElement` exposes resolved geometry to JavaScript (`JsEngine`, `JsElement`, `BoxEngine`)
- **`data:` URL parsing** — `DataUri` decodes `data:` URLs (base64 and percent-encoded) for use by `fetch` and image loading (`DataUri`)
- **`XMLHttpRequest` improvements** — async send on a background thread with `readyState`, `status`, `statusText`, `responseText`, `responseURL`, and `onload` / `onerror` / `onreadystatechange` callbacks (`JsXmlHttpRequest`)
- **Acid1 compliance** — the Acid1 reference renders correctly and an 800×600 baseline is approved (`baselines/acid1.png`)
- **Tests** — new `AttributeApiTests`, `CssWideKeywordTests`, `HostObjectTests`, `LayoutTests`, `MutationObserverTests`, and `NavigationTests` suites; `Lite.Example` renamed from `Example`

### Fixed

- **Unitless `line-height`** — a bare-number computed `line-height` (e.g. `font: 10px/1`) is now treated as a font-size multiplier instead of a pixel length, which had collapsed every line box to ~1px and overlapped the Acid1 headings (`BoxEngine`)
- **Box / table / CSS parser / drawer** — a series of correctness improvements to the box model, table column sizing, CSS parser, and renderer landed alongside the conformance work

## [0.0.7] - 2026-06-10

### Added

- **In-page navigation** — clicking a same-origin `<a href>` (and submitting a form) now loads the target document _in place_ instead of opening the OS browser; links to a different origin still open in the system default browser, and pure `#` fragment links are ignored. The document load runs on a background thread so the UI stays responsive
- **Navigation loading animation** — a browser-style indeterminate progress bar sweeps across the top of the window over a dimmed snapshot of the outgoing page while the next page is fetched/parsed/rendered; the page is then revealed with a short cross-fade + slide-up. Interaction is frozen for the duration so the (single-threaded) JS engine is never touched concurrently (`LoadingAnimation`, `PageTransition`)
- **`fetch()`** — Promise-based `fetch(url, options)` with `method` and `body`; the response exposes `ok`, `status`, `statusText`, `url`, `.text()`, and `.json()`. The HTTP request runs on a background thread and the callback is marshaled back onto the event loop; `http(s)` and `data:` URLs supported (`JsFetch`)
- **Web Storage** — `localStorage` (persisted per-origin under `%LocalAppData%/Lite/Storage`) and `sessionStorage` (in-memory for the process); `getItem`, `setItem`, `removeItem`, `clear`, `key(i)`, `length` (`JsStorage`)
- **ES modules** — `<script type="module">`, `import` / `export`, for both inline and `src` modules; specifiers resolved relative to the page base URL and fetched over `http(s)` (`HttpModuleLoader`); classic scripts run first, modules deferred per spec
- **JavaScript event loop** — a macrotask queue drained on the UI thread so timers and `fetch` callbacks never touch Jint off-thread; a Promise microtask checkpoint runs after each task (so `.then()` continuations fire); `queueMicrotask` polyfill installed
- **Form submission** — a `<form>` submits on Enter (from a text input) or when a submit control is activated; a cancelable `submit` event is dispatched first, then an `application/x-www-form-urlencoded` query is built from the form's successful controls and the engine navigates to the resolved `action` (GET query appended). `form.submit()` / `form.reset()` from JavaScript (`FormSubmitter`)
- **Constraint validation** — `required`, `type="email"` / `type="url"`, and `pattern` are validated; `element.validity` (`ValidityState` with `valueMissing` / `typeMismatch` / `patternMismatch` / `valid`), `willValidate`, `checkValidity()`, `reportValidity()` (`FormValidation`)
- **Keyboard events** — `keydown` / `keyup` dispatched to the focused element (or `<body>`) with `key`, `keyCode`, `code`, and `ctrlKey` / `shiftKey` / `altKey` / `metaKey` modifiers
- **Mouse events** — `mousedown` / `mouseup` / `mousemove` dispatched with `clientX/Y`, `pageX/Y`, and `button`
- **Real `innerHTML` / `outerHTML`** — the getters serialize the live node subtree (`HtmlSerializer`, with HTML escaping and void-element handling); the setters parse an HTML fragment (`Parser.ParseFragment`) against the page's full stylesheet cascade and rebuild the children. Added **`insertAdjacentHTML(position, html)`** (`beforebegin` / `afterbegin` / `beforeend` / `afterend`)
- **DOM mutation convenience methods** — `append`, `prepend`, `before`, `after`, `replaceWith`, `remove` (accepting `Element`s or strings), with stylesheet cascade re-resolution applied to inserted subtrees via `StyleResolver`
- **`MouseEvent` / `KeyboardEvent` / `CustomEvent` constructors** — `new CustomEvent(type, { detail })`, `new Event(type, { bubbles, cancelable })`; `event.detail` payload
- **Element form members** — `element.type`, `element.name`, and `element.form` (nearest ancestor `<form>`)
- **Document additions** — `document.URL`, `document.domain`, `document.cookie` (single in-memory jar), `createElementNS`
- **Expanded CSS selectors** — `:empty`, `:checked`, `:disabled`, `:enabled`, `:link`, `:visited` (never matched — no history), `:required`, `:optional`, `:valid`, `:invalid`; the dynamic `:hover` / `:focus` / `:active` classes now also match on detached (parentless) elements
- **Tests** — new `Lite.Tests` project with a lightweight probe/runner and cascade, DOM, event-loop, and form suites (`InternalsVisibleTo` granted to the test project)
- **Multi-page Example site** — the single demo page is split into a navigable site (typography, colors, layout, lists & tables, forms, graphics, transforms & animations, JavaScript DOM) that exercises in-page navigation, the loading animation, `fetch`, Web Storage, modules, form submission, and validation

### Fixed

- **Timer thread-safety** — `setTimeout` / `setInterval` previously invoked the JS callback directly from a `System.Threading.Timer` background thread (and `setTimeout(fn, 0)` fired synchronously during bootstrap); callbacks are now queued onto the UI event loop, since Jint is not thread-safe
- **`Jint` version pinned** — the package reference moved from `3.*` to `3.1.6` for reproducible builds

## [0.0.6] - 2026-04-16

### Added

- **`linear-gradient()`** — `background-image: linear-gradient()` support with angle keywords (`to top/right/bottom/left`, diagonals), degree values, and multi-stop color lists; rendered via `SKShader.CreateLinearGradient` with correct CSS angle convention
- **CSS `transform`** — `rotate()`, `scale()`, `scaleX()`, `scaleY()`, `translate()`, `translateX()`, `translateY()`, `skew()`, `skewX()`, `skewY()` parsed into a composed `SKMatrix`; applied around the element's center (transform-origin: center); deg/rad/turn units supported
- **CSS `filter`** — `blur()` (via `SKImageFilter`), `grayscale()`, `sepia()`, `brightness()`, `contrast()`, `saturate()`, `hue-rotate()`, `invert()`, `opacity()` (all via `SKColorFilter` color matrices); multiple filters composed; applied as a `SaveLayer` paint
- **`text-overflow: ellipsis`** — single-line overflow text truncated with `…` when `overflow: hidden` and `white-space: nowrap`; truncation point found via binary search in `TextMeasure`
- **`position: sticky`** — element sticks at its `top` offset within the viewport while remaining within its parent container; sub-pixel clamping prevents sticking beyond the container bottom
- **`aspect-ratio`** — `width / height` and single-value (`1.5`) syntax; height is derived from content width when no explicit height is set
- **`pointer-events: none`** — hit regions are not registered for the element; mouse events pass through to elements underneath
- **`element.dataset`** — `JsDataset` proxy reads and writes `data-*` attributes with camelCase ↔ kebab-case conversion
- **`animation-play-state`** — `running` / `paused`; pausing freezes elapsed time; resuming continues from the frozen point; toggling via JavaScript works correctly
- **`window.scrollTo(x, y)` / `window.scrollBy(dx, dy)`** — programmatic viewport scrolling; `window.scrollY`, `window.scrollX`, `window.pageXOffset`, `window.pageYOffset` read-only properties
- **`autofocus` attribute** — first element with `autofocus` receives focus automatically after page load
- **Animation lifecycle events** — `animationstart`, `animationend`, `animationiteration` fired on the element; `transitionend` fired when a CSS transition completes; events dispatched through the full capture/bubble chain via `EventDispatcher`

## [0.0.5] - 2026-03-23

### Added

- **`text-transform`** — `uppercase`, `lowercase`, `capitalize`, `none`
- **`letter-spacing`** — character-level spacing with custom draw/measure routines
- **`word-spacing`** — additional space between words
- **`text-indent`** — first-line indent for block text
- **`border-style`** — `solid`, `dotted`, `dashed`, `double`, `groove`, `ridge`, `inset`, `outset`, `none` per side
- **`list-style-type`** — `disc`, `circle`, `square`, `decimal`, `lower-alpha`, `upper-alpha`, `lower-roman`, `upper-roman`, `none`
- **`list-style-position`** — `outside` (default) and `inside` with proper text offset
- **`outline`** — `outline-width`, `outline-color`, `outline-style`, `outline-offset` shorthand and individual properties
- **`background-image`** — `url()` references to raster images (PNG, JPEG) with `background-repeat` (`repeat`, `repeat-x`, `repeat-y`, `no-repeat`), `background-position`, and `background-size` (`cover`, `contain`, `auto`, px/%)
- **`vertical-align`** — `baseline`, `top`, `middle`, `bottom`, `text-top`, `text-bottom`, `sub`, `super` for inline elements
- **`::before` / `::after` pseudo-elements** — CSS `content` property with quoted strings, `open-quote`/`close-quote`, and CSS unicode escape sequences (`\201C`, `\25B6`, etc.); pseudo-element styles (color, font-weight, font-size, display) applied via `StyleOverrides`
- **`border-collapse`** — `collapse` and `separate` on tables
- **`border-spacing`** — horizontal and vertical spacing between table cells
- **Form: `input[type=password]`** — masked text display with bullet characters
- **Form: `input[type=number]`** — numeric input with clickable up/down stepper arrows; respects `min`, `max`, `step` attributes
- **Form: `input[type=range]`** — range slider with click-to-set and mouse drag support; respects `min`, `max`, `step`
- **Form: `input[type=radio]`** — radio button circles with group selection logic (only one per `name` group); proper intrinsic sizing in both inline and flex layout
- **Form: `<textarea>`** — multi-line text input with placeholder, monospace font, word wrapping, and Enter key support for new lines
- **Form: `<select>`** — dropdown select with option list overlay drawn on top of all content; click to open/close; option selection updates displayed value
- **CSS shorthand parsing** — `border-style`, `outline`, `list-style` shorthands decomposed into individual properties

### Fixed

- **Pseudo-element text overlap** — `::before`/`::after` content was drawn on top of the parent's text; now the parent's text is moved into a `#text` child node so all content flows together as inline children
- **CSS unicode escapes** — `ParseContentValue` now decodes CSS escape sequences like `\201C` (left quote) and `\25B6` (triangle) into actual characters via `DecodeCssEscapes`
- **Background image loading** — `DrawBackgroundImage` passed `null` as the base URL to `ResourceLoader.FetchImage`, so relative image paths couldn't resolve; now passes `Parser.BaseUrl`
- **Number input steppers** — click hit region for the text area covered the entire input including the arrow buttons; now the text hit region excludes the 16px arrow zone
- **Range slider stuck dragging** — drag was initiated on mouse-up instead of mouse-down, causing the slider to follow the mouse until the next click; moved drag initiation to `WM_LBUTTONDOWN`
- **Radio button sizing in flex containers** — `FlexEngine.MeasureIntrinsicMain/Cross` returned 0 for form elements with no text/children; added `GetFormIntrinsicSize` to return correct intrinsic dimensions for all form element types
- **Select dropdown z-order** — dropdown overlay was drawn during normal tree traversal and could be covered by later-painted elements; now deferred and drawn after all content
- **List inside position** — `list-style-position: inside` marker was drawn at the content edge causing text overlap; now tracks marker width and offsets the text

## [0.0.4] - 2026-03-22

### Added

- **SVG rendering** — full `SvgRenderer` supporting `<rect>`, `<circle>`, `<ellipse>`, `<line>`, `<polyline>`, `<polygon>`, `<path>` (via `SKPath.ParseSvgPathData`), `<text>`, and `<g>` grouping; `viewBox` scaling, `transform` attribute (translate, scale, rotate, skewX, skewY, matrix), fill/stroke with opacity, stroke-linecap/linejoin, and HSL color parsing
- **`<canvas>` element** — `CanvasRenderingContext2D` exposed to JavaScript with rect operations (`fillRect`, `strokeRect`, `clearRect`), path API (`beginPath`, `moveTo`, `lineTo`, `arc`, `arcTo`, `ellipse`, `quadraticCurveTo`, `bezierCurveTo`, `closePath`, `fill`, `stroke`, `clip`), text (`fillText`, `strokeText`, `measureText`), transforms (`save`, `restore`, `translate`, `rotate`, `scale`, `setTransform`, `resetTransform`), `drawImage`, and full paint state (`fillStyle`, `strokeStyle`, `lineWidth`, `globalAlpha`, `lineCap`, `lineJoin`, `font`)
- **CSS selector engine** — `SelectorEngine` supporting compound selectors: `#id`, `.class`, `tag`, `tag.class`, `tag#id`, attribute selectors (`[attr]`, `[attr=val]`, `[attr^=val]`, `[attr$=val]`, `[attr*=val]`, `[attr~=val]`), combinators (descendant, child `>`, adjacent `+`, general sibling `~`), pseudo-classes (`:first-child`, `:last-child`, `:nth-child()`, `:not()`), and comma-separated selector lists
- **`document.querySelectorAll`** — now uses the full selector engine for complex queries
- **`document.createTextNode`** — creates `#text` layout nodes from JavaScript
- **`document.createDocumentFragment`** — lightweight container for batch DOM mutations
- **`window.getComputedStyle`** — returns a `JsComputedStyle` proxy that reads resolved CSS values from the layout node
- **`XMLHttpRequest`** — synchronous `open`/`send` with `responseText`, `status`, `readyState`, and `onload` callback; supports GET requests to the page origin
- **`TreeWalker`** — `document.createTreeWalker` with `NodeFilter.SHOW_ELEMENT`, `currentNode`, `nextNode()`, `previousNode()`, `parentNode()`, `firstChild()`, `lastChild()`
- **`JsEvent` object** — `type`, `target`, `currentTarget`, `preventDefault()`, `stopPropagation()` passed to event handlers; `event` global available inside inline handlers
- **Element DOM API expansions** — `insertBefore`, `replaceChild`, `cloneNode(deep)`, `nextElementSibling`, `previousElementSibling`, `firstElementChild`, `lastElementChild`, `childNodes`, `closest(selector)`, `matches(selector)`, `getBoundingClientRect()`, `contains(node)`, `ownerDocument`, `nodeType`, `nodeName`, `className` (get/set), `dataset` proxy for `data-*` attributes
- **`element.classList`** — proper `add`, `remove`, `contains`, `toggle` via Jint object property (replaces `classList_add`/`classList_remove` workaround)
- **`element.style` improvements** — `setProperty`/`getPropertyValue`/`removeProperty` methods; camelCase ↔ kebab-case conversion
- **`data-*` attributes** — captured during parse and accessible via `element.dataset` and `getAttribute`
- **Event bubbling** — events now propagate up the DOM tree from target to root, checking handlers at each ancestor; `stopPropagation()` halts the walk
- **`setTimeout` / `setInterval` / `clearInterval`** — timer APIs via `JsWindow` driving the Win32 animation timer
- **`window.innerWidth` / `window.innerHeight`** — viewport dimensions accessible from JavaScript
- **`requestAnimationFrame`** — schedules a callback on the next animation frame tick

### Fixed

- **Font crash on missing typeface** — `SKTypeface.FromFamilyName` returning `null` for uninstalled fonts now falls back to `SKTypeface.Default` instead of passing `null` to `SKFont` constructor
- **Animation color parse exceptions** — `SKColor.Parse` in `AnimationEngine.TryParseColor` threw `ArgumentException` for every numeric value (e.g. opacity `"0.35"`) on every animation frame; replaced with `SKColor.TryParse` to avoid first-chance exceptions
- **SVG zero-dimension guards** — `<rect>` with zero width/height, `<circle>` with zero radius, and `<ellipse>` with zero radii now skip rendering instead of throwing
- **SVG font size floor** — `<text>` font size clamped to minimum 1px
- **Canvas arc with zero radius** — `arc()` and `ellipse()` with zero or negative radius no longer throw `ArgumentException` from `SKPath.ArcTo`
- **Canvas font size floor** — `ParseFont` now clamps parsed size to minimum 1px
- **Border drawing on tiny elements** — `SKRect.Inflate` with negative inset producing an invalid rect now skips `DrawRoundRect` instead of throwing
- **Null text in caret measurement** — `MeasureText` for the text input caret now guards against null text value
- **Font size floor** — `TextMeasure.CreateFont` clamps font size to minimum 1px to prevent zero-size font exceptions
- **Tag name normalization** — Parser now normalizes all tag names to uppercase via `ToUpperInvariant()`, fixing SVG elements that AngleSharp returns in lowercase
- **`#text` node tag casing** — synthetic text nodes now use lowercase `#text` consistently across Parser, BoxEngine, and FlexEngine
- **Window title** — `BrowserWindow` now correctly sets the Win32 window title
- **Selector null pointer** — fixed null reference in CSS selector matching

## [0.0.3] - 2026-03-20

### Added

- **`opacity`** — element opacity (0–1) with composited subtree rendering via temporary SkiaSharp layers
- **`border-radius`** — rounded corners on all box types via `SKRoundRect`; supports `px` and `%` units
- **`box-shadow`** — multi-layer box shadows with offset, blur, spread, and color; `inset` keyword parsed
- **`text-shadow`** — single-layer text shadow with offset, blur, and color
- **`float: left` / `float: right`** — floated elements removed from normal flow with shrink-to-fit sizing; subsequent content narrows around floats
- **`clear: left` / `right` / `both`** — clears past floated elements
- **Scrollbar UI** — visual scrollbar track and thumb rendered when content overflows the viewport; thumb draggable with mouse, track click jumps to position
- **`:hover` pseudo-class** — CSS properties applied on mouse hover with interactive re-render
- **`:focus` pseudo-class** — CSS properties applied when a form input is focused
- **`:active` pseudo-class** — CSS properties applied during mouse-down
- **`@media` queries** — responsive design support with `min-width`, `max-width`, `min-height`, `max-height`, `orientation`; media types `screen`, `all`, `print`; combinators `and`, `not`, comma (OR); re-evaluated on window resize
- **CSS transitions** — `transition` property with `property`, `duration`, `delay`, and `timing-function`; triggers on pseudo-class state changes; interpolates numeric (px, em, %) and color (rgba) values
- **CSS `@keyframes` animations** — `animation` shorthand with `name`, `duration`, `delay`, `timing-function`, `iteration-count` (including `infinite`), `direction` (`alternate`, `reverse`), `fill-mode` (`forwards`, `backwards`, `both`); 60fps timer-driven animation loop
- **Easing functions** — `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, `step-start`, `step-end`, `cubic-bezier(...)` with Newton-Raphson solver
- **`calc()` expressions** — recursive descent evaluator for `calc()` in all length-resolving properties; supports `+`, `-`, `*`, `/` operators and `px`, `%`, `em`, `rem`, `vw`, `vh` units; nested `calc()` flattened
- **CSS custom properties (`--*` / `var()`)** — custom properties declared on any element (including `:root`), inherited via ancestor chain walk; `var(--name)` and `var(--name, fallback)` with recursive resolution; nested `var()` in both resolved values and fallbacks; automatic shorthand expansion for `padding`, `margin`, `gap`

### Fixed

- **Button text wrapping** — added `white-space: nowrap` to prevent "Hover me" button text from wrapping in flex containers
- **`var()` values in non-override properties** — properties like `background-color`, `color`, `padding` containing `var()` references were silently dropped because AngleSharp cannot resolve custom properties; now any property with a `var()` value is stored in StyleOverrides regardless of the property whitelist

## [0.0.2] - 2026-03-18

### Added

- **Inline text elements** — `<strong>`, `<b>`, `<em>`, `<i>`, `<u>`, `<ins>`, `<s>`, `<del>`, `<strike>`, `<small>`, `<sub>`, `<sup>`, `<mark>`, `<code>`, `<kbd>`, `<samp>`, `<var>`, `<tt>` now render correctly via UA stylesheet rules
- **`font-style: italic`** — rendered using the italic typeface slant via SkiaSharp
- **`text-decoration: line-through`** — strikethrough line drawn at the correct baseline offset
- **`text-align`** — `left`, `center`, `right`, and `justify` support for block and inline runs
- **`line-height`** — configurable via px, em, percentage, or unitless multiplier; falls back to `1.4`
- **`white-space`** — `normal`, `nowrap`, `pre`, `pre-wrap`, and `pre-line` modes all implemented
- **`margin: auto` horizontal centering** — fixed-width blocks with `margin-left: auto` / `margin-right: auto` are centered in their container
- **Vertical margin collapsing** — adjacent block siblings now use `max(marginBottom, marginTop)` instead of summing both margins (CSS 2.1 §8.3.1)
- **`<br>` line breaks** — forced line break inside inline runs
- **`<hr>` horizontal rule** — renders as a styled horizontal line respecting `border-top-width` and `border-top-color`
- **`<pre>` and `<blockquote>`** — block layout with correct UA stylesheet margins and monospace font
- **`<dl>`, `<dt>`, `<dd>`** — definition list elements with correct block display and indentation
- **List rendering** — `<ul>` and `<ol>` render bullet (•) and ordered (1.) markers; nested lists supported
- **Mixed inline content** — text nodes interleaved with element children (e.g. `text <strong>bold</strong> more`) are now preserved in DOM order using synthetic `#TEXT` layout nodes
- **Inter-element spacing** — whitespace between inline siblings (e.g. `</label> <input>`) correctly produces a single space; whitespace-only nodes between block siblings are suppressed
- **`label` is inline by default** — matches browser UA stylesheet; `button` gets a default `1px` border
- **Button CSS** — background color, text color, and border colors are now read from computed styles instead of hardcoded gray values
- **Background and borders on `<p>`** — `PaintTextBlock` now paints background and borders before text, consistent with block elements
- **Monospace font mapping** — `monospace`, `ui-monospace`, `Courier`, and `Courier New` map to `Consolas`; `system-ui` variants map to `Segoe UI`
- **`box-sizing: border-box`** — explicit `height` (and `width`) now correctly subtract padding and border to get content size, matching the `* { box-sizing: border-box }` CSS reset
- **Viewport canvas background** — body background color is propagated to the canvas clear color, eliminating the bare margin strip visible at the page edges
- **Absolute element shrink-wrap** — absolutely positioned inline elements (e.g. badges) now measure their text content for width instead of defaulting to half the container width
- **`display: flex` / `display: inline-flex`** — full CSS Flexbox Level 1 layout engine (`FlexEngine`) implementing:
  - `flex-direction`: `row`, `row-reverse`, `column`, `column-reverse`
  - `flex-wrap`: `nowrap`, `wrap`, `wrap-reverse`
  - `flex-grow` and `flex-shrink` with iterative frozen-item resolution (CSS §9.7)
  - `flex-basis` in px, %, or `auto`/`content`
  - `justify-content`: `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly`
  - `align-items` / `align-self`: `stretch`, `flex-start`, `flex-end`, `center`, `baseline`
  - `align-content` for multi-line flex containers
  - `order` property for paint and layout ordering
  - `gap`, `row-gap`, `column-gap`
  - `min-width` / `max-width` / `min-height` / `max-height` clamping on flex items
  - Auto-margin absorption on both axes
  - Baseline alignment in row containers
  - Cross-axis stretch re-layout at final size
  - Static position tracking (`FlexStaticX/Y`) so absolutely positioned children inside a flex container use the correct static position fallback
- **`display: table`** — table layout engine (`TableEngine`) supporting `<table>`, `<thead>`, `<tbody>`, `<tfoot>`, `<tr>`, `<td>`, `<th>`:
  - Two-pass layout: measure natural cell heights in pass 1, stretch all cells to the uniform row height in pass 2
  - Column widths: explicit `width` on any cell takes priority; remaining columns share space evenly
  - Explicit row `height` is honoured as a minimum row height
  - Row groups (`thead`/`tbody`/`tfoot`) are transparent wrappers resolved by tag name
  - UA stylesheet defaults: `1px` padding on `td`/`th`, bold font on `th`
- **`z-index`** — stacking context for `position: absolute`, `position: fixed`, and `position: relative` elements with an explicit `z-index`; negative z-index elements paint first, non-negative positioned elements paint last
- **`overflow: hidden`** — clips child painting to the element's padding box
- **`overflow: scroll` / `overflow: auto`** — same clip behaviour as `hidden` (scrollable axis not yet interactive)
- **`position: relative`** — element shifted by `top`/`left`/`right`/`bottom` without affecting normal flow
- **`position: absolute`** — removed from normal flow, resolved against the nearest positioned ancestor; `left + right` computes width, `top + bottom` computes height
- **`position: fixed`** — resolved against the viewport and painted after scroll restore so it stays on screen
- **`visibility: hidden` / `collapse`** — property parsed and stored via `StyleOverrides`
- **Percentage `width` / `height`** — percentage sizes now correctly resolve against the parent's content dimension; `vh`/`vw` units resolve against the viewport
- **Percentage `height` on children** — `parentContentHeight` is threaded through `LayoutBlock` and `LayoutChildren` so children can resolve `height: 50%` against the actual parent content height
- **`min-width` / `max-width` / `min-height` / `max-height`** — resolved correctly for both px and percentage values; auto min-width detection via `IsAutoMinWidth`
- **Flex CSS extraction workaround** — `ExtractMatchedCssProperties` iterates all matching stylesheet rules and copies flex/gap/visibility properties into `StyleOverrides`, working around AngleSharp not cascading these via `ComputeCurrentStyle()`; `flex` and `flex-flow` shorthands are decomposed automatically
- **`inline-flex` in inline runs** — `display: inline-flex` elements participate in inline formatting contexts as inline-block equivalents, with intrinsic sizing from max-content measurement

### Fixed

- `<hr>` was incorrectly matched by the heading paint path (`H` + digit check) and never rendered
- Inline elements (`<strong>`, `<em>`, `<mark>`, `#TEXT`, etc.) had no paint path and were silently skipped
- `#TEXT` nodes inherited `display: block` from parent computed style; now forced to `display: inline`
- `GetLineHeight` switch had an invalid `or` pattern for `Em`/`Percent` units — split into separate cases
- `overflow: hidden` clip was not restored when the painted node used an early-return code path, causing all subsequent siblings to be clipped to the overflow box
- `position: relative` elements without an explicit `z-index` were incorrectly sorted into the z-index paint pass, causing them to render after `overflow: hidden` siblings whose clip had not been restored
- Whitespace-only text nodes between block siblings produced phantom inline runs adding unwanted line height
- Block-level elements (`display: flex`, `display: table`) inside an inline run no longer get collected into the inline formatting context — the run-collection loop now breaks on `Flex` and `Table` display types

## [0.0.1] - 2026-03-17

### Added

- `BrowserWindow` API — create a native Win32 window that renders a web page from a URL
- HTML parser using AngleSharp with CSS style computation
- Custom two-pass CSS box model layout engine (`BoxEngine`) supporting block and inline line boxes
- SkiaSharp-based renderer (`Drawer`) producing a pixel buffer from the layout tree
- JavaScript runtime (`JsEngine`) powered by Jint with a minimal browser-compatible DOM API
  - `document.getElementById`, `querySelector`, `querySelectorAll`, `createElement`
  - Element proxy with `textContent`, `innerHTML`, `value`, `checked`, `style`, `classList`, attributes, children, and event listeners
  - `console.log/error/warn` and `window.alert`
  - Inline event attribute support (`onclick`, `oninput`, `onchange`)
- Support for common HTML elements: `div`, `section`, `header`, `footer`, `main`, `article`, `nav`, `aside`, `ul`, `ol`, `li`, `form`, `span`, `h1`–`h6`, `p`, `a`, `img`, `input` (text & checkbox), `button`, `label`, `script`
- CSS property support: `display`, `width`, `height`, `margin`, `padding`, `border-width/color/radius`, `background-color`, `color`, `font-size/weight/style`, `text-decoration`, `text-align`, `cursor`
- `FormState` for tracking text input values, checkbox state, and focused element
- `EventDispatcher` for routing click/change/input events to JS handlers
- `ResourceLoader` for HTTP image fetching with bitmap cache
- Scroll support
- Static file server (`StaticFileServer`) in the Example project using ASP.NET Core Kestrel
- NuGet package published
- Example project with a demo page featuring typography, buttons, form inputs, a counter, and a todo list
