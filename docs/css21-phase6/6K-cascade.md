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

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

- `Lite/Parser.cs`: removed the old regex-based `InlineImports` method body (36 lines) so the spec-compliant `InlineImports` lives in owned `Lite/Parser.Cascade.cs`. The HTML5 gate was not rerun (per lane rules).

## Progress

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
