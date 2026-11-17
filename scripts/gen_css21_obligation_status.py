#!/usr/bin/env python3
"""The only writer of CSS 2.1 obligation statuses in Profile/css21-requirements.json.

An obligation's evidence is exactly what the C# gate (Css21Inventory.Evaluate) reads:

- every reviewed case the applicability file maps to it for the medium: applicable cases,
  and the replacements standing in for its defective cases (derived, never hand-copied);
- its explicit `tests` (unit, manual and curated mappings);
- for a roll-up section, the obligations listed in `rollUp`.

Per case the evidence is `pass` (the gate's HasEvidence holds), `fail` (the case executed
on the current inventory and did not pass) or `unproven` (no matching row, a stale
inventory, or a local-environment WPT row the gate does not accept). The status is then:

- `implemented`: at least one piece of evidence, and every case and roll-up child passes;
- `failing`: some case failed, or some roll-up child is failing;
- `untested`: anything else (missing or unproven evidence, nothing mapped).

Statuses move both ways: a regression demotes an implemented obligation. Only obligations
whose media include the evaluated medium are decided; print stays out of scope.

Evidence must come from the current working tree (identity.sourceSha256) and every report
must share one identity. Writing the statuses changes the inventory hash, so readiness then
needs a fresh run on the committed tree (Phase 7 of docs/css21-conformance-plan.md).

    python -I scripts/gen_css21_obligation_status.py \\
        --evidence Lite.Conformance/artifacts/css21-full-screen-0-of-10.json ... \\
        --evidence Lite.Conformance/artifacts/unit-results.json [--dry-run | --check]
"""
import argparse
import collections
import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFORMANCE = ROOT / 'Lite.Conformance'
REQUIREMENTS = 'Profile/css21-requirements.json'
APPLICABILITY = 'Css21/css21-applicability.json'
INVENTORY_INPUTS = ['Profile/css21-sections.json', 'Profile/css21-properties.json', REQUIREMENTS, APPLICABILITY]
OFFICIAL_PREFIX = 'css21-official/'
WHOLE_CASE = '*'


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_sha256() -> str:
    """The harness's SourceSha256: name+content hash over every tracked and untracked file."""
    names = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
                           cwd=ROOT, capture_output=True, check=True).stdout.decode('utf-8')
    digest = hashlib.sha256()
    for name in sorted(set(n for n in names.split('\0') if n)):
        digest.update((name + '\0').encode('utf-8'))
        file = ROOT / name
        digest.update(sha256_file(file).encode('utf-8') if file.is_file() else b'deleted')
    return digest.hexdigest()


def inventory_hash() -> str:
    """Css21Inventory.InventoryHash: the four inventory files, each named and content-hashed."""
    text = '\n'.join(f + '\0' + sha256_file(CONFORMANCE / f) for f in INVENTORY_INPUTS)
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def load_json(relative: str):
    return json.loads((CONFORMANCE / relative).read_text(encoding='utf-8-sig'))


def document_mode(path: str) -> str:
    bare = path.split('?')[0].split('#')[0].lower()
    return 'xhtml' if bare.endswith(('.xht', '.xhtml', '.xml')) else 'html'


def case_state(rows: list[dict], suite: str, path: str, media: str, inventory: str, assertion: str | None) -> str:
    """Mirror of Css21Inventory.HasEvidence, widened to tell a failure from missing proof."""
    if suite == 'unit':
        if not rows:
            return 'unproven'
        ok = all(r.get('outcome') == 'pass' and r.get('subtests') and
                 all(s.get('status') == 0 for s in r['subtests']) and
                 (not assertion or assertion == WHOLE_CASE or any(s.get('name') == assertion for s in r['subtests']))
                 for r in rows)
        return 'pass' if ok else 'fail'
    rows = [r for r in rows if (r.get('css') or {}).get('media') == media]
    if not rows:
        return 'unproven'
    current = [r for r in rows if r['css'].get('inventorySha256') == inventory]
    if len(current) != len(rows):
        return 'unproven'
    def passes(r: dict) -> bool:
        css = r['css']
        return (css.get('documentMode') == document_mode(path) and css.get('viewportWidth', 0) > 0 and
                css.get('viewportHeight', 0) > 0 and r.get('outcome') == 'pass' and
                r.get('harnessStatus') in (None, 0) and bool(r.get('subtests')) and
                all(s.get('status') == 0 for s in r['subtests']) and
                (not assertion or assertion == WHOLE_CASE or any(s.get('name') == assertion for s in r['subtests'])))
    if all(passes(r) for r in rows):
        if suite == 'css21-wpt' and any(r.get('environment') != 'upstream-wpt' for r in rows):
            return 'unproven'
        return 'pass'
    # A row that did not pass because no route executed it is missing proof, not a failure.
    if all(r.get('outcome') in ('unsupported', 'manualpending') for r in rows if not passes(r)):
        return 'unproven'
    return 'fail'


