# Standards compliance TODO

Target: Lite's Windows x64 compatibility profile for [HTML5 (28 October 2014)](https://www.w3.org/TR/2014/REC-html5-20141028/), [CSS 2.1 (7 June 2011)](https://www.w3.org/TR/2011/REC-CSS2-20110607/), and [ECMA-262 11th edition (ES2020)](https://262.ecma-international.org/11.0/). The [active profile](Lite.Conformance/Profile/lite-html5-css21-es2020-profile.json) includes CSS screen and print. Keep its published exclusions and optional-capability decisions explicit.

**How to use this list:** A checked box requires implementation **and** reviewed, current conformance evidence. Items marked **confirmed gap** have a specific missing or simplified behavior documented in the source review. Items marked **review** cover partially implemented or unverified behavior; they are not claims that the whole feature is absent. The section indexes linked below are the exhaustive heading-level queues. Individual normative obligations and missing behaviors cannot be exhaustively identified until their clauses and tests have been reviewed.

Scope follows the pinned editions: later HTML additions such as `details`/`summary`/`dialog`, responsive images, custom elements, shadow DOM, and module scripts are regression coverage unless a pinned clause requires them. Later CSS modules and ECMA-402/Intl are separate targets. Browser chrome, OS accessibility mappings, and HTTP/TLS transport internals retain the profile's published exclusions; script-visible and HTML-visible behavior still requires review.

## HTML5

### Infrastructure, DOM, and document state

- [ ] **Review:** Classify all [762 HTML5 sections](Lite.Conformance/Profile/html5-sections.json) into user-agent obligations, authoring/informative text, optional capabilities, dependencies, and justified exclusions; split applicable clauses into atomic requirements. Use the [numbered section backlog](docs/html5-section-backlog.md) to ensure every heading is accounted for.
- [ ] **Review:** Complete dependency coverage for DOM, Web IDL, URL, Encoding, MIME, Fetch, security, scripting-disabled behavior, and applicable optional capabilities (2.1–2.3).
- [ ] **Review:** Complete microsyntax parsing and reflection for booleans, enumerated values, numbers, dates/times, durations, colors, token lists, invalid values, and defaults (2.4).
- [ ] **Review:** Complete dynamic base URL behavior and resource fetching: type/encoding decisions, CORS, credentials, redirects, cancellation, failures, and load ordering (2.5–2.6).
- [ ] **Confirmed gap:** Replace snapshot DOM collections with live `HTMLAllCollection`, `HTMLFormControlsCollection`, and `HTMLOptionsCollection`; implement named/indexed access and `DOMStringMap` behavior (2.7).
- [ ] **Review:** Audit IDL conversion, attribute reflection, callbacks, exceptions, garbage collection, and namespace/qualified-name behavior through creation, mutation, parsing, and serialization (2.7–2.8).
- [ ] **Confirmed gap:** Implement structured cloning and transfer semantics for messages/history: cycles, supported types, identity separation, detachment, and specified errors (2.7.4–2.7.5).
- [ ] **Confirmed gap:** Make the DOM authoritative independently of layout; preserve head, text, comments, doctype, disconnected nodes, adoption, node identity, document modes, metadata, and lifetime (3.1).
- [ ] **Confirmed gap:** Replace the global cookie dictionary with origin/domain/path isolation, expiry, security attributes, and network integration (3.1.2 and referenced cookie rules).
- [ ] **Review:** Complete `HTMLElement` interfaces, constructors/prototypes, global attributes, `classList`, inline style, `data-*`, language/direction, and applicable accessibility semantics (3.2).

### Elements, embedded content, and forms

