# 6.K Selectors, cascade and syntax

Branch `css21/p6-k`, worktree `C:\Repos\Lite-p6-k`. Case list `Lite.Conformance/artifacts/lanes/K.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Parser.Cascade.cs
- Lite/Layout/StyleResolver.cs
- Lite/Layout/PropertyTable.cs
- Lite.AngleSharp.Css/** (new fork patches take the next number in its README, with a regression test that fails on the pristine source)

## Opens with (parked findings)

- The 4-digit hex `#1000`, which CSS 2.1 must reject (`color-030`, `outline-color-030`).
- `default-attribute-selector`: a styled `<input>` keeps its UA chrome.
- `character-encoding-017/018/028`: the encoding-dependent selector never matches.

## Coordination

- `Parser.cs` (core) is shared with the HTML5 track through `JsDocument` and the DOM projection. Name any edit there in your notes, and say that the HTML5 gate was not rerun.

## Handed in

- `first-letter-selector-011/012/013/014` (official + WPT, from 6.F): a selector list that mixes a
  pseudo-element part with a normal part (`div p:first-letter, span { color: green; margin: 15px }`)
  is lifted whole by `TryExtractPseudoElementRule`/`NeutralizePseudoElementRules`, so the `span`
  part never reaches the cascade and the control line renders unstyled. The pseudo part matches,
  so only the non-pseudo parts of such lists need to stay in the cascade. These four cases are the
  first-letter ones 6.F cannot close without it.
- `before-after-001` (official + WPT, from 6.F): `div:before:after { content: 'FAIL FAIL' }` is an
  invalid nested pseudo-element and must generate nothing; the red text is painted.
- Dynamic CharacterData mutations do not re-project or regenerate generated boxes: a text node
  that owns a split `::first-letter` keeps its stale letter/tail after `data = ""`
  (`first-letter-nested-002`), and a counter-driven `:before` is not recomputed after
  `insertBefore` (`counter-increment-000/001/002`, `counter-reset-000/001/002`,
  `counter-increment-display-004`, `counters-root-000`). Dynamic pseudo/rule generation is parked
  to 6.K per the README; 6.F has not touched it.

- `first-line-selector-016` (official + WPT) — `span, div:first-line { color: white; }` is neutralized as a whole in `Parser.Cascade.cs`, leaving `<span>FAIL</span>` inheriting `div`'s `color: red` (40 red px). — from 6.A

## Edits outside owned files

- `Lite/Parser.cs`: removed the old regex-based `InlineImports` method body (36 lines) so the spec-compliant `InlineImports` lives in owned `Lite/Parser.Cascade.cs`. The HTML5 gate was not rerun (per lane rules).

## Progress

