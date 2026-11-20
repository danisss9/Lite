# 6.E Tables

Branch `css21/p6-e`, worktree `C:\Repos\Lite-p6-e`. Case list `Lite.Conformance/artifacts/lanes/E.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/TableEngine.cs
- Lite/Layout/BoxEngine.Normalize.cs (anonymous table boxes and the block-in-inline split)

## Opens with (parked findings)

- `table-anonymous-objects` 134 (reused loose cells keep their previous-pass row band after re-projection), 203 and 204 (cell-metrics fringe).
- Row heights run one line-height metric high when content is taller than a specified table height (`table-height-algorithm-004/009/010`); a `vertical-align: top` cell spans 4px (`012-014`).
- `column-visibility-002` paints red.
- `border-conflict-element-029` (RTL column order) is shared with 6.G.
- The content-box percentage width of `display: table` (with 6.C).

## Coordination

- The block-in-inline split in `BoxEngine.Normalize.cs` is shared territory with 6.A. Tell 6.A before changing it.

## Handed in

- `border-applies-to-004`, `border-color-applies-to-004`, `border-{left,right}-applies-to-004`, `border-{left,right}-color-applies-to-004`, `border-{left,right}-width-applies-to-004` (official+WPT, 16): in `border-collapse: collapse`, a `table-row`'s left/right border is resolved onto the interior boundary between adjacent cells in `TableEngine` instead of only the row's outer left/right edges (from 6.I).
- `outline-applies-to-016`, `outline-applies-to-017` (WPT, 2): replaced `<img>` with `display: table-column-group` / `table-column` produces no laid-out box, so nothing paints (from 6.I).

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
