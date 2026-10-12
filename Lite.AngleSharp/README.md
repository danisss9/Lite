# Vendored AngleSharp

This project builds the pinned upstream AngleSharp parser from source so the HTML 5.0
conformance track can carry tree-construction patches the NuGet package does not have.

## Provenance

- Upstream: https://github.com/AngleSharp/AngleSharp
- Base: tag `v1.7.3`, commit `0e6979a` (matches the NuGet package pinned in
  `Directory.Packages.props` at 1.7.3; verified by assembly identity: name `AngleSharp`,
  version `1.7.3.0`, public key token `e83494dcdc6d31ea` from upstream's committed
  `Key.snk`, which this project signs with).
- Scope: `src/AngleSharp/**` only (no upstream tests, benchmarks or docs). The upstream
  project file is replaced by `Lite.AngleSharp.csproj`, which keeps the upstream compile
  surface for net8.0 (`Nullable` enable, LangVersion latest, SYSLIB0014 suppressed).

## Patch policy

The `native/src` tree starts pristine. Every deviation from upstream is a git commit on
top of it and MUST be listed here with the html5lib/WPT evidence that motivates it, so
the diff against the pinned tag stays reviewable (`git diff v1.7.3-equivalent`).
Re-basing onto a newer upstream tag is a deliberate, separately-reviewed act.

| Patch | Motivation |
| --- | --- |
| 001 — menuitem parses as a normal element (`HtmlMenuItemElement` loses `Special\|SelfClosing`; `menuitem` removed from the `AllBodyClosed` void-close list) | menuitem is absent from the W3C HTML 5.0 REC, so its start tag takes the "any other start tag" path and accepts children; upstream's post-2014 void semantics hoisted content out as siblings. menuitem-element.dat 9→18/20 (two residuals: formatting reconstruction with `<p><b></p><menuitem>`, and the in-select case whose expectation is itself post-2014). |

## Coordination

AngleSharp.Css and AngleSharp.Xml stay on their NuGet pins; both bind the vendored
assembly by its unchanged strong-name identity. The CSS 2.1 track compiles against the
same assembly — changes here must keep `--suite css21` green.
