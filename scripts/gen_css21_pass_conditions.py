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
bar-gap, line-style, look-same, solid-line, border-ring, border-sample,
text-color, text-decoration, align-right, shape-relation, space-between,
stack-three, two-size); unmatched sentences stay unchecked.

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

GENERATED = '2026-11-01'
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
SHAPE = r'(box|square|rectangle|block|bar)'
NO_RED_CONJUNCT = re.compile(r'[,\s]+(?:and|with)\s+(?:there\s+is\s+)?no\s+red(?:\s+visible)?(?:\s+on\s+(?:the|this)\s+page)?$', re.I)
ONE_SHAPE = re.compile(
    rf'^there\s+(?:is|should\s+be)\s+(?:an?|only\s+one)\s+(?:{COLOR}\s+)?(?:{SHAPE})'
    r'(?:\s+below(?:\s+this\s+line)?)?$')
ONE_SHAPE_PREDICATE = re.compile(
    rf'^the\s+{SHAPE}\s+below\s+(?:is|should\s+be)\s+(?:a\s+)?{COLOR}$')
SAME_DIMENSION = re.compile(
    rf'^(?:the|both)\s+(two\s+)?(?:{COLOR}\s+(?:and\s+{COLOR}\s+)?)?(?:boxes|bars|squares)\s+below\s+are\s+the\s+same\s+'
    r'(size|width|height)$')
LINE_STYLE = re.compile(
    rf'^(?:the\s+line\s+below\s+is\s+(dashed|dotted)'
    rf'|there\s+is\s+an?\s+(?:short\s+)?(?:(horizontal|vertical)\s+)?(?:{COLOR}\s+)?(dashed|dotted)\s+(?:{COLOR}\s+)?line(?:\s+below)?)$')
BAR_GAP = re.compile(r'^the\s+middle\s+bar\s+is\s+as\s+wide\s+as\s+the\s+space\s+between\s+the\s+first\s+and\s+last\s+bars$')
LOOK_SAME = re.compile(
    r'^the\s+following\s+two\s+(lines?|columns?|tables?)\s+should\s+(?:look\s+the\s+same|be\s+the\s+same|be\s+identical)[:]?$'
    r'|^the\s+two\s+(lines?|boxes|tables?|columns?)\s+(?:of\s+text\s+below\s+)?(?:below\s+)?look\s+(?:the\s+same|identical)$'
    r'|^the\s+two\s+(lines?|boxes|tables?|columns?)\s+below\s+(?:should\s+)?(?:look\s+the\s+same|are\s+identical|should\s+be\s+identical)[:.]?$'
    r'|^the\s+two\s+tables\s+should\s+be\s+identical[:]?$')
SOLID_LINE = re.compile(
    rf'^(?:the\s+line\s+below\s+is\s+solid\s+{COLOR}(?:\s+or\s+shades\s+of\s+{COLOR})?'
    rf'|there\s+is\s+a\s+line\s+below'
    rf'|there\s+is\s+a\s+(?:solid\s+)?{COLOR}\s+line\s+below)$')
BORDER_RING = re.compile(
    rf'^(?:the\s+box\s+below\s+has\s+a\s+(dashed|dotted)\s+border'
    rf'|there\s+is\s+a\s+box\s+below\s+with\s+a\s+(dashed|dotted)(?:\s+{COLOR})?\s+border'
    rf'|there\s+is\s+a\s+(dashed|dotted)\s+lined\s+box(?:\s+below)?)$')
BORDER_SAMPLE = re.compile(
    r'^the\s+center\s+box\s+in\s+the\s+table\s+below\s+has\s+(?:a\s+border\s+similar\s+to\s+this:?|no\s+border)$')
TEXT_COLOR = re.compile(rf"^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+is\s+{COLOR}$")
TEXT_DECORATION = re.compile(
    r"^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+(does\s+not\s+have|has)\s+a\s+line\s+under\s+it$"
    r"|^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+has\s+a\s+line\s+over\s+it,\s+a\s+line\s+under\s+it\s+and\s+a\s+line\s+through\s+it$")
ALIGN_RIGHT = re.compile(r"^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+is\s+aligned\s+to\s+the\s+right\s+side\s+of\s+the\s+box$")
CENTERED_RIGHT = re.compile(
    rf'^the\s+{COLOR}\s+square\s+below\s+is\s+vertically\s+cente ?red\s+and\s+on\s+the\s+right\s+inner\s+edge\s+of\s+the\s+{COLOR}\s+(?:box|square)$')
SIZE_RELATION = re.compile(rf'^the\s+{COLOR}\s+box\s+is\s+(larger|smaller)\s+than\s+the\s+{COLOR}\s+box$')
BENEATH = re.compile(
    rf'^the\s+{COLOR}\s+box\s+is\s+directly\s+beneath\s+the\s+{COLOR}\s+box'
    rf'(?:\s+and\s+neither\s+box\s+is\s+touching\s+the\s+{COLOR}\s+line)?$')
