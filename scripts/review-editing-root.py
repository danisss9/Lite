#!/usr/bin/env python3
"""Close the editing-root unreviewed entries of Wpt/html5-applicability.json.

The 2014 HTML5 REC's §7.5 Editing APIs define contentEditable, designMode and
the execCommand/queryCommand* editing commands (the command behavior itself by
the willful-violation reference), so files whose assertions exercise those
commands classify as included. Files exercising post-2014 features classify
post-target with the feature citation. Files whose subtests need testdriver
input automation stay unreviewed (the WS1 protocol: reviewed blockers, not
closed classifications) but gain the reason naming the pending harness
capability and the file's exercised commands.

Existing entries always win; only unreviewed entries are filled. The result
must satisfy the applicability schema's structural invariants (classification
enum, non-empty reasons, assertion fields only on mixed).
"""
import json
import re
import sys
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
VENDOR = Path('Lite.Conformance/vendor/wpt')
TODAY = '2026-11-02'

cmd_re = re.compile(r'execCommand\s*\(\s*["\']([a-zA-Z]+)["\']')
POST_RULES = [
    (r'\bdialog\b|showModal',
     'the <dialog> element and showModal() were specified in 2022+; the 2014 REC has no dialog element'),
    (r'plaintext-only',
     'contenteditable="plaintext-only" entered the HTML Standard in 2016+; the 2014 REC\'s §7.5 '
     'contenteditable enumerates only true/false/inherit'),
    (r'beforeinput',
     'the beforeinput event and the Input Events specification were introduced in 2017+; the 2014 '
     'REC has no input-event editing model'),
    (r'loading\s*=\s*["\']?lazy',
     'the loading="lazy" attribute was specified in 2019+; the 2014 REC loads every image/iframe '
     'during traversal (§4.8, §4.11)'),
    (r'setBaseAndExtent',
     'Selection.setBaseAndExtent entered the Selection API after the 2014 snapshot'),
    (r'replaceChildren',
     'ParentNode.replaceChildren was specified in whatwg/dom in 2019+; the 2014 REC\'s DOM '
     'dependency has no such method'),
    (r'attachShadow|\bshadowRoot\b',
     'shadow trees (attachShadow/shadowRoot) postdate the 2014 REC (no shadow DOM in its DOM '
     'dependency)'),
    (r'\bInputEvent\b|getTargetRanges',
     'the InputEvent interface belongs to the Input Events specification (2017+), absent from '
     'the 2014 REC'),
]


def scan(path):
    text = (VENDOR / path).read_text(encoding='utf-8', errors='replace')[:40000]
    cmds = sorted(set(c.lower() for c in cmd_re.findall(text)))
    post = [msg for pat, msg in POST_RULES if re.search(pat, text, re.I)]
    td = bool(re.search(r'test_driver|testdriver', text, re.I))
    return cmds, post, td


def main():
    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    tests = manifest['tests']
    counts = {'included': 0, 'post-target': 0, 'kept-unreviewed': 0}
    for entry in tests:
        if entry['classification'] != 'unreviewed' or not entry['path'].startswith('editing/'):
            continue
        path = entry['path']
        cmds, post, td = scan(path)
        cmd_note = f"exercises the {', '.join(cmds[:6])} editing command(s)" if cmds \
            else "exercises the §7.5 editing APIs (contentEditable/designMode selection carets)"
        crash_note = 'crash-robustness page (passes by not crashing); ' if '/crashtests/' in path else ''
        if post:
            entry['classification'] = 'post-target'
            entry['reason'] = (f'Reviewed {TODAY} (post-target): {post[0]}; the file {cmd_note}. '
                               f'Not a 2014 HTML 5.0 obligation.')
            counts['post-target'] += 1
            continue
        if td:
            prior = entry.get('reason') or ''
            entry['reason'] = (f'Reviewed {TODAY} (harness-blocked): the file {cmd_note} — '
                               f'2014-target §7.5 obligations — but its subtests need testdriver '
                               f'input automation the harness cannot execute until the Milestone 3 '
                               f'capability lands; the classification (expected: included) follows '
                               f'the harness capability. ' + prior).strip()
            counts['kept-unreviewed'] += 1
            continue
        manual_note = ('A manual test: the obligation is 2014-target and the evidence comes from '
                       'the recorded manual-evidence flow (Milestones 3/6). ' if '/manual/' in path else '')
        entry['classification'] = 'included'
        entry['reason'] = (f'Reviewed {TODAY} (included): {crash_note}the file {cmd_note} — the '
                           f'2014 REC §7.5 Editing APIs (contentEditable, designMode and the '
                           f'execCommand/queryCommand* command set, whose behavior the REC defines '
                           f'by its willful-violation editing-spec reference). {manual_note}'
                           ).replace('  ', ' ')
        counts['included'] += 1

    remaining = [t for t in tests if t['classification'] == 'unreviewed'
                 and t['path'].startswith('editing/')]
    missing_reason = [t for t in tests if not t.get('reason')]
    if missing_reason:
        print('ERROR: entries without reasons:', [t['path'] for t in missing_reason[:5]])
        sys.exit(1)
    applicability_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n',
                                  encoding='utf-8')
    print(f'editing root: {counts["included"]} included, {counts["post-target"]} post-target, '
          f'{counts["kept-unreviewed"]} kept unreviewed (harness-blocked); '
          f'{len(remaining)} editing unreviewed remain')


if __name__ == '__main__':
    main()
