# 6.H Backgrounds

Branch `css21/p6-h`, worktree `C:\Repos\Lite-p6-h`. Case list `Lite.Conformance/artifacts/lanes/H.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Drawer.Backgrounds.cs
- Lite/Network/ResourceLoader.cs
- Lite/Media/*

## Opens with (parked findings)

- A `data:` URL in `background` is cut at its `;` (`data:image/png;base64,...` reads as `data:image/png`). The raw-background rescue lives in `Parser.Cascade.cs` (6.K): coordinate.
- `background-position-091/092`: `ex` must be the font's measured x-height, not 0.5em. 6.J provides the metric; re-check these after 6.J lands.
- `background-intrinsic` (SVG intrinsic sizes), `background-alpha`, `background-table`, `background-attachment`.
- `background-animated-001`: the animated-image work landed (`Lite/Media/AnimatedImages.cs`). Confirm its route with 6.M.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_
- Heads-up from 6.E: my commit b4547ba0 (table layer background paint areas) edits your
  `Drawer.Backgrounds.cs` PaintColumnBackgrounds — it now draws the per-cell background rects
  `TableEngine.TryGetCellAreaBackgrounds` records for the separated model (§17.5.1) and clips
  column background images to them; the band painting stays as fallback. Also §14.2 background
  colours now paint the border box in `Drawer.cs` PaintBlock/PaintBlockDecorations, so
  transparent borders show the background through — re-check your background-position cases
  that have transparent borders after the merge.
- A `data:` URL in `background` is cut at its `;` (`data:image/png;base64,...` reads as `data:image/png`). The raw-background rescue lives in `Parser.Cascade.cs` (6.K): **still open**, untouched this batch.
- `background-position-091/092` (and `background-position-001/002`, same root cause): `ex` must be the font's measured x-height, not 0.5em. 6.J's metric is still missing; all four still fail. Re-check after 6.J lands.
- `background-intrinsic` (001/002/003/006/007/008/009): **root cause found** — `ResourceLoader.FetchImage` hands SVG bytes to `SKCodec`, which has no SVG codec, so every SVG background image decodes to null and paints nothing. Needs an SVG rasterizer reachable from `FetchImage` (planned next batch; `Lite/Media/` + `ResourceLoader.cs` are mine).
- `background-attachment-006/007`: diagnosed — the selftest's exact-#ff0000 scan counts `.outer`'s deliberate `border-top-color: red`, which the inner `10px dotted` border leaves visible between its dots. Needs a border/judge decision (6.I + 6.M), not a background painter change.
- `background-animated-001`: not in `H.txt` this batch; route still to be confirmed with 6.M.

## Handed out

- **6.M (judges/routes):**
  - `background-046/049/067/072/079/084` (WPT + official): the pass-condition route scrolls `{"to":"bottom-right"}` but the sentence is "when the box is scrolled down, the cat images move. Also, the cat in the left bottom corner is not cut off." Scrolling right removes the content's left-bottom tile from the scrollport, so no complete cat can sit at the crop's left edge; the route should be `{"to":"bottom"}` with `variant: left`.
  - `background-013/023`: `ImageTiled`'s `BestPeriod` caps the period at `region.Width / 2` (95 for a 196px box minus 3px inflate), but the tile is 98px wide → "the ink is not periodically tiled". Allow a period of at least `region.Width - 8` (one repeat) for these.
  - `background-028`: `grid-topright` uses the COLUMN period for the row window (`WindowDiff(rows, 0, p, p, p)`); the row period is 99, the column period 98, which is the giant profile diff. Use the measured row period, as `grid-bottom` already does.
  - `background-bg-pos-207` (WPT + official): scroll judge reports the purple box clipped by the viewport at (784,583)..(798,599); the case is a page-scroll target and the render is the full viewport, so the crop/edge rule needs review.
  - `background-root-101/102/103` (official html4 copies): the `setTimeout(test, 5)` class change has not run when the selftest snapshot is taken (the WPT twins, which carry `class="reftest-wait"`, now pass). The official selftest route needs a frame/timer pump before its final snapshot.
  - `background-attachment-006/007`: see the parked finding; judge/border decision.
- **6.J (fonts):** `background-position-001/002/091/092` (WPT + official, 8 cases) need the measured `ex` (Ahem's x-height); both suites still resolve it as 0.5em.
- **6.K (cascade):** `background-reset-001` (WPT + official): `background-color: red; background: bottom fixed` must reset the colour to transparent. The raw-background rescue merges the shorthand over existing longhands instead of resetting all of them first. Same file as the parked `data:` finding.
- **6.E (tables):** `background-position-applies-to-001a/002a`: the row-group image is 4px right and 3px low against the reference's explicit 93px/18px layout; the tbody/thead box comes out 4px wider (border-spacing) and the p margin 3px taller.
- **6.A/6.J (text/fonts):** `background-root-006/007` (WPT + official): the test and its reference wrap the same text differently (`* { margin:1em; padding:1em }` vs the reference's fixed 72px padding), so the tile grid and text bands differ; the background painter is not implicated.
- **6.X/6.K:** `background-20.xml` (other-formats): 465888 red pixels; the XML variant still paints red. Needs a first diagnosis before it can be assigned.

- `css/CSS2/backgrounds/background-attachment-006.xht`, `background-attachment-007.xht` — the WPT manual copy now
  runs the fail-closed selftest route and fails on visible red: 006's `.outer` red top border should be covered by
  the `-10px`-margin inner box, and 007's red background should be covered by the tiled image. Both are layout/paint
  coverage of the background layer, not the judge. Render evidence: `artifacts/ba6.png` in the 6.M worktree. From 6.M.

## Edits outside owned files

- `Lite.Conformance/Css21/SelfTestOracle.cs` (6.M) — commit `cbe1d649`: parse `scroll.then` into `ScrollSpec`. One-line route fix; 6.M owns the file.
- `Lite/Layout/StyleResolver.cs` + `Lite/Parser.Cascade.cs` (6.K) — commit `fd3c10fd`: `StoreProp` now marks every key it writes in `CascadeAppliedProps` (so a shorthand's parse-time longhands are retractable), and a winning shorthand covers its longhands in the initial-value fallback. Both files are 6.K's.
- `Lite/Drawer.cs` (shared, no lane owner) — commit `6eea0bea`: paint background images on all inline painting paths (`DrawBackgroundImageIn`).
- `Lite/Drawer.Text.cs` (6.A's text painting; the list-marker members belong to 6.F) — commit `6eea0bea`: `PaintListItem` paints the background image and gradient.

## Progress

### Batch 1 — 2026-11-20

**Moved: 58 fail → pass, 1 fail → manual-pending (was 0/116).**

- `cbe1d649` (route): background-119/121/125/127/131/133/143/145/149/151/155/157/167/169/173/175/179/181/191/193/197/199/203/205 — 24 WPT + 24 official.
- `fd3c10fd` (cascade): background-root-101, background-root-102 (WPT).
- `6eea0bea` (painting): background-image-applies-to-008 (official), background-image-applies-to-010 (WPT + official), background-repeat-applies-to-008 (WPT + official), background-repeat-applies-to-010 (official pass, WPT → manual-pending), background-position-applies-to-008 (WPT + official).

**Still failing (57) and why:** 12 route (`046/049/067/072/079/084`), 6 judge (`013/023/028`), 2 judge (`bg-pos-207`), 3 timer route (official `root-101/102/103`), 2 attachment border/judge, 8 `ex` (6.J), 14 SVG background decode, 2 reset shorthand (6.K), 4 `root-006/007` text wrap, 2 `position-applies-to-001a/002a` table geometry (6.E), 1 `background-20.xml`, 1 `root-103` (attribute-selector restyle: `html[lang]` change does not re-cascade `body`; `JsElement.OnAttributeChanged` only recascades `class`/`id`/`data-*`).

**Checks (batch head `6eea0bea`):** units 652 passed / 0 failed; css21 gate 51 passed / 0 failed / 1 expected-fail; wpt gate 86 / 0 / 2; lane H report `H-after.json` 58 pass + 1 manual-pending + 57 fail; canary `canary-after.json` 300/300.

### Batch 2 — 2026-11-21

- **Commit 1 (SVG background images, CSS 2.1 §14.2.1 / CSS Images 3 §5.3):** added `Lite/Media/SvgImage.cs`, cached fetched SVG documents in `ResourceLoader.FetchSvgImage`, and resolved vector concrete sizes and rasterization against the background positioning area in `Drawer.Backgrounds.cs`.
  - **Moved (14 fail → pass):** `background-intrinsic-001/002/003/006/007/008/009` (7 official + 7 WPT).
  - **Checks:** units 736 passed / 0 failed; css21 gate 51/1; wpt gate 86/2; `H.txt` 14/61 (7/30 official + 7/31 WPT); `H-pass.txt` 1274/1274 (694/694 official + 580/580 WPT); `canary.txt` 960/960 (492/492 official + 468/468 WPT).
