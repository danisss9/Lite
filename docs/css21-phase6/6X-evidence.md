# 6.X Evidence, exclusions and the xhtml1 variant (no engine changes)

Branch `css21/p6-x`, worktree `C:\Repos\Lite-p6-x`. Case list `Lite.Conformance/artifacts/lanes/L.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite.Conformance/Css21/css21-applicability.json (sole writer)
- Lite.Conformance/Css21/OfficialCatalog.cs
- scripts/restore-css21-*.py
- the suite lock and catalog, re-pinned per the documented OrdinalIgnoreCase rule

## Opens with (parked findings)

- The readiness blocker `css21-official-variant-unavailable:xhtml1`. Restore the xhtml1 variant from the pinned pre-snapshot csswg-test commit with a byte-exact build-transform proof (as `restore-css21-xht-refs.py` does for the references). Otherwise argue a reviewed equivalence that the gate can check.
- Open decisions, which the user rules on (record them only after the user decides): `c5504-mrgn-l-002` (defective with a passing replacement, else optional), inline-svg (4 WPT cases: your case list `L.txt`), and `background-animated-001`.
- Reclassification candidates handed in by other lanes: each needs its permitting or contradicting sentence quoted.

## Coordination

- A reclassification can only follow a reviewed spec reading. It never exists to make a number move.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

- `css/CSS2/cascade/inherit-computed-001.html` — CSS 2.2 change (links `http://www.w3.org/TR/CSS22/changes.html#s.6.2.1`): tests `em { color: red; border: inherit }` inheriting `p`'s resolved border color rather than `color`. CSS 2.1 §8.5.2 specifies `border-color` initial value as `"the value of the 'color' property"` and Computed value as `"the computed value is the same as the specified value"`, which contradicts the CSS 2.2 computed-value inheritance requirement. — from 6.A
- `css/CSS2/visudet/inline-block-baseline-003.xht`..`006.xht` — CSS 2.2 proposal (links `http://www.w3.org/TR/CSS22/changes.html#s.10.8.1`): asserts that the baseline of an `inline-block` with non-`visible` `overflow` is the higher of its bottom margin edge or its last line box's baseline. CSS 2.1 §10.8.1 explicitly contradicts this: `"The baseline of an 'inline-block' is the baseline of its last line box in the normal flow, unless it has either no in-flow line boxes or if its 'overflow' property has a computed value other than 'visible', in which case the baseline is the bottom margin edge."` — from 6.A
- `vertical-align-sub-001` & `vertical-align-super-001` (official + WPT) — antialiasing fringe over identical-coordinate red overdraw: both `#span1` (red) and `#span2` (green, `vertical-align: sub/super`) are `position: absolute` (blockified per §9.7, so §10.8.1 `vertical-align` does not apply and does not alter `font-size`); green glyphs paint at the exact same coordinates over red glyphs, leaving sub-pixel alpha-blended red fringe pixels compared against the single-layer green reference (`vertical-align-sub-001-ref.xht`). — from 6.A
- `css21-official/html4/text-indent-intrinsic-003.htm` & `004.htm` — defective 20110323 official `-ref.htm` (`width: 0` and `width: 1em` for the `&#x200B;` lines where the second line is `1em` / `3em`); WPT fixed both references to `1em` and `3em` and both WPT twins (`text-indent-intrinsic-003.xht`, `004.xht`) pass. — from 6.A

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
