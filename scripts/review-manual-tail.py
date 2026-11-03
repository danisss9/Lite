#!/usr/bin/env python3
"""Close the 8 manual-kind unreviewed entries of Wpt/html5-applicability.json.

These are the last entries of the 2026-11-02 harness-blocked tail: manual-kind
pages (the 2014 REC §5.6 Offline Web applications family and the window-name
restore flow) whose stated procedure is a human interaction — disable the
network connection and refresh, toggle "work offline", click a link that opens
a window and follow its instructions. No headless harness execution can decide
them, so the classifier's survey route does not apply.

They close instead by static review under the dom-straggler convention: the
obligations are 2014-target (REC §5.6 Offline Web applications; §6 session
history and the Window object), and the engine gaps the interactions exercise
are statically verifiable in the source — there is no applicationCache
implementation at all, navigator.onLine is a fixed true with no network-state
source and no online/offline event dispatch, and window.open does not exist
(also measured: the https-suffixed batch's failure-check-sequence and
clear-window-name reject on it). A reviewed applicable test that the engine
currently fails records the review in text while the machine state joins the
gate as included reviewed-failing — the mechanism that feeds the Milestone 4
engine queue. Human observation on a native Windows build (the manual-evidence
recordings) remains the Milestone 6 evidence step for these obligations.

Existing entries always win — only unreviewed entries are filled. The result
must satisfy the applicability schema's structural invariants.
"""
import json
import sys
from collections import Counter
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
DECIDED = '2026-11-03'

HEADER = ('Reviewed 2026-11-02 (harness-blocked); decided ' + DECIDED +
          ' (included, reviewed-failing — feeds the Milestone 4 engine queue): '
          'the page is a manual instruction test whose procedure is a human interaction, '
          'and the obligation it exercises is statically reviewed against the engine source. ')

OFFLINE = (
    HEADER +
    'The obligation is the 2014 REC §5.6 Offline Web applications model, which the engine '
    'does not implement: there is no applicationCache object in the source tree (a manifest '
    'attribute parses as an ordinary attribute, no cache-update algorithm runs, and no '
    'caching events exist), so the page\'s pass condition — the cached document rendering '
    'with the network disabled — cannot be met. Navigator.onLine is additionally a fixed '
    'true with no network-state source. The manual-evidence recording on a native build '
    'remains the Milestone 6 evidence step. Prior review: '
)

DECISIONS = {
    'html/browsers/offline/browser-state/navigator_online_event-manual.https.html':
        HEADER +
        'The obligation is the 2014 REC §5.6 navigator.onLine and its online/offline events '
        '(§5.6.2 Browser state): the page instructs the user to toggle work-offline mode and '
        'asserts the matching event is raised. The engine exposes navigator.onLine as a fixed '
        'true with no network-state source and dispatches no online/offline events (both '
        'statically verified in the source), so neither asserted event can fire. The '
        'manual-evidence recording on a native build remains the Milestone 6 evidence step.',
    'html/browsers/offline/manifest_main_empty-manual.https.html':
        OFFLINE + 'an empty manifest must still drive the §5.6 cache-update algorithm '
        '(downloading event, cached event) so the page works offline.',
    'html/browsers/offline/manifest_notchanged_online-manual.https.html':
        OFFLINE + 'a reloaded page whose manifest is unchanged must reuse the existing cache '
        'without re-downloading (the §5.6 "no update" branch, noupdate event).',
    'html/browsers/offline/manifest_section_empty-manual.https.html':
        OFFLINE + 'a manifest with an empty section must parse per the §5.6.3 manifest-parsing '
        'steps and still drive the update algorithm.',
    'html/browsers/offline/manifest_section_many-manual.https.html':
        OFFLINE + 'a manifest with many entries must download them all through the §5.6 update '
        'algorithm before the cached event.',
    'html/browsers/offline/section_network_offline-manual.https.html':
        OFFLINE + 'entries under the NETWORK section must be excluded from the cache (§5.6.3): '
        'with the network disabled the master document must still render from the cache while '
        'network-section resources fail.',
    'html/browsers/offline/section_network_online-manual.https.html':
        OFFLINE + 'the page must render normally online while its NETWORK-section resources '
        'load from the network (§5.6 cache selection and the fallback model).',
    'html/browsers/windows/restore-window-name-manual.https.html':
        HEADER +
        'The obligation is the 2014 REC §6.1.4 session history and the Window name: window.name '
        'is restored when navigating back from a cross-origin document. The page\'s procedure '
        'opens a named browsing context through window.open — which the engine does not '
        'implement (statically absent from the source, and measured in the decided '
        'failure-check-sequence.https.html and clear-window-name.https.html files, whose '
        'subtests reject on it) — so the flow cannot start. The manual-evidence recording on a '
        'native build remains the Milestone 6 evidence step.',
}


def main():
    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    tests = manifest['tests']
    changed = 0
    for entry in tests:
        if entry['classification'] != 'unreviewed':
            continue
        decision = DECISIONS.get(entry['path'])
        if decision is None:
            continue
        entry['classification'] = 'included'
        entry['reason'] = decision
        changed += 1

    remaining = [t['path'] for t in tests if t['classification'] == 'unreviewed']
    if remaining:
        print('ERROR: unreviewed entries remain:', remaining)
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
    print(f'manual tail closed: {changed} entries now included reviewed-failing; '
          f'manifest now {dict(Counter(t["classification"] for t in tests))}; '
          f'unreviewed remainder: 0')


if __name__ == '__main__':
    main()
