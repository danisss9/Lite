#!/usr/bin/env python3
"""Workstream 1: propose HTML 5.0 applicability review batches (plan-html5-
full-conformance-harmonic-treasure, WS1 "Review tooling").

Reads the refreshed html5-inventory.json and the current applicability
manifest, and proposes classification buckets for the unclassified catalog
sources, each with matched evidence lines:

- post-target API greps: exact later-feature names the pinned 28 October
  2014 REC predates (toggleAttribute, replaceChildren, AbortSignal, the
  promise-returning play(), popover, inert, loading=, the structuredClone()
  global, ...);
- dependency-usage batches: window-context files whose code exercises an
  out-of-scope dependency (Web Storage, Workers, Web Messaging) — emitted at
  low confidence because the dependency use can be incidental to a real REC
  obligation, so every file in those batches needs individual eyes;
- test-kind needs: testdriver-based files and multi-origin/HTTPS pages the
  harness cannot execute until Workstream 2 lands (proposed as reviewed
  blockers, not closed classifications).

The tool NEVER writes `included` and never touches the manifest on its own:
`propose` (the default) only writes the proposals artifact;
`--apply <file> --batch <id> --clause "<REC citation>"` records one accepted
batch into Wpt/html5-applicability.json with the citation in every reason.
Existing entries always win, and a batch whose rule matched nothing is an
error (no silent no-ops), mirroring the Workstream 0 batch script.

Sanity gates before any write: the inventory artifact's manifestSha256 must
match the current WPT manifest, and the result must satisfy the applicability
schema's structural invariants.
"""
import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

inventory_artifact = Path('Lite.Conformance/artifacts/html5-inventory.json')
manifest_path = Path('Lite.Conformance/artifacts/wpt-manifest.json')
applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
vendor_wpt = Path('Lite.Conformance/vendor/wpt')
proposals_artifact = Path('Lite.Conformance/artifacts/html5-applicability-proposals.json')

TODAY = datetime.now(timezone.utc).strftime('%Y-%m-%d')

# High-precision post-target API greps: the matched identifier names the exact
# later feature, so a whole batch can be accepted from a spot-check.
POST_TARGET_API_RULES = [
    ('toggleAttribute', r"""\.toggleAttribute\s*\(|\btoggleAttribute\b""",
     'Element.toggleAttribute was specified in whatwg/dom in 2016+; the 2014 REC\'s DOM dependency (§2.6, §5) defines no such method.'),
    ('replaceChildren', r"""\.replaceChildren\s*\(""",
     'ParentNode.replaceChildren was specified in whatwg/dom in 2019+; the 2014 REC\'s DOM dependency has no such method.'),
    ('abort-controller', r"""\bAbortController\b|\bAbortSignal\b""",
     'AbortController/AbortSignal were specified in 2016+ (whatwg/dom); the 2014 REC defines no abort signal and no fetch dependency step consumes one.'),
    ('popover', r"""\bpopover\b|showPopover\s*\(|hidePopover\s*\(|togglePopover\s*\(|beforetoggle""",
     'The popover attribute and popover methods were specified in 2022+, absent from the 2014 REC.'),
    ('inert', r"""\.inert\b|setAttribute\(\s*['"]inert['"]|\binert\s*=""",
     'the inert attribute and HTMLElement.inert were specified in 2022+, absent from the 2014 REC\'s §7 disabled-elements model.'),
    ('lazy-loading', r"""\.loading\b|getAttribute\(\s*['"]loading['"]|loading\s*=\s*["']lazy["']""",
     'the loading="lazy" attribute was specified in 2019+; the 2014 REC loads every img/iframe as it is traversed (§4.8, §4.11).'),
    ('structured-clone-global', r"""\bstructuredClone\s*\(""",
     'the structuredClone() global was specified in 2022+; the 2014 REC §2.7.5 defines internal structured clone for postMessage/history.state only, with no exposed global.'),
    ('move-before', r"""\.moveBefore\s*\(""",
     'ParentNode.moveBefore was specified in 2024+; the 2014 REC moves nodes through insertBefore/appendChild with reparenting at adoption.'),
    ('promise-play', r"""\.play\s*\(\s*\)\s*\.\s*(then|catch|finally)\b|await\s+[^;\n]*\.play\s*\(\s*\)""",
     'HTMLMediaElement.play() returned void in the 2014 REC (§4.8.10 media elements); the promise-returning play() was specified in 2016+.'),
    ('dialog-element', r"""<dialog\b|\bshowModal\s*\(|\bclose\s*\(\s*["']""",
     'the <dialog> element and showModal() were specified in 2022+; the 2014 REC has no dialog element (Lite keeps it as an extension).'),
    ('queue-microtask', r"""\bqueueMicrotask\s*\(""",
     'queueMicrotask() was specified in 2018+; the 2014 REC exposes microtask checkpointing (§6.1.4) through no global.'),
    ('observers', r"""\b(IntersectionObserver|ResizeObserver|PerformanceObserver|MutationObserver)\b""",
     'the observer APIs were specified after the 2014 REC (MutationObserver 2013+ in the separate DOM spec the REC predates, the rest 2015+); the 2014 REC defines none of them.'),
    ('text-decoder-api', r"""\bnew\s+(TextDecoder|TextEncoder)\b""",
     'the Encoding Standard\'s TextDecoder/TextEncoder JS API is not part of the 2014 REC\'s encoding dependency, which covers document decoding, legacy encodings and form encoding (§8.2.2) only.'),
]