- [ ] **Review:** Complete `html`, `head`, `title`, `base`, `link`, `meta`, and `style` interfaces and dynamic effects, including stylesheet blocking, alternate sets, and resource events (4.1–4.2).
- [ ] **Review:** Cover every indexed sectioning, grouping, and text element's DOM semantics and rendering, including lists/numbering, ruby, bidi, time/data, quotations, edits, and applicable outlining (4.3–4.6).
- [ ] **Review:** Complete image request states, intrinsic sizing, partial/broken images, alt fallback, CORS, events, and mutation behavior (4.7.1).
- [ ] **Review:** Complete iframe browsing-context identity, navigation, `src`/`srcdoc` mutation, lifecycle/disposal, sandbox, origins, and targeting (4.7.2).
- [ ] **Review:** Complete `embed`, `object`, and `param` resource/type selection, nested documents, parameters, and fallback behavior for unsupported plugins (4.7.3–4.7.5).
- [ ] **Review:** Complete 2014 `audio`, `video`, `source`, and `track` network/ready states, seeking, buffering, rates, volume, autoplay/preload, errors/events, track lists/cues, and `MediaController`/`mediagroup` (4.7.6–4.7.10).
- [ ] **Review:** Complete `map`/`area` collections, shape and coordinate hit testing, activation, and mutation (4.7.11–4.7.13).
- [ ] **Review:** Complete MathML/SVG foreign-content integration, dimensions, resource behavior, and focus where referenced by HTML5 (4.7.14–4.7.16).
- [ ] **Review:** Complete links, targets, downloads, URL reflection, link relations, navigation, and dynamic/security behavior (4.8).
- [ ] **Review:** Complete table element interfaces, live collections, insertion/deletion APIs, spans, and header-association algorithms (4.9).
- [ ] **Review:** Complete every form element: `form`, `label`, `input`, `button`, `select`, `datalist`, `optgroup`, `option`, `textarea`, `output`, `progress`, `meter`, `fieldset`, and `legend` (4.10.3–4.10.17).
- [ ] **Confirmed gap:** Review and implement the 2014 `keygen` element or record a spec-supported optional-capability decision and required fallback (4.10.12).
- [ ] **Review:** Cover every 2014 input state—hidden, text, search, tel, URL, email, password, date, month, week, time, local date/time, number, range, color, checkbox, radio, file, submit, image, reset, and button—including sanitization, typed values, and stepping (4.10.5.1).
- [ ] **Review:** Complete form ownership/reassociation, dirty value/checkedness, disabled fieldsets, option/label rules, `dirname`, length limits, autofocus/autocomplete, and selection APIs (4.10.18–4.10.20).
- [ ] **Review:** Complete every `ValidityState` flag, barred controls, custom validity/messages, `invalid` event behavior, and submission blocking (4.10.21).
- [ ] **Review:** Complete successful-control construction, submitter/image coordinates, implicit submission, cancellation/targets, `accept-charset`, URL-encoded/multipart/plain-text submissions, and reset (4.10.22–4.10.23).
- [ ] **Confirmed gap:** Align parser and dynamic script execution, `async`/`defer`, stylesheet blocking, failed/cancelled loads, and document readiness with a shared streaming parser insertion point (4.11.1–4.11.2).
- [ ] **Review:** Preserve inert template `DocumentFragment` contents, owner-document identity, parsing, cloning/adoption, and activation (4.11.3).
- [ ] **Review:** Complete canvas element resize/reset, context/fallback, serialization, and origin-clean behavior (4.11.4).
- [ ] **Confirmed gap:** Complete referenced Canvas 2D behavior: drawing-state save/restore, compositing, gradients/patterns, text alignment/max width, all `drawImage` overloads, pixel APIs, and errors.
- [ ] **Review:** Complete HTML enabled/disabled, checked/indeterminate/default, validity/range, and read/write selector states and invalidation; review common-idiom applicability (4.12–4.14).

### Browsing contexts, interaction, and parsing

