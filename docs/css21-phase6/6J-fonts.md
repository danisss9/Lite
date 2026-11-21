# 6.J Fonts

Branch `css21/p6-j`, worktree `C:\Repos\Lite-p6-j`. Case list `Lite.Conformance/artifacts/lanes/J.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/FontRegistry.cs
- Lite/Layout/PlatformFonts.cs
- Lite/Layout/TextMeasure.cs: the font/metrics API (typeface resolution, metrics, x-height)

## Opens with (parked findings)

- Resolve `ex` from the font's real x-height (OS/2 sxHeight or a measured 'x'). 6.C wires it in `CssUnits.cs`, and 6.H re-checks `background-position-091/092`.
- Bold Ahem glyphs do not widen (`font-weight`); `@font-face` (`font-family-rule`); font matching; the `c527` font sentences.

## Coordination

- **Your branch merges first in every batch**: font metrics move pixels everywhere. Run the canary on every commit.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- table-height-algorithm-013/014 (official+WPT, from 6.E): the engine geometry is right — the three
  `vertical-align: top` divs sit at identical tops — but the selftest judge measures TEXT INK tops,
  and the ink offset below a box top scales with font size ((ascent − cap-top) × size). With the
  default font (empty font-family → Liberation Sans) the 10pt/20pt/30pt inks spread ~5px. Needs a
  default font whose ascent sits at the cap line (Ahem-like) or a metrics-aware judge.

- `css/CSS2/css1/c527-font-008.xht` (and the official `c527-font-008.htm`) — `.nine { font: oblique normal 700 18px/200% sans-serif; }` paints every run at 12px: the `font` shorthand's size (and the 200% line-height) is not applied. The judge now reads the instruction paragraph's own runs and reports "every painted run is 12px, the sentence states 18px". From 6.M.

## Edits outside owned files

- `Lite/Parser.cs`: 3 lines wiring `ComputedFontWeight` on parsed elements (CSS 2.1 §15.6); `ResolveFontSizeValue` steps `larger`/`smaller` through the §15.7 absolute-size table; reject invalid `font` shorthands in `FontSizeOf`/`OwnFontLineHeightDeclaration` and reset leaked `font-family`/`line-height` (§15.8 / §10.8.1 / §6.2.1). **Batch 2:** `FontSizeOf` skips the bare-number `<font-weight>` when scanning the `font` shorthand for the size (§15.8/§4.3.2, c527-font-008).
- `Lite/Parser.Fonts.cs`: new partial file for 6.J font longhand cascade (`OwnFontWeightDeclaration`, `FontWeightOf`, `IsValidFontShorthand`, `HasOnlyInvalidFontShorthand`).
- `Lite/Models/LayoutNode.cs`: `ComputedFontWeight` property.
- `Lite/Extensions/DrawCommandExtensions.cs`: `GetFontBold` reads `TextMeasure.ComputedFontWeight(node) >= 600`; `GetFontFamilies` returns the full ordered list of available font families for missing-glyph fallback (CSS 2.1 §15.2 / §15.3); `GetLineHeight` honours `inherit` override and rejects negative `CssLengthValue` (§10.8.1).
- `Lite/Layout/BoxEngine.Inline.cs` (6.A's file; **batch 2**, four minimal edits, each its own commit):
  - `ReorderForBidi` builds its fragments in logical item order — a `<br>` takes the base level, every other non-text item is a UAX #9 neutral joining the surrounding run's level (§16.2, font-family-015 / bidi-span-001);
  - a signed below-baseline accumulator (`maxBelowSigned` + `LineMetrics`'s `BelowSigned`) so `line-height: 0` line boxes are 0 tall (§10.8.1, font-family-015);
  - `CommitLine` recomputes the line's metrics over the content range, excluding the removed trailing whitespace runs (§16.6.1);
  - `ContentAreaMetrics` takes `TextMeasure.FontAscentDescent` (§10.6.1, font-size-116).

## Progress

### 1. Computed `font-weight`, CSS Fonts weight matching, and synthetic bold

- **Spec:** CSS 2.1 §15.5, §15.6, §15.8; CSS Fonts 3 §5.2; CSS Fonts 4 §2.2.
- **Moved (+11 pass, +1 manualpending, 0 losses):**
  - `css21-official/html4/font-weight-002.htm`, `css/CSS2/fonts/font-weight-002.xht`
  - `css21-official/html4/font-weight-003.htm`, `css/CSS2/fonts/font-weight-003.xht`
  - `css21-official/html4/font-weight-012.htm`, `css/CSS2/fonts/font-weight-012.xht`
  - `css21-official/html4/font-weight-013.htm`, `css/CSS2/fonts/font-weight-013.xht`
  - `css21-official/html4/font-weight-bolder-001.htm`
  - `css21-official/html4/font-weight-lighter-001.htm`
  - `css21-official/html4/font-weight-normal-001.htm`
  - `css/CSS2/fonts/font-weight-015.xht` (fail → manualpending; no failure red)
- **Still failing (67):** `font-family-rule-*` (16), `font-family-002..006` (10), `font-style-002..004` + `font-matching-rule-001/006` (10), `font-weight-010/011/014` (6), `font-family-invalid-characters-002..004` (6), `font-size-003/014` (4), `fonts-011..013` (3), `font-045` (2), `font-matching-rule-009` (2), `font-size-rule-002` (2), `font-146`, `font-147`, `font-family-013`, `font-family-015`, `font-size-116`, `font-size-121`.
- **Checks:** units 650/0; curated css21 51/1, wpt 86/2; J.txt 11 pass / 1 manualpending / 67 fail; canary 300/300.

### 2. Per-family missing-glyph fallback and default-ignorable bidi characters

- **Spec:** CSS 2.1 §15.2, §15.3; CSS Fonts 3 §5.2.
- **Moved (+2 pass, 0 losses):**
  - `css/CSS2/fonts/font-family-013.xht`
  - `css/CSS2/fonts/fonts-013.xht`
- **Still failing (65):** `fonts-011` still needs bidi control-character stripping/zero-width handling (6.G), `fonts-012` needs per-run mixed-font fallback in `Drawer.Text.cs` (6.A), `font-family-015` still shows 36 red pixels (baseline/metric difference between fallback fonts); remaining clusters as above.
- **Checks:** units 651/0; curated css21 51/1, wpt 86/2; J.txt 13 pass / 1 manualpending / 65 fail; canary 300/300.

### 3. Relative `font-size` keywords (`larger`/`smaller`) step through the absolute-size table

- **Spec:** CSS 2.1 §15.7 (`larger` and `smaller` step through the absolute-size table before falling back to the 1.2 ratio outside table bounds).
- **Moved (+3 pass, 0 losses):**
  - `css21-official/html4/font-size-rule-002.htm`
  - `css/CSS2/fonts/font-size-rule-002.xht`
  - `css/CSS2/fonts/font-size-121.xht`
- **Still failing (62):** `font-family-rule-*` (16), `font-family-002..006` (10), `font-style-002..004` + `font-matching-rule-001/006` (10), `font-weight-010/011/014` (6), `font-family-invalid-characters-002..004` (6), `font-size-003/014` (4), `font-045` (2), `font-matching-rule-009` (2), `fonts-011`, `fonts-012`, `font-146`, `font-147`, `font-family-015`, `font-size-116`.
- **Checks:** units 652/0; curated css21 51/1, wpt 86/2; J.txt 16 pass / 1 manualpending / 62 fail; canary 300/300.

### 4. `font` shorthand validation (mixed `inherit` and negative `/<line-height>`)

- **Spec:** CSS 2.1 §15.8, §10.8.1, §6.2.1, §4.2.
- **Moved (+1 pass, +2 manualpending with 0 red pixels, 0 losses):**
  - `css/CSS2/fonts/font-146.xht` (fail → pass)
  - `css21-official/html4/font-045.htm` (fail 61440 red px → manualpending, 0 red px)
  - `css/CSS2/fonts/font-045.xht` (fail 96000 red px → manualpending, 0 red px)
- **Still failing (59) — breakdown & hand-offs:**
  - **6.M (judges — 46 cases already rendering correctly, judge heuristics rejecting valid output):**
    - `font-family-rule-004/007/011..016` (16 cases): renders a 16x16 Ahem box above `"X"` (verified on `font-family-rule-004.xht`), judge reports `0 boxes sit above the letter x`.
    - `font-family-002..006` (10 cases): renders `"PASS PASS"` with a 16x16 Ahem box below (verified on `font-family-002.htm`), judge reports `no black box below the words`.
    - `font-style-002..004`, `font-matching-rule-001/006` (10 cases): slanted 1em Ahem box rejected by minimum-size threshold (`the shape is only 20x16` / `36x16`).
    - `font-weight-010/011/014` (6 cases): green bold/normal `"Filler Text"` clearly visible under black overlay (verified on `font-weight-010.htm` and `font-weight-014.htm`), judge checks dominant ink (`#ff000000`) or reports no ink.
    - `font-size-003/014` (4 cases): 1px Ahem box below judge's minimum shape size (`0 top-level shapes below the instructions`).
  - **6.K (selectors/cascade/syntax — 7 cases):**
    - `font-family-invalid-characters-002..004` (6 cases): CSS syntax error recovery for unbalanced `(`/`)`/`{`/`[` pairs in stylesheet parsing.
    - `font-147.xht` (1 case): unquoted `font-family` syntax validation in `font` shorthand.
  - **6.A / 6.G (inline/text painting & bidi — 2 cases):**
    - `fonts-011.xht` (1 case): bidi control chars `U+202E`/`U+202C` need zero-width/stripping in bidi/text painting (6.G).
    - `fonts-012.xht` (1 case): mixed-font per-run fallback (`XXXX İ FAIL`) in `Drawer.Text.cs` (6.A).
  - **Remaining 6.J / design decisions (4 cases):**
    - `font-matching-rule-009` (2 cases): test declares `font-family: serif` while `font-weight-normal-ref.html` declares no `font-family`, assuming the UA default font is `serif` (Lite defaults to `"Arial"`; changing the global default font affects the whole suite and needs a separate decision).
    - `font-family-015.xht` (1 case): 36 red pixels from baseline/metric difference on `&#x05e2;` between fallback fonts under `line-height: 0`.
    - `font-size-116.xht` (1 case): 2358 red pixels at `font: 1000px/1 Ahem`.
