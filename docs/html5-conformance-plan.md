# Full HTML5 conformance plan

Planning baseline: 3 October 2026. Scope: Windows x64, screen rendering, retaining Lite's C#/AngleSharp/QuickJS/Skia architecture. This document is an implementation plan; it does not change the active compatibility profile or claim that any new coverage has passed. The current state and confirmed-gap inventory live in [html5-conformance.md](html5-conformance.md); this document defines the order of work and the rules that keep each gate honest while it grows.

## Target and completion contract

Keep the [W3C HTML5 Recommendation of 28 October 2014](https://www.w3.org/TR/2014/REC-html5-20141028/) (HTML 5.0) as the normative target. The WHATWG living standard, HTML 5.1 and later W3C editions are not targets; where a pinned WPT test asserts later behavior, the later assertion is classified `post-target`, not silently absorbed. Modern features that exist in Lite as extensions (details/dialog, picture, modules, and other regressions from later targets) stay available but never count as HTML 5.0 requirements.

Browser chrome, OS accessibility platform mappings, and HTTP/TLS internals retain their published profile exclusions. Everything HTML-visible — including sandbox, origins, prompt APIs, and obsolete-element processing — remains required. Dependency specifications (DOM, Web IDL, URL, Encoding, MIME, Fetch, Unicode/Bidi, Web Storage, Web Messaging, workers, File API) are mapped per clause as dependencies; they are tracked where HTML 5.0 references them and are not auto-inherited as whole workstreams.

Completion means every applicable user-agent obligation is inventoried from the pinned Recommendation, implemented, and supported by identity-matched passing evidence. It does not mean a selected manifest is green. `html5ProfileReady` must remain false until the section review and the evidence collection are complete; passing regressions and a green gate never establish readiness by themselves.

## Verified starting point

Measured from source and local artifacts on the baseline date (not a fresh full-corpus run):

- The `--suite html5` gate executes **1 test** (`lite/html5/iframe-initial-document.html`) and passes. `--suite wpt` (curated) passes 86/88 with both non-passes published as expected failures. Unit tests and the ES2020/CSS 2.1 gates are maintained by their own workstreams.
- `Wpt/html5-applicability.json` holds **89 entries**: 1 `included`, 0 `mixed`, 59 `unreviewed`, 9 `post-target`, 20 `regression-only`.
- The upstream WPT catalog (`--suite html5-inventory`, artifacts) contains **~15,372 candidate test cases** across the candidate roots (`html`, `dom`, `uievents`, `url`, `encoding`, `mimesniff`, `fetch`, `cors`, `cookies`, `webstorage`, `webmessaging`, `selection`, `FileAPI`, `custom-elements`, `shadow-dom`); all but ~16 are unreviewed.
- The section inventory lists **762 REC sections, all unreviewed**; the profile holds 197 HTML 5.0 requirements of which 191 are untested, 1 implemented, 2 failing (both recorded regression fixtures for later-era features, not 2014 obligations), 3 profile-excluded.
- Runner machinery already works end to end: worker-process isolation, testharness/reftest/crashtest/selftest kinds, subtest-level evidence, upstream serving via `serve-wpt.ps1` where a test requires it. Unsupported execution kinds today: testdriver-based input automation, dedicated/serviceworker/shared worker contexts, manual-test recording.
- The parser is AngleSharp's spec-oriented HTML5 parser; the repo-owned risk is the **projection layer** (`Parser.Traverse`), skipped metadata tags, collapsed whitespace, snapshot collections, and the non-authoritative DOM — all documented as confirmed gaps.

There are uncommitted CSS/ES workstream changes in the checkout from parallel efforts. This track touches only the files listed under "Coordination" below and must rebuild before collecting evidence.

## Milestone 1: applicability review and gate growth (current work)

Review the catalog against the pinned Recommendation, root by root, and grow the executable gate. Proposed batch order — smallest, most-dependable roots first so the review protocol hardens before the large `html` root: `dom`, `uievents`, `url`, `encoding`, `webstorage`, `webmessaging`, `cookies`, `fetch`/`cors` (dependency-mapped), `FileAPI`, `selection`, `mimesniff`, then `html` by subdirectory (syntax/parsing, semantics, browsers, editing, forms, interaction, rendering, infrastructure, dom/documents…).

Classification protocol (schema-constrained to `path`, `classification`, `reason`, plus assertion inventories for `mixed`):

- `included` — every subtest is a 2014-target obligation and passes. Included tests are the gate; they must pass in the same change that includes them.
- `mixed` — the file mixes 2014-target and later-era assertions. Requires `assertionInventoryComplete` with a per-assertion classification and reason; the gate tolerates failure only in reviewed non-target assertions.
- `post-target` — the assertions only bind behavior introduced after HTML 5.0 (for example `toggleAttribute`, `append`/`prepend`, `replaceChildren`, `moveBefore`, lazy loading, `customElements`). A one-line normative justification naming the later feature is required.
- `regression-only` — curated Lite regression or a later-target regression fixture that cannot count toward HTML 5.0 readiness.
- `profile-excluded` — the obligation itself is outside the compatibility profile (chrome, accessibility mappings, networking internals).
- `unreviewed` — everything else, including a **reviewed applicable test that Lite currently fails**: record `reason: "Reviewed (failing): <spec clause, failing assertion, engine gap>"` so the review is preserved in text while the machine state stays out of the gate. Each such entry is added to the Milestone 4 work queue and becomes `included` only when it passes. Never include a failing test to show work, and never use an exclusion classification to hide an applicable failure.

Gate growth discipline: each batch is classified, then `--suite html5` must exit 0 before the batch is considered done; `inventoryComplete` flips to true only when every catalog candidate has a classification, and omissions are detected from the catalog rather than trusted from the flag.

**Exit:** a reviewed corpus of meaningful size across the dependency roots, zero unexpected failures, the reviewed-failing queue feeding Milestone 4, and the review protocol demonstrated end to end on the `dom` root.

## Milestone 2: parse fidelity and serialization

Measure and fix HTML parsing/serialization conformance (REC §8), the section the project itself flags as "projection requires audit". Run the vendored tree-construction corpus (`html/syntax/parsing` html5lib runners over the 62 `.dat` files), the tokenizer cases, named-character-references, and serialization/innerHTML tests through the real pipeline. Where the projection layer (skipped metadata tags, whitespace collapsing, fragment contexts, script insertion) breaks parse-fidelity assertions, either fix the projection or record the reviewed-failing entry. Add WPT-independent unit fixtures for tokenizer edge cases only where WPT has no window-context equivalent.

**Exit:** a measured parse-fidelity baseline with failure clusters named (foster parenting, adoption agency, foreign content, template, encoding restart), and no silent skip between the vendored corpus and the gate.

## Milestone 3: harness automation

Remove the `Unsupported` execution kinds so no applicable reviewed test is blocked by harness capability: testdriver input automation (click, keyboard, async waits) over the native/headless hosts; `.any.js`/`.worker.js` worker contexts; upstream serving integration for tests requiring substitutions, multiple origins, or HTTPS; reftest fuzzy/DPI metadata; recorded manual evidence for interactive cases. Add historical fixtures where upstream removed 2014 features (application cache, keygen). Add harness regression fixtures for each capability as it lands, mirroring the CSS plan's rule that runner bugs must never be able to produce false passes.

**Exit:** for every applicable reviewed test, the only non-pass outcomes are genuine engine failures.

## Milestone 4: engine work packages

Each package starts from a measured failing cluster (Milestone 1 reviews, Milestone 2 baseline), implements the behavior, adds focused regressions, updates the corresponding `html5.*` profile entries (only `html5.*` — see coordination rules), then reruns the cluster plus shared regressions. A package is never closed from a sample.

| Package | Confirmed gaps addressed | Primary areas |
| --- | --- | --- |
| DOM authority | snapshot collections, non-authoritative DOM, title/metadata split | `JsDocument`, `JsElement`, `LayoutNode`, `DocumentState` |
| Events and messaging | postMessage ignores targetOrigin/transfer, structured clone | `JsWindowProxy`, `JsEvent`, event loop |
| Origins and sandbox | contentDocument exposes child directly, no sandbox flags | iframes, `BrowserWindow`, navigation |
| History and navigation | cross-document traversal treated as same-document | `JsHistory`, `JsLocation` |
| Dynamic markup | `document.open/close` no-ops, `write` cannot join split markup | `Parser`, `JsDocument` streaming insertion |
| Cookies | single static dictionary, attributes ignored | `JsDocument`, network stack |
| Navigator and capabilities | fixed values, no handler/plugin interfaces | `JsNavigator` |
| Canvas 2D | save/restore scope, drawImage overloads, text maxWidth | `JsCanvasContext2D` |
| Forms and validation | keygen, per-state coverage, submission encodings | `JsElement`, `JsFormData`, `Interaction` |
| Media (2014 semantics) | ready/network states, MediaController/mediagroup | `Lite.Media`, `JsElement` |
| Editing and selection | no Range/Selection/designMode/execCommand | new DOM ranges, `Interaction` |
| Obsolete and rendering | applet/marquee/frameset/frame processing, UA styles, print obligations | `Parser`, UA stylesheet, `Layout`, `Drawer` |

**Exit:** each reviewed-failing entry in the package's cluster becomes `included` (passing) or receives a reviewed `profile-excluded`/`post-target` classification with justification.

## Milestone 5: section and obligation review closure

Work the 762-section backlog into atomic user-agent obligations with the existing importer workflow (`scripts/import-html5-sections.py`; reviews survive only when number/title/URL match). Author/informative rules, optional capabilities, and browser-chrome items are classified with explicit decisions; every applicable obligation maps to a profile requirement and at least one test or a named reason for having none. Keep unmapped obligations untested rather than inventing coverage.

**Exit:** `html5-section-backlog.md` shows no unreviewed sections, the applicability manifest reports `inventoryComplete`, and the profile's `requirementsWithoutTests` list is empty or each remainder carries a documented justification.

## Milestone 6: evidence, audit, and readiness

Collect identity-matched evidence for every applicable requirement on one identified build: all reviewed suites, unit results, native Windows keyboard/editing/file/media evidence, and manual recordings where pixels or harnesses cannot prove behavior. Run the final audit against the profile schema (stale, conflicting, or unexplained evidence cannot establish readiness), keep the gate hygiene invariants (a waiver that passes is an error; expected-fail annotations track real outcomes), and only then re-evaluate `html5ProfileReady`. CI progression follows the CSS plan: PRs run focused suites plus the curated gates; scheduled jobs run the growing reviewed corpus; readiness flags are published, not enforced, until the audit completes.

**Exit:** complete evidence set, `html5ProfileReady` eligible, combined `releaseReady` still a separate decision.

## Coordination with the CSS 2.1 and ES2020 workstreams

The profile JSON (`Lite.Conformance/Profile/lite-html5-css21-es2020-profile.json`) is shared. This track edits only `html5.*` requirement entries inside it, re-reads the file immediately before each write, and never rebases or reverts unrelated edits it finds in progress. This track never touches: `Lite.Conformance/Test262/*` (including expected-failure lists), `Lite.Conformance/Css21/*`, `Lite.QuickJs/native/patches/*`, or `css21-*`/`es2020-*` artifacts. Gate ownership stays separate: this track must keep `--suite html5` green, and must leave `--suite wpt`, `--suite css21`, and the ES2020 supervisor exits exactly as it found them.

## Delivery order and validation

Milestone 1 runs now (dependency roots first), Milestone 2 next because parse fidelity feeds the largest `html`-root review decisions, Milestone 3 unblocks the deep `html` root and interactive clusters, Milestone 4 packages proceed in dependency order (DOM authority and dynamic markup before forms/media), Milestones 5 and 6 close. Each change delivers a reviewed classification or obligation mapping, a reproducer or upstream case, the implementation when required, regression evidence, and an updated reviewed-failing queue.

Use `dotnet build Lite.sln -c Release` and the custom executable runner `dotnet run --project Lite.Tests -c Release --no-build` for code validation, then:

```powershell
./scripts/fetch-tests.ps1 -IncludeCss21Official
./scripts/build-wpt-manifest.ps1
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html5-inventory
dotnet run --project Lite.Conformance -c Release --no-build -- --suite html5 --report Lite.Conformance/artifacts/html5-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite wpt --report Lite.Conformance/artifacts/wpt-results.json
dotnet run --project Lite.Conformance -c Release --no-build -- --suite profile --evidence Lite.Conformance/artifacts/unit-results.json --evidence Lite.Conformance/artifacts/html5-results.json --evidence Lite.Conformance/artifacts/wpt-results.json
```

For vendor WPT tests that need upstream serving, run `./scripts/serve-wpt.ps1` and pass `--wpt-base-url http://web-platform.test:8000`. Keep reports in ignored `Lite.Conformance/artifacts/`; finish edits and rebuild before collecting evidence because source, profile, lock, binaries, checkout and manifest are fingerprinted into the identity.
