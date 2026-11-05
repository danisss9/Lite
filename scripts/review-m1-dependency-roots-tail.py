#!/usr/bin/env python3
"""M1 inventory completion: static sweep of the dependency roots' unmanifested
catalog sources — encoding, fetch, cors, dom, selection, uievents, url,
mimesniff, FileAPI, cookies.

Same batch discipline as review-m1-html-static-tail.py:

- whole-directory post-target where the directory's own feature postdates the
  2014 REC (TextDecoder/TextEncoder streams 2019+, fetch-later 2023+,
  compression dictionaries 2023+, CORB/ORB 2017+/2020+, Fetch Metadata 2019+,
  the DOM Observable API 2024+, cookie partitioning/schemeful-same-site 2020+,
  SameSite and cookie prefixes RFC6265bis 2016+);
- post-target content markers naming later-era APIs, each reason carrying the
  matched source line;
- .sub./get_host_info serving-blocked sources classify included
  reviewed-failing naming the Milestone 3 upstream-serving residual (the
  dom-straggler convention);
- everything else joins a per-group survey queue for measured outcomes.

The 2014 REC dependency mapping each family cites: [ENCODING] document/form
encoding (§8.2.2), [FETCH]/[CORS] fetching (§2.6), [DOM] DOM4 2014 (§2.6),
[UIEVENTS] (§5), [URL] (§2.5), [MIMESNIFF] (§2.7), [FILEAPI] (§4.10/§4.11),
[COOKIES] (document.cookie, §4.12.2). window.postMessage stays applicable
(§6.4 cross-document messaging); URLSearchParams stays applicable (in the
[URL] surface the REC references).
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

root = Path('Lite.Conformance')
applicability_path = root / 'Wpt/html5-applicability.json'
catalog_path = root / 'artifacts/wpt-manifest.json'
vendor = root / 'vendor/wpt'
TODAY = '2026-11-05'

QUEUES = {
    'encoding': root / 'artifacts/m1-queue-encoding.txt',
    'fetch': root / 'artifacts/m1-queue-fetch.txt',
    'dom': root / 'artifacts/m1-queue-dom.txt',
    'rest': root / 'artifacts/m1-queue-rest.txt',
}

ROOTS = ['encoding', 'fetch', 'cors', 'dom', 'selection', 'uievents', 'url', 'mimesniff',
         'FileAPI', 'cookies']

COMMON_POST_RULES = [
    (r'\bAbortController\b|\bAbortSignal\b', 'AbortController/AbortSignal were specified in 2016+'),
    (r'\.toggleAttribute\s*\(|\btoggleAttribute\b', 'Element.toggleAttribute was specified in whatwg/dom in 2016+'),
    (r'\.replaceChildren\s*\(', 'ParentNode.replaceChildren was specified in whatwg/dom in 2019+'),
    (r'MutationObserver|ResizeObserver|IntersectionObserver|PerformanceObserver',
     'the observer interfaces were specified after the 2014 REC'),
    (r'attachShadow|\bshadowRoot\b|<slot\b', 'shadow trees postdate the 2014 REC'),
    (r'\bstructuredClone\s*\(', 'the structuredClone() global was specified in 2022+'),
    (r'\bnew\s+(TextDecoder|TextEncoder)\b',
     'the TextDecoder/TextEncoder JS API is not part of the 2014 REC\'s encoding dependency (document decoding, §8.2.2 only)'),
    (r'TextDecoderStream|TextEncoderStream|ReadableStream|WritableStream|TransformStream',
     'the Streams API (ReadableStream/TextDecoderStream and friends) postdates the 2014 REC'),
    (r'\bnew\s+(Shared)?ArrayBuffer\b|\bAtomics\b', 'SharedArrayBuffer/Atomics postdate the 2014 REC'),
    (r'\bnew\s+(MessageChannel|MessagePort|BroadcastChannel)\b',
     'the Web Messaging API ([WEBMSG], MessageChannel/ports/BroadcastChannel) is outside the profile per the 2026-10-24 scope decision'),
    (r'\bnew\s+Worker\s*\(|\bnew\s+SharedWorker\s*\(|navigator\.serviceWorker',
     'the Workers dependency ([WEBWORKERS]) is outside the profile per the 2026-10-24 scope decision'),
    (r'\blocalStorage\b|\bsessionStorage\b',
     'the Web Storage dependency ([WEBSTORAGE]) is outside the profile per the 2026-10-24 scope decision'),
    (r'\bIndexedDB\b|\bindexedDB\b',
     'the IndexedDB dependency is outside the profile per the 2026-10-24 scope decision'),
    (r'\.moveBefore\s*\(', 'ParentNode.moveBefore was specified in 2024+'),
    (r'\bqueueMicrotask\b', 'queueMicrotask() entered WHATWG HTML in 2017+'),
    (r'setBaseAndExtent', 'Selection.setBaseAndExtent entered the Selection API after the 2014 snapshot'),
]

POST_DIRS = [
    ('encoding/streams',
     'the TextDecoderStream/TextEncoderStream Streams API was specified in 2019+; the whole directory tests that API surface'),
    ('fetch/fetch-later',
     'the fetchLater() API was specified in 2023+; the whole directory tests that API'),
    ('fetch/compression-dictionary',
     'compression dictionary transport was specified in 2023+; the whole directory tests that feature'),
    ('fetch/corb',
     'Cross-Origin Read Blocking was specified in 2017-2019 (the Opaque Response Blocking line); the 2014 REC predates it'),
    ('fetch/orb',
     'Opaque Response Blocking was specified in 2020+; the 2014 REC predates it'),
    ('fetch/metadata',
     'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the 2014 REC predates them'),
    ('fetch/client-hints',
     'client hint request headers were specified in 2020+; the 2014 REC predates them'),
    ('dom/observable',
     'the Observable API was specified in 2024+; the whole directory tests that API'),
    ('cookies/partitioned-cookies',
     'cookie partitioning (the Partitioned attribute) was specified in 2020+; the 2014 REC predates it'),
    ('cookies/schemeful-same-site',
     'schemeful same-site was specified in 2020+; the 2014 REC predates it'),
    ('cookies/samesite',
     'the SameSite cookie attribute was specified in RFC6265bis (2016+); the 2014 REC predates it'),
    ('cookies/prefix',
     'cookie name prefixes (__Host-/__Secure-) were specified in RFC6265bis (2016+); the 2014 REC predates them'),
]

ROOT_POST_RULES = {
    'fetch': [
        (r"duplex\s*[:=]\s*['\"]half['\"]",
         'the fetch duplex option (streaming request bodies) was specified in 2021+'),
        (r'\bsec-fetch-', 'Fetch Metadata request headers were specified in 2019+'),
        (r'Private-?Network-?Access', 'private network access was specified in 2020+'),
    ],
    'cors': [
        (r'client-hint|Client-Hint', 'client hint request headers were specified in 2020+'),
    ],
    'dom': [
        (r'new\s+Observable|\bObservable\s*\(', 'the Observable API was specified in 2024+'),
        (r'\bscrollend\b', 'the scrollend event was specified in 2023+'),
        (r'AddEventListenerOptions|\bpassive\s*:\s*true',
         'AddEventListenerOptions ({once, passive, signal}) postdate the 2014 DOM4 draft, which accepts only a boolean capture argument'),
        (r'\bStaticRange\b', 'StaticRange was specified in the DOM Standard (2016+) after the 2014 DOM4 draft'),
        # append/prepend stay OUT deliberately: the 2014-07-10 DOM4 draft the REC
        # references defines ParentNode.prepend()/append() (listed at-risk), so a
        # read is not a later-feature marker. ChildNode.after/before/replaceWith
        # are absent from that draft (removed before publication).
        (r'\.replaceWith\s*\(|\.after\s*\(|\.before\s*\(',
         'ChildNode.after()/before()/replaceWith() are absent from the 2014-07-10 DOM4 draft HTML 5.0 references (which defines only remove()), and were introduced after the 28 October 2014 snapshot'),
        (r'\bElementInternals\b', 'ElementInternals was specified in 2018+'),
        (r'\bscrollingElement\b', 'document.scrollingElement was specified in 2015+'),
        (r'\.delegatesFocus|assignedSlot', 'shadow-tree surface postdates the 2014 DOM4 draft'),
    ],
    'url': [
        (r'\bURLPattern\b', 'URLPattern was specified in 2021+'),
        (r'\.canParse\s*\(', 'URL.canParse() was specified in 2023+'),
    ],
    'FileAPI': [
        (r'\.stream\s*\(\s*\)', 'Blob.stream() was specified in 2019+'),
        (r'\.bytes\s*\(\s*\)', 'the Blob bytes() promise reader was specified in 2021+'),
        (r'\bBlobEvent\b', 'BlobEvent was specified in 2017+'),
    ],
    'cookies': [],
    'uievents': [],
    'mimesniff': [],
    'encoding': [],
    'selection': [],
}

FAMILY_CLAUSES = {
    'encoding': 'the 2014 REC §8.2.2 character-encoding determination and document/form encoding dependency (the fallback-encoding table, the BOM step, prescan, transport charset, legacy multibyte decodings and the form-encoding set)',
    'fetch': 'the 2014 REC §2.6 fetching-resources dependency (the fetch algorithm the REC invokes for every resource load) as the REC maps it',
    'cors': 'the 2014 REC §2.6 cross-origin resource-sharing dependency ([CORS] — the cross-origin request/response model the REC\'s fetching invokes)',
    'dom': 'the 2014 REC §2.6 DOM dependency (DOM4 2014 WD-dom-20140710 — node-tree, event, range and collection semantics)',
    'selection': 'the 2014 REC §7.5 editing dependency — the Selection/Range editing surface the pinned Editing APIs draft defines',
    'uievents': 'the 2014 REC §5 UIEvents dependency ([UIEVENTS] — the UI event model the REC\'s interactive elements reference)',
    'url': 'the 2014 REC §2.5 URL dependency ([URL] — URL parsing, resolution and the document base URL)',
    'mimesniff': 'the 2014 REC §2.7 MIME-sniffing dependency ([MIMESNIFF] — the content-type determination resource loads apply)',
    'FileAPI': 'the 2014 REC §4.10/§4.11 [FILEAPI] dependency (the File/Blob/FileReader surface the file-upload and form-submission models reference)',
    'cookies': 'the 2014 REC document.cookie model (§4.12.2 via the document object; the [COOKIES] RFC6265 storage the REC references)',
}


def read_text(path):
    try:
        return (vendor.joinpath(*path.split('/'))).read_text(encoding='utf-8', errors='replace')[:120000]
    except OSError:
        return ''


def main():
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    manifest = json.loads(applicability_path.read_text(encoding='utf-8-sig'))
    tests = manifest['tests']
    existing = {t['path'] for t in tests}
    counts = Counter()
    queues = {k: [] for k in QUEUES}
    serving = ('Reviewed {} (included, reviewed-failing — feeds the Milestone 4 engine queue): '
               'the file asserts {{}} — a 2014-target dependency obligation — but the page runs '
               'under a .sub. multi-host origin or reads get_host_info, which real upstream '
               'wptserve must serve; the single-origin ConformanceServer cannot produce the '
               'substitutions/origins the assertions read (the Milestone 3 serving residual). '
               'The upstream-serving recording remains the Milestone 6 evidence step.'
               ).format(TODAY)

    def add(path, classification, reason):
        if path in existing:
            return
        tests.append({'path': path, 'classification': classification, 'reason': reason})
        existing.add(path)
        counts[classification] += 1

    def queue_for(path):
        if path.startswith('encoding/'):
            return 'encoding'
        if path.startswith(('fetch/', 'cors/')):
            return 'fetch'
        if path.startswith('dom/'):
            return 'dom'
        return 'rest'

    sources = set()
    for kind, tree in catalog['items'].items():
        if kind in ('support', 'spec'):
            continue
        walk(tree, '', kind, existing, sources)
    for path in sorted(s for s in sources if s.split('/')[0] in ROOTS):
        rootname = path.split('/')[0]
        text = read_text(path)
        clause = FAMILY_CLAUSES[rootname]

        post_dir = next((msg for prefix, msg in POST_DIRS if path.startswith(prefix + '/')), None)
        if post_dir:
            add(path, 'post-target',
                f'Reviewed {TODAY} (post-target): {post_dir[0]} — the directory\'s own feature '
                f'postdates the 2014 REC. Not a 2014 HTML 5.0 obligation.')
            continue

        rules = COMMON_POST_RULES + ROOT_POST_RULES.get(rootname, [])
        matched = next(((pat, msg) for pat, msg in rules if re.search(pat, text, re.I)), None)
        if matched:
            pat, msg = matched
            line = next((l.strip()[:160] for l in text.splitlines() if re.search(pat, l, re.I)), '')
            add(path, 'post-target',
                f'Reviewed {TODAY} (post-target): {msg}. Marker in the file\'s own source: '
                f'"{line}". The file otherwise sits in {clause.split(" (")[0]}. '
                f'Not a 2014 HTML 5.0 obligation.')
            continue

        if '.sub.' in path or 'get_host_info' in text:
            add(path, 'included', serving.format(clause))
            continue

        queues[queue_for(path)].append(path)

    for name, queue in queues.items():
        QUEUES[name].write_text('\n'.join(queue) + '\n', encoding='utf-8')

    for entry in tests:
        if entry['classification'] not in ('included', 'mixed', 'post-target', 'profile-excluded',
                                           'regression-only', 'unreviewed'):
            print('ERROR: invalid classification:', entry['path'])
            sys.exit(1)
        if not entry.get('reason', '').strip():
            print('ERROR: empty reason:', entry['path'])
            sys.exit(1)
    applicability_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n',
                                  encoding='utf-8')
    print(f'dependency roots static tail: {dict(counts)}')
    for name, queue in queues.items():
        print(f'survey queue {name}: {len(queue)} sources -> {QUEUES[name]}')


def walk(tree, prefix, kind, reviewed, sources):
    for name, node in tree.items():
        source = prefix + name
        if isinstance(node, dict):
            walk(node, source + '/', kind, reviewed, sources)
            continue
        if not isinstance(node, list) or len(node) < 2:
            continue
        for item in node[1:]:
            path = (item[0] or source).lstrip('/')
            if source.split('/')[0] in ROOTS and path not in reviewed and source not in reviewed:
                sources.add(source)


if __name__ == '__main__':
    main()
