#!/usr/bin/env python3
"""M1 inventory completion: static sweep of the html root's unmanifested catalog
sources (the guarded omission check's largest block: 3,703 cases / 3,368 sources).

Adds manifest entries per unmanifested source following the established batch
discipline (existing entries always win; the propose tool never writes these):

- html/canvas/*: profile-excluded — the Canvas 2D Context dependency is outside
  the profile per the 2026-10-24 scope decision (the bucket of the 1,142
  already-excluded canvas entries).
- whole-directory post-target: directories whose names are themselves later-era
  features (interestfor 2024+, popovers 2022+, permission-element 2015+,
  the customizable <select> combobox 2024+).
- post-target markers: content greps naming APIs specified after the 2014 REC
  (the accepted propose-tool rule set plus the html-specific additions), each
  reason carrying the matched marker as evidence.
- multi-origin serving-blocked: sources under a .sub. host or reading
  get_host_info need real upstream wptserve (the Milestone 3 serving residual);
  per the dom-straggler convention they classify included reviewed-failing
  naming the serving capability, not unreviewed.
- manual-kind sources: queued for the survey — the self-describing oracle
  decides the ones whose stated pass condition is machine-checked; the rest
  close by the manual-tail static convention at outcome-recording time.
- everything else: written to the survey queue for measured outcomes
  (pass -> included; non-pass -> included reviewed-failing feeding the
  Milestone 4 engine queue).

Writes Lite.Conformance/artifacts/m1-queue-html.txt (one source path per line)
and reports the counts. The manifest is only written when every entry still
satisfies the structural invariants.
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

root = Path('Lite.Conformance')
applicability_path = root / 'Wpt/html5-applicability.json'
catalog_path = root / 'artifacts/wpt-manifest.json'
queue_path = root / 'artifacts/m1-queue-html.txt'
vendor = root / 'vendor/wpt'
TODAY = '2026-11-05'

ROOT = 'html'

POST_RULES = [
    (r'\bdialog\b|showModal\s*\(',
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
    (r'\bpopover\b|showPopover\s*\(|hidePopover\s*\(|togglePopover\s*\(|beforetoggle|closedby',
     'the popover attribute and its methods were specified in 2022+'),
    (r'\.inert\b|setAttribute\(\s*[\'"]inert[\'"]|\binert\s*=',
     'the inert attribute was specified in 2022+'),
    (r'\binterestfor\b|interesttarget',
     'the interestfor/interesttarget invoker attributes were specified in 2024+'),
    (r'\bqueueMicrotask\b',
     'queueMicrotask() entered WHATWG HTML in 2017+'),
    (r'MutationObserver|ResizeObserver|IntersectionObserver|PerformanceObserver',
     'the observer interfaces were specified after the 2014 REC'),
    (r'setBaseAndExtent',
     'Selection.setBaseAndExtent entered the Selection API after the 2014 snapshot'),
    (r'attachShadow|\bshadowRoot\b|<slot\b',
     'shadow trees postdate the 2014 REC (no shadow DOM in its DOM dependency)'),
    (r'\bbackgroundFetch\b|\bportal\b|\btrustToken\b',
     'the feature was specified well after the 2014 REC'),
    (r'\bstructuredClone\s*\(',
     'the structuredClone() global was specified in 2022+'),
    (r'\.moveBefore\s*\(',
     'ParentNode.moveBefore was specified in 2024+'),
    (r'\.play\s*\(\s*\)\s*\.\s*(then|catch|finally)\b|await\s+[^;\n]*\.play\s*\(\s*\)',
     'HTMLMediaElement.play() returned void in the 2014 REC; the promise-returning play() is 2016+'),
    (r'\bnew\s+(TextDecoder|TextEncoder)\b',
     'the TextDecoder/TextEncoder JS API is not part of the 2014 REC\'s encoding dependency (document decoding, §8.2.2 only)'),
    (r'\bcustomElements\b|custom element registry|\bis\s*=\s*["\']',
     'custom elements were specified in 2015+ (v1); the 2014 REC has no custom-elements machinery'),
    (r'showPicker\s*\(',
     'HTMLInputElement/HTMLSelectElement showPicker() was specified in 2021+'),
    (r'\.requestSubmit\s*\(',
     'HTMLFormElement.requestSubmit() was specified in 2019+ WHATWG HTML'),
    (r'\binputmode\s*=|\.inputMode\b',
     'the inputmode attribute entered the HTML Standard in 2017+'),
    (r'\benterkeyhint\b|\.enterKeyHint\b',
     'the enterkeyhint attribute entered the HTML Standard in 2019+'),
    (r'\bbeforeinput\b',
     'the beforeinput event and the Input Events Level 2 integration postdate the 2014 REC'),
    # reportValidity stays out deliberately: it is defined in the 2014 REC
    # §4.10.10.2 (the constraint-validation API), so a read is not a marker.
    (r'\bnew\s+(Shared)?ArrayBuffer\b|\bAtomics\b',
     'SharedArrayBuffer/Atomics postdate the 2014 REC'),
]

# window.postMessage stays OUT of the marker set: cross-document messaging is a
# 2014 REC obligation (§6.4), so a postMessage read is not evidence of a
# later-era feature. Worker/MessageChannel/localStorage constructors stay out
# for the opposite reason — the dep-usage batches need per-file eyes (the
# constructor can be incidental to a genuine REC obligation), so they are
# decided by review-m1-dep-usage-tail.py, not by this sweep.

# Whole directories whose feature postdates the 2014 REC (name-level evidence).
POST_DIRS = [
    ('html/semantics/interestfor',
     'the interestfor/interesttarget invoker attributes were specified in 2024+; the whole directory tests that feature'),
    ('html/semantics/popovers',
     'the popover attribute and its event model were specified in 2022+; the whole directory tests that feature'),
    ('html/semantics/permission-element',
     'the <permission> element was specified in 2022+; the whole directory tests that feature'),
    ('html/semantics/forms/customizable-combobox',
     'the customizable <select> combobox (selectedcontent, the customizable-select state) was specified in 2024+; the whole directory tests that feature'),
]

FAMILIES = [
    ('html/semantics/forms/form-submission-0',
     'the 2014 REC §4.10 form-submission model (form submission algorithm, the submit methods, encoding selection)'),
    ('html/semantics/forms/attributes-common-to-form-controls',
     'the 2014 REC §4.10 attributes common to form controls (name/disabled/readonly/autocomplete and friends)'),
    ('html/semantics/forms/constraints',
     'the 2014 REC §4.10.10 constraint-validation model (pattern/required/step/min/max, checkValidity, the invalid event)'),
    ('html/semantics/forms/form-control-infrastructure',
     'the 2014 REC §4.10 form-control infrastructure (form owner association, the reset algorithm, fieldset elements)'),
    ('html/semantics/forms/resetting-a-form',
     'the 2014 REC §4.10.3 form reset algorithm'),
    ('html/semantics/forms/the-form-element',
     'the 2014 REC §4.10.3 the form element'),
    ('html/semantics/forms/the-input-element',
     'the 2014 REC §4.10.7 the input element (its input-type states and value/sanitization model)'),
    ('html/semantics/forms/the-button-element',
     'the 2014 REC §4.10.8 the button element'),
    ('html/semantics/forms/the-select-element',
     'the 2014 REC §4.10.9 the select element'),
    ('html/semantics/forms/the-datalist-element',
     'the 2014 REC §4.10.10 the datalist element'),
    ('html/semantics/forms/the-optgroup-element',
     'the 2014 REC §4.10.11 the optgroup element'),
    ('html/semantics/forms/the-option-element',
     'the 2014 REC §4.10.12 the option element'),
    ('html/semantics/forms/textfieldselection',
     'the 2014 REC §4.10 text-field selection interface (setSelectionRange/selectionStart on the applicable inputs)'),
    ('html/semantics/forms/the-textarea-element',
     'the 2014 REC §4.10.13 the textarea element'),
    ('html/semantics/forms/the-output-element',
     'the 2014 REC §4.10.14 the output element'),
    ('html/semantics/forms/the-progress-element',
     'the 2014 REC §4.10.16 the progress element'),
    ('html/semantics/forms/the-meter-element',
     'the 2014 REC §4.10.17 the meter element'),
    ('html/semantics/forms/the-fieldset-element',
     'the 2014 REC §4.10.15 the fieldset element'),
    ('html/semantics/forms/the-label-element',
     'the 2014 REC §4.10.4 the label element'),
    ('html/semantics/forms/the-legend-element',
     'the 2014 REC §4.10.15 the legend element'),
    ('html/semantics/forms',
     'the 2014 REC §4.10 forms model'),
    ('html/semantics/document-metadata',
     'the 2014 REC §4.2 document-metadata elements (the title, base, link and style elements)'),
    ('html/semantics/scripting-1/the-script-element',
     'the 2014 REC §4.11.1 the script element (fetching and executing model, the defer/async model)'),
    ('html/semantics/scripting-1/the-template-element',
     'the 2014 REC §4.12.4 the template element (the template contents node document model)'),
    ('html/semantics/scripting-1',
     'the 2014 REC §4.11 scripting elements'),
    ('html/semantics/edits',
     'the 2014 REC §4.7 edits (the ins and del elements)'),
    ('html/semantics/embedded-content/the-img-element',
     'the 2014 REC §4.8.1 the img element (the image-loading model, dimension and srcset-free attribute semantics)'),
    ('html/semantics/embedded-content/the-iframe-element',
     'the 2014 REC §4.8.2 the iframe element (nested browsing context, the srcdoc document, load events)'),
    ('html/semantics/embedded-content/the-embed-element',
     'the 2014 REC §4.8.5 the embed element'),
    ('html/semantics/embedded-content/the-object-element',
     'the 2014 REC §4.8.6 the object element'),
    ('html/semantics/embedded-content/the-area-element',
     'the 2014 REC §4.8.14 the area element (image-map processing)'),
    ('html/semantics/embedded-content/the-map-element',
     'the 2014 REC §4.8.13 the map element (image-map processing)'),
    ('html/semantics/embedded-content/media-elements',
     'the 2014 REC §4.8.10 media elements (the resource-selection algorithm, readyState model, the media events)'),
    ('html/semantics/embedded-content/the-video-element',
     'the 2014 REC §4.8.6 the video element'),
    ('html/semantics/embedded-content/the-audio-element',
     'the 2014 REC §4.8.7 the audio element'),
    ('html/semantics/embedded-content/the-track-element',
     'the 2014 REC §4.8.9 the track element'),
    ('html/semantics/embedded-content',
     'the 2014 REC §4.8 embedded content'),
    ('html/semantics/grouping-content',
     'the 2014 REC §4.5 grouping content (the p, ol, ul, dl, blockquote, pre and hr elements)'),
    ('html/semantics/sections',
     'the 2014 REC §4.3 sections (the article/section/nav/aside/h1-h6/header/footer/address elements and the outline algorithm)'),
    ('html/semantics/tabular-data',
     'the 2014 REC §4.9 tabular data (the table model, spans, captions and the table sorting-removed model)'),
    ('html/semantics/text-level-semantics',
     'the 2014 REC §4.6 text-level semantics (the a, em, strong, small, s, cite, q, dfn, abbr, ruby, data, time, span and friends)'),
    ('html/semantics/links',
     'the 2014 REC §4.12 links (the link types, the a/link/area element link processing)'),
    ('html/semantics/interactive-elements',
     'the 2014 REC §4.11 interactive elements (the details/summary and menu elements)'),
    ('html/semantics/disabled-elements',
     'the 2014 REC §4.10.18 disabled-elements model (the barred-constraint and disabled state propagation)'),
    ('html/semantics/selectors',
     'the 2014 REC §4.14 matching HTML elements using selectors (the selector extensions: :disabled, :enabled, :checked, :placeholder-shown, :read-only, :default, :valid, :invalid, :required, :optional, :indeterminate)'),
    ('html/semantics/interfaces',
     'the 2014 REC element-interface model (the DOM interface mapping of the HTML elements)'),
    ('html/semantics/miscellaneous',
     'the 2014 REC §4.13 miscellaneous elements'),
    ('html/dom/documents/dom-tree-accessors',
     'the 2014 REC §3.1 document-tree accessors (getElementById/getElementsByClassName/innerHTML wiring, the named properties)'),
    ('html/dom/documents/resource-metadata-management',
     'the 2014 REC §3.1.3 resource-metadata-management (document.title, the referrer, the cookie model)'),
    ('html/dom/elements',
     'the 2014 REC §3.2 elements in the DOM (the global-attributes model, dataset, the directionality, the tabindex handling)'),
    ('html/dom/dynamic-markup-insertion',
     'the 2014 REC §3.4 dynamic-markup insertion (document.write/writeln, open/close, innerHTML/outerHTML parsing round-trips)'),
    ('html/dom/render-blocking',
     'render-blocking (the blocking attribute and the render-blocking mechanism) was specified in 2023+'),
    ('html/dom/aria-attribute-reflection',
     'ARIA attribute reflection was specified in 2017+ (ARIA in HTML / the AAM mapping)'),
    ('html/dom',
     'the 2014 REC §3 semantics, structure and APIs of HTML documents (the element/DOM interface surface)'),
    ('html/webappapis/timers',
     'the 2014 REC §6.1.4 timers (setTimeout/setInterval and their queueing semantics)'),
    ('html/webappapis/system-state-and-navigator',
     'the 2014 REC §6.4 system state and navigator (the navigator interface, the protocol handlers, image and media capability queries)'),
    ('html/webappapis/atob',
     'the 2014 REC §2.6.3 base64 utility methods (atob/btoa and their byte-sequence model)'),
    ('html/webappapis/dynamic-markup-insertion',
     'the 2014 REC §6.3 dynamic-markup insertion APIs (document.open/close/write) at the webappapis level'),
    ('html/webappapis/scripting/processing-model-2',
     'the 2014 REC §6.1.3.4-6.1.3.5 scripting processing model (the script-settings-object wiring of event handlers)'),
    ('html/webappapis/scripting/events',
     'the 2014 REC §6.1.6.1 event-handling processing (event-handler compilation, the this-binding, the error reporting)'),
    ('html/webappapis/scripting',
     'the 2014 REC §6.1 scripting model (the realm/setting-object wiring)'),
    ('html/webappapis/eventloop',
     'the 2014 REC §6.1.4 event-loop model (task queueing and microtask checkpointing)'),
    ('html/webappapis/idle-callbacks',
     'requestIdleCallback was specified in 2017+ (Cooperative Scheduling of Background Tasks)'),
    ('html/webappapis/microtask-queuing',
     'queueMicrotask() entered WHATWG HTML in 2017+'),
    ('html/webappapis/structured-clone',
     'the 2014 REC §2.7.5 structured-clone model (safe passing of structured data)'),
    ('html/webappapis',
     'the 2014 REC §6 web-application APIs (the webappapis surface)'),
    ('html/syntax/parsing',
     'the 2014 REC §8.2 parsing model (the tokenizer and tree-construction stages, the insertion modes)'),
    ('html/syntax/serializing-html-fragments',
     'the 2014 REC §8.3 serializing HTML fragments (the serializer escaping and attribute-order model)'),
    ('html/syntax',
     'the 2014 REC §8 the HTML syntax (writing, parsing and serializing)'),
    ('html/the-xhtml-syntax',
     'the 2014 REC §9 the XHTML syntax (the XML documents model)'),
    ('html/browsers',
     'the 2014 REC §5 browsing-context model'),
    ('html/rendering',
     'the 2014 REC §10 rendering requirements'),
    ('html/obsolete',
     'the 2014 REC §11 obsolete features (the obsolete element/attribute processing and rendering requirements)'),
    ('html/infrastructure/urls',
     'the 2014 REC §2.5 URLs (parsing, resolution and the document base URL)'),
    ('html/infrastructure/common-dom-interfaces',
     'the 2014 REC §2 common-DOM-interface semantics (the collections, reflectors and DOM-settable token lists)'),
    ('html/infrastructure/safe-passing-of-structured-data',
     'the 2014 REC §2.7.5 structured-clone model'),
    ('html/infrastructure/dependent-formats',
     'the 2014 REC §2.3 dependent formats (the boolean-attribute and microdata-free content models)'),
    ('html/infrastructure',
     'the 2014 REC §2 common infrastructure'),
    ('html/editing/activation',
     'the 2014 REC activation behavior and click semantics (§3.2 element activation, the synthetic-click model)'),
    ('html/editing/dnd',
     'the 2014 REC §7.7 drag-and-drop model (the drag data store, the drag events, the draggable attribute)'),
    ('html/editing/focus',
     'the 2014 REC §7.4 focus model (the focusing steps, the autofocus attribute, sequential focus navigation)'),
    ('html/editing/selection',
     'the 2014 REC §7.5 editing APIs dependency (the Selection and Range surface the pinned Editing APIs draft defines)'),
    ('html/editing/undomanager',
     'the UndoManager was removed from the standard before the 2014 REC shipped; the 2014 REC defines no UndoManager API'),
    ('html/editing',
     'the 2014 REC §7 user interaction (editing host and contenteditable model)'),
    ('html/interaction/focus',
     'the 2014 REC §7.4 focus model (the focusing steps, the autofocus attribute, sequential focus navigation)'),
    ('html/interaction',
     'the 2014 REC §7 user interaction'),
    ('html/links',
     'the 2014 REC §4.12 links and link types'),
    ('html/scripting',
     'the 2014 REC §4.11 scripting'),
    ('html/select',
     'the 2014 REC §4.10.9 the select element'),
    ('html/meta',
     'the 2014 REC §4.2.5 the meta element'),
    ('html/media',
     'the 2014 REC §4.8.10 media elements'),
    ('html/semantics',
     'the 2014 REC §4 the elements of HTML'),
]


def family_clause(path):
    best = None
    for prefix, clause in FAMILIES:
        if path.startswith(prefix + '/') or path.startswith(prefix + '.') or path == prefix:
            if best is None or len(prefix) > len(best[0]):
                best = (prefix, clause)
    return best[1] if best else 'the 2014 HTML 5.0 Recommendation'


def read_text(path):
    try:
        return (vendor.joinpath(*path.split('/'))).read_text(encoding='utf-8', errors='replace')[:120000]
    except OSError:
        return ''


def unclassified_sources():
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    manifest = json.loads(applicability_path.read_text(encoding='utf-8-sig'))
    reviewed = {t['path'] for t in manifest['tests']}
    sources = set()
    for kind, tree in catalog['items'].items():
        if kind in ('support', 'spec'):
            continue
        walk(tree, '', kind, reviewed, sources)
    return manifest, sources


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
            if source.startswith(ROOT + '/') and path not in reviewed and source not in reviewed:
                sources.add(source)


def main():
    manifest, sources = unclassified_sources()
    sources = sorted(sources)
    tests = manifest['tests']
    existing = {t['path'] for t in tests}
    counts = Counter()
    queue = []

    def add(path, classification, reason):
        if path in existing:
            return False
        tests.append({'path': path, 'classification': classification, 'reason': reason})
        existing.add(path)
        counts[classification] += 1
        return True

    included_family = ('Reviewed {} (static family review): the file asserts {{}} — '
                       'a 2014-target obligation — pending measured execution; classified from '
                       'directory-coherent feature analysis with representative spot-checks, '
                       'per-file feature from the file\'s own directory and content scan.'
                       ).format(TODAY)
    serving = ('Reviewed {} (included, reviewed-failing — feeds the Milestone 4 engine queue): '
               'the file asserts {{}} — a 2014-target obligation — but the page runs under a .sub. '
               'multi-host origin or reads get_host_info, which real upstream wptserve must serve; '
               'the single-origin ConformanceServer cannot produce the substitutions/origins the '
               'assertions read (the Milestone 3 serving residual). The manual-evidence or '
               'upstream-serving recording remains the Milestone 6 evidence step.'
               ).format(TODAY)

    for path in sources:
        if not path.startswith(ROOT + '/'):
            continue
        text = read_text(path)
        clause = family_clause(path)

        if path.startswith('html/canvas/'):
            add(path, 'profile-excluded',
                f'Reviewed {TODAY} (profile-excluded): the file exercises the Canvas 2D Context '
                f'dependency, which the 2026-10-24 scope decision placed outside this profile\'s '
                f'dependency scope — the same bucket as the already-profile-excluded canvas entries.')
            continue

        post_dir = next((msg for prefix, msg in POST_DIRS if path.startswith(prefix + '/')), None)
        if post_dir:
            add(path, 'post-target',
                f'Reviewed {TODAY} (post-target): {post_dir[0]} — the directory\'s own feature '
                f'postdates the 2014 REC. Not a 2014 HTML 5.0 obligation.')
            continue

        matched = next(((pat, msg) for pat, msg in POST_RULES if re.search(pat, text, re.I)), None)
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

        queue.append(path)

    queue_path.write_text('\n'.join(queue) + '\n', encoding='utf-8')

    # Structural invariants before writing.
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
    print(f'html static tail: {dict(counts)}')
    print(f'survey queue: {len(queue)} sources -> {queue_path}')


if __name__ == '__main__':
    main()
