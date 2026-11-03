#!/usr/bin/env python3
"""Build the HTML parse-fidelity worklist from the attribution pass.

Groups the recorded failures of artifacts/html5-parse-attribution.json into
families (corpus-expectation issues versus genuine engine deviations), ranked
by size, and writes a checkbox worklist plus a per-case JSON sidecar for
--filter lists.

The attribution pass is the HTML analog of the CSS 2.1 probe batches: it runs
every html5lib tree-construction case through the real pipeline and classifies
each non-pass as an engine deviation (anglesharp), a pipeline defect
(pipeline), or a diagnostic artifact (pipeline-fixes/mixed). This script adds
no verdicts of its own; it only regroups what the pass recorded.

Run from the repo root:
    python -I scripts/gen_html5_parse_worklist.py
"""
import json
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path('.')
ATTR = ROOT / 'Lite.Conformance/artifacts/html5-parse-attribution.json'
OUT_MD = ROOT / 'Lite.Conformance/artifacts/html5-parse-worklist.md'
OUT_JSON = ROOT / 'Lite.Conformance/artifacts/html5-parse-worklist.json'

# Families the plan tracks, keyed by (file, index). Each case joins exactly one
# family; anything unlisted lands in "triage" so new failures are never silent.
FAMILIES = {
    'processing-instructions': ('corpus-invalid', 'rec-8.2.4.42', [
        ('processing-instructions.dat', None), ('tests1.dat', {39, 40, 43, 46}),
        ('html5test-com.dat', {11}),
    ]),
    'select-mode': ('corpus-invalid', 'post-target select rework; engine must match the 2014 letter', [
        ('webkit02.dat', {25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 37, 39, 42, 44, 45, 46, 47}),
        ('tests_innerHTML_1.dat', {75}), ('tests1.dat', {29, 99}),
    ]),
    'isindex': ('corpus-invalid', 'post-target; REC 8.2.5.4.7 expansion stands', [
        ('isindex.dat', {0, 1, 2, 3}), ('tests19.dat', {7}),
    ]),
    'foreign-attributes': ('corpus-invalid', 'post-target adjustment table (tests11)', [
        ('tests11.dat', {2, 4, 5, 6}),
    ]),
    'nul-in-head': ('corpus-invalid', 'post-target NUL handling (patch 005 note)', [
        ('plain-text-unsafe.dat', {1, 2, 4, 5}),
    ]),
    'search-element': ('corpus-invalid', 'post-target 2023 element', [
        ('search-element.dat', {0, 1}),
    ]),
    'keygen-corpus-conflict': ('corpus-invalid', 'tests7 #33 contradicts webkit02 #43', [
        ('tests7.dat', {33}),
    ]),
    'foreign-fragments': ('engine', 'math/svg fragment-context roots and attr names', [
        ('tests9.dat', {4, 5, 17, 18}), ('tests10.dat', {3, 4, 16, 17}),
        ('foreign-fragment.dat', {61, 62}), ('namespace-sensitivity.dat', {0}),
    ]),
    'plaintext-residual': ('engine', 'tests18 plaintext', [
        ('tests18.dat', {13, 14}),
    ]),
    'frameset-whitespace': ('engine', 'tests2 two-space run (patch 007 leftover)', [
        ('tests2.dat', {7}),
    ]),
    'menuitem-formatting': ('engine', 'menuitem formatting reconstruction (patch 001 leftover)', [
        ('menuitem-element.dat', {13}),
    ]),
    'scripted-write': ('diagnostic', 'document.write through the legacy top-level path', [
        ('scripted_webkit01.dat', {0, 1}), ('scripted_adoption01.dat', {0}),
        ('scripted_ark.dat', {0}),
    ]),
}


def family_of(file: str, index: int) -> str:
    for name, (_, _, members) in FAMILIES.items():
        for member_file, indices in members:
            if member_file == file and (indices is None or index in indices):
                return name
    return 'triage'


def main() -> None:
    attr = json.loads(ATTR.read_text(encoding='utf-8'))
    failures = attr['failures']
    totals = attr['totals']
    engine_pass = totals.get('pass', 0) + totals.get('pipeline-fixes', 0)
    total_cases = sum(f['cases'] for f in attr['files'])

    grouped = defaultdict(list)
    for f in failures:
        grouped[family_of(f['file'], f['index'])].append(f)

    def rank(f):
        return (f['classification'] != 'anglesharp', f['file'], f['index'])

    kinds = {name: kind for name, (kind, _, _) in FAMILIES.items()}

    md = []
    md.append(f"# HTML parse-fidelity worklist\n")
    md.append(f"Source: `artifacts/html5-parse-attribution.json` "
              f"({attr['generatedUtc']}).\n")
    md.append(f"Engine-pass {engine_pass}/{total_cases} unique cases "
              f"({100.0 * engine_pass / total_cases:.1f}%). "
              f"{len(failures)} recorded failures: "
              f"{totals.get('anglesharp', 0)} anglesharp, "
              f"{totals.get('pipeline', 0)} pipeline, "
              f"{totals.get('mixed', 1)} mixed, "
              f"{totals.get('pipeline-fixes', 0)} pipeline-fixes (counted "
              f"engine-pass; diagnostic artifacts).\n")
    md.append("Ledger candidates (corpus-expectation issues) are marked "
              "`[ledger]`; genuine engine deviations are marked "
              "`[engine]` and feed the fork-patch queue.\n")

    order = sorted(grouped, key=lambda n: (n == 'triage', -len(grouped[n])))
    for name in order:
        kind = kinds.get(name, 'engine')
        tag = '[ledger]' if kind == 'corpus-invalid' else (
            '[diagnostic]' if kind == 'diagnostic' else '[engine]')
        cases = sorted(grouped[name], key=rank)
        md.append(f"\n## {tag} {name} — {len(cases)} case(s)"
                  f"{' — UNFAMILIAR, triage first' if name == 'triage' else ''}\n")
        for f in cases:
            frag = f" frag={f['fragment']}" if f['fragment'] else ''
            md.append(f"- [ ] `{f['file']}#{f['index']}`{frag} "
                      f"({f['classification']}) {f['signature']}")

    OUT_MD.write_text('\n'.join(md) + '\n', encoding='utf-8')

    sidecar = {
        'formatVersion': 1,
        'kind': 'html5-parse-worklist',
        'generatedUtc': attr['generatedUtc'],
        'enginePass': engine_pass,
        'totalCases': total_cases,
        'families': {
            name: {
                'kind': kinds.get(name, 'engine'),
                'cases': [{'file': f['file'], 'index': f['index'],
                           'fragment': f['fragment'],
                           'classification': f['classification']}
                          for f in sorted(grouped[name], key=rank)],
            }
            for name in order
        },
    }
    OUT_JSON.write_text(json.dumps(sidecar, indent=2, ensure_ascii=False) + '\n',
                        encoding='utf-8')

    print(f"worklist written: {OUT_MD} ({len(failures)} failures in "
          f"{len(grouped)} families; engine-pass {engine_pass}/{total_cases})")


if __name__ == '__main__':
    main()