# Low-confidence dependency-usage batches: the dependency API can be incidental
# to a genuine REC obligation (a history test that counts loads through
# localStorage, a sandbox test that constructs a Worker). The reviewer must
# reject false positives file by file — never accept these blind.
DEPENDENCY_USAGE_RULES = [
    ('dep-web-storage-usage', r"""\blocalStorage\b|\bsessionStorage\b""",
     'profile-excluded',
     'WARNING: incidental use is common. Accept only files whose assertions bind the Web Storage API itself ([WEBSTORAGE], excluded from dependency scope on 2026-10-24); reject files that merely use storage as test infrastructure for a REC obligation.'),
    ('dep-workers-usage', r"""\bnew\s+(Worker|SharedWorker)\s*\(|navigator\.serviceWorker""",
     'profile-excluded',
     'WARNING: incidental use is common. Accept only files whose assertions bind the Workers API itself ([WEBWORKERS], excluded from dependency scope on 2026-10-24); reject sandbox (§5.4) and browsing-context files that construct a worker to observe a REC obligation.'),
    ('dep-web-messaging-usage', r"""\bnew\s+(MessageChannel|BroadcastChannel|MessagePort)\b""",
     'profile-excluded',
     'WARNING: incidental use is common. Accept only files whose assertions bind the Web Messaging API itself ([WEBMSG], excluded from dependency scope on 2026-10-24); WindowProxy and origin tests that pass messages as a mechanism stay reviewed.'),
]

# Test-kind needs: these propose reviewed blockers (unreviewed with a reason),
# not closed classifications — the gate keeps them out until Workstream 2
# removes the harness limit.
TESTDRIVER_REASON = ('Requires the Workstream 2 testdriver backend: the file drives test_driver '
                     'click/keyboard/action_sequence/bless, which the harness cannot execute yet.')
MULTIORIGIN_REASON = ('Requires real wptserve (Workstream 2): the page is a multi-origin (.sub., www1/alt '
                      'hosts) or HTTPS document, and the single-origin ConformanceServer cannot serve the '
                      'substitutions the assertions read.')


def read_text(path: str) -> str:
    file = vendor_wpt.joinpath(*path.split('/'))
    try:
        return file.read_text(encoding='utf-8', errors='replace')
    except OSError:
        return ''


def evidence_lines(text: str, pattern: re.Pattern, limit: int = 3) -> list[str]:
    found = []
    for line in text.splitlines():
        if pattern.search(line):
            found.append(line.strip()[:200])
            if len(found) >= limit:
                break
    return found


def build_proposals(inventory, reviews) -> dict:
    classified = {r['path'] for r in reviews}
    by_source = {t['source']: t for t in inventory['tests']}
    sources = sorted(s for s in by_source if s not in classified)
    api_rules = [(rid, re.compile(pattern), justification)
                 for rid, pattern, justification in POST_TARGET_API_RULES]
    dep_rules = [(rid, re.compile(pattern), classification, warning)
                 for rid, pattern, classification, warning in DEPENDENCY_USAGE_RULES]
    batches = {}
    unproposed = []

    def record(batch_id, classification, confidence, justification, path, compiled, text):
        bucket = batches.setdefault(batch_id, {
            'classification': classification,
            'confidence': confidence,
            'justification': justification,
            'paths': [], 'evidence': {},
        })
        bucket['paths'].append(path)
        bucket['evidence'][path] = evidence_lines(text, compiled)

    for source in sources:
        text = read_text(source)
        proposed = False
        for rule_id, compiled, justification in api_rules:
            if compiled.search(text):
                record(f'post-target-api/{rule_id}', 'post-target', 'high', justification,
                       source, compiled, text)
                proposed = True
        for rule_id, compiled, classification, warning in dep_rules:
            if compiled.search(text):
                record(rule_id, classification, 'low', warning, source, compiled, text)
                proposed = True
        meta = by_source.get(source, {})
        if meta.get('testDriver'):
            record('needs/testdriver', 'unreviewed', 'high', TESTDRIVER_REASON, source,
                   re.compile(r'.'), text)
            proposed = True
        if '.sub.' in source or '.https.' in source or 'get_host_info' in text:
            record('needs/multi-origin', 'unreviewed', 'high', MULTIORIGIN_REASON, source,
                   re.compile(r'.'), text)
            proposed = True
        if not proposed:
            unproposed.append(source)

    for batch in batches.values():
        batch['paths'].sort()
    return {
        'formatVersion': 1,
        'target': 'https://www.w3.org/TR/2014/REC-html5-20141028/',
        'generated': TODAY,
        'manifestSha256': hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
        'batches': dict(sorted(batches.items())),
        'unproposed': sorted(unproposed),
    }


