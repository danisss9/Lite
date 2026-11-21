# 6.C Block flow, margin collapse and sizing

Branch `css21/p6-c`, worktree `C:\Repos\Lite-p6-c`. Case list `Lite.Conformance/artifacts/lanes/C.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Block.cs
- Lite/Layout/BoxDimensions.cs
- Lite/Layout/CssUnits.cs

## Opens with (parked findings)

- Lite sizes a `display: table` box's percentage width as content-box, so a bordered 100%-wide table's cells record boxes past the table's border (`text-align-applies-to-008`). Settle the reading against the spec and browsers with 6.E.
- `inline-block-replaced-height-008`: an `<object>` whose `style` sets `width: 15%` together with a height keeps its intrinsic 60px (replaced sizing lives in `BoxEngine.Block.cs`).
- **The SVG replaced cluster (root cause, needs an integrator decision):** ~25 cases on C's
  list (`replaced-intrinsic-001/003/004/005`, `replaced-elements-*` ×10, `inline/block-
  replaced-width/height-002..008`, `inline-block-non-replaced-width-002`, …) fail because
  **SVG images are not decoded at all**: `ResourceLoader.DecodeImageBytes` goes through
  `SKCodec`, which has no SVG codec, so `<img src="*.svg">`/`<object>` yields no bitmap and no
  intrinsic size (probe: `replaced-intrinsic-001.xht`'s object renders 104×35.75 instead of
  150×150). Most of the family also needs §10.3.2's intrinsic-ratio rules (viewBox-only SVGs,
  the 300×150 default object size) which `TryResolveReplacedSize` does not implement — it uses
  only `IntrinsicWidth/Height`. Fixing this needs an SVG rasterization path (a `Svg.Skia`-style
  dependency or a hand-rolled mini-rasterizer) in the media layer plus ratio plumbing —
  integrator scope, not lane-6.C scope. The official `*-applies-to` twins of these cases fail
  the same way ("No reference path passed" — the twin they reference is one of these).
- **`margin/padding-em-inherit-001` (root cause, hand to 6.K):** AngleSharp resolves
  `margin: inherit` by copying the parent's SPECIFIED value, so `#parent { margin: 2em … }` /
  `#child { margin: inherit }` gives the child the raw string "2em" (probe: the child's used
  margin-top is 80px = 2em at the child's 40px font, not the parent's computed 56px). At the
  LayoutNode layer the keyword is gone and indistinguishable from an authored `2em`, so no
  post-parse fix can see it; the fix is a parse-time winning-declaration resolution (the
  `OwnFontSizeDeclaration` precedent) or an AngleSharp-level computed-value fix. The
  css-wide-keyword/override-channel half IS fixed and unit-tested (below).

## Coordination

- The `ex` unit resolves in `CssUnits.cs`, which you own, but the measured x-height comes from 6.J. Take 6.J's metric API, and do not measure fonts here.
- `auto-margins-used-values-with-floats.tentative` stays failing by choice: it is tentative
  (csswg#9174) and expects the START-side auto margin to resolve within the float-shortened
  band while the END side reads against the full containing block — a band-aware §10.3.3 solve
  that should wait for the issue to settle.

## Handed in

- `border-dynamic-001`, `border-dynamic-002` (official+WPT, 4): even statically (without JS), a bordered `#test` containing an empty child with `margin: 10em 0` collapses to 0 height instead of including the child's self-collapsed 10em margin inside the bordered block's auto height (from 6.I).

**To 6.B (floats):** `margin-collapse-145`, `-146`, `-147`, `-148` (block flow stem, still on
C's list) — the §8.3.1 adjoining-chain forms they need are now in place (commit 2edcae6b:
floats are out of flow for `SelfCollapses`; the chain is transitive through consecutive
self-collapsers), but the float's ink inside the collapsed-through chain still lands one band
short: the chain-float ride machinery (`chainWasSpentAbove` / `CollapseShift` in
`LayoutChildrenImpl`, tuned for margin-collapse-162/163) keeps a float placed at a chain's
START at its placement point, and 145/147 need it to track the chain bottom that the LAST
self-collapser's bottom margin (f's 1em) opens below the float. All four carry the CSS1-era
"assumes the w3c-css-wg 0347 proposed changes" caveat; the pass condition compares 2 ink
bands and Lite draws 1.

