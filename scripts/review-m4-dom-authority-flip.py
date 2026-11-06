#!/usr/bin/env python3
"""Close the DOM authority package's cluster entries in Wpt/html5-applicability.json.

The package's cluster is the dom root minus dom/events (the events-and-messaging
package's rows): dom/nodes, dom/traversal, dom/collections and dom/lists. After
the package lands and the cluster rerun executes on one build identity, every
cluster entry whose expanded case set is fully passing in the evidence report
gets a closure note appended to its reason — the original review text always
wins, so the note is append-only. Entries without complete passing evidence
timeouts, crashes, partial failures, or no report row at all — stay untouched:
the reviewed-failing queue only drains on measured passes, never on a sample.
"""
import json
import sys
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
report_path = Path(sys.argv[1] if len(sys.argv) > 1
                   else 'Lite.Conformance/artifacts/html5-results.json')
closed_note = (' Closed 2026-11-05: the DOM authority package landed (live collection '
               'platform-object semantics, getElementsByTagNameNS, the verbatim DOMTokenList '
               'stringifier and per-interface coverage, DOMStringMap/NamedNodeMap enumeration, '
               'the title engine-tree authority, initial-parse comment projection, the '
               'wrong-typed-wrapper TypeError, query-path origin-sheet exclusion, and the '
               'TreeWalker/NodeIterator residuals) and this entry\'s full case set passes on '
               'the cluster rerun.')


def cluster(path: str) -> bool:
    return path.startswith('dom/') and not path.startswith('dom/events/')


def main():
    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    report = json.loads(report_path.read_text(encoding='utf-8-sig'))
    outcomes = {}
    for row in report.get('tests', []):
        outcomes.setdefault(row['path'], []).append(row)

    flipped, failing, unmatched = [], [], []
    for entry in manifest['tests']:
        if entry['classification'] != 'included' or not cluster(entry['path']):
            continue
        # The cluster's dom/nodes rows carry obligations owned by several Milestone 4 packages;
        # only the entries the manifest's own reasons assign to the DOM authority package flip
        # here — a passing row owned by another package stays that package's queue evidence.
        if 'DOM authority' not in entry.get('reason', ''):
            continue
        rows = outcomes.get(entry['path'])
        if not rows:
            unmatched.append(entry['path'])
            continue
        if all(row.get('outcome') == 'pass' for row in rows):
            if 'Closed 2026-11-05' not in entry['reason']:
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
    print(f'DOM authority cluster: {len(flipped)} entries now pass and carry the closure note, '
          f'{len(failing)} still fail, {len(unmatched)} have no report row (left untouched)')
    if failing:
        print('still failing:')
        for path in failing:
            print('  ' + path)
    if unmatched:
        print('no report row:')
        for path in unmatched:
            print('  ' + path)


if __name__ == '__main__':
    main()
