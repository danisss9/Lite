#!/usr/bin/env python3
"""One-shot per batch: record the official-suite applicability review in
Css21/css21-applicability.json.

Run after scripts/gen_css21_section_review.py (reads the reviewed sections and
obligations as ground truth). For each case in css21-official-proposals.json:

- helps anchors are resolved to spec clauses via the vendored pinned REC HTML
  (every named anchor maps to its nearest preceding heading), then to
  obligations css21.<clause> that exist in css21-requirements.json;
- flag "may" cases are classified optional (specification-permitted, not
  required), matching the proposals;
- cases whose only cited sections are informative (aural appendix, change
  history, grammar, indexes, tutorial chapters) are classified informative;
- cases citing only optional sections (18.2 system colors, 18.5 magnification)
  are classified optional with that rationale;
- everything else is applicable with media ["print"] for flag "paged" and
  ["screen"] otherwise; flag "userstyle" adds css21.ua.user-stylesheet;
- cases with no resolvable in-spec anchor (76 today: they cite CSS1, css3-page,
  css3-background, or UTR #9) are left unreviewed for an individual batch and
  listed on stdout; reviewComplete flips only when the official catalog and the
  WPT candidate catalog both have zero unclassified cases.

Existing reviewed entries are preserved; generated entries never overwrite a
review that already carries a rationale.
"""
import collections
import io
import json
import re
from zipfile import ZipFile

TARGET = 'https://www.w3.org/TR/2011/REC-CSS2-20110607/'
REVIEW_DATE = '2026-10-07'
zip_path = 'Lite.Conformance/vendor/css21-spec-20110607/css2.zip'
proposals_path = 'Lite.Conformance/Css21/css21-official-proposals.json'
sections_path = 'Lite.Conformance/Profile/css21-sections.json'
requirements_path = 'Lite.Conformance/Profile/css21-requirements.json'
applicability_path = 'Lite.Conformance/Css21/css21-applicability.json'
catalog_paths = {
    'css21-official': lambda: [c['path'] for c in json.load(
        open(proposals_path, encoding='utf-8'))['cases']],
    'css21-wpt': None,  # filled in main() from the WPT manifest artifact
}

head_re = re.compile(r'<h([2-6])[^>]*>(.*?)</h\1>', re.S | re.I)
anchor_re = re.compile(r'<a\s+name="([^"]+)"', re.I)
clause_re = re.compile(r'^\s*(?:appendix\s+)?([0-9]+(?:\.[0-9]+)*|[A-Z](?:\.[0-9]+)*)\.?\s', re.I)


def build_anchor_map() -> dict[str, str]:
    archive = ZipFile(io.BytesIO(open(zip_path, 'rb').read()))
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
    return amap


