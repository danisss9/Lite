# 6.K Selectors, cascade and syntax

Branch `css21/p6-k`, worktree `C:\Repos\Lite-p6-k`. Case list `Lite.Conformance/artifacts/lanes/K.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Parser.Cascade.cs
- Lite/Layout/StyleResolver.cs
- Lite/Layout/PropertyTable.cs
- Lite.AngleSharp.Css/** (new fork patches take the next number in its README, with a regression test that fails on the pristine source)

## Opens with (parked findings)

- The 4-digit hex `#1000`, which CSS 2.1 must reject (`color-030`, `outline-color-030`).
- `default-attribute-selector`: a styled `<input>` keeps its UA chrome.
- `character-encoding-017/018/028`: the encoding-dependent selector never matches.

## Coordination

- `Parser.cs` (core) is shared with the HTML5 track through `JsDocument` and the DOM projection. Name any edit there in your notes, and say that the HTML5 gate was not rerun.

## Handed in

- `border-{top,right,bottom,left}-color-030` (official, 4) and `outline-color-030` (official, 1): 4-digit hex `#1000` is parsed as transparent instead of rejected as invalid CSS 2.1 color (already listed in your Opens with) (from 6.I).
- `border-width-010` (official+WPT, 2): `border: red solid -1px` has a negative border width and must drop the entire shorthand declaration during cascade so the prior green 8px solid border wins (from 6.I).
- `border-width-011/012` (official, 2) and `outline-width-096` (official, 1): `StyleResolver.EffectiveValue` passes the parent's raw width instead of computed `0px` when the parent's `border-style` is `none`/`hidden` (§8.5.1) or `outline-style` is `none` (§18.4). Note: for `border-width-011/012`, once `StyleResolver` writes `"0px"` into `StyleOverrides`, coordinate with 6.I if `GetBorderSideWidth` also needs to read inherited `"0px"` (from 6.I).

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
