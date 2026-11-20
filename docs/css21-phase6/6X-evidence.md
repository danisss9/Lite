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

- `content-159` (official + WPT, from 6.F): served as XHTML, the `div:lang(en) { quotes: '"' '"'
  "'" "'" }` declaration is lost, so the pseudo content renders the default curly quotes instead
  of the straight ones the reference uses. `--render` (which serves .xht as HTML) matches the
  reference, so this is the XML stylesheet path, not 6.F's quote-depth logic.
- `first-letter-selector-000` (official + WPT, from 6.F): the test builds "TESTING" from seven
  floated `:first-letter` divs, one of which holds `<![CDATA[E]]>`; the CDATA box renders empty,
  so the actual is "TSTING". The HTML twin passes.

## Edits outside owned files

_(None yet.)_

## Progress

_(None yet.)_
