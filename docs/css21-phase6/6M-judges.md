# 6.M Judges and verdict routes (no engine changes)

Branch `css21/p6-m`, worktree `C:\Repos\Lite-p6-m`. Case list `Lite.Conformance/artifacts/lanes/judges.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite.Conformance/Css21/VisualChecks.cs
- Lite.Conformance/Wpt/WptVisualPage.cs
- Lite.Conformance/Css21/SelfTestOracle.cs
- Lite.Conformance/Css21/AnimatedImageCheck.cs
- scripts/gen_css21_pass_conditions.py
- Lite.Conformance/Css21/css21-pass-conditions.json and css21-pass-condition-reviews.json (sole writer)

## Opens with (parked findings)

- About 64 cases have a judge that cannot read the render, and about 48 have no route: WPT `manual`-kind copies, same-color pairs, an external reference origin.
- Timed renders: `floats-137`, `background-position-201` and `background-bg-pos-205`.
- `background-root-014`/`014a` need a resize protocol at a normal window height.
- Strict templates for manual-pending sentences that are machine-readable. The person-read residue goes to the approved-render ledger, which only a person approves.

## Coordination

- **Fail closed:** a judge change may only move a pass to unsupported or fail, never manufacture a pass. Every new check kind needs a unit test, and `PassConditions_EveryCheckKindHasJudgeAndEveryCaseHasARoute` must hold. Your case list is `judges.txt`.

## Handed in

- `text-align-bidi-002/003/004` (official + WPT, from 6.G, handoff): the engine centers the line on
  its advance width and the P's content box (8..792); the judge's `AlignCenter` walks
  `container = container.Parent` one level per iteration (it assigns `parent.Parent`), so from the P
  it lands on the document box (0..800) instead of the body/P content box, and its ink-edge gaps pick
  up the 8px body margin plus glyph side bearings (reported 223/213 on a 784px content box). The
  rendered advance box is symmetric around 400. Suggest using the nearest block whose content box is
  at least the subject's width, and a bearing-tolerant ink read.
- `direction-unicode-bidi-026/027/028` (official + WPT, 6 cases, from 6.G): the reviewed needle has
  **two lines** (`line1\nline2`, the inner block's paragraph and the outer override's paragraph), but
  `PaintedOrder` normalizes the whole needle to one string and compares it against the first single
  painted line carrying non-ASCII — no single line can equal the joined two-line expectation. The engine
  now paints both lines in the reviewed order (019/021/023 pass through the same path). Suggest matching
  the expected line whose non-ASCII content is on that band, or comparing the painted line sequence.
- `list-style-type-001`, `list-style-applies-to-005/006`, `list-style-type-applies-to-005/006`
  (official + WPT, from 6.F): `round-dot` rejects a 16px disc. The render is a filled circle 7-8px
  across including antialiasing, but `Components`' 5x5 ink bbox has every bbox edge inked, so
  `Rectangular` is true. A larger disc would pass here but break the `<=24px` bullet limit of
  `list-style-position-003/008/009` at 85px, so the two checks have no common marker size.
  Suggest testing corner emptiness (a circle's top row is narrower than its bbox) instead.
- `content-008` (from 6.F): the pass-condition needle is `"1."` but the sentence and test ask for
  the square bullet (`counter(test, square)`); the generated check does not match the sentence.
- `content-142` (from 6.F): `painted-only` compares the painted "PASS PASS" case-sensitively
  against the lowercase needles `["pass","pass"]` and reports the correct text as non-allowed.
- `quotes-applies-to-005/006` (from 6.F): `painted-only` with no needles reports the rendered
  quote glyphs as non-allowed tokens although the sentence only asks for quotes.
- `content-counters-003` (from 6.F): the two lines are pixel-identical (small hollow-circle
  counters) but `look-same` finds 0 ink bands below the instructions.
- `list-style-type-017` (official only, from 6.F): the twin reftest passes but the self-describing
  check reports "8 ink bands below the instructions".
- `list-style-position-020/022` (official + WPT, from 6.F): `hanging-indent` finds one text band
  instead of two because the blue border of the box encloses both text lines and its component
  spans them; the render itself has the second line shifted left under the bullet (the intended
  result). Suggest ignoring a band whose component is the box border, or grouping text
  components only.

- `text-align-applies-to-007`, `012`, `013`, `015` (official + WPT) — filler text is flush with the table/body content box (`x=8` left / `x=792` right with 8px body margin), but the judge compares against viewport edges `0` / `800`. — from 6.A
_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- table-height-algorithm-031/032 (from 6.E): the engine now renders the assert correctly (the two
  visible "Filler Text" bottoms share the row baseline; the caption is intentionally invisible
  via `color: white`), but the selftest judge still reports "a filler text painted no ink" — it
  appears to count the intentionally-white caption as a missing text. Needs a verdict route that
  accepts an explicitly-white filler text.
- `border-{top,right,bottom,left}-style-007/008` (official+WPT, 16) and `border-style-rendering-001/002` (official+WPT, 4): engine renders groove/ridge green lines and boxes cleanly; judge splits the two touching shades of green into `2 top-level shapes below the instructions` (from 6.I).
- `border-style-003/004/007/008` (official) and `border-style-shorthand-002/003/004`
  (official+WPT): the dotted side of these was a painter bug, fixed in 6.I on 20 November —
  those sides now read `dotted`. What remains is thin `double` at 3px: 6.I now paints the two
  sub-lines as crisp 1px filled rows, but `Components` drops every component whose bbox is
  1px tall (`right == left || maxY == minY`), so the sub-lines vanish and the side reads
  `none`. Official `border-style-004/008` also have the pass-condition word `single` for the
  solid left side where the judge spells `solid` (from 6.I).
- `border-016/017/018` (official+WPT, 6), `border-{left,right}-016/017` (official+WPT, 8), `border-bottom-017` (official+WPT, 2), `border-top-017` (official, 1): engine renders nested dashed boxes and parallel pairs of dashed lines cleanly; judge expects a single box or merges parallel dashed lines into one non-straight cluster (from 6.I).
- `border-{top,right,bottom,left}-width-092` (official+WPT, 6) and `border-{top,bottom}-width-014` (official, 2): engine renders 1px/1pt thin solid lines cleanly; judge misses 1px-thin lines (`0 top-level shapes`) (from 6.I).
- `border-{top,right,bottom,left}-width-047` (WPT, 4): engine renders the short 50x8 green line (top/bottom) and 2 green lines (left/right) with no red; judge rejects `50x8` as not a line and expects 1 line instead of 2 on left/right (from 6.I).
- `border-{top,bottom}-applies-to-010`, `border-{top,bottom}-color-applies-to-010`, `border-{top,right,bottom,left}-style-applies-to-010` (official + WPT `border-bottom-style-applies-to-010`), `border-left-width-applies-to-010` (official+WPT) (11 total): engine renders both the border line and the list-item marker bullet cleanly; judge misses the bullet when vertically offset from a top/bottom border or fails to detect dashed lines (from 6.I).
- `outline-style-applies-to-008` (official+WPT, 2): engine renders the blue dotted outline around the inline box; judge treats the inner "Filler Text" glyphs as shapes not on the box perimeter (from 6.I).
- `clipping-017` (official+WPT, 2): `#test-outer` (`left:60px;top:60px`) and `#control-outer` (`margin:20px 0 0 200px`) sit side-by-side horizontally on the same band; judge expects 2 vertically stacked ink bands (`the two lines render identically: 1 ink bands`) (from 6.I).

