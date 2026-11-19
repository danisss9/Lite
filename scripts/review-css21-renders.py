"""Approved-render review for the CSS 2.1 residue (the only writer of
Lite.Conformance/Css21/css21-approved-renders.json).

A self-describing case whose stated condition no machine check can read stays manual-pending.
A person compares its render with the stated condition once; an approval records the render's
RGBA SHA-256 (with platform and viewport), and from then on the harness decides the case by
hash (Css21/ApprovedRenders.cs). Any later change to the render drops it back to
manual-pending for re-review.

  1. Dump the renders (the harness renders exactly as the self-test route does):
       dotnet run --project Lite.Conformance -c Release -- --dump-renders <list.txt> <dump-dir>
  2. Build the contact sheet and review it in a browser:
       python scripts/review-css21-renders.py sheet <dump-dir>
       python scripts/review-css21-renders.py serve <dump-dir>
     Open the printed http://127.0.0.1 address (a file:// page cannot load the renders in
     every viewer), mark each group Approve or Reject, and press Export: approvals.json is
     saved in <dump-dir>. Pixel-identical renders (typically an official case and its
     WPT twin) form one group, decided once.
  3. Merge the approvals:
       python scripts/review-css21-renders.py apply <dump-dir> <approvals.json> [--operator NAME]
     Every approval must match a dumped render byte for byte (path and hash); rejected and
     undecided renders are left out.
"""
import argparse
import datetime
import html
import json
import re
import subprocess
import sys
from collections import OrderedDict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LEDGER = ROOT / 'Lite.Conformance' / 'Css21' / 'css21-approved-renders.json'
VENDOR = ROOT / 'Lite.Conformance' / 'vendor'
ASSERT_RE = re.compile(r'<meta\s+[^>]*name=["\']assert["\'][^>]*content=["\']([^"\']*)["\']'
                       r'|<meta\s+[^>]*content=["\']([^"\']*)["\'][^>]*name=["\']assert["\']', re.I)


def source_file(path: str) -> Path | None:
    if path.startswith('css21-official/'):
        candidate = VENDOR / 'css21-official-20110323' / path[len('css21-official/'):]
    else:
        candidate = VENDOR / 'wpt' / path
    return candidate if candidate.exists() else None


def assert_text(path: str) -> str:
    file = source_file(path)
    if file is None:
        return ''
    match = ASSERT_RE.search(file.read_text(encoding='utf-8', errors='replace'))
    return html.unescape((match.group(1) or match.group(2)) if match else '').strip()


def load_index(dump: Path) -> list[dict]:
    return json.loads((dump / 'index.json').read_text(encoding='utf-8'))


def sheet(dump: Path) -> None:
    entries = load_index(dump)
    groups: OrderedDict[str, list[dict]] = OrderedDict()
    for e in entries:
        groups.setdefault(e['sha256'], []).append(e)
    cards = []
    for i, (sha, members) in enumerate(groups.items()):
        first = members[0]
        statement = first.get('sentence') or ''
        label = 'Stated condition' if statement else 'Assertion (no stated sentence)'
        if not statement:
            statement = assert_text(first['path']) or '(the case states no condition)'
        names = ''.join(f'<li><code>{html.escape(m["path"])}</code></li>' for m in members)
        cards.append(f'''
<section class="card" data-group="{i}" data-sha="{sha}">
  <header><span class="n">#{i + 1}</span><ul>{names}</ul></header>
  <p class="label">{label}</p>
  <p class="statement">{html.escape(statement)}</p>
  <img loading="lazy" src="{html.escape(first['file'])}" alt="render of {html.escape(first['path'])}">
  <div class="actions">
    <button data-v="approve">Approve</button><button data-v="reject">Reject</button><button data-v="">Undecided</button>
    <span class="state"></span>
  </div>
</section>''')
    members_js = json.dumps({sha: [m['path'] for m in ms] for sha, ms in groups.items()})
    page = f'''<!doctype html><html><head><meta charset="utf-8"><title>CSS 2.1 render review</title>
<style>
 body{{font:14px/1.4 system-ui,sans-serif;margin:16px;background:#f6f6f4;color:#222}}
 .bar{{position:sticky;top:0;background:#f6f6f4;padding:8px 0;border-bottom:1px solid #ccc;z-index:1}}
 .card{{background:#fff;border:1px solid #ccc;border-radius:6px;padding:12px;margin:14px 0}}
 .card.approve{{border-color:#2a7;box-shadow:0 0 0 2px #2a7}} .card.reject{{border-color:#c33;box-shadow:0 0 0 2px #c33}}
 header ul{{margin:0;padding-left:18px}} .n{{font-weight:600}} .label{{margin:8px 0 0;color:#666;font-size:12px}}
 .statement{{margin:2px 0 10px;font-size:15px}} img{{max-width:100%;border:1px solid #999;display:block}}
 .actions{{margin-top:8px}} button{{margin-right:6px}}
</style></head><body>
<div class="bar"><b>{len(groups)}</b> groups / {len(entries)} renders.
 Approve only when the render plainly satisfies the stated condition.
 <span id="count"></span> <button id="export">Export approvals.json</button></div>
{''.join(cards)}
<script>
const members = {members_js};
const key = 'css21-review:' + location.pathname;
let state = {{}};
try {{ state = JSON.parse(localStorage.getItem(key) || '{{}}'); }} catch (e) {{}}
function paint() {{
  let a = 0, r = 0;
  document.querySelectorAll('.card').forEach(c => {{
    const v = state[c.dataset.sha] || '';
    c.classList.toggle('approve', v === 'approve'); c.classList.toggle('reject', v === 'reject');
    c.querySelector('.state').textContent = v; if (v === 'approve') a++; if (v === 'reject') r++;
  }});
  document.getElementById('count').textContent = `${{a}} approved, ${{r}} rejected.`;
}}
document.querySelectorAll('.card button').forEach(b => b.addEventListener('click', () => {{
  const sha = b.closest('.card').dataset.sha;
  if (b.dataset.v) state[sha] = b.dataset.v; else delete state[sha];
  try {{ localStorage.setItem(key, JSON.stringify(state)); }} catch (e) {{}}
  paint();
}}));
document.getElementById('export').addEventListener('click', async () => {{
  const out = [];
  for (const [sha, v] of Object.entries(state)) for (const p of members[sha] || []) out.push({{path: p, sha256: sha, decision: v}});
  const body = JSON.stringify(out, null, 1);
  // Served by `review-css21-renders.py serve`: the decisions are saved next to the renders.
  try {{
    const r = await fetch('approvals', {{method: 'POST', headers: {{'Content-Type': 'application/json'}}, body}});
    if (r.ok) {{ alert(`Saved ${{out.length}} decisions to approvals.json in the review folder.`); return; }}
  }} catch (e) {{}}
  const blob = new Blob([body], {{type: 'application/json'}});
  const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = 'approvals.json'; a.click();
}});
paint();
</script></body></html>'''
    (dump / 'review.html').write_text(page, encoding='utf-8')
    print(f'{dump / "review.html"}: {len(groups)} groups, {len(entries)} renders')


