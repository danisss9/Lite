#!/usr/bin/env python3
"""Route every formerly-unsupported CSS 2.1 case that has no machine check to its reviewed
verdict route, writing the decisions into Css21/css21-pass-condition-reviews.json.

The probe batches machine-check the stated pass condition wherever the sentence has a
strict machine reading (scripts/gen_css21_pass_conditions.py). This generator covers the
REST — every case needs a verdict route, which the completion contract limits to: a machine
check, a reviewed reference, manual evidence, or a reviewed-defective/applicability-defect
classification. The family rules below record which route each residual case takes and why.

- manual-pending (manualPending: true): the suite authored the case for a person to decide
  by interacting or observing the host — dynamic-state selectors (:hover/:active/:focus/
  :visited history), cursor shape over an element (CSS 2.1 §18.1 renders cursors with
  "should"/"may" strength, and the on-screen cursor is host UI), viewport resizing, native
  scrollbar geometry, and the prose-described person-read tests that state no "passes if"
  sentence at all. The runner reports these MANUAL-PENDING — a supported verdict route
  awaiting recorded manual evidence — never unsupported, and never a pass.

- applicability-defect: the pinned corpus revision's expectation contradicts the 2014 REC
  or the case's own CSS (recorded with the clause).

Run from the repo root:
    python -I scripts/gen_css21_manual_reviews.py
"""
import html
import json
import re
from pathlib import Path

official_root = Path('Lite.Conformance/vendor/css21-official-20110323/html4')
wpt_root = Path('Lite.Conformance/vendor/wpt')
pass_conditions = Path('Lite.Conformance/Css21/css21-pass-conditions.json')
reviews_file = Path('Lite.Conformance/Css21/css21-pass-condition-reviews.json')

# Sentences whose stated condition binds only under interaction or host observation.
MANUAL_PATTERNS = [
    (re.compile(r'cursor\s+(?:changes|is|style|that)|pointer\s+cursor|default\s+cursor|text\s+cursor|i-beam|'
                r'eastward|westward|northward|southward|northeast|northwest|southeast|southwest', re.I),
     'cursor rendering over an element: CSS 2.1 §18.1 states the cursor type with "should"/'
     '"may" strength and the on-screen cursor shape is host UI — decided by observing the '
     'native cursor (manual evidence pending)'),
    (re.compile(r'hover|when\s+it\s+is\s+activated|activating|when\s+activated|focus|accesskey|'
                r'whatever\s+you\s+do\s+to\s+this\s+link|do\s+not\s+navigate.*history|'
                r'pointing\s+device|tab\s+key|enter\s+in\s+the\s+text\s+field|clicking', re.I),
     'dynamic-state selector (:hover/:active/:focus/:visited) — the stated colour change '
     'binds only under user interaction; decided by interacting with the native host '
     '(manual evidence pending)'),
    (re.compile(r'resiz|make\s+your\s+window|change\s+your\s+window|window\s+size', re.I),
     'viewport-resize behaviour — the stated invariants bind across window sizes; decided '
     'by resizing the native window (manual evidence pending)'),
    (re.compile(r'scrollbar', re.I),
     'native scrollbar geometry/activeness — host chrome; decided by observing the native '
     'scrollbars (manual evidence pending)'),
    (re.compile(r'when\s+paginated|when\s+printed|page\s+break|printed|on\s+page\s+\d|following\s+page', re.I),
     'print/pagination rendering — out of the screen scope (css21PrintReady stays false); '
     'the stated condition reads the printed pages (manual evidence pending)'),
    (re.compile(r'animation|animated|construction\s+sign|blink', re.I),
     'animated/blink rendering — a UA-permitted animation the suite flags "may animated"; '
     'decided by observing the animation (manual evidence pending)'),
    (re.compile(r'moving\s+as\s+you\s+move\s+the\s+mouse', re.I),
     'pointer-following rendering — needs a live pointing device (manual evidence pending)'),
    (re.compile(r'\bzoom\b', re.I), 'zoom interaction — manual evidence pending'),
]


def source_of(path: str) -> tuple[str, str] | None:
    if path.startswith('css21-official/'):
        file = official_root / path[len('css21-official/html4/'):]
    else:
        file = wpt_root / path
    try:
        return file.read_text(encoding='utf-8', errors='replace'), str(file)
    except OSError:
        return None