def validate(applicability) -> list[str]:
    errors = []
    seen = set()
    for t in applicability['tests']:
        path = t.get('path', '')
        if path in seen:
            errors.append(f'duplicate path {path}')
        seen.add(path)
        if t.get('classification') not in ('included', 'mixed', 'unreviewed', 'post-target', 'regression-only', 'profile-excluded'):
            errors.append(f'bad classification on {path}')
        if not t.get('reason', '').strip():
            errors.append(f'empty reason on {path}')
        if t.get('classification') != 'mixed' and ('assertions' in t or 'assertionInventoryComplete' in t):
            errors.append(f'assertion fields on non-mixed {path}')
    return errors


def apply_batch(proposals_path: Path, batch_id: str, clause: str, date: str) -> int:
    if not clause.strip():
        print('--apply requires a REC clause citation via --clause.')
        return 2
    proposals = json.loads(proposals_path.read_text(encoding='utf-8'))
    if proposals.get('manifestSha256') != hashlib.sha256(manifest_path.read_bytes()).hexdigest():
        print('Proposals are stale versus the current WPT manifest; re-run propose first.')
        return 2
    batch = proposals['batches'].get(batch_id)
    if batch is None:
        print(f'Unknown batch: {batch_id}. Available: {", ".join(proposals["batches"])}')
        return 2
    if batch['classification'] == 'included':
        print('The proposal tool never writes included; applicability reviews that grow the gate '
              'are recorded by the section review, with the test passing in the same change.')
        return 2

    applicability = json.loads(applicability_path.read_text(encoding='utf-8'))
    existing = {t['path'] for t in applicability['tests']}
    additions = []
    skipped = []
    for path in batch['paths']:
        if path in existing:
            skipped.append(path)
            continue
        additions.append({
            'path': path,
            'classification': batch['classification'],
            'reason': f'{batch["justification"]} Accepted {date} under REC clause {clause.strip()}.',
        })
    applicability['tests'].extend(additions)
    errors = validate(applicability)
    if errors:
        for e in errors:
            print(f'SCHEMA ERROR: {e}')
        return 2
    applicability_path.write_text(json.dumps(applicability, indent=2, ensure_ascii=False), encoding='utf-8')
    counts = {}
    for t in applicability['tests']:
        counts[t['classification']] = counts.get(t['classification'], 0) + 1
    print(f'Applied batch {batch_id}: {len(additions)} entries recorded, {len(skipped)} already reviewed and preserved.')
    print(f'Manifest now holds {len(applicability["tests"])} entries: {counts}')
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--apply', metavar='FILE', help='apply one accepted batch from a proposals artifact')
    parser.add_argument('--batch', metavar='ID', help='batch id to apply')
    parser.add_argument('--clause', metavar='CITATION', help='the REC clause the reviewer accepts the batch under')
    parser.add_argument('--date', default=TODAY, help='acceptance date recorded in the reasons')
    args = parser.parse_args()

    if not inventory_artifact.exists():
        print(f'Missing inventory artifact {inventory_artifact}; run the html5-inventory suite first.')
        return 2
    inventory = json.loads(inventory_artifact.read_text(encoding='utf-8'))
    if inventory.get('formatVersion') != 1:
        print('Unexpected inventory formatVersion.')
        return 2
    if inventory.get('manifestSha256') != hashlib.sha256(manifest_path.read_bytes()).hexdigest():
        print('Inventory artifact is stale versus the current WPT manifest; run the html5-inventory suite to regenerate it.')
        return 2
    applicability = json.loads(applicability_path.read_text(encoding='utf-8'))
    reviews = applicability['tests']

    if args.apply:
        return apply_batch(Path(args.apply), args.batch or '', args.clause or '', args.date)

    proposals = build_proposals(inventory, reviews)
    proposals_artifact.write_text(json.dumps(proposals, indent=1, ensure_ascii=False), encoding='utf-8')
    print(f'Proposals: {proposals_artifact}')
    for batch_id, batch in proposals['batches'].items():
        print(f'  {batch_id}: {len(batch["paths"])} paths -> {batch["classification"]} [{batch["confidence"]} confidence]')
    print(f'  (no rule matched): {len(proposals["unproposed"])} paths stay for the section review')
    return 0


if __name__ == '__main__':
    sys.exit(main())