HEIGHT_SIMILAR = re.compile(
    rf'^the\s+height\s+of\s+the\s+{COLOR}\s+box\s+is\s+similar\s+to\s+the\s+height\s+of\s+the\s+{COLOR}\s+boxes?'
    r'(?:\.\s*it\s+is\s+acceptable.*)?$')
NO_VERTICAL_GAP = re.compile(
    rf'^there\s+is\s+(?:a|an)\s+{COLOR}\s+and\s+(?:a|an)?\s*{COLOR}\s+square\s+below\s+and\s+there\s+is\s+no\s+vertical\s+gap\s+between\s+the\s+squares$')
SPACE_BETWEEN = re.compile(rf'^there\s+is\s+{COLOR}\s+space\s+between\s+the\s+boxes\s+below$')
STACK_THREE = re.compile(r'^the\s+pattern\s+below\s+should\s+be\s+a\s+stack\s+of\s+three\s+(?:perfect\s+)?rectangles$')
TWO_SIZE = re.compile(r'^there\s+is\s+a\s+(?:small\s+and\s+large|large\s+and\s+small)\s+box\s+on\s+the\s+page$')
OUTLINE_TEXT = re.compile(
    r"^there\s+is\s+a\s+(dashed|dotted)\s+outline\s+that\s+goes\s+around\s+the\s+following\s+[\"']sample\s+text[\"']"
    r'(?:\s+and\s+the\s+[\""]filler\s+text[\""]\s+below)?$')
UNIFORM_RING = re.compile(r'^there\s+is\s+a\s+box\s+below\s+with\s+four\s+sides\s+that\s+are\s+the\s+same\s+width')
LINE_PAIR = re.compile(
    rf'^(?:below\s+there\s+is\s+a\s+(vertical|horizontal)\s+solid\s+{COLOR}\s+line,\s+and\s+to\s+its\s+right\s+side,\s+a\s+(dotted|dashed)\s+{COLOR}\s+line'
    rf'|below\s+there\s+is\s+a\s+(vertical|horizontal)\s+solid\s+{COLOR}\s+line\s+above\s+a\s+(dotted|dashed)\s+{COLOR}\s+line'
    rf'|there\s+are\s+two\s+(dashed|dotted)\s+{COLOR}\s+lines\s+below)$')
MARKER_BULLET = re.compile(
    rf'^there\s+is\s+(?:a\s+filled\s+{COLOR}\s+square|a\s+(?:box|square)(?:\s+below)?)\s+and\s+there\s+is\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+(?:box|square)$'
    rf'|^there\s+is\s+a\s+(?:box|square)\s+below\s+and\s+there\s+is\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+(?:box|square)$')
LETTER_X = re.compile(r'^there\s+is\s+a\s+letter\s+[\""]x[\""]\s+below$')
BOX_ABOVE_X = re.compile(r'^there\s+is\s+a\s+box\s+above\s+the\s+letter\s+[\""]x[\""]\s+below$')
ROUND_DOT = re.compile(r'^there\s+is\s+a\s+single\s+round\s+dot\s+below$')
DOUBLE_LINE = re.compile(r'^there\s+is\s+a\s+double\s+line\s+below$')
NO_THROUGH = re.compile(r'^to\s+pass,\s+there\s+must\s+not\s+be\s+a\s+line\s+through\s+the\s+text\s+below$')
THIS_LINE_COLOR = re.compile(rf'^this\s+line\s+should\s+be\s+{COLOR}$')
APPROX_SAME = re.compile(r'^the\s+following\s+two\s+lines\s+should\s+look\s+approximately\s+the\s+same:?')
NOT_JAGGED = re.compile(r'^there\s+is\s+a\s+single\s+box\s+below\s+and\s+the\s+top\s+edge\s+is\s+not\s+jagged$')
SLANTED = re.compile(r'^there\s+is\s+a\s+box\s+below\s+that\s+appears\s+to\s+be\s+slanted\s+to\s+one\s+side$')


