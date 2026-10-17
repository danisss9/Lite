# Vendored AngleSharp.Css

This project builds the pinned upstream AngleSharp.Css library from source so the CSS 2.1
conformance track can carry parser patches the NuGet package does not have.

## Provenance

- Upstream: https://github.com/AngleSharp/AngleSharp.Css
- Base: tag `v1.0.2`, commit `d158478` (matches the NuGet package pinned in
  `Directory.Packages.props` at 1.0.2; verified by assembly identity: name `AngleSharp.Css`,
  version `1.0.2.0`, public key token `e83494dcdc6d31ea` from upstream's committed
  `Key.snk`, which this project signs with).
- Scope: `src/AngleSharp.Css/**` only (no upstream tests, benchmarks or docs). The upstream
  project file is replaced by `Lite.AngleSharp.Css.csproj`, which compiles the same sources
  for net8.0 against the vendored `Lite.AngleSharp` core instead of the NuGet AngleSharp.

## Patch policy

The `native/src` tree starts pristine. Every deviation from upstream MUST be listed here with
the CSS 2.1 suite evidence that motivates it, so the diff against the pinned tag stays
reviewable. Re-basing onto a newer upstream tag is a deliberate, separately-reviewed act.

| Patch | Motivation |
| --- | --- |
| 001 — CSS 2.1 §4.2 error recovery: `CssTokenizer.ContentFrom` reads a component-value run observing strings, comments and the matching pairs `()`/`[]`/`{}`, ends a declaration value at a top-level `;`/`}`, an at-rule prelude also at `{`, and a style-rule prelude only at `{`/`}`; a string broken by a newline (BAD_STRING) or a `url(` that is not a complete CSS 2.1 URI token (BAD_URI, which keeps its parenthesis open) invalidates the construct. `CssBuilder` drops such declarations and rules, skips malformed rule sets through their block, and skips declarations, statements and unknown at-rule preludes observing `()`/`[]`. `CssComponentScan.IsWellFormedValue` exposes the scan so Lite's raw `background` rescue rejects exactly what the parser rejects. | Upstream scanned raw text to the first `;`, `{` or `}` with no pair or BAD_STRING/BAD_URI handling, so it resynchronised at the wrong token: `@ import "x"; div {…}` kept the div rule (at-rule-001/011/012, at-rules-004), `url("x";` and `url(test/* test)` became background images (uri-003/012/013/016), and unknown at-rule preludes ended at a `;` inside `[ ]` (at-rule-013). Regression tests: `Lite.Tests/CssErrorRecoveryTests.cs` (all five fail against the pristine source). |

## Coordination

The HTML track's AngleSharp core fork (`Lite.AngleSharp`) is the dependency; AngleSharp.Xml
stays on its NuGet pin and binds the vendored core by its unchanged strong-name identity.
Changes here must keep `--suite css21`, `--suite html5` and `--suite wpt` green.
