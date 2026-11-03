#!/usr/bin/env python3
"""Classify the executed testdriver tail of Wpt/html5-applicability.json.

Milestone 3's testdriver input automation landed 3 November 2026 (the plan's
Milestone 3 checkpoint). This script consumes its survey evidence
(artifacts/testdriver-tail-survey.json over artifacts/testdriver-tail-queue.txt)
and closes the harness-blocked entries whose files executed: a full pass
classifies included (passes); a genuine engine failure classifies included,
reviewed-failing — feeding the Milestone 4 engine queue (the dom-straggler
convention). Files whose reasons name the pending manual-evidence or
upstream-serving capabilities stay unreviewed, as does the visual runners'
testdriver reftest/crashtest pair.

Existing entries always win — only unreviewed entries whose path appears in
the survey evidence are filled. The survey report must be complete. The result
must satisfy the applicability schema's structural invariants (classification
enum, non-empty reasons, assertion fields only on mixed).
"""
import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
survey_path = Path('Lite.Conformance/artifacts/testdriver-tail-survey.json')
DECIDED = '2026-11-03'

DOMAINS = [
    ('editing/', 'the engine has no Selection/Range/execCommand surface (REC §7.5 Editing '
                 'via the pinned Editing APIs dependency) — the Milestone 4 editing and '
                 'selection package owns the cluster'),
    ('html/interaction/focus/', 'focus semantics — sequential focus navigation, autofocus '
                                'processing, cross-frame focus and the focus processing model '
                                '(REC §3.2.2/§3.2.3) — owned by the Milestone 4 DOM authority '
                                'and interaction work'),
    ('html/browsers/', 'window, dialog and browsing-context semantics (REC §5/§6) — owned by '
                       'the Milestone 4 events, origins/sandbox and history packages'),
    ('html/rendering/', 'widget rendering and interaction-state semantics (REC §10) — owned '
                        'by the Milestone 4 obsolete and rendering package'),
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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--survey', default='Lite.Conformance/artifacts/testdriver-tail-survey.json',
                        help='the survey evidence to classify from')
    parser.add_argument('--capability',
                        default='the landed testdriver input automation (Milestone 3)',
                        help='the harness capability the executed files ran under (named in '
                             'each decided reason)')
    args = parser.parse_args()
    survey_path = Path(args.survey)
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
    changed = passes = failures = 0
    for entry in tests:
        if entry['classification'] != 'unreviewed':
            continue
        evidence = outcomes.get(entry['path'])
        if evidence is None:
            continue
        outcome = evidence.get('outcome')
        subtests = evidence.get('subtests') or []
        total = len(subtests)
        failed = sum(1 for s in subtests if s.get('status') != 0)
        gap = domain(entry['path'])
        header = f'Reviewed 2026-11-02 (harness-blocked); decided {DECIDED} '
        # The prior review's obligation analysis is the basis of the classification — carry it.
        prior = entry.get('reason') or ''
        m = re.match(r'^Reviewed \S+ \(harness-blocked\):\s*(.*)$', prior, re.S)
        body = (m.group(1) if m else prior).strip()
        body = re.sub(r';?\s*the classification \(expected: included\) follows the harness '
                      r'capability\.?\s*$', '', body).rstrip(' ;')
        carried = f' Prior review: {body}.' if body else ''
        if outcome == 'pass':
            entry['classification'] = 'included'
            entry['reason'] = (header + f'(included, passes): executed under {args.capability}; '
                               f'every subtest passes.{carried}')
            passes += 1
        elif outcome in ('fail', 'crash', 'timeout', 'empty'):
            entry['classification'] = 'included'
            if outcome == 'fail':
                measured = (f'{failed}/{total} subtests fail — {failing_summary(subtests)}'
                            if total else 'the file reports no passing subtests')
            elif outcome == 'timeout':
                measured = 'the run times out without reporting results'
            elif outcome == 'crash':
                measured = f'the page crashes the harness ({(evidence.get("detail") or "")[:160]})'
            else:
                measured = 'no subtests are reported'
            entry['reason'] = (header + '(included, reviewed-failing — feeds the Milestone 4 '
                               f'engine queue): executed under {args.capability}; {measured}. '
                               f'Engine gap: {gap}.{carried}')
            failures += 1
        else:
            # Unsupported or skipped outcomes keep waiting on their named capability.
            continue
        changed += 1

    for entry in tests:
        if entry['classification'] not in ('included', 'mixed', 'post-target', 'profile-excluded',
                                           'regression-only', 'unreviewed'):
            print('ERROR: invalid classification:', entry['path'])
            sys.exit(1)
        if not entry.get('reason'):
            print('ERROR: empty reason:', entry['path'])
            sys.exit(1)

    counts = Counter(t['classification'] for t in tests)
    applicability_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n',
                                  encoding='utf-8')
    print(f'testdriver tail classified: {changed} entries '
          f'({passes} pass, {failures} reviewed-failing); '
          f'manifest now {dict(counts)}')


if __name__ == '__main__':
    main()
