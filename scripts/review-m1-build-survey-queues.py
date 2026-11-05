#!/usr/bin/env python3
"""M1 inventory completion: build runnable survey queues from the per-root
source queues the static sweeps wrote.

The survey runner resolves catalog paths exactly, so each queued source maps to
the window-context catalog case that exercises it:

- an .any.js source maps to its .any.html window wrapper (the .any.worker.html
  variant is the worker context the 2026-10-24 scope decision excludes);
- a .window.js source maps to its .window.html wrapper;
- a variant-driven source (meta name=variant) maps to its FIRST variant case —
  one measured run classifies the source, and the bare path is preferred when
  the catalog carries one;
- every other source is already its own case path.

Sources with no window-context case at all are reported (they need a static
route instead of a measured one). Output: the queue files are rewritten in
place with the runnable paths, one per line.
"""
import json
import sys
from pathlib import Path

root = Path('Lite.Conformance')
catalog_path = root / 'artifacts/wpt-manifest.json'

QUEUES = [root / 'artifacts/m1-queue-html.txt', root / 'artifacts/m1-queue-encoding.txt',
          root / 'artifacts/m1-queue-fetch.txt', root / 'artifacts/m1-queue-dom.txt',
          root / 'artifacts/m1-queue-rest.txt']


def main():
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    cases = []
    for kind, tree in catalog['items'].items():
        if kind in ('support', 'spec'):
            continue
        walk(tree, '', kind, cases)
    by_source = {}
    case_to_source = {}
    for source, path in cases:
        by_source.setdefault(source, []).append(path)
        case_to_source.setdefault(path, source)

    for queue_file in QUEUES:
        if not queue_file.exists():
            continue
        lines = [l.strip() for l in queue_file.read_text(encoding='utf-8').splitlines() if l.strip()]
        # Idempotent: a line may be a source path or a case path from a previous build.
        sources = []
        seen = set()
        for line in lines:
            source = case_to_source.get(line, line)
            if source not in seen:
                seen.add(source)
                sources.append(source)
        runnable, missing = [], []
        for source in sources:
            case_paths = by_source.get(source, [])
            window = [p for p in case_paths if not is_worker_context(p)]
            if not window:
                missing.append(source)
                continue
            bare = [p for p in window if '?' not in p and '#' not in p]
            pick = bare[0] if bare else sorted(window)[0]
            runnable.append(pick)
        queue_file.write_text('\n'.join(runnable) + '\n', encoding='utf-8')
        print(f'{queue_file.name}: {len(sources)} sources -> {len(runnable)} runnable case paths, '
              f'{len(missing)} without a window-context case')
        for m in missing[:10]:
            print('   MISSING-WINDOW:', m)


def is_worker_context(path):
    file = path.split('?')[0].split('#')[0]
    return ('.worker.' in file or '.serviceworker.' in file or
            '.sharedworker.' in file or '.shadowrealm.' in file)


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
    sys.exit(main())
