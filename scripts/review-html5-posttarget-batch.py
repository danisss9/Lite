#!/usr/bin/env python3
"""One-shot per batch: record the HTML5 plan Milestone 1 quick post-target
bulk classifications in Wpt/html5-applicability.json, or the Workstream 0
scope-decided `profile-excluded` classifications with --classification
profile-excluded.

Every rule below classifies catalog sources (the file-level `source` path,
matching the manifest's Expand behaviour) whose assertions only bind behavior
introduced after the pinned 28 October 2014 HTML 5.0 Recommendation (default
mode), or whose obligations the 2026-10-24 scope decision excluded from the
profile (--classification profile-excluded), each with a one-line normative
justification. Existing entries always win: this script never rewrites,
overrides or deletes a review.

Sanity gates before any write:
- the inventory artifact's manifestSha256 must match the current WPT manifest,
  so the classified set is derived from the catalog this review is about;
- a rule that matches zero catalog sources is an error (no silent no-ops);
- the result must satisfy the applicability schema's structural invariants
  (classification enum, non-empty reasons, assertion fields only on mixed).
"""
import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

REVIEW_DATE = '2026-10-15'
SCOPE_DECISION_DATE = '2026-10-24'
inventory_artifact = Path('Lite.Conformance/artifacts/html5-inventory.json')
manifest_path = Path('Lite.Conformance/artifacts/wpt-manifest.json')
applicability_path = Path('Lite.Conformance/Wpt/html5-applicability.json')
vendor_wpt = Path('Lite.Conformance/vendor/wpt')

