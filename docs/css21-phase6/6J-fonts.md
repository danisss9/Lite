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

## Edits outside owned files

- `Lite/Parser.cs`: 3 lines wiring `ComputedFontWeight` on parsed elements (CSS 2.1 §15.6); `ResolveFontSizeValue` steps `larger`/`smaller` through the §15.7 absolute-size table; reject invalid `font` shorthands in `FontSizeOf`/`OwnFontLineHeightDeclaration` and reset leaked `font-family`/`line-height` (§15.8 / §10.8.1 / §6.2.1).
- `Lite/Parser.Fonts.cs`: new partial file for 6.J font longhand cascade (`OwnFontWeightDeclaration`, `FontWeightOf`, `IsValidFontShorthand`, `HasOnlyInvalidFontShorthand`).
- `Lite/Models/LayoutNode.cs`: `ComputedFontWeight` property.
- `Lite/Extensions/DrawCommandExtensions.cs`: `GetFontBold` reads `TextMeasure.ComputedFontWeight(node) >= 600`; `GetFontFamilies` returns the full ordered list of available font families for missing-glyph fallback (CSS 2.1 §15.2 / §15.3); `GetLineHeight` honours `inherit` override and rejects negative `CssLengthValue` (§10.8.1).

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
