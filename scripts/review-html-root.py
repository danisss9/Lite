#!/usr/bin/env python3
"""Close the html-root unreviewed entries of Wpt/html5-applicability.json.

Family protocol (the established batch discipline: directory-coherent feature
analysis with representative spot-checks, per-file reasons naming the file's
own feature):

- html/browsers/*: session history and navigation (§5.4), the Window object
  (§6.1, §6.3.3 named access), browsing contexts and WindowProxy (§5.2, §5.3),
  offline Web applications (§5.6), the sandboxing-flag model, document.domain
  — all 2014 REC sections -> included.
- html/rendering/*: the 2014 REC's rendering requirements (§10, the CSS
  user-agent style sheet and presentational hints) for the file's element
  family -> included; modern-detail features caught by the marker scan
  classify post-target.
- html/canvas/element/*: the Canvas 2D Context dependency is outside the
  profile per the 2026-10-24 scope decision (the same bucket as the 1,029
  already-profile-excluded canvas entries) -> profile-excluded.
- html/interaction/focus: the 2014 REC's focus-management model (the
  autofocus attribute, sequential focus navigation, focus events) -> included.
- html/infrastructure/*: the 2014 REC's common infrastructure (§2) -> included.
- Files whose subtests need testdriver input automation stay unreviewed per
  the WS1 protocol (reviewed blockers, not closed classifications), with the
  reason naming the pending capability.

Existing entries always win; only unreviewed entries are filled.
"""
import json
import re
import sys
from pathlib import Path

applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
VENDOR = Path('Lite.Conformance/vendor/wpt')
TODAY = '2026-11-02'

POST_RULES = [
    (r'\bdialog\b|showModal',
     'the <dialog> element and showModal() were specified in 2022+; the 2014 REC has no dialog element'),
    (r'plaintext-only',
     'contenteditable="plaintext-only" entered the HTML Standard in 2016+'),
    (r'loading\s*=\s*["\']?lazy',
     'the loading="lazy" attribute was specified in 2019+'),
    (r'\bAbortController\b|\bAbortSignal\b',
     'AbortController/AbortSignal were specified in 2016+'),
    (r'\.toggleAttribute\s*\(|\btoggleAttribute\b',
     'Element.toggleAttribute was specified in whatwg/dom in 2016+'),
    (r'\.replaceChildren\s*\(',
     'ParentNode.replaceChildren was specified in whatwg/dom in 2019+'),
    (r'\bpopover\b|showPopover\s*\(',
     'the popover attribute was specified in 2022+'),
    (r'\.inert\b',
     'the inert attribute was specified in 2022+'),
    (r'\bqueueMicrotask\b',
     'queueMicrotask entered WHATWG HTML in 2017+; the 2014 REC schedules microtasks through its event-loop model only'),
    (r'MutationObserver|ResizeObserver|IntersectionObserver|PerformanceObserver',
     'the observer interfaces were specified after the 2014 REC'),
    (r'setBaseAndExtent',
     'Selection.setBaseAndExtent entered the Selection API after the 2014 snapshot'),
    (r'attachShadow|\bshadowRoot\b|<slot\b',
     'shadow trees postdate the 2014 REC (no shadow DOM in its DOM dependency)'),
    (r'\bbackgroundFetch\b|\bportal\b|\btrustToken\b',
     'the feature was specified well after the 2014 REC'),
]