### Fix round (batch 1 losses): ignored statements no longer end the `@import` prologue (`Parser.Cascade.cs`)
- **Losses found by the integrator** (`fix-losses.txt`, each passing at base `05228a26`): `at-import-009`, `at-import-010`, `at-rule-008`, `at-rule-009`, official and WPT (8 cases).
- **Root cause:** commit 2 (`526976c7`). Its prologue scanner ended the `@import` prologue at the first statement other than `@charset`/`@import`, whether or not that statement was ignored. CSS 2.1 §6.3 only counts a *non-ignored* statement, and §4.2 ignores unknown at-rules (`@badat-rule foo;`), malformed known at-rules (`@media;`, `@page;`) and rule sets with an invalid selector (`# {…}`, `:unknownpseudo {…}`, `1badselector {…}`).
- **Fix:** the scanner now measures each statement (an at-rule to its `;` or block; a rule set through any `;` to the end of its block, as the fork's patch 001 does), then asks the AngleSharp.Css parser whether anything survives. A statement that yields no rule, or only a style rule with a null selector, is kept verbatim and the scan goes on. Any other statement still ends the prologue and leaves later `@import` rules to the CSS parser. No fork patch changed.
- **Moved:** all 8 losses pass again. No lane gain was given up. `at-import-001/011`, `at-rule-006/007`, `c11-import-000`, `import-000`, `eof-005/006` and `uri-018` still pass, because `foo{}`, `div {…}`, `p {…}` and `body {…}` are valid rule sets, and at-import-011's `# :unknownpseudo @import …; .import {…}` is one ignored statement.
- **Checks:**
  - Build: `dotnet build Lite.sln -c Release` succeeded.
  - Units: full suite 655 passed, 0 failed, including the new `CssErrorRecoveryTests.AtImport_IgnoredStatementsDoNotEndTheImportPrologue`.
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - `fix-losses.txt`: 8/8 (4/4 official, 4/4 WPT).
  - Lane K: 24/62 official, 25/89 WPT (unchanged; the gains held).
  - `K-pass.txt`: 1,117/1,117 (587/587 official, 530/530 WPT).
  - Canary: 960/960 (490/490 official, 470/470 WPT).

### 6. Declaration property-name validation & recovery + escaped newlines in selector strings (AngleSharp.Css patch 012)
- **Spec:** CSS 2.1 §4.1.1 (declaration property name must be an `IDENT`, not a `STRING` or `NUMBER`), §4.2 (malformed declaration starting with `:` skips to `;` rather than entering CSS nesting), §4.3.7 (backslash-escaped newline in a selector string is removed).
- **Moved (6 cases: +3 official, +3 WPT):**
  - `css21-official/html4/malformed-decl-008.htm`, `css/CSS2/syntax/malformed-decl-008.xht`
  - `css21-official/html4/quoted-keywords-001.htm`, `css/CSS2/syntax/quoted-keywords-001.xht`
  - `css21-official/html4/strings-000.htm`, `css/CSS2/syntax/strings-000.xht`
- **Still failing (remaining K clusters for next wave):**
  - `lang-pseudoclass-*` / `lang-selector-*` (8 cases: `:lang()` matching & HTTP `Content-Language`)
  - `html-attribute-021/022/023/027` (8 cases: HTML presentational attribute mapping)
  - `cascade-001/004/006/010/011/012`, `c32-cascading-000`, `html-precedence-003/004/005`, `selectors-002` (cascade / `! important` / user-style precedence)
  - `matching-brackets-001/003`, `at-rule-013`, `malformed-decl-block-001`, `c71-fwd-parsing-001`, `escapes-001`, `escaped-newline-001`, `ident-020`, `numbers-units-018`
  - `c21-pseud-link-002/003`, `c25-pseudo-elmnt-000`, `pseudo-007/008`, `first-child-selector-003`, `specificity-011`
  - `at-charset-001/034`, `character-encoding-017/018/028/041`, `character-representation-001`
  - `cascade-import-001/002`, `cascade-import-dynamic-control`, `dom-hover-001`, `dynamic-sibling-combinator-001`, `c64-uri-000`, `colors-006/007`, `color-000`
  - `default-attribute-selector-007/008` (parked: styled `<input>` keeps UA chrome)
  - `inherit-computed-001/002`, `inherit-static-offset-003`, `floats/inheritance.html`, `child-bottom-margin-in-unresolvable-percentage-height.html`
- **Handed to other lanes (for integrator routing):**
  - **6.E / 6.I (8 cases):** `css/CSS2/css21-errata/s-11-1-1b-001..006, 008, 009.html` — `overflow: hidden` on `<table>` with `<caption>` (CSS 2.1 §11.1.1b erratum, table/overflow layout, not cascade/syntax).
  - **6.X (8 cases):** `css/CSS2/cascade-import/cascade-import-005..012.xht` — fetch external host `http://software.hixie.ch/utilities/cgi/test-tools/delayed-file` and require interactive alternate-stylesheet selection.
  - **6.C (1 case):** `css/CSS2/normal-flow/child-bottom-margin-in-unresolvable-percentage-height.html` — block flow / percentage height, not cascade/syntax.
- **Checks:**
  - Units: `CssErrorRecoveryTests` 12/12 passed (including new `DeclarationPropertyValidationAndSelectorEscapedNewline`).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 24/62 official, 25/89 WPT (from 0/62, 0/89 at batch start; **49 cases moved**).
  - Canary: 300/300 (154/154 official, 146/146 WPT).

### 5. CSS 2.1 color syntax: reject 4/8-digit hex and mixed `rgb()` units (AngleSharp.Css patch 011)
- **Spec:** CSS 2.1 §4.3.6 (`#rgb` and `#rrggbb` only — rejects 4-digit `#1000` and 8-digit hex; comma-separated `rgb()` must use all integers or all percentages, never mixed).
- **Moved (3 cases in K: +2 official, +1 WPT; also unblocks `outline-color-030` in 6.I):**
  - `css21-official/html4/color-030.htm` (parked finding)
  - `css21-official/html4/colors-004.htm`, `css/CSS2/syntax/colors-004.xht`
- **Still failing (related):** `colors-006`, `color-000`, `colors-007` (JS `style.color` canonical serialization).
- **Handed to other lanes:** None yet.
- **Checks:**
  - Units: `CssErrorRecoveryTests` 11/11 passed (including new `ColorSyntax_RejectsFourDigitHexAndMixedRgbUnits`).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 21/62 official, 22/89 WPT (from 19/62, 21/89; +3 cases).
  - Canary: 300/300 (154/154 official, 146/146 WPT).

### 4. CSS 2.1 selector validation & ASCII case-insensitive pseudo names (AngleSharp.Css patch 010)
- **Spec:** CSS 2.1 §4.1.3 (identifiers cannot start with a digit or `-`+digit; pseudo-class/pseudo-element names are ASCII case-insensitive), §4.1.7 / §5.2.1 (empty comma-separated selector items invalidate the selector group), §5.10 / §5.12 (pseudo-elements must appear at the end of a selector).
- **Moved (8 cases: +4 official, +4 WPT):**
  - `css21-official/html4/bad-selector-001.htm`, `css/CSS2/syntax/bad-selector-001.xht`
  - `css21-official/html4/ident-003.htm`, `css/CSS2/syntax/ident-003.xht`
  - `css21-official/html4/pseudo-014.htm`, `css/CSS2/selectors/pseudo-014.xht`
  - `css21-official/html4/pseudo-016.htm`, `css/CSS2/selectors/pseudo-016.xht`
- **Still failing (related):** `pseudo-007` (`div:fiRsT-cHiLd`), `pseudo-008` (`div:bEfOre`/`div:aFtEr`), `c25-pseudo-elmnt-000` (needs investigation — likely reference/line-box rendering rather than selector parsing, since the unit test confirms `div:first-child`, `div:before`, `div:after` all parse and `p:first-line.two` is dropped).
- **Handed to other lanes:** None yet.
- **Checks:**
  - Units: `CssErrorRecoveryTests` 10/10 passed (including new `SelectorValidation_EnforcesCss21GrammarAndCaseInsensitivePseudos`).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 19/62 official, 21/89 WPT (from 15/62, 17/89; +8 cases).
  - Canary: 300/300 (154/154 official, 146/146 WPT).

### 3. Pseudo-element cascade specificity and descendant universal base selector (`Parser.Cascade.cs`)
- **Spec:** CSS 2.1 §6.4.1 / §6.4.3 (pseudo-element rules cascade by origin, `!important`, specificity, and source order; single-colon `:before`/`:after`/`:first-letter`/`:first-line` count in `c`, not `b`), §5.10 / §5.12 (combinator or descendant space before a pseudo-element implies `*`, e.g. `div :after` → `div *`).
- **Moved (8 cases: +3 official, +5 WPT):**
  - `css21-official/html4/specificity-005.htm`, `css/CSS2/cascade/specificity-005.xht`
  - `css21-official/html4/specificity-008.htm`, `css/CSS2/cascade/specificity-008.xht`
  - `css21-official/html4/specificity-009.htm`, `css/CSS2/cascade/specificity-009.xht`
  - `css/CSS2/cascade/cascade-009a.xht`
  - `css/CSS2/cascade/cascade-009b.xht`
- **Still failing (related):** `specificity-011` (likely `border-top`/`border-bottom` rendering or shorthand expansion on `::after` box — needs a closer look if not cascade).
- **Handed to other lanes:** None yet.
- **Checks:**
  - Units: `CascadeTests` 11/11 passed (including new `PseudoElementCascade_RespectsSpecificityAndDescendantUniversal`).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 15/62 official, 17/89 WPT (from 12/62, 12/89; +8 cases).
  - Canary: 300/300 (154/154 official, 146/146 WPT).

### 2. `@import` leading-position enforcement, EOF closure, and `data:` URLs (`Parser.Cascade.cs`)
- **Spec:** CSS 2.1 §6.3 (ignore `@import` after any non-ignored statement other than `@charset` or `@import`), §4.2 (close open strings, `url(` and statements at EOF), §4.3.4 (unescape `url()` tokens), RFC 2397 (`data:` URIs in `FetchCssText`).
- **Moved (18 cases: +9 official, +9 WPT):**
  - `css21-official/html4/at-import-001.htm`, `css/CSS2/cascade/at-import-001.xht`
  - `css21-official/html4/at-import-011.htm`, `css/CSS2/cascade/at-import-011.xht`
  - `css21-official/html4/at-rule-006.htm`, `css/CSS2/syntax/at-rule-006.xht`
  - `css21-official/html4/at-rule-007.htm`, `css/CSS2/syntax/at-rule-007.xht`
  - `css21-official/html4/c11-import-000.htm`, `css/CSS2/css1/c11-import-000.xht`
  - `css21-official/html4/eof-005.htm`, `css/CSS2/syntax/eof-005.xht`
  - `css21-official/html4/eof-006.htm`, `css/CSS2/syntax/eof-006.xht`
  - `css21-official/html4/import-000.htm`, `css/CSS2/syntax/import-000.xht`
  - `css21-official/html4/uri-018.htm`, `css/CSS2/syntax/uri-018.xht`
- **Still failing (related):** `cascade-import-001` (needs investigation — likely `data:` nested `@import` URL decoding or selector matching), `cascade-import-002` (HTTP `Link:` header), `cascade-import-005..012` (external host `software.hixie.ch` + alternate stylesheet UI -> 6.X candidate).
- **Handed to other lanes:** None yet.
- **Checks:**
  - Units: `CssErrorRecoveryTests` 9/9 passed (including new `AtImport_HonorsLeadingPositionEofClosureAndEscapedUrls`).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 12/62 official, 12/89 WPT (from 3/62, 3/89; +18 cases).
  - Canary: 300/300 (154/154 official, 146/146 WPT).

### 1. Escaped newlines in strings and EOF construct closure (AngleSharp.Css patches 008, 009)
- **Spec:** CSS 2.1 §4.3.7 / §4.1.3 (escaped newlines removed from strings) and §4.2 ("Unexpected end of style sheet": close open constructs at EOF).
- **Moved (6 cases: +3 official, +3 WPT):**
  - `css21-official/html4/eof-002.htm`, `css/CSS2/syntax/eof-002.xht`
  - `css21-official/html4/escapes-000.htm`, `css/CSS2/syntax/escapes-000.xht`
  - `css21-official/html4/uri-017.htm`, `css/CSS2/syntax/uri-017.xht`
- **Still failing (related):** `eof-005`, `eof-006`, `uri-018` (`@import` at EOF without trailing `;`), `escaped-newline-001` (hex escape `\00000a` followed by newline).
- **Handed to other lanes:** None.
- **Checks:**
  - Units: 628 passed, 21 failed (all 21 are sandbox `127.0.0.1` loopback socket permission errors; all non-socket units including new `EscapedNewlineInString_IsRemoved` and `EndOfSheet_ClosesOpenConstructs` passed).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 3/62 official, 3/89 WPT (from 0/62, 0/89).
  - Canary: 300/300 (154/154 official, 146/146 WPT).
