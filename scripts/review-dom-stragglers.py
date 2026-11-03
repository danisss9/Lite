#!/usr/bin/env python3
"""Close the 24 unreviewed dom-straggler entries of Wpt/html5-applicability.json
(plan html5-conformance full-conformance, applicability review: the dom root).

The 20 reviewed-failing files are 2014-target DOM4/REC obligations the engine
does not satisfy yet: they classify as included, which makes the readiness
check demand executed passing evidence per file — the mechanism that feeds the
Milestone 4 engine queue. The 4 deferred passes resolve per the reviewed
sibling's conventions (Event-defaultPrevented.html: dedicated returnValue
subtests are post-target members, while legacy-alias reads such as srcElement
embedded inside 2014-target subtests do not demote the subtest).

Existing entries always win — this script only fills entries whose
classification is still unreviewed. The result must satisfy the applicability
schema's structural invariants.
"""
import json
import re
import sys
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')

DECISIONS = {
    # --- the 4 deferred passes: 2014-target obligations with embedded legacy reads ---
    'dom/events/Event-stopImmediatePropagation.html':
        'Reviewed 2026-10-12; decided 2026-11-02 (included, passes): the single subtest asserts '
        'stopImmediatePropagation dispatch semantics and the stop-propagation flag\'s cancelBubble '
        'reflection — both DOM4 2014 (WD-dom-20140710) section 6.3 (Event dispatch), the DOM '
        'dependency HTML 5.0 references (§2.6); cancelBubble is a 2014 DOM4 attribute.',
    'dom/events/Event-defaultPrevented-after-dispatch.html':
        'Reviewed 2026-10-12; decided 2026-11-02 (included, passes): the subtests assert '
        'defaultPrevented during and after dispatch and the target — DOM4 2014 section 5.8 '
        '(canceled flag, dispatch), 2014-target; the embedded srcElement alias reads stay '
        '2014-consistent per the reviewed sibling Event-defaultPrevented.html convention '
        '(embedded legacy-alias reads do not demote the subtest; only dedicated returnValue '
        'subtests split as post-target).',
    'dom/events/Event-initEvent.html':
        'Reviewed 2026-10-12; decided 2026-11-02 (included, passes): the subtests assert the '
        'DOM4 2014 section 5.8 initEvent property-initialization and re-initialization steps '
        '(type/bubbles/cancelable/defaultPrevented/isTrusted/target) — 2014-target. The four '
        '"Properties of initEvent(type, B, C)" subtests embed a returnValue read: a legacy alias '
        'absent from the 2014 DOM4 draft, but embedded inside a 2014-target subtest, so per the '
        'reviewed sibling convention the subtest does not split (only dedicated returnValue '
        'subtests classify post-target); the read is documented here.',
    'dom/events/Event-dispatch-detached-click.html':
        'Reviewed 2026-10-12; decided 2026-11-02 (included, passes): the single subtest asserts '
        'dispatchEvent to a detached element — DOM4 2014 section 7 (dispatch: the event path of '
        'a node outside a document), 2014-target; the srcElement read is a 2014-consistent '
        'legacy alias per the reviewed sibling convention.',
}


def main():
    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    tests = manifest['tests']
    changed = 0
    for entry in tests:
        if entry['classification'] != 'unreviewed':
            continue
        path = entry['path']
        if path in DECISIONS:
            entry['classification'] = 'included'
            entry['reason'] = DECISIONS[path]
            changed += 1
            continue
        if not path.startswith('dom/'):
            continue
        prior = entry.get('reason') or ''
        if '(failing)' in prior:
            entry['classification'] = 'included'
            entry['reason'] = re.sub(
                r'^Reviewed (\S+) \(failing\):',
                r'Reviewed \1; decided 2026-11-02 (included, reviewed-failing — feeds the '
                r'Milestone 4 engine queue):',
                prior)
            changed += 1

    # Sanity: no dom unreviewed left, schema invariants hold.
    remaining = [t for t in tests if t['classification'] == 'unreviewed' and t['path'].startswith('dom/')]
    if remaining:
        print('ERROR: dom unreviewed entries remain:', [t['path'] for t in remaining])
        sys.exit(1)
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
    print(f'dom stragglers closed: {changed} entries now included '
          f'({sum(1 for t in tests if t["path"].startswith("dom/") and t["classification"] == "unreviewed")} '
          f'dom unreviewed remain)')


if __name__ == '__main__':
    main()
