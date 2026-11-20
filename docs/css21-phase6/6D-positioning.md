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

- `clipping-006`, `clipping-007` (official+WPT, 4): `overflow: hidden` on a `position: fixed` ancestor clips a `position: fixed` descendant whose containing block is the viewport (`PaintThroughClippers` / containing-block clipping in `Drawer.cs`) (from 6.I).
- `outline-layout-002` (official+WPT, 2): `span1`'s 50px solid white outline paints before `span2`'s red text instead of after inline siblings in the stacking context (CSS 2.1 Appendix E §E.2 step 10) (from 6.I).

- `css/CSS2/box-display/containing-block-017.xht`, `containing-block-018.xht` — the WPT manual copy now runs the
  fail-closed selftest route and fails on a large visible red box (the page's stated condition is "no red visible");
  the abspos containing-block placement is wrong. Render evidence: `artifacts/cb17.png` in the 6.M worktree. From 6.M.

## Edits outside owned files

- 682105b0: `Lite/Layout/BoxEngine.Block.cs` (6.C's area) — the natural-extent fallback now skips
  fixed/abspos children, matching `ScrollableExtent`. 5 lines + comment, own commit.
- ffb6318b: `Lite/Extensions/DrawCommandExtensions.cs` (shared style-getter layer) — `GetOffset`
  resolves the 'inherit' keyword by walking the ancestors. ~40 lines incl. the helper, own commit.

## Progress

### 20 November 2026 — commits eaa168a9, 714c24e6 (wave 1)

The session opened with an uncommitted in-progress fix left in the worktree (stacking model +
over-constrained relpos); it was split into the two concerns below, tested and landed.

**eaa168a9 — positioned `z-index: auto` boxes are stacking items, not contexts.** §9.9.1 + Appendix
E step 8. `EstablishesStackingContext` now requires an integer 'z-index' for relative/absolute
boxes; `IsStackingItem` covers the auto-z items; `CollectStackingItems` descends through them so
their positioned descendants join the enclosing context; `PaintThroughClippers` replays the
ancestors' §9.4.3 offsets around those descendants; synthesized nodes no longer read a shared host
style as their own opacity/transform.
- Moved: `z-index-abspos-001.htm` + `.xht`.
- Still failing here: `z-index-abspos-009` (216 red px — a smaller paint-order remnant),
  `z-index-020`/`zorder/z-index-020`, `fixed-pos-stacking-001`, `zindex-affects-block-in-inline`.
- Units 649/0 (new `StackingContextTests`, the z-index test fails on the pre-fix code); css21
  51+1 expected-fail; wpt 86+2 expected-fail; lane list +2, 0 losses; canary 300/300.

**714c24e6 — over-constrained relative offsets follow the containing block's 'direction'.**
§9.4.3 rule 4 + §10.1 item 2. `RelativeOffset` applied 'left' unconditionally when both 'left' and
'right' are set; it now picks by the direction of the nearest block container ancestor.
- Moved: `position-relative-010`, `position-relative-038`, `relpos-calcs-006` (each official +
  WPT twin).
- Still failing here: `position-relative-032/035` (other relpos sub-cases), the
  `*-applies-to-005/006/015` families.
- Units 651/0 (new `RelativePositioningTests`); css21 51+1; wpt 86+2; lane list +6, 0 losses;
  canary 300/300.

### 20 November 2026 — commit 682105b0 (wave 1, continued)

**682105b0 — a scroll box's natural extent ignores abspos children with an outside containing
block.** §11.1.1's clip exception runs both ways. The widest-child fallback for `naturalW` in
`BoxEngine.Block.cs` (not an owned file — see "Edits outside owned files") read a scroll box's
abspos child's margin box when `ScrollableExtent` returned null, and on every re-layout picked up
the previous pass's geometry: the whole abspos-overflow family (scroll + auto) failed on thumb
length / phantom bars.
- Moved: `abspos-overflow-001/002/003/007/008/009` ×2 corpora (12 cases).
- Units 652/0 (new `ScrollGutterTests.ScrollExtent_IgnoresAbsposChildWithAnOutsideContainingBlock`,
  fails pre-fix); css21 51+1; wpt 86+2; lane list +12, 0 losses; canary 300/300.

All runs on one shard, one conformance process at a time. The parked findings (viewport gutter,
`max-height` auto scroll, `inline-static-position-001`, `vertical-align-top-bottom-001`) are all
still open.

### 20 November 2026 — commit 1b410c2d (wave 1, continued)

**1b410c2d — a fixed box is the stacking-context root for its own subtree.** The walk stops at
fixed boxes (they never join an ancestor's z lists) but PaintFixedNodes paints them with
PaintNodeInner, whose child pass skips stacking items — so a positioned descendant of a fixed box
was laid out (its §10.1 containing block resolves fine) and painted by nobody.
- Moved: `containing-block-010/015/021/022` ×2 corpora + `fixed-pos-stacking-001.xht` (9 cases).
- Still failing here: `containing-block-017/018/031` (inline-fragment padding-edge corners, big
  red-pixel misses), `left/right-offset-position-fixed-001`.
- Units 653/0 (new `StackingContextTests.FixedAncestor_IsTheContextRootForItsPositionedDescendants`);
  css21 51+1; wpt 86+2; lane list +9, 0 losses; canary 300/300.

### 20 November 2026 — commit ffb6318b (wave 1, continued)

**ffb6318b — `inherit` offset properties resolve against the parent's value.** §6.2. The cascade
kept the raw keyword and `GetOffset` (shared style-getter layer — see "Edits outside owned files")
mapped it to 'auto': a relpos box with `top: inherit` inherited no shift and an abs-pos box fell
back to its static position. `GetOffset` now walks the ancestors for the first concrete value and
parses it against the inheritor's own basis, like the border-style getter already did.
- Moved: `dynamic-top-change-001/004` ×2 corpora (4 cases).
- Still failing here: `dynamic-top-change-005/005a/005b` (diagnostics TBD — 5000-red-pixel
  variants with the change applied to different properties), `z-index-abspos-009`.
- Units 654/0 (new `RelativePositioningTests.InheritedTop_TakesTheParentSComputedValue`, served-page
  pipeline because srcdoc parsing resolves the keyword; fails pre-fix); css21 51+1; wpt 86+2;
  lane list +4, 0 losses; canary 300/300.

### 20 November 2026 — Fix round (batch-1 loss)

**`clipping-009` (official + WPT), lost by eaa168a9.** §11.1.2 / Appendix E step 8. Since
eaa168a9 a positioned 'z-index: auto' box is a stacking item, not a context, so its abs-pos
children join the enclosing context and paint from its root through `PaintThroughClippers`. That
chain replayed the overflow clips and relative offsets of the boxes in between, but not an
abs-pos ancestor's `clip: rect()`, which §11.1.2 applies to the box's descendants too. In
`clipping-009` the green box (abs-pos, z auto, clipped) no longer painted its red abs-pos child
itself, and the child painted unclipped. `PaintThroughClippers` now also replays the 'clip'
rectangle of every absolutely or fixed positioned ancestor between the root and the item, before
that ancestor's own overflow clip (the order `PaintNode` uses).
- Moved: `clipping-009` ×2 corpora pass again. No other change on the lane list.
- Gains kept: all 33 lane gains hold (D.txt 33 pass / 136 fail, same as after ffb6318b). No gain
  had to go.
- Units 655/0 (new `StackingContextTests.AutoZIndexClippedAncestor_ClipsItsPositionedDescendants`,
  fails pre-fix); css21 51+1; wpt 86+2; `fix-losses.txt` 2/2; `D-pass.txt` 1155/1155;
  `canary.txt` 960/960.