# Whole directory trees whose every catalog file tests a post-2014 feature.
PREFIX_RULES = [
    ('shadow-dom/',
     'Reviewed 2026-10-15 (post-target): the pinned REC has no shadow-tree concept — attachShadow, <slot> and shadow-root interfaces come from the separate Shadow DOM specification (v1, 2016+) integrated into the DOM and HTML standards after 28 October 2014.'),
    ('custom-elements/',
     'Reviewed 2026-10-15 (post-target): the customElements registry (define/upgrade, observedAttributes, custom-element reactions) was specified after the 2014 REC, which defines no custom-element machinery.'),
    ('html/anonymous-iframe/',
     'Reviewed 2026-10-15 (post-target): anonymous iframes (opaque partitioned origins for embedded content) were specified in 2023; the 2014 REC defines no anonymous-iframe origin mode.'),
    ('html/cross-origin-embedder-policy/',
     'Reviewed 2026-10-15 (post-target): Cross-Origin-Embedder-Policy and Cross-Origin-Resource-Policy enforcement entered Fetch/HTML in 2018+; the 2014 REC\'s fetching dependency (§2.6) has no embedder-policy step.'),
    ('html/cross-origin-opener-policy/',
     'Reviewed 2026-10-15 (post-target): Cross-Origin-Opener-Policy browsing-context-group isolation was specified in 2018+; the 2014 REC\'s opener model (§5.2, §7.4) has no COOP concept.'),
    ('html/document-isolation-policy/',
     'Reviewed 2026-10-15 (post-target): Document-Isolation-Policy was specified in 2024; the 2014 REC has no isolation pragma.'),
    ('html/user-activation/',
     'Reviewed 2026-10-15 (post-target): the User Activation API (navigator.userActivation, transient-activation propagation and expiry) was specified in 2018+; the 2014 REC\'s activation notion (§6.5 user interaction with the prompt APIs) exposes no activation state to script.'),
    ('html/capability-delegation/',
     'Reviewed 2026-10-15 (post-target): capability delegation (forwarding transient activation through postMessage) was specified in 2021.'),
    ('html/browsers/origin/cross-origin-objects/',
     'Reviewed 2026-10-15 (post-target): the cross-origin objects integration tested here (immutable [[Prototype]], own-property whitelist enumeration, cross-origin function wrappers) was specified after the 2014 REC; its §5.2.3 WindowProxy exotic object and §5.3 cross-origin member lists define a different model.'),
    ('html/browsers/origin/origin-keyed-agent-clusters/',
     'Reviewed 2026-10-15 (post-target): origin-keyed agent clusters (Origin-Agent-Cluster header, originAgentCluster) were specified in 2021; the 2014 REC defines no agent clusters.'),
    ('html/browsers/origin/api/',
     'Reviewed 2026-10-15 (post-target): the assertions read the Origin API (window/worker/self origin attribute, HyperlinkElementUtils origin), added to HTML in 2015+; the 2014 REC §5.3 defines origin comparison but no script-visible origin attribute.'),
    ('html/browsers/browsing-the-web/history-traversal/pagereveal/',
     'Reviewed 2026-10-15 (post-target): the PageReveal event and its ordering guarantees were specified in 2023+, absent from the 2014 REC\'s event model.'),
    ('html/browsers/browsing-the-web/history-traversal/pageswap/',
     'Reviewed 2026-10-15 (post-target): the PageSwap event and its navigation-timing guarantees were specified in 2023+, absent from the 2014 REC\'s event model.'),
    ('html/browsers/windows/consume-user-activation/',
     'Reviewed 2026-10-15 (post-target): user-activation consumption by window.open() was specified in 2018+; the 2014 REC has no transient activation to consume.'),
    ('html/canvas/offscreen/',
     'Reviewed 2026-10-15 (post-target): OffscreenCanvas and OffscreenCanvasRenderingContext2D were added to HTML after the 2014 REC, which defines only the document-attached canvas (§4.11.4).'),
    ('html/canvas/element/wide-gamut-canvas/',
     'Reviewed 2026-10-15 (post-target): canvas color-space selection (colorSpace, display-p3) post-dates the 2014 REC\'s Canvas 2D Context dependency.'),
    ('html/canvas/element/color-type/',
     'Reviewed 2026-10-15 (post-target): the canvas colorType/color-space API post-dates the 2014 REC\'s Canvas 2D Context dependency.'),
    ('html/canvas/element/filters/',
     'Reviewed 2026-10-15 (post-target): the context filter attribute comes from the CSS Filter Effects integration added to canvas after the 2014 snapshot; the Canvas 2D Context the 2014 REC references defines no filter member.'),
    ('html/canvas/element/global-hdr-headroom/',
     'Reviewed 2026-10-15 (post-target): canvas HDR headroom rendering was specified in 2023+, after the 2014 REC.'),
    ('html/canvas/element/reset/',
     'Reviewed 2026-10-15 (post-target): CanvasRenderingContext2D.reset() was added to the HTML standard in 2020; the 2014 REC\'s 2D context dependency resets state only through canvas resize (§4.11.4).'),
    ('html/canvas/element/layers/',
     'Reviewed 2026-10-15 (post-target): the canvas layers API (beginLayer/endLayer, layer filters) was added to the HTML standard in 2022+, absent from the 2014 REC.'),
    ('html/canvas/element/manual/draw-element-image/privacy/',
     'Reviewed 2026-10-15 (post-target): drawImage-from-element privacy redaction (iframes, cross-origin frames, filters, selection and visited-link painting) was specified in 2023+; the 2014 REC\'s drawImage accepts only canvas, img and video sources with no redaction model.'),
    ('html/semantics/interestfor/',
     'Reviewed 2026-10-15 (post-target): the interestfor attribute and interest events were specified in 2023+, absent from the 2014 REC.'),
    ('html/semantics/popovers/',
     'Reviewed 2026-10-15 (post-target): the popover API (popover attribute, showPopover/hidePopover/togglePopover, beforetoggle) was specified in 2022+, absent from the 2014 REC.'),
    ('html/semantics/permission-element/',
     'Reviewed 2026-10-15 (post-target): the <permission> element was specified in 2022+, absent from the 2014 REC\'s interactive elements.'),
    ('html/dom/render-blocking/',
     'Reviewed 2026-10-15 (post-target): blocking=render render-blocking documents were specified in 2023; the 2014 REC (§4.2.4) blocks rendering through stylesheets alone, with no blocking attribute.'),
    ('html/links/manifest/',
     'Reviewed 2026-10-15 (post-target): <link rel=manifest> is the Web App Manifest specification (2015+), not a 2014 REC link type (§4.8.2).'),
]

