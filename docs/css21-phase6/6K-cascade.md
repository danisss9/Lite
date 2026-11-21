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

- `table-visual-layout-012` (official + WPT, from 6.E): `background` shorthand vs `background-color`
  longhand. A LOWER-specificity shorthand declaration wins over a more specific longhand when the
  shorthand comes later: on a plain `div`, `div{background-color:orange} #x{background-color:blue}`
  renders blue, but `#x{background-color:blue} div{background:orange}` renders orange.
  `getComputedStyle(div).backgroundColor` already reads orange, so it is the computed-style cascade
  (AngleSharp's shorthand expansion order), not the painter. The test's `#cell{background-color:blue}`
  loses to `td{background:orange}`, so the rowspan cell paints orange; it probably affects 6.H too.
  6.E's TableEngine only consumes the computed colour.

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
  **Batch 2 update (6.F):** 6.F now rebuilds the `::first-letter` split after a CharacterData
  mutation (`Lite/Parser.GeneratedContent.cs` `RefreshFirstLetter`, called from
  `Parser.ReProjectChildren` and `JsElement.SetCharacterData`), which closed
  `first-letter-nested-002/003/004/005`; `counter-increment-display-004` and `counters-root-000`
  are fixed outright (a hidden element's pseudo does not increment; repeated names reset one
  instance). What remains yours: the counters' pages use `setTimeout('run()', 0)` and the engine's
  `setTimeout` accepts only a function (`TypeError: not a function`), so the mutation never
  happens; and the inserted element still needs its own generated box.
- **Batch 2 hand-ins (from 6.F):**
  - `before-after-dynamic-restyle-001` (official + WPT): a class change that first makes
    `.cl:after` match must create the box; 6.F's refresh only rewrites existing `attr()` text.
  - `first-letter-quote-002/004/005` (quote-002/005 WPT, quote-004 both) and
    `first-letter-punctuation-184`: the first letter must start at punctuation that sits in an
    earlier box. 6.F wrote the split but did not land it: it makes
    `first-letter-dynamic-001`'s REFERENCE green while the subject's `:first-letter` rule (added to
    a `<style>` after parse) is never collected, a pass-list loss. Land the two together.
    `first-letter-punctuation-184` is separately a Unicode-version question (.NET 8: U+166D is
    `So`) — see 6.M.
  - `first-letter-dynamic-001/002/003a/003b` (official + WPT): rules added to a `<style>` element
    after parse never enter `Current.PseudoElementRules` (collected once at parse start), so a
    new `:first-letter`/`:before` rule never matches. Needs collection on sheet mutation plus a
    recascade.
  - `first-letter-selector-028` (WPT): same selector-list neutralization as 011–014
    (`div#test:first-letter, span#reference { … }`), which you already have.
  - `before-inheritable-002` / `after-inheritable-002` (official + WPT) and `before-after-001`:
    still undiagnosed by 6.F; `before-after-001` is the invalid `:before:after` case you already
    have.

- `first-line-selector-016` (official + WPT) — `span, div:first-line { color: white; }` is neutralized as a whole in `Parser.Cascade.cs`, leaving `<span>FAIL</span>` inheriting `div`'s `color: red` (40 red px). — from 6.A
- `border-{top,right,bottom,left}-color-030` (official, 4) and `outline-color-030` (official, 1): 4-digit hex `#1000` is parsed as transparent instead of rejected as invalid CSS 2.1 color (already listed in your Opens with) (from 6.I).
- `border-width-010` (official+WPT, 2): `border: red solid -1px` has a negative border width and must drop the entire shorthand declaration during cascade so the prior green 8px solid border wins (from 6.I).
- `border-width-011/012` (official, 2) and `outline-width-096` (official, 1): `StyleResolver.EffectiveValue` passes the parent's raw width instead of computed `0px` when the parent's `border-style` is `none`/`hidden` (§8.5.1) or `outline-style` is `none` (§18.4). Note: for `border-width-011/012`, once `StyleResolver` writes `"0px"` into `StyleOverrides`, coordinate with 6.I if `GetBorderSideWidth` also needs to read inherited `"0px"` (from 6.I).

- `css/CSS2/cascade/html-precedence-003.xht` — the WPT manual copy now runs the fail-closed selftest route and fails:
  `<font color="green">` inside a red `div` paints red. The file's assert says the presentational `color` attribute
  trumps the author `div` rule and the user sheet's universal selector (§6.4.4 cascade order); the attribute seems
  ignored. Render evidence: `artifacts/hp3.png` in the 6.M worktree. From 6.M.
- **`margin-em-inherit-001` / `padding-em-inherit-001` (WPT twins), from 6.C** — the `inherit`
  computed-value bug is fixed (commit 1 of this batch); what remains is one 2-pixel row at the bottom
  of the green box. The reference fixes `height: 194px` for a 100px inline image in a 40px font,
  i.e. a 110px line box; our `line-height: normal` strut measures 108.48px (font descent 8.48px,
  ascent 36.21px at 40px). This is the font metric and the §10.8.1 strut, not the cascade.
  **From 6.K (this batch) to 6.J + 6.A.**

- **`default-attribute-selector-007/008` (official + WPT, 4 cases), from 6.K to 6.I (Drawer
  painter):** the selectors are correct — the test's `input`/`button` green background never
  paints because `Drawer.PaintInput` (Lite/Drawer.cs:1771) fills text-like inputs with
  `SKColors.White` and a hardcoded grey border, and `PaintButton` (line 2204) paints native
  chrome; the author `background-color`/border is not consulted. The painted dominant colour is
  the chrome grey (`#cacaca` / `#a0a0a0`). A fix is: when the element carries an author
  background or border, `DrawBorders` + `GetBackgroundColor` instead of the native chrome.

- **`cascade-001/004/006/010/011` and `html-precedence-003` (official), from 6.K to 6.X
  (evidence/vendor):** the official `html4/support/cascade.css` and
  `html4/support/html-precedence-003.css` are missing from the vendored official tree, so
  `WptVisualPage.UserStyleSession` finds no user sheet and the prerequisite user styles never
  apply. The maintained WPT copies (`css/CSS2/cascade/support/cascade.css`,
  `support/html-precedence-003.css`) exist and are identical. `cascade-012`'s imported sheet was
  a real engine bug and is fixed in commit 3.

- **`html-precedence-004/005` (WPT), from 6.K to 6.M:** the user stylesheet is quoted inside the
  test text, not linked; a harness cannot know it. Manual-pending, like the existing
  user-stylesheet judges.
- **`pseudo-007` (official + WPT), from 6.K to 6.X:** the maintained WPT twin's `rel=match` is
  `universal-selector-002-ref.xht` (blue borders around viewport and div) while the test only
  sets `div:fiRsT-cHiLd { color: green }` and carries no border rule. No engine can match that
  reference; the official copy's own diagnostic passes. The reference (or the match) needs fixing
  upstream.
- **`pseudo-008` (official + WPT), from 6.K to 6.A/6.F:** `<div> </div>` between `::before`
  ("Filler") and `::after` ("Text") carries a single space that must collapse to one space
  (reference: "Filler Text"). Our generated line comes out without the space (a collapsible
  whitespace-only text run between the two boxes is dropped).
- **`c64-uri-000`, from 6.K to 6.X:** the test's own support sheets (`css1test64a.css`,
  `css1test64b.css`) set `background: red url(support/swatch-red.png)` for `.one`/`.two`, and
  `swatch-red.png` is solid red, while the page and the WPT reference demand three GREEN
  backgrounds. The test contradicts its own stylesheets as vendored.
