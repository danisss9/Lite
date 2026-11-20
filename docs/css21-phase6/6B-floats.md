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

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

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