- [ ] **Review:** Complete `WindowProxy`, named/indexed window properties, parent/top/opener, open/close, targets, and browsing-context lifetime (5.1–5.2).
- [ ] **Confirmed gap:** Enforce cross-origin access, opaque/inherited origins, domain relaxation, iframe sandbox flags, and `postMessage` `targetOrigin`/transfer rules (5.3–5.4 and referenced messaging rules).
- [ ] **Confirmed gap:** Implement cross-document history traversal/restoration, state cloning, same-origin URL checks, fragments/reload, unload/beforeunload, lifecycle, and scroll restoration (5.5–5.6).
- [ ] **Confirmed gap:** Address HTML5 offline application cache: manifest parsing, cache selection/update/fallback, events, and `ApplicationCache`, including any spec-permitted optional behavior decision (5.7).
- [ ] **Review:** Complete realms, event-loop task sources, microtask checkpoints, event handlers, exception reporting, and readiness ordering (6.1).
- [ ] **Review:** Complete Base64 conversion/error behavior and timers: nesting/clamping, cancellation, string handlers, and lifetime (6.2, 6.4).
- [ ] **Confirmed gap:** Implement `document.open` replacement/listener reset and streaming `write`/`writeln`/`close`, including parser re-entry and ignored-write cases (6.3).
- [ ] **Review:** Complete script-visible `alert`, `confirm`, `prompt`, and `print` behavior with modal-task and sandbox/host rules (6.5).
- [ ] **Confirmed gap:** Complete applicable `Navigator` fields, plugin/MIME arrays, handler registration, `External`, online state, and other 2014 system capabilities (6.6).
- [ ] **Review:** Complete hidden/inert interaction, trusted activation, tab order, `tabindex`, autofocus/accesskey, nested focus, and focus-event ordering (7.1–7.5).
- [ ] **Confirmed gap:** Implement `contenteditable`, `designMode`, editing commands, `Range`, `Selection`, and applicable spellcheck behavior (7.6).
- [ ] **Review:** Validate full HTML tokenizer/tree construction, encoding sniff/restart, malformed recovery, foreign content, foster parenting, formatting, fragments, entities, and exact serialization; audit AngleSharp-to-Lite projection (8).
- [ ] **Review:** Dispatch XHTML by MIME type with XML well-formedness, namespaces/case, fragments/serialization, and XML script behavior (9).
- [ ] **Review:** Complete HTML rendering rules: UA styles/presentational hints, replaced content, bidi/ruby, controls, focus/selection, framesets, media, unstyled XML, and print behavior (10).
- [ ] **Review:** Complete required obsolete processing and fallback for `applet`, `marquee`, `frameset`, `frame`, and legacy interfaces (11).
- [ ] **Review:** Complete relevant HTML/XHTML MIME behavior, multipart replacement, form encodings, cache-manifest MIME, and custom protocols (12).

### HTML5 evidence

- [ ] Review all pinned WPT assertions against the **2014** target; split mixed-era tests and add historical fixtures where modern WPT lacks coverage, especially application cache and `keygen`.
- [ ] Complete the HTML5 test/variant inventory and required harness support for upstream serving, multiple origins/HTTPS, `testdriver`, relevant worker dependencies, manual cases, and reftest metadata.
- [ ] Collect fresh identity-matched results for every applicable requirement, including native keyboard/editing/file/media evidence; keep `html5ProfileReady` false until the [HTML5 conformance contract](docs/html5-conformance.md) passes.

## CSS 2.1

### Inventory, stylesheet handling, and cascade