def derived_tests(reviews: list[dict], media: str) -> dict[str, list[tuple[str, str]]]:
    """Mirror of Css21Inventory.DerivedTests."""
    derived: dict[str, list[tuple[str, str]]] = collections.defaultdict(list)
    for review in reviews:
        if media not in review.get('media', []):
            continue
        if review['classification'] == 'applicable':
            suite = 'css21-official' if review['path'].startswith(OFFICIAL_PREFIX) else 'css21-wpt'
            for rid in review['requirementIds']:
                if (suite, review['path']) not in derived[rid]:
                    derived[rid].append((suite, review['path']))
        elif review['classification'] == 'defective':
            for rid in review['requirementIds']:
                for replacement in review['replacements']:
                    key = (replacement['suite'], replacement['path'])
                    if key not in derived[rid]:
                        derived[rid].append(key)
    return derived


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--evidence', action='append', default=[], help='evidence report; repeat for every shard')
    parser.add_argument('--media', default='screen', choices=['screen'])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--dry-run', action='store_true', help='print decisions, write nothing')
    mode.add_argument('--check', action='store_true', help='exit 1 if the committed statuses differ')
    parser.add_argument('--verbose', action='store_true', help='print every obligation, not only changes')
    args = parser.parse_args()
    if not args.evidence:
        print('No --evidence reports given; refusing to decide from nothing.')
        return 2

    current = source_sha256()
    identity = None
    by_case: dict[tuple[str, str], list[dict]] = collections.defaultdict(list)
    for raw in args.evidence:
        path = Path(raw)
        report = json.loads(path.read_text(encoding='utf-8-sig'))
        if report.get('formatVersion') != 6 or not report.get('completed'):
            print(f'{path.name}: not a completed format-6 evidence report; nothing was decided.')
            return 2
        if (report.get('identity') or {}).get('sourceSha256') != current:
            print(f'{path.name}: stale - sourceSha256 differs from the working tree; recollect it. Nothing was decided.')
            return 2
        if identity is None:
            identity = report['identity']
        elif report['identity'] != identity:
            print(f'{path.name}: identity differs from the first report; evidence must come from one build.')
            return 2
        for test in report.get('tests', []):
            by_case[(test['suite'], test['path'])].append(test)

    inventory = inventory_hash()
    requirements_path = CONFORMANCE / REQUIREMENTS
    raw = requirements_path.read_text(encoding='utf-8')
    requirements = json.loads(raw.lstrip('﻿'))
    reviews = load_json(APPLICABILITY)['tests']
    derived = derived_tests(reviews, args.media)
    rows = {r['id']: r for r in requirements['requirements']}

    def leaf_state(r: dict) -> tuple[str, collections.Counter]:
        counts: collections.Counter = collections.Counter()
        for suite, path in derived.get(r['id'], []):
            counts[case_state(by_case.get((suite, path), []), suite, path, args.media, inventory, None)] += 1
        for t in r['tests']:
            counts[case_state(by_case.get((t['suite'], t['path']), []), t['suite'], t['path'],
                              args.media, inventory, t.get('assertion'))] += 1
        return ('failing' if counts['fail'] else
                'implemented' if counts['pass'] and not counts['unproven'] else 'untested'), counts

    decided: dict[str, str] = {}
    details: dict[str, collections.Counter] = {}

    def decide(rid: str, trail: tuple[str, ...] = ()) -> str:
        if rid in decided:
            return decided[rid]
        if rid in trail:
            raise SystemExit(f'roll-up cycle through {rid}')
        r = rows[rid]
        state, counts = leaf_state(r)
        children = [decide(c, trail + (rid,)) for c in r.get('rollUp', [])]
        if children:
            counts['rollup-implemented'] = children.count('implemented')
            counts['rollup-other'] = len(children) - children.count('implemented')
            if state == 'failing' or 'failing' in children:
                state = 'failing'
            elif all(c == 'implemented' for c in children) and (state == 'implemented' or not sum(
                    counts[k] for k in ('pass', 'fail', 'unproven'))):
                state = 'implemented'
            else:
                state = 'untested'
        decided[rid], details[rid] = state, counts
        return state

    changes = []
    for rid, r in rows.items():
        if args.media not in r['media']:
            continue
        after = decide(rid)
        if args.verbose:
            c = details[rid]
            print(f'  {rid:24} {after:12} pass={c["pass"]} fail={c["fail"]} unproven={c["unproven"]}'
                  + (f' rollup={c["rollup-implemented"]}/{c["rollup-implemented"] + c["rollup-other"]}'
                     if r.get('rollUp') else ''))
        if r['status'] != after:
            changes.append((rid, r['status'], after))
            r['status'] = after

    totals = collections.Counter(decided[rid] for rid in decided)
    print(f'{args.media}: {len(decided)} obligations - ' + ', '.join(f'{k} {v}' for k, v in sorted(totals.items())))
    for rid, before, after in changes:
        print(f'  {"would change" if args.dry_run or args.check else "changed"} {rid}: {before} -> {after}')
    if args.check:
        return 1 if changes else 0
    if args.dry_run or not changes:
        print(f'{len(changes)} status changes; requirements file not written.')
        return 0
    newline = '\r\n' if '\r\n' in raw else '\n'
    body = json.dumps(requirements, indent=2, ensure_ascii=False) + '\n'
    requirements_path.write_text(('﻿' if raw.startswith('﻿') else '') + body.replace('\n', newline),
                                 encoding='utf-8', newline='')
    print(f'{len(changes)} statuses written. The inventory hash changed: readiness needs a fresh run.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