- **Checks:** units 653/0; curated css21 51/1, wpt 86/2; J.txt 17 pass / 3 manualpending / 59 fail; canary 300/300.

### 5. Batch 2 — the `font` shorthand's bare number is the weight, not the size

- **Spec:** CSS 2.1 §15.8 (the `font` shorthand grammar: `[ <style> || <variant> || <weight> ]? <size> [ / <line-height> ]? <family>`), §4.3.2 (a non-zero length needs a unit).
- **Moved (+2 pass, 0 losses):**
  - `css21-official/html4/c527-font-008.htm` (fail → pass)
  - `css/CSS2/css1/c527-font-008.xht` (fail → pass)
- **Side effect:** `font-family-015` now parses as the spec reads (`font: 900 5em/0 serif` → size 5em, weight 900, line-height 0) and its red-pixel count went 27 → 838 (still fail): with the correct 80px size the two fallback glyphs are both visible and un-overlapped. The two causes are fixed in the next two commits.
- **Checks:** units 736/0; curated css21 51/1, wpt 86/2; J.txt 2 moved / 59 fail; J-pass 519/519, F-pass 1789/1789, canary 960/960.

### 6. Batch 2 — bidi reordering keeps non-text fragments at their logical position

- **Spec:** CSS 2.1 §16.2 / UAX #9 L2 (reordering runs over the whole logical sequence); UAX #9 N1/N2 (inline boxes between runs are neutrals).
- **Cause (found by rendering `font-family-015`):** `ReorderForBidi` split text items into level fragments first and appended the non-text ones (breaks, images, empty-span struts) after them, so the L2 pass carried a `<br>` past the RTL runs it separates — `ע<br>ע` laid all its runs side by side on one line. V1 of the fix (logical position + base level for every rider) regressed `bidi-span-001` (canary + G-pass loss): an empty span's strut between two Hebrew runs must JOIN the run (neutral), not split it.
- **Fix:** fragments build in one pass over the items. A `<br>` is a line terminator and takes the base level (reordering never crosses a line box, so a paragraph's line order cannot flip). Every other inline box takes the level of the surrounding characters when both sides share one level, else the base level. `ChainLevel` removed.
- **Moved:** engine-only so far; it is half of `font-family-015` (below). No J.txt movement by itself.
- **Checks (amended commit):** units 737/0; gates css21 51/1, wpt 86/2; J-pass 519/519, F-pass 1789/1789, A-pass 1405/1405; G-pass lost `bidi-span-001` on v1 → fixed forward; canary 959/960 on v1 → same fix.

### 7. Batch 2 — `line-height: 0` line boxes are 0 tall (the below-baseline reach is signed)

- **Spec:** CSS 2.1 §10.8.1 (leading = line-height − (ascent + descent), split half above and half below; a box's below-baseline reach is its line-height box's bottom edge, signed).
- **Cause:** the line accumulator floored the below-reach at 0, so a line whose every box ends above the baseline (all text at `line-height: 0`) came out half an ascent tall (27.7px at 80px Arial) and the second line's baseline sat that far below the first.
- **Fix:** `maxBelowSigned` tracks the signed max beside the 0-floored `maxBelow` the float-band heuristics read; `CommitLine` and `LineMetrics` take the signed value for the committed line height.
- **Moved (+9, on other lanes' fail lists, 0 losses):**
  - `line-height-bleed-002` official + WPT (6.A)
  - `text-indent-011` official + WPT (6.A)
  - `vertical-align-negative-leading-001` WPT (6.A)
  - `background-root-006` official + WPT (6.H)
  - `background-root-007` official + WPT (6.H)
- **Checks:** units 738/0; gates css21 51/1, wpt 86/2; canary 960/960; C.txt / F.txt / G.txt / I.txt / E.txt fail lists unchanged.

### 8. Batch 2 — a line's trailing whitespace run does not size the line box

- **Spec:** CSS 2.1 §16.6.1 (a space at the end of a line is removed — the box it would have made is gone from the line box).
- **Cause:** the removed runs sat in `placed` (painting nothing) while their metrics had already raised the line's above-reach — a trailing run in the block's own font dropped that line's baseline against its neighbours.
- **Fix:** `CommitLine` recomputes the line's metrics over the content range (`LineMetrics` gains an end bound), walking back past the paint-nothing riders and the removed whitespace-only text items; an empty inline's strut is a real box and still stops the walk.
- **`font-family-015` verdict (11 red px, was 838):** the two lines now break and stack correctly; the residue is a metric-environment artifact, NOT an engine bug. With `line-height: 0` every box is a zero-height box at (ascent−descent)/2 above the baseline: the serif strut (Times, 27.00 at 80px) pokes 0.7226px above the ע's fallback box (Lucida Sans Unicode, 26.277), Consolas bottoms above both, so §10.8.1's union is a 0.7226px line box and line 2's baseline sits that much lower — the pixel snap exposes 11 exact-red fringe pixels (9 at the ע's top tip, 2 at the tail). The test's perfect overlap requires the ע face's box to dominate the strut's; upstream environments get that (Courier New itself carries Hebrew), Lite's fallback chain (Courier New → Consolas → Lucida Sans Unicode) narrowly misses. Hand to 6.M: judge the stated condition (green ע painted over the red, gray Xs overlapped) or record an environment tolerance; the engine reading is §10.8.1-exact.
- **Checks:** units 739/0; battery (gates, J.txt, pass lists, canary) with commit 9/HEAD.

### 9. Batch 2 — font-size-116 diagnosed to the end; the metrics fix reverted on pass-list losses

- **Cause (font-size-116, `font: 1000px/1 Ahem`, fully diagnosed):** `CreateFont` edges Ahem with alias edging (the grid-fitted box edges batch 1 established), and Skia's ALIAS strike reports metrics **fitted to the rasterizer** — Ahem at 1000px: ascent 797/descent 203 — while the glyph it rasterizes keeps the font's true outlines (800/200; measured in-render: ink at baseline−800..baseline+200 exactly). The line box was sized from the fitted metrics, so the ink escaped 2.875px above it and left the box's last rows unpainted: the test's X uncovered 3 rows of the red control (2358 judged red px). The fit wobbles ±0.2–0.4px at every size and only vanishes at a few (20/80/100/160/200px); at 100px it is absent, which is why a scaled-down replica rendered clean.
- **Attempted fix (commit 2f05bacb, reverted):** `TextMeasure.FontAscentDescent` read the metrics through an AA-edged twin for alias-edged fonts. It took font-size-116 to 0 red pixels — and lost 29 pass cases (6.A ×23, 6.C ×5, 6.G ×1, all one red row of an Ahem control: white-space-*, margin-collapse-*, c548-ln-ht-001). Those cases' references had been matched WITH the fitted metrics: correcting the metrics moved the snapped baseline a row against a red background anchored at the unsnapped layout baseline. The pre-existing wart is that the ink snaps to the pixel grid (`SnapToDevicePixelY`) while the inline content area (the background rect) anchors at the unsnapped baseline; the Alias fit happened to compensate. Fixing it properly means re-aligning the paint-side snap with the content area (6.A's paint path) and re-tuning the batch-1 Ahem grid-fitting cases — not a rush job at the end of a batch.
- **State:** reverted; font-size-116 still fails (2358 red px) with the diagnosis above. Hand to 6.A + 6.J follow-up: paint the inline content area around the same snapped baseline the glyphs use (or drop the snap for aliased runs), then re-land the AA-metrics read.
- **Checks (batch head 6f7b6bd6, the definitive battery):** units 739/0; curated css21 51/1, wpt 86/2; J.txt 2 pass / 59 fail (font-family-015 at 11 red px, font-size-116 at 2358); J-pass 519/519, F-pass 1789/1789, A-pass 1405/1405, C-pass 2974/2974, G-pass 223/223, H-pass 1274/1274, I-pass 2475/2475; canary 960/960.

