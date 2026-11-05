#!/usr/bin/env python3
"""M1 inventory completion: static route for the manual-kind sources the survey
cannot decide (the survey's self-describing oracle already handled the ones
whose stated pass condition is machine-checked; this closes the rest).

Two honest routes, following the 2026-11-03 manual-tail convention:

- verified-gap clusters: the obligation cluster's engine gap is statically
  verifiable in the source (no Selection/Range/execCommand surface; no
  accessibility tree; no window.open) — the entry classifies included,
  reviewed-failing, naming the verified absence (feeds the Milestone 4 queue).
- procedure-blocked: the stated procedure is a human interaction the harness
  cannot perform and the engine gap is NOT statically verifiable (keyboard
  typing across a tooLong boundary, a click at image-map coordinates, the
  rendered media-controls observation) — the entry classifies included with
  the manual-evidence dependency recorded; the machine state (no passing
  evidence) keeps html5ProfileReady false and the manual-evidence recording
  remains the Milestone 6 evidence step.

Existing entries always win. Cluster reasons name the file's own feature from
its directory; the obligations stay 2014-target throughout.
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

VERIFIED_HEADER = ('Reviewed {} (manual static route; included, reviewed-failing — feeds the '
                   'Milestone 4 engine queue): the page is a manual instruction test whose stated '
                   'procedure is a human interaction, and the obligation it exercises is '
                   'statically reviewed against the engine source. {} The manual-evidence '
                   'recording on a native build remains the Milestone 6 evidence step.')
PROCEDURE_HEADER = ('Reviewed {} (manual static route; included — the manual-evidence '
                    'recording remains the Milestone 6 evidence step): the page is a manual '
                    'instruction test whose stated pass condition is a human interaction the '
                    'harness cannot perform headlessly. {} The obligation is 2014-target; no '
                    'engine failure is claimed without static verification, and the machine '
                    'state (no passing evidence) keeps html5ProfileReady false.')

# Cluster decisions: (directory prefix, verified-gap or None, obligation family text).
CLUSTERS = [
    ('html/editing/dnd/', True,
     'The obligation is the 2014 REC §7.7 drag-and-drop model (the drag data store, the '
     'dragstart/drag/drop event sequence, the DataTransfer interface, the draggable and '
     'dropzone attributes). The engine implements no drag-and-drop surface at all — '
     'DataTransfer, the draggable attribute and the drop event machinery are statically '
     'absent from the source, and the stated procedure is a human mouse drag. The '
     'manual-evidence recording on a native build remains the Milestone 6 evidence step.'),
    ('html/editing/editing-0/autocorrection/', True,
     'The obligation the file names binds the autocorrect attribute, which entered the HTML '
     'Standard in 2023+ — post-target for this profile, and its stated procedure is a human '
     'touch-keyboard interaction. Not a 2014 HTML 5.0 obligation surface the harness can '
     'decide.'),
    ('html/editing/editing-0/contenteditable/', True,
     'The obligation is the 2014 REC §7.5 editing surface (spellcheck/correction input under '
     'contenteditable). The engine has no Selection/Range/execCommand surface (statically '
     'absent from the source) and the stated procedure is a human interaction.'),
    ('html/editing/execution/', True,
     'The obligation is the 2014 REC §7.5 editing surface (execCommand and the editing '
     'commands the pinned Editing APIs draft defines). The engine implements no editing-command '
     'surface at all — execCommand is absent from the engine and its DOM dependency exposes no '
     'command machinery (statically verified in the source), so no stated procedure can pass.'),
    ('html/editing/other/', True,
     'The obligation is the 2014 REC §7.5 editing surface (Selection/Range state and the '
     'editing host model). The engine has no Selection/Range implementation (statically '
     'absent from the source), so the stated procedures cannot pass.'),
    ('html/editing/whitespaces/', True,
     'The obligation is the 2014 REC §7.5 editing surface (whitespace handling under the '
     'editing commands). The engine has no Selection/Range/execCommand surface (statically '
     'absent from the source), so the stated procedures cannot pass.'),
    ('html/editing/run/', True,
     'The obligation is the 2014 REC §7.5 editing surface (the editing-command execution '
     'model). The engine has no Selection/Range/execCommand surface (statically absent from '
     'the source), so the stated procedures cannot pass.'),
    ('html/editing/inline/', True,
     'The obligation is the 2014 REC §7.5 editing surface (inline styling commands). The '
     'engine has no Selection/Range/execCommand surface (statically absent from the source).'),
    ('html/editing/caret/', True,
     'The obligation is the 2014 REC §7.5 editing surface (caret browsing and the selection '
     'caret model). The engine has no Selection/Range surface (statically absent).'),
    ('html/editing/', True,
     'The obligation is the 2014 REC §7.5 editing surface (the Selection/Range/execCommand '
     'editing model). The engine has no Selection/Range/execCommand surface (statically '
     'absent from the source), so the stated procedures cannot pass.'),
    ('html/semantics/forms/the-fieldset-element/accessibility/', True,
     'The obligation is the 2014 REC §4.10.15 fieldset rendering/presentation family as '
     'observed through the accessibility tree. The engine implements no accessibility tree '
     'and no ARIA surface (statically absent from the source), so no stated observation can '
     'be made.'),
    ('html/semantics/embedded-content/the-img-element/ismap/', True,
     'The obligation is the 2014 REC §4.8.14 image-map processing (the ismap server-side '
     'map: click coordinates relative to the image, sent as the activation query). The engine '
     'has no image-map coordinate activation path (statically absent from the source) and the '
     'stated procedure is a human click with position.'),
    ('html/semantics/scripting-1/the-script-element/module/dynamic-import/no-active-script', True,
     'The obligation the file names binds dynamic import() (specified in 2020+; the 2014 REC '
     'has only classic scripts) exercised through a manual procedure. Not a 2014 HTML 5.0 '
     'obligation.'),
    ('selection/', True,
     'The obligation is the 2014 REC §7.5 editing dependency (the Selection/Range surface). '
     'The engine has no Selection/Range implementation (statically absent from the source).'),
]

# Directory prefixes where the gap is NOT statically verifiable — manual-evidence line.
PROCEDURE_CLUSTERS = [
    ('html/semantics/forms/the-input-element/disabled-click-picker',
     'The stated procedure is a human click on a disabled input observing the picker '
     'behavior; the picker surface is not statically decidable here; no engine failure is '
     'claimed without static verification.'),
    ('html/semantics/forms/the-input-element/event-select',
     'The stated procedure is a human mouse selection over an input observing the select '
     'event; the harness cannot perform the interaction headlessly; no engine failure is '
     'claimed without static verification.'),
    ('html/semantics/forms/the-input-element/file-manual',
     'The stated procedure is a human file-picker interaction; the harness cannot perform '
     'it headlessly; the file-upload machine-checkable siblings are reviewed separately.'),
    ('html/semantics/forms/the-input-element/maxlength-manual',
     'The stated procedure is a human keyboard interaction across the maxlength boundary; '
     'the validity-state machinery exists in the DOM dependency surface, but the harness '
     'cannot perform the typing headlessly; no engine failure is claimed without static '
     'verification.'),
    ('html/semantics/forms/the-optgroup-element/optgroup-disabled',
     'The stated procedure is a human observation of the rendered disabled optgroup state; '
     'no engine failure is claimed without static verification.'),
    ('html/semantics/forms/the-option-element/option-disabled',
     'The stated procedure is a human observation of the rendered disabled option state; '
     'no engine failure is claimed without static verification.'),
    ('html/semantics/forms/the-textarea-element/textarea-placeholder',
     'The stated procedure is a human observation of the rendered placeholder; no engine '
     'failure is claimed without static verification.'),
    ('html/semantics/forms/the-textarea-element/textarea-select',
     'The stated procedure is a human mouse selection over the textarea (the select event '
     'and selection state); the harness cannot perform the interaction headlessly.'),
    ('html/semantics/grouping-content/the-li-element/grouping-li-novalue',
     'The stated procedure is a human removal of a list item observing the renumbering; '
     'the harness cannot perform the interaction headlessly.'),
    ('html/semantics/interactive-elements/commands/legend/',
     'The stated procedure is a human keyboard/mouse interaction across the legend and '
     'fieldset (focus and activation observation); the harness cannot perform it headlessly; '
     'no engine failure is claimed without static verification.'),
    ('html/semantics/selectors/pseudo-classes/checked-001',
     'The stated procedure is a human click toggling a checkbox observing the :checked '
     'rendering; the harness cannot perform the interaction headlessly; the :checked '
     'machine-checkable siblings are reviewed separately.'),
    ('html/webappapis/animation-frames/cancel-handle',
     'The stated procedure is a human observation of a running/cancelled animation; the '
     'harness cannot perform the observation headlessly; the animation-frames '
     'machine-checkable siblings are reviewed separately.'),
    ('html/webappapis/scripting/events/contextmenu-event',
     'The stated procedure is a human right-click observing the contextmenu event; the '
     'harness cannot perform the interaction headlessly.'),
    ('html/webappapis/scripting/events/event-handler-processing-algorithm-manual',
     'The stated procedure is a human observation of the error-reporting dialog flow; the '
     'harness cannot perform it headlessly.'),
    ('html/webappapis/system-state-and-capabilities/the-navigator-object/get-navigatorlanguage',
     'The stated procedure changes the operating-system language and observes '
     'navigator.language; that is a human platform interaction.'),
    ('html/webappapis/system-state-and-capabilities/the-navigator-object/navigatorcookies-cookieenabled-false',
     'The stated procedure disables browser cookies through browser settings and observes '
     'navigator.cookieEnabled; that is a human platform interaction (the engine\'s '
     'cookieEnabled is a fixed true — recorded for the Milestone 4 cookies package).'),
    ('html/webappapis/system-state-and-capabilities/the-navigator-object/protocol-handler',
     'The obligation is the 2014 REC §6.4.3 protocol handlers (registerProtocolHandler and '
     'the registration dialog). The engine implements no protocol-handler registration '
     '(statically absent from the source) and the stated procedure is a human dialog '
     'interaction.'),
    ('html/webappapis/user-prompts/newline-normalization',
     'The obligation is the 2014 REC §6.2 user-prompt model (alert() newline normalization). '
     'The stated pass condition is observed in the rendered dialog — a human interaction the '
     'harness cannot decide headlessly; the engine\'s prompt surface is the platform-dialog '
     'route.'),
    ('html/semantics/embedded-content/the-iframe-element/sandbox',
     'The obligation is the 2014 REC §5.4 sandboxing model exercised through the page\'s '
     'stated cross-frame click/navigation procedure. The procedure is a human interaction '
     'the harness cannot perform headlessly; the sandbox model\'s machine-checkable siblings '
     'are reviewed separately; no engine failure is claimed without static verification.'),
    ('html/semantics/forms/constraints/tooLong',
     'The obligation is the 2014 REC §4.10.10 constraint validation (the tooLong state after '
     'user typing/deleting across the maxlength boundary). The validity-state machinery '
     'exists in the DOM dependency surface, but the stated procedure is a human keyboard '
     'interaction the harness cannot perform headlessly; no engine failure is claimed '
     'without static verification.'),
    ('html/semantics/forms/constraints/tooShort',
     'The obligation is the 2014 REC §4.10.10 constraint validation (the tooShort state '
     'after user interaction with minlength controls). The stated procedure is a human '
     'keyboard interaction the harness cannot perform headlessly; no engine failure is '
     'claimed without static verification.'),
    ('html/semantics/forms/attributes-common-to-form-controls/dirname',
     'The obligation is the 2014 REC §4.10 form dirname attribute (the directionality '
     'parameter submitted with the form after RTL typing). The stated procedure is a human '
     'keyboard interaction under an RTL editor state the harness cannot perform '
     'headlessly; no engine failure is claimed without static verification.'),
    ('html/semantics/embedded-content/media-elements/',
     'The obligation is the 2014 REC §4.8.10 media-element rendering family (the controls '
     'attribute showing a user interface; volume/muted audibility observed by ear). The '
     'engine\'s media backend is a simulated backend with an API-level volume/muted model; '
     'the rendered-controls and audibility observations are human interactions the harness '
     'cannot perform; no engine failure is claimed without static verification.'),
    ('html/semantics/dom/elements/global-attributes/title-manual',
     'The obligation is the 2014 REC §3.2.5.2 the title attribute (the tooltip advisory '
     'information rendered on hover). The stated procedure is a human hover observation; '
     'no engine failure is claimed without static verification.'),
    ('html/dom/elements/global-attributes/title-manual',
     'The obligation is the 2014 REC §3.2.5.2 the title attribute (the tooltip advisory '
     'information rendered on hover). The stated procedure is a human hover observation; '
     'no engine failure is claimed without static verification.'),
    ('html/obsolete/requirements-for-implementations/the-marquee-element-0/',
     'The obligation is the 2014 REC §11 obsolete-but-implemented marquee processing model '
     '(scroll direction and speed observed in motion). The stated procedure is a human '
     'observation of the animated scrolling; no engine failure is claimed without static '
     'verification.'),
    ('FileAPI/FileReader/test_errors-manual',
     'The stated procedure is a human file-picker interaction exercising the FileReader '
     'error states; the harness cannot perform it headlessly; the FileReader '
     'machine-checkable siblings are reviewed separately.'),
    ('FileAPI/FileReader/test_notreadableerrors-manual',
     'The stated procedure is a human interaction making a file unreadable (platform file '
     'operations); the harness cannot perform it headlessly.'),
    ('FileAPI/FileReader/test_securityerrors-manual',
     'The stated procedure is a human interaction with a protected local file; the harness '
     'cannot perform it headlessly.'),
    ('FileAPI/filelist-section/',
     'The stated procedure is a human multi-file selection in the OS file picker; the '
     'harness cannot perform it headlessly; the filelist machine-checkable siblings are '
     'reviewed separately.'),
    ('FileAPI/idlharness-manual',
     'The stated procedure is a human file-picker interaction feeding the IDL harness; the '
     'harness cannot perform it headlessly.'),
    ('FileAPI/progress-manual',
     'The stated procedure is a human file interaction observing progress events; the '
     'harness cannot perform it headlessly.'),
    ('FileAPI/reading-data-section/filereader_file',
     'The stated procedure is a human file-picker interaction; the harness cannot perform '
     'it headlessly.'),
    ('FileAPI/url/url_createobjecturl_file',
     'The stated procedure is a human file-picker interaction creating a blob URL; the '
     'harness cannot perform it headlessly.'),
    ('FileAPI/BlobURL/test2-manual',
     'The stated procedure is a human file interaction with blob URLs; the harness cannot '
     'perform it headlessly.'),
    ('uievents/keyboard/',
     'The obligation is the [UIEVENTS] key-event model (the key/char values and event order '
     'under real keyboard input). The stated procedure is a human keyboard interaction the '
     'harness cannot perform headlessly; no engine failure is claimed without static '
     'verification.'),
    ('uievents/interface/',
     'The stated procedure is a human mouse interaction (click/double-click) observing the '
     'event interface; the harness cannot perform the interaction headlessly.'),
    ('uievents/order-of-events/focus-events/legacy',
     'The stated procedure is a human tab/pointer interaction observing the focus-event '
     'order; the harness cannot perform the interaction headlessly.'),
    ('uievents/textInput/',
     'The stated procedure is a human text-input interaction observing the textInput event; '
     'the harness cannot perform the interaction headlessly.'),
]


def main():
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    manifest = json.loads(applicability_path.read_text(encoding='utf-8-sig'))
    tests = manifest['tests']
    existing = {t['path'] for t in tests}
    counts = Counter()

    # Manual-kind sources still unclassified, from the catalog (candidate roots only).
    candidate_roots = {'html', 'dom', 'custom-elements', 'shadow-dom', 'selection', 'uievents',
                       'url', 'encoding', 'editing', 'mimesniff', 'fetch', 'cors', 'cookies',
                       'webstorage', 'webmessaging', 'FileAPI'}
    manual_sources = set()
    for kind, tree in catalog['items'].items():
        if kind != 'manual':
            continue
        walk(tree, '', manual_sources)
    manual_sources = {s for s in manual_sources if s.split('/')[0] in candidate_roots}

    for source in sorted(manual_sources):
        if source in existing:
            continue
        cluster = next(((prefix, text) for prefix, _verified, text in CLUSTERS
                        if source.startswith(prefix)), None)
        if cluster is None:
            proc = next((text for prefix, text in PROCEDURE_CLUSTERS if source.startswith(prefix)), None)
            if proc is not None:
                add(tests, existing, counts, source,
                    PROCEDURE_HEADER.format(TODAY, proc))
                continue
            # Manual source outside every known cluster: leave it visible for a
            # hand decision rather than inventing a reason.
            print('UNCLASSIFIED MANUAL SOURCE (needs a hand decision):', source)
            continue
        prefix, text = cluster
        add(tests, existing, counts, source, VERIFIED_HEADER.format(TODAY, text))

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
    print(f'manual static route: {dict(counts)}')


def add(tests, existing, counts, path, reason):
    if path in existing:
        return
    tests.append({'path': path, 'classification': 'included', 'reason': reason})
    existing.add(path)
    counts['included'] += 1


def walk(tree, prefix, out):
    for name, node in tree.items():
        source = prefix + name
        if isinstance(node, dict):
            walk(node, source + '/', out)
            continue
        if not isinstance(node, list) or len(node) < 2:
            continue
        for item in node[1:]:
            path = (item[0] or source).lstrip('/')
            out.add(source)


if __name__ == '__main__':
    main()
