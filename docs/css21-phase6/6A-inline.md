# 6.A Inline formatting and the line box

Branch `css21/p6-a`, worktree `C:\Repos\Lite-p6-a`. Case list `Lite.Conformance/artifacts/lanes/A.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Layout/BoxEngine.Inline.cs
- Lite/Layout/IntrinsicSizer.cs
- Lite/Layout/TextMeasure.cs (measuring helpers only; the font/metrics API belongs to 6.J)
- Lite/Drawer.Text.cs (text painting; the list-marker members belong to 6.F)

## Opens with (parked findings)

- `c5502-imrgn-r-003/005/006` and `c5504-imrgn-l-003/005/006`: the layout matches; what remains is a 1px offset on the reference's "I" bar, which depends on the default font's space width.
- Justified, pre-wrap and pre-line text still use the single wrapped item, because the painter justifies across the lines of one item. Per-line items for these would unify the paths.
- Classification candidates for 6.X: `inherit-computed-001`, `inline-block-baseline-003..006` (a CSS 2.2 proposal) and `vertical-align-sub/super-001` (an antialiasing fringe).

## Coordination

- 6.G (bidi) starts after this lane's first merge and reshapes the line assembly for the visual order. Keep `ReorderForBidi` working and tell 6.G what changed in the line assembly.
- 6.F owns the marker painting inside `Drawer.Text.cs`.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

- `Lite/Parser.cs` (1-line fix in `Traverse` mixed-children loop: call `NormalizeTextWhitespace(textNode.Data, ws)` instead of `CollapseWhitespace(textNode.Data)`, matching the leaf-text path at line 1078, so `white-space: pre`/`pre-wrap`/`pre-line` preserves newlines and spaces in mixed-children elements).

## Progress

### 2026-11-20 — Commit 2: §16.6 preserve white-space on mixed-children text nodes (`Parser.cs`, edit outside owned files)
- **Moved (+4: 2 official, 2 WPT, 0 losses; cumulative +10: 5 official, 5 WPT):**
  - `c562-white-sp-000` (official + WPT)
  - `word-spacing-characters-001` (official + WPT)
- **What changed:**
  - `Lite/Parser.cs` (1 line): mixed-children `#text` nodes now normalize via `NormalizeTextWhitespace(textNode.Data, ws)` instead of `CollapseWhitespace(textNode.Data)`.
- **Still failing (pre/mixed):** `white-space-mixed-001..003` (still needs mixed `normal`/`pre`/`nowrap` boundary handling), `text-indent-intrinsic-001..004` (addressed next in Commit 3 via `IntrinsicSizer.cs`).
- **Handed to other lanes:** None yet.
- **Checks:** Units 649/649; `css21` 51/1; `wpt` 86/2; Lane A 5/74 official, 5/90 WPT (+4, 0 losses); Canary 300/300 (154/154 official, 146/146 WPT).

### 2026-11-20 — Commit 1: §16.6.1 collapsible whitespace across inline boundaries and empty inline struts
- **Moved (+6: 3 official, 3 WPT, 0 losses):**
  - `white-space-collapsing-004` (official + WPT)
  - `white-space-collapsing-005` (official + WPT)
  - `white-space-normal-008` (official + WPT)
- **What changed:**
  - Restored parser-trimmed boundary spaces on leaf inline elements in `CollectInlineItems` (`BoxEngine.Inline.cs`) so the first inline keeps its own trailing space (and background) while the line loop removes consecutive/line-edge spaces.
  - Fixed the inner-loop `continue` in `LayoutInlineRun` that failed to drop a collapsed whitespace-only item after a preceding collapsible space.
  - Treated `InlineItemKind.Strut` (empty inline) as transparent to `atLineStart` and consecutive-space collapsing, used CSS whitespace checks (`IsCssSpace`) instead of `char.IsWhiteSpace` (preserving U+00A0/U+2000), trimmed `Text` when stripping trailing line-end space, and allowed surviving non-zero-width spaces in `AccumulateWrapperFragment`.
- **Still failing (white-space):** `white-space-normal-007` (reduced from 2400 to 1600 red px; remaining cause is parser trimming whitespace-only `<span class="green"> </span>` to empty `Text` without setting `TrimmedLeadingSpace`/`TrimmedTrailingSpace`), `white-space-normal-004` (`&#xFEFF;` + `&nbsp;`), `white-space-mixed-001..003`, `white-space-processing-042` (tab stops), `white-space-processing-054` (en quad).
- **Handed to other lanes:** None yet.
- **Checks:** Units 648/648; `css21` 51/1; `wpt` 86/2; Lane A 3/74 official, 3/90 WPT (+6, 0 losses); Canary 300/300 (154/154 official, 146/146 WPT).
