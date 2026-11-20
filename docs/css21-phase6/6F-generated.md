# 6.F Generated content, counters, quotes and lists

Branch `css21/p6-f`, worktree `C:\Repos\Lite-p6-f`. Case list `Lite.Conformance/artifacts/lanes/F.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Parser.GeneratedContent.cs
- Lite/Layout/CssCounterStyles.cs
- Lite/Drawer.Text.cs: the list-marker members only (`PaintListItem`, `GetMarkerText`, `DrawBulletMarker`, `IsInsideOrderedList`, `GetOrderedIndex`)

## Opens with (parked findings)

- Dynamic pseudo-element rules: `content-178`, `first-letter-selector-029` and `first-line-selector-017`. Rules that match only after a state change must still generate `::before`/`::after`/`::first-letter` (coordinate with 6.K, which owns the dynamic-rule cascade).
- Outside markers above bars do not paint (`list-style-position-003`).

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
- `Lite.Tests/GeneratedContentTests.cs` is new (my lane's tests).

## Progress

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

### Editor's note

The lane list read before this batch was 0/97 official and 0/103 WPT by definition (every entry is
a failure), so "moved" means "passes now".
