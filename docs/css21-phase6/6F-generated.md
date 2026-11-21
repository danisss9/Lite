# 6.F Generated content, counters, quotes and lists

Branch `css21/p6-f`, worktree `C:\Repos\Lite-p6-f`. Case list `Lite.Conformance/artifacts/lanes/F.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Parser.GeneratedContent.cs
- Lite/Layout/CssCounterStyles.cs
- Lite/Drawer.Text.cs: the list-marker members only (`PaintListItem`, `GetMarkerText`, `DrawBulletMarker`, `IsInsideOrderedList`, `GetOrderedIndex`)

## Opens with (parked findings)

- Dynamic pseudo-element rules: `content-178`, `first-letter-selector-029` and `first-line-selector-017`. Rules that match only after a state change must still generate `::before`/`::after`/`::first-letter` (coordinate with 6.K, which owns the dynamic-rule cascade).
- ~~Outside markers above bars do not paint (`list-style-position-003`).~~ Fixed in batch 1 by
  the generated marker line (§12.5.1); the case passes (official + WPT).

## Coordination

- `::first-line` layout belongs to 6.A. The `::first-letter` box and its generation are yours.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

- `Lite/Parser.cs` (2 lines): calls `CreateListMarkerChild` after the pseudo children and before
  `::first-letter`, with the originating `IElement` (needed for the LI ordinal). No other change.
- `Lite/Extensions/DrawCommandExtensions.cs` (`InsideMarkerIndent`): returns 0 when the item has
  a `#pseudo-marker` child, because the real marker box now advances the first line itself.
- `Lite/Extensions/DrawCommandExtensions.cs` (`GetVerticalAlign`): a synthesized `#text` child of
  a table cell without its own value returns baseline (§17.5.4; the cell's `middle` is a
  row-alignment, not an inline value). Fixes the reference half of `first-letter-selector-007`.
- `Lite/Drawer.cs` (`PaintInlineContent`, 1 line): a replaced `content: url(...)` pseudo box is
  forwarded to `PaintNode` like an IMG, instead of the empty-text inline path.
- `Lite/Drawer.cs` (`PaintNodeContentBody`, `case "A"`): an anchor with `display: list-item`
  takes the list-item paint path (the tag dispatch otherwise bypassed the marker).
- `Lite/Scripting/Dom/JsElement.cs`: `OnAttributeChanged` re-evaluates a generated box whose
  'content' uses `attr()`; `SetCharacterData` re-projects the parent and rebuilds the containing
  blocks' `::first-letter` split (§12.2, §5.12.2). Both are the narrow generated-content half of
  the dynamic work parked to 6.K — if 6.K lands a general regeneration, these hooks are redundant
  but harmless (they are idempotent).