- **`numbers-units-018`, from 6.K to 6.C:** §4.3.2 on the root element — `20em`/`20ex` in
  `html { margin-left: 20em; margin-top: 20ex }` must use the INITIAL font size (16px), not the
  root's own `font: 20px`. The em/ex conversion lives in `Lite/Layout/CssUnits.cs` (6.C).
- **`html-attribute-021/022` (iframe `marginheight`/`marginwidth`), from 6.K to 6.H:** the nested
  document's `* { background-color: green }` on `html`/`body` must propagate to the frame's
  canvas; our iframe paints white (no canvas-background propagation for child documents), so the
  frame shows no green box at all. The margin attributes themselves are low-precedence hints and
  not the blocker.
- **`specificity-011` (WPT), from 6.K: parked for 6.F/6.I.** `div :after { content: "PASSED";
  border-top: solid teal }` matches (the base selector resolves to `div *`), but the reference's
  teal top / blue bottom borders on the generated inline do not match ours; needs a render diff
  of the ::after border painting, not the cascade.
- **`first-child-selector-003` (WPT), from 6.K: needs a selector-engine look.** `:root:first-child
  { color: green }` must beat `html { color: red }`; AngleSharp's match of `:root:first-child`
  on the document element is the suspect.

- **`escapes-001` (official + WPT), from 6.K to 6.J (font fallback):** the render is correct
  (green background, white text) and the only pixel difference is the glyph face: the test's
  `font-family: "\"", '\'', serif` should fall through the two unavailable quoted families to
  `serif`, but our font selection lands on the default sans. The declaration parses (background
  applies); the family-list fallback does not.