# Individual sources or filename families that test one post-2014 feature.
EXACT_RULES = [
    ('html/canvas/element/text/2d.text.drawing.style.letterSpacing.change.font.html',
     'Reviewed 2026-10-15 (post-target): ctx.letterSpacing was added to canvas text rendering in 2018+; the Canvas 2D Context the 2014 REC references has no spacing attributes.'),
    ('html/canvas/element/text/2d.text.drawing.style.letterSpacing.measure.html',
     'Reviewed 2026-10-15 (post-target): ctx.letterSpacing was added to canvas text rendering in 2018+; the Canvas 2D Context the 2014 REC references has no spacing attributes.'),
    ('html/canvas/element/text/2d.text.drawing.style.wordSpacing.change.font.html',
     'Reviewed 2026-10-15 (post-target): ctx.wordSpacing was added to canvas text rendering in 2018+; the Canvas 2D Context the 2014 REC references has no spacing attributes.'),
    ('html/canvas/element/text/2d.text.drawing.style.wordSpacing.measure.html',
     'Reviewed 2026-10-15 (post-target): ctx.wordSpacing was added to canvas text rendering in 2018+; the Canvas 2D Context the 2014 REC references has no spacing attributes.'),
    ('html/canvas/canvas-css-random.html',
     'Reviewed 2026-10-15 (post-target): painting with CSS random-scheme function values (random()) was specified in 2023+.'),
    ('html/canvas/canvas-sibling-index-crash.html',
     'Reviewed 2026-10-15 (post-target): exercises the CSS sibling-index()/sibling-count() functions specified in 2023+.'),
    ('html/canvas/text-metrics-for-unpaintable-crash.html',
     'Reviewed 2026-10-15 (post-target): asserts post-2014 TextMetrics behavior for unpaintable canvases (the TextMetrics advance/alignment members post-date the 2014 snapshot).'),
    ('html/canvas/color.window.js',
     'Reviewed 2026-10-15 (post-target): the canvas colorSpace/colorType API post-dates the 2014 REC\'s Canvas 2D Context dependency.'),
    ('html/canvas/historical.any.js',
     'Reviewed 2026-10-15 (post-target): asserts the absence of members removed after the 2014 snapshot (scrollPathIntoView was part of the Canvas 2D Context specification the 2014 REC references).'),
    ('html/canvas/historical.window.js',
     'Reviewed 2026-10-15 (post-target): asserts the absence of members removed after the 2014 snapshot (scrollPathIntoView was part of the Canvas 2D Context specification the 2014 REC references).'),
    ('html/dom/historical.html',
     'Reviewed 2026-10-15 (post-target): asserts the post-2014 removal state — document.applets always empty, while the 2014 REC still defines applet elements (§4.7.5, §11 obsolete processing) and their document collections — plus layer/nolayer interface checks.'),
    ('html/dom/aria-attribute-reflection-enumerated.tentative.html',
     'Reviewed 2026-10-15 (post-target): aria-* IDL attribute reflection was specified after the 2014 REC (WAI-ARIA 1.2 era), which defines no ARIA attribute bindings.'),
    ('html/dom/aria-attribute-reflection.html',
     'Reviewed 2026-10-15 (post-target): aria-* IDL attribute reflection was specified after the 2014 REC (WAI-ARIA 1.2 era), which defines no ARIA attribute bindings.'),
    ('html/dom/aria-attribute-reflection.tentative.html',
     'Reviewed 2026-10-15 (post-target): aria-* IDL attribute reflection was specified after the 2014 REC (WAI-ARIA 1.2 era), which defines no ARIA attribute bindings.'),
    ('html/dom/aria-element-reflection-disconnected.html',
     'Reviewed 2026-10-15 (post-target): ARIA element reflection (ElementReflectionInterface referenced-element IDs) was specified in 2021+, absent from the 2014 REC.'),
    ('html/dom/aria-element-reflection-labelledby.html',
     'Reviewed 2026-10-15 (post-target): ARIA element reflection (ElementReflectionInterface referenced-element IDs) was specified in 2021+, absent from the 2014 REC.'),
    ('html/dom/aria-element-reflection.html',
     'Reviewed 2026-10-15 (post-target): ARIA element reflection (ElementReflectionInterface referenced-element IDs) was specified in 2021+, absent from the 2014 REC.'),
    ('html/browsers/browsing-the-web/navigating-across-documents/cross-origin-top-navigation-with-user-activation-in-parent.window.js',
     'Reviewed 2026-10-15 (post-target): activation-gated cross-origin top-level navigation (sandboxed-top-navigation-without-user-activation flag) was added to the sandbox model after the 2014 REC, whose §5.4 sandboxed top-level navigation flag allows navigation without activation.'),
    ('html/browsers/browsing-the-web/navigating-across-documents/cross-origin-top-navigation-with-user-activation.window.js',
     'Reviewed 2026-10-15 (post-target): activation-gated cross-origin top-level navigation (sandboxed-top-navigation-without-user-activation flag) was added to the sandbox model after the 2014 REC, whose §5.4 sandboxed top-level navigation flag allows navigation without activation.'),
    ('html/browsers/browsing-the-web/navigating-across-documents/cross-origin-top-navigation-without-user-activation-nested.window.js',
     'Reviewed 2026-10-15 (post-target): activation-gated cross-origin top-level navigation (sandboxed-top-navigation-without-user-activation flag) was added to the sandbox model after the 2014 REC, whose §5.4 sandboxed top-level navigation flag allows navigation without activation.'),
    ('html/browsers/browsing-the-web/navigating-across-documents/cross-origin-top-navigation-without-user-activation.window.js',
     'Reviewed 2026-10-15 (post-target): activation-gated cross-origin top-level navigation (sandboxed-top-navigation-without-user-activation flag) was added to the sandbox model after the 2014 REC, whose §5.4 sandboxed top-level navigation flag allows navigation without activation.'),
    ('html/browsers/browsing-the-web/navigating-across-documents/same-origin-top-navigation-without-user-activation.window.js',
     'Reviewed 2026-10-15 (post-target): the sandboxed-top-navigation-without-user-activation flag (governing same-origin top navigation too) was added to the sandbox model after the 2014 REC, whose §5.4 sandboxed top-level navigation flag allows navigation without activation.'),
]

