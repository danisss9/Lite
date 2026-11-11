#!/usr/bin/env python3
"""Close the DOM authority package's cluster entries in Wpt/html5-applicability.json.

The package's cluster is the Milestone 4 queue table's dom roster minus dom/events
(the events-and-messaging package's rows): the entries listed in
Lite.Conformance/artifacts/m1-queue-dom.txt outside dom/events/ — nodes, traversal,
collections and lists. After the package lands and the cluster rerun executes on one
build identity, every roster entry whose expanded case set is fully passing in the
evidence report gets a closure note appended to its reason — the original review text
always wins, so the note is append-only. Entries without complete passing evidence —
timeouts, crashes, partial failures, or no report row at all — stay untouched: the
reviewed-failing queue only drains on measured passes, never on a sample.
"""
import json
import sys
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
roster_path = Path('Lite.Conformance/artifacts/m1-queue-dom.txt')
report_path = Path(sys.argv[1] if len(sys.argv) > 1
                   else 'Lite.Conformance/artifacts/html5-results.json')
closed_note = (' Closed 2026-11-08: the DOM authority package landed (live collection '
               'platform-object semantics, getElementsByTagNameNS, the verbatim DOMTokenList '
               'stringifier and per-interface coverage, DOMStringMap/NamedNodeMap enumeration, '
               'the title engine-tree authority, initial-parse comment projection, the '
               'wrong-typed-wrapper TypeError, query-path origin-sheet exclusion, and the '
               'TreeWalker/NodeIterator residuals) and this entry\'s full case set passes on '
               'the cluster rerun.')


def main():
    roster = {line.strip() for line in roster_path.read_text(encoding='utf-8').splitlines()
              if line.strip() and not line.strip().startswith('dom/events/')}
    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    report = json.loads(report_path.read_text(encoding='utf-8-sig'))
    outcomes = {}
    for row in report.get('tests', []):
        outcomes.setdefault(row['path'], []).append(row)

    flipped, failing, unmatched, notincluded = [], [], [], []
    for entry in manifest['tests']:
        if entry['path'] not in roster:
            continue
        if entry['classification'] != 'included':
            notincluded.append((entry['path'], entry['classification']))
            continue
        rows = outcomes.get(entry['path'])
        if not rows:
            unmatched.append(entry['path'])
            continue
        if all(row.get('outcome') == 'pass' for row in rows):
            if 'Closed 2026-11-08' not in entry['reason']:
                entry['reason'] = entry['reason'] + closed_note
            flipped.append(entry['path'])
        else:
            failing.append(entry['path'])

    # Schema invariants: classifications valid, reasons non-empty.
    for entry in manifest['tests']:
        if entry['classification'] not in ('included', 'mixed', 'post-target', 'profile-excluded',
                                           'regression-only', 'unreviewed'):
            print('ERROR: invalid classification:', entry['path'])
            sys.exit(1)
        if not entry.get('reason'):
            print('ERROR: empty reason:', entry['path'])
            sys.exit(1)

    applicability_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n',
                                  encoding='utf-8')
    print(f'DOM authority roster: {len(roster)} entries; {len(flipped)} now pass and carry the '
          f'closure note, {len(failing)} still fail, {len(unmatched)} have no report row, '
          f'{len(notincluded)} are not included-classified (left untouched)')
    for label, rows in (('still failing', failing), ('no report row', unmatched)):
        if rows:
            print(f'{label}:')
            for path in rows:
                print('  ' + path)


if __name__ == '__main__':
    main()