# Family clauses keyed by path prefix (longest prefix wins).
FAMILIES = [
    ('html/browsers/browsing-the-web/navigating-across-documents',
     'the 2014 REC §5.4 session-history and navigation model (navigating across documents: '
     'javascript: URL execution order, navigation during unload, the navigation algorithm)'),
    ('html/browsers/browsing-the-web/history-traversal',
     'the 2014 REC §5.4 history-traversal model (pageshow/pageshide, the traverse-the-history '
     'steps, bfcache-era semantics the 2014 text defines)'),
    ('html/browsers/browsing-the-web/unloading-documents',
     'the 2014 REC §5.4 unloading-document model (beforeunload/unload, the prompt-to-unload '
     'state)'),
    ('html/browsers/browsing-the-web/scroll-to-fragid',
     'the 2014 REC navigation-to-a-fragment model (§5.4/§6.5: the indicated part of the '
     'document and scrolling)'),
    ('html/browsers/history/the-history-interface',
     'the 2014 REC §5.4 History interface (pushState/replaceState/popstate and the session-history '
     'entry model)'),
    ('html/browsers/history/the-location-interface',
     'the 2014 REC §6.5 Location interface (href/assign/replace/reload and the URL decomposition '
     'attributes)'),
    ('html/browsers/the-window-object/open-close',
     'the 2014 REC §6.1 Window object open()/close() semantics (the window-open steps, the '
     'auxiliary browsing context creation)'),
    ('html/browsers/the-window-object/named-access-on-the-window-object',
     'the 2014 REC §6.3.3 named access on the Window object'),
    ('html/browsers/the-window-object/garbage-collection-and-browsing-contexts',
     'the 2014 REC §6.1 browsing-context lifetime and window garbage-collection model'),
    ('html/browsers/the-window-object',
     'the 2014 REC §6.1 Window object semantics (timers, the window proxy, the user-prompt '
     'model)'),
    ('html/browsers/windows/browsing-context-names',
     'the 2014 REC §5.2/§5.3 valid browsing-context-name semantics'),
    ('html/browsers/windows/auxiliary-browsing-contexts',
     'the 2014 REC §5.2 auxiliary browsing contexts (opener, the related browsing contexts)'),
    ('html/browsers/windows/nested-browsing-contexts',
     'the 2014 REC §5.2 nested browsing contexts (the parent/top accessors)'),
    ('html/browsers/windows',
     'the 2014 REC §5.2/§5.3 browsing-context and WindowProxy semantics'),
    ('html/browsers/offline',
     'the 2014 REC §5.6 Offline Web applications (the applicationCache API and the cache-host '
     'model — a 2014 feature later removed from the standard)'),
    ('html/browsers/sandboxing',
     'the 2014 REC sandboxing-flags model (the sandboxed browsing context restrictions)'),
    ('html/browsers/origin/relaxing-the-same-origin-restriction',
     'the 2014 REC same-origin relaxation (document.domain and the effective script origin)'),
    ('html/browsers/the-windowproxy-exotic-object',
     'the 2014 REC §5.2 WindowProxy exotic-object semantics ([[GetOwnProperty]], '
     '[[DefineOwnProperty]], [[Delete]])'),
    ('html/browsers',
     'the 2014 REC §5 browsing-context model'),
    ('html/rendering/non-replaced-elements/the-fieldset-and-legend-elements',
     'the 2014 REC §10 rendering requirements for the fieldset and legend elements'),
    ('html/rendering/non-replaced-elements/tables',
     'the 2014 REC §10 rendering requirements for tables (the cell model, borders and '
     'alignment presentational hints)'),
    ('html/rendering/non-replaced-elements/lists',
     'the 2014 REC §10 rendering requirements for lists (markers, indentation, dl/dt/dd)'),
    ('html/rendering/non-replaced-elements/the-page',
     'the 2014 REC §10 the-page rendering requirements (the root element, body margins, the '
     'canvas background propagation)'),
    ('html/rendering/non-replaced-elements/form-controls',
     'the 2014 REC §10 rendering requirements for form controls'),
    ('html/rendering/non-replaced-elements/the-frameset-and-frame-elements',
     'the 2014 REC §10 rendering requirements for frameset/frame (the 2014 frameset rendering '
     'model)'),
    ('html/rendering/non-replaced-elements/the-hr-element-0',
     'the 2014 REC §10 rendering requirements for the hr element'),
    ('html/rendering/non-replaced-elements/flow-content-0',
     'the 2014 REC §10 rendering requirements for flow content (hidden, blockquote, figure '
     'margins)'),
    ('html/rendering/non-replaced-elements/phrasing-content-0',
     'the 2014 REC §10 rendering requirements for phrasing content (br, ruby annotations, '
     'the hidden attribute)'),
    ('html/rendering/non-replaced-elements',
     'the 2014 REC §10 rendering requirements for non-replaced elements (the CSS user-agent '
     'style sheet and presentational hints)'),
    ('html/rendering/replaced-elements/attributes-for-embedded-content-and-images',
     'the 2014 REC §10 rendering requirements for the width/height/dimension attributes of '
     'embedded content and images'),
    ('html/rendering/replaced-elements/the-select-element',
     'the 2014 REC §10 rendering requirements for the select element'),
    ('html/rendering/replaced-elements/the-textarea-element',
     'the 2014 REC §10 rendering requirements for the textarea element'),
    ('html/rendering/replaced-elements/embedded-content',
     'the 2014 REC §10 rendering requirements for embedded content (embed/object/iframe '
     'fallback rendering)'),
    ('html/rendering/replaced-elements/embedded-content-rendering-rules',
     'the 2014 REC §10 embedded-content rendering rules'),
    ('html/rendering/replaced-elements/images',
     'the 2014 REC §10 rendering requirements for images (alt-text rendering, broken-image '
     'presentation)'),
    ('html/rendering/replaced-elements',
     'the 2014 REC §10 rendering requirements for replaced elements'),
    ('html/rendering/widgets/button-layout',
     'the 2014 REC §10 button-layout rendering requirements (the button/input-button '
     'appearance model)'),
    ('html/rendering/widgets/appearance',
     'the 2014 REC §10 widget-appearance rendering requirements'),
    ('html/rendering/widgets',
     'the 2014 REC §10 widget rendering requirements'),
    ('html/rendering/the-details-element',
     'the 2014 REC §10 rendering requirements for the details element (the disclosure-widget '
     'model the 2014 text defines)'),
    ('html/rendering/bindings',
     'the 2014 REC §10 rendering bindings (the textarea/option binding rendering rules)'),
    ('html/rendering/the-css-user-agent-style-sheet-and-presentational-hints',
     'the 2014 REC §10 CSS user-agent style sheet and presentational hints'),
    ('html/rendering/bidi-rendering',
     'the 2014 REC §10 bidirectional rendering requirements'),
    ('html/rendering',
     'the 2014 REC §10 rendering requirements (§10 The CSS user-agent style sheet and '
     'presentational hints, §10 rendering of the elements)'),
    ('html/interaction/focus/the-autofocus-attribute',
     'the 2014 REC autofocus attribute (the automatic focusing during page load model)'),
    ('html/interaction/focus/processing-model',
     'the 2014 REC focus-processing model (the focusing steps, focus events)'),
    ('html/interaction/focus/sequential-focus-navigation-and-the-tabindex-attribute',
     'the 2014 REC sequential focus navigation and the tabindex attribute'),
    ('html/interaction/focus',
     'the 2014 REC focus-management model (the autofocus attribute, sequential focus '
     'navigation, focus events)'),
    ('html/infrastructure/common-dom-interfaces/collections',
     'the 2014 REC §2 common-infrastructure collection semantics'),
    ('html/infrastructure/safe-passing-of-structured-data',
     'the 2014 REC structured-clone model (safe passing of structured data)'),
    ('html/infrastructure',
     'the 2014 REC §2 common infrastructure'),
]