# ---------------------------------------------------------------------------
# Workstream 0 scope rules (--classification profile-excluded): the 2026-10-24
# scope decision removed four dependency specifications from the profile.
# Canvas element tests (getContext rules, toDataURL, origin-clean, fallback,
# sizing) stay for review: they are kept out of these rules by path and by the
# 2D-context usage grep, and remain visible in the kept-file report.
# ---------------------------------------------------------------------------

CANVAS_2D_REASON = (
    'Reviewed 2026-10-24 (profile-excluded): this file asserts the Canvas 2D Context drawing API, '
    'the specification that REC §4.11.4 references for its rendering context and that the 2026-10-24 '
    'scope decision places outside this profile\'s dependency scope. The §4.11.4 canvas element clause '
    '(getContext rules, toDataURL, the origin-clean flag, fallback content, width/height sizing) remains '
    'required and keeps its own review in html/semantics/embedded-content/the-canvas-element/ and in '
    'html/canvas/element/canvas-context/ and canvas-host/, which these rules never classify.')

WEBSTORAGE_EXCLUDED_REASON = (
    'Reviewed 2026-10-24 (profile-excluded): Web Storage ([WEBSTORAGE], localStorage/sessionStorage) is '
    'outside this profile\'s dependency scope per the 2026-10-24 scope decision; the existing JsStorage '
    'stays as an extension and never counts as HTML 5.0 coverage.')

WEBMESSAGING_EXCLUDED_REASON = (
    'Reviewed 2026-10-24 (profile-excluded): Web Messaging ([WEBMSG], cross-document, channel and broadcast '
    'messaging) is outside this profile\'s dependency scope per the 2026-10-24 scope decision; the existing '
    'postMessage/JsMessageChannel implementations stay as extensions and never count as HTML 5.0 coverage.')

WORKER_EXCLUDED_REASON = (
    'Reviewed 2026-10-24 (profile-excluded): this variant executes in a worker global context, and the Web '
    'Workers specification ([WEBWORKERS]) is outside this profile\'s dependency scope per the 2026-10-24 '
    'scope decision; the existing JsWorker stays as an extension, and the window-context files of the same '
    'feature carry the review this profile counts.')

EXCLUDED_PREFIX_RULES = [
    # Whole dependency roots the scope decision removed.
    ('webstorage/', WEBSTORAGE_EXCLUDED_REASON),
    ('webmessaging/', WEBMESSAGING_EXCLUDED_REASON),
    # Canvas 2D Context spec chapters under html/canvas/element/ (the element
    # clause dirs canvas-context/ and canvas-host/ are deliberately absent).
    ('html/canvas/element/compositing/', CANVAS_2D_REASON),
    ('html/canvas/element/drawing-images-to-the-canvas/', CANVAS_2D_REASON),
    ('html/canvas/element/drawing-rectangles-to-the-canvas/', CANVAS_2D_REASON),
    ('html/canvas/element/fill-and-stroke-styles/', CANVAS_2D_REASON),
    ('html/canvas/element/line-styles/', CANVAS_2D_REASON),
    ('html/canvas/element/path-objects/', CANVAS_2D_REASON),
    ('html/canvas/element/pixel-manipulation/', CANVAS_2D_REASON),
    ('html/canvas/element/shadows/', CANVAS_2D_REASON),
    ('html/canvas/element/text/', CANVAS_2D_REASON),
    ('html/canvas/element/the-canvas-state/', CANVAS_2D_REASON),
    ('html/canvas/element/transformations/', CANVAS_2D_REASON),
    ('html/canvas/element/video/', CANVAS_2D_REASON),
    ('html/canvas/element/conformance-requirements/', CANVAS_2D_REASON),
    # The same 2D-conformance-chapter files duplicated at the element root.
    ('html/canvas/element/2d.conformance.requirements.', CANVAS_2D_REASON),
]