**To 6.E (tables):** `margin-applies-to-007`, `margin-bottom-applies-to-012/-013/-014/-015`,
`margin-left-applies-to-007`, `margin-top-applies-to-007` (block flow stem, still on C's
list; official + WPT twins) — §8.3: margin does not apply to table-cell/row/caption-family
boxes; Lite applies it. The fix is in the table engine's cell/row box placement
(`TableEngine.cs`), not in block flow. Same cause likely for C's `min/max-width-applies-to-`
and `padding-*-applies-to-` table cases (`-007`/`-008`/`-010`/`-014`/`-015`).

**To 6.I (borders):** `c5511-brdr-tw-001/-003`, `c5512-brdr-rw-001/-003`, `c5513-brdr-bw-001/
-003`, `c5514-brdr-lw-001/-003`, `c5515-brdr-w-001`, `c5518-brdr-t-001`, `c5520-brdr-b-001`
(css1 stem, still on C's list; official + WPT twins) — the diagnostics say the border lines
simply do not paint above/below `td`/`span`/`li.three` boxes (`border-top: blue 2px solid` on
table cells and inline fragments). Border painting, not block flow; the table-cell half may
need 6.E.

**To 6.M (judges):** `height-014`, `max-height-014`, `min-height-014` (official + WPT twins,
still on C's list) — the engine renders them correctly (a 1pt = 1.33px full-width band is
visible in the render), but the judge's shape detector reports "0 top-level shapes below the
instructions" where the stated pass condition is "one solid line". The band is likely under
the detector's minimum height.

**To 6.M/6.X:** `margin-percentage-inherit-001` — percentage pass-through now works (the
`inherit` keyword carries the percentage verbatim and it re-resolves at the child's containing
block, verified by unit test); the case still fails on the abs-pos overlay geometry, which
needs a look at the test's positioned layer before I can say the cause is one thing.

- `css21-official/html4/margin-collapse-clear-000.htm`, `-001`, `-004`, `-006`, `-007`, `-010` and
  the WPT twins under `css/CSS2/margin-padding-clear/`: these contain no `float` and no `clear` at
  all — the failing bar is the collapsed-through parent's background ("each coloured bar matches a
  same-coloured box", the #c0c0c0 bar at 19,151). Pure §8.3.1 margin collapsing, BoxEngine.Block.cs.
  From 6.B (they landed on B by test-name stem).
- `floats-wrap-bfc-with-margin-001..010` (WPT): shared — the §10.3.3 negative-margin "prop-up" of an
  auto-width BFC beside a float is the Block-side width resolution in the
  `EstablishesBlockFormattingContext` branch; 6.B owns the float side of the same cases. From 6.B.

## Edits outside owned files

- **Lite/Scripting/QuickJsInterop.cs** (commit 69d5f09e, one branch plus a JS proxy
  literal): a `JsStyle` wrapper now goes through a Proxy so unbound string properties
  reach `getPropertyValue`/`setProperty`/`removeProperty`, with camelCase mapped to the
  hyphenated CSS name. CSSOM defines CSSStyleDeclaration as a named-property object; before
  this every scripted write outside `JsStyle`'s fixed CLR list was an expando the layout
  never saw (`style[marginProp]`, `style.flexWrap`, `style.direction` in
  floats-wrap-bfc-with-margin's generated rows). Merge-next expect: any page that writes a
  CSS property not exposed as a CLR member now actually applies it. C-pass 2,974/2,974,
  B-pass 494/494 and canary 960/960 read green with it in.

- **Lite/Scripting/Dom/JsElement.cs** (commit a6f13280, one getter pair): `offsetLeft`/
  `offsetTop` now read `BorderBox.Left/Top` instead of `MarginBox.*` — CSSOM View reports the
  border edge, and the margin edge made `auto-margins-root-element` read 0 instead of 700.
  Expect a possible (small) diff in any case asserting `offsetLeft`/`offsetTop` on a margin'd
  box; canary 300/300 and both curated gates were read green with it in.
- **Lite/Layout/StyleResolver.cs** (one commit, two hunks): `EffectiveValue` converts
  'em'/'ex' to px at the DECLARING element's font-size when a css-wide keyword inherits them
  (§6.2.1 computed values — a child re-resolving the raw "2em" doubles it), and percentages
  now pass through verbatim instead of falling to the initial value; plus
  `ResolvePendingCssWideKeywords` stamps `margin-*`/`padding-*` keywords found on the DECLARED
  channel into the override channel so a stylesheet `margin: inherit` resolves at all.
  6.K merges before 6.C — trivial conflict surface. Unit-tested
  (`CascadeTests.Inherit_MarginTakesParentComputedLengthNotRawEm`); the served
  em-inherit family needs the parse-time AngleSharp fix above and stays red.
  **Fix round (commit 8dcc06a3):** one more line in the same function. `EffectiveValue`
  opens `CssUnits.ForFont(node)` around the em/ex conversion, so `ex` reads the declaring
  element's x-height and not the 0.5em fallback.


## Progress

### Commit 7bbc83bc — min/max-height separates the parent/child bottom-margin collapse

- **Moved (6):** `margin-collapse-min-height-001` (html twin), `margin-collapse-min-height-002`
  (html + xht), `min-height-separates-margin`, `max-height-separates-margin`,
  `margin-collapse-through-percentage-height-block`. Lane list 222 → 216.
- **What changed:** `LayoutBlockCore` defers the §8.3.1 collapse-through decision until after the
  §10.7 min/max-height clamp (a clamped height separates the margins — CSS 2.2 change s.8.3.1c);
  a fully self-collapsed chain that escaped through the box's top edge no longer re-opens a gap
  inside it behind a top boundary; `SelfCollapses` resolves percentage heights against the
  containing-block height (§10.5) and requires a zero computed `min-height`.
- **Still fails, honestly:** `margin-collapse-min-height-001.xht` (Bert Bos's original with the
  float-only last child) still fails on this commit alone — it needs the adjoining-through-
  out-of-flow chain (next commit). The `*-applies-to-*` twin family and `replaced-intrinsic-*`
  clusters are untouched.
- **Checks:** units 650/0 (3 new tests); gates css21 51/1 expected-fail, wpt 86/2; canary 300/300.

### Commit 2edcae6b — margins adjoin through out-of-flow children

- **Moved (1):** `margin-collapse-min-height-001.xht` (the Bert Bos original; the html twin
  moved with commit 1). Lane list 216 → 215.
- **What changed:** `SelfCollapses` skips float children (a float is out of flow, §9.3.1, so a
  float-only box still self-collapses), and `SelfCollapserBottomJoin` is transitive through
  consecutive self-collapsing siblings.
- **Still fails, honestly:** `margin-collapse-145..148` (see "Handed in" to 6.B) — the chain
  forms are in place, but the float's position inside the collapsed-through chain (the chain-
  float ride machinery) still lands the float's ink 1 band short of the two bands the pass
  condition compares.
- **Checks:** units 651/0 (1 new test); gates css21 51/1, wpt 86/2; lane list 0 losses; canary
  300/300.

### Commit 0a10caf4 — auto horizontal margins per §10.3.3

- **Moved (1):** `margin-auto-on-block-box` (the ~60-box over-constraint reftest).
- **What changed:** `LayoutBlockCore`'s auto-margin solve subtracts the SPECIFIED opposite
  margin from the slack (it used to double-count it), never lets the START side go negative,
  and lets the END side take the slack as-is (negative under over-constraint); both auto with
  an overflow is 0/−100 under 'ltr'. Mirrored under the containing block's 'rtl'.
- **Still fails, honestly:** `auto-margins-used-values-with-floats.tentative` — tentative
  (csswg#9174): the expected start-side auto margin resolves within the float-shortened band
  while the end side reads against the full containing block. Needs a band-aware §10.3.3
  solve; parked rather than implement a resolution that may still change.
- **Checks:** units 654/0 (3 new tests); gates css21 51/1, wpt 86/2; lane list 0 losses; canary
  300/300.

### Commit a6f13280 — offsetLeft/offsetTop report the border edge (edit outside owned files)

- **Moved (1):** `auto-margins-root-element` (with 0a10caf4: the root's auto margin now
  resolves, and `offsetLeft` reads the border edge as CSSOM View requires instead of the
  margin edge).
- **Checks:** units 654/0; gates css21 51/1, wpt 86/2; lane list 222 → 213 total, 0 losses;
  canary 300/300.

### Commit c80a206f — css-wide keyword inheritance passes computed values (§6.2.1)

- **Moved (0):** hardening only — lane list stays 213, no losses.
- **What changed:** `EffectiveValue` converts em/ex to px at the declaring element's font-size
  and passes percentages through verbatim; `ResolvePendingCssWideKeywords` stamps declared-
  channel margin-*/padding-* keywords into the override channel.
- **Still fails, honestly:** the served `margin-em-inherit-001` / `padding-em-inherit-001` —
  AngleSharp copies the parent's SPECIFIED value for `inherit`, so the keyword never reaches
  layout (probe: the child's used margin is 2em at its own 40px font = 80px, not 56px). Root
  cause and the parse-time fix direction are in "Opens with (parked findings)"; handed to 6.K.
- **Checks:** units 655/0 (1 new test); gates css21 51/1, wpt 86/2; canary 300/300.

## Fix round (after the first trial integration)

The integrator's full baseline of the merged batch found 13 cases that passed at the lane base
`05228a26` and fail on `css21/p6-c` alone (`fix-losses.txt`). I bisected each one by building
every lane commit and running the list.

| Lost case (official + WPT twins) | Broken by | Root cause | Fixed by |
| --- | --- | --- | --- |
| `margin-collapse-128`, `-129`, `-135` | 7bbc83bc | That commit counts a chain behind a top boundary in the content height. That is right, but it exposed two places that cut a chain to its scalar value: a trailing chain worth 0 (1em with -1em) was dropped, and a self-collapser's effective top folded in as a float. Both lost the cancelling member. | c15ff277 |
| `height-114` | 7bbc83bc | A net-negative chain inside a bordered box gave a negative content height (-160px). §10.6.3 heights are never negative, so it clamps at 0. | 452bab08 |
| `margin-collapse-038` | 7bbc83bc | The 'max-height' half of the separation. §8.3.1 asks only for an 'auto' computed height, which a max-height cut leaves in place. | d46f240d |
| `new-fc-separates-from-float` (WPT only) | 2edcae6b | The chain now runs through a float-only box, so a following 200px BFC's 200px top margin joined it and dragged the float down. A BFC that cannot fit beside the chain's floats goes below them (§9.5), which separates its margin like clearance (§9.5.2). | 03fd69de |
| `word-spacing-101` | c80a206f | The inherited em/ex conversion ran outside a font scope, so 'ex' used 0.5em, not Ahem's 0.8em x-height. | 8dcc06a3 (StyleResolver.cs, outside owned files) |

**One lane gain dropped: `max-height-separates-margin` (WPT).** It and `margin-collapse-038`
are mutually exclusive. Both have a max-height cap over a taller last child that has a bottom
margin: one expects the margin separated, the other expects it collapsed through. wpt.fyi
stable (20 November) shows Chrome, Edge and Firefox pass max-height-separates-margin and fail
-038, with Safari the other way round. The CSS 2.1 text ("'auto' computed height") and the
0-loss rule (-038 is two cases, in both suites) settle it for -038. The min-height half of
7bbc83bc is untouched: the CSS 2.2 s.8.3.1c change covers it, and no case contradicts it.

**Moved:**
- all 13 losses back to pass;
- bonus `c5505-mrgn-003` (official + WPT), with 452bab08;
- lane list C.txt 9 → 10 passes: -1 max-height-separates-margin, +2 c5505-mrgn-003. Every
  other lane gain is held: `margin-collapse-min-height-001` (html + xht), `-002` (html + xht),
  `min-height-separates-margin`, `margin-collapse-through-percentage-height-block`,
  `margin-auto-on-block-box`, `auto-margins-root-element`.

**Checks (series head 8dcc06a3):**
- units 659/0 (5 new, 1 replaced);
- gates: css21 51/1 expected-fail, wpt 86/2;
- `fix-losses.txt` 13/13;
- `C.txt` 10/222;
- `C-pass.txt` 2,944/2,944;
- `B-pass.txt` 444/444;
- `canary.txt` 960/960.

Each new unit test fails on the pre-fix engine.

## Batch 2 (queue C.txt, 81 official + 113 WPT)

### Commit 69d5f09e — CSSStyleDeclaration named properties reach layout

- **Moved (0 on the lane lists directly):** unblocks the generated rows of
  `floats-wrap-bfc-with-margin-001..010`, whose script writes
  `style[marginProp]` (hyphenated), `style.flexWrap` and `style.direction`. All three were
  written into a JS expando because `JsStyle` bound only a fixed camelCase CLR list.
- **What changed:** `QuickJsInterop` wraps a `JsStyle` in a Proxy; unbound string names
  go to `getPropertyValue`/`setProperty`/`removeProperty`, camelCase converted to the
  hyphenated CSS name. Target and proxy are cached per element (style identity holds).
- **Checks:** covered by the series-head run below (cac4a979); the two new units pass and
  no list or pass-list reading was taken for this commit alone.

### Commit cac4a979 — BFCs beside floats and unresolvable percentage heights

- **Moved (2):** `new-fc-beside-adjoining-float` (WPT, B list — the first trial merge's
  pass is back) and `child-bottom-margin-in-unresolvable-percentage-height` (WPT, 6.K
  hand-in). The latter now renders the reference; both height cases below do too.
- **What changed — §10.3.3/§9.5:** an auto-width BFC's border box fills the float band.
  A margin on the float's side lies outside the border box (it may overlap the float's
  margin box), so it neither narrows nor moves the box; a far-side margin insets it. A
  negative total inline-axis margin is the §10.3.3 lower bound (`containing width −
  margins`) and props the border box up until it cannot fit beside the float, so it wraps
  below. This advances the negative-margin rows of `floats-wrap-bfc-with-margin-001..010`
  (they wrap now; the cases still differ — see below).
- **What changed — §8.3.1/§9.5:** the adjoining-chain fit test used the grandparent's
  width, so `new-fc-beside-adjoining-float`'s 300px red box in a 200px outer box made
  200 + 100 look too wide and severed the 190px chain (regression from 03fd69de). The
  test now resolves the holder's containing block to the nearest definite ancestor
  content width; the float rides to the chain bottom.
- **What changed — §10.5:** a percentage height against an auto-height containing block
  computes to `auto` (not 0); the root resolves against the initial containing block.
- **Still fails, honestly:**
  - `floats-wrap-bfc-with-margin-001..010`: the block side now wraps the negative rows,
    but `-001`/`-002`/`-003` still differ from their references by one flex line: the
    references set `align-content: start` on a wrapping inline-flex, and AngleSharp drops
    that declaration entirely (probe: `align-content` raw `''`), so the reference falls
    back to `stretch` and its second line starts ~6px lower than the block side's
    float-bottom wrap (expected aqua top 28 vs actual 22 in the first cell). That is a
    parser/value gap, not the flex algorithm. `-004`/`-005`/`-006`/`-008`/`-009` need the
    used-width/placement reading for a 51px far-side margin (and `-010` a negative
    margin-top) settled; `new-fc-separates-from-float` still passes.
  - `height-percentage-003a`, `min-height-percentage-003`: the layout now matches the
    reference; the only pixels left are two anti-aliased text pixels at the end of the
    sentence (x=705, y=5–6). Their assertions hold (green page, no red/scrollbar). That
    is a text/font difference, not block flow.
  - `margin-collapse-145..148`: every diagnostic says "the two shapes render
    identically"; the judge's shape count reads 1 ink band where it expects 2. The ink
    geometry matches the control, so this looks like the same minimum-band detector
    issue as `height-014` — 6.M, not block flow.
  - `margin-collapse-132/133/162`: unchanged (float placement inside/after a collapsed
    chain plus 162's 3200-pixel z-order/float diff).
- **Checks (series head cac4a979):**
  - units 741/0 (one full run; 6 new tests);
  - gates css21 51/0 + 1 expected-fail, wpt 86/0 + 2 expected-fail;
  - `C.txt` 0/81 official + 0/113 WPT (the listed cases are still the batch-2 failures);
  - `B.txt` 0/39 official + 1/85 WPT (`new-fc-beside-adjoining-float`);
  - `C-pass.txt` 1,401/1,401 + 1,573/1,573;
  - `B-pass.txt` 204/204 + 290/290;
  - `canary.txt` 492/492 + 468/468.

### Handed on (batch 2)

**To 6.B (floats):** `floats-wrap-bfc-with-margin-004/005/006/008/009/010` still need
the used-width/placement reading for a BFC whose far-side margin exceeds the float band
(51px) and for `-010`'s negative margin-top; the negative-margin rows of `-001..003/007`
now wrap on the block side, so only the reference side remains (next item).

**To 6.K (cascade/syntax):** the references for `-001`/`-002`/`-003` set
`align-content: start`; AngleSharp drops the declaration (`GetPropertyValueSafe` returns
`""`), so the reference flex container stays at the initial `stretch` and its wrapped
line starts ~6px lower than the block side. Accepting the CSS Box Alignment `start`/`end`
keywords (or `flex-start`-equivalent) for `align-content` should close those three.

**To 6.M (judges):** `margin-collapse-145..148` render both columns identically, but the
shape detector reads one ink band where the stated condition compares two (same minimum
band issue as `height-014`).

**To 6.J (fonts):** `height-percentage-003a` and `min-height-percentage-003` differ from
their references by two anti-aliased pixels at the tail of the text line; layout is
otherwise identical.

### Batch-2 stop

Two commits on `css21/p6-c` after `52f3bb54`. One lane-list move on B and one on K; no
losses on C-pass, B-pass, canary or either curated gate. Stopping here for the integrator.
