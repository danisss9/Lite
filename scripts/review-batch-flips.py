#!/usr/bin/env python3
"""Generic reviewed-batch flip tool for Wpt/html5-applicability.json.

Two operations, both append-only on reasons (the original review text always wins):

flip    — every roster entry (matched by --filter substrings over entry paths, or an
          explicit --roster file of paths) whose expanded case set is FULLY passing in
          the evidence report(s) gets a closure note appended to its reason. Entries
          without complete passing evidence stay untouched: the reviewed-failing queue
          only drains on measured passes, never on a sample.
reclass — the explicit --reclass path=classification entries (one per line, optionally
          path|classification|reason) get their classification changed with the reviewed
          reason replacing/annotating the old one. Used for later-feature assertions
          the original review could not split (post-target with justification).

Schema invariants are enforced after every edit: classifications must be valid, reasons
non-empty. The manifest is written back with ensure_ascii=False, indent=1 (the
established format), so the diff stays reviewable.
"""
import argparse
import json
import sys
from pathlib import Path

VALID = ('included', 'mixed', 'post-target', 'profile-excluded', 'regression-only', 'unreviewed')


def load_report_paths(paths):
    outcomes = {}
    for p in paths:
        report = json.loads(Path(p).read_text(encoding='utf-8-sig'))
        for row in report.get('tests', []):
            outcomes.setdefault(row['path'], []).append(row)
    return outcomes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--op', choices=['flip', 'reclass'], required=True)
    ap.add_argument('--note', default='', help='closure note appended on flip (idempotent by marker)')
    ap.add_argument('--marker', default='', help='idempotency marker substring; default derives from note')
    ap.add_argument('--filters', default='', help='comma-separated substrings over entry paths')
    ap.add_argument('--roster', default='', help='file with one entry path per line')
    ap.add_argument('--reports', nargs='*', default=['Lite.Conformance/artifacts/html5-results.json'])
    ap.add_argument('--reclass', default='', help='file with path|classification|reason lines (reclass op)')
    ap.add_argument('--apply', action='store_true', help='write the manifest (default: dry run)')
    args = ap.parse_args()

    manifest_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    outcomes = load_report_paths(args.reports)

    flipped, failing, unmatched, notincluded = [], [], [], []
    if args.op == 'flip':
        terms = [t.strip() for t in args.filters.split(',') if t.strip()]
        roster = set()
        if args.roster:
            roster = {line.strip() for line in Path(args.roster).read_text(encoding='utf-8').splitlines()
                      if line.strip()}
        marker = args.marker or (args.note[:40] if args.note else 'Closed')
        for entry in manifest['tests']:
            path = entry['path']
            if roster and path not in roster:
                continue
            if terms and not any(t in path for t in terms):
                continue
            if entry['classification'] != 'included':
                if roster:
                    notincluded.append((path, entry['classification']))
                continue
            rows = outcomes.get(path)
            if not rows:
                unmatched.append(path)
                continue
            if all(row.get('outcome') == 'pass' for row in rows):
                if marker not in entry['reason']:
                    entry['reason'] = entry['reason'] + ' ' + args.note
                flipped.append(path)
            else:
                failing.append(path)

    reclassed = []
    if args.op == 'reclass':
        wanted = {}
        for line in Path(args.reclass).read_text(encoding='utf-8').splitlines():
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            parts = line.split('|')
            if len(parts) < 2:
                print('ERROR: bad reclass line (need path|classification|reason):', line)
                sys.exit(1)
            wanted[parts[0].strip()] = (parts[1].strip(), parts[2].strip() if len(parts) > 2 else '')
        index = {e['path']: e for e in manifest['tests']}
        for path, (classification, reason) in wanted.items():
            entry = index.get(path)
            if entry is None:
                unmatched.append(path)
                continue
            if classification not in VALID:
                print('ERROR: invalid classification:', classification)
                sys.exit(1)
            if entry['classification'] == classification and reason in entry.get('reason', ''):
                continue
            old = entry['classification']
            entry['classification'] = classification
            note = (' Reclassified 2026-11-12 from %s: %s' % (old, reason)) if reason else \
                (' Reclassified 2026-11-12 from %s.' % old)
            entry['reason'] = entry['reason'] + note
            reclassed.append(path)

    # Schema invariants: classifications valid, reasons non-empty.
    for entry in manifest['tests']:
        if entry['classification'] not in VALID:
            print('ERROR: invalid classification:', entry['path'])
            sys.exit(1)
        if not entry.get('reason'):
            print('ERROR: empty reason:', entry['path'])
            sys.exit(1)

    if args.apply:
        manifest_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')

    if args.op == 'flip':
        print(f'flip: {len(flipped)} pass (note {"applied" if args.apply else "DRY RUN"}), '
              f'{len(failing)} still fail, {len(unmatched)} no report row, {len(notincluded)} not included')
    else:
        print(f'reclass: {len(reclassed)} reclassified, {len(unmatched)} unmatched')
    for label, rows in (('still failing', failing[:20]), ('no report row', unmatched[:20])):
        if rows:
            print(f'{label} (first {min(20, len(rows))}):')
            for path in rows:
                print('  ' + path)


if __name__ == '__main__':
    main()
