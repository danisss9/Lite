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

- `css/CSS2/css1/c527-font-008.xht` (and the official `c527-font-008.htm`) — `.nine { font: oblique normal 700 18px/200% sans-serif; }` paints every run at 12px: the `font` shorthand's size (and the 200% line-height) is not applied. The judge now reads the instruction paragraph's own runs and reports "every painted run is 12px, the sentence states 18px". From 6.M.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