def main() -> int:
    amap = build_anchor_map()
    proposals = json.load(open(proposals_path, encoding='utf-8'))
    sections = {s['clause']: s['classification'] for s in
                json.load(open(sections_path, encoding='utf-8'))['sections']}
    requirements = json.load(open(requirements_path, encoding='utf-8'))
    obligation_ids = {r['id'] for r in requirements['requirements']}

    applicability = json.load(open(applicability_path, encoding='utf-8'))
    existing = {t['path']: t for t in applicability['tests']}

    reviews, unreviewed, counts = [], [], collections.Counter()
    for case in proposals['cases']:
        path = case['path']
        if path in existing and existing[path]['classification'] != 'unreviewed':
            reviews.append(existing[path])  # human review wins
            counts[existing[path]['classification']] += 1
            continue
        clauses, external = [], []
        for help_url in case['helps']:
            key = help_url.replace('http://www.w3.org/TR/CSS21/', '')
            key = key.replace('http://www.w3.org/TR/2011/REC-CSS2-20110607/', '')
            if key.lower() in amap:
                clauses.append(amap[key.lower()])
            elif key.startswith('http'):
                external.append(key)
        flags = case['flags']
        if 'may' in flags:
            reviews.append(record(path, 'optional', [], 'optional', flags, external))
            counts['optional'] += 1
            continue
        classes = {cl: sections.get(cl, 'unreviewed') for cl in set(clauses)}
        normative = sorted({cl for cl, k in classes.items() if k == 'user-agent'},
                           key=clause_key)
        if not normative and not clauses:
            stem = re.sub(r'[-_]?\d+[a-z]?(\.htm|\.html)$', '', path.rsplit('/', 1)[-1])
            curated = STEM_CLAUSES.get(stem)
            if curated is None:
                unreviewed.append((path, sorted(set(external))[:3]))
                continue
            clause, why = curated
            ids = [f'css21.{clause}']
            if clause == 'ua.user-stylesheet':
                ids = ['css21.ua.user-stylesheet']
            media = ['screen']
            reviews.append(dict(path=path, classification='applicable', media=media,
                requirementIds=ids,
                rationale=(f'Reviewed {REVIEW_DATE}: no spec-anchor metadata; classified by test '
                           f'cluster "{stem}" and its recorded assertion ({why}).')))
            counts['applicable'] += 1
            continue
        if not normative:
            if 'unreviewed' in classes.values() or not clauses:
                unreviewed.append((path, sorted(set(external))[:3]))
                continue
            kind = 'optional' if 'optional' in classes.values() else 'informative'
            reviews.append(record(path, kind, [], kind, flags, external))
            counts[kind] += 1
            continue
        ids = [f'css21.{cl}' for cl in normative if f'css21.{cl}' in obligation_ids]
        if 'userstyle' in flags:
            ids.append('css21.ua.user-stylesheet')
        # Print-only obligations (13.2-13.4) make the case print media regardless of flags.
        print_only = any(cl.startswith(('13.2', '13.3', '13.4')) for cl in normative)
        media = ['print'] if ('paged' in flags or print_only) else ['screen']
        reviews.append(record(path, 'applicable', ids, 'applicable', flags, external, media))
        counts['applicable'] += 1

    # Review completeness spans both candidate catalogs; the WPT batch closes its own half.
    unclassified_official = len(unreviewed)
    wpt_paths = wpt_candidates()
    reviewed_paths = {r['path'] for r in reviews}
    unclassified_wpt = sum(1 for p in wpt_paths if p not in reviewed_paths)
    applicability['tests'] = sorted(reviews, key=lambda t: t['path'])
    applicability['reviewComplete'] = unclassified_official == 0 and unclassified_wpt == 0

    with open(applicability_path, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(json.dumps(applicability, indent=2, ensure_ascii=False) + '\n')
    print(f'official batch: {dict(counts)}; unreviewed (need individual review): {len(unreviewed)}')
    for path, externals in unreviewed[:20]:
        print(f'  unreviewed: {path} cites {externals}')
    print(f'reviewComplete={applicability["reviewComplete"]} '
          f'(unclassified official={unclassified_official}, wpt={unclassified_wpt})')
    return 0


def record(path: str, classification: str, ids: list[str], kind: str,
           flags: list[str], external: list[str], media: list[str] | None = None) -> dict:
    parts = [f'Reviewed {REVIEW_DATE}: official-suite case classified {kind}']
    if ids:
        parts.append('maps to obligations ' + ', '.join(ids))
    if flags:
        parts.append('flags: ' + ' '.join(sorted(flags)))
    if external:
        parts.append('cites outside the pinned target: ' + '; '.join(sorted(set(external))[:2]))
    if 'interact' in flags or 'animated' in flags:
        parts.append('requires interaction; manual execution evidence pending')
    return {'path': path, 'classification': classification,
            'rationale': '. '.join(parts) + '.', 'media': media or [],
            'requirementIds': sorted(set(ids))}


def wpt_candidates() -> list[str]:
    """WPT css/CSS2 candidate paths from the inventory artifact (no runner needed)."""
    try:
        artifact = json.load(open('Lite.Conformance/artifacts/css21-inventory.json', encoding='utf-8'))
        return [c['path'] for c in artifact['cases'] if c['source'].startswith('css/CSS2/')]
    except (OSError, KeyError, json.JSONDecodeError):
        return []


def clause_key(clause: str) -> list:
    parts = re.split(r'(\d+)', clause)
    return [int(p) if p.isdigit() else p for p in parts]


# Clusters whose metadata cites no in-spec anchor (CSS1-mirror tests, harness
# suites): classified by test cluster and recorded assertion. Stem -> (clause, why).
STEM_CLAUSES = {
    'at-import': ('6.3', 'the @import rule'),
    'media-dependency': ('7.2.1', 'the @media rule with a target medium'),
    'user-stylesheet': ('ua.user-stylesheet', 'the user stylesheet UA control'),
    'html-precedence': ('6.4.4', 'precedence of presentational hints over author CSS'),
    'ident': ('4.1.3', 'identifier characters and case'),
    'counters': ('12.4.1', 'nested counters and scope'),
    'counter-increment': ('12.4', 'counters in elements with display:none'),
    'inline-table': ('9.2.4', "the 'display' property's inline-table value"),
    'inline-table-valign': ('17.4', 'inline-table boxes in the visual formatting model'),
    'border-conflict-element': ('17.6.2.1', 'border conflict resolution per element'),
    'border-bottom-color': ('8.5.2', 'border color'),
    'outline-color': ('18.4', 'dynamic outlines'),
    'root-box': ('10.1', 'the root containing block'),
    'eof': ('4.1.1', 'tokenization at end of stylesheet'),
    'block-in-inline': ('9.2.1.1', 'anonymous block boxes around inline content'),
    'c26-psudo-nest': ('12.1', 'nested :before/:after pseudo-elements'),
    'c541-word-sp': ('16.4', 'word spacing'),
    'c543-txt-decor': ('16.3.1', 'text decoration'),
    'c547-indent': ('16.1', 'text indentation'),
    'c5526c-display': ('9.7', "display/position/float relationships"),
    'inherited-value': ('6.1.2', 'computed values through inheritance'),
    'table-visual-layout': ('17.5', 'visual layout of table contents'),
    'text-decoration': ('16.3.1', 'text decoration'),
    'text-indent': ('16.1', 'text indentation'),
    'word-spacing-characters': ('16.4', 'word spacing'),
    # WPT css/CSS2 clusters without resolvable anchors.
    'background-position': ('14.2.1', 'background positioning'),
    'border-width': ('8.5.1', 'border widths'),
    'groove-default': ('8.5.3', 'groove border style'),
    'ridge-default': ('8.5.3', 'ridge border style'),
    'shand-border': ('8.5.4', 'border shorthand'),
    'cascade-import': ('6.3', 'the @import rule and cascade interaction'),
    'inherit': ('6.2', 'inheritance'),
    'sort-by-order': ('6.4.1', 'cascading order'),
    'float': ('9.5', 'floats'),
    'float-non-replaced-width': ('10.3.5', 'floating non-replaced element widths'),
    # WPT css/CSS2 clusters added 2026-10-07 while closing the review blocker
    # (unnumbered files whose only citations are bug trackers, editor's-draft
    # file-level anchors, or nothing).
    'floats-placement': ('9.5.1', 'float placement rules'),
    'adjoining-floats': ('9.5', 'floats adjacent to boxes that must not overlap them'),
    'floats-wrap-bfc': ('9.5', 'block formatting contexts beside floats'),
    'new-fc': ('9.5', 'new block formatting contexts beside floats'),
    'clearance-containing': ('9.5.2', 'clearance and float containment'),
    'computed-float-position-absolute': ('9.7', "computed 'float' under absolute positioning"),
    'table-sizing-with-adjacent-floats': ('9.5', 'a table beside floats must not overlap them and shrinks into the free band'),
    'absolute-non-replaced-width': ('10.3.7', 'absolutely positioned non-replaced widths'),
    'block-non-replaced-width': ('10.3.3', 'block non-replaced widths'),
    'inline-block-non-replaced-width': ('10.3.9', 'inline-block non-replaced widths'),
    'overflow-propagation': ('11.1.1', 'overflow propagation between body and viewport'),
    'box-offsets-rel-pos': ('9.3.2', 'box offsets for relative positioning'),
    'box-offsets-abs-pos': ('9.3.2', 'box offsets for absolute positioning'),
    'right-offset': ('9.3.2', "the 'right' offset property"),
    'top': ('9.3.2', "the 'top' offset property"),
    'top-offset-percentage': ('9.3.2', 'percentage offsets resolved against the containing block'),
    'left-offset-percentage': ('9.3.2', 'percentage offsets resolved against the containing block'),
    'margin-em-inherit': ('6.2.1', "the 'inherit' value on margins"),
    'margin-percentage-inherit': ('6.2.1', "the 'inherit' value on margins"),
    'padding-em-inherit': ('6.2.1', "the 'inherit' value on padding"),
    'padding-percentage-inherit': ('6.2.1', "the 'inherit' value on padding"),
    'height-applies-to': ('10.7', "the 'height' property applicability"),
    'outline-width': ('18.4', 'outline widths'),
    'z-index-dynamic': ('9.9.1', 'z-index stacking dynamics'),
    'z-index-stack': ('9.9.1', 'z-index stacking order'),
    'column-visibility': ('11.2', "visibility 'collapse' on table columns"),
    'white-space-collapsing': ('16.6.1', 'the white-space processing model'),
    'shand-font': ('15.8', "the 'font' shorthand"),
    'abspos-float-with-inline-container': ('10.1', 'containing blocks with inline containers'),
    'anonymous-block-change': ('9.2.1.1', 'anonymous block boxes around inline content'),
    'line-break-after-leading-float': ('9.5.1', 'line boxes beside leading floats'),
    'zindex': ('9.9.1', 'z-index stacking order'),
    'data-alignment': ('17.5.3', 'cell data alignment (vertical per 17.5.3; horizontal variants via inherited text-align)'),
    'table-borders': ('17.6', 'borders on table elements'),
    'table-organization': ('17.2', 'the CSS table model organization of rows and columns'),
    'bidi-flag-emoji': ('9.10', 'bidirectional text ordering'),
}


if __name__ == '__main__':
    raise SystemExit(main())