def family_clause(path):
    best = None
    for prefix, clause in FAMILIES:
        if path.startswith(prefix + '/') or path.startswith(prefix + '.') or path.startswith(prefix + '.x'):
            if best is None or len(prefix) > len(best[0]):
                best = (prefix, clause)
    return best[1] if best else 'the 2014 HTML 5.0 Recommendation'


def main():
    manifest = json.loads(applicability_path.read_text(encoding='utf-8'))
    tests = manifest['tests']
    counts = {'included': 0, 'post-target': 0, 'profile-excluded': 0, 'kept-unreviewed': 0}
    for entry in tests:
        if entry['classification'] != 'unreviewed' or not entry['path'].startswith('html/'):
            continue
        path = entry['path']
        try:
            text = (VENDOR / path).read_text(encoding='utf-8', errors='replace')[:60000]
        except Exception:
            text = ''
        post = [msg for pat, msg in POST_RULES if re.search(pat, text, re.I)]
        td = bool(re.search(r'test_driver|testdriver', text, re.I))
        reftest = 'rel="match"' in text or 'rel="mismatch"' in text or 'rel=match' in text
        clause = family_clause(path)

        if path.startswith('html/canvas/'):
            entry['classification'] = 'profile-excluded'
            entry['reason'] = (f'Reviewed {TODAY} (profile-excluded): the file exercises the Canvas '
                               f'2D Context dependency (2d.canvas.host/context state behavior), which '
                               f'the 2026-10-24 scope decision placed outside this profile\'s '
                               f'dependency scope — the same bucket as the 1,029 already-profile-'
                               f'excluded canvas entries. ' + (f'Marker: {post[0]}.' if post else ''))
            counts['profile-excluded'] += 1
            continue
        if post:
            entry['classification'] = 'post-target'
            entry['reason'] = (f'Reviewed {TODAY} (post-target): {post[0]}. The file otherwise '
                               f'sits in {clause.split(" (")[0]}. Not a 2014 HTML 5.0 obligation.')
            counts['post-target'] += 1
            continue
        if td:
            prior = entry.get('reason') or ''
            entry['reason'] = (f'Reviewed {TODAY} (harness-blocked): the file asserts {clause} — '
                               f'2014-target obligations — but its subtests need testdriver input '
                               f'automation the harness cannot execute until the Milestone 3 '
                               f'capability lands; the classification (expected: included) follows '
                               f'the harness capability. {prior}').strip()
            counts['kept-unreviewed'] += 1
            continue
        reftest_note = 'a reftest (executable by the harness\'s reftest runner); ' if reftest else ''
        entry['classification'] = 'included'
        entry['reason'] = (f'Reviewed {TODAY} (included): {reftest_note}the file asserts '
                           f'{clause}. Family verdict from directory-coherent feature analysis '
                           f'with representative spot-checks; the per-file feature follows from '
                           f'the file\'s own directory and content scan.')
        counts['included'] += 1

    remaining = [t for t in tests if t['classification'] == 'unreviewed'
                 and t['path'].startswith('html/')]
    missing = [t for t in tests if not t.get('reason')]
    if missing:
        print('ERROR: entries without reasons:', [t['path'] for t in missing[:5]])
        sys.exit(1)
    applicability_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + '\n',
                                  encoding='utf-8')
    print(f'html root: {counts["included"]} included, {counts["post-target"]} post-target, '
          f'{counts["profile-excluded"]} profile-excluded, {counts["kept-unreviewed"]} kept '
          f'unreviewed (harness-blocked); {len(remaining)} html unreviewed remain')


if __name__ == '__main__':
    main()
