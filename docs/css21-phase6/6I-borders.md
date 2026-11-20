# 6.I Borders, outline and visual effects

Branch `css21/p6-i`, worktree `C:\Repos\Lite-p6-i`. Case list `Lite.Conformance/artifacts/lanes/I.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Drawer.Borders.cs
- Lite/Extensions/DrawCommandExtensions.cs

## Opens with (parked findings)

- Thin double sub-lines (width/3 is about 1.67px) read wrongly in the ring judges: their
  component pass drops 1px-tall components, so a crisp 3px `double` vanishes. The dotted duty
  cycle was a painter bug too and is fixed (see Progress, 20 November); the engine now paints
  both. Check each case's render before touching the painter, and hand judge-side issues to 6.M.

## Coordination

- The collapsed-border winners come from `TableEngine` (6.E). You own how they paint, not how they resolve.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

- `Lite/Scripting/Dom/JsStyle.cs` (minimal 1-property addition `clip`, in its own commit): exposes `element.style.clip` on `CSSStyleDeclaration` so `el.style.clip = "auto"` overrides stylesheet `clip: rect(...)` (CSS 2.1 §11.1.2, `clip-001`). Not owned by any lane.

## Progress

### 2026-11-20 — 3D border bevel shading on near-black colors (CSS 2.1 §8.5.3)
- **Moved (+2 WPT):** `css/CSS2/borders/groove-default.html`, `css/CSS2/borders/ridge-default.html`.
- **Change:** `Drawer.Borders.cs` (`LightenColor`) lifts near-black colors (`R+G+B < 96`) toward white so `groove`/`ridge` with the default black `border-color` paint visibly distinct light/dark halves instead of collapsing into solid black, while keeping pure-channel multiplicative scaling on hues. Added `BorderStyleTests.GrooveAndRidge_WithDefaultBlackColor_DifferFromSolid`.
- **Checks:** units 648/0; curated `css21` 51/1, `wpt` 86/2; lane `I` 0/72 official, 2/54 WPT; canary 300/300 (154/154 official, 146/146 WPT).

### 2026-11-20 — `clip` applies only to absolutely positioned elements (CSS 2.1 §11.1.2)
- **Moved (+1 official, +1 WPT):** `css21-official/html4/clip-non-absolute-001.htm`, `css/CSS2/visufx/clip-non-absolute-001.xht`.
- **Change:** `Lite/Extensions/DrawCommandExtensions.cs` (`GetClipRect`) returns `null` unless `node.GetPosition()` is `PositionType.Absolute` or `PositionType.Fixed` (§9.3.1 / §11.1.2). Added `BorderStyleTests.ClipRect_AppliesOnlyToAbsolutelyPositionedElements`.
- **Checks:** units 649/0; curated `css21` 51/1, `wpt` 86/2; lane `I` 1/72 official, 3/54 WPT; canary 300/300 (154/154 official, 146/146 WPT).

### 2026-11-20 — Expose `element.style.clip` on `JsStyle` (CSS 2.1 §11.1.2 / CSSOM)
- **Moved (+1 official, +1 WPT):** `css21-official/html4/clip-001.htm`, `css/CSS2/visufx/clip-001.xht`.
- **Change:** `Lite/Scripting/Dom/JsStyle.cs` adds `public string clip { get => Get("clip"); set => Set("clip", value); }` so dynamic `el.style.clip = "auto"` writes to `StyleOverrides["clip"]` and clears a stylesheet `clip: rect(...)`. Added `BorderStyleTests.StyleClipProperty_OverridesStylesheetClipToAuto`.
- **Checks:** units 650/0; curated `css21` 51/1, `wpt` 86/2; lane `I` 2/72 official, 4/54 WPT; canary 300/300 (154/154 official, 146/146 WPT).

### 2026-11-20 — Remaining 120 cases triaged and handed to owning lanes
- **To 6.M (78 cases, engine renders verified):** groove/ridge 2-shade split (20), thin double / dotted ring judge (14), nested dashed boxes & twin dashed lines (17), 1px/1pt thin lines (8), `-width-047` short/twin green lines (4), `-applies-to-010` list-item bullet + border line (11), `outline-style-applies-to-008` inner text glyphs (2), `clipping-017` side-by-side bands (2).
- **To 6.E (18 cases):** `border-*-applies-to-004` collapsed `table-row` interior border winner (16), `outline-applies-to-016/017` replaced `table-column(-group)` layout (2).
- **To 6.K (10 cases):** `border-*-color-030` + `outline-color-030` 4-digit hex `#1000` (5), `border-width-010` negative shorthand drop (2), `border-width-011/012` + `outline-width-096` `StyleResolver.EffectiveValue` computed `0px` under `style: none/hidden` (3).
- **To 6.D (6 cases):** `clipping-006/007` fixed-in-overflow containing block (4), `outline-layout-002` Appendix E §E.2 step 10 outline paint order (2).
- **To 6.C (4 cases):** `border-dynamic-001/002` bordered block auto height with self-collapsing empty child margin (4).
- **To 6.A (4 cases):** `border-001/003` WPT reference Ahem `word-spacing` wrapping (4).

### 2026-11-20 — §8.5.3 border-style painting: crisp double, even dotted cycle
- **Moved (+10 = 6 official / 4 WPT):** `border-style-002`, `border-style-006`,
  `border-style-shorthand-001..004` (official and WPT).
- **Change:** `Drawer.Borders.cs` `DrawBorderSide`: a `double` side is painted as two filled
  crisp bands on whole device rows (integer thirds, gap = the declared width minus the two
  lines) instead of two centred antialiased 1px strokes — a 1px stroke at a fractional
  position covers two rows at about half strength each, and the fringes merged the pair into
  one grey band that read as `solid`. `ApplyBorderStyle`: `dotted` now uses a `[w, w]` dash
  cycle (browsers' equal dot and gap) instead of `[w, 2w]`; the 1:3 cycle inked only a third
  of the band, below the 2/5 ink a mid-side reading needs, so a dotted side read as `none`.
  Added `BorderStyleTests.DoubleBorder_PaintsTwoCrispSubLinesWithAGap` and
  `BorderStyleTests.DottedBorder_UsesEqualDotAndGapLengths`.
- **Still failing, judge-side (6.M):** official `border-style-003/007` now report "bottom
  reads none, not double" — the ring judge's `Components` pass skips components whose bbox
  height (or width) is 1px, so the two crisp sub-lines of a 3px double are dropped.
  Official `border-style-004/008` also carry the pass-condition word `single` for the solid
  left side where the judge spells `solid`. Handed to 6.M.
- **Checks:** units 652/0; curated `css21` 51 passed/1 expected-fail, `wpt` 86/2; lane `I`
  8/72 official, 8/54 WPT (was 2/72, 4/54); canary 300/300 (154/154 official, 146/146 WPT).
