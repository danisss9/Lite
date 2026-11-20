# 6.H Backgrounds

Branch `css21/p6-h`, worktree `C:\Repos\Lite-p6-h`. Case list `Lite.Conformance/artifacts/lanes/H.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Drawer.Backgrounds.cs
- Lite/Network/ResourceLoader.cs
- Lite/Media/*

## Opens with (parked findings)

- A `data:` URL in `background` is cut at its `;` (`data:image/png;base64,...` reads as `data:image/png`). The raw-background rescue lives in `Parser.Cascade.cs` (6.K): coordinate.
- `background-position-091/092`: `ex` must be the font's measured x-height, not 0.5em. 6.J provides the metric; re-check these after 6.J lands.
- `background-intrinsic` (SVG intrinsic sizes), `background-alpha`, `background-table`, `background-attachment`.
- `background-animated-001`: the animated-image work landed (`Lite/Media/AnimatedImages.cs`). Confirm its route with 6.M.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

- `css/CSS2/backgrounds/background-attachment-006.xht`, `background-attachment-007.xht` — the WPT manual copy now
  runs the fail-closed selftest route and fails on visible red: 006's `.outer` red top border should be covered by
  the `-10px`-margin inner box, and 007's red background should be covered by the tiled image. Both are layout/paint
  coverage of the background layer, not the judge. Render evidence: `artifacts/ba6.png` in the 6.M worktree. From 6.M.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
