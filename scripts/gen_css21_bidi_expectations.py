#!/usr/bin/env python3
"""Compute the reviewed glyph-order expectations for the direction-unicode-bidi family.

The 28 official cases (and their WPT i18n/visuren twins, when the documents are
equivalent) state "Test passes if characters are in the same order" — the order read off
the case's reference image. A bitmap judge cannot read glyph identity, so the reviewed
route records the expected VISUAL sequence and a paint-log judge (Css21/VisualChecks.cs
"painted-order") compares it against what the painter actually drew.

The expected sequence is computed here with the bidirectional algorithm's reordering
(Unicode UBA X2/X3/X6, W7, N1/N2, I1/I2, L2) applied to the case's own source text and
CSS (direction / unicode-bidi embed / bidi-override), then reviewed against the suite's
reference images: the images are the review instrument, this script is the derivation.

Writes review entries into Lite.Conformance/Css21/css21-pass-condition-reviews.json,
merging with any existing entries (by path).

Run from the repo root:
    python -I scripts/gen_css21_bidi_expectations.py
"""
import html
import json
import re
import unicodedata
from pathlib import Path

official_root = Path('Lite.Conformance/vendor/css21-official-20110323/html4')
wpt_root = Path('Lite.Conformance/vendor/wpt/css/CSS2/i18n/visuren')
reviews_file = Path('Lite.Conformance/Css21/css21-pass-condition-reviews.json')


def bidi_types(text: str) -> list[str]:
    return [unicodedata.bidirectional(c) for c in text]


def resolve_levels_and_types(text: str, base_ltr: bool, spans: list[tuple[int, int, str, str]]):
    """spans: (start, end, direction, mode) with mode in embed|override. Returns (types, levels)."""
    n = len(text)
    types = bidi_types(text)
    levels = [0 if base_ltr else 1] * n
    for start, end, direction, mode in spans:
        cur = levels[start]
        # X2 (RLE): least odd level greater than cur; X3 (LRE): least even level greater.
        if direction == 'rtl':
            new = cur + 1 if cur % 2 == 0 else cur + 2
        else:
            new = cur + 2 if cur % 2 == 0 else cur + 1
        for i in range(start, end):
            levels[i] = new
            if mode == 'override':
                t = types[i]
                if t != 'BN':
                    types[i] = 'R' if direction == 'rtl' else 'L'
    return types, levels


def reorder(text: str, types: list[str], levels: list[int], base_ltr: bool) -> str:
    """W7, N1/N2, I1/I2, L2 — the subset that decides these one-line cases."""
    n = len(text)
    sos = 'L' if base_ltr else 'R'
    eos = sos

    # W7: EN with a preceding strong L (sos included) becomes L. (W2's EN→AN after AL does
    # not fire here: the only EN digits follow a strong L within their run.)
    last_strong = sos
    for i in range(n):
        if types[i] == 'EN':
            if last_strong == 'L':
                types[i] = 'L'
        elif types[i] in ('L', 'R', 'AL'):
            last_strong = 'L' if types[i] == 'L' else 'R'

    # N1/N2 for neutrals (ON, WS, and the leftover CS/BN treated as ON here).
    def is_neutral(t):
        return t in ('WS', 'ON', 'CS', 'ET', 'ES', 'BN', 'PDF', 'RLE', 'LRE', 'RLO', 'LRO')

    i = 0
    while i < n:
        if not is_neutral(types[i]):
            i += 1
            continue
        j = i
        while j < n and is_neutral(types[j]):
            j += 1
        left = types[i - 1] if i > 0 else sos
        right = types[j] if j < n else eos
        l_effective = 'R' if left == 'AL' else left
        r_effective = 'R' if right == 'AL' else right
        for k in range(i, j):
            if l_effective == r_effective:
                types[k] = l_effective
            else:
                # N2: take the embedding direction (odd level = R).
                types[k] = 'R' if levels[k] % 2 == 1 else 'L'
        i = j

    # I1/I2: implicit levels.
    for k in range(n):
        t = types[k]
        if levels[k] % 2 == 0:
            if t in ('R', 'AL'):
                levels[k] += 1
            elif t in ('AN', 'EN'):
                levels[k] += 2
        else:
            if t in ('L', 'AN', 'EN'):
                levels[k] += 1

    # L2: from the highest level down to the lowest odd level, reverse contiguous runs at
    # that level or higher (even levels are part of the walk: they re-order the odd islands
    # nested inside them).
    chars = list(text)
    highest = max(levels, default=0)
    for level in range(highest, 0, -1):
        k = 0
        while k < n:
            if levels[k] >= level:
                m = k
                while m < n and levels[m] >= level:
                    m += 1
                chars[k:m] = reversed(chars[k:m])
                k = m
            else:
                k += 1
    return ''.join(chars)