- **`escaped-newline-001` (official + WPT), from 6.K: parsing parked.** The paint trace shows no
  generated text at all on the test page (the reference page paints `Filler Text Filler Text`
  correctly), so the `div:before` rule is being lost, not mis-measured. The value is
  `"Filler\<escaped newline> Text\00000a<newline> Filler Text"`; patch 008 handles the escaped
  newline and `ConsumeEscape` consumes the whitespace after the hex escape, so the remaining
  suspect is the CSSOM serialization/host `ParseCssTextToDict` round trip of a string containing
  a line feed. Needs a focused parser probe next batch.

- **Remaining syntax/matching cases not investigated this batch** (`matching-brackets-001/003`,
  `at-rule-013`, `malformed-decl-block-001`, `c71-fwd-parsing-001`, `ident-020`,
  `numbers-units-018`, `c64-uri-000`): `numbers-units-018` is root-em/ex (6.C), `c64-uri-000`
  looks like a broken test (above); the rest need per-case parser probes with the fork before
  any fix.

## Edits outside owned files

- `Lite/Parser.cs`: removed the old regex-based `InlineImports` method body (36 lines) so the spec-compliant `InlineImports` lives in owned `Lite/Parser.Cascade.cs`. The HTML5 gate was not rerun (per lane rules).
- Batch 2, `Lite/Parser.cs` (shared with the HTML5 track; the HTML5 gate was not rerun):
  - `ParseState.ContentLanguage` plus its two fetch-path assignments (`TraversePage` and
    `ParseChildPage`) for `:lang()`'s HTTP document language (commit 2);
  - `StyleResolver.InheritOverrideChannelIntoSubtree(node)` after `ApplyPresentationalHints`
    (commit 2), so a child element/text run inherits an override-channel value;
  - the linked-sheet `<style>` insertion at the link's position instead of `head.AppendChild`
    (commit 4).

## Progress

### Batch 2, commit 4 - a fetched `<link>` sheet keeps its source-order position (§6.4.1)

- **Spec:** CSS 2.1 §6.4.1: declarations of one origin with equal specificity are ordered by
  source order; §14.3 defines `<link rel=stylesheet>` as an author sheet at its document position.
- **Root cause:** `Parser.cs` eagerly fetches every linked sheet and appends the generated
  `<style>` element to the END of `<head>`. A `<link>` placed BEFORE an inline `<style>` therefore
  cascaded AFTER it: `c32-cascading-000`'s linked `.c { color: red }` beat the later inline
  `.c { color: green }`, reddening the last paragraph.
- **Fix:** the generated `<style>` is inserted at the link's own position
  (`link.Parent.InsertBefore(styleEl, link.NextSibling)`); the head-append remains as the
  detached-link fallback.
- **Moved (2):** `css21-official/html4/c32-cascading-000.htm`,
  `css/CSS2/css1/c32-cascading-000.xht`.