- `Lite.Tests/GeneratedContentTests.cs` is new (my lane's tests).

## Progress

**Batch 2 reading:** lane list 0/58 + 0/72 → **9/58 + 10/72 = 19 of 130**, 0 losses; units 741;
css21 51/1; wpt 86/2; canary 960/960. Three commits.

**Batch 1 reading:** lane list 0/97 + 0/103 → **36/97 + 27/103 = 63 of 200**, 0 losses; units 663;
css21 51/1; wpt 86/2; canary 300/300. Eight commits. The remaining 137 are grouped at the end of
this section.

### A hidden element's pseudo counter does not run; a repeated name resets one instance (§12.4)

- **Moved (4):** `counter-increment-display-004` and `counters-root-000` (official + WPT).
  `PseudoCounters` applied a pseudo's `counter-increment` for every element with a content pseudo
  rule, including a `display: none` one, so the hidden div skipped a number. `ApplyCounterProperties`
  treated each repeated name in one declaration as a fresh nested instance, so
  `html { counter-reset: c 0 c 4 c 0 }` counted 0.4.0 instead of 4 — §12.4 orders repeated names on
  the one instance the declaration creates.
- **Still fails:** `counter-increment-000/001/002` and `counter-reset-000/001/002` — these pages run
  `setTimeout('run()', 0)` and the engine's `setTimeout` accepts only a function
  (`[JS Error] TypeError: not a function`), so the DOM mutation never happens. Verified with a
  diagnostic copy using `setTimeout(run, 0)`: the counters then renumber correctly. Handed to 6.K
  (the inserted element also needs its own ::before, which the dynamic generation covers).
- **Checks:** build Lite.sln Release; units 737; css21 51/1; wpt 86/2; F.txt official 0→2/58,
  WPT 0→2/72; F-pass 885 + 904, J-pass 264 + 255, canary 492 + 468, 0 losses.

### attr() text follows an attribute change; whitespace between generated boxes (§12.2, §16.6.1)

- **Moved (4):** `before-after-dynamic-attr-001` and `before-after-selector-001` (official + WPT).
  attr(X) returns the attribute's value for the subject, so an attribute set after load must
  re-evaluate the generated box's text instead of leaving the parse-time value. Separately, an
  element whose own text is whitespace-only between two generated boxes keeps one collapsed space
  (§16.6.1: only line start/end spaces go), so `<div> </div>` with `:before`+`:after` renders
  "Filler Text Filler Text", not "Filler TextFiller Text".
- **Still fails:** `before-after-dynamic-restyle-001` — the class change only then makes `.cl:after`
  match, so a box must be created; the refresh rewrites existing text only. 6.K's dynamic rule
  generation.
- **Checks:** units 738; css21 51/1; wpt 86/2; F.txt official 2→4/58, WPT 2→4/72; F-pass 885 + 904,
  J-pass 264 + 255, canary 492 + 468, 0 losses.

### The ::first-letter split follows DOM text changes and inherits from its host pseudo (§5.12.2)

- **Moved (11):** `first-letter-nested-002/003/004/005` (official + WPT),
  `before-first-letter-selector-001` (official + WPT) and WPT `first-letter-quote-004`. The split is
  a function of the block's first-line text: re-projection kept the block's box, so a parse-time
  split stayed frozen (nested-002 kept the cleared text's letter) and text set/appended after layout
  never grew one (nested-003/004/005, quote-004). A stale split is detected by comparing its
  letter+tail with the host's current document text and is rebuilt. The synthesized letter and tail
  also now copy the host's inherited declarations, so `::before { color: green }` reaches them
  (before-first-letter-selector-001 painted them black).
- **WPT `first-letter-quote-004` passes for the wrong reason:** both it and its reference path
  (the still-unstyled `first-letter-quote-002` document) render no split because the first text is
  a lone `"`. It must be re-checked when the punctuation split lands (6.K).
- **Punctuation split is deferred to 6.K:** `first-letter-quote-002/005` need the first letter to
  start at punctuation in an earlier box (`span:before`'s open-quote, a `"` text node). The code is
  written but it makes `first-letter-dynamic-001`'s REFERENCE render green while its subject cannot
  (the `:first-letter` rule is added to a `<style>` after parse, and pseudo rules are collected only
  at parse start), which is a pass-list loss. Both halves must land together with 6.K's dynamic
  rule collection. `first-letter-selector-028` is the same selector-list neutralization 6.K already
  has for 011–014.
- **Checks:** units 741; css21 51/1; wpt 86/2; F.txt official 4→9/58, WPT 4→10/72;
  F-pass 885 + 904, J-pass 264 + 255, canary 492 + 468, 0 losses.

### Generated pseudo boxes: `vertical-align` no longer leaks from the host (§10.8.1)

- **Moved (10):** `content-037`, `content-046`, `content-052`, `content-053`, `content-065`,
  `content-081`, `content-103`, `content-132`, `content-149` (official; their twins are the WPT
  `generated-content` references) and `first-letter-selector-007`. A `td:before` was middle-aligned
  because the pseudo borrowed the cell's style object, so the UA `vertical-align: middle` read back
  as the pseudo's own and the line box grew by 1.4px against the reference cell.
- **Still fails:** the same files' WPT entries (except `content-037`) were never in this lane's
  list; the remaining `content-*` cases have other causes (`attr()` case folding, counters,
  `content: url()`).
- **Checks:** units 649 passed; css21 gate 51 passed / 1 expected-fail; wpt gate 86 / 2
  expected-fail; lane list official 0→10, WPT 0 (0 losses); canary 300/300.

### List markers are generated inline boxes (§12.5.1)

- **Moved (44 of the 200 lane cases now pass, from 10):** `list-style-position-003/005/008/009`,
  `list-style-position-020`-family still open, `list-style-position-applies-to-001/002/003/004/007/013`,
  `list-style-applies-to-012/014`, `list-style-type-applies-to-012/014`,
  `list-style-image-applies-to-012/014`, `list-style-position-023/024`, `list-style-position-003`
  also fixed the parked "outside markers above bars" case. A marker is now a real
  `#pseudo-marker` child: an 'inside' marker is the first inline box (so it gets its own line
  above a block child and the item's text wraps after it — the hanging indent), and an empty
  item gets one zero-width marker line so an outside marker lands where the reference puts it.
  The painted disc is capped so an 85px item keeps a bullet at the marker's size.
- **Still fails:** `list-style-position-020/022` (hanging indent: the item's text is a direct
  child, not a #text child, in the span form), `list-style-applies-to-009/017` and the
  `-image/-type/-position-009/017` family (the A tag's painter path and the float-sibling line
  band), the round-dot family below, `list-style-type-017`, `list-style-020`, `c566`.
- **Checks:** units 652 passed; css21 51/1; wpt 86/2; lane list official 10→27, WPT 0→17 (44
  total, 0 losses); canary 300/300.

### An anchor with `display: list-item` is a list item box

- **Moved (8):** `list-style-applies-to-009`, `list-style-image-applies-to-009`,
  `list-style-type-applies-to-009`, `list-style-position-applies-to-009` (official + WPT). The
  `A` tag's dedicated paint path has no marker, so these empty `a { display: list-item }` items
  rendered nothing. The list-item branch now owns the tag; its hit region keeps the link target.
- **Still fails:** `list-style-applies-to-017` and `list-style-image/type/position-…-017`: the
  list item beside a block-level float keeps its full-width content box (no line band shift), so
  the marker paints off the left edge — a float/block-flow issue (handed to 6.B).
- **Checks:** units 663; css21 51/1; wpt 86/2; lane list 55→63 (official 36, WPT 27), 0 losses;
  canary 300/300.

### attr() case folding and inherited presentational hints (§12.2, §6.2.1)

- **Moved:** `content-attr-case-001` (official + WPT; `attr(Title)` now finds HTML's `title`) and
  `content-145` (official + WPT; the body's `text="green"` hint now reaches the `body:before`,
  which shares the Style object but not the host's hint override). The hint propagation covers
  only inherited hint properties.
- **Checks:** units 662; css21 51/1; wpt 86/2; lane list 51→55 (official 32, WPT 23), 0 losses;
  canary 300/300.

### 'inherit' on a generated box takes the host's computed value (§6.2.1)

- **Moved:** `first-letter-inherit-001` (official + WPT; `float: inherit` on ::first-letter now
  floats the letter). Pseudo declarations are copied into StyleOverrides, so the literal
  `inherit` reached the property readers as an unknown value; `white-space: inherit` also
  collapsed a 'pre' item's "\A" to a space (content-172's .pre row now renders the step pattern).
- **Still fails:** `content-172` (the .normal row's cell shrinks to its longest `\A` line — a
  table max-content measure of a pseudo child, 6.E-adjacent), `content-173/174/175`,
  `content-white-space-002`.
- **Checks:** units 660; css21 51/1; wpt 86/2; lane list 49→51 (official 30, WPT 21), 0 losses;
  canary 300/300.

### `content: url(...)` is a replaced inline box (§12.2)

- **Moved:** `content-004` (official + WPT). The pseudo box got the fetched bitmap and laid out
  correctly, but the inline paint path only forwarded IMG and the atomic inline-boxes to
  `PaintNode`, so a `#pseudo-before` with an image painted its (empty) text and dropped the
  bitmap.
- **Still fails:** the `before-after-*` family that combines `content: url(...)` with display
  types, floats, positioning or table parts — separate causes below.
- **Checks:** units 659; css21 51/1; wpt 86/2; lane list 47→49 (official 29, WPT 20), 0 losses;
  canary 300/300.

### Cell text and the ::first-letter tail keep the baseline (§17.5.4)

- **Moved:** `first-letter-selector-007` (WPT; the official twin already passed after the first
  commit). A table cell's anonymous `#text` borrowed the cell's `vertical-align: middle` as an
  inline value, so mixed cell content (`<span>F</span>iller Text`) put the tail 2px below the
  span; that is the reference page of this test, while the test's split `::first-letter` tail had
  been given the initial baseline, so the two no longer matched. Synthesized `#text` children of
  a table cell now start from baseline unless they carry an own value; the first-letter tail is
  also explicitly baseline.
- **Checks:** units 658; css21 51/1; wpt 86/2; lane list 46→47 (official 28, WPT 19), 0 losses;
  canary 300/300.

### The implicit 'list-item' counter (§12.4)

- **Moved:** `counter-reset-increment-002` (official + WPT). Every list element now implicitly
  resets `list-item` (0, or `start`-1 from HTML's hint) and every list item implicitly increments
  it; the marker's ordinal and the inside-marker text read that counter, so
  `ol { counter-reset: list-item -3 }` / `start="-3"` render -3, -2, ... instead of 1, 2, ...
  (`list-style-type-017` now renders the right numbers; its remaining failure is the official
  self-describing check below).
- **Still fails:** `list-style-type-017` (official only) reports "8 ink bands" from the
  self-describing check although its twin reftest passes — a 6.M pass-condition issue.
- **Checks:** units 656; css21 51/1; wpt 86/2; lane list 44→46 (official 28, WPT 18), 0 losses;
  canary 300/300.

### Round-dot pass conditions (handed to 6.M)

`list-style-type-001`, `list-style-applies-to-005/006`, `list-style-type-applies-to-005/006` (and
their official twins) fail the `round-dot` machine check because a 16px disc paints a 5x5 ink
blob whose bbox edges are all inked, so the check's rectangle heuristic calls it square. The
render is a filled circle 7-8px across including antialiasing (browsers draw ~4-5px here). A larger
disc would fix the check but then `list-style-position-003/008/009` (85px) fail the `<=24px`
bullet threshold — the two checks have no common marker size. 6.M should relax `round-dot` to
test the shape's corner emptiness rather than every bbox edge.

### What is left, and where it goes

- **6.K (dynamic):** `first-letter-nested-003/004/005`, `first-letter-dynamic-002/003a/003b`,
  `before-after-dynamic-attr-001`, `before-after-dynamic-restyle-001`, the `content-178` parked
  case, and the counter-after-mutation family (`counter-increment-000/001/002`,
  `counter-reset-000/001/002`, `counter-increment-display-004`, `counters-root-000`). Rules and
  text that appear after load must regenerate pseudo boxes and re-run the counter pass.
- **6.K (cascade):** `first-letter-selector-011/012/013/014`, `before-after-001`,
  `before-first-letter-selector-001` — a selector list mixing a pseudo-element and a normal part.
- **6.E:** `content-172` (table max-content of a pseudo child).
- **6.B:** the float-sibling `-017` list tests.
- **6.X:** `content-159` (XHTML `<style>` quotes), `first-letter-selector-000` (CDATA).
- **6.M:** the machine checks above (`round-dot`, `painted-only`, `content-008`'s needle,
  `hanging-indent`, `list-style-type-017`'s band count, `content-counters-003`).
- **Not diagnosed this batch:** `before-after-display-types-001`, `before-after-floated-001`,
  `before-after-positioned-001/002/003`, `before-after-table-parts-001`,
  `before-inheritable-002`/`after-inheritable-002`, `before-after-011`,
  `content-inherit-002`, `content-173/174/175/177`, `content-white-space-002`,
  `content-counter-004/009`, `content-counters-009/017/018`, `counter-increment-054`,
  `list-style-020`, `c566-list-stl`, `content-type-000/001`, `content-height-005`,
  `c541-word-sp`, `c542-letter-sp`.
  Most need the generated pseudo's display type (block/table-cell/float/position) or a font
  question; I left them rather than guess.

### Batch 2: what is left, and where it goes

Reading: 9/58 official + 10/72 WPT still fail 111 cases. Grouped by cause, from this batch's
diagnosis:

- **6.K (dynamic rules and CharacterData), 20+:** `before-after-dynamic-restyle-001` (a class
  change must create `.cl:after`), `first-letter-dynamic-001/002/003a/003b` (a `<style>` added after
  parse must feed `Current.PseudoElementRules` and re-cascade), `first-letter-quote-002/004/005` +
  `first-letter-punctuation-184` (the punctuation split — see above), the counter-after-mutation
  family (`counter-increment-000/001/002`, `counter-reset-000/001/002`, which also need
  `setTimeout('run()', 0)` string handlers), `content-178`, `first-line-selector-017`,
  `first-letter-selector-029`, `first-letter-selector-011/012/013/014` and `-028`
  (selector-list neutralization), `before-after-001`, `before-inheritable-002`/`after-inheritable-002`.
- **6.D (paint order):** `content-177` — two absolutely positioned spans overlap; the later red
  `ED` paints over the green control, while the reference paints them in the other order.
- **6.A/6.H (inline background overflow):** `content-174` — a `white-space: nowrap` `:before` box
  wider than its 5em parent paints its green background only for the first ~5em; the reference
  covers the whole overflowing run.
- **6.E:** `content-172` (table max-content of a pseudo child).
- **6.B:** the float-sibling `-017` list family (8 WPT + 4 official twins).
- **6.M (machine checks):** `round-dot` (`list-style-type-001`, `list-style-applies-to-005/006`,
  `list-style-type-applies-to-005/006`), `painted-only` (`quotes-applies-to-005/006` — the four
  painted quote glyphs tokenize as one word; `content-142`), `hanging-indent`
  (`list-style-position-020/022` — the render shows both lines, the check counts one band),
  `content-008`'s needle, `list-style-type-017`'s band count, `content-counters-003`.
  `first-letter-punctuation-184`: the suite's Unicode 5.x calls U+166D CANADIAN SYLLABICS CHI SIGN
  `Po`; .NET 8's `CharUnicodeInfo` calls it `So`, so `FirstLetterLength` cannot include it by
  category. Needs a reviewed defect or an explicit table.
- **6.X:** `content-159` (XHTML `<style>` quotes), `first-letter-selector-000` (CDATA).
- **Still undiagnosed:** `before-after-display-types-001`, `before-after-floated-001`,
  `before-after-positioned-001/002/003`, `before-after-table-parts-001`, `before-after-011`,
  `content-inherit-002`, `content-173/175`, `content-white-space-002`, `content-counter-004/009`,
  `content-counters-009/017/018`, `counter-increment-054`, `list-style-020`, `c566-list-stl`,
  `content-type-000/001`, `content-height-005`, `c542-letter-sp`, `first-letter-abspos` (the split
  exists and its color override is green in the parse path, but the render paints it black — likely
  a paint-path choice for the split letter inside a block whose first child is absolute).
  `content-173` is a content-string parsing bug (the `\A`-laden multi-line single-quoted strings
  with `\'` come back truncated; only the tail of the :before cat and none of the :after cat paint).

### Editor's note

The lane list read before this batch was 0/97 official and 0/103 WPT by definition (every entry is
a failure), so "moved" means "passes now".
