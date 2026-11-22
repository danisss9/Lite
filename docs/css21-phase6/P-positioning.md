# Lane P: positioning and viewport (batch 3)

Merges 6.D ([6D-positioning.md](6D-positioning.md)). Branch `css21/b3-p`, worktree `C:\Repos\Lite\.claude\worktrees\b3-p`.
Case list `Lite.Conformance/artifacts/lanes/P.txt` (pass list `P-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Layout/BoxEngine.Positioned.cs
- Lite/Layout/Viewport.cs
- Lite/Layout/ElementScrollState.cs
- Lite/Drawer.cs: the stacking-context walk (`PaintChildrenSorted`, `CollectStackingItems`,
  `EstablishesStackingContext`, `PaintThroughClippers`) and the scrollbar painters

## Opens with (parked findings)

- Clusters: `positioning` (23), `abspos` (7), `box-display` (6), `visufx` (5), `overflow-applies-to`.
- The viewport scrollbar's own gutter (`background-bg-pos-207`). It changes the initial containing
  block of every tall page, so run the whole canary before and after.
- Vertical scrolling for `max-height`-clamped `auto` boxes.
- `containing-block-017/018`: a large red box is visible. The cause is abs-pos containing-block
  placement, at the inline fragments' padding-edge corners (6M → 6D).
- `outline-layout-002`, `content-177`: Appendix E paint order for outlines and stacked abs-pos boxes.
- `top/left/right/bottom/position-applies-to-005/006/015`: lane Tb owns the table-display variants (6E
  took them). Hand on any that are still on this list.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
