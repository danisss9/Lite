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

_(None this batch.)_

## Edits outside owned files

- `Lite.Tests/Css21CoverageTests.cs`: one unit test, `OfficialVariants_VendoredClaimsRequireTheBuildDirectory`,
  for the new `OfficialCatalog.MissingVendoredVariants` check (the file is not in the owned list; the
  edit is the test only and is in the catalog commit).

## Open decisions (recommendations; nothing recorded until the user rules)

### `c5504-mrgn-l-002` (official + WPT) — recommend **optional**

The case sets `margin-left: -2em; padding-left: 2em` and asks the reader to "scroll to the left and see
a green box". CSS 2.1 §11.1.1 leaves that to the UA: "Even if 'overflow' is set to 'visible', content
may be clipped to a UA's document window by the native operating environment", and for a viewport that
does overflow, "The behavior of the 'auto' value is user agent-dependent, but should cause a scrolling
mechanism to be provided for overflowing boxes." No CSS 2.1 sentence requires the scrolling mechanism to
expose negative-x content, so a UA that clamps the origin is conforming. Not defective: the margin
computation itself (negative margin shifts the box; the padding brings the content back) is covered by
the passing `c5504-mrgn-l-000/001/003` and `c5504-imrgn-l-*` family, and the assert here is about the
scroll mechanism, not the margin value. If the user prefers the defective route, the replacement would
have to be an applicable case sharing `css21.8.3` and covering the same negative-margin layout; the
`c5504-imrgn-l-*` cases are the nearest, but they assert inline margins, not a viewport scroll.

### inline-svg (4 WPT cases, `L.txt`) — recommend **keep applicable, hand to the Phase 6 engine queue**

All four render and differ from their references (the lane run reports "No reference path passed", i.e.
the comparison ran and every reference path mismatched; the references exist under
`css/CSS2/reference/`). Lite already has `Lite/Rendering/SvgRenderer.cs` and its layout treats `SVG` as
a replaced element (`BoxEngine.Inline.cs`), so this is a real engine gap, not a missing harness route:
§10.3.2 (inline replaced width) and §10.6.2 (inline replaced height) apply, while the tests' intrinsic
sizing and viewBox rules come from SVG 1.1 §IntrinsicSizing. `inline-svg-margin-padding-border` asserts
only §8.3–§8.5 box properties on a replaced element, so it is squarely CSS 2.1's box model even though
the element is SVG. Later-feature would be a weakening while the renderer exists.

### `background-animated-001` (official + WPT) — recommend **keep applicable; the existing review stands**

§14.2 requires the background image to be rendered and gives no sentence permitting a still frame in
place of an animated image. The 19 November project decision (animate GIFs; judge `image-animates`)
already implements it, and both copies pass on a filtered run (5/5 frames). Recommendation: record no
applicability change and keep `applicable` with the animated-image judge as its evidence. If the user
wants the exclusion instead, the permitting text would have to come from outside the pinned target, and
the plan's 19 November decision would be reversed.

### Rulings, 20 November

- **`c5504-mrgn-l-002` → optional.** Applied to both rows (`css/CSS2/css1/c5504-mrgn-l-002.xht`,
  `css21-official/html4/c5504-mrgn-l-002.htm`) with `permits` quoting §11.1.1; `media` and
  `requirementIds` are cleared (the case leaves the screen matrix), and the dated rationale records
  the scroll-mechanism reading and the sibling cases that carry the margin computation.
- **inline-svg → keep applicable, hand to the Phase 6 engine queue.** No applicability change. The
  four cases stay in `L.txt` (0/4) until the integrator's next lane split; the engine items are
  inline SVG intrinsic sizing, viewBox scaling and box properties (§10.3.2/§10.6.2 on a replaced
  element; SVG 1.1 §IntrinsicSizing supplies the sizing rules).
- **`background-animated-001` → keep applicable/implemented.** No change; the 19 November
  animated-image decision and the `image-animates` judge stand.

## Progress

