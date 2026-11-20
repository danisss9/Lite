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

_(None yet.)_
