# ES2020 implementation and remaining work

Lite targets [ECMA-262, 11th edition](https://262.ecma-international.org/11.0/),
including earlier language features and browser Annex B. **Full support is
established**: the compatibility profile reports `es2020ProfileReady` true, and
compatibility CI and NuGet release validation enforce it. The complete language
evidence on the pinned corpus is 67,117 required Test262 executions passing
with zero failures and zero expected failures, the browser host suite passes
20/20 cases, all eight host obligations are reviewed, and the normative section
review covers all 2,115 ECMA-262 11th edition clauses. The one accepted
dependency exception is the reviewed `atomics-multi-agent` exclusion below,
deferred to the Web Workers workstream.

Lite uses the bundled QuickJS 2026-06-04 runtime through `Lite.QuickJs`; the
earlier Jint results in the changelog are historical.

## Completed reviews and obligations

The table below records the review verdict for each area of the agreed scope.

| Area                                 | Review verdict                                                                                                                                                                                                                                                                                                                         | Evidence                                                                                                                                                                                                                                           |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `document.all`                       | Closed: the live HTMLAllCollection boundary carries [[IsHTMLDDA]] (typeof undefined, ToBoolean false, `== null`), stays live across mutations, and supports indexed/named property access, numeric and string calls, item/namedItem, multi-match named collections, and name-attribute lookups on the HTML named-element tags. | The annex-b-document-all host case pins all of it; the obligation review is recorded in `es2020-host-obligations.json` and the requirement is published as implemented. |
| Complete normative obligations       | Closed: all 2,115 ECMA-262 11th edition sections are reviewed in `es2020-sections.json` — 848 map directly to included tests by esid, 26 whose only direct tests are post-target features map reviewed additionalTests on the same topics, and the rest carry reviewed non-executable reasons (structural parents reviewed through their children, editorial and methodology clauses 1-5, grammar summaries, annex summaries, and specification-internal definitional or abstract-operation clauses exercised through their callers). | The readiness evaluator verifies every reviewed section against current passing evidence. |
| Complete edition applicability       | Closed: the 1,226 upstream staging tests were executed diagnostically and reviewed individually — 1,179 pass in all modes and are included in the required corpus, 47 are out-of-scope with per-test reasons; the 3,873 post-target mix-in classifications clear executably once `semanticReviewComplete` is set, with any section lacking retained included coverage still failing closed. | `scripts/gen_staging_review.py` records the staging review; the post-target rule lives in `Test262Catalog`. `sm/String/replace-math.js` (bug 805121, esid:pending, accepted outcomes include out-of-memory) is out-of-scope: it cannot meet the per-execution time contract. |
| Modern shared harness helpers         | Closed: the pinned typed-array helper's later-feature machinery is feature-gated (Float16Array only joins the constructor list when defined; resizable/growable/shrinkable/immutable buffer factories exist only when ArrayBuffer.prototype.resize is present), and the excluded-feature table classifies every test using those factories post-target, so no required execution exercises them. The one mixed-era typed-array slice test is excluded with separately mapped fixed-buffer coverage for every ES2020 typed-array constructor (supplemental/typedarray-slice-overlapping-buffer.js). | Verified against the pinned harness source; the zero-failure full run is the mechanical proof that no included execution references helper machinery absent from this engine, since a missing factory would raise and fail the shard. |
| Full language-family coverage        | Closed: the obligation-to-test review for lexical grammar, Unicode, declarations and scope, functions/classes, destructuring, iteration, generators, async functions/iteration, objects/proxies/reflect, symbols, RegExp, strings, numbers/BigInt, collections, dates, JSON, promises, buffers/typed arrays, Atomics, modules, and Annex B is complete through the section review; feature-level evidence is exported in the generated report. | All 67,117 required executions pass with full-selection, shard-consistent, identity-matched evidence. |
| Shared-memory concurrency            | The 59 multi-agent `Atomics.wait`/`Atomics.notify` executions are excluded by the reviewed `atomics-multi-agent` dependency exception in `es2020-applicability.json`; agent-cluster execution is deferred to the separate Web Workers workstream, and resolving that workstream retires the exception. | The QuickJS Test262 adapter rejects `$262.agent.start`, so cross-agent shared-memory execution cannot run in this host. The exception is published as the `es2020.shared-memory-agent-clusters` dependency exception in the compatibility profile. Every agent-free Atomics case remains required. |
| Harness execution contract            | Closed: revalidated on the QuickJS adapter by the eight-shard full runs - every mandatory execution ran under the Test262 contract (fresh engine per mode, separate parse/link/evaluation, real negative error types, raw-source preservation, asynchronous completion, isolated $262 facilities), with worker recycle and one retry for crashes and timeouts, and shard-consistent, identity-matched evidence at zero unexpected outcomes. | Focused harness regressions and supplemental reproducers pass; the full suite reports 67,117 required language executions, all passing, with zero failures and zero expected failures. |
| Module fetch options                 | Closed: root module fetches carry the script element's credentials mode and the document URL as referrer; descendants inherit the credentials mode with their importer as referrer; cross-origin requests carry origin-only Referer (default policy); credentialed cross-origin responses require an exact-origin ACAO; redirect taint re-derives CORS per hop. | Host cases cover CORS allow/deny, redirect bases, rejection types, cancellation, credentials against wildcard and exact-origin ACAO, referrer headers, and redirect taint. A complete fetch-options record (referrer policies from attributes) is a separate HTML fetch workstream. |
| Module identity and source ownership | Closed: module identity is keyed on the canonical absolute response URL (dot segments collapse, query strings distinguish); namespaces keep identity across imports with @@toStringTag and read-only properties; the module map caches fetch failures so failed URLs reject identically without refetching; inline modules keep synthetic per-script specifiers with the document base URL as import base; classic-script redirects and callback-origin import bases are closed. | Host cases cover nested graphs, live bindings, cycles, repeated imports, response-URL bases, inline metadata, identity edges (dot segments, query distinctness, namespace identity/immutability), failed-load caching, <base>-aware inline bases, redirect response URLs, and timer-callback import bases. |
| Document and iframe realms           | Closed: iframe child documents keep their own globals, intrinsics, module maps and error reporting; canceling the parent's module loads rejects child pending loads. | The iframe-realm-modules host case covers per-realm module maps, global isolation, realm-correct errors and navigation cancellation; document-realm-isolation covers separate documents. |
| Error and rejection notifications    | Closed: script errors dispatch trusted cancelable ErrorEvents with message, stack-parsed location and error identity; cancellation (preventDefault or returning-true onerror with the five legacy arguments) suppresses the default report through one unified path; unhandledrejection carries promise/reason identity and canceling it marks the promise handled (no later rejectionhandled); listener and handler-property exceptions report through the same path. | Host cases cover error identity, parse-error types, rejection/handled notifications, event fields and location, onerror cancellation with diagnostics suppression, and rejection-event cancellation. |
| Jobs and readiness                   | Closed: microtask checkpoints after scripts/callbacks/tasks; module loading or evaluation failures do not hold the document back from DOMContentLoaded/load/complete; failed module URLs fetch once; observer callbacks run within the same checkpoint after promise microtasks. | The jobs-and-readiness-failures host case covers readiness after module failures and checkpoint ordering; microtasks-and-rejection-events and module-readiness-and-inline-meta cover ordering and deferred completion. |
| Final readiness evidence             | Obtained: a reviewed inventory plus current passing results for every required execution and host obligation, with no unknown classifications, missing shards, skipped mandatory tests, timeouts, crashes, or unresolved dependency exceptions; the reviewed `atomics-multi-agent` exception is the only accepted one. | `es2020ProfileReady` is true and is enforced by compatibility CI (`run-es2020.py --require-ready`) and NuGet release validation; any execution regression, reopened review, or unresolved exception turns it false and fails those gates. |

Intl/ECMA-402, public Web Workers, parser-blocking execution, `document.write`
reentrancy, and complete dynamic-script processing remain separate workstreams.
Supported newer JavaScript features are preserved unless they conflict with
required ES2020 behavior.

## Reproduce the current results

Use Windows x64, .NET 8, and Python. From the repository root:

```powershell
./scripts/fetch-tests.ps1 -IncludeCss21Official
./scripts/build-wpt-manifest.ps1
dotnet build Lite.sln -c Release
dotnet run --project Lite.Tests -c Release --no-build
python scripts/run-es2020.py --require-ready
```

The script runs eight deterministic Test262 shards and the host suite, keeps all
failures, aggregates evidence, exports the remaining-work list, and invokes
`--require-es2020-ready`. A failing shard does not prevent the other shards or
report generation from finishing.

With `--require-ready` (used by compatibility CI and NuGet release validation)
the readiness verdict gates the exit status: any execution regression, stale or
conflicting evidence, unreviewed classification, unreviewed obligation, or
unresolved dependency exception fails the run. Without the flag the verdict is
still computed and recorded in `supervisor.json` (`readinessIsGating: false`)
but only reported. The reviewed `atomics-multi-agent` dependency exception is
the only accepted one; every other blocker keeps readiness false and, with the
flag, fails the script.

Outputs are under `Lite.Conformance/artifacts/es2020/`:

| File                                      | Content                                                                                                                    |
| ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `inventory.json`                          | Every test/fixture, source hash, metadata, required mode, classification, reason, section mapping, and inventory identity. |
| `test262-0.json` through `test262-7.json` | Individual execution outcomes, expected/observed phase and error type, selection, and shard identity.                      |
| `host.json`                               | Browser JavaScript integration outcomes.                                                                                   |
| `es2020-backlog.md`                       | Feature coverage, failures, host/inventory blockers, and every unreviewed specification section.                           |
| `es2020-backlog.json`                     | The same backlog plus every unresolved test classification.                                                                |
| `profile.json`                            | Independent ES2020, HTML, CSS, and combined readiness fields.                                                              |
| `supervisor.json` and `*.log`             | Supervisor exit codes and failure diagnostics, including incomplete runs.                                                  |

Focused commands:

```powershell
# The historical small regression selection, explicitly not full evidence.
dotnet run --project Lite.Conformance -c Release --no-build -- --suite test262 --test262-set smoke
# One mandatory language shard.
dotnet run --project Lite.Conformance -c Release --no-build -- --suite test262 --shard 0/8
# Supplemental obligations and historical engine reproducers.
dotnet run --project Lite.Conformance -c Release --no-build -- --suite test262 --filter supplemental/
dotnet run --project Lite.Conformance -c Release --no-build -- --suite test262 --filter block-decl-func-skip-arguments.js
dotnet run --project Lite.Conformance -c Release --no-build -- --suite es2020-host --filter annex-b-document-all
# Export an inventory without execution evidence; all required executions show missing.
dotnet run --project Lite.Conformance -c Release --no-build -- --suite es2020-inventory
```

`es2020-inventory` accepts repeated `--evidence` arguments to report current
executed outcomes. `profile --require-es2020-ready` accepts the same arguments.
Filtered runs and smoke runs are diagnostics and cannot satisfy full readiness.

## Coverage and evidence contract

The pinned Test262 revision is
`de8e621cdba4f40cff3cf244e6cfb8cb48746b4a`, with the complete `test` and `harness`
trees. Its 53,379 candidate tests exclude imported fixture files; source-file
counts also include fixtures and Lite's separately identified supplemental tests.
The original 1,304-file baseline was a smoke selection, not a full-support claim.

Execution follows the [Test262 contract](https://github.com/tc39/test262/blob/main/INTERPRETING.md):
fresh engines per mode, separate parsing/linking/evaluation, real JavaScript
negative error types, raw-source preservation, asynchronous completion, and
isolated `$262` facilities. Worker processes enforce a hard per-execution timeout
and restart after a crash or timeout. Mandatory skipped tests remain failures.

Evidence format 6 records source revision/content, dependency binaries and pins,
engine/harness binaries, suite inputs, platform, inventory, execution mode,
expected/observed phase and error type, and selection/shard identity. Missing,
stale, conflicting, incomplete, filtered, or smoke evidence cannot establish
readiness. Editing sources during a run preserves diagnostics but invalidates
the run for readiness. Do not rewrite an artifact's identity to reuse it.

The complete review contract requires more than green Test262 counts. Unknown
classifications and unmapped normative obligations remain blockers: the
semantic-review flag records that the staging and post-target editions review
is complete, and the section review records every clause's verdict.

Two manifests separate a known defect from a new one, on the same contract as the
curated WPT manifest:
[`es2020-expected-failures.txt`](../Lite.Conformance/Test262/es2020-expected-failures.txt)
for mandatory Test262 executions and
[`es2020-host-expected-failures.txt`](../Lite.Conformance/Test262/es2020-host-expected-failures.txt)
for host obligations. A listed test still executes, its real outcome is still
written to the evidence, and it still keeps `es2020ProfileReady` false; listing it
only stops a published, unresolved defect from reading as a fresh regression on
every run. Every entry must be published in the compatibility profile as `failing`
or `dependency-exception`, or the profile validator rejects it, and a listed test
that starts passing is an unexpected pass that fails the suite. Neither file may be
used to waive an unexplained failure: `Test262/skip-list.txt` remains the place for
a dependency exception once there is an upstream issue to cite.

Tests that cannot run in this host at all are excluded one level earlier, in the
reviewed `dependencyExceptions` block of
[`es2020-applicability.json`](../Lite.Conformance/Test262/es2020-applicability.json).
Each exception is named, carries its own reason and the workstream that will
resolve it, and must be published in the compatibility profile as
`dependency-exception`; an exception with an unknown path or a missing review
state fails readiness closed. The reviewed `atomics-multi-agent` exception
(multi-agent `Atomics.wait`/`Atomics.notify` coverage, deferred to the Web
Workers workstream) is currently the only one.

The host review is separately recorded in
[`es2020-host-obligations.json`](../Lite.Conformance/Test262/es2020-host-obligations.json).
Each obligation must be reviewed and mapped to mandatory host tests; its open
implementation or review work is also included in the generated backlog.
