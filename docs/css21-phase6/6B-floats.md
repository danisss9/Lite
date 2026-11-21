# 6.B Floats and clear

Branch `css21/p6-b`, worktree `C:\Repos\Lite-p6-b`. Case list `Lite.Conformance/artifacts/lanes/B.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Floats.cs

## Opens with (parked findings)

- `border-conflict-style-107` (WPT): five floated 100x100 bordered tables pack into overlapping bars instead of a 200x200 square. This is float sizing and placement of bordered tables, shared with 6.E.
- `floats-137` is a timed render (6.M owns the protocol). The engine side is yours once 6.M routes it.

## Coordination

- Float placement inside inline runs (`PlaceInRunFloat`) lives in your file, but its caller is in `BoxEngine.Inline.cs` (6.A).
- Margin collapsing and clearance interact: `ApplyClear` is yours, and the margin chain (`BoxEngine.Block.cs`) is 6.C's.

## Handed in

- `list-style-applies-to-017`, `list-style-type-applies-to-017`,
  `list-style-image-applies-to-017`, `list-style-position-applies-to-017` (all WPT, from 6.F):
  an empty `display: list-item` block that is a sibling of a block-level float keeps its
  full-width content box instead of taking the float's shifted line band; its outside marker
  therefore paints off the left edge (expected: beside the float, where the reference puts it).
  The pseudo/position work in 6.F is done; only the float band is missing.

## Edits outside owned files

- **Commit 6e7088af** touches two files outside `BoxEngine.Floats.cs`, as one concern (a float
  joining the line box it is generated in cannot be split across the run collector, the run loop
  and the float placement):
  - `Lite/Layout/BoxEngine.Inline.cs` (6.A): the `InlineItemKind.Float` branch applies the reflow
    shift PlaceInRunFloat returns, recomputes the band with the line's height, measures the
    line's content extent without trailing collapsible whitespace, and a rule-7 hook shifts an
    empty line downward past float bottoms until the coming content fits.
  - `Lite/Layout/BoxEngine.Block.cs` (6.C): the run collector lets non-cleared float children
    join the run (they were split out and placed on top of the line's content; §9.7
    blockification made them hit the block-level break first), and a whitespace+float run keeps
    the pending margin chain adjoining (§8.3.1) while laying its floats at the flow point.
  Both edits were verified against the canary (300/300) and the curated gates after each step.
- **Fix round** (see "Fix round" under Progress), `Lite/Layout/BoxEngine.Inline.cs` (6.A) only:
  - commit 412ad54f: both `PlaceInRunFloat` call sites pass the run's `containingBlockHeight`
    (argument only);
  - commit 43909242: the empty-line rule-7 hook no longer requires the pen at the
    band's start edge (it carries the line's start offset, e.g. text-indent), and a collapsible
    space that overflows a line with no content no longer commits that line (§16.6.1).
- **Batch 2:**
  - `Lite/Layout/BoxEngine.Inline.cs` (6.A): the empty-line rule-7 hook delegates to
    `FitBandBelow` over `contentH` and always updates `bandLeft`/`bandRight`/`lineX` from that
    full-height band (`floats-zero-height-wrap-001/002`); `BreakAllowedBefore` re-enters the item
    loop (`itemIndex--; continue;`) after `CommitLine()` so wrapped lines run the empty-line
    rule-7 hook (`floats-wrap-top-below-inline-002l/002r/003l/003r`); `lineContentRight` starts at
    `bandLeft` so an empty line does not look occupied to `PlaceInRunFloat` (`c414-flt-wrap-001`);
    and `InlineBoxEdges`/`ApplyInlineItem` call `IsBrokenAroundInFlowBlock` so out-of-flow
    floated/abspos children do not strip an inline wrapper's horizontal margins (`floats-149`).
    Verified against `A-pass.txt` (1405/1405).
  - `Lite/Layout/BoxEngine.Block.cs` (6.C): removed the normal-flow cursor advance and float
    retirement on cleared floats (`clear` on a float is §9.5.2 constraint #10 on the float's own
    outer top inside `LayoutFloat`/`PlaceInRunFloat`, never on the in-flow cursor `runY` —
    `floats-zero-height-wrap-001/002`, `floats-placement-001/002/003`, `zero-width-floats`); and
    delegated the BFC-beside-floats branch to `AvoidsFloatsInBfc`/`FitBfcBesideFloats` (in
    `BoxEngine.Floats.cs`) with a post-layout re-fit loop when an auto-height BFC grows into a
    wider lower float (`floats-wrap-top-below-bfc-002l/002r/003l/003r`,
    `floats-wrap-bfc-003-left-table/003-right-table/004/008`, `floats-bfc-003`, `floats-039`).
    Verified against `C-pass.txt` (2974/2974) and `D-pass.txt` (1203/1203).

## Progress

### Commit 9a58beff — float auto margins are used as 0 (§10.3.5/§10.3.2)

- **Root cause:** the generic size resolver returned `(total-size)/2` for an `auto` horizontal
  margin — the in-flow block's §10.3.3 centering rule. §10.3.5/§10.3.2 use 0 for floats.
  Both float paths (`LayoutFloat`, `PlaceInRunFloat`) now zero the auto sides.
- **Moved (18):** official `float-non-replaced-width-001/003/005`,
  `float-replaced-width-001/003/005/006/011`; WPT twins `float-non-replaced-width-001/003/005`,
  `float-replaced-width-001/003/005/006/007/008/011`.
- **Still failing:** the rest of the lane list. Largest remaining groups, by root cause:
  - **Mid-line float placement** (biggest, ~50 cases): a float joined to the current line box is
    placed at the band edge on top of the line's earlier content instead of the line re-flowing
    beside it (floats-placement-vertical-001a/b/c, floats-006/036/…, c414-flt-wrap-001); and line
    boxes query bands with a 1px probe, missing floats whose top starts partway down the line
    (floats-wrap-top-below-inline/bfc, floats-zero-height-wrap). Needs `PlaceInRunFloat` upgrades
    (mine) plus caller edits in BoxEngine.Inline.cs (6.A) and BoxEngine.Block.cs's run collector (6.C).
  - **§9.5.1 rule 5:** a float may sit higher than an earlier float's top (floats-146). One-line fix
    in `LayoutFloat`, next commit.
  - **Clearance/margin-collapse-clear family:** the members with no float/clear at all (000, 001,
    004, 006, 007, 010) are margin-collapse cases — handed to 6.C below.
  - **floats-wrap-bfc-with-margin-001..010:** need §10.3.3 negative-margin "prop-up" of auto-width
    BFCs beside floats; their references are flex-based, so the flex engine must agree too.
  - **hit-test-floats-003/005:** `elementFromPoint` hit-testing, not layout — handed to 6.M below.
  - **computed-float-position-absolute:** `getComputedStyle(...).float` is not exposed — CSSOM, not
    layout; handed to 6.M below.
- **Handed to other lanes:** see "Handed in" additions for 6.C and 6.M below.
- **Checks:** units 653/653; css21 51+1ef; wpt 86+2ef; lane list 8/62 official + 10/112 WPT
  (from 0/0); canary 300/300.

### Commit af86209c — a float may not sit higher than an earlier float's top (§9.5.1 rule 5)

- **Root cause:** `LayoutFloat` pinned the float to the cursor and the open line (rules 4/6) but
  never to the tops of floats generated earlier in the source, so a later float rose beside an
  earlier one, above a float that had dropped.
- **Moved (6):** official `floats-141`, `floats-146`, `floats-placement-vertical-003`; WPT
  `css/CSS2/floats-clear/floats-141`, `floats-146`,
  `css/CSS2/floats/floats-placement-vertical-003`.
- **Checks:** units 650/650; css21 51+1ef; wpt 86+2ef; lane list 10/62 official + 13/112 WPT
  cumulative; canary 300/300.

### Commit 6e7088af — floats join the line box they are generated in (§9.5, §9.5.1)

- **Root cause (two facets, the lane's biggest group):**
  1. A float met inside inline content was split out of the run and placed by the block-level
     path at the band's edge — on top of the line box's already-laid-out content. §9.5 shortens
     the *current* line box by the float's margin box, so the line's earlier content re-flows
     beside the float.
  2. Line boxes queried float bands with a 1px probe, so a float whose top started partway down
     the line never shortened it, and an empty line whose band could not hold the coming content
     never shifted downward past the float bottoms (§9.5 rule 7).
- **What changed:** see "Edits outside owned files" above for the split across the three files.
  `PlaceInRunFloat` now owns the full §9.5.1 placement: replaced sizing (§10.3.2), rule-5 floor,
  rule-7 drops (band too narrow, or the reflow would push the line's content out of the band —
  floats-placement-vertical-003's 100px float beside a 5px "H"), the rule-6+8 open-line join onto
  the run's just-committed line (a `<br>` between float groups), and the §9.5 reflow shift.
  Trailing collapsible whitespace is excluded from the line's content extent (§16.6.1;
  floats-006's space between the span and the floated divs).
- **Moved (22):** official `floats-006/016/029/030/031/036`, `floats-placement-vertical-001a/b/c`,
  `float-replaced-height-004/005`; WPT twins `css/CSS2/floats-clear/floats-006/016/029/030/031/036`,
  `css/CSS2/floats/floats-placement-vertical-001a/b/c`,
  `css/CSS2/floats-clear/float-replaced-height-004/005`.
- **Still failing (the big remaining groups):**
  - `floats-wrap-top-below-bfc-002l/r, 003l/r` (+ official twins): the BFC-beside-floats branch
    in `BoxEngine.Block.cs` still probes bands with a 1px height — it needs the BFC's own extent
    (a post-layout re-position), which is 6.C's branch.
  - `c414-flt-wrap-001` (+ twin), `c5525-fltinln/fltmrgn/fltmult` (+ twins), `floats-114/141/
    143/146/147/149`, `floats-rule3-outside-left/right`, `floats-wrap-bfc-003/004/006`,
    `floats-zero-height-wrap-001/002`, `floats-placement-vertical-001a`: variously still short —
    they exercise the same §9.5 machinery at finer precision (replaced floats in runs, negative
    margins, rtl bands). Next batch starts here.
  - `floats-wrap-bfc-with-margin-001..010`: §10.3.3 negative-margin prop-up (handed to 6.C).
  - The `margin-collapse-clear` members with actual float/clear (002, 003, 008, 009): clearance +
    margin-chain interaction — `ApplyClear` is mine, the chain is 6.C's; parked for a joint pass.
  - `border-conflict-style-107` stays parked (shared with 6.E); `floats-137` stays with 6.M.
- **Checks:** units 650/650; css21 51+1ef; wpt 86+2ef; lane list 22/62 official + 24/112 WPT
  cumulative (from 0/0 at the batch start); canary 300/300. Two canary-lane flakes were
  investigated and re-run on their own per the README (`float-replaced-width-007/008`,
  `floats-placement-vertical-003` — contention failures that pass standalone; the 003 pair was
  additionally fixed properly by the rule-7 left-side gate).

### Fix round — the 35 cases 6e7088af lost

The integrator's baseline of the merged batch found 35 cases (official + WPT) that passed at the
lane base `05228a26` and fail on this branch alone (`fix-losses.txt`). The lane checks missed them:
they ran only `B.txt` and the 300-case canary.

- **Bisect:** all 35 pass at `05228a26`, `9a58beff` and `af86209c`; all 35 fail from `6e7088af`
  on. That commit made a float that follows whitespace or inline content join the inline run
  (`PlaceInRunFloat`) instead of the block-level path (`LayoutFloat`). That exposed two weaknesses
  of the in-run path and one in the line loop. Three root causes, three commits:
  1. **412ad54f, the in-run float laid out differently from a direct-child float** (§10.3.5, §10.6,
     §10.7). With an explicit height it never laid out its children. A floated table or flex box
     skipped its engine. Percentage heights resolved against 0 and min/max-height was not
     clamped. Both paths now share `MeasureFloat` and `LayoutFloatBox`.
     **Moved (5):** official `stack-floats-001/003`; WPT `stack-floats-001/003`,
     `border-conflict-style-107`.
  2. **f0a11d34, a leading in-run float did not move an empty line's start edge** (§9.5). The
     reflow shift was returned only when the line already held content, so whatever followed a
     float at the start of a line was laid out under it.
     **Moved (29):** official `c414-flt-fit-000/001`, `c5525-fltwidth-001`,
     `clear-applies-to-012/014`, `clear-inline-001`, `floats-040/122/132/133/134/136`,
     `inline-replaced-width-013`; their 13 WPT twins; and WPT `floats-placement-004`,
     `floats-placement-vertical-004`, `font-size-122`.
  3. **43909242, an indented empty line beside a leading float did not shift below it**
     (§9.5 rule 7, §16.6.1). Two parts in `BoxEngine.Inline.cs`: the empty-line rule-7 hook
     required the pen at the band edge, which a text-indented line never is, and the collapsible
     space after the float committed the still-empty line.
     **Moved (1):** WPT `line-break-after-leading-float`.
- **Lane gains:** none had to go. `B.txt` reads 22/62 official + 24/112 WPT, the same 46 cases as
  the lane's last run.
- **Font-size-119:** named in the hand-over but not in `fix-losses.txt`. Only `font-size-122`
  (WPT) is listed, and it is fixed.
- **Checks (final state):** units 653/653; css21 51 + 1 expected-fail; wpt 86 + 2 expected-fail;
  `fix-losses` 35/35; `B` 22/62 + 24/112; `B-pass` 444/444; `C-pass` 2944/2944;
  `D-pass` 1155/1155; `canary` 960/960. In the intermediate state after commit 1, 3 canary cases
  failed (`floats-134`, WPT `clear-applies-to-014` and `floats-040`). All 3 are loss-list members
  that commit 2 fixes.
- **Process note for the next batch:** run `B-pass.txt` (and the neighbouring lanes' pass lists
  when a commit touches their files), not just the canary. Of the 35 losses, 24 sat in
  `B-pass`, 5 in `D-pass`, 2 in `C-pass` and 1 in `J-pass`; the canary held only 3.

### Batch 2 — §9.5 / §9.5.1 / §9.5.2 float placement, full-height line & BFC band fitting, and cleared-float isolation

- **Root causes fixed:**
  1. **Full-height band fitting (`FitBandBelow`, §9.5):** both line boxes (`BoxEngine.Inline.cs`)
     and BFCs / tables / block-level replaced elements (`BoxEngine.Block.cs` via
     `AvoidsFloatsInBfc` and `FitBfcBesideFloats` in `BoxEngine.Floats.cs`) now probe
     `AvailableBand` over their full height rather than 1px at their top edge, stepping down one
     `NextFloatBottom` at a time until the box fits. When an item wraps onto a new line via
     `BreakAllowedBefore`, the loop now re-enters after `CommitLine()` (`itemIndex--; continue;`)
     so the new empty line runs the rule-7 hook (`floats-wrap-top-below-inline-002l/002r/003l/003r`,
     `floats-wrap-top-below-bfc-002l/002r/003l/003r`, `floats-wrap-bfc-008`, `floats-039`).
     Auto-width tables beside floats floor `wantedW` by `TableEngine.MeasureTableContentWidths(...).SumMin`
     (§17.5.2.2, `floats-wrap-bfc-003-left-table/003-right-table/004`), and auto-height BFCs that
     grow taller than their initial probe re-fit over their laid-out border-box height
     (`floats-bfc-003`).
  2. **Cleared floats do not advance the normal-flow cursor (§9.5.2 constraint #10):**
     `LayoutChildrenImpl` previously called `ApplyClear`, `RetireFloats`, and advanced `runY`
     when a float had `clear != none`, pushing subsequent normal-flow content down to the cleared
     float's Y. `ApplyClear` now applies strictly to the float's own `placeY`/`outerTop` inside
     `LayoutFloat` and `PlaceInRunFloat`, and `PlaceInRunFloat` guards its `reflow` shift with
     `af.Bottom > lineTopAbs` so a zero-height float at a line's top edge does not shift `lineX`
     (`floats-zero-height-wrap-001/002`, `floats-placement-001/002/003`, `zero-width-floats`).
  3. **§9.5.1 rules 3 & 7 outside the containing block (`TryPlaceFloatAt`):** a float with no
     same-side float to its outer side may extend past the opposite edge of its containing block
     (rule 7), provided it does not overlap an opposite-side float in the same BFC even when that
     float lies outside the containing block (rule 3, `floats-rule3-outside-left/right-001`).
  4. **Empty-line in-run float drop (`c414-flt-wrap-001`):** when an in-run float cannot fit at
     `lineTopAbs` on a line that holds no content yet (`lineContentRight == bandLeft`),
     `PlaceInRunFloat` slides down from `outerTop` past blocking float bottoms without adding the
     empty line's strut height `lineH`.
  5. **First-line unbreakable width stops at `<br>` (`float-no-content-beside-001`):**
     `FirstUnbreakableWidth` returns `0f` on `InlineItemKind.LineBreak` or `Float` so a long word
     after an initial `<br>` does not push the empty `<br>` line itself below the float.
  6. **Floated `::before`/`::after` edge isolation (`floats-114`), inline wrapper margins around
     floats (`floats-149`), and floated table minimum width (`floated-table-wider-than-specified`):**
     `MeasureFloat` only applies margin/padding/border/width/height on `#pseudo*` nodes when
     declared in `StyleOverrides`; `IsBrokenAroundInFlowBlock` ignores out-of-flow children when
     deciding whether an inline wrapper's horizontal margins are suppressed; and `MeasureFloat`
     floors a floated `display: table` by `SumMin`.
