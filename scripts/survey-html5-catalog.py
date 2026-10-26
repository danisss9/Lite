#!/usr/bin/env python3
"""Workstream 0: full-catalog survey baseline (plan-html5-full-conformance-harmonic-treasure WS0.5).

Derives the queue of remaining unclassified window-context testharness cases from the
refreshed html5-inventory.json, runs each through the real pipeline via
`--suite wpt --survey-list`, and records pass/partial/fail/crash/timeout/unsupported per
file into the ignored Lite.Conformance/artifacts/html5-catalog-survey.json.

Survey-noise rules (docs/html5-conformance-plan.md, Coordination): run on a quiet machine,
warm up first, run one survey at a time, and never alongside a CSS 2.1 baseline. A killed
run can be resumed with --resume: the runner rewrites its evidence every 100 outcomes, and
paths it already recorded are dropped from the queue and merged into the final artifact.

Usage (from the isolated worktree, after fetch-tests/build-wpt-manifest/html5-inventory):
    python scripts/survey-html5-catalog.py            # warm-up + full run
    python scripts/survey-html5-catalog.py --resume   # continue an interrupted run
"""
import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / 'Lite.Conformance' / 'artifacts'
INVENTORY = ARTIFACTS / 'html5-inventory.json'
QUEUE = ARTIFACTS / 'html5-catalog-survey-queue.txt'
RUN = ARTIFACTS / 'html5-catalog-survey-run.json'
LOG = ARTIFACTS / 'html5-catalog-survey-run.log'
SURVEY = ARTIFACTS / 'html5-catalog-survey.json'
CONFIGURATION = 'Release'
TARGET = 'https://www.w3.org/TR/2014/REC-html5-20141028/'


def read_json(path: Path):
    return json.loads(path.read_text(encoding='utf-8'))


def derive_queue(inventory) -> list[dict]:
    queue = [t for t in inventory['tests']
             if t['classification'] == 'unreviewed' and t['context'] == 'window'
             and t['kind'] == 'testharness']
    queue.sort(key=queue_order)
    return queue


def queue_order(item: dict):
    # Slow clusters run last: measured on the 2026-10-26 baseline run, each
    # editing/run page executes >1,200 subtests (minutes per file) and the
    # encoding legacy-mb cluster averages ~44s per file on timeout-bound async
    # failures. The html root is the Workstream 1 review frontier, so it goes
    # first; every path still runs exactly once.
    path = item['source']
    return (path.startswith('editing/run/'), not path.startswith('html/'), path)


def run_survey(paths: list[str], label: str) -> int:
    with tempfile.NamedTemporaryFile('w', suffix='.txt', delete=False, encoding='utf-8') as handle:
        handle.write('\n'.join(paths) + '\n')
        list_path = handle.name
    arguments = ['dotnet', 'run', '--project', str(ROOT / 'Lite.Conformance'),
                 '-c', CONFIGURATION, '--no-build', '--',
                 '--suite', 'wpt', '--survey-list', list_path, '--report', str(RUN)]
    print(f'[{label}] running {len(paths)} paths; log: {LOG.name}', flush=True)
    with LOG.open('a', encoding='utf-8') as log:
        log.write(f'\n===== {label}: {len(paths)} paths =====\n')
        process = subprocess.Popen(arguments, cwd=str(ROOT), stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')
        assert process.stdout is not None
        for line in process.stdout:
            log.write(line)
            log.flush()
            if line.startswith('  ') or line.startswith('==='):
                print(line, end='', flush=True)
        process.wait()
    Path(list_path).unlink(missing_ok=True)
    print(f'[{label}] runner exit code {process.returncode}', flush=True)
    return process.returncode


def merge(queue: list[dict], runs: list[dict]) -> dict:
    by_path: dict[str, dict] = {}
    for report in runs:
        for test in report.get('tests', []):
            by_path[test['path']] = test
    results = []
    counts = {'pass': 0, 'fail': 0, 'crash': 0, 'timeout': 0, 'empty': 0, 'skipped': 0, 'unsupported': 0, 'missing': 0}
    for item in queue:
        path = item['source']
        test = by_path.get(path)
        if test is None:
            counts['missing'] += 1
            results.append({'path': path, 'source': path, 'kind': item['kind'], 'context': item['context'],
                            'outcome': 'missing', 'detail': 'not executed by any run', 'subtestTotal': 0,
                            'subtestPass': 0})
            continue
        outcome = test['outcome']
        if outcome in counts:
            counts[outcome] += 1
        subtests = test.get('subtests') or []
        results.append({'path': path, 'source': path, 'kind': test.get('kind', item['kind']),
                        'context': test.get('context', item['context']), 'outcome': outcome,
                        'detail': test.get('detail', ''), 'subtestTotal': len(subtests),
                        'subtestPass': sum(1 for s in subtests if s.get('status') == 0)})
    last = runs[-1]
    return {
        'formatVersion': 1,
        'target': TARGET,
        'queueSize': len(queue),
        'outcomeCounts': counts,
        'identity': last.get('identity'),
        'startedUtc': min((r.get('startedUtc', '') for r in runs), default=''),
        'finishedUtc': last.get('finishedUtc', ''),
        'completed': all(r.get('completed') for r in runs) and counts['missing'] == 0,
        'results': results,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--warmup', type=int, default=25,
                        help='prime the pipeline with the first n queue files before the full run (0 disables)')
    parser.add_argument('--skip-warmup', action='store_true')
    parser.add_argument('--resume', action='store_true',
                        help='drop paths the interrupted run report already recorded, then merge both')
    parser.add_argument('--dry-run', action='store_true', help='write the queue file and print counts only')
    args = parser.parse_args()

    if not INVENTORY.exists():
        print(f'Missing {INVENTORY}; run --suite html5-inventory first.')
        return 2

    inventory = read_json(INVENTORY)
    queue = derive_queue(inventory)
    QUEUE.write_text('\n'.join(t['source'] for t in queue) + '\n', encoding='utf-8')
    print(f'Queue: {len(queue)} unclassified window-context testharness cases -> {QUEUE}')
    if args.dry_run:
        return 0

    runs = []
    queue_paths = [t['source'] for t in queue]
    if args.resume and RUN.exists():
        prior = read_json(RUN)
        recorded = {t['path'] for t in prior.get('tests', [])}
        runs = [prior]
        queue_paths = [p for p in queue_paths if p not in recorded]
        print(f'Resume: {len(recorded)} paths already recorded; {len(queue_paths)} remain.')
        if not queue_paths:
            print('Nothing left to run.')
    else:
        RUN.unlink(missing_ok=True)

    if queue_paths:
        if not args.skip_warmup and args.warmup > 0:
            warm = queue_paths[:args.warmup]
            print(f'Warm-up: {len(warm)} files (results are discarded; the full run re-executes them).')
            run_survey(warm, 'warm-up')
        code = run_survey(queue_paths, 'full')
        if code == 2:
            print('Runner reported a configuration error; survey artifact not written.')
            return 2
        if not RUN.exists():
            print(f'Missing run report {RUN}; the run may have crashed before the first checkpoint.')
            return 2
        runs.append(read_json(RUN))

    survey = merge(queue, runs)
    SURVEY.write_text(json.dumps(survey), encoding='utf-8')
    print(f"Survey artifact: {SURVEY}")
    print(f"Outcomes: {survey['outcomeCounts']}")
    print(f"Completed: {survey['completed']}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
