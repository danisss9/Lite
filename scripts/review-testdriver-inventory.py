#!/usr/bin/env python3
"""Inventory the testdriver-flagged catalog sources into Wpt/html5-applicability.json.

Milestone 1's inventory completion: the applicability manifest must give every
catalog candidate a classification before inventoryComplete can flip (the
guarded check in ProfileRunner detects omissions from the catalog, not from the
flag). The propose tool's `needs/testdriver` batch names 481 sources that were
blocked on input automation — a capability that landed 2026-11-03 (Milestone 3,
the plan's checkpoint). This script consumes the survey evidence recorded under
that capability (artifacts/testdriver-inventory-survey.json over
artifacts/testdriver-inventory-queue.txt) and ADDS a manifest entry per surveyed
source: a full pass classifies included (passes); a measured non-pass classifies
included, reviewed-failing — the dom-straggler convention that feeds the
Milestone 4 engine queue. Existing entries always win; sources absent from the
survey are left unmanifested. The result must satisfy the applicability schema's
structural invariants.
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
survey_path = Path('Lite.Conformance/artifacts/testdriver-inventory-survey.json')
DECIDED = '2026-11-03'
CAPABILITY = 'the landed testdriver input automation (Milestone 3)'

DOMAINS = [
    ('dom/ranges/', 'the engine has no Range/Selection surface (the DOM dependency the REC '
                    'references defines ranges; Lite implements none) — the Milestone 4 '
                    'editing and selection package owns the cluster'),
    ('dom/events/', 'event semantics — owned by the Milestone 4 events and messaging package'),
    ('dom/nodes/', 'node tree mutation and traversal semantics — owned by the Milestone 4 '
                   'DOM authority package'),
    ('html/editing/', 'the engine has no Selection/Range/execCommand surface (REC §7.5 Editing '
                      'via the pinned Editing APIs dependency) — the Milestone 4 editing and '
                      'selection package owns the cluster'),
    ('html/interaction/', 'focus and interaction semantics (REC §3.2/§7.4) — owned by the '
                          'Milestone 4 DOM authority and interaction work'),
    ('html/browsers/', 'window, dialog and browsing-context semantics (REC §5/§6) — owned by '
                       'the Milestone 4 events, origins/sandbox and history packages'),
    ('cookies/', 'cookie storage attributes (REC §4.12.2 via the document object) — the '
                 'engine keeps a single static cookie dictionary; the Milestone 4 cookies '
                 'package owns the cluster'),
    ('fetch/', 'fetching semantics the REC maps as a dependency — owned by the Milestone 4 '
               'networking area'),
    ('selection/', 'the engine has no Selection/Range surface — the Milestone 4 editing and '
                   'selection package owns the cluster'),
    ('uievents/', 'UI event semantics (the UI Events dependency) — owned by the Milestone 4 '
                  'events and messaging package'),
]


def domain(path):
    for prefix, text in DOMAINS:
        if path.startswith(prefix):
            return text
    return 'engine behavior owned by the Milestone 4 queue'


def failing_summary(subtests, limit=2):
    failed = [s for s in subtests if s.get('status') != 0]
    parts = []
    for s in failed[:limit]:
        name = (s.get('name') or '').strip()
        message = (s.get('message') or '').strip()
        piece = name[:120]
        if message:
            piece += f" ({message[:160]})"
        parts.append(piece)
    suffix = f'; and {len(failed) - limit} more' if len(failed) > limit else ''
    return '; '.join(parts) + suffix if parts else 'no subtest detail recorded'


def main():
    if not survey_path.exists():
        print(f'ERROR: survey evidence not found: {survey_path}')
        sys.exit(1)
    report = json.loads(survey_path.read_text(encoding='utf-8-sig'))
    if not report.get('completed'):
        print('ERROR: survey report is incomplete — rerun the survey before classifying.')
        sys.exit(1)
    outcomes = {t['path']: t for t in report['tests'] if t.get('suite') == 'wpt'}

    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    tests = manifest['tests']
    existing = {t['path'] for t in tests}
    added = passes = failures = 0
    for path in sorted(outcomes):
        if path in existing:
            continue
        evidence = outcomes[path]
        outcome = evidence.get('outcome')
        subtests = evidence.get('subtests') or []
        total = len(subtests)
        failed = sum(1 for s in subtests if s.get('status') != 0)
        if outcome not in ('pass', 'fail', 'crash', 'timeout', 'empty'):
            continue  # unsupported/skipped sources stay unmanifested pending their capability
        if outcome == 'pass':
            tests.append({'path': path, 'classification': 'included',
                          'reason': (f'Reviewed {DECIDED} (inventory batch): executed under '
                                     f'{CAPABILITY}; every subtest passes.')})
            passes += 1
        else:
            if outcome == 'fail':
                measured = (f'{failed}/{total} subtests fail — {failing_summary(subtests)}'
                            if total else 'the file reports no passing subtests')
            elif outcome == 'timeout':
                measured = 'the run times out without reporting results'
            elif outcome == 'crash':
                measured = f'the page crashes the harness ({(evidence.get("detail") or "")[:160]})'
            else:
                measured = 'no subtests are reported'
            tests.append({'path': path, 'classification': 'included',
                          'reason': (f'Reviewed {DECIDED} (inventory batch; included, '
                                     f'reviewed-failing — feeds the Milestone 4 engine queue): '
                                     f'executed under {CAPABILITY}; {measured}. Engine gap: '
                                     f'{domain(path)}.')})
            failures += 1
        added += 1

    for entry in tests:
        if entry['classification'] not in ('included', 'mixed', 'post-target', 'profile-excluded',
                                           'regression-only', 'unreviewed'):
            print('ERROR: invalid classification:', entry['path'])
            sys.exit(1)
        if not entry.get('reason'):
            print('ERROR: empty reason:', entry['path'])
            sys.exit(1)

    applicability_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n',
                                  encoding='utf-8')
    print(f'testdriver inventory batch: {added} entries added '
          f'({passes} pass, {failures} reviewed-failing); '
          f'manifest now {len(tests)} entries {dict(Counter(t["classification"] for t in tests))}')


if __name__ == '__main__':
    main()