- **Moved (47 — 19 official + 28 WPT):**
  - Official (19): `c414-flt-wrap-001`, `floats-039`, `floats-114`, `floats-149`,
    `floats-rule3-outside-left-001`, `floats-rule3-outside-right-001`,
    `floats-wrap-bfc-003-left-table`, `floats-wrap-bfc-003-right-table`, `floats-wrap-bfc-004`,
    `floats-wrap-top-below-bfc-002l`, `floats-wrap-top-below-bfc-002r`,
    `floats-wrap-top-below-bfc-003l`, `floats-wrap-top-below-bfc-003r`,
    `floats-wrap-top-below-inline-002l`, `floats-wrap-top-below-inline-002r`,
    `floats-wrap-top-below-inline-003l`, `floats-wrap-top-below-inline-003r`,
    `floats-zero-height-wrap-001`, `floats-zero-height-wrap-002`.
  - WPT (28): the 19 WPT twins of the official cases above, plus `floats-bfc-003`,
    `float-no-content-beside-001`, `floated-table-wider-than-specified`, `floats-placement-001`,
    `floats-placement-002`, `floats-placement-003`, `floats-wrap-bfc-008`,
    `floats-wrap-bfc-with-margin-010`, `zero-width-floats`.
- **Still failing in `B.txt` (20 official + 57 WPT) and why:**
  - `c5525-fltinln-000`, `c5525-fltmrgn-000` (2 official + 2 WPT): layout is pixel-exact
    (`c5525-fltmrgn-000` dropped from 9000 to 3150 red pixels); the remaining red pixels are
    painted by `Drawer.cs` (`PaintInlineBoxOwnContent` line 1452) where an anonymous `#text` child
    of a block with `background: red url(...)` paints the parent's `background-color: red` in
    step 5 over the parent's step-3 background image on space characters. Handed to 6.D.
  - `c5525-fltmult-000` (1 official): uses a literal `<div class="red"><p>Red</p></div>` as one of
    4 test color swatches compared against a reference table; fails `redScan`. Handed to 6.M.
  - `floats-placement-005` (1 WPT): test page layout is exact (`*-actual.png`), but the reference
    page `floats-placement-005-ref.html` renders `.float-left` with inline `style="background:
    cyan"` as green. Handed to 6.K.
  - `floats-wrap-bfc-006` (1 official + 1 WPT), `clear-applies-to-015` (1+1),
    `float-applies-to-005/006/015` (3+3): table caption vs table grid independent wrapping around
    floats (§17.4) and anonymous table wrapper sizing. Handed to 6.E.
  - `float-replaced-height-007` (1 official + 1 WPT): SVG `<object>` intrinsic size (part of the
    SVG replaced cluster).
  - `floats-143` (1 official + 1 WPT), `intrinsic-size-float-and-line` (1 WPT): shrink-to-fit
    `max-content` width in `IntrinsicSizer.cs` (6.A) takes `max(float, inline)` instead of summing
    consecutive un-cleared floats alongside adjacent inline content.
  - `margin-collapse-clear-000/001/004/006/007/010` (6 official + 6 WPT, no floats/clear, already
    handed to 6.C) and `margin-collapse-clear-002/003/008/009` (4 official + 4 WPT),
    `adjoining-float-*` (4 WPT), `negative-clearance-after-adjoining-float` (1 WPT),
    `no-clearance-due-to-large-margin*` (2 WPT), `new-fc-beside-adjoining-float` (1 WPT),
    `new-fc-separates-from-float-2/3` (2 WPT), `clear-on-replaced-element` (1 WPT):
    margin-collapse chain + adjoining-float clearance interaction in `BoxEngine.Block.cs` (6.C).
  - `floats-wrap-bfc-with-margin-001..009` (10 WPT) and `zero-width-floats-positioning.tentative`
    (1 WPT): negative-margin BFC beside floats (mostly tentative CSSWG tests).
  - `floats-clear-multicol-*` (4 WPT): CSS Multi-column Layout (`column-count`), outside CSS 2.1.
  - `zero-space-between-floats-001..004` (4 WPT): `JsElement.offsetLeft`/`offsetTop` ignores
    `offsetParent` (`#container` has `position: relative`, so `offsetLeft` reads `108`/`8` instead
    of `100`/`0`).
  - `hit-test-floats-003/005` (2 WPT), `computed-float-position-absolute` (1 WPT): already handed
    to 6.M.
  - `float-root` (1 WPT), `floats-line-wrap-shifted-001` (1 WPT),
    `float-percentage-resolution-quirks-mode` (1 WPT): root-element float margin/containing-block,
    first-line wrap after narrow float band, and quirks-mode percentage height resolution.
- **Handed to other lanes:** added entries to `6M-judges.md`, `6E-tables.md`, `6K-cascade.md`, and
  `6D-positioning.md`.
- **Checks:** units 741/741; css21 51 + 1 expected-fail; wpt 86 + 2 expected-fail;
  `B.txt` 19/39 official + 28/85 WPT (47 moved from 0/39 + 0/85); `B-pass.txt` 494/494 (204
  official + 290 WPT, 0 losses); `canary.txt` 960/960 (492 official + 468 WPT, 0 losses);
  `C-pass.txt` 2974/2974 (1401 official + 1573 WPT, 0 losses); `A-pass.txt` 1405/1405 (669
  official + 736 WPT, 0 losses); `D-pass.txt` 1203/1203 (555 official + 648 WPT, 0 losses).
