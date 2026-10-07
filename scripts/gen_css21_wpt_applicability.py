#!/usr/bin/env python3
"""One-shot per batch: record the WPT css/CSS2 applicability review in
Css21/css21-applicability.json (complementary evidence per the plan).

Run after scripts/gen_css21_section_review.py. For each of the 9,290 WPT
candidates the test source is parsed for rel=help citations and flags (any
attribute order, both quote styles — the earlier order-and-quote-specific
regex silently missed the single-quoted i18n suites):

- Citations into /TR/CSS21/, /TR/CSS2/ or /TR/CSS22/ resolve through the same
  REC anchor map as the official suite (2.2 re-publishes the same anchors; a
  CSS22 citation is recorded as such in the rationale, not silently swapped:
  the plan requires adopted corrections to be reviewed individually before the
  target changes).
- flag "may" cases are optional (specification-permitted).
- Cases citing only later specifications (css-cascade-3/4, css-fonts-3/4,
  css3-page, css-writing-modes-3, ...) are later-feature; REC-CSS1-only or
  mixed citations are later-feature with the citations recorded.
- print-reftest cases are media ["print"]; everything else ["screen"].
- Cases without citations fall to a curated cluster table (crashtests map to
  the error-handling obligation; *interpolation* cases are later-feature
  transitions tests); anything else stays unreviewed for an individual batch.

Existing reviewed entries win; reviewComplete flips only when both the official
and WPT catalogs have zero unclassified cases.
"""
import collections
import json
import re
from pathlib import Path

import io
from zipfile import ZipFile

REVIEW_DATE = '2026-10-07'
zip_path = Path('Lite.Conformance/vendor/css21-spec-20110607/css2.zip')
wpt_root = Path('Lite.Conformance/vendor/wpt')
sections_path = 'Lite.Conformance/Profile/css21-sections.json'
requirements_path = 'Lite.Conformance/Profile/css21-requirements.json'
inventory_artifact = 'Lite.Conformance/artifacts/css21-inventory.json'
applicability_path = 'Lite.Conformance/Css21/css21-applicability.json'
anchor_map_cache = Path('Lite.Conformance/artifacts/css21-anchor-map.json')

REC_PREFIX = re.compile(r'^https?://[^/]+/TR/(CSS21|CSS2|CSS22)/', re.I)
WGDRAFT_PREFIX = re.compile(r'^https?://drafts\.csswg\.org/css2/', re.I)
link_tag_re = re.compile(r'<link\b[^>]*>', re.I)
help_rel_re = re.compile(r'rel\s*=\s*["\']?help\b', re.I)
href_attr_re = re.compile(r'href\s*=\s*(["\'])([^"\']+)\1', re.I)
flag_re = re.compile(r'<meta\s+name=["\']flags["\']\s+content=["\']([^"\']*)["\']', re.I)


def extract_helps(text: str) -> list[str]:
    """rel=help hrefs in any attribute order and both quote styles. The earlier
    single regex (rel before href, double quotes only) silently missed the
    single-quoted i18n suites and reversed-attribute links."""
    helps = []
    for tag in link_tag_re.findall(text):
        if help_rel_re.search(tag):
            m = href_attr_re.search(tag)
            if m:
                helps.append(m.group(2))
    return helps

head_re = re.compile(r'<h([2-6])[^>]*>(.*?)</h\1>', re.S | re.I)
anchor_re = re.compile(r'<a\s+name="([^"]+)"', re.I)
clause_re = re.compile(r'^\s*(?:appendix\s+)?([0-9]+(?:\.[0-9]+)*|[A-Z](?:\.[0-9]+)*)\.?\s', re.I)


def build_anchor_map() -> dict[str, str]:
    if anchor_map_cache.exists():
        return json.loads(anchor_map_cache.read_text(encoding='utf-8'))
    archive = ZipFile(io.BytesIO(zip_path.read_bytes()))
    amap: dict[str, str] = {}
    for name in archive.namelist():
        if not name.endswith('.html'):
            continue
        html = archive.read(name).decode('utf-8', 'ignore')
        heads = []
        for h in head_re.finditer(html):
            m = clause_re.match(re.sub(r'<[^>]+>', ' ', h.group(2)))
            if m:
                heads.append((h.start(), m.group(1)))
        if not heads:
            continue
        for a in anchor_re.finditer(html):
            prev = [cl for pos, cl in heads if pos < a.start()]
            if prev:
                amap[f'{name}#{a.group(1)}'.lower()] = prev[-1]
    anchor_map_cache.write_text(json.dumps(amap, indent=1, sort_keys=True), encoding='utf-8')
    return amap


