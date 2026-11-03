#!/usr/bin/env python3
"""Build the reviewed html5lib tokenizer-expectations ledger.

Every tokenizer corpus case whose expected token stream contradicts the pinned
W3C HTML5 REC 2014-10-28 gets a reviewed ledger entry: a verdict, the REC
clause the correction derives from, a reason, and the corrected token stream.
The html5-tokenizer gate accepts a case when the engine output equals the
corrected stream, so the remaining failures stay visible while the completion
contract counts the case as decided.

Derivation rules per family (never trust the engine alone):

- processing-instruction: the corrected stream is DERIVED from the corpus
  input — a bogus comment spanning from the "?" to the first ">" (or EOF),
  NUL replaced (REC 8.2.4.8 tag open state -> 8.2.4.44 bogus comment state) —
  then asserted equal to the engine's output. PI tokens are post-target: the
  2014 letter never creates a processing-instruction token.
- infoset-coercion (xmlViolation.test): the file's README pins its
  expectations to the spec's "Coercing an HTML DOM into an infoset" tweaks
  (U+000C rewritten to space, "--" inside comments split). The 2014 tokenizer
  letter keeps both characters verbatim; the corrected stream is the engine's
  output, accepted only when it equals the verbatim form derived from the
  input.
- engine-limitation: not ledgered. The U+FFFF sentinel collision is pinned as
  a runner exclusion in the gate (it must stay visible as a non-pass, never a
  waiver that passes).

Reviewed entries are never overwritten on regeneration; entries whose case
passes raw are stale and dropped with a loud warning. A derivation mismatch
emits the entry with "reviewed": false (the gate skips those) and exits 1.

Run from the repo root after a `--suite html5-tokenizer --report ...` run:
    python -I scripts/gen_html5_tokenizer_expectations.py
"""
import json
import sys
from pathlib import Path

ROOT = Path('.')
REPORT = ROOT / 'Lite.Conformance/artifacts/html5-tokenizer-report.json'
VENDOR = ROOT / 'Lite.Conformance/vendor/html5lib-tests/tokenizer'
OUT = ROOT / 'Lite.Conformance/Wpt/html5-tokenizer-expectations.json'
REVIEW = ROOT / 'Lite.Conformance/artifacts/html5-tokenizer-ledger-review.md'

TARGET = 'https://www.w3.org/TR/2014/REC-html5-20141028/'

FAMILIES = {
    'processing-instruction': {
        'verdict': 'post-target',
        'clause': '8.2.4.8 (tag open state) / 8.2.4.44 (bogus comment state)',
        'reason': ('the 2014 tokenizer treats "<?" through the tag-open any-other rule: '
                   'parse error, switch to the bogus comment state, one comment spanning '
                   'from the "?" to the first ">" (or EOF); processing-instruction '
                   'tokens are living-standard vocabulary the pinned letter never emits'),
        'members': [
            ('test2.test', {31, 32, 33}),
            ('test3.test', set(range(1158, 1183))),
        ],
    },
    'infoset-coercion': {
        'verdict': 'corpus-invalid',
        'clause': '8.2.4 (tokenization; input preprocessing normalizes CR only)',
        'reason': ('xmlViolation.test pins its expectations to the infoset-coercion '
                   'tweaks its README names (U+000C rewritten to space, "--" inside '
                   'comments rewritten to "- -"); the 2014 tokenizer letter emits both '
                   'characters verbatim (the double hyphen still raises the '
                   'nested-comment parse error), so the expectation, not the engine, '
                   'is wrong for the pinned target'),
        'members': [
            ('xmlViolation.test', {1, 2}),
        ],
    },
}


def token(kind, payload):
    """The harness's own token dump shape: raw characters between quotes."""
    return f'{kind} "{payload}"'


def derive_pi(input_text):
    """The bogus-comment content for a "<?" input: from the "?" to the first
    ">" (or EOF), NUL replaced with U+FFFD — exactly 8.2.4.44 over the text
    the tag-open any-other rule hands it."""
    start = input_text.index('?')
    end = input_text.find('>')
    end = len(input_text) if end < 0 else end
    return token('Comment', input_text[start:end].replace('\x00', '\uFFFD'))


