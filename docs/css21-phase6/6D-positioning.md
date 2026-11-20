# 6.D Positioning, stacking and the viewport

Branch `css21/p6-d`, worktree `C:\Repos\Lite-p6-d`. Case list `Lite.Conformance/artifacts/lanes/D.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Positioned.cs
- Lite/Layout/Viewport.cs
- Lite/Layout/ElementScrollState.cs
- Lite/Drawer.cs: the stacking-context walk (`PaintChildrenSorted`, `CollectStackingItems`, `EstablishesStackingContext`, `PaintThroughClippers`) and the scrollbar painters

## Opens with (parked findings)

- The viewport bar's own gutter (`background-bg-pos-207`): Lite's page scrollbar overlays the viewport edge. Reserving the gutter changes the initial containing block of every tall page, so run the canary carefully.
- Vertical scrolling for `max-height`-clamped `auto` boxes is not tracked yet.
- `inline-static-position-001` and `vertical-align-top-bottom-001` render now and fail honestly.

## Coordination

- The 6.H and 6.I painters are called from the walk in `Drawer.cs`. Keep their call signatures stable.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