def curated(source: str, helps: list[str], text: str | None = None) -> dict | None:
    """Cluster table for citation-less and unresolvable-citation cases."""
    import gen_css21_official_applicability as official
    name = source.rsplit('/', 1)[-1]
    # Numbered test names strip their number; unnumbered ones still lose the
    # extension (the old stem kept e.g. "-crash.html" and matched nothing).
    stem = re.sub(r'[-_]?\d+[a-z]?(\.xht|\.htm|\.html)$', '', name)
    stem = re.sub(r'\.sub$', '', re.sub(r'\.(xht|htm|html)$', '', stem))
    if source in FILE_OVERRIDES:
        return dict(FILE_OVERRIDES[source])
    if '/crashtests/' in source:
        return dict(classification='applicable', media=['screen'], ids=['css21.3.3'],
                    why='crashtest: malformed input must not crash the engine (error conditions)')
    if re.search(r'-ref\d*$', stem):
        return dict(classification='informative', media=[], ids=[],
                    why=f'reference/support document paired with {stem}; not an independent assertion')
    if 'crash' in stem.split('-') or stem.endswith('-crash'):
        return dict(classification='applicable', media=['screen'], ids=['css21.3.3'],
                    why='crashtest: malformed input must not crash the engine (error conditions)')
    if 'should not crash' in (text or ''):
        return dict(classification='applicable', media=['screen'], ids=['css21.3.3'],
                    why='titled as a non-crash assertion; error-condition robustness (3.3) '
                        'with the layout pinned by the reference')
    if stem in CURATED_OPTIONAL_CLAUSES:
        clause = CURATED_OPTIONAL_CLAUSES[stem]
        return dict(classification='optional', media=[], ids=[],
                    why=f'cites only the specification-permitted system colors of clause {clause}')
    # Errata-derived tests are named after the clause they pin (s-11-1-1b-001 -> 11.1.1).
    if '/css21-errata/' in source:
        m = re.search(r'\bs-(\d+(?:-\d+)*)', stem)
        if m:
            clause = m.group(1).replace('-', '.').rstrip('.')
            return dict(classification='applicable', media=['screen'], ids=[f'css21.{clause}'],
                        why=f'errata-derived test named after clause {clause}')
    if 'interpolation' in stem or 'animation' in stem or 'transition' in stem:
        return dict(classification='later-feature', media=[], ids=[],
                    why='interpolation/animation behavior defined by later CSS specifications')
    if 'opacity' in stem or ('stacking' in stem and 'scroll' in stem):
        return dict(classification='later-feature', media=[], ids=[],
                    why='opacity/compositing-driven stacking is defined by later CSS specifications')
    if stem.endswith('-paint-order'):
        return dict(classification='applicable', media=['screen'], ids=['css21.E.2'],
                    why='Appendix E.2 painting order with replaced/atomic inline-level content; '
                        'the element kinds are later HTML features but the stacking obligation is E.2')
    table = official.STEM_CLAUSES
    # Try the full stem, then progressively shorter dash-prefixes (cascade-import-dynamic -> cascade-import).
    parts = stem.split('-')
    for cut in range(len(parts), 0, -1):
        candidate = '-'.join(parts[:cut])
        if candidate in table:
            clause, why = table[candidate]
            return dict(classification='applicable', media=['screen'],
                        ids=[f'css21.{clause}'],
                        why=f'no resolvable anchor; classified by test cluster "{candidate}" ({why})')
    for h in helps:
        if REC_PREFIX.match(h) or WGDRAFT_PREFIX.match(h):
            frag = h[h.find('#'):] if '#' in h else ''
            if frag in DRAFT_FRAGMENT_CLAUSES:
                clause, why = DRAFT_FRAGMENT_CLAUSES[frag]
                return dict(classification='applicable', media=['screen'],
                            ids=[f'css21.{clause}'],
                            why=f'cites the CSS2 editor\'s draft anchor {frag} for {why}')
    return None


