#!/usr/bin/env python3
"""M1 inventory completion: record the measured survey outcomes into
Wpt/html5-applicability.json — the same mechanism as the 2026-11-03
testdriver-inventory batch, applied to the inventory-completion remainder.

Consumes the M1 survey shard reports (artifacts/m1-survey-*-of-3.json over
artifacts/m1-queue-all.txt) and ADDS one manifest entry per measured source,
keyed by the source path so every variant case it expands to is covered:

- pass        -> included: executed; every subtest passes.
- fail        -> included reviewed-failing (the dom-straggler convention):
                 the measured failing-subtest summary and the owning Milestone 4
                 engine area are recorded in the reason.
- timeout / crash / empty -> included reviewed-failing with the measured detail.
- unsupported / skipped  -> left for the static routes; listed on stdout.

Existing entries always win. The manifest is written only when the structural
invariants hold.
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

root = Path('Lite.Conformance')
applicability_path = root / 'Wpt/html5-applicability.json'
catalog_path = root / 'artifacts/wpt-manifest.json'
TODAY = '2026-11-05'
CAPABILITY = 'the M1 inventory-completion survey (single-origin ConformanceServer serving)'

DOMAINS = [
    ('dom/ranges/', 'the engine has no Range/Selection surface — the Milestone 4 editing and selection package owns the cluster'),
    ('dom/events/', 'event semantics — owned by the Milestone 4 events and messaging package'),
    ('dom/nodes/', 'node tree mutation and traversal semantics — owned by the Milestone 4 DOM authority package'),
    ('dom/', 'DOM dependency semantics — owned by the Milestone 4 DOM authority package'),
    ('selection/', 'the engine has no Selection/Range surface — the Milestone 4 editing and selection package owns the cluster'),
    ('uievents/', 'UI event semantics — owned by the Milestone 4 events and messaging package'),
    ('url/', 'URL dependency semantics — owned by the Milestone 4 networking area'),
    ('mimesniff/', 'MIME-sniffing dependency semantics — owned by the Milestone 4 networking area'),
    ('FileAPI/', 'the File/Blob/FileReader dependency surface — owned by the Milestone 4 file area'),
    ('cookies/', 'cookie storage attributes — the engine keeps a single static cookie dictionary; the Milestone 4 cookies package owns the cluster'),
    ('encoding/', 'encoding-dependency semantics (document/form encoding, §8.2.2) — owned by the Milestone 4 encoding area'),
    ('fetch/', 'fetching semantics the REC maps as a dependency — owned by the Milestone 4 networking area'),
    ('cors/', 'CORS dependency semantics — owned by the Milestone 4 networking area'),
    ('html/editing/', 'the engine has no Selection/Range/execCommand surface (REC §7.5 Editing via the pinned Editing APIs dependency) — the Milestone 4 editing and selection package owns the cluster'),
    ('html/semantics/forms/', 'form semantics — owned by the Milestone 4 forms and DOM authority packages'),
    ('html/semantics/embedded-content/', 'embedded-content semantics (media, iframe, image loading) — owned by the Milestone 4 embedded-content package'),
    ('html/semantics/scripting-1/', 'script semantics — owned by the Milestone 4 scripting and dynamic-markup packages'),
    ('html/semantics/', 'element semantics — owned by the Milestone 4 DOM authority package'),
    ('html/dom/', 'document/element DOM surface — owned by the Milestone 4 DOM authority package'),
    ('html/webappapis/', 'webappapis semantics (timers, event handlers, dynamic markup insertion) — owned by the Milestone 4 events and dynamic-markup packages'),
    ('html/syntax/', 'parser/serializer semantics — owned by the Milestone 4 parser packages (parse fidelity is corpus-closed; residuals are engine findings)'),
    ('html/browsers/', 'window, dialog and browsing-context semantics (REC §5/§6) — owned by the Milestone 4 events, origins/sandbox and history packages'),
    ('html/rendering/', 'rendering requirements (§10) — owned by the CSS 2.1 track\'s rendering queue'),
    ('html/obsolete/', 'obsolete-feature processing/rendering — owned by the Milestone 4 obsolete-elements package'),
    ('html/infrastructure/', 'common-infrastructure semantics — owned by the Milestone 4 DOM authority package'),
    ('html/the-xhtml-syntax/', 'XHTML-syntax semantics — owned by the Milestone 4 parser packages'),
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
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    case_pairs = []
    for kind, tree in catalog['items'].items():
        if kind in ('support', 'spec'):
            continue
        walk(tree, '', kind, case_pairs)
    case_to_source = {path: source for source, path in case_pairs}

    manifest = json.loads(applicability_path.read_text(encoding='utf-8-sig'))
    tests = manifest['tests']
    existing = {t['path'] for t in tests}
    counts = Counter()
    residue = []

    report_files = sorted((root / 'artifacts').glob('m1-survey-*.json'))
    report_files = [f for f in report_files
                    if f.name not in ('m1-survey-smoke.json', 'm1-survey-residue.json')]
    if not report_files:
        print('ERROR: no m1-survey-*.json reports found; run the survey shards first.')
        sys.exit(1)
    for report_file in report_files:
        report = json.loads(report_file.read_text(encoding='utf-8-sig'))
        if not report.get('completed'):
            print(f'NOTE: {report_file.name} is a partial report '
                  f'({len(report.get("tests", []))} outcomes) — its completed outcomes are recorded.')
        outcomes = [t for t in report.get('tests', []) if t.get('suite') == 'wpt']
        for outcome in outcomes:
            path = outcome.get('path') or ''
            source = case_to_source.get(path, path.split('?')[0].split('#')[0])
            if source in existing:
                continue
            kind = outcome.get('cat') or outcome.get('outcome')
            subtests = outcome.get('subtests') or []
            total = len(subtests)
            failed = sum(1 for s in subtests if s.get('status') != 0)
            detail = (outcome.get('detail') or '')[:200]

            if kind == 'pass':
                add(tests, existing, counts, source, 'included',
                    f'Reviewed {TODAY} (inventory batch): executed under {CAPABILITY}; '
                    f'every subtest passes.')
            elif kind == 'fail':
                measured = (f'{failed}/{total} subtests fail — {failing_summary(subtests)}'
                            if total else 'the file reports no passing subtests')
                add(tests, existing, counts, source, 'included',
                    f'Reviewed {TODAY} (inventory batch; included, reviewed-failing — feeds the '
                    f'Milestone 4 engine queue): executed under {CAPABILITY}; {measured}. '
                    f'Engine gap: {domain(source)}.')
            elif kind == 'timeout':
                add(tests, existing, counts, source, 'included',
                    f'Reviewed {TODAY} (inventory batch; included, reviewed-failing — feeds the '
                    f'Milestone 4 engine queue): executed under {CAPABILITY}; the run times out '
                    f'without reporting results ({detail}). Engine gap: {domain(source)}.')
            elif kind == 'crash':
                add(tests, existing, counts, source, 'included',
                    f'Reviewed {TODAY} (inventory batch; included, reviewed-failing — feeds the '
                    f'Milestone 4 engine queue): executed under {CAPABILITY}; the page crashes '
                    f'the harness ({detail}). Engine gap: {domain(source)}.')
            elif kind == 'empty':
                add(tests, existing, counts, source, 'included',
                    f'Reviewed {TODAY} (inventory batch; included, reviewed-failing — feeds the '
                    f'Milestone 4 engine queue): executed under {CAPABILITY}; no subtests are '
                    f'reported ({detail}). Engine gap: {domain(source)}.')
            elif kind in ('manual-pending', 'manualpending'):
                residue.append((source, 'manual-pending', detail))
            else:  # unsupported, skipped, anything else
                residue.append((source, kind, detail))

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
    print(f'recorded outcomes: {dict(counts)}')
    if residue:
        by_kind = Counter(k for _, k, _ in residue)
        print(f'residue for the static routes: {dict(by_kind)} ({len(residue)} sources)')
        residue_path = root / 'artifacts/m1-survey-residue.json'
        residue_path.write_text(json.dumps(
            [{'source': s, 'kind': k, 'detail': d} for s, k, d in sorted(set(residue))],
            indent=1) + '\n', encoding='utf-8')
        print(f'  -> {residue_path}')


def add(tests, existing, counts, path, classification, reason):
    if path in existing:
        return
    tests.append({'path': path, 'classification': classification, 'reason': reason})
    existing.add(path)
    counts[classification] += 1


def walk(tree, prefix, kind, out):
    for name, node in tree.items():
        source = prefix + name
        if isinstance(node, dict):
            walk(node, source + '/', kind, out)
            continue
        if not isinstance(node, list) or len(node) < 2:
            continue
        for item in node[1:]:
            path = (item[0] or source).lstrip('/')
            out.append((source, path))


if __name__ == '__main__':
    main()
