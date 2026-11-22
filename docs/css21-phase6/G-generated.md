# Lane G: generated content and lists (batch 3)

Merges 6.F ([6F-generated.md](6F-generated.md)). Branch `css21/b3-g`, worktree `C:\Repos\Lite\.claude\worktrees\b3-g`.
Case list `Lite.Conformance/artifacts/lanes/G.txt` (pass list `G-pass.txt`). The
[README](README.md) has the rules every lane follows. The full diagnoses of the batch-1/2 items below
are in the archived lane notes linked above.

Scope: the lane's **failing** cases only. Manual-pending and unsupported cases are out of batch-3 scope.

## Owned files

- Lite/Parser.GeneratedContent.cs
- Lite/Layout/CssCounterStyles.cs
- Lite/Drawer.Text.cs: the list-marker members only (`PaintListItem`, `GetMarkerText`,
  `DrawBulletMarker`, `IsInsideOrderedList`, `GetOrderedIndex`)

## Opens with (parked findings)

- Clusters: `generated-content` (31), `lists` (14), `first-letter-selector`, `counter-increment`,
  `content-counters`.
- An empty list item's U+FEFF marker line must survive font and bidi changes; both integrations hit
  this regression. Keep `list-style-position-applies-to-008` and the empty-item cases on your pass
  list.
- `first-letter-quote-002/004/005`: the first letter starts at punctuation in an earlier box. The split
  was written but not landed, because it turned `first-letter-dynamic-001`'s reference green
  (6F → 6K).
- Dynamic generated boxes and counters (`before-after-dynamic-restyle-001`, `counter-increment-000..002`,
  `counter-reset-000..002`) need lane S's cascade and the string form of `setTimeout`. Coordinate
  with S.
- `content-159` (a `quotes` declaration lost when the page is served as XHTML) and
  `first-letter-selector-000` (an empty CDATA box) are the XML stylesheet and DOM path. Check them
  with lane S.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

## Progress