def main() -> int:
    amap = build_anchor_map()
    artifact = json.loads(Path(inventory_artifact).read_text(encoding='utf-8'))
    cases = [c for c in artifact['cases'] if c['source'].startswith('css/CSS2/')]
    sections = {s['clause']: s['classification'] for s in
                json.loads(Path(sections_path).read_text(encoding='utf-8'))['sections']}
    obligation_ids = {r['id'] for r in
                      json.loads(Path(requirements_path).read_text(encoding='utf-8'))['requirements']}
    applicability = json.loads(Path(applicability_path).read_text(encoding='utf-8'))
    existing = {t['path']: t for t in applicability['tests']}

    reviews, unreviewed, counts = [], [], collections.Counter()
    for case in cases:
        path = case['path']
        if path in existing and existing[path]['classification'] != 'unreviewed':
            reviews.append(existing[path])
            counts[existing[path]['classification']] += 1
            continue
        source = Path(str(wpt_root)) / path
        text = source.read_text(encoding='utf-8', errors='ignore') if source.exists() else ''
        helps = extract_helps(text)
        flags = flag_re.search(text)
        flags = flags.group(1).split() if flags else []
        rec, later, css1 = [], [], []
        for h in helps:
            if REC_PREFIX.match(h):
                key = re.sub(r'^https?://[^/]+/TR/(CSS21|CSS2|CSS22)/', '', h).lower()
                cl = amap.get(key)
                if cl:
                    rec.append((cl, 'CSS22' if h.lower().find('/css22/') >= 0 else 'CSS2'))
                elif '#' in h:
                    later.append(h)
                # file-level citations identify no section; the stem table decides
            elif WGDRAFT_PREFIX.match(h):
                cl = amap.get(re.sub(WGDRAFT_PREFIX, '', h).lower())
                if cl:
                    rec.append((cl, 'the CSS2 editor\'s draft'))
                elif '#' in h:
                    later.append(h)
            elif '/REC-CSS1' in h:
                css1.append(h)
            elif h.startswith('http'):
                later.append(h)
        if 'may' in flags:
            reviews.append(record(path, 'optional', [], 'optional', flags, later))
            counts['optional'] += 1
            continue
        classes = {cl: sections.get(cl, 'unreviewed') for cl, _ in rec}
        normative = sorted({cl for cl, k in classes.items() if k == 'user-agent'}, key=clause_key)
        # Print-only obligations (13.2-13.4) make the case print media regardless of kind.
        print_only = any(cl.startswith(('13.2', '13.3', '13.4')) for cl in normative)
        media = ['print'] if (case['kind'] == 'print-reftest' or print_only) else ['screen']
        if not normative:
            fallback = curated(path, helps, text)
            if fallback is None:
                if rec or helps:
                    unreviewed.append((path, [h for _, h in rec[:2]] + [h[:60] for h in later[:1]]))
                else:
                    unreviewed.append((path, ['no citations']))
                continue
            ids = [i for i in fallback['ids'] if i in obligation_ids]
            if fallback['classification'] == 'applicable' and not ids:
                unreviewed.append((path, ['no obligation for cluster']))
                continue
            reviews.append(dict(path=path, classification=fallback['classification'],
                media=fallback['media'] if fallback['classification'] == 'applicable' else [],
                requirementIds=ids,
                rationale=f'Reviewed {REVIEW_DATE}: {fallback["why"]}.'))
            counts[fallback['classification']] += 1
            continue
        ids = [f'css21.{cl}' for cl in normative if f'css21.{cl}' in obligation_ids]
        if not ids:
            unreviewed.append((path, sorted(classes)[:3]))
            continue
        cites_css22 = any(spec == 'CSS22' for _, spec in rec)
        reviews.append(record(path, 'applicable', ids, 'applicable', flags, later,
                              media, cites_css22))
        counts['applicable'] += 1

    official_proposals = json.loads(Path('Lite.Conformance/Css21/css21-official-proposals.json')
                                    .read_text(encoding='utf-8'))
    official_paths = {c['path'] for c in official_proposals['cases']}
    unclassified_official = sum(1 for p in official_paths
                                if p not in {r['path'] for r in reviews}
                                and (p not in existing or existing[p]['classification'] == 'unreviewed'))
    reviewed_paths = {r['path'] for r in reviews}
    unclassified_wpt = sum(1 for c in cases if c['path'] not in reviewed_paths)
    applicability['tests'] = sorted(reviews + [t for t in applicability['tests']
                                               if t['path'] not in reviewed_paths],
                                    key=lambda t: t['path'])
    applicability['reviewComplete'] = unclassified_official == 0 and unclassified_wpt == 0
    with open(applicability_path, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(json.dumps(applicability, indent=2, ensure_ascii=False) + '\n')
    print(f'wpt batch: {dict(counts)}; unreviewed (need individual review): {len(unreviewed)}')
    for path, cites in unreviewed[:25]:
        print(f'  unreviewed: {path} cites {cites}')
    print(f'reviewComplete={applicability["reviewComplete"]} '
          f'(unclassified official={unclassified_official}, wpt={unclassified_wpt})')
    return 0


def record(path: str, classification: str, ids: list[str], kind: str, flags: list[str],
           later: list[str], media: list[str] | None = None, cites_css22: bool = False) -> dict:
    parts = [f'Reviewed {REVIEW_DATE}: WPT css/CSS2 case classified {kind}']
    if ids:
        parts.append('maps to obligations ' + ', '.join(ids))
    if cites_css22:
        parts.append('cites CSS 2.2 anchors (same anchor names as the pinned 2.1 target; '
                     'the citation is recorded, not adopted as a target change)')
    if flags:
        parts.append('flags: ' + ' '.join(sorted(set(flags))))
    if later:
        parts.append('cites later specifications: ' + '; '.join(sorted({h[:70] for h in later})[:2]))
    return {'path': path, 'classification': classification, 'rationale': '. '.join(parts) + '.',
            'media': media or [], 'requirementIds': sorted(set(ids))}


def clause_key(clause: str) -> list:
    parts = re.split(r'(\d+)', clause)
    return [int(p) if p.isdigit() else p for p in parts]


# Individual review calls that no cluster rule can express. Each carries its own
# dated rationale through curated(); provenance lives in this table.
FILE_OVERRIDES = {
    'css/CSS2/fonts/font-148.xht': dict(
        classification='later-feature', media=[], ids=[],
        why="font shorthand accepting a calc() font-size (css-fonts-4 and css-values-3 citations); "
            "the calc notation is later-CSS"),
    'css/CSS2/normal-flow/video-controls-hit-test-order.html': dict(
        classification='later-feature', media=[], ids=[],
        why='hit-testing of native video controls (cssom-view citation); UA chrome behavior '
            'outside the CSS 2.1 rendering obligations'),
    'css/CSS2/positioning/abspos-paged-001.xht': dict(
        classification='applicable', media=['print'], ids=['css21.10.1'],
        why='initial containing block in paged media; the chapter 13 intro anchor precedes the '
            'first heading of the pinned target and the paged flag sets print media'),
    'css/CSS2/positioning/abspos-paged-002.xht': dict(
        classification='applicable', media=['print'], ids=['css21.10.1'],
        why='initial containing block in paged media; the chapter 13 intro anchor precedes the '
            'first heading of the pinned target and the paged flag sets print media'),
    'css/CSS2/tables/table-intro-example-001.xht': dict(
        classification='applicable', media=['screen'], ids=['css21.16.2'],
        why='chapter 17 introduction example pinning cell text alignment (inherited text-align)'),
    'css/CSS2/tables/table-intro-example-002.xht': dict(
        classification='applicable', media=['screen'], ids=['css21.17.5.3'],
        why='chapter 17 introduction example pinning cell vertical alignment'),
    'css/CSS2/tables/table-intro-example-003.xht': dict(
        classification='applicable', media=['screen'], ids=['css21.17.6'],
        why='chapter 17 introduction example pinning border-collapse and table borders'),
    'css/CSS2/tables/table-intro-example-004.xht': dict(
        classification='applicable', media=['screen'], ids=['css21.17.4.1'],
        why='chapter 17 introduction example pinning caption positioning'),
}

# Citation-less stems whose only cited section is specification-permitted.
CURATED_OPTIONAL_CLAUSES = {
    'system-colors': '18.2',
}

# Editor's-draft anchors that identify a section but resolve against no REC
# heading (file-level or propdef fragments).
DRAFT_FRAGMENT_CLAUSES = {
    '#floats': ('9.5', 'floats'),
    '#float-position': ('9.5.1', 'float positioning'),
    '#propdef-float': ('9.5.1', "the 'float' property"),
    '#propdef-clear': ('9.5.2', "the 'clear' property"),
    '#inline-formatting': ('9.4.2', 'inline formatting'),
    '#inline-boxes': ('9.4.2', 'inline boxes'),
    '#height-layout': ('17.5.3', 'table height layout'),
    '#blockwidth': ('10.3.3', 'block-level non-replaced widths'),
    '#min-max-widths': ('10.4', 'minimum and maximum widths'),
    '#min-max-heights': ('10.7', 'minimum and maximum heights'),
    '#static-position': ('10.3.7', 'static positions of absolutely positioned boxes'),
    '#stacking-context': ('9.9.1', 'stacking contexts'),
}


if __name__ == '__main__':
    raise SystemExit(main())
