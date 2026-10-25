#!/usr/bin/env python3
"""Classify the stated pass condition of every self-describing CSS 2.1 case.

The selftest route's no-red scan proves only that no failure red (#ff0000)
painted. That is the WHOLE pass condition for a minority of cases ("Test passes
if there is no red."); most self-describing cases also state geometry, colour or
text conditions ("the blue box is in the upper-left corner of the black box")
the scan cannot see. This generator records, per case, the sentence and a
condition class, plus the WPT reftest twin (same file stem under css/CSS2) that
the runner's twin oracle uses when one exists:

- no-red:    the condition is solely the absence of red; the scan is sufficient.
- same-color: an embedded #test/#reference swatch pair ("same color as").
- compound-no-red: absence of red plus further visual conditions.
- visual:    visual conditions only (shape, position, size, text).
- manual:    needs interaction (WPT manual kind, the "interact" flag, or the
             sentence asks the reader to hover/click/focus/resize/scroll).
- unstated:  no pass sentence found; needs an individual review.

A "check" is attached where the whole visual sentence matches a template that
Css21/VisualChecks.cs evaluates against the render (one-shape, same-dimension,
bar-gap, line-style); unmatched sentences stay unchecked.

Cases covered: official html4 selftests (no reftest.list entry) and WPT
css/CSS2 visual/manual cases. Entries are generated proposals with
"reviewed": false; a reviewed entry (reviewed true) is never overwritten, so
manual corrections survive regeneration.

Run from the repo root after scripts/build-wpt-manifest.ps1:
    python -I scripts/gen_css21_pass_conditions.py
"""
import html
import json
import re
from pathlib import Path

GENERATED = '2026-10-19'
official_root = Path('Lite.Conformance/vendor/css21-official-20110323')
official_catalog = Path('Lite.Conformance/artifacts/css21-official-catalog.json')
wpt_root = Path('Lite.Conformance/vendor/wpt')
wpt_manifest = Path('Lite.Conformance/artifacts/wpt-manifest.json')
output = Path('Lite.Conformance/Css21/css21-pass-conditions.json')
OFFICIAL_PREFIX = 'css21-official/'

paragraph_re = re.compile(r'<p\b[^>]*>(.*?)</p>', re.I | re.S)
tag_re = re.compile(r'<[^>]+>')
comment_re = re.compile(r'<!--.*?-->', re.S)
flags_re = re.compile(r'<meta\s+name=["\']flags["\']\s+content=["\']([^"\']*)["\']', re.I)
passes_re = re.compile(r'\b(?:test\s+)?pass(?:es)?\s+if\b(.*)', re.I | re.S)
NO_RED_ONLY = re.compile(
    r'^(?:there\s+(?:is|should\s+be|must\s+be)\s+)?(?:absolutely\s+)?no\s+red'
    r'(?:\s+(?:visible|below|borders(?:\s+on\s+the\s+table\s+below)?|(?:on|in)\s+(?:the|this)\s+page))*'
    r'(?:,?\s+except\s+for\s+anti-?aliasing(?:\s+issues)?)?$')
# "(do not hover over it)" states a precondition of a visual check, not an interaction.
INTERACTION = re.compile(
    r"(?<!not\s)(?<!n't\s)\b(hover|mouse|click|focus|tab\s+key|press|select\s+the|resize|scroll|drag|zoom|print\s+preview|keyboard)\w*",
    re.I)
STATED = re.compile(r'\b(?:should|must)\b', re.I)


def pass_sentence(text: str) -> str | None:
    text = comment_re.sub('', text)
    for paragraph in paragraph_re.findall(text):
        plain = ' '.join(html.unescape(tag_re.sub(' ', paragraph)).split())
        if passes_re.search(plain) or STATED.search(plain):
            return plain
    return None


def classify(sentence: str | None, kind: str, flags: list[str]) -> str:
    if kind == 'manual' or 'interact' in flags:
        return 'manual'
    if sentence is None:
        return 'unstated'
    if INTERACTION.search(sentence):
        return 'manual'
    match = passes_re.search(sentence)
    clause = (match.group(1) if match else sentence).strip().lower()
    clause = ' '.join(clause.rstrip('.!').split())
    if NO_RED_ONLY.match(clause):
        return 'no-red'
    if re.search(r'\bsame colou?r\b', clause):
        return 'same-color'
    if re.search(r'\bno\s+red\b', clause):
        return 'compound-no-red'
    return 'visual'


COLOR = (r'(maroon|red|orange|yellow|olive|purple|fuchsia|white|lime|green|navy|blue|aqua|teal|black|silver|gr[ae]y)')
NO_RED_CONJUNCT = re.compile(r',?\s+and\s+(?:there\s+is\s+)?no\s+red(?:\s+visible)?(?:\s+on\s+(?:the|this)\s+page)?$')
ONE_SHAPE = re.compile(
    rf'^there\s+(?:is|should\s+be)\s+(?:an?|only\s+one)\s+(?:{COLOR}\s+)?(box|square|rectangle|block|bar)'
    r'(?:\s+below(?:\s+this\s+line)?)?$')
