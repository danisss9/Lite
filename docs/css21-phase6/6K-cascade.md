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

- `first-letter-selector-011/012/013/014` (official + WPT, from 6.F): a selector list that mixes a
  pseudo-element part with a normal part (`div p:first-letter, span { color: green; margin: 15px }`)
  is lifted whole by `TryExtractPseudoElementRule`/`NeutralizePseudoElementRules`, so the `span`
  part never reaches the cascade and the control line renders unstyled. The pseudo part matches,
  so only the non-pseudo parts of such lists need to stay in the cascade. These four cases are the
  first-letter ones 6.F cannot close without it.
- `before-after-001` (official + WPT, from 6.F): `div:before:after { content: 'FAIL FAIL' }` is an
  invalid nested pseudo-element and must generate nothing; the red text is painted.
- Dynamic CharacterData mutations do not re-project or regenerate generated boxes: a text node
  that owns a split `::first-letter` keeps its stale letter/tail after `data = ""`
  (`first-letter-nested-002`), and a counter-driven `:before` is not recomputed after
  `insertBefore` (`counter-increment-000/001/002`, `counter-reset-000/001/002`,
  `counter-increment-display-004`, `counters-root-000`). Dynamic pseudo/rule generation is parked
  to 6.K per the README; 6.F has not touched it.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
