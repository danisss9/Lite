# 6.D Positioning, stacking and the viewport

Branch `css21/p6-d`, worktree `C:\Repos\Lite-p6-d`. Case list `Lite.Conformance/artifacts/lanes/D.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Positioned.cs
- Lite/Layout/Viewport.cs
- Lite/Layout/ElementScrollState.cs
- Lite/Drawer.cs: the stacking-context walk (`PaintChildrenSorted`, `CollectStackingItems`, `EstablishesStackingContext`, `PaintThroughClippers`) and the scrollbar painters

## Opens with (parked findings)

- ~~The viewport bar's own gutter (`background-bg-pos-207`)~~ — FIXED in 3de075c2.
- ~~Vertical scrolling for `max-height`-clamped `auto` boxes~~ — FIXED in 7a30ea2d.
- ~~`inline-static-position-001`~~ — FIXED in 965dbdbb (the static positions were right; the
  CSSOM offset origin was the red). ~~`vertical-align-top-bottom-001`~~ — RESOLVED (passes).

## Coordination

- The 6.H and 6.I painters are called from the walk in `Drawer.cs`. Keep their call signatures stable.

## Handed in

- `clipping-006`, `clipping-007` (official+WPT, 4): `overflow: hidden` on a `position: fixed` ancestor clips a `position: fixed` descendant whose containing block is the viewport (`PaintThroughClippers` / containing-block clipping in `Drawer.cs`) (from 6.I). — **clipping-006 FIXED in 09a4aea1 (the fixed-descendant paint + §11.1.2 clip replay); clipping-007 already passed.**
- `outline-layout-002` (official+WPT, 2): `span1`'s 50px solid white outline paints before `span2`'s red text instead of after inline siblings in the stacking context (CSS 2.1 Appendix E §E.2 step 10) (from 6.I). — **still open** (not reached this batch).

- `css/CSS2/box-display/containing-block-017.xht`, `containing-block-018.xht` — the WPT manual copy now runs the
  fail-closed selftest route and fails on a large visible red box (the page's stated condition is "no red visible");
  the abspos containing-block placement is wrong. Render evidence: `artifacts/cb17.png` in the 6.M worktree. From 6.M. **Still open** (inline-fragment padding-edge corners).

**Handed to other lanes:**

- To 6.B (then 6.C/6.J): the `ch` unit measures 0.5em (`CssUnits.cs` line ~87) where CSS Values §6.2 asks for the '0' glyph advance (1em in Ahem, ~0.6em in the suite's monospace). A working measured fix (TextMeasure.ZeroAdvanceRatio + CssUnits wiring, both files' owners see this note) was **reverted here**: it makes `float-nowrap-3/4/8/9` and `table-anonymous-objects-214` fail — the CORRECT width exposes a float-interaction bug (a right-floated box inside nowrap text paints over the line instead of shortening it: the test paints the float mid-line, the reference at the span start). Fix the float behavior first, then land the measurement; my probe harness reports (`probe-ch.json` in this worktree's artifacts) show exactly which cases flip.

## Edits outside owned files

- 682105b0: `Lite/Layout/BoxEngine.Block.cs` (6.C's area) — the natural-extent fallback now skips
  fixed/abspos children, matching `ScrollableExtent`. 5 lines + comment, own commit.
- ffb6318b: `Lite/Extensions/DrawCommandExtensions.cs` (shared style-getter layer) — `GetOffset`
  resolves the 'inherit' keyword by walking the ancestors. ~40 lines incl. the helper, own commit.
- 591e482d: `Lite/Layout/TableEngine.cs` (6.E's area) — `IsColumn`/`IsColumnGroup`/`IsCaption` no
  longer match boxes §9.7 blockifies (abs/fixed/floated), and `LayoutTable` records the table's
  content-edge static position for its abs/fixed children (the `RecordTableStaticPositions` call,
  one line). ~16 lines incl. the helper, one concern-commit with the owned core.
- 965dbdbb: `Lite/Scripting/Dom/JsElement.cs` (6.C's area — same file as its a6f13280 precedent)
  — `offsetParent` added and `offsetLeft`/`offsetTop` re-origined per CSSOM View §10.
- d1ebd03c: `Lite/Scripting/JsEngine.cs` (6.C's scripting area) — `ResolveNamedWindowProperty`
  falls back to the DOM document when the rendering tree dropped the element (the block-in-inline
  hoist removes an inline whose only child was a block).

## Progress

### 21 November 2026 — commits 3de075c2, 7a30ea2d (batch 2)

**3de075c2 — the page scrollbars' gutters come out of the initial containing block.** The
parked viewport-gutter finding. The page's classic bars painted OVER the viewport edge; the
gutter (BarWidth+BarMargin per axis) is now subtracted from the layout viewport when the page
extends past the fold and the root's overflow is not hidden — the same reservation
`Scrollport` already makes for element scroll boxes (§11.1.1 + §10.1). Whether the page
scrolls is only known after a layout, so the decision settles on the second pass (a page that
now fits re-lays at the full window — the first pass's answer — and cannot oscillate). The
fixed-background positioning area (§14.2.1) follows the layout viewport actually used.
- Moved: `background-bg-pos-207` official + WPT (from 6.H's hand-in in the parked findings).
- The DDG homepage unit expectation followed (800×600: the page is taller than the fold, so the
  search input lays out 465.6px, was 470.4 at the unreserved width; 1280×800 fits — 600 stands).
- Units 740/0; css21 51+1; wpt 86+2; lane list +2, 0 losses; canary 960/960; A-pass 1405/1405;
  B-pass 494/494; C-pass 2974/2974; D-pass 1203/1203; E-pass 1777/1777; H-pass 1274/1274;
  I-pass 2475/2475 — the whole seven-list sweep the parked finding called for.

**7a30ea2d — a max-height-clamped auto scroll box tracks its vertical extent.** The parked
max-height finding. The auto-height branch tracked only the width axis (the box grows to fit);
the §10.7 clamp runs later and never re-checked the vertical. The vertical state is now
computed against the clamped height through the same Scrollport the explicit-height branch
uses. BoxEngine.Block.cs is 6.C's file (see "Edits outside owned files").
- Moves 0 on the lane list — the tracked state feeds element scrollbar painting and scroll
  offsets, whose judged cases land on later baselines.
- Units 741/0 (`MaxHeightClampedAutoBox_TracksVerticalScrollExtent` fails pre-fix); css21 51+1;
  wpt 86+2; lane list unchanged, 0 losses; canary 960/960; D-pass 1203/1203; C-pass 2974/2974.

**Parked findings after this batch:** the viewport gutter — FIXED (3de075c2); max-height auto
scroll — FIXED (7a30ea2d); `inline-static-position-001` — FIXED (965dbdbb, the static-position
plumbing was right, the offset measurement was the red); `vertical-align-top-bottom-001` —
RESOLVED (it passes on A-pass + canary). Still open from "Handed in": `outline-layout-002`
(Appendix E step 10 outline ordering, on 6.I's list, 40 red px) and `containing-block-017/018`
(inline-fragment padding-edge corners, still on D.txt). The `ch` unit measurement is handed to
6.B/6.C/6.J (see the 21 November 965dbdbb entry).

### 21 November 2026 — commits 965dbdbb, d1ebd03c (batch 2)

**965dbdbb — offsetParent and the offsetLeft/offsetTop origin follow CSSOM View.** The getters
returned the absolute page coordinate: the relpos div of inline-static-position-001 (body margin
8 + 1px border) made the check-layout read offsetLeft 59 (page absolute) where 50 (the parent's
padding edge) is asserted, and the adjacent-to-relpos-inline cases crashed on the MISSING
offsetParent property. `offsetParent` (nearest non-static ancestor, positioned inline included;
null for root/body/fixed) and the padding-edge origin are now CSSOM View §10. The static
positions themselves were already right — the orange span paints exactly one glyph run after
'12345'.
- Moved: `inline-static-position-001` (the parked finding — the plumbing was right, the
  measurement was the red).
- **ch investigation (no commit):** the `width: 10ch` containers resolve at 0.5em (CssUnits ch ≈
  0.5em); the measured '0'-glyph advance would be 1em for Ahem. A working fix (TextMeasure
  ZeroAdvanceRatio + CssUnits wiring) was reverted: it flips float-nowrap-3/4/8/9 and
  table-anonymous-objects-214 to fail (the CORRECT width exposes float-interaction bugs — 6.B's
  area) for no D-side gain. Handed to 6.B/6.C/6.J: fix the float interaction first, then measure
  ch.
- **vertical-align-top-bottom-001 (parked finding): RESOLVED** — it passes (on A-pass + canary).

**d1ebd03c — window named access falls back to the document for dropped boxes.** The block-in-
inline hoist leaves no box for an inline whose only child was a block; the resolver's rendering
walk missed `<span id="posMe">`, so `posMe.style.position` threw "cannot read property 'style'
of null". The DOM-side fallback wraps the first-bound layout node.
- Moved: `adjacent-to-relpos-inline-that-had-block.html` +
  `adjacent-to-relpos-inline-in-inline-that-had-block.html`.
- The offsets those cases assert were already right (the abs box sized by the outer relative div);
  only the script setup could not run.
- Units 740/0 (`OffsetLeft_MeasuresAgainstTheOffsetParentPaddingEdge`); css21 51+1; wpt 86+2;
  lane list +33 total on D.txt, 0 losses; D-pass 1203/1203; canary 960/960; C-pass 2974/2974
  (both files are 6.C's area).
- Note: a stray `kprobe.html` (another lane's probe, 00:50 timestamp) sat untracked in the
  vendored wpt checkout and tripped the profile-readiness unit test; moved to
  `Lite.Conformance/artifacts/stray-kprobe-from-vendor.html`, vendor pristine again.

### 20 November 2026 — commit 09a4aea1 (batch 2)

**09a4aea1 — a fixed box's own fixed descendants paint, under its clip.** §10.1: a position:fixed
box is not the containing block of its own fixed descendants, so they anchor to the viewport too —
the PaintFixedNodes walk stopped descending at the first fixed box, so such a descendant was laid
out and painted by nobody. The separate paint replays the §11.1.2 'clip: rect()' of every
abs/fixed ancestor (the same replay PaintThroughClippers makes for stacking items), but NOT an
ancestor's overflow clip (§11.1.1's exception).
- Moved: `clipping-006` official + WPT (handed in by 6.I).
- Lost and repaired in-commit: `clipping-010` (the red fixed child of a clipped fixed parent
  painted outside the clip once it painted at all — the clip replay is part of this commit).
- Units 739/0 (`FixedDescendantOfFixedOverflowBox_PaintsAtItsViewportAnchor`,
  `FixedDescendantOfClippedFixedParent_StaysInsideTheClip`); css21 51+1; wpt 86+2; lane list +2,
  0 losses; D-pass 1203/1203; canary 960/960; I-pass 2475/2470→2475/2475 (rerun with the replay).

### 20 November 2026 — commit 591e482d (batch 2)

**591e482d — a blockified table-internal child of a table is ordinary out-of-flow content.**
§9.7 (dis-pos-flo) computes an abs/fixed/floated box's 'table-column-group'/'table-column'/
'table-caption' display to 'block', so the box has left the table's structure (§17.2.1) and is
not a column of it (§17.5.1's cell-area background layer does not apply). Two defects: the
predicates in TableEngine still matched such a child by its RAW display, so its background
painted only the cells' slice (the green square of the *-applies-to-005/006 family was clipped
to a 96×18 strip behind an 18px table); and the table recorded no static position for its
out-of-flow children (§10.3.7 / §10.6.4), so with one offset 'auto' the box fell to the initial
containing block's corner. `RecordTableStaticPositions` (owned core) + the `IsColumn`/
`IsColumnGroup`/`IsCaption` exclusion + the call site (TableEngine.cs — see "Edits outside
owned files").
- Moved 30: `top/bottom/left/right/position-applies-to-005/006/015`, each official + WPT twin.
  The -015 caption variants pass through the same ordinary-block path.
- Still failing here: everything else on D.txt (34 official / 59 WPT), unchanged.
- Units 737/0 (`AbsPosTableColumnGroupChild_PaintsItsOwnBorderBox` fails pre-fix;
  `AbsPosTableChild_TakesTheTableStartAsItsStaticPosition`); css21 51+1 expected-fail; wpt 86+2
  expected-fail; lane list +30, 0 losses; D-pass 1203/1203; canary 960/960; E-pass 1777/1777
  (TableEngine.cs touched, so 6.E's list ran).

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