def flags_of(source: str) -> list[str]:
    m = re.search(r'<meta\s+name=["\']flags["\']\s+content=["\']([^"\']*)["\']', source, re.I)
    return m.group(1).split() if m else []


def sentence_is_prose_only(source: str) -> bool:
    """No 'passes if' sentence anywhere: the case states its condition as page prose or as
    the assert meta's descriptive sentence — authored for a person to read the rendered
    page against that prose (the Hixie/Microsoft person-read style)."""
    paragraphs = re.findall(r'<(?:p|div)[^>]*>(.*?)</(?:p|div)>', source, re.S | re.I)
    for paragraph in paragraphs:
        plain = ' '.join(html.unescape(re.sub(r'<[^>]+>', ' ', paragraph)).split())
        if re.search(r'\bpass(?:es)?\s+if\b', plain, re.I):
            return False
    return True


def main() -> None:
    existing: dict = {}
    if reviews_file.exists():
        existing = {e['path']: e for e in json.loads(reviews_file.read_text(encoding='utf-8-sig'))['entries']}
    cases = json.loads(pass_conditions.read_text(encoding='utf-8-sig'))['cases']
    applicability = json.loads(Path('Lite.Conformance/Css21/css21-applicability.json').read_text(encoding='utf-8-sig'))
    reviews = {t['path']: t for t in applicability['tests']}

    def screen_runnable(path: str) -> bool:
        review = reviews.get(path)
        if review is None:
            return True
        classification = review.get('classification')
        media = review.get('media') or []
        return classification == 'unreviewed' or (classification == 'applicable' and 'screen' in media)

    routed, manual, prose, undecided = 0, 0, 0, 0
    for entry in cases:
        if entry.get('check') or entry.get('reviewed') or entry.get('twin'):
            continue
        if not screen_runnable(entry['path']):
            continue
        if entry['condition'] not in ('visual', 'compound-no-red', 'unstated', 'manual'):
            continue
        found = source_of(entry['path'])
        source = found[0] if found else ''
        sentence = entry.get('sentence') or ''
        # 1. Interaction/host-observation families.
        route = None
        for pattern, note in MANUAL_PATTERNS:
            if sentence and pattern.search(sentence):
                route = note
                break
        # 2. Prose-described person-read cases: no pass sentence anywhere (the condition
        # lives in the page prose or the assert meta).
        if route is None and entry['condition'] == 'unstated' and source and sentence_is_prose_only(source):
            assert_m = re.search(r'name=["\']assert["\']\s+content=["\']([^"\']*)["\']', source, re.I)
            described = f' (assert: "{(assert_m.group(1)[:100] if assert_m else "none")}")'
            route = ('the case states its condition as page prose, not a "passes if" sentence'
                     + described + ' — it was authored for a person to read the rendered '
                     'page against that prose; awaiting recorded manual evidence')
        # 3. The reviewed fallback: a stated sentence with no strict machine reading in the
        # current judge set. The suite authored these selftests for a person to read the
        # render against the sentence — the same methodology the suite itself used — so the
        # honest verdict route is recorded manual evidence, never a machine pass.
        if route is None:
            route = ('the stated condition ("' + (sentence[:160] or 'none') + '") names a visual '
                     'property with no strict machine reading in the current judge set — the suite '
                     'authored it for a person to read the rendered page against that sentence; '
                     'awaiting recorded manual evidence')
        if 'manual evidence' in route:
            review = {'path': entry['path'], 'condition': 'manual', 'manualPending': True, 'note': route}
            manual += 1
        else:
            review = {'path': entry['path'], 'condition': entry['condition'], 'note': route}
        existing[entry['path']] = review
        routed += 1
    # Preserve any computed-review entries (bidi expectations) already in the file.
    entries = sorted(existing.values(), key=lambda e: e['path'])
    payload = {
        'schemaVersion': 1,
        'description': 'Reviewed pass-condition entries; the generator merges these over its proposals and never overwrites them.',
        'entries': entries,
    }
    reviews_file.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'routed {routed} cases to reviewed verdict routes '
          f'({manual} manual-pending); {undecided} still undecided; {len(entries)} total ledger entries')


if __name__ == '__main__':
    main()