def serve(dump: Path, port: int) -> int:
    """Serve the dump folder on 127.0.0.1 so the sheet loads its renders in any browser, and
    save an exported decision list (POST /approvals) as approvals.json in the same folder."""
    import http.server
    import functools

    class Handler(http.server.SimpleHTTPRequestHandler):
        def do_POST(self):
            if self.path.rstrip('/') != '/approvals':
                self.send_error(404)
                return
            length = int(self.headers.get('Content-Length') or 0)
            data = self.rfile.read(length)
            try:
                decisions = json.loads(data)
                if not isinstance(decisions, list):
                    raise ValueError('expected a list')
            except ValueError as error:
                self.send_error(400, str(error))
                return
            (dump / 'approvals.json').write_text(json.dumps(decisions, indent=1), encoding='utf-8')
            self.send_response(204)
            self.end_headers()
            print(f'saved {len(decisions)} decisions to {dump / "approvals.json"}', flush=True)

        def log_message(self, *args):
            pass

    server = http.server.ThreadingHTTPServer(('127.0.0.1', port), functools.partial(Handler, directory=str(dump)))
    print(f'review sheet: http://127.0.0.1:{server.server_address[1]}/review.html', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


def default_operator() -> str:
    try:
        return subprocess.run(['git', 'config', 'user.name'], capture_output=True, text=True, cwd=ROOT).stdout.strip()
    except OSError:
        return ''


def apply(dump: Path, approvals_file: Path, operator: str, ledger_file: Path = LEDGER) -> int:
    if not operator:
        print('An operator name is required (--operator).', file=sys.stderr)
        return 2
    index = {e['path']: e for e in load_index(dump)}
    approvals = json.loads(approvals_file.read_text(encoding='utf-8'))
    ledger = json.loads(ledger_file.read_text(encoding='utf-8')) if ledger_file.exists() else {
        'schemaVersion': 1,
        'description': 'Renders a person approved against the stated condition; written only by '
                       'scripts/review-css21-renders.py and decided by hash in Css21/ApprovedRenders.cs.',
        'entries': []}
    entries = {e['path']: e for e in ledger['entries']}
    today = datetime.date.today().isoformat()
    added = rejected = 0
    for a in approvals:
        path, sha, decision = a.get('path'), a.get('sha256'), a.get('decision')
        dumped = index.get(path)
        if dumped is None or dumped['sha256'] != sha:
            print(f'refused {path}: not in this dump with that hash', file=sys.stderr)
            return 1
        if decision != 'approve':
            rejected += decision == 'reject'
            continue
        entries[path] = {
            'path': path,
            'platform': dumped['platform'],
            'viewport': dumped['viewport'],
            'sha256': sha,
            'sentence': dumped.get('sentence') or assert_text(path),
            'operator': operator,
            'date': today,
        }
        added += 1
    ledger['entries'] = sorted(entries.values(), key=lambda e: e['path'])
    with open(ledger_file, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(ledger, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print(f'{added} approvals written ({rejected} rejected left out); ledger holds {len(ledger["entries"])} entries.')
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    s = sub.add_parser('sheet')
    s.add_argument('dump', type=Path)
    v = sub.add_parser('serve')
    v.add_argument('dump', type=Path)
    v.add_argument('--port', type=int, default=8765)
    a = sub.add_parser('apply')
    a.add_argument('dump', type=Path)
    a.add_argument('approvals', type=Path)
    a.add_argument('--operator', default=None)
    a.add_argument('--ledger', type=Path, default=LEDGER, help='ledger to merge into (default: the harness ledger)')
    args = parser.parse_args()
    if args.command == 'sheet':
        sheet(args.dump)
        return 0
    if args.command == 'serve':
        return serve(args.dump, args.port)
    return apply(args.dump, args.approvals, args.operator if args.operator is not None else default_operator(), args.ledger)


if __name__ == '__main__':
    sys.exit(main())
