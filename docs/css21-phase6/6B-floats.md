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

_(None yet.)_

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