- [ ] **Review:** Classify all [841 CSS sections](Lite.Conformance/Profile/css21-sections.json), [115 indexed properties](Lite.Conformance/Profile/css21-properties.json), and applicable appendices into atomic screen/print obligations, permitted choices, informative material, and exclusions. The aural appendix is informative and does not itself require speech rendering.
- [ ] **Review:** Finish the [requirement inventory](Lite.Conformance/Profile/css21-requirements.json) and [test applicability inventory](Lite.Conformance/Css21/css21-applicability.json), including every property/shorthand's value grammar, initial/inherited/computed value, percentage basis, restrictions, and interactions.
- [ ] **Confirmed gap:** Model document-owned UA, user, and author stylesheets with correct origin/importance; retrieve linked/imported sheets, handle failure and encoding, select alternate sets, disable author styles, and load a user stylesheet file.
- [ ] **Review:** Unify initial parsing and dynamic restyling for mutations, inline edits, stylesheet changes, pseudo-classes, and medium changes; distinguish specified, computed, used, and actual values.
- [ ] **Review:** Complete CSS tokenization/error recovery, escapes, comments, strings/URLs, numeric ranges/units, invalid declarations/selectors, shorthand reset, `inherit`, `@charset`, `@import`, source order, specificity, and presentational hints (chapters 4–6).
- [ ] **Review:** Complete CSS 2.1 selectors and pseudo-elements across HTML and real XHTML/XML trees, including language, case sensitivity, structural and interactive states, `:first-line`, `:first-letter`, and permitted visited-link behavior (chapter 5).
- [ ] **Confirmed gap:** Add `print` media evaluation and correct medium-dependent stylesheet selection and restyling (chapter 7).

### Box model and screen layout

- [ ] **Review:** Complete box generation: anonymous boxes, inline splitting, `display` transformations/run-in, containing blocks, and formatting-context boundaries (chapters 8–10).
- [ ] **Review:** Complete widths/heights and auto margins, min/max limits, percentage bases, replaced intrinsic sizes/ratios, shrink-to-fit, margin collapsing, clearance, and empty boxes (chapters 8–10).
- [ ] **Review:** Complete floats, line exclusions, relative/absolute/fixed positioning, static positions, over-constrained equations, RTL cases, and scroll/resize invalidation (chapter 9).
- [ ] **Review:** Complete line boxes, struts, baselines, vertical alignment, whitespace/wrapping, text indent/alignment/justification, spacing, transforms, decorations, and first-line/first-letter layout (chapters 9, 16).
- [ ] **Review:** Complete font matching/fallback, family/size/weight/style/variant/system fonts, `ex` measurements, Unicode bidi, shaping, and consistent measurement/painting (chapters 15–16).
- [ ] **Confirmed gap:** Apply both horizontal and vertical `border-spacing` values; review full fixed/auto table layout, anonymous table repair, captions, row/column spans, borders, backgrounds, baselines, and `visibility: collapse` (chapter 17).
- [ ] **Review:** Complete generated content, counters, quotes, `attr()`, list markers/styles, and pseudo-element inheritance (chapter 12).
- [ ] **Review:** Complete colors/system colors, borders, backgrounds and canvas propagation, overflow/clip/visibility, Appendix E stacking/paint order, outlines, focus, cursors, and relevant user preferences (chapters 11, 14, 18; Appendix E).
- [ ] **Review:** Diagnose and fix Acid2 differences against an independent reference; map each failure to its CSS/HTML/image obligation rather than treating the local baseline as conformance evidence.

### Print and paged media

- [ ] **Confirmed gap:** Create a print rendering context and paginated layout/paint model that shares the document's formatting rules without slicing a screen image.
- [ ] **Review:** Complete `@media print`, `@page` margins and first/left/right selectors, page boxes, break-before/after/inside, forced/avoided breaks, widows/orphans, and oversized content (chapter 13).
- [ ] **Review:** Handle page interactions with floats, tables, counters, backgrounds, clipping, fixed positioning, page parity, and permitted table-header choices.
- [ ] **Confirmed gap:** Expose deterministic per-page render/PDF output and Windows preview/print using the same pagination results; preserve live screen layout when paper settings change.

### CSS 2.1 evidence

- [ ] Import and classify the locked official 2011 CSS 2.1 suite and complementary WPT CSS2 cases, including HTML/XHTML, screen/print, resources, references, fonts, and manual variants.
- [ ] Build full screen/print execution and independent readiness gates. Honor MIME/encoding, viewport/DPI, reference graph, resource readiness, reviewed per-test fuzziness, manual interaction, and source/binary identity. The existing 52 curated cases remain a regression set.
- [ ] Run the complete applicable screen and print matrices and retain page-level diagnostics. Keep `css21ProfileReady` false until the [CSS 2.1 plan](docs/css21-conformance-plan.md) has reviewed obligations and passing evidence for both media.

