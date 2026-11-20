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

- `first-line-selector-016` (official + WPT) — `span, div:first-line { color: white; }` is neutralized as a whole in `Parser.Cascade.cs`, leaving `<span>FAIL</span>` inheriting `div`'s `color: red` (40 red px). — from 6.A

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