# Sources for worker-only contexts in any root: the scope decision excluded the
# Workers dependency, and these variants cannot carry window-context review.
WORKER_SUFFIXES = ('.worker.js', '.sharedworker.js', '.serviceworker.js')

# Canvas element-clause and non-2D-feature paths the grep rule must never
# classify; they stay unreviewed for the section review to split.
CANVAS_KEPT_PREFIXES = [
    'html/canvas/element/canvas-context/',        # getContext rules (§4.11.4)
    'html/canvas/element/canvas-host/',           # element sizing and reset-on-resize (§4.11.4)
    'html/canvas/element/manual/context-attributes/',  # getContext attributes
    'html/canvas/element/manual/imagebitmap/',    # createImageBitmap is post-target, not 2D scope
    'html/canvas/element/manual/wide-gamut-canvas/',   # post-target feature dir
    'html/canvas/element/manual/filters/',        # post-target feature dir
    'html/canvas/element/manual/layers/',         # post-target feature dir
    'html/canvas/element/manual/draw-element-image/hit-test/',  # html-in-canvas proposal
]

# Filename markers of §4.11.4 element-clause assertions (the origin-clean flag's
# taint model, toDataURL, fallback content) that keep their review even inside
# 2D-chapter directories.
CANVAS_KEPT_NAME_PARTS = ('taint', 'crossorigin', 'cross-origin', 'todataurl', 'fallback', 'security')

# A file uses the 2D context beyond setup when it creates a '2d' context and,
# with the creation calls removed, still touches a CanvasRenderingContext2D
# member. Files whose harness performs the creation (canvas-tests.js hands the
# test a ready ctx) count the member use directly. Creation-only or
# element-clause files stay for review.
CONTEXT_CREATE = re.compile(r"""getContext\s*\(\s*['"]2d['"]""")
CONTEXT_HARNESS = re.compile(r"""canvas-tests\.js|_addTest\s*\(\s*function\s*\(\s*\w+\s*,\s*ctx\b""")
CONTEXT_MEMBER = re.compile(
    r"""\.\s*(?:fillStyle|strokeStyle|fillRect|strokeRect|clearRect|beginPath|closePath|moveTo|lineTo|"""
    r"""bezierCurveTo|quadraticCurveTo|arcTo|arc|rect|fill|stroke|clip|drawImage|createImageData|"""
    r"""getImageData|putImageData|createLinearGradient|createRadialGradient|createPattern|save|restore|"""
    r"""scale|rotate|translate|transform|setTransform|resetTransform|isPointInPath|isPointInStroke|"""
    r"""fillText|strokeText|measureText|setLineDash|getLineDash|lineDashOffset|addColorStop|"""
    r"""shadowBlur|shadowColor|shadowOffsetX|shadowOffsetY|globalAlpha|globalCompositeOperation|"""
    r"""lineCap|lineJoin|miterLimit|lineWidth|font|textAlign|textBaseline|direction|filter|"""
    r"""imageSmoothingEnabled|imageSmoothingQuality|letterSpacing|wordSpacing|fontKerning|fontStretch|"""
    r"""fontVariantCaps|textRendering)\b""")


def uses_2d_context(text: str) -> bool:
    creates = CONTEXT_CREATE.search(text) is not None
    harness = CONTEXT_HARNESS.search(text) is not None
    if not (creates or harness):
        return False
    body = CONTEXT_CREATE.sub('', text)
    return CONTEXT_MEMBER.search(body) is not None