## Batch 2 summary

- **Four commits, 0 losses anywhere:** the `font` shorthand's bare number is the weight (c527-font-008 ×2 moved); bidi reordering keeps non-text fragments at their logical position; `line-height: 0` line boxes are 0 tall; a line's trailing whitespace run does not size the line box.
- **Moved: 11 cases** — c527-font-008 ×2 (J); line-height-bleed-002 ×2, text-indent-011 ×2, vertical-align-negative-leading-001 (6.A); background-root-006 ×2, background-root-007 ×2 (6.H).
- **Still failing, fully diagnosed:** font-family-015 (11 red px — metric-environment artifact, §10.8.1-exact engine; judge/environment call, see entry 8); font-size-116 (2358 red px — the alias-metrics vs rasterized-ink split, entries 9; fix reverted on 29 pass losses); font-matching-rule-009 ×2 (the serif-default decision item above).
- **Handed in this batch:** the font-matching-rule-009 decision to the user; font-family-015's judge call to 6.M; the snap/content-area realignment to 6.A. Carried hand-offs from batch 1 unchanged: 46 judge cases (6.M), font-family-invalid-characters-002..004 + font-147 (6.K), fonts-011 (6.G), fonts-012 (6.A).

### Decision item for the user: `font-matching-rule-009` assumes the UA default family is serif