- `css/CSS2/floats/hit-test-floats-003.html`, `-005`: the checks call `elementFromPoint` over
  floats and get "error" instead of a target — hit-testing protocol/implementation, not float
  layout (no layout-side fix moves them). From 6.B.
- `css/CSS2/floats/computed-float-position-absolute.html`: the check reads
  `getComputedStyle(...).float` and gets `undefined` — the computed style does not expose `float`,
  a CSSOM/protocol gap, not float layout. From 6.B.

## Edits outside owned files

- `Lite.Conformance/Wpt/WptRunner.cs`: the manual-pending dispatch (`IsManualPendingRoute`, two route checks and `EvidenceKind`). The route cannot be expressed from the owned files (the kind switch lives here); it is 13 lines and one concern.
- `Lite.Tests/Css21CoverageTests.cs`: the unit tests for every new route/check (the lane rules require them).
- `docs/css21-phase6/6D-positioning.md`, `6H-backgrounds.md`, `6K-cascade.md`: findings handed in, one section each.

## Progress

### 1. Reviewed WPT manual copies get a fail-closed route (commit `route manual copies`)

- **Moved:** the 36 WPT `manual`-kind copies that the catalog could not execute now run their official-case route. 31 move `unsupported` → `manualpending` (reviewed, awaiting native evidence):
  `background-attachment-004/005/008`, `background-position-201`, `background-root-014a`, `block-in-inline-004/005/006`,
  `c414-flt-ln-002`, `c543-txt-decor-000`, `floats-110`, `floats-137`, `content-178`, `inline-formatting-context-010b`,
  `inline-block-004/005`, `abspos-inline-001/002`, `active-selector-002`, `dom-hover-001/002`, `first-letter-selector-029`,
  `first-line-selector-017`, `link-visited-selector-002`, `white-space-mixed-004`, `outline-019`,
  `outline-no-relayout-001`, `overflow-007/008/009`, `visibility-collapse-001`.