## ES2020

### Language and built-ins

- [ ] **Review:** Classify all [2,115 ECMA-262 sections](Lite.Conformance/Test262/es2020-sections.json) into mandatory language/Annex B obligations, informative material, and host hooks; map each applicable obligation to exact tests.
- [ ] **Review:** Finish edition classification for staging, post-2020, and mixed-era Test262 cases and modern helper files; supply ES2020-specific coverage before excluding an overlapping test.
- [ ] **Review:** Complete grammar, Unicode, declarations/scope, functions/classes, destructuring, iteration, generators, async functions/iteration, objects, proxies, reflect, symbols, and abstract operations.
- [ ] **Review:** Complete numbers/BigInt, strings/RegExp, dates/JSON, collections, buffers/typed arrays/DataView, Atomics/shared memory, promises, and all standard built-ins.
- [ ] **Review:** Complete script/module parsing, linking, evaluation, namespaces/live bindings, cycles, dynamic import, `import.meta`, and abrupt completion semantics.
- [ ] **Review:** Complete Annex B web compatibility behavior and all proper-tail-call positions/execution-context effects. Proper tail calls already have a passing focused test; this is a coverage review.
- [ ] **Confirmed gap:** Correct the stock-Jint Annex B block-function case where a declaration named `arguments` shadows the function's arguments object (`block-decl-func-skip-arguments.js`). Retain the exact reproducer until a supported stock-Jint release passes it.
- [ ] **Review:** Complete shared-memory agent scheduling, memory-ordering tests, blocking/nonblocking `Atomics.wait` behavior, and browser agent configuration.

### Browser host integration

- [ ] **Confirmed gap:** Implement live `document.all` with Annex B `[[IsHTMLDDA]]` falsy, loose-equality, `typeof`, and callable behavior; find a supported stock-Jint integration path.
- [ ] **Review:** Carry credentials, referrer, CORS, redirect-taint, MIME, cancellation, and other fetch options through root and descendant module loads.
- [ ] **Review:** Complete module URL canonicalization, importer-relative resolution, inline-module identity, failed-load caching, and source ownership after redirects or callback/eval imports.
- [ ] **Review:** Complete document/iframe realm isolation for globals, intrinsics, module maps/jobs, navigation cancellation, and cross-realm errors.
- [ ] **Review:** Complete microtask checkpoints, promise jobs, observer/callback/task ordering, deferred modules, failure paths, and readiness transitions.
- [ ] **Review:** Replace simplified error/rejection notification objects with complete browser event semantics, source locations, cancellation/default reporting, and callback-exception handling.

### ES2020 evidence

- [ ] Review the Test262 execution contract for fresh realms, strict/sloppy modes, raw and module cases, importer-relative fixtures, negative phase/type checks, async completion, `$262` host helpers, and timeout/crash isolation.
- [ ] Run all mandatory classified Test262 modes, host obligations, and supplemental tests on one identified build; account for every shard, skip, exclusion, known failure, and missing result.
- [ ] Resolve the published Annex B and `document.all` failures, finish the normative/edition/host reviews, and require current passing evidence before setting `es2020ProfileReady`. Use the [ES2020 backlog](docs/es2020-conformance.md) and generated `Lite.Conformance/artifacts/es2020/es2020-backlog.md` for exact case IDs.

## Combined release evidence

- [ ] Keep HTML5, CSS screen, CSS print, ES2020, and combined release readiness independent and fail closed on unreviewed obligations, unsupported variants, stale evidence, missing artifacts, and known failures.
- [ ] Rebuild and rerun applicable suites after implementation changes; record the exact profile, source, dependencies, suite revisions, environment, and artifacts before making a compliance claim.
