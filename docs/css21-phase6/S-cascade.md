# Lane S: selectors, cascade and syntax (batch 3)

Merges 6.K ([6K-cascade.md](6K-cascade.md)). Branch `css21/b3-s`, worktree `C:\Repos\Lite\.claude\worktrees\b3-s`.
Case list `Lite.Conformance/artifacts/lanes/S.txt` (pass list `S-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Parser.Cascade.cs
- Lite/Layout/StyleResolver.cs
- Lite/Layout/PropertyTable.cs
- Lite.AngleSharp.Css/**: a new fork patch takes the next number in its README and comes with a
  regression test that fails on the pristine source

## Opens with (parked findings)

- Clusters: `syntax` (11), `cascade` (14), `selectors` (6), `html-attribute` (4).
- `html-precedence-003`: a presentational `<font color=green>` paints red, because the attribute loses
  (6M).
- `computed-float-position-absolute`: `getComputedStyle(...).float` is undefined (6B; routed here).
- `table-visual-layout-012`: a later, lower-specificity `background` shorthand beats a more specific
  `background-color`. The cause is AngleSharp's shorthand expansion order (6E).
- `first-letter-selector-011..014`: a selector list that mixes a pseudo-element part with a normal part
  is lifted whole, so the normal part never reaches the cascade (6F).
- `before-after-001`: `div:before:after` is invalid and must generate nothing (6F).
- Dynamic generated content (6F; coordinate with lane G):
  - the string form `setTimeout('run()', 0)` throws;
  - an inserted element, or a class change, needs its own generated box.
- The `data:` URL `;` split in the raw-background rescue (with lane V).

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