- **Moved to fail (5, all audited by eye — the render is the verdict):** `background-attachment-006`/`007` (red
  background/border visible where the inner box/image should cover it), `containing-block-017`/`018` (large red box
  visible; the page's own condition is "no red"), `html-precedence-003` (`<font color=green>` paints red; the
  presentational attribute loses). Handed to 6.H, 6.D and 6.K respectively.
- **Mechanism:** `WptRunner.IsManualPendingRoute` sends a `manual`-kind `css/CSS2` case whose reviewed entry is
  `manualPending` and has no machine check through `SelfTestOracle`, which can only report `manualpending` (clean
  render) or `fail` (failure red) — never a pass. `dom-hover-001`'s at-rest red is by design (`#green { color: red }`,
  the condition is the hover-then-removal state), so its reviewed entry sets `redScan: false`; the at-rest scan is off
  and the case stays manual-pending.
- **Left:** the other 76 unsupported are judge reads (c527 font ~20, decoration 16, marker/alignment 10, same-color
  without an embedded pair 10, …); they are the next batch.
- **Readings:** units 648/648; css21 51 passed + 1 expected-fail; wpt 86 passed + 2 expected-fail; lane list
  1078 manualpending / 5 fail / 76 unsupported, 0 passes (before: 1047 / 0 / 112); canary 300/300.

### 2. Judges read the render for the decoration, bullet, font and justify families (commit `judge reads`)

- **Moved to pass (20):**
  - `c527-font-001/002` × official+WPT (4): the paint-log font judge now reads the instruction paragraph's own
    runs when nothing paints below it (the c527 family prints the sentence in the face under test), verifying size
    and baseline pitch. Audited render: 18px serif, 27px baselines, 9px gap.
  - `text-decoration-071/072/073/074` × official+WPT (8): the template now carries `anchor: next` and the judge
    reads the first text element with its own text, so the float/inline-block subject is found.
  - `text-decoration-propagation-004/005` × official+WPT (4): the decoration judge takes the deepest
    "Filler Text" element (the float), whose border box covers the glyphs; the wrapper's zero-height box did not.
  - `list-style-001/016` × official+WPT (4): the plain "text below has a square bullet" sentence no longer requires
    a second line; only the "second line flows below that bullet" variant (`mode: flow-below`) does.
- **Moved to fail (8, audited):**
  - `c527-font-008` ×2: `font: oblique normal 700 18px/200%` paints 12px — handed to 6.J.
  - `text-align-bidi-008/009/010` ×2: the judge now reads the justify bands inside the block's content box (the 1px
    border merged every line into one band) and mirrors the last line's start edge for rtl; the RTL lines end 10px
    apart, so the stated both-edge alignment does not hold — handed to 6.G.
- **Strict templates fixed:** `TEXT_DECORATION` treated "has a line under it" as "does not have" (the group was
  read as a boolean); 15 entries now carry the stated polarity. This is what keeps `text-decoration-a-element-001`
  and `propagation-003` passing under the deeper filler read (they were passing only because the wide wrapper hid
  the line).
- **Still unsupported (honest, no route):** `c527-font-000/004/005/006/007/10` (small-caps: the painter records no
  paint-log entry for it), `c527-font-003` (the sentence states no font property a judge reads),
  `text-decoration-082/083` (image-only page, no text subject for the underline clause).
- **Check readings:** units 650/650; css21 51 + 1 expected-fail; wpt 86 + 2 expected-fail; lane list
  1078 manualpending / 13 fail / 48 unsupported / 20 pass; canary 300/300. A 1,370-case affected-kind run
  (`text-*`, `painted-*`, `first-line-*`, `justify`, `align-*`) shows no new-looking failures beyond the handed-in
  ones.