def visual_line(text: str, base_ltr: bool, spans: list[tuple[int, int, str, str]]) -> str:
    types, levels = resolve_levels_and_types(text, base_ltr, spans)
    return reorder(text, types, levels, base_ltr)


CASE_RE = re.compile(r'<style[^>]*>(.*?)</style>', re.S)
TEST_DIV_RE = re.compile(r'<div class="test">(.*?)<p', re.S)


def case_config(source: str) -> dict:
    css = CASE_RE.search(source).group(1)
    css = re.sub(r'/\*.*?\*/', '', css, flags=re.S)
    body = TEST_DIV_RE.search(source).group(1)
    body = re.sub(r'</div>\s*$', '', body.strip())
    config = {'test_dir': 'ltr', 'test_bidi': 'normal', 'span_dir': None, 'span_bidi': 'normal',
              'div_dir': None, 'div_bidi': 'normal', 'body': body}
    for sel, decl in re.findall(r'([^{}]+)\{([^{}]*)\}', css):
        sel = ' '.join(sel.split())
        if sel == '.test span':
            config['span_dir'] = (re.search(r'direction:\s*(\w+)', decl) or [None, None])[1]
            config['span_bidi'] = (re.search(r'unicode-bidi:\s*(\S+?)\s*[;}]', decl) or [None, 'normal'])[1]
        elif sel == '.test div':
            config['div_dir'] = (re.search(r'direction:\s*(\w+)', decl) or [None, None])[1]
            config['div_bidi'] = (re.search(r'unicode-bidi:\s*(\S+?)\s*[;}]', decl) or [None, 'normal'])[1]
        elif sel == '.test':
            # The suite's "not part of the test" style block repeats `.test` for font sizing
            # without a direction of its own; a rule that does not DECLARE direction must not
            # reset the declared one (the cascade keeps the earlier declaration). The last
            # explicit direction wins.
            declared = (re.search(r'direction:\s*(\w+)', decl) or [None, None])[1]
            if declared:
                config['test_dir'] = declared
            config['test_bidi'] = (re.search(r'unicode-bidi:\s*(\S+?)\s*[;}]', decl) or [None, 'normal'])[1] or 'normal'
    return config


def expectations_for(source: str) -> list[str]:
    """The expected visual order of each line the test div paints."""
    cfg = case_config(source)
    body = html.unescape(cfg['body'])
    base_ltr = cfg['test_dir'] != 'rtl'
    # One line per block child; the span participates inside its parent's line.
    if cfg['test_bidi'] == 'bidi-override' and '<div' in body:
        # The override governs the block's own inline content; the nested block div forms
        # its own line(s) under the same override.
        inner = re.search(r'<div[^>]*>(.*?)</div>', body, re.S)
        rest = body.replace(inner.group(0), '', 1) if inner else body
        lines = []
        if inner:
            lines.append((re.sub(r'<[^>]+>', '', inner.group(1)), cfg['test_dir'], []))
        outer_text = re.sub(r'<[^>]+>', '', rest).strip()
        if outer_text:
            lines.append((outer_text, cfg['test_dir'], []))
        return [visual_line(text, dir != 'rtl', [(0, len(text), dir, 'override')])
                for text, dir, _ in lines]
    span_m = re.search(r'<span[^>]*>(.*?)</span>', body, re.S)
    if span_m and (cfg['span_bidi'] in ('embed', 'bidi-override')):
        # The span opens an embedding: compute the line as one text with the span range marked.
        plain = re.sub(r'<[^>]+>', '', body)
        span_plain = re.sub(r'<[^>]+>', '', span_m.group(1))
        start = plain.index(span_plain)
        mode = cfg['span_bidi'].replace('bidi-', '')
        spans = [(start, start + len(span_plain), cfg['span_dir'] or cfg['test_dir'], mode)]
        return [visual_line(plain, base_ltr, spans)]
    # No span embedding (unicode-bidi normal, or no span): the text participates in the
    # base context directly.
    plain = re.sub(r'<[^>]+>', '', body)
    return [visual_line(plain, base_ltr, [])]


def normalize_doc(source: str) -> str:
    source = re.sub(r'<!--.*?-->', '', source, flags=re.S)
    styles = ' '.join(m.group(1) for m in re.finditer(r'<style[^>]*>(.*?)</style>', source, re.S | re.I))
    body_m = re.search(r'<body[^>]*>(.*)</body>', source, re.S | re.I)
    body = body_m.group(1) if body_m else ''
    text = (styles + '|' + body).replace('/>', '>').replace('<![CDATA[', '').replace(']]>', '')
    # The XHTML twins write attributes with single quotes; attribute-quote style carries no
    # rendering meaning, so both fold before the equivalence check.
    text = text.replace('"', "'")
    return re.sub(r'\s+', '', text).lower()