- **Edits outside owned files:** `Lite/Parser.cs` (shared with the HTML5 track). **The HTML5 gate
  was not rerun** (lane rules).
- **Checks:**
  - Build: succeeded.
  - Units: 739 passed, 0 failed, including the new
    `CascadeTests.LinkedSheetKeepsItsSourceOrderBeforeALaterStyleElement` (a data: `<link>`
    before an inline `<style>`).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 4/37 official, 8/65 WPT (from 0/37 + 1/65 at batch start; **11 cases moved** —
    12 now pass, of which `inherit-static-offset-003` already passed).
  - `K-pass.txt`: 1,166/1,166; Canary: 960/960.
  - `C-pass.txt` (source-order change reaches every page): 2,974/2,974.

### Batch 2, commit 3 - `! important` (whitespace) keeps its priority (AngleSharp.Css patch 013)

- **Spec:** CSS 2.1 §4.1.9: a declaration's priority is `!` followed by optional whitespace and
  the keyword `important`.
- **Root cause:** `CssBuilder.CreateValue` required the exact string `!important` at the end of
  the value, so `cascade-012`'s imported `support/cascade-012.css` (`.test { color: green !
  important }`) lost its priority and the later author `color: red` won. The host's
  `NormalizeImportantBangs` only rewrites `<style>` blocks in the HTML source, never `@import`ed
  text — the fork patch covers every sheet.
- **Moved (1 case):** `css/CSS2/cascade/cascade-012.xht`.
- **Still failing (related):** none of the other `! important` cases; K-pass held.
- **Checks:**
  - Build: succeeded.
  - Units: 738 passed, 0 failed, including the new
    `CssErrorRecoveryTests.BangImportant_AllowsWhitespaceBeforeTheKeyword` (fails on pristine).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 3/37 official, 7/65 WPT (from 0/37 + 1/65 at batch start: **9 cases moved** in the
    batch).
  - `K-pass.txt`: 1,166/1,166; Canary: 960/960.
  - `A-pass.txt` (parser change reaches every stylesheet): 1,405/1,405.
  - Fork patch: next number is now 014 in `Lite.AngleSharp.Css/README.md`. The HTML5 gate was
    not rerun (lane rules); the fork is shared with the HTML5 track.

### Batch 2, commit 2 - `:lang()` language ranges, document language and override-channel inheritance (§5.11.4 / §6.2.1)

- **Moved (8 cases: +3 official, +5 WPT):**
  - `css21-official/html4/lang-pseudoclass-001.htm`, `css/CSS2/selectors/lang-pseudoclass-001.html`
  - `css21-official/html4/lang-selector-005.htm`, `css/CSS2/selectors/lang-selector-005.xht`
    (HTTP `Content-Language: fr`)
  - `css21-official/html4/lang-selector-006.htm`, `css/CSS2/selectors/lang-selector-006.xht`
    (`meta http-equiv="content-language" content="fr"`)
  - `css/CSS2/selectors/lang-selector-004.xht` (inherited `lang="fr"`)
  - `css/CSS2/selectors/lang-pseudoclass-002.xht` (XHTML `xml:lang`, range semantics, `<em>`)
- **Root causes, three of them:**
  1. **Range semantics were wrong** (`Parser.Cascade.cs`, `ElementMatchesLang`): the old code
     compared the PRIMARY subtags only, so `:lang(en-GB)` wrongly matched `lang="en"`, `lang="en-US"`
     and `lang="en-GB"` all. §5.11.4's argument is a language range: it matches the language
     itself or the range followed by a hyphen and more subtags.
  2. **No document language** when no `lang`/`xml:lang` is in scope: now the HTTP
     `Content-Language` header (two lines in `Parser.cs`'s `ParseState`/fetch paths) and an HTML
     or XHTML `meta http-equiv="content-language"` are consulted, in that order.
  3. **The override channel did not inherit** (`StyleResolver.cs`): a `:lang`/`:link`/dynamic
     rule reaches only the element's override channel, which AngleSharp's cascade never sees. Text
     runs and child elements therefore kept the stale computed value: the XML-served
     `lang-selector-004` painted its text black, and `lang-pseudoclass-002`'s `<em>` inside
     `p:lang(es) { color: white }` stayed black. New
     `StyleResolver.InheritOverrideChannelIntoSubtree` copies the element's inherited
     override values onto its text runs and child elements whose OWN cascade does not declare the
     property (checked with `ComputeExplicitStyleFor`), after presentational hints so a child
     `<font color>` attribute still wins.
- **Edits outside owned files:** `Lite/Parser.cs` (shared with the HTML5 track): the
  `ParseState.ContentLanguage` field, its two fetch-path assignments, and the
  `InheritOverrideChannelIntoSubtree` call after `ApplyPresentationalHints`. **The HTML5 gate was
  not rerun** (lane rules).
- **Checks:**
  - Build: `dotnet build Lite.sln -c Release` succeeded.
  - Units: full suite 737 passed, 0 failed, including the new
    `CascadeTests.Lang_MatchesRangesDocumentLanguageAndReachesText`.
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 3/37 official, 6/65 WPT (from 0/37, 1/65; **8 cases moved**, the list above).
  - `K-pass.txt`: 1,166/1,166 (611/611 official, 555/555 WPT).
  - Canary: 960/960 (492/492 official, 468/468 WPT).
  - `C-pass.txt` (inheritance/cascade change): 2,974/2,974.

### Batch 2, commit 1 - `inherit` passes the parent's COMPUTED font-relative length (§6.2.1)

- **Handed in from 6.C:** `margin-em-inherit-001` / `padding-em-inherit-001` (official and WPT
  twins, 4 served paths). AngleSharp replaces the `inherit` keyword with the parent's SPECIFIED
  value, so `#child { margin: inherit }` under `#parent { margin: 2em; font-size: 28px }` reached
  layout as the string `2em`, which the child's own 40px font scaled to 80px instead of the
  parent's computed 56px.
