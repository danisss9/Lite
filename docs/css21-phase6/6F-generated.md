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

_(None yet.)_

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

### Editor's note

The lane list read before this batch was 0/97 official and 0/103 WPT by definition (every entry is
a failure), so "moved" means "passes now".
