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
| 002 — foreign-content breakout applies in the fragment case (`ForeignNormalTag` no longer branches to `ForeignSpecialTag` when `IsFragmentCase`; the pop loop checks before popping, and the fragment-root case reprocesses through the insertion-mode rules via `Home` to avoid re-dispatching on the foreign context) | REC §8.2.5 pops breakout tags out of foreign content with no document/fragment distinction; upstream disabled the breakout for fragments, so `<nobr>`, `<div>`, `<h1>`, … inside an SVG/MathML fragment context stayed foreign (`<svg nobr>`, `<math div>`). foreign-fragment.dat 48→64/66. |
| 003 — fragment context tokenizer states are HTML-namespace only (`ParseFragment` gates the RCDATA/RAWTEXT/… setup on `context.NamespaceUri == HTML`) | The context-element tokenizer state list names HTML elements; an SVG `<title>` fragment context is foreign content parsed in the data state with integration-point rules. Upstream matched on the local name alone, so SVG title fragments parsed their input as RCDATA text (`foreign-fragment.dat` svg title/svg desc cases). |
| 004 — input/keygen/textarea in select: close-select + reprocess, with the fragment context counting for the scope check | The 2014 in-select rule pops until a select is popped, resets, and reprocesses; upstream's version ignored the tokens entirely in fragments (the context select is not on the stack). webkit02 #43 (document, sibling) and tests_innerHTML_1 #76/77 (fragment, root children) both hold under this reading; tests7 #33's nested-keygen expectation contradicts the 2014 letter and the sibling expectation of #43 — recorded as a corpus-internal inconsistency. |
| 005 — the data state emits U+0000 as-is; HTML text insertion drops it (`HtmlTokenizer.DataText` appends NUL; `AddCharacters` strips it — `Foreign()` already replaced NUL with U+FFFD) | §8.2.5 replaces NUL with U+FFFD in foreign text; upstream's tokenizer dropped NUL before the tree builder ever saw it, so `<svg>\u0000filler` lost both replacement characters (plain-text-unsafe.dat #14/#15 now pass). Four plain-text-unsafe cases (#1/#2/#4/#5) flip the other way: their expectations ignore NUL during the head phases — post-2014 behavior the 2014-correct engine no longer reproduces (recorded for the expectation-invalid family). |
| 006 — template in after-head processes using the in-head rules against the existing head | Browsers insert `<template>` into head in the after-head mode; upstream fell through to the anything-else branch, creating a body and landing the template there. Not yet effective on the corpus (template.dat #103–105 still fail) — needs a follow-up trace. |
| 007 — frameset mode inserts every whitespace character and ignores the rest (`InFrameset` filters per character instead of `TrimStart` + reprocess) | The in-frameset whitespace rule keeps each space/tab/LF/FF; upstream's TrimStart dropped all but one space (`<frameset> te st\n` dumps `"  \n"` in browsers). tests2.dat #7 fixed; #6's two-space run still differs. |
| 008 — the reset's html case follows the head pointer (`Reset()` selects before-head when the document has no head, otherwise after-head, instead of upstream's unconditional before-head) | The 2014 reset algorithm: "If the head element pointer is null, switch the insertion mode to before head; otherwise, the head element pointer is not null, switch the insertion mode to after head." The after-head EOF then synthesizes the body. Upstream's before-head surfaced once the template-in-after-head handling left the stack as [html] at the `</template>` reset: EOF created a second empty head AND a body (template.dat #103-105 — the corpus wants one head carrying the template plus the synthesized body, matching Chromium). |

## Coordination

AngleSharp.Css and AngleSharp.Xml stay on their NuGet pins; both bind the vendored
assembly by its unchanged strong-name identity. The CSS 2.1 track compiles against the
same assembly — changes here must keep `--suite css21` green.