- **Fix (`Parser.Cascade.cs`):** after the cascade, a declaration whose computed value is a bare
  `em`/`ex` length is checked against the element's own explicit cascade
  (`ComputeExplicitStyle`, which still holds the `inherit` keyword). When the own winner is
  `inherit`, the keyword is stamped into the override channel, which 6.C's
  `ResolvePendingCssWideKeywords` resolves from the parent's computed value. A cheap gate keeps
  the second cascade pass to elements whose matching rules or style attribute actually spell
  `inherit`. No fork patch changed.
- **Moved in K: none** (these cases belong to 6.C, not to this lane).
- **Still fails, honestly:** all four served paths now put `#child` at the exact geometry the
  test states (`offsetLeft=238`, `offsetTop=174`, margin `56/84/28/112`) and the *official*
  renders are fully green with no red. The maintained WPT twins (`margin-em-inherit-001.xht`,
  `padding-em-inherit-001.xht`) still diff by **one 2-pixel row** (784 and 800 px): their
  reference fixes the green box at `height: 194px`, which needs a 110px line box for the inline
  100px image, i.e. a 10px font descent at 40px. Our serif metric gives a 108.48px line box
  (descent 8.48px), so the box is 1.5px short. Probe: a 40px text line measures 44.69px
  (ascent 36.21, descent 8.48). That is `line-height: normal` strut/font metrics, not cascade:
  **handed to 6.J/6.A** (see Handed in).
- **Checks:**
  - Build: `dotnet build Lite.sln -c Release` succeeded.
  - Units: full suite 736 passed, 0 failed, including the new
    `CascadeTests.Inherit_StylesheetMarginAndPaddingTakeParentComputedEm`.
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 0/37 official, 1/65 WPT (unchanged).
  - `K-pass.txt`: 1,166/1,166 (611/611 official, 555/555 WPT).
  - Canary: 960/960 (492/492 official, 468/468 WPT).
  - `C-pass.txt` (cascade change reaches every lane): 2,974/2,974 (1,401/1,401 official,
    1,573/1,573 WPT).

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