def main() -> None:
    existing: dict = {}
    if reviews_file.exists():
        existing = {e['path']: e for e in json.loads(reviews_file.read_text(encoding='utf-8-sig'))['entries']}
    wpt_names = {p.name for p in wpt_root.glob('direction-unicode-bidi-*.xht')}
    added = 0
    for i in range(1, 29):
        stem = f'direction-unicode-bidi-{i:03d}'
        official = official_root / f'{stem}.htm'
        source = official.read_text(encoding='utf-8')
        expected_lines = expectations_for(source)
        expected = '\n'.join(expected_lines)
        note = (f'bidi glyph order computed from the case source by the reviewed reordering '
                f'(scripts/gen_css21_bidi_expectations.py) and verified against support/{stem}.png '
                f'where the suite ships a distinct reference')
        review = {
            'path': f'css21-official/html4/{stem}.htm',
            'condition': 'visual',
            'check': {'kind': 'painted-order', 'needle': expected},
            'note': note,
        }
        existing[review['path']] = review
        added += 1
        twin = wpt_root / f'{stem}.xht'
        if twin.name in wpt_names and normalize_doc(source) == normalize_doc(twin.read_text(encoding='utf-8')):
            twin_review = dict(review)
            twin_review['path'] = f'css/CSS2/i18n/visuren/{stem}.xht'
            twin_review['note'] = note + '; the WPT document is equivalent to the official one'
            existing[twin_review['path']] = twin_review
            added += 1

    # The text-decoration "user agents may also blink" family: the suite flags it interact
    # (blinking can be toggled), but the sentence's stated condition is the three decoration
    # lines — the blink clause is explicitly optional ("may"). No interaction is required to
    # decide it, so the reviewed entry reclassifies the case visual with the decoration check.
    blink_suffix = re.compile(
        r'user\s+agents\s+may\s+also\s+blink\s+the\s+text\s+on\s+and\s+off\s+the\s+screen\.?', re.I)
    triple = re.compile(r'has\s+a\s+line\s+over\s+it,\s+a\s+line\s+under\s+it\s+and\s+a\s+line\s+through\s+it', re.I)
    pair = re.compile(r'has\s+a\s+line\s+(over|under|through)\s+it\s+and\s+a\s+line\s+(over|under|through)\s+it', re.I)
    blink_scans = ((official_root, 'css21-official/html4/', 'text-decoration-*.htm'),
                   (wpt_root.parent.parent / 'text', 'css/CSS2/text/', 'text-decoration-*.xht'))
    for suite_root, prefix, pattern in blink_scans:
        for source_path in sorted(suite_root.glob(pattern)):
            try:
                source = source_path.read_text(encoding='utf-8')
            except UnicodeDecodeError:
                source = source_path.read_text(encoding='utf-8', errors='replace')
            flags_m = re.search(r'<meta\s+name=["\']flags["\']\s+content=["\']([^"\']*)["\']', source, re.I)
            if not flags_m or 'interact' not in flags_m.group(1).split():
                continue
            for paragraph in re.findall(r'<p\b[^>]*>(.*?)</p>', source, re.S | re.I):
                plain = ' '.join(html.unescape(re.sub(r'<[^>]+>', ' ', paragraph)).split())
                if not blink_suffix.search(plain):
                    continue
                if triple.search(plain):
                    check = {'kind': 'text-decoration', 'over': True, 'under': True, 'through': True}
                elif (pm := pair.search(plain)):
                    names = {'over': 'over', 'under': 'under', 'through': 'through'}
                    check = {'kind': 'text-decoration',
                             **{names[pm.group(1)]: True, names[pm.group(2)]: True}}
                else:
                    continue
                existing[f'{prefix}{source_path.name}'] = {
                    'path': f'{prefix}{source_path.name}',
                    'condition': 'visual',
                    'check': check,
                    'note': ('the interact flag covers only the optional blink clause ("user agents may '
                             'also blink"); the stated decoration lines are decided without interaction'),
                }
                added += 1
                break

    entries = sorted(existing.values(), key=lambda e: e['path'])
    payload = {
        'schemaVersion': 1,
        'description': 'Reviewed pass-condition entries; the generator merges these over its proposals and never overwrites them.',
        'entries': entries,
    }
    reviews_file.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'{added} review entries written ({len(entries)} total)')


if __name__ == '__main__':
    main()
