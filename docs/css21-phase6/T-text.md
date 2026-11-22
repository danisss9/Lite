# Lane T: text: inline, line box and fonts (batch 3)

Merges 6.A ([6A-inline.md](6A-inline.md)) and 6.J ([6J-fonts.md](6J-fonts.md)). Branch `css21/b3-t`, worktree `C:\Repos\Lite\.claude\worktrees\b3-t`.
Case list `Lite.Conformance/artifacts/lanes/T.txt` (pass list `T-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Layout/BoxEngine.Inline.cs (except `ReorderForBidi` and its helpers: lane Bd)
- Lite/Layout/IntrinsicSizer.cs
- Lite/Layout/TextMeasure.cs (both the measuring helpers and the font/metrics API)
- Lite/Layout/FontRegistry.cs
- Lite/Layout/PlatformFonts.cs
- Lite/Drawer.Text.cs (text painting; the list-marker members belong to lane G)

## Opens with (parked findings)

- Clusters: `css/CSS2/text` (20), `css/CSS2/css1` (15), `linebox` (8), `text-align-white-space`.
- `c527-font-008` (both suites): `font: oblique normal 700 18px/200% sans-serif` paints 12px. The
  shorthand's size and line-height are not applied (6J).
- `font-family-rule-011` (both suites): unquoted `default` matches an installed family. It must be the
  reserved keyword and fall back (6J).
- `fonts-012`, `font-family-015`, `font-size-116` (metric residues).
- `font-matching-rule-009` needs the user's serif-default ruling. Do not change the default before it.
- `ex` must be the font's measured x-height, not 0.5em (`background-position-001/002/091/092`, from
  6H), and `ch` must be the '0' advance.
  - The `ch` fix was reverted in 6D because it exposed the nowrap-float bug (`float-nowrap-3/4/8/9`).
  - Agree with lane F before landing it.
- `table-height-algorithm-013/014`: the judge's ink-top read depends on the default font's ascent
  (6E → 6J).
- `content-174` (WPT): an overflowing nowrap `:before` background stops at 5em (6A).
- `border-001/003` (both suites): the WPT references' Ahem text with `word-spacing` mis-wraps (6A).

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
