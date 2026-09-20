# HTML5 conformance and remaining implementation work

Lite targets the [W3C HTML5 Recommendation, 28 October 2014](https://www.w3.org/TR/2014/REC-html5-20141028/), also called HTML 5.0. This replaces HTML 5.3. The [active contract](../Lite.Conformance/Profile/lite-html5-css21-es2020-profile.json), CLI, schemas, applicability manifest, evidence identity and CI now use `html5`. CSS 2.1 and ES2020 remain separate targets.

**Lite is not HTML5 conformant.** `html5ProfileReady` must remain false. Existing functionality and passing regressions do not establish complete feature conformance. Broad contract entries are workstreams awaiting atomic requirement and test review.

## Complete inventory

The [complete section backlog](html5-section-backlog.md) lists **all 762 numbered sections**, with exact specification links. Its machine-readable source is [html5-sections.json](../Lite.Conformance/Profile/html5-sections.json). It includes every indexed element, input state, parsing state, history algorithm and rendering topic, including authoring and informative material. Every section still needs normative review. No section is silently treated as implemented or excluded.

The table below groups the remaining implementation work by feature. **Confirmed gap** means inspected source exposes missing or simplified behavior. **Review/complete** means some implementation or dependency exists, but complete 2014 behavior has not been established. Neither means every subfeature is missing. The backlog is exhaustive at heading level; an exhaustive list of individual unimplemented normative requirements requires algorithm review and mapped test execution. There is no justified completion percentage yet.

## Remaining features

| Feature and clauses | Work still required | Evidence / implementation area |
| --- | --- | --- |
| Infrastructure (2.1–2.3) | Scripting-disabled behavior, optional capabilities, DOM/Web IDL/URL/Encoding/MIME/Fetch dependencies, ASCII comparisons and security requirements. | Review/complete; dependency inventory remains empty. |
| Microsyntaxes (2.4) | Boolean/enumerated attributes, integer/float parsing, dates/months/weeks/times/durations, colors, tokens and invalid/default values. | Review/complete: Parser and JsElement. |
| URLs and fetching (2.5–2.6) | Dynamic base changes, resource types/encodings, CORS/credentials, cancellation, failure and load ordering. | Review/complete: Parser, Network, JsFetch and JsXmlHttpRequest. |
| Common DOM interfaces (2.7) | Live HTMLAllCollection, HTMLFormControlsCollection, HTMLOptionsCollection; named/indexed access; DOMStringMap; Web IDL conversion/reflection/exceptions. | Confirmed gap: JsDocument collection methods return snapshot arrays. |
| Structured data (2.7.4–2.7.5) | Cloning cycles and supported objects, identity separation, transfer/detachment and errors in APIs that require them. | Confirmed gap: JsWindowProxy converts messages to CLR objects; JsHistory retains JsValue references. |
| Namespaces (2.8) | Preserve namespaces and qualified names through creation, mutation, parsing and serialization. | Review/complete: Parser and JsDocument. |
| Document model (3.1) | Authoritative DOM independent of layout, exact text/comments/doctype/head nodes, disconnected nodes, accessors, metadata, modes, adoption and lifetime. | Confirmed gap: JsDocument traverses LayoutNode while title reads a separate AngleSharp document. |
| Cookies (3.1.2/dependency) | Origin/domain/path isolation, expiry, security attributes and network integration. | Confirmed gap: JsDocument uses one static dictionary and ignores cookie attributes. |
| HTMLElement/global attributes (3.2) | Prototypes/constructors, id/title/lang/translate/dir/class/style/data attributes, bidi and in-spec ARIA behavior. | Review/complete: JsElement/JsEngine. OS accessibility mappings remain excluded. |
| Root/metadata (4.1–4.2) | html/head/title/base/link/meta/style interfaces and dynamic effects, pragmas, stylesheet blocking, alternate stylesheets and resource events. | Review/complete: Parser, document styles and JsDocument. |
| Sections/grouping/text/edits (4.3–4.6) | Every indexed element's interface/semantics; lists/numbering, ruby/bidi, time/data, quotations, insertion/deletion and applicable outline processing. | Review/complete; rendering alone is insufficient. |
| Images (4.7.1) | Request state changes, partial/broken images, intrinsic sizes, alt fallback, CORS, events and mutations. | Review/complete: Parser/JsElement. Responsive image extensions are outside this target. |
| Iframes (4.7.2) | Stable contexts across navigation, src/srcdoc changes, lifecycle/disposal, sandbox, origin access and targeting. | Only a narrow initial-document assertion is mapped and implemented. |
| embed/object/param (4.7.3–4.7.5) | Resource selection, nested documents, fallback, parameters and type handling. | Review/complete; optional plugin support does not remove fallback obligations. |
| Media (4.7.6–4.7.10) | audio/video/source/track loading and ready/network states, autoplay/preload, errors, seeking/buffering/ranges, rates/volume, ended/loop, events, track lists/cues and 2014 MediaController/mediagroup. | Review/complete: Lite.Media, JsElement and MediaTests. Use 2014 play() behavior, not later promise requirements. |
| Image maps (4.7.11–4.7.13) | map/area collections, shapes/coordinate hit testing, activation and mutation. | Review/complete: Parser and Interaction. |
| MathML/SVG embedding (4.7.14–4.7.16) | Foreign-content integration, dimensions, resources/focus and referenced requirements. | Review/complete; independent SVG support is insufficient. |
| Links (4.8) | a/area URL reflection, following links, targets, downloads, link relations and dynamic/security behavior. | Review/complete: JsElement/JsLocation and navigation. |
| Tables (4.9) | caption/colgroup/col/sections/rows/cells interfaces, live collections, insert/delete APIs, spans and table/header algorithms. | Review/complete: layout and element wrappers. |
| Form elements (4.10.3–4.10.17) | form/label/input/button/select/datalist/optgroup/option/textarea/keygen/output/progress/meter/fieldset/legend. | Review/complete; confirmed gap: no keygen implementation found. Review optional cryptographic choices explicitly. |
| Input states (4.10.5.1) | Hidden, text, search, tel, URL, email, password, date, month, week, time, local date/time, number, range, color, checkbox, radio, file, submit, image, reset and button; sanitization, typed values and stepping. | Review/complete: FormTests/JsElement cover subsets. Every state is listed in the backlog. |
| Form infrastructure (4.10.18–4.10.20) | Ownership/reassociation, dirty value/checkedness, disabled fieldsets, options, labels, mutability, dirname, length limits, autofocus/autocomplete and selection APIs. | Review/complete: JsElement/Interaction. |
| Validation (4.10.21) | Every ValidityState flag, barred controls, custom validity/messages, invalid event cancellation and submission blocking. | Review/complete; partial tests do not prove all rules. |
| Submission/reset (4.10.22–4.10.23) | Successful controls, submitter/image coordinates, implicit submission, cancellation, targets, accept-charset and URL-encoded/multipart/plain-text encodings; reset. | Review/complete: JsFormData and forms. |
| Scripts/noscript (4.11.1–4.11.2) | Parser/dynamic script distinctions, async/defer, stylesheet blocking, failed/cancelled loads and lifecycle ordering. | Confirmed integration gap: parsing/document.write lack a shared streaming insertion point; scheduling needs full review. |
| Templates (4.11.3) | Inert DocumentFragment contents, owner document, parsing, cloning/adoption and activation. | Review/complete: layout-backed DOM must preserve inert nodes and identity. |
| Canvas element (4.11.4) | Bitmap reset on resizing, fallback, context rules, serialization and origin-clean checks. | Review/complete: JsCanvas/JsCanvasContext2D. |
| Canvas 2D dependency | Full drawing-state save/restore, compositing, gradients/patterns, text alignment, image overloads, pixels and errors. | Confirmed gaps: save/restore only saves native canvas/matrix state; drawImage has one overload; text ignores maxWidth. Review referenced Canvas 2D separately. |
| Disabled elements/selectors (4.13–4.14) | HTML enabled/disabled/checked/indeterminate/default/valid/invalid/range/read-write matching and invalidation. | Review/complete: SelectorEngine/styles. Review applicability of 4.12 common idioms too. |
| Contexts/Window (5.1–5.2) | WindowProxy identity, named/indexed properties, parent/top/opener, open/close, targets and lifetime. | Review/complete: JsWindow/JsWindowProxy/BrowserWindow. |
| Origins/sandbox (5.3–5.4) | Cross-origin access, opaque/inherited origins, domain relaxation and sandbox restrictions. | Confirmed gaps: contentDocument exposes the child directly; postMessage ignores targetOrigin and transfer. |
| History/navigation (5.5–5.6) | Cross-document restoration, state cloning, same-origin URL checks, responses/fragments/reload, unload/beforeunload, lifecycle and scroll restoration. | Confirmed gap: JsHistory explicitly treats cross-document traversal as same-document. |
| Offline application cache (5.7) | Manifest parsing, cache selection/update/download/fallback, events and ApplicationCache API; explicitly review optional behavior. | Confirmed gap: no applicationCache implementation found. Modern WPT may lack historical tests. |
| Script execution/event loop (6.1) | Realms/settings, task sources/checkpoints, handlers, exceptions and readiness ordering. | Review/complete: JsEngine/EventDispatcher and partial tests. |
| Base64/timers (6.2, 6.4) | Invalid-character/conversion behavior, timer nesting/clamping/cancellation/string handlers and lifetime. | Review/complete: JsWindow/JsEngine. |
| Dynamic markup (6.3) | document.open replacement/listener reset, streaming write/writeln/parser re-entry, close and ignored-write cases. | Confirmed gap: open/close are no-ops; write appends body fragments and cannot join split markup. |
| Prompts (6.5) | alert/confirm/prompt/print APIs, modal task interaction and sandbox/host restrictions. | Review/complete; chrome exclusion does not remove script-visible behavior. |
| System capabilities (6.6) | Navigator identification/languages/online state, handler registration, plugin/MIME arrays, storage mutex and External; document optional decisions. | Confirmed gap: JsNavigator exposes fixed values and lacks handler/plugin interfaces. |
| Interaction (7.1–7.5) | Hidden/inert effects, trusted activation, tab order, tabindex/autofocus, nested focus, focus events and accesskey. | Review/complete: native/headless input need consistent behavior. |
| Editing (7.6) | contenteditable/designMode, commands, selection/range dependencies and applicable spellcheck behavior. | Confirmed gap: no designMode/execCommand/createRange/getSelection implementation found. Native evidence needed. |
| HTML parsing/serialization (8) | Encoding sniff/restart, tokenizer/tree construction, malformed recovery, foreign content, foster parenting, formatting, fragments, entities and exact serialization. | Review/complete: AngleSharp helps; Lite projection and script insertion require audit. |
| XHTML (9) | MIME-directed XML parsing, well-formedness errors, namespaces/case, fragments/serialization and XML scripts. | Review/complete; XHTML MIME serving with HTML parsing is insufficient. |
| Rendering (10) | UA styles/presentational hints, non-replaced/replaced elements, bidi/ruby, controls, focus/selection, framesets, media behavior and unstyled XML. | Review/complete: Layout/Drawer. Explicitly review print obligations; CSS screen scope does not mark HTML print complete. |
| Obsolete processing (11) | Required applet/marquee/frameset/frame and legacy interface/parser/rendering behavior, including unsupported-capability fallback. | Review/complete; obsolete authoring status does not remove processing requirements. |
| MIME/protocols (12) | HTML/XHTML, multipart replacement, form encodings, cache-manifest MIME and custom protocols. | Review/complete: Network/Parser. |

## Scope changes

Details/summary/dialog, picture/srcset/sizes, custom elements, shadow DOM/slots, module scripts and later additions are not requirements of this pinned Recommendation. Existing extensions stay available. Details fixtures remain ordinary WPT regressions with `regression-only` applicability and cannot count toward HTML5 readiness. The initial iframe fixture was reviewed against 4.7.2 and remains included.

Drag-and-drop, Web Messaging, Web Storage, workers and other separate specifications require explicit dependency mapping where referenced or separately chosen capability tracking. They are not automatically inherited as whole HTML 5.3 workstreams. Conservative WPT candidate roots still surface related tests; candidate membership does not establish applicability.

Browser chrome, OS accessibility platform mappings and HTTP/TLS internals retain their published exclusions. HTML-visible behavior remains subject to review. Optional capabilities require documented decisions and required fallback behavior. This is a compatibility profile with exclusions, not an unrestricted browser-conformance claim.

## Remaining conformance infrastructure

- Review all sections into atomic user-agent obligations, author/informative rules, optional capabilities and exclusions. Map dependencies/tests and keep unmapped obligations untested.
- Review modern pinned WPT assertions against 2014, including mixed-era assertions. Add historical fixtures for removed APIs such as application cache/keygen where upstream tests are missing. The test revision is not the normative HTML version.
- Complete applicable input/testdriver automation, worker contexts for dependencies, HTTPS/multiple-origin CI, manual recording and unsupported reftest metadata (fuzzy/DPI).
- Collect current identity-matched evidence for every applicable requirement. Local fixtures prove only named assertions; vendor WPT requires upstream serving.
- Collect native Windows keyboard/editing/file-selection/media evidence and execute the full applicable corpus before enabling the completion gate.

## Commands

```powershell
python scripts/import-html5-sections.py
./scripts/fetch-tests.ps1
./scripts/build-wpt-manifest.ps1
dotnet build Lite.sln -c Release
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html5-inventory
dotnet run --project Lite.Tests -c Release --no-build -- --report Lite.Conformance/artifacts/unit-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html5 --report Lite.Conformance/artifacts/html5-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite wpt --report Lite.Conformance/artifacts/wpt-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite profile --evidence Lite.Conformance/artifacts/unit-results.json --evidence Lite.Conformance/artifacts/html5-results.json --evidence Lite.Conformance/artifacts/wpt-results.json
```

The inventory command requires the locked WPT manifest. The importer supports `--source <downloaded-index.html>` and generates both section JSON and Markdown; reviews survive only when number/title/URL match.

The report exposes `html5ProfileReady`, `html5Blockers`, exclusions and independent combined `releaseReady`. `--require-html-ready` must currently exit 1; `--require-ready` checks HTML/CSS/ES together. Old HTML 5.3 evidence becomes stale after the profile identity changes and must be regenerated. Old CLI/report field names are replaced.

For vendor WPT, run `./scripts/serve-wpt.ps1` separately and pass `--wpt-base-url http://web-platform.test:8000`. Configure host resolution/HTTPS certificates using the pinned WPT documentation. CI uses Python 3.12. The manifest helper accepts `-Python`, `-VirtualEnvironment` and `-SkipEnvironmentSetup` for an existing environment.

Keep reports in ignored `Lite.Conformance/artifacts/`. Finish edits and rebuild before collecting evidence: source/profile/lock/binaries/dependencies/checkout/manifest are fingerprinted. Stale, incomplete, conflicting, empty, unsupported or unexplained failing results cannot establish readiness. CSS/ES and Acid results remain separate workstreams.
