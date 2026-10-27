#!/usr/bin/env python3
"""Workstream 5: the only writer that flips html5 requirement statuses in the
shared profile (plan-html5-full-conformance-harmonic-treasure, WS5).

For every `html5` requirement with applicability `included` and at least one
mapped test, the script decides from identity-matched evidence:

- flip `untested` -> `implemented` only when EVERY mapped case has passing
  evidence in the supplied reports AND the reports were produced by exactly
  the current source tree (identity.SourceSha256 equals the hash of the
  working tree, computed the same way the harness computes it);
- flip `implemented` -> `untested` on any regression: a mapped case whose
  evidence is missing, failing, or no longer identity-matched.

Requirements with no mapped tests stay untested and are listed as the
remaining work; nothing is invented. A requirement whose mapped tests include
a `mixed` applicability review passes only under that review's rules
(target assertions pass; reviewed non-target assertions may fail) — the same
rule the C# gate applies.

The profile is written only when a status changes; every decision is printed.

Run from the repo root after collecting evidence:
    python -I scripts/gen_html5_obligation_status.py \
        --evidence Lite.Conformance/artifacts/html5-results.json \
        --evidence Lite.Conformance/artifacts/wpt-results.json
"""
import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROFILE = ROOT / 'Lite.Conformance/Profile/lite-html5-css21-es2020-profile.json'
APPLICABILITY = ROOT / 'Lite.Conformance/Wpt/html5-applicability.json'
LOCK = ROOT / 'Lite.Conformance/test-suites.lock.json'


def source_sha256() -> str:
    """The harness's SourceSha256: name+content hash over every tracked file."""
    names = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
                           cwd=ROOT, capture_output=True, check=True).stdout.decode('utf-8')
    digest = hashlib.sha256()
    for name in sorted(n for n in names.split('\0') if n):
        digest.update((name + '\0').encode('utf-8'))
        file = ROOT / name
        if file.is_file():
            digest.update(hashlib.sha256(file.read_bytes()).hexdigest().encode('utf-8'))
        else:
            digest.update(b'deleted')
    return digest.hexdigest()


def has_passing_evidence(evidence: dict, suite: str, path: str, assertion: str | None,
                         review: dict | None) -> bool:
    tests = [t for t in evidence.get('tests', [])
             if t.get('suite') == suite and (t.get('path') == path or t.get('path', '').split('?')[0] == path)]
    if not tests:
        return False
    test = tests[0]
    subtests = test.get('subtests') or []
    if review is not None and review.get('classification') == 'mixed':
        # A mixed test may fail ONLY in reviewed non-target assertions; the
        # assertion set must match the recorded subtests one to one.
        if test.get('outcome') not in ('pass', 'fail') or not review.get('assertionInventoryComplete'):
            return False
        expected = {a['name']: a for a in review.get('assertions', [])}
        actual = {s['name']: s for s in subtests}
        if len(expected) != len(review.get('assertions', [])) or len(actual) != len(subtests):
            return False
        if set(expected) != set(actual):
            return False
        if not any(a.get('classification') == 'included' for a in expected.values()):
            return False
        for name, a in expected.items():
            if a.get('classification') == 'included' and actual[name].get('status') != 0:
                return False
            if actual[name].get('status') not in (0, 1):
                return False
        return True
    if test.get('outcome') != 'pass' or not subtests:
        return False
    if any(s.get('status') != 0 for s in subtests):
        return False
    if assertion and not any(s.get('name') == assertion for s in subtests):
        return False
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--evidence', action='append', default=[],
                        help='evidence report to trust; may be repeated')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if not args.evidence:
        print('No --evidence reports given; refusing to decide from nothing.')
        return 2

    current = source_sha256()
    reports = []
    for raw in args.evidence:
        path = Path(raw)
        if not path.exists():
            print(f'Missing evidence report: {path}')
            return 2
        report = json.loads(path.read_text(encoding='utf-8'))
        identity = report.get('identity') or {}
        if identity.get('sourceSha256') != current:
            print(f'Evidence {path.name} is stale (sourceSha256 differs from the working tree); '
                  'recollect it after the next build. Nothing was decided.')
            return 2
        reports.append(report)

    reviews = {}
    if APPLICABILITY.exists():
        reviews = {t['path']: t for t in json.loads(APPLICABILITY.read_text(encoding='utf-8'))['tests']}

    profile = json.loads(PROFILE.read_text(encoding='utf-8'))
    changes = []
    untested_without_tests = 0
    for requirement in profile['requirements']:
        if requirement.get('specification') != 'html5' or requirement.get('applicability') != 'included':
            continue
        mapped = requirement.get('tests') or []
        if not mapped:
            if requirement.get('status') == 'untested':
                untested_without_tests += 1
            continue
        complete = all(
            has_passing_evidence(report, t['suite'], t['path'], t.get('assertion'),
                                 reviews.get(t['path']))
            for t in mapped for report in reports) and any(
            any(has_passing_evidence(report, t['suite'], t['path'], t.get('assertion'),
                                     reviews.get(t['path'])) for report in reports)
            for t in mapped)
        if complete and requirement.get('status') != 'implemented':
            changes.append((requirement['id'], requirement.get('status'), 'implemented'))
            requirement['status'] = 'implemented'
        elif not complete and requirement.get('status') == 'implemented':
            changes.append((requirement['id'], 'implemented', 'untested'))
            requirement['status'] = 'untested'

    if args.dry_run:
        for req_id, before, after in changes:
            print(f'  would flip {req_id}: {before} -> {after}')
        print(f'{len(changes)} statuses would change; profile not written.')
        return 0
    if not changes:
        print(f'No status changes. {untested_without_tests} included html5 requirements remain '
              'untested without mapped tests (the remaining work queue).')
        return 0
    PROFILE.write_text(json.dumps(profile, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    for req_id, before, after in changes:
        print(f'  flipped {req_id}: {before} -> {after}')
    print(f'{len(changes)} statuses written to the profile.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
