# 6.I Borders, outline and visual effects

Branch `css21/p6-i`, worktree `C:\Repos\Lite-p6-i`. Case list `Lite.Conformance/artifacts/lanes/I.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Drawer.Borders.cs
- Lite/Extensions/DrawCommandExtensions.cs

## Opens with (parked findings)

- Thin double sub-lines (width/3 is about 1.67px) and dotted duty cycles read wrongly in the ring judges. The engine paints them. Check each case's render before touching the painter, and hand judge-side issues to 6.M.

## Coordination

- The collapsed-border winners come from `TableEngine` (6.E). You own how they paint, not how they resolve.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