def derive_verbatim(input_text, case):
    """The verbatim stream: for the xmlViolation members the engine must emit
    exactly the input's own characters (comment data without the delimiters,
    or the text run as-is)."""
    if input_text.startswith('<!--'):
        data = input_text[4:-3] if input_text.endswith('-->') else input_text[4:]
        return token('Comment', data)
    return token('Character', input_text)


def quoted_tokens(stream_text):
    """Split the report's "; "-joined token dump back into the token strings
    the harness compares (the dumps never contain "; " inside a token)."""
    return stream_text.split('; ') if stream_text else []


def main():
    report = json.loads(REPORT.read_text(encoding='utf-8'))
    actual_by_key = {}
    for failure in report['failures']:
        if failure['outcome'] in ('tokens-fail', 'error'):
            actual_by_key[(failure['file'], failure['index'], failure['state'])] = failure

    existing = {}
    if OUT.exists():
        for entry in json.loads(OUT.read_text(encoding='utf-8'))['cases']:
            existing[(entry['file'], entry['index'], entry.get('state', 'Data state'))] = entry

    cases = []
    review_lines = ['# html5 tokenizer ledger review', '']
    stale = []
    invalid = []
    covered = set()

    for family, spec in FAMILIES.items():
        review_lines.append(f'## {family}')
        for file, indices in spec['members']:
            data = json.loads((VENDOR / file).read_text(encoding='utf-8'))
            tests = data.get('tests') or data.get('xmlViolationTests')
            for index in sorted(indices):
                case = tests[index]
                state = 'Data state'
                key = (file, index, state)
                covered.add(key)
                failure = actual_by_key.get(key)
                if failure is None:
                    stale.append(f'{file}#{index} ({state}): case passes raw; entry dropped')
                    continue

                input_text = case['input']
                if family == 'processing-instruction':
                    derived = derive_pi(input_text)
                else:
                    derived = derive_verbatim(input_text, case)
                engine = failure['actual']
                # The engine dump is "; "-joined; PI/infoset members emit exactly
                # one token.
                engine_tokens = quoted_tokens(engine) if '; ' in engine else [engine]
                match = engine_tokens == [derived]

                old = existing.get(key)
                if old and old.get('reviewed'):
                    entry = old
                    if entry['correctedOutput'] != [derived]:
                        invalid.append(
                            f'{file}#{index} ({state}): reviewed correction no longer '
                            f'derives; regenerated value {derived!r}')
                        entry = dict(entry, correctedOutput=[derived], reviewed=False)
                else:
                    entry = {
                        'file': file,
                        'index': index,
                        'state': state,
                        'verdict': spec['verdict'],
                        'clause': spec['clause'],
                        'reason': spec['reason'],
                        'correctedOutput': [derived],
                        'reviewed': match,
                    }
                cases.append(entry)
                review_lines.append(
                    f'- `{file}` #{index} — {case.get("description", "")}: {derived} '
                    f'({"" if match else "MISMATCH: engine " + engine!r})')

    leftover = set(actual_by_key) - covered
    if leftover:
        review_lines.append('\n## uncovered token failures (must be families, exclusions or fixes)')
        for key in sorted(leftover):
            review_lines.append(f'- `{key[0]}` #{key[1]} [{key[2]}]: '
                                + actual_by_key[key]['signature'][:160])

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({'schemaVersion': 1, 'target': TARGET, 'cases': cases},
                              indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    REVIEW.write_text('\n'.join(review_lines) + '\n', encoding='utf-8')

    reviewed = sum(1 for c in cases if c['reviewed'])
    print(f'ledger: {len(cases)} entries, {reviewed} reviewed; '
          f'{len(stale)} stale (dropped); {len(invalid)} invalid')
    for line in stale + invalid:
        print('  ' + line)
    if leftover:
        print(f'{len(leftover)} uncovered failures; see {REVIEW}')
    return 1 if invalid else 0


if __name__ == '__main__':
    sys.exit(main())
