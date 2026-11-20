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
- Heads-up from 6.E: my commit b4547ba0 (table layer background paint areas) edits your
  `Drawer.Backgrounds.cs` PaintColumnBackgrounds — it now draws the per-cell background rects
  `TableEngine.TryGetCellAreaBackgrounds` records for the separated model (§17.5.1) and clips
  column background images to them; the band painting stays as fallback. Also §14.2 background
  colours now paint the border box in `Drawer.cs` PaintBlock/PaintBlockDecorations, so
  transparent borders show the background through — re-check your background-position cases
  that have transparent borders after the merge.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
