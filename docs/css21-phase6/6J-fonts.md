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

_(None yet.)_

## Progress

_(None yet.)_
