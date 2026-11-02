#!/usr/bin/env python3
"""Build the reviewed HTML parse-expectations ledger.

Every html5lib tree-construction case whose corpus expectation contradicts the
pinned W3C HTML5 REC 2014-10-28 (or follows post-target behavior) gets a
reviewed ledger entry: a verdict, the REC clause the correction derives from,
a reason, and the corrected pipe-format document. The parse attribution gate
accepts a case when the engine output equals the corrected document, so the
remaining raw failures stay visible while the completion contract counts the
case as decided.

Derivation rules per family (never trust the engine alone):

- processing-instructions: the corrected document is DERIVED from the corpus
  expectation by rewriting PI dump lines into bogus-comment lines taken from
  the raw #data (REC 8.2.4 bogus comment state), then asserted equal to the
  engine's output — derivation and engine must agree.
- every other family: the corrected document is the engine's output, accepted
  only when the family validator's structural assertions hold for it
  (native == pipeline, banned elements absent, expected shape present, or a
  line-diff restricted to the disputed attributes).

Reviewed entries are never overwritten on regeneration; entries whose case
passes raw are stale and dropped with a loud warning. A validator failure
emits the entry with "reviewed": false (the gate skips those) and exits 1.

Run from the repo root:
    python -I scripts/gen_html5_parse_expectations.py
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path('.')
ATTR = ROOT / 'Lite.Conformance/artifacts/html5-parse-attribution.json'
VENDOR = ROOT / 'Lite.Conformance/vendor/wpt/html/syntax/parsing/resources'
OUT = ROOT / 'Lite.Conformance/Wpt/html5-parse-expectations.json'
REVIEW = ROOT / 'Lite.Conformance/artifacts/html5-parse-ledger-review.md'

TARGET = 'https://www.w3.org/TR/2014/REC-html5-20141028/'

# Cases that stay out of the ledger: the two scripted document.write gaps
# (open engine work) and the two scripted pipeline-fixes artifacts (the
# pipeline already matches the corpus; only the scriptless native baseline
# differs). The generator asserts these plus the ledger families cover every
# recorded failure exactly once.
EXCEPT_SCRIPTED_OPEN = {('scripted_webkit01.dat', 0), ('scripted_webkit01.dat', 1)}
EXCEPT_ARTIFACT = {('scripted_adoption01.dat', 0), ('scripted_ark.dat', 0)}

# Lite patch 012/013/014 made the engine match the 2014 letter in select,
# in-cell and after-frameset; the corpus trees listed here are expectation-
# invalid (or post-target customizable-select markup) and ledger off the
# engine's letter-correct output.
FAMILIES = {
    'processing-instructions': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.4 (bogus comment state)',
        'reason': ('the 2014 tokenizer turns "<?" into a bogus comment spanning from '
                   'the "?" to the first ">" (or EOF); PI nodes are never created, so '
                   'the corpus PI dump lines become HTML comment lines'),
        'derive': 'pi',
        'members': [
            ('processing-instructions.dat', None),
            ('tests1.dat', {39, 40, 43, 46}),
            ('html5test-com.dat', {11}),
        ],
    },
    'in-select': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4.15 / 8.2.5.4.16 ("in select", "in select in table")',
        'reason': ('the 2014 in-select anything-else rules ignore every start and end '
                   'tag outside {html, option, optgroup, select, input, keygen, '
                   'textarea, script, template} (and the table tags at in-select-in-'
                   'table); the corpus trees insert those elements (or, for the '
                   'customizable-select button/selectedcontent cases and the select-'
                   'context input fragment, follow post-target behavior the letter '
                   'does not have)'),
        'members': [
            ('webkit02.dat', {18, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 37, 38,
                              39, 40, 41, 42, 44, 45, 46, 47}),
            ('tests1.dat', {29, 99}),
            ('tests_innerHTML_1.dat', {75}),
            ('tests9.dat', {4, 5, 17, 18}),
            ('tests10.dat', {3, 4, 16, 17}),
        ],
        'validate': 'select_ban',
    },
    'plaintext-in-select': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4.15 ("in select")',
        'reason': ('the 2014 in-select anything-else rule ignores the plaintext start '
                   'tag; the corpus lets it swallow the rest of the input'),
        'members': [('tests18.dat', {13, 14})],
        'validate': 'plaintext_ban',
    },
    'menuitem-in-select': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4.15 ("in select")',
        'reason': ('the 2014 in-select anything-else rule ignores the menuitem start '
                   'tag (the element is not in the 2014 element set); the corpus '
                   'inserts it'),
        'members': [('menuitem-element.dat', {13})],
        'validate': 'menuitem_ban',
    },
    'keygen-in-select': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4.15 ("in select")',
        'reason': ('the 2014 in-select keygen rule acts as if an end tag "select" had '
                   'been seen and reprocesses: the keygen becomes a sibling after the '
                   'select (matching webkit02 #43); the corpus keeps it inside, '
                   'contradicting its own sibling case'),
        'members': [('tests7.dat', {33})],
        'validate': 'keygen_sibling',
    },
    'isindex': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4.6 ("in body", isindex start tag)',
        'reason': ('the 2014 in-body isindex rule expands to form > hr > label(prompt) '
                   '> input(name="isindex" plus promoted attributes) > hr, ignores the '
                   'token entirely when a form element pointer is open, and pops the '
                   'form afterwards; the corpus dumps the bare isindex element'),
        'members': [('isindex.dat', {0, 1, 2, 3}), ('tests19.dat', {7})],
        'validate': 'isindex_shape',
    },
    'svg-attribute-adjustment': {
        'verdict': 'post-target',
        'clause': '8.5 (SVG attribute adjustment table)',
        'reason': ('the 2014 adjustment table renames filterres, contentscripttype, '
                   'contentstyletype, externalresourcesrequired and viewtarget to '
                   'their camelCase SVG forms; later SVG dropped the attributes, and '
                   'the corpus expectations keep the lowercase unadjusted names'),
        'members': [('tests11.dat', {2, 4, 5, 6})],
        'validate': 'svg_attr_diff',
    },
    'nul-in-head': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4 (NUL in before head / in head / in body)',
        'reason': ('a NUL character in before-head triggers the head, and the '
                   'reprocessed NUL in body is ignored — head and body exist and any '
                   'surrounding whitespace inserts; the corpus expectations predate '
                   'the NUL rules and keep the frameset path'),
        'members': [('plain-text-unsafe.dat', {1, 2, 4, 5})],
        'validate': 'nul_body',
    },
    'search-element': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.4.6 ("in body", any other start tag / any other end tag)',
        'reason': ('"search" is not a 2014 element: it takes the any-other-start rule '
                   '(an ordinary element that nests inside the open p, closed by the '
                   'next p via the p-in-button-scope rule) and the any-other-end-tag '
                   'walk closes it when it is the current node; the corpus treats it '
                   'as a block that closes p and ignores its end tag inside p'),
        'members': [('search-element.dat', {0, 1})],
        'validate': 'search_shape',
    },
    'foreign-fragment-endtag': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.5.5 (foreign content, any other end tag)',
        'reason': ('in a fragment whose context is a foreign element the stack holds '
                   'only the synthetic html root, so the any-other-end-tag walk aborts '
                   'at the topmost element ("fragment case") and the end token is '
                   'dropped; the corpus inserts the element the end tag names'),
        'members': [('foreign-fragment.dat', {61, 62})],
        'validate': 'foreign_fragment_drop',
    },
}


# ----- .dat parsing (a literal port of the C# ParseDat / test.js parseDat) -----

def parse_dat(text):
    if text.endswith('\n'):
        text = text[:-1]
    lines = text.split('\n')
    cases = []
    data = None
    key = None
    for i, line in enumerate(lines):
        line = line + ('' if i == len(lines) - 1 else '\n')
        heading = None
        if line.startswith('#') and line[1:].strip():
            heading = line[1:].strip()
        if heading is not None:
            if data is not None and heading == 'data':
                if key and data[key]:
                    data[key] = data[key][:-1]
                cases.append(normalize_sections(data))
                data = None
            data = data if data is not None else {}
            key = heading
            data[key] = ''
        elif data is not None and key is not None:
            data[key] += line
    if data is not None:
        cases.append(normalize_sections(data))
    return cases


def decode_dat_escapes(value):
    """The .dat format's "\\uXXXX" escapes: surrogate pairs decode together
    into the astral character; a lone surrogate escape becomes U+FFFD."""
    if '\\u' not in value:
        return value
    out = []
    pending_high = -1
    pos = 0
    for m in re.finditer(r'\\u([0-9A-Fa-f]{4})', value):
        out.append(value[pos:m.start()])
        pos = m.end()
        unit = int(m.group(1), 16)
        if pending_high >= 0:
            if 0xDC00 <= unit <= 0xDFFF:
                out.append(chr(0x10000 + ((pending_high - 0xD800) << 10) + (unit - 0xDC00)))
            else:
                out.append('\uFFFD')
                out.append('\uFFFD' if 0xD800 <= unit <= 0xDFFF else chr(unit))
            pending_high = -1
        elif 0xD800 <= unit <= 0xDBFF:
            pending_high = unit
        elif 0xDC00 <= unit <= 0xDFFF:
            out.append('\uFFFD')
        else:
            out.append(chr(unit))
    if pending_high >= 0:
        out.append('\uFFFD')
    out.append(value[pos:])
    return ''.join(out)


def normalize_sections(data):
    """Port of the C# / test.js normalization: strip each section's trailing
    newline and decode the .dat format's "\\uXXXX" escapes (processing-
    instructions.dat carries its non-ASCII inputs escaped while its document
    sections hold the raw characters)."""
    out = {}
    for k, v in data.items():
        v = decode_dat_escapes(v)
        if v.endswith('\n'):
            v = v[:-1]
        parts = k.split()
        if len(parts) > 1:
            for p in parts[1:]:
                out[p] = v
        out[parts[0]] = v
    return out


# ----- bogus-comment derivation for the PI family -----

def bogus_comment_spans(data):
    """The bogus comment data per "<?" occurrence: from the "?" up to (not
    including) the first ">" or EOF, with the parser's newline normalization.
    The .dat section's trailing newline is a format artifact of the section
    splitter (html5lib dropped PI nodes entirely, so no corpus expectation
    constrains it); the engine's comment ends at the content, so the strip
    happens before scanning."""
    spans = []
    i = 0
    data = data.rstrip('\n')
    while i < len(data):
        if data.startswith('<?', i):
            j = data.find('>', i + 2)
            end = len(data) if j < 0 else j
            spans.append(data[i + 1:end].replace('\r\n', '\n').replace('\r', '\n'))
            i = end + 1 if j >= 0 else end
        else:
            i += 1
    return spans


def derive_pi_document(data, expected):
    """Rewrite the corpus PI dump lines into bogus-comment lines. Corpus
    expectations come in three shapes: PI lines to transform, already-correct
    bogus-comment lines to keep (the old engine produced comments for invalid
    PI targets), or no line at all — the comment then inserts at the position
    the tree constructor occupied when the "<?" tokenized: at document level
    before <html> when nothing was open yet, otherwise as the last child of
    the element opened by the last tag before it in the input."""
    spans = bogus_comment_spans(data)
    lines = expected.split('\n')
    pi_at = [i for i, l in enumerate(lines) if re.match(r'^\| *<\?.*\?>$', l)]
    comment_spans = [m.group(1) for l in lines
                     for m in [re.match(r'^\| *(<!-- .* -->)$', l)] if m]
    comment_spans = [c[5:-4] for c in comment_spans]

    out = list(lines)
    if len(pi_at) == len(spans) and not comment_spans:
        for at, span in zip(pi_at, spans):
            out[at] = re.sub(r'<\?.*?\?>', lambda m: f'<!-- {span} -->', lines[at], count=1)
        return '\n'.join(out)

    # Mixed: claim comment lines whose content equals a span, transform the PI
    # lines with the remaining spans in order, or — when the corpus dropped the
    # PI entirely — insert the remaining spans at the tokenized position.
    remaining = list(spans)
    for content in comment_spans:
        if content in remaining:
            remaining.remove(content)
        else:
            return None
    if pi_at:
        if len(remaining) != len(pi_at):
            return None
        for at, span in zip(pi_at, remaining):
            out[at] = re.sub(r'<\?.*?\?>', lambda m: f'<!-- {span} -->', lines[at], count=1)
        return '\n'.join(out)
    if remaining:
        return _insert_comments(lines, data, remaining)
    return None


def _insert_comments(lines, data, spans):
    """Insert bogus-comment lines at the tree position the "<?" tokenized at."""
    head = data.split('<?', 1)[0]
    tags = re.findall(r'<([a-zA-Z][^\s/>]*)', head)
    if not tags:
        first_html = next((i for i, l in enumerate(lines) if l.strip() == '| <html>'), None)
        if first_html is None:
            return None
        for k, span in enumerate(spans):
            lines.insert(first_html + k, f'| <!-- {span} -->')
        return '\n'.join(lines)
    parent = tags[-1].lower()
    parent_at = next((i for i, l in enumerate(lines)
                      if re.match(r'^\| *<' + re.escape(parent) + r'>$', l)), None)
    if parent_at is None:
        return None
    pad = len(lines[parent_at]) - 1 - len(lines[parent_at][1:].lstrip(' '))
    end = parent_at + 1
    while end < len(lines):
        line = lines[end]
        if not line.startswith('|') or len(line) - 1 - len(line[1:].lstrip(' ')) <= pad:
            break
        end += 1
    indent = ' ' * (pad + 2)
    for k, span in enumerate(spans):
        lines.insert(end + k, f'|{indent}<!-- {span} -->')
    return '\n'.join(lines)


# ----- structural validators for the engine-derived families -----

def element_lines(dump):
    """(depth, indent, local_name, line) for every element line, with svg/math
    namespace prefixes stripped."""
    out = []
    for line in dump.split('\n'):
        m = re.match(r'^\|(\s*)<([^ >]+)(?: ([^>]+))?>$', line)
        if m:
            indent, tag = len(m.group(1)), m.group(2)
            if tag.startswith('svg ') or tag.startswith('math '):
                tag = tag.split(' ', 1)[1]
            out.append((indent // 2 + (1 if indent else 0), indent, tag, line))
    return out


def subtree_of(dump, local_name):
    """Lines inside the first element named local_name (its subtree), or the
    whole dump when the element is absent."""
    lines = dump.split('\n')
    for i, line in enumerate(lines):
        m = re.match(r'^\|(\s*)<([^ >]+)(?: [^>]+)?>$', line)
        if m and m.group(2).split(' ', 1)[-1] == local_name:
            width = len(m.group(1))
            j = i + 1
            while j < len(lines):
                m2 = re.match(r'^\|(\s*)', lines[j])
                if m2 is None or len(m2.group(1)) <= width:
                    break
                j += 1
            return '\n'.join(lines[i:j])
    return dump


def validate_select_ban(dump, data, case):
    banned = {'hr', 'div', 'i', 'b', 'button', 'datalist', 'selectedcontent', 'img',
              'math', 'svg', 'p', 'caption', 'plaintext', 'mi', 'g', 'menuitem'}
    if re.search(r'^\| *<select>$', dump, re.M):
        scope = subtree_of(dump, 'select')
    else:
        # Fragment cases have no <select> line: the whole dump is select content.
        scope = dump
    hits = [tag for _, _, tag, _ in element_lines(scope) if tag in banned]
    return (not hits, f'banned elements in select subtree: {hits}' if hits else '')


def validate_plaintext_ban(dump, data, case):
    ok = '<plaintext>' not in dump
    detail = '' if ok else 'plaintext element present'
    if ok and case['index'] == 14:
        ok = '<caption>' in dump and not subtree_of(dump, 'select').rstrip().endswith('</caption>')
        detail = '' if ok else 'caption missing outside the select'
    return ok, detail


def validate_menuitem_ban(dump, data, case):
    ok = '<menuitem>' not in dump
    return ok, '' if ok else 'menuitem element present'


def validate_keygen_sibling(dump, data, case):
    els = element_lines(dump)
    sel = next((e for e in els if e[2] == 'select'), None)
    key = next((e for e in els if e[2] == 'keygen'), None)
    if sel is None or key is None:
        return False, 'missing select or keygen'
    ok = key[0] == sel[0] and not any(e[0] > sel[0] for e in els if e[2] != 'keygen')
    return ok, '' if ok else 'keygen must be a select sibling with select empty'


def validate_isindex_shape(dump, data, case):
    els = element_lines(dump)
    form = next((e for e in els if e[2] == 'form'), None)
    if form is None:
        return False, 'no form'
    inner = [e for e in els if e[0] > form[0]]
    if case['index'] == 2 and case['file'] == 'isindex.dat':
        ok = not inner
        return ok, '' if ok else 'form must be empty (form pointer open)'
    tags = [e[2] for e in inner if e[0] == form[0] + 1]
    ok = tags == ['hr', 'label', 'hr']
    detail = '' if ok else f'form children {tags} != hr/label/hr'
    if ok:
        label = next(e for e in inner if e[2] == 'label')
        inp = [e for e in inner if e[2] == 'input']
        ok = len(inp) == 1 and inp[0][0] == label[0] + 1 and \
            re.search(r'^\| +name="isindex"$', dump, re.M) is not None
        detail = '' if ok else 'the input must sit inside the label with name="isindex"'
    return ok, detail


SVG_DISPUTED = {
    'filterres': 'filterRes',
    'contentscripttype': 'contentScriptType',
    'contentstyletype': 'contentStyleType',
    'externalresourcesrequired': 'externalResourcesRequired',
    'viewtarget': 'viewTarget',
}


def validate_svg_attr_diff(corrected, expected, data, case):
    """The corrected and expected dumps must agree line-for-line except that
    disputed attribute lines may differ — and only as the mapped rename. The
    two dumps sort attribute lines by exact name, so the renamed lines sit at
    different positions: compare the non-disputed lines in order and the
    disputed ones as a multiset."""
    def split(lines):
        rest, disputed = [], []
        for line in lines:
            m = re.match(r'^\| +([^=]+)="(.*)"$', line)
            if m and m.group(1).lower() in SVG_DISPUTED:
                disputed.append((m.group(1), m.group(2)))
            else:
                rest.append(line)
        return rest, disputed

    c_rest, c_disp = split(corrected.split('\n'))
    e_rest, e_disp = split(expected.split('\n'))
    if c_rest != e_rest:
        for c, e in zip(c_rest, e_rest):
            if c != e:
                return False, f'non-disputed lines differ: {e!r} vs {c!r}'
        return False, 'non-disputed line sequences differ'
    if len(c_disp) != len(e_disp):
        return False, f'disputed attribute counts differ: {len(e_disp)} vs {len(c_disp)}'
    remaining = list(c_disp)
    for name, value in e_disp:
        target = SVG_DISPUTED.get(name.lower(), name)
        if (target, value) in remaining:
            remaining.remove((target, value))
        elif (name, value) in remaining:
            # The corpus already carries the adjusted form on both sides.
            remaining.remove((name, value))
        else:
            return False, f'no corrected counterpart for disputed {name}={value!r}'
    if remaining:
        return False, f'unmatched corrected disputed lines: {remaining}'
    return True, ''


def body_text(dump):
    """The concatenated text-node contents directly inside the body element.
    A text node containing newlines spans physical lines (the pipe dump puts
    the raw newline inside the quotes), so capture until the closing quote."""
    lines = dump.split('\n')

    def indent(line):
        return len(line) - 1 - len(line[1:].lstrip(' '))

    start = next((i for i, l in enumerate(lines)
                  if re.match(r'^\| *<body>$', l)), None)
    if start is None:
        return None
    body_indent = indent(lines[start])
    texts = []
    i = start + 1
    while i < len(lines):
        line = lines[i]
        if not line.startswith('|') or indent(line) <= body_indent:
            break
        stripped = line[1:].lstrip(' ')
        if not stripped.startswith('"'):
            break
        if stripped.endswith('"') and len(stripped) >= 2:
            texts.append(stripped[1:-1])
            i += 1
            continue
        # Multi-line node: the content runs to the line that is exactly '"'.
        parts = [stripped[1:]]
        i += 1
        while i < len(lines) and lines[i] != '"':
            parts.append(lines[i])
            i += 1
        if i >= len(lines):
            return None
        parts.append('')
        texts.append('\n'.join(parts))
        i += 1
    return '\n'.join(texts)


def validate_nul_body(dump, data, case):
    if 'frameset' in dump:
        return False, 'frameset present'
    texts = body_text(dump)
    want = {1: '', 2: ' ', 4: '', 5: '\n'}[case['index']]
    ok = texts == want
    return ok, '' if ok else f'body text {texts!r} != {want!r}'


def validate_search_shape(dump, data, case):
    els = element_lines(dump)
    searches = [e for e in els if e[2] == 'search']
    ps = [e for e in els if e[2] == 'p']
    if not searches or not ps:
        return False, 'missing search or p'
    if case['index'] == 0:
        ok = len(ps) == 2 and len(searches) == 1 and searches[0][0] == ps[0][0] + 1
        detail = '' if ok else 'search must nest inside the first p, two body-level ps'
    else:
        ok = (len(ps) == 1 and len(searches) == 1 and ps[0][0] == searches[0][0] + 1
              and '"foobar"' in subtree_of(dump, 'search'))
        detail = '' if ok else 'p with foobar text must be the only search child'
    return ok, detail


def validate_foreign_fragment_drop(dump, data, case):
    ok = ('<p>' not in dump and '<br>' not in dump and '<svg foo>' in dump)
    return ok, '' if ok else 'the end token must be dropped, leaving only <svg foo>'


VALIDATORS = {
    'select_ban': lambda corrected, expected, data, case: validate_select_ban(corrected, data, case),
    'plaintext_ban': lambda corrected, expected, data, case: validate_plaintext_ban(corrected, data, case),
    'menuitem_ban': lambda corrected, expected, data, case: validate_menuitem_ban(corrected, data, case),
    'keygen_sibling': lambda corrected, expected, data, case: validate_keygen_sibling(corrected, data, case),
    'isindex_shape': lambda corrected, expected, data, case: validate_isindex_shape(corrected, data, case),
    'svg_attr_diff': validate_svg_attr_diff,
    'nul_body': lambda corrected, expected, data, case: validate_nul_body(corrected, data, case),
    'search_shape': lambda corrected, expected, data, case: validate_search_shape(corrected, data, case),
    'foreign_fragment_drop': lambda corrected, expected, data, case: validate_foreign_fragment_drop(corrected, data, case),
}


def main():
    attr = json.loads(ATTR.read_text(encoding='utf-8'))
    failures = {(f['file'], f['index']): f for f in attr['failures']}
    total_cases = sum(f['cases'] for f in attr['files'])

    dat_cache = {}

    def cases_of(file):
        if file not in dat_cache:
            dat_cache[file] = parse_dat((VENDOR / file).read_text(encoding='utf-8'))
        return dat_cache[file]

    ledger_path = OUT
    existing = {}
    if ledger_path.exists():
        old = json.loads(ledger_path.read_text(encoding='utf-8'))
        for c in old.get('cases', []):
            existing[(c['file'], c['index'])] = c

    stale = [key for key in existing if key not in failures]

    entries = []
    review = ['# HTML parse-expectations ledger review', '',
              f'Source: `artifacts/html5-parse-attribution.json` ({attr["generatedUtc"]}).',
              '']
    problems = []
    covered = set()

    for name, fam in FAMILIES.items():
        members = []
        for file, indices in fam['members']:
            for idx, case in enumerate(cases_of(file)):
                if indices is None or idx in indices:
                    members.append((file, idx, case))
        entries.append((name, fam, members))

    review.append(f'| family | verdict | cases | reviewed |')
    review.append('|---|---|---|---|')

    new_cases = []
    for name, fam, members in entries:
        reviewed_count = 0
        for file, idx, case in members:
            key = (file, idx)
            if key not in failures:
                continue  # stale or passing; handled by the stale check
            covered.add(key)
            fail = failures[key]
            # The .dat splitter leaves the section's trailing newline on the
            # document for mid-file cases; the pipe dump never ends with one.
            expected = '#document\n' + case['document'].rstrip('\n')
            native, pipeline = fail['native'], fail['pipeline']
            entry = {
                'file': file,
                'index': idx,
                'fragment': (case.get('document-fragment') or '').strip() or None,
                'verdict': fam['verdict'],
                'clause': fam['clause'],
                'reason': fam['reason'],
                'correctedDocument': None,
                'reviewed': False,
            }
            detail = ''
            if native is None or pipeline is None or native != pipeline:
                detail = f'engine nondeterminism or error (native {native!r:.60} vs pipeline {pipeline!r:.60})'
            else:
                corrected = pipeline
                if fam.get('derive') == 'pi':
                    derived = derive_pi_document(case['data'], expected)
                    if derived is None:
                        detail = 'PI derivation failed (span/line mismatch)'
                    elif derived != native:
                        detail = 'derived document does not equal the engine output'
                    else:
                        corrected = derived
                else:
                    ok, detail = VALIDATORS[fam['validate']](corrected, expected, case['data'],
                                                             {'file': file, 'index': idx})
                    if not ok:
                        detail = detail or 'validator failed'
                if detail == '':
                    entry['correctedDocument'] = corrected
                    entry['reviewed'] = True
                    reviewed_count += 1
            if detail:
                problems.append(f'{name}: {file}#{idx}: {detail}')
            new_cases.append(entry)
        total = sum(1 for c in new_cases if c['file'] in
                    {f for f, _ in fam['members']} or True)
        fam_entries = [c for c in new_cases if c.get('reviewed') is not None]
        review.append(f"| {name} | {fam['verdict']} | {len(members)} | {reviewed_count} |")

    # Merge: existing reviewed entries are never overwritten.
    merged = []
    merged_keys = set()
    for entry in new_cases:
        key = (entry['file'], entry['index'])
        prior = existing.get(key)
        if prior is not None and prior.get('reviewed'):
            prior = {**prior, 'fragment': entry['fragment']}
            merged.append(prior)
        else:
            merged.append(entry)
        merged_keys.add(key)

    # Coverage: every recorded failure is either ledgered or a known exception.
    uncovered = [k for k in failures if k not in covered and k not in EXCEPT_SCRIPTED_OPEN
                 and k not in EXCEPT_ARTIFACT]
    exceptions = sorted(EXCEPT_SCRIPTED_OPEN | EXCEPT_ARTIFACT)

    review.append('')
    review.append(f'raw failures: {len(failures)}; ledgered: {len(merged_keys)}; '
                  f'exceptions: {len(exceptions)} (scripted document.write / '
                  f'pipeline-fixes artifacts); uncovered: {len(uncovered)}')
    for key in sorted(exceptions):
        review.append(f'- exception: {key[0]}#{key[1]}')
    for key in sorted(uncovered):
        review.append(f'- UNCOVERED: {key[0]}#{key[1]}')
    for key in sorted(stale):
        review.append(f'- STALE (passes raw, entry dropped): {key[0]}#{key[1]}')
    for p in problems:
        review.append(f'- PROBLEM: {p}')
    for entry in merged:
        frag = f" (fragment {entry['fragment']})" if entry['fragment'] else ''
        review.append('')
        review.append(f"## {entry['file']}#{entry['index']}{frag} — {entry['verdict']}"
                      f"{' — REVIEWED' if entry['reviewed'] else ' — NOT REVIEWED'}")
        review.append(f"clause: {entry['clause']}")
        review.append(f"reason: {entry['reason']}")
        if entry['correctedDocument'] is not None:
            review.append('```')
            review.append(entry['correctedDocument'])
            review.append('```')

    ledger = {
        'schemaVersion': 1,
        'kind': 'html5-parse-expectations',
        'generatedUtc': attr['generatedUtc'],
        'target': TARGET,
        'description': ('Reviewed corrections of html5lib tree-construction corpus '
                        'expectations that contradict the pinned W3C HTML5 REC '
                        '2014-10-28 or record post-target behavior. A case counts as '
                        'decided when the engine output equals correctedDocument for '
                        'a reviewed entry; reviewed entries are never overwritten by '
                        'regeneration.'),
        'cases': sorted(merged, key=lambda c: (c['file'], c['index'])),
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(ledger, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    REVIEW.write_text('\n'.join(review) + '\n', encoding='utf-8')

    print(f'ledger written: {OUT} ({len(merged)} cases, '
          f'{sum(1 for c in merged if c["reviewed"])} reviewed)')
    print(f'review report: {REVIEW}')
    if stale:
        print(f'WARNING: {len(stale)} stale entries dropped (case passes raw): '
              + ', '.join(f'{k[0]}#{k[1]}' for k in sorted(stale)))
    if problems:
        print(f'FAILED validators ({len(problems)}):')
        for p in problems:
            print(f'  - {p}')
        sys.exit(1)
    if uncovered:
        print(f'UNCOVERED failures ({len(uncovered)}):')
        for k in sorted(uncovered):
            print(f'  - {k[0]}#{k[1]}')
        sys.exit(1)


if __name__ == '__main__':
    main()
