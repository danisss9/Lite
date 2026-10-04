# ES2020 implementation and remaining work

Lite targets [ECMA-262, 11th edition](https://262.ecma-international.org/11.0/),
including earlier language features and browser Annex B. Full support is **not
established**. The profile must keep `es2020ProfileReady` false until every
mandatory execution and host obligation has reviewed, current passing evidence.

Lite now uses the bundled QuickJS 2026-06-04 runtime through `Lite.QuickJs`.
Jint is no longer a runtime or package dependency. The earlier Jint results in
the changelog are historical. On the current QuickJS build the full pinned
Test262 selection executes green: every mandatory language execution passes
with zero failures and zero expected failures, and the host suite passes, so
no engine defect is currently published or waived. Full support is still not
established — readiness additionally requires the normative and edition
reviews and the host obligations below.

## Remaining features and obligations

This is the implementation and verification backlog for the agreed scope.
An unreviewed obligation is not a claim that the corresponding feature is absent.
Because the normative and edition reviews are unfinished, the confirmed defect
list cannot yet be asserted to contain every possible ES2020 defect.

| Area                                 | Remaining work                                                                                                                                                                                                                                                                                                                         | Current evidence or limitation                                                                                                                                                                                                                                           |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `document.all`                       | Review full HTMLAllCollection legacy property semantics and normative coverage. | A stable, live, callable collection now passes the focused `annex-b-document-all` host case, including `[[IsHTMLDDA]]` conversion, equality, and `typeof`. This is not yet a completed obligation review. |
| Complete normative obligations       | Review syntax, static semantics, abstract operations, execution contexts, built-ins, module semantics, shared memory, and Annex B; split sections into individually mapped obligations where needed.                                                                                                                                   | The 2,115-section index is complete as a section index, but every section starts unreviewed. Importing section headings is not normative verification.                                                                                                                   |
| Complete edition applicability       | Review staging tests, untagged later semantic changes, and every mixed-era exclusion. Supply a separately mapped ES2020 test before excluding incompatible mixed coverage.                                                                                                                                                             | The first full inventory found 1,226 unreviewed staging tests and 3,873 post-target tests referring to ES2020 sections. The exact current list is exported in `es2020-backlog.json`.                                                                                     |
| Modern shared harness helpers        | Review implicit later features introduced by helper files, even where test metadata only names ES2020 features.                                                                                                                                                                                                                        | The pinned typed-array helper also generates Float16, resizable, growable, and immutable buffers. One overlapping-slice test now has separately mapped fixed-buffer coverage for every ES2020 typed-array constructor. The rest still needs review.                      |
| Full language-family coverage        | Finish obligation-to-test review for lexical grammar, Unicode, declarations and scope, functions/classes, destructuring, iteration, generators, async functions/iteration, objects/proxies/reflect, symbols, RegExp, strings, numbers/BigInt, collections, dates, JSON, promises, buffers/typed arrays, Atomics, modules, and Annex B. | Full candidate execution exists. Passing candidates alone do not establish that each normative obligation is tested. The generated report lists feature-level evidence and all remaining section IDs.                                                                    |
| Shared-memory concurrency            | The 59 multi-agent `Atomics.wait`/`Atomics.notify` executions are excluded by the reviewed `atomics-multi-agent` dependency exception in `es2020-applicability.json`; agent-cluster execution is deferred to the separate Web Workers workstream, and resolving that workstream retires the exception. | The QuickJS Test262 adapter rejects `$262.agent.start`, so cross-agent shared-memory execution cannot run in this host. The exception is published as the `es2020.shared-memory-agent-clusters` dependency exception in the compatibility profile. Every agent-free Atomics case remains required. |
| Harness execution contract           | Review metadata, raw/module combinations, realm host APIs, negative phase boundaries, asynchronous completion, timeout recovery, and crash diagnostics on the QuickJS adapter. | Focused harness regressions pass. `$262.IsHTMLDDA` is callable and returns null like `document.all`, and dependency-module load/parse failures are classified as resolution-phase errors. The adapter and remaining failure classifications still need broader revalidation after this engine switch. |
| Module fetch options                 | Closed: root module fetches carry the script element's credentials mode and the document URL as referrer; descendants inherit the credentials mode with their importer as referrer; cross-origin requests carry origin-only Referer (default policy); credentialed cross-origin responses require an exact-origin ACAO; redirect taint re-derives CORS per hop. | Host cases cover CORS allow/deny, redirect bases, rejection types, cancellation, credentials against wildcard and exact-origin ACAO, referrer headers, and redirect taint. A complete fetch-options record (referrer policies from attributes) is a separate HTML fetch workstream. |
| Module identity and source ownership | Closed: module identity is keyed on the canonical absolute response URL (dot segments collapse, query strings distinguish); namespaces keep identity across imports with @@toStringTag and read-only properties; the module map caches fetch failures so failed URLs reject identically without refetching; inline modules keep synthetic per-script specifiers with the document base URL as import base; classic-script redirects and callback-origin import bases are closed. | Host cases cover nested graphs, live bindings, cycles, repeated imports, response-URL bases, inline metadata, identity edges (dot segments, query distinctness, namespace identity/immutability), failed-load caching, <base>-aware inline bases, redirect response URLs, and timer-callback import bases. |
| Document and iframe realms           | Expand integration evidence for iframe imports, independent module maps/jobs, navigation cancellation, and realm-correct errors across document boundaries.                                                                                                                                                                            | Separate-document globals/intrinsics/module maps and navigation cancellation have focused checks; existing iframe unit tests provide additional regressions. The complete host obligation review remains open.                                                           |
| Error and rejection notifications    | Complete browser event semantics and error location details, including cancellation/default reporting and callback exceptions.                                                                                                                                                                                                         | Original thrown objects are preserved, parse failures report `SyntaxError`, and rejection/handled notifications preserve promise and reason identity. Error/rejection notifications currently use simple objects rather than complete browser event implementations.     |
| Jobs and readiness                   | Expand ordering coverage across scripts, deferred modules, callbacks, observers, tasks, and failure paths.                                                                                                                                                                                                                             | Modules complete before `DOMContentLoaded`; deferred execution observes `interactive`; promise/timer order is tested. This does not claim full HTML script-processing conformance.                                                                                       |
| Final readiness evidence             | Obtain a reviewed inventory plus current passing results for every required execution and host obligation, with no unknown classifications, missing shards, skipped mandatory tests, timeouts, crashes, or unresolved dependency exceptions; the reviewed `atomics-multi-agent` exception is the only accepted one. | Eight-shard execution and fail-closed aggregation are implemented. The readiness verdict stays false while any mandatory work above remains; CI and release validation report it. |

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
python scripts/run-es2020.py
```

The script runs eight deterministic Test262 shards and the host suite, keeps all
failures, aggregates evidence, exports the remaining-work list, and invokes
`--require-es2020-ready`. A failing shard does not prevent the other shards or
report generation from finishing.

Its exit status covers execution only — the shards, the host suite and the
inventory export. The readiness verdict is printed and recorded in
`supervisor.json` (`readinessIsGating: false`), but does not fail the script,
because readiness also depends on the unfinished normative and edition review
below: no amount of green execution can clear it, so gating everyday builds on
it would report every change as broken for reasons unrelated to that change.
Compatibility CI therefore publishes the readiness verdict without enforcing it.
NuGet release validation publishes the same verdict as release evidence without
enforcing it. No Test262 execution currently fails; the incomplete
`document.all` obligation review, the remaining host obligations, and the
unfinished normative and edition review keep readiness false. Reconsider the
release gate after those gaps and the review are resolved.

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
classifications and unmapped normative obligations remain blockers. Previous
Jint-specific outcomes do not establish QuickJS behavior; retain useful
reproducers and regenerate execution evidence after the migration.

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