- `css21-official/html4/font-matching-rule-009.htm` + `css/CSS2/fonts/font-matching-rule-009.xht` (and their shared reference `font-weight-normal-ref.html`): the test's divs declare `font-family: serif` (weights 400 and 9000-invalid); the reference declares NO font-family. The pair only matches when the UA's default font IS the face its `serif` keyword resolves to — true in Chrome/Firefox (their default font is a serif), not in Lite (default `Arial`, `serif` → Times New Roman). Result: "No reference path passed" on both cases.
- **Spec reading:** CSS 2.1 does not pin the UA default family. §15.3.1 maps the `serif` generic to "the user agent's default serif face"; the default font-family itself is UA stylesheet territory (F.2? no — the 2.1 sample UA sheet uses `font-family: serif` for body text, and CSS Fonts 3 §4.3 leaves the default to the implementation). So Lite's Arial default is spec-legal; the TEST is environment-dependent by construction.
- **Measured impact of changing Lite's default to the serif face:** among J-pass (519), canary (960), A-pass (1,405) and C-pass (2,974) — 5,858 passing cases — 3,611 (62%) render text with no `font-family`/`font:` declaration outside `@font-face` (rough count: static grep of the test sources; linked stylesheets under-count it). Changing the default re-renders the majority of the passing corpus: every wrap point, line height and reftest pixel that rides on Arial's metrics moves. That is a whole-suite re-baseline, not a lane fix.
- **Options:** (a) keep Arial and treat the 2 cases as environment-dependent (recommended — zero suite risk); (b) switch the UA default to the serif face for suite parity with browsers (needs a fresh baseline + re-triaged losses); (c) map `serif` → Arial (dishonest naming; breaks the generic-family cases). Stopped for the user's decision — no code changed.