- **20 November — the xhtml1 variant (one commit, no engine changes).**
  - **Readiness blocker cleared.** `scripts/restore-css21-xhtml1.py` rebuilds the 2011-03-23
    xhtml1 build from csswg-test@fa448700877 (the same pre-snapshot commit the support
    subtree and the .xht references came from): 9,595 test/reference documents and 392
    support files. The build selection is pub-css21.pl's directory/manifest list verbatim;
    it yields 9,418 tests, the published total (asserted by the script). 9,537 documents are
    proven byte-exact by the recovered XHTML->HTML build transform against the vendored
    html4 twin (doctype conversion, `xml:lang` rewrite, the Microsoft `../support/`
    rewrite, reference-link injection, and the html5lib `invisibleChars`/entity rules from
    `CSSTestLib/html5lib.patch`). The exceptions are recorded per file: 57 sources with no
    html4 twin (47 `nonHTML` tests that only the XHTML build carries, plus 10 the html4
    capture never recovered) are restored from the source blob verbatim, and one
    (`replaced-elements-001`) is identified by name/title/assert because html5lib's handling
    of a named entity in an `input` value attribute is not byte-reproducible. The 25
    `HTMLonly` tests are skipped, as the XHTML build skips them; generated index pages are
    not test evidence.
  - **Lock and gate.** `variantAvailability["xhtml1-screen"] = "vendored"`; the tree hash
    re-pins with the documented OrdinalIgnoreCase canonical rule
    (`e231e67f…` → `f4d0ff8e…`). `OfficialCatalog` now blocks a "vendored" claim whose
    variant directory is absent (`css21-official-variant-missing:<variant>`), so the lock's
    statement is checkable. The inventory reports `unavailableVariants: []`; the
    `css21-official-variant-unavailable:xhtml1-screen` blocker is gone. `unavailableTests`
    stays 54 (tests the executable html4/other builds do not carry); the variant is a second
    syntax for the same cases, not new cases, so the catalog total stays 9,364.
  - **Checks.** Units 648/648 (the WPT manifest provenance was regenerated for the new lock
    with `scripts/build-wpt-manifest.ps1`; the meta's `suiteLockSha256` now matches). Curated
    gates css21 51 pass / 1 published expected-fail, wpt 86 pass / 2. Lane list `L.txt`:
    0/4, unchanged (all four inline-svg cases still "No reference path passed"). Canary:
    300/300 (154 official + 146 WPT). Inventory: screen blockers 18,088 (13
    defective-without-passing-replacement, 21 implemented-without-evidence, 17,887
    missing-case, 10 missing-evidence, 157 unimplemented); print 577.
  - **Named finding for the integrator (not fixed here):** 25 vendored html4 artifacts are
    gzip *bodies* (Wayback stored the response compressed and the fetch did not
    decompress), e.g. `html4/block-in-inline-001.htm`, `html4/border-bottom-color-057.htm`,
    `html4/inline-table-*.htm`, `html4/outline-color-001/002.htm`, `html4/text-indent-011.htm`.
    The restore script decompresses them for the provenance proof and records
    `twinCapture: gzip`; a browser loading those bytes without `Content-Encoding` would see
    garbage, so the fetch path needs a decompression fix and a re-pin in a separate batch.
  - **Integrator:** the catalog was rebuilt locally against the new tree hash (9,364 cases).
    The next baseline must let `OfficialCatalog.Ensure()` rebuild the catalog again on the
    merged identity, and must not refetch the official tree (the fetch would drop xhtml1;
    the restore script is the documented post-fetch step).
  - **Open decisions:** see the section above; nothing was recorded.
- **20 November — the rulings (one commit, evidence only).**
  - `c5504-mrgn-l-002` (both suites) reclassified **optional** with the §11.1.1 permitting sentence;
    two cases leave the screen matrix (screen blockers 18,088 → 18,086; missing-case 17,887 →
    17,885). inline-svg and `background-animated-001` recorded as ruled with no applicability change.
  - **Checks:** units 648/648; curated gates css21 51 pass / 1 expected-fail, wpt 86 pass / 2;
    inventory 18,086 screen / 577 print; lane L 0/4 unchanged; canary 300/300.
