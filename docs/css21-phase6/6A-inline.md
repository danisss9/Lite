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

- `border-001`, `border-003` (official+WPT, 4): the bordered test box renders pixel-perfectly; the WPT reference (`border-001-ref.xht` / `border-003-ref.xht`) uses Ahem text with `word-spacing` which mis-wraps in inline layout (from 6.I).

## Edits outside owned files

- `Lite/Extensions/DrawCommandExtensions.cs` (6.I's file; batch 2, `e4b06e76`): `GetLineHeight` resolves an ANCESTOR's em/ex/%/calc `line-height` against that ancestor's own font-size, so the computed absolute length is what descendants inherit (§10.8.1, §6.1.2). I-pass 2475/2475, J-pass 519/519.
- `Lite/Drawer.cs` (inline fragment painting, no named owner; nearest is 6.H; batch 2, `7de89196`): anonymous `#text` boxes no longer paint their host element's `background-color` over their line fragments (§9.2.1.1, §6.2.1); block-in-inline pieces keep the old paint. H-pass checked.
- `Lite/Parser.cs` (1-line fix in `Traverse` mixed-children loop: call `NormalizeTextWhitespace(textNode.Data, ws)` instead of `CollapseWhitespace(textNode.Data)`, matching the leaf-text path at line 1078, so `white-space: pre`/`pre-wrap`/`pre-line` preserves newlines and spaces in mixed-children elements).

## Progress

### 2026-11-21 — Batch 2: line-height inheritance, anonymous-box backgrounds, aligned subtrees
Three commits (`e4b06e76`, `7de89196`, and the aligned-subtree commit after them).
- **`e4b06e76` §10.8.1/§6.1.2, relative `line-height` inherits as the computed length** (`DrawCommandExtensions.cs`, outside owned files): an ancestor's `1em`/`200%` resolved against the descendant's font. c548-ln-ht-003/004 drop from 4400 to 1600 red px, and nothing flips on this commit alone. Unit `LineHeight_RelativeLengthInheritsAsComputedPixels`.
- **`7de89196` §9.2.1.1/§6.2.1, anonymous inline boxes paint no background** (`Drawer.cs`, outside owned files): the text child repainted the host's red `background-color` over the block's background image. **Moved:** `c548-ln-ht-003`, `c548-ln-ht-004` (official + WPT), fail → pass. Unit `AnonymousText_DoesNotPaintHostBackground`.
- **Aligned subtrees, §10.8.1** (`BoxEngine.Inline.cs`): a bare `#text` takes the initial `vertical-align: baseline` (§9.2.1.1) unless a value is declared on the text itself. A baseline-aligned item inside a `top`/`bottom` inline ancestor hangs on that ancestor's baseline. The host's reach is the tallest of its hanging items, at least its own strut, and it is placed from the line-box edge the host aligns to (`BaselineAnchorHost`, `InlineStrut`). Other non-baseline ancestors (middle, sub, length) keep the item-level alignment. **Moved:** `css/CSS2/linebox/anonymous-inline-inherit-001.html` fail → pass (won back). Unit `VerticalAlignTop_TextHangsOnHostBaseline`.
- **A.txt:** 5 pass / 120 fail (was 0/125). Official 2/59, WPT 3/66. 0 losses.
- **Still failing, and why:**
  - css1 "HI" cases `c5502-imrgn-r-003`, `c5504-imrgn-l-003`, `c5507-ipadn-r-003`, `c5509-ipadn-l-003` (official + WPT): the shared reference places the I bar 25px plus one default-font space after the H. Our default serif's space is 4px, and the Ahem test needs 5px (I bar at 142 vs 143). This is font-dependent, so it is left parked.
  - css1 image-row 1px shifts: `c5502-imrgn-r-005/006`, `c5504-imrgn-l-005/006`, `c5507-ipadn-r-004`, `c5509-ipadn-l-004`, `c5510-ipadn-000`, `c5508-ipadn-b-003`. The reference's image line sits 1px higher, because its strut descent below a 15px image is 3px where Blink's is 4px. Blink rounds ascent and descent to whole pixels and gives the floored half-leading to the ascent and the rest to the descent. Lite centres unrounded metrics (`TextMeasure.ComputeAscent`, `GetLineHeight` 'normal' = ascent+descent without line gap). Changing this touches 6.J's metrics API and every line, so it needs a coordinated change with 6.J. It was not done here.
  - `line-height-125`: the inheritance is now right (10px), but the case still fails. Not yet diagnosed.
  - `vertical-align-negative-leading-001`: `top`/`bottom` boxes with negative leading (line-height 10px, 30px font) place the glyphs off.
  - `vertical-align-baseline-003`: an inline-block's baseline must be its LAST line box (§10.8.1); we align on the first.
  - `line-height-oof-descendants-001`: an empty abs/float/fixed span gives a `line-height: 0` line real height. The reference also renders wrong (0-height paragraphs collapse through).
  - `line-height-bleed-002`: the `::first-letter` box under `line-height: 0` is placed above the rest.
  - `vertical-align-applies-to-007`: a `vertical-align: bottom` table cell keeps its text at the top (§17.5.3; 6.E's area).
  - `line-height-203/206`: web-font (Revalia/AD woff) ascent/descent (6.J's area).
  - `fonts-012` (per-run mixed-font fallback, handed in from 6.J): not started in this batch.
  - The rest are unchanged from batch 1: `::first-line`, `text-align-white-space`, `text-indent`, `text-transform`, `white-space-mixed/normal/processing-054`, `letter-spacing-080/102`, `word-spacing-justify-001`, and the 6.X/6.M candidates listed above.
- **Handed to other lanes:** a line-assembly note to 6.G (`6G-bidi.md`). No cases moved.
- **Checks (on the final tree):** build 0 errors. Units 738/738. css21 51/1. wpt 86/2. `A-pass.txt` 1405/1405. `canary.txt` 960/960. `G-pass.txt` 223/223. `I-pass.txt` 2475/2475. `H-pass.txt` 1274/1274. `J-pass.txt` 519/519 (on `e4b06e76`).

### 2026-11-20 — Fix round: §16.6.1 line-end space before closing inline edges
- **Losses from the batch-1 baseline, both caused by commit 1 (`5e148b2c`):** `css21-official/html4/block-in-inline-append-002.htm` and `css/CSS2/borders/border-color-012.xht`. Commit 1 restores the parser-trimmed boundary spaces on leaf inline boxes, so `...text </span></span>` now ends its line with a real space. The line-end trim only fired when that text was the line's LAST placed item, and here closing inline edges follow it, so the space stayed and the wrapper borders reached one space past the glyphs. (In `block-in-inline-append-002` it was the static reference that grew the space; the DOM-built test has none.)
- **Fix:** `CommitLine` first removes a trailing collapsible space from the line's last content run when only inline edges, out-of-flow markers, floats, struts or collapsed whitespace follow it, and shifts those trailing items left by the removed width. The case where the run is itself the last item keeps its existing path.
- **Moved:** both losses pass again; also new passes `inline-box-001` (official + WPT, in A.txt) and `css/CSS2/normal-flow/block-in-inline-append-002.xht` (WPT, in C.txt; it failed at base).
- **Gains kept:** all 24 earlier lane gains hold. No gain had to go.
- **Checks:** units 653/653 (new `WhiteSpaceCollapse_LineEndSpaceBeforeClosingEdgesIsRemoved`, which fails without the fix); css21 51/1; wpt 86/2; `fix-losses.txt` 2/2; `A.txt` 26 pass / 134 fail / 4 manualpending (was 24/136/4); `A-pass.txt` 1358/1358; `canary.txt` 960/960.

### 2026-11-20 — Commit 5: §16.6.1 8-space tab stops measured from containing block's starting content edge
- **Moved (+2 pass, 0 losses; cumulative +24 pass, +4 fail→manualpending):**
  - `white-space-processing-042` (official + WPT)
- **What changed:**
  - `Lite/Layout/TextMeasure.cs`: added `NextTabStop(offsetFromLineStart, font)` (multiples of `8 * spaceWidth`) and updated `MeasureWithSpacing` (with optional `lineStartOffset`) and `SplitPreserved` to measure U+0009 tabs accurately.
  - `Lite/Layout/BoxEngine.Inline.cs`: re-measure mid-line text items containing `\t` at their actual `lineX - bandLeft` offset from the block's starting content edge (keeps `word-spacing-characters-001` passing alongside `white-space-processing-042`).
  - `Lite/Drawer.Text.cs`: draw `\t` segments aligned to 8-space tab stops measured from the containing block's `ContentBox.Left`.
- **Coordination note for 6.G (bidi):** `ReorderForBidi` was not altered and remains intact; line assembly changes in `BoxEngine.Inline.cs` were limited to §16.6.1 boundary-space restoration/collapsing, empty inline `Strut` transparency, mid-line tab re-measurement, and `FirstLineStylesOf` scoping.
- **Handed to other lanes:**
  - 6.X (`6X-evidence.md`): `inherit-computed-001`, `inline-block-baseline-003..006`, `vertical-align-sub-001`, `vertical-align-super-001`, and official `text-indent-intrinsic-003/004` (20110323 `-ref.htm` bug; WPT twins pass).
  - 6.M (`6M-judges.md`): `text-align-applies-to-007/012/013/015` (judge checks viewport edge 0/800 instead of table/body content edge 8/792).
  - 6.K (`6K-cascade.md`): `first-line-selector-016` (`span, div:first-line { color: white; }` neutralized as a whole in `Parser.Cascade.cs`).
- **Checks:** Units 652/652; `css21` 51/1; `wpt` 86/2; Lane A 11/74 official (+11 pass, +2 manualpending vs before), 13/90 WPT (+13 pass, +2 manualpending vs before), 0 losses; Canary 300/300 (154/154 official, 146/146 WPT). Note: `Parser.cs` was edited in Commit 2 (HTML5 gate not rerun per lane rules).

### 2026-11-20 — Commit 4: §5.12.1 ::first-line scoping across preceding blocks, `<br>`, and inline-blocks
- **Moved (+4 pass, +4 fail→manualpending, 0 losses; cumulative +22 pass, +4 fail→manualpending):**
  - `first-line-selector-010` (official + WPT): fail → pass
  - `first-line-selector-011` (official + WPT): fail → pass
  - `first-line-pseudo-002` (official + WPT): fail (711 red px) → manualpending (0 red px)
  - `first-line-pseudo-005` (official + WPT): fail (711 red px) → manualpending (0 red px)
- **What changed:**
  - `Lite/Layout/BoxEngine.Inline.cs`: updated `FirstLineStylesOf` to verify via `IsOnParentsFirstLine` that no preceding block sibling, `<br>`, or preserved newline already consumed/ended the ancestor's first formatted line, and to propagate `appliesToAllLines = true` when descending through an atomic inline (`inline-block`/`inline-table`/`inline-flex`) sitting on the ancestor's first formatted line.
  - `Lite/Drawer.Text.cs`: applied ancestor `::first-line` inherited styles across all lines of an atomic inline when `appliesToAllLines` is true.
- **Still failing (`::first-line`):** `first-line-selector-016` (dropped from 89 to 40 red px; remaining 40 red px are `<span>FAIL</span>` because `Parser.Cascade.cs` neutralizes the comma-separated `span, div:first-line` rule — handing to 6.K); `first-line-selector-007/008` (`font` shorthand / `px` `font-size` / `letter-spacing` / `text-decoration` in `::first-line`); `first-line-selector-001` (antialiased green color check `#ff219021`); `first-line-000`, `first-line-floats-002`, `first-line-pseudo-012/013`.
- **Handed to other lanes:** `first-line-selector-016` to 6.K (`span, div:first-line` selector-list neutralization in `Parser.Cascade.cs`).
- **Checks:** Units 651/651; `css21` 51/1; `wpt` 86/2; Lane A 10/74 official (+2 pass, +2 manualpending), 12/90 WPT (+2 pass, +2 manualpending), 0 losses; Canary 300/300 (154/154 official, 146/146 WPT).

### 2026-11-20 — Commit 3: §16.1 + §10.3.5 intrinsic sizing for negative text-indent, preserved newlines, U+200B, and leaf inline edges
- **Moved (+8: 3 official, 5 WPT, 0 losses; cumulative +18: 8 official, 10 WPT):**
  - `text-indent-intrinsic-001` (official + WPT)
  - `text-indent-intrinsic-002` (official + WPT)
  - `text-indent-intrinsic-003` (WPT)
  - `text-indent-intrinsic-004` (WPT)
  - `white-space-mixed-003` (official + WPT)
- **What changed:**
  - `Lite/Layout/IntrinsicSizer.cs`: allow signed (negative) `text-indent` on the first line's min/max intrinsic widths (clamping min at 0 and keeping `max >= min`); split preserved newlines (`pre`/`pre-wrap`/`pre-line`) into forced breaks in `FlattenInline`; restrict the space-width fallback in `TextMinMax` to CSS whitespace-only leaves so U+200B stays zero-width; include horizontal margin/border/padding on leaf inline elements in `FlattenInline`.
- **Still failing (`text-indent-intrinsic`):** `css21-official/html4/text-indent-intrinsic-003.htm` and `004.htm` fail only because the 20110323 official snapshot's `-ref.htm` files had an upstream ref bug (`width: 0` / `width: 1em` instead of `1em` / `3em` for the `&#x200B;` lines) that WPT later fixed — both WPT twins now pass.
- **Handed to other lanes:** Note `text-indent-intrinsic-003/004` official ref discrepancy for 6.X.
- **Checks:** Units 650/650; `css21` 51/1; `wpt` 86/2; Lane A 8/74 official, 10/90 WPT (+8, 0 losses); Canary 300/300 (154/154 official, 146/146 WPT).

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