SAME_DIMENSION = re.compile(
    rf'^(?:the|both)\s+(two\s+)?(?:{COLOR}\s+(?:and\s+{COLOR}\s+)?)?(?:boxes|bars|squares)\s+below\s+are\s+the\s+same\s+'
    r'(size|width|height)$')
LINE_STYLE = re.compile(
    rf'^(?:the\s+line\s+below\s+is\s+(dashed|dotted)'
    rf'|there\s+is\s+an?\s+(?:short\s+)?(?:(horizontal|vertical)\s+)?(?:{COLOR}\s+)?(dashed|dotted)\s+(?:{COLOR}\s+)?line(?:\s+below)?)$')
BAR_GAP = re.compile(r'^the\s+middle\s+bar\s+is\s+as\s+wide\s+as\s+the\s+space\s+between\s+the\s+first\s+and\s+last\s+bars$')


def visual_check(sentence: str | None) -> dict | None:
    """A machine check for the stated condition, when the whole sentence matches a known
    template (Css21/VisualChecks.cs evaluates it). Anything unmatched stays unchecked."""
    if not sentence:
        return None
    match = passes_re.search(sentence)
    if not match or not re.match(r'^\s*(?:test\s+)?pass', sentence, re.I):
        return None
    clause = ' '.join(match.group(1).strip().lower().rstrip('.!').split())
    clause = NO_RED_CONJUNCT.sub('', clause)
    if m := ONE_SHAPE.match(clause):
        shape = 'box' if m.group(2) == 'block' else m.group(2)
        return {k: v for k, v in {'kind': 'one-shape', 'shape': shape, 'color': m.group(1)}.items() if v}
    if m := SAME_DIMENSION.match(clause):
        count = 2 if m.group(1) or clause.startswith('both') else None
        return {k: v for k, v in {'kind': 'same-dimension', 'dimension': m.group(4), 'count': count}.items() if v}
    if m := LINE_STYLE.match(clause):
        style = m.group(1) or m.group(4)
        color = m.group(3) or m.group(5)
        return {k: v for k, v in {'kind': 'line-style', 'shape': style, 'color': color,
                                  'dimension': m.group(2)}.items() if v}
    if BAR_GAP.match(clause):
        return {'kind': 'bar-gap'}
    return None


def walk(tree: dict, prefix: str, kind: str, into: list) -> None:
    for name, node in tree.items():
        path = prefix + name
        if isinstance(node, dict):
            walk(node, path + '/', kind, into)
        elif path.startswith('css/CSS2/'):
            into.append((kind, path))


def read(path: Path) -> str:
    try:
        return path.read_text(encoding='utf-8', errors='replace')
    except OSError:
        return ''


def main() -> None:
    manifest = json.loads(wpt_manifest.read_text(encoding='utf-8'))
    wpt_cases: list = []
    for kind in ('reftest', 'visual', 'manual'):
        walk(manifest['items'].get(kind, {}), '', kind, wpt_cases)
    reftest_by_stem: dict[str, list[str]] = {}
    for kind, path in wpt_cases:
        if kind == 'reftest':
            reftest_by_stem.setdefault(Path(path).stem, []).append(path)

    existing = {}
    if output.exists():
        existing = {e['path']: e for e in json.loads(output.read_text(encoding='utf-8-sig'))['cases']}

    entries = []

    def add(suite: str, path: str, file: Path, kind: str) -> None:
        if existing.get(path, {}).get('reviewed'):
            entries.append(existing[path])
            return
        text = read(file)
        flags = (flags_re.search(text).group(1).split() if flags_re.search(text) else [])
        sentence = pass_sentence(text)
        twins = sorted(reftest_by_stem.get(Path(path).stem, []))
        condition = classify(sentence, kind, flags)
        entry = {
            'path': path,
            'suite': suite,
            'condition': condition,
            'sentence': sentence,
            'twin': twins[0] if twins else None,
            'reviewed': False,
        }
        if condition in ('visual', 'compound-no-red') and (check := visual_check(sentence)):
            entry['check'] = check
        entries.append(entry)

    catalog = json.loads(official_catalog.read_text(encoding='utf-8-sig'))
    for case in catalog['cases']:
        if case['kind'] != 'selftest':
            continue
        add('css21-official', case['path'], official_root / case['path'][len(OFFICIAL_PREFIX):], 'selftest')
    for kind, path in wpt_cases:
        if kind in ('visual', 'manual'):
            add('css21-wpt', path, wpt_root / path, kind)

    entries.sort(key=lambda e: e['path'])
    output.write_text(json.dumps({
        'schemaVersion': 1,
        'generated': GENERATED,
        'description': 'Stated pass condition per self-describing CSS 2.1 case; see scripts/gen_css21_pass_conditions.py.',
        'cases': entries,
    }, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')

    summary: dict = {}
    for e in entries:
        key = (e['suite'], e['condition'], 'twin' if e['twin'] else 'no-twin')
        summary[key] = summary.get(key, 0) + 1
    for key in sorted(summary):
        print(f'{summary[key]:6}  {key[0]:15} {key[1]:16} {key[2]}')


if __name__ == '__main__':
    main()