def canvas_uses_2d_context(source: str) -> bool:
    file = vendor_wpt.joinpath(*source.split('/'))
    try:
        return uses_2d_context(file.read_text(encoding='utf-8', errors='replace'))
    except OSError:
        return False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--classification', choices=('post-target', 'profile-excluded'), default='post-target',
                        help='post-target: Milestone 1 later-feature rules. '
                             'profile-excluded: Workstream 0 scope-decided dependency exclusions.')
    args = parser.parse_args()
    if not inventory_artifact.exists():
        print(f'Missing inventory artifact {inventory_artifact}; run the html5-inventory suite first.')
        return 2
    inventory = json.loads(inventory_artifact.read_text(encoding='utf-8'))
    if inventory.get('formatVersion') != 1:
        print('Unexpected inventory formatVersion.')
        return 2
    manifest_hash = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
    if inventory.get('manifestSha256') != manifest_hash:
        print('Inventory artifact is stale versus the current WPT manifest; run the html5-inventory suite to regenerate it.')
        return 2

    sources = sorted({t['source'] for t in inventory['tests']})
    applicability = json.loads(applicability_path.read_text(encoding='utf-8'))
    existing = {t['path'] for t in applicability['tests']}

    if args.classification == 'profile-excluded':
        result = excluded_additions(sources, existing)
        if result is None:
            return 2
        additions, matched, kept = result
    else:
        result = posttarget_additions(sources, existing)
        if result is None:
            return 2
        additions, matched = result
        kept = []

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
    label = 'profile-excluded' if args.classification == 'profile-excluded' else 'post-target'
    print(f'Added {len(additions)} {label} reviews ({len(matched)} sources matched, '
          f'{len(existing)} existing entries preserved).')
    if kept:
        print(f'Kept {len(kept)} canvas element-clause sources for review (spot-check this list by hand):')
        for path in kept:
            print(f'  KEPT  {path}')
    print(f'Manifest now holds {len(applicability["tests"])} entries: {counts}')
    return 0


def posttarget_additions(sources, existing):
    additions = []
    matched = set()
    for source in sources:
        if source in existing:
            continue
        reason = None
        for prefix, prefix_reason in PREFIX_RULES:
            if source.startswith(prefix):
                reason = prefix_reason
                break
        if reason is None:
            for exact, exact_reason in EXACT_RULES:
                if source == exact:
                    reason = exact_reason
                    break
        if reason is not None:
            matched.add(source)
            additions.append({'path': source, 'classification': 'post-target', 'reason': reason})

    for prefix, _ in PREFIX_RULES:
        if not any(s.startswith(prefix) for s in sources):
            print(f'ERROR: prefix rule matched no catalog source: {prefix}')
            return None
    unmatched_exact = [e for e, _ in EXACT_RULES if e not in matched and e not in existing]
    if unmatched_exact:
        for e in unmatched_exact:
            print(f'ERROR: exact rule matched no catalog source: {e}')
        return None
    return additions, matched


def excluded_additions(sources, existing):
    additions = []
    matched = set()
    kept = []
    for source in sources:
        if source in existing:
            continue
        reason = None
        if source.startswith('html/canvas/'):
            # `.tentative.` marks a not-yet-standardized feature upstream: the
            # post-target sweep owns those files, not this scope exclusion.
            name = source.rsplit('/', 1)[1]
            if (any(source.startswith(p) for p in CANVAS_KEPT_PREFIXES)
                    or '.tentative.' in source
                    # origin-clean/taint model, toDataURL and fallback content are
                    # §4.11.4 element-clause obligations the scope decision keeps.
                    or any(part in name.lower() for part in CANVAS_KEPT_NAME_PARTS)):
                kept.append(source)
                continue
        for prefix, prefix_reason in EXCLUDED_PREFIX_RULES:
            if source.startswith(prefix):
                reason = prefix_reason
                break
        if reason is None and source.endswith(WORKER_SUFFIXES):
            reason = WORKER_EXCLUDED_REASON
        if reason is None and source.startswith('html/canvas/'):
            if canvas_uses_2d_context(source):
                reason = CANVAS_2D_REASON
            else:
                kept.append(source)
        if reason is not None:
            matched.add(source)
            additions.append({'path': source, 'classification': 'profile-excluded', 'reason': reason})

    for prefix, _ in EXCLUDED_PREFIX_RULES:
        if not any(s.startswith(prefix) for s in sources):
            print(f'ERROR: excluded prefix rule matched no catalog source: {prefix}')
            return None
    if not any(s.endswith(WORKER_SUFFIXES) for s in sources):
        print('ERROR: worker-variant rule matched no catalog source.')
        return None
    return additions, matched, kept


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
        if t.get('classification') == 'mixed' and not t.get('assertions'):
            errors.append(f'mixed without assertions {path}')
    return errors


if __name__ == '__main__':
    sys.exit(main())
