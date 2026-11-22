# Lane V: visual: borders, outline and backgrounds (batch 3)

Merges 6.H ([6H-backgrounds.md](6H-backgrounds.md)) and 6.I ([6I-borders.md](6I-borders.md)). Branch `css21/b3-v`, worktree `C:\Repos\Lite\.claude\worktrees\b3-v`.
Case list `Lite.Conformance/artifacts/lanes/V.txt` (pass list `V-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Drawer.Backgrounds.cs
- Lite/Drawer.Borders.cs
- Lite/Extensions/DrawCommandExtensions.cs
- Lite/Network/ResourceLoader.cs
- Lite/Media/*

## Opens with (parked findings)

- Clusters: the engine half of `css/CSS2/borders`, `border-style-inset/outset`, `background-alpha`,
  `background-table`, `background-iframes`, `background-html-body`.
- Most failing border cases are judge misreads and sit on lane J's list. Fix only what the render gets
  wrong.
- `background-intrinsic-001..009`: `SKCodec` has no SVG codec, so SVG background images decode to
  null. This needs an SVG rasterizer reachable from `ResourceLoader.FetchImage` (6H).
- A `data:` URL in `background` is cut at its `;`. The raw-background rescue lives in
  `Parser.Cascade.cs`, so coordinate with lane S (6H).
- `background-attachment-006/007`: the exact-red scan counts `.outer`'s deliberate red border showing
  between the dots. This needs a border/judge decision with lane J (6H).
- `c5511..c5515`, `c5518`, `c5520`: border widths on cells and inline fragments (6C → 6I). The
  table-cell half may need lane Tb.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
