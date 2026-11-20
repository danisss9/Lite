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

## Edits outside owned files

- `Lite/Parser.cs`: 3 lines wiring `ComputedFontWeight` on parsed elements (CSS 2.1 §15.6); `ResolveFontSizeValue` steps `larger`/`smaller` through the §15.7 absolute-size table.
- `Lite/Parser.Fonts.cs`: new partial file for 6.J font longhand cascade (`OwnFontWeightDeclaration`, `FontWeightOf`).
- `Lite/Models/LayoutNode.cs`: `ComputedFontWeight` property.
- `Lite/Extensions/DrawCommandExtensions.cs`: `GetFontBold` reads `TextMeasure.ComputedFontWeight(node) >= 600`; `GetFontFamilies` returns the full ordered list of available font families for missing-glyph fallback (CSS 2.1 §15.2 / §15.3).

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