def visual_check(sentence: str | None) -> dict | None:
    """A machine check for the stated condition, when the whole sentence matches a known
    template (Css21/VisualChecks.cs evaluates it). Anything unmatched stays unchecked."""
    if not sentence:
        return None
    match = passes_re.search(sentence)
    if match:
        clause = ' '.join(match.group(1).strip().lower().rstrip('.!').split())
    elif re.match(r'^(?:the|there|both)\b', sentence, re.I):
        clause = ' '.join(sentence.strip().lower().rstrip('.!').split())
    else:
        return None
    clause = NO_RED_CONJUNCT.sub('', clause)
    if m := ONE_SHAPE.match(clause):
        shape = 'box' if m.group(2) == 'block' else m.group(2)
        return {k: v for k, v in {'kind': 'one-shape', 'shape': shape, 'color': m.group(1)}.items() if v}
    if m := ONE_SHAPE_PREDICATE.match(clause):
        shape = 'box' if m.group(1) == 'block' else m.group(1)
        return {'kind': 'one-shape', 'shape': shape, 'color': m.group(2)}
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
    if m := LOOK_SAME.match(clause):
        return {'kind': 'look-same'}
    if m := SOLID_LINE.match(clause):
        return {k: v for k, v in {'kind': 'solid-line', 'color': m.group(1) or m.group(3)}.items() if v}
    if m := BORDER_RING.match(clause):
        style = m.group(1) or m.group(2) or m.group(3)
        color = m.group(4) if m.group(2) else None
        return {k: v for k, v in {'kind': 'border-ring', 'shape': style, 'color': color}.items() if v}
    if m := BORDER_SAMPLE.match(clause):
        return {'kind': 'border-sample', 'variant': 'none' if 'no border' in clause else 'similar'}
    if m := TEXT_COLOR.match(clause):
        return {'kind': 'text-color', 'color': m.group(1)}
    if m := TEXT_DECORATION.match(clause):
        if m.group(1):  # "does not have a line under it"
            return {'kind': 'text-decoration', 'under': False}
        return {'kind': 'text-decoration', 'over': True, 'under': True, 'through': True}
    if ALIGN_RIGHT.match(clause):
        return {'kind': 'align-right'}
    if m := CENTERED_RIGHT.match(clause):
        return {'kind': 'shape-relation', 'relation': 'centered-right', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := SIZE_RELATION.match(clause):
        return {'kind': 'shape-relation', 'relation': m.group(2), 'colorA': m.group(1), 'colorB': m.group(3)}
    if m := BENEATH.match(clause):
        check = {'kind': 'shape-relation', 'relation': 'beneath', 'colorA': m.group(1), 'colorB': m.group(2)}
        if m.group(3):
            check['lineColor'] = m.group(3)
        return check
    if m := HEIGHT_SIMILAR.match(clause):
        # Some variants add "it is acceptable to have the blue box height be shorter or
        # taller" — the clause cancels the comparison, so no machine check exists.
        if re.search(r'acceptable', clause):
            return None
        return {'kind': 'shape-relation', 'relation': 'height-similar', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := NO_VERTICAL_GAP.match(clause):
        return {'kind': 'shape-relation', 'relation': 'no-vertical-gap', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := SPACE_BETWEEN.match(clause):
        return {'kind': 'space-between', 'color': m.group(1)}
    if STACK_THREE.match(clause):
        return {'kind': 'stack-three'}
    if TWO_SIZE.match(clause):
        return {'kind': 'two-size'}
    if m := OUTLINE_TEXT.match(clause):
        return {'kind': 'outline-text', 'shape': m.group(1)}
    if UNIFORM_RING.match(clause):
        return {'kind': 'border-ring', 'shape': 'uniform'}
    if m := LINE_PAIR.match(clause):
        if m.group(1):
            return {'kind': 'line-pair', 'firstShape': 'solid', 'firstColor': m.group(2),
                    'secondShape': m.group(3), 'secondColor': m.group(4), 'arrangement': 'right-of' if m.group(1) == 'vertical' else 'below'}
        if m.group(5):
            return {'kind': 'line-pair', 'firstShape': 'solid', 'firstColor': m.group(6),
                    'secondShape': m.group(7), 'secondColor': m.group(8), 'arrangement': 'below'}
        return {'kind': 'line-pair', 'firstShape': m.group(9), 'secondShape': m.group(9),
                'secondColor': m.group(10), 'arrangement': 'either'}
    if m := MARKER_BULLET.match(clause):
        return {'kind': 'marker-bullet', 'color': m.group(1)}
    if LETTER_X.match(clause):
        return {'kind': 'letter-x'}
    if BOX_ABOVE_X.match(clause):
        return {'kind': 'letter-x', 'variant': 'box-above'}
    if ROUND_DOT.match(clause):
        return {'kind': 'round-dot'}
    if DOUBLE_LINE.match(clause):
        return {'kind': 'double-line'}
    if NO_THROUGH.match(clause):
        return {'kind': 'text-decoration', 'through': False}
    if m := THIS_LINE_COLOR.match(clause):
        return {'kind': 'text-color', 'color': m.group(1), 'anchor': 'instruction'}
    if APPROX_SAME.match(clause):
        return {'kind': 'look-same', 'approx': True}
    if NOT_JAGGED.match(clause):
        return {'kind': 'one-shape', 'shape': 'box'}
    if SLANTED.match(clause):
        return {'kind': 'slanted-box'}
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
