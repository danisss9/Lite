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

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

- `css/CSS2/cascade/html-precedence-003.xht` — the WPT manual copy now runs the fail-closed selftest route and fails:
  `<font color="green">` inside a red `div` paints red. The file's assert says the presentational `color` attribute
  trumps the author `div` rule and the user sheet's universal selector (§6.4.4 cascade order); the attribute seems
  ignored. Render evidence: `artifacts/hp3.png` in the 6.M worktree. From 6.M.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
