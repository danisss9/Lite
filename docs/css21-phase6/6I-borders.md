# 6.I Borders, outline and visual effects

Branch `css21/p6-i`, worktree `C:\Repos\Lite-p6-i`. Case list `Lite.Conformance/artifacts/lanes/I.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Drawer.Borders.cs
- Lite/Extensions/DrawCommandExtensions.cs

## Opens with (parked findings)

- Thin double sub-lines (width/3 is about 1.67px) and dotted duty cycles read wrongly in the ring judges. The engine paints them. Check each case's render before touching the painter, and hand judge-side issues to 6.M.

## Coordination

- The collapsed-border winners come from `TableEngine` (6.E). You own how they paint, not how they resolve.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

_(None yet.)_

## Progress

### 2026-11-20 — 3D border bevel shading on near-black colors (CSS 2.1 §8.5.3)
- **Moved (+2 WPT):** `css/CSS2/borders/groove-default.html`, `css/CSS2/borders/ridge-default.html`.
- **Change:** `Drawer.Borders.cs` (`LightenColor`) lifts near-black colors (`R+G+B < 96`) toward white so `groove`/`ridge` with the default black `border-color` paint visibly distinct light/dark halves instead of collapsing into solid black, while keeping pure-channel multiplicative scaling on hues. Added `BorderStyleTests.GrooveAndRidge_WithDefaultBlackColor_DifferFromSolid`.
- **Checks:** units 648/0; curated `css21` 51/1, `wpt` 86/2; lane `I` 0/72 official, 2/54 WPT; canary 300/300 (154/154 official, 146/146 WPT).
