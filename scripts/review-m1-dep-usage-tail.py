#!/usr/bin/env python3
"""M1 inventory completion: the propose tool's low-confidence dependency-usage
batches (dep-workers-usage, dep-web-messaging-usage, dep-web-storage-usage) get
the per-file eyes their rules demand — the constructor match can be incidental
to a genuine REC obligation.

Per the propose tool's warning, a file is profile-excluded ONLY where its
assertions bind the excluded dependency API itself ([WEBSTORAGE],
[WEBWORKERS], [WEBMSG] — the 2026-10-24 scope decision; the same bucket as the
webmessaging/ root entries); files that merely use the dependency as
infrastructure (a fetch test running through a Worker, a sandbox test whose
observable is a storage read) stay applicable and return to the survey queue.
Where the infrastructure use cannot execute at all in this engine, the survey
records the measured outcome and the reason names the dependency-execution
gap; where the feature the file binds itself postdates the 2014 REC, the file
is post-target with that feature named.

This script also corrects the 2026-11-05 static sweeps' blanket dep-marker
classifications on exactly this batch (matched by path + today's reason prefix
— no prior review is touched), and rewrites the survey queue files with the
files it returns to the queue removed.
"""
import json
import sys
from collections import Counter
from pathlib import Path

root = Path('Lite.Conformance')
applicability_path = root / 'Wpt/html5-applicability.json'
catalog_path = root / 'artifacts/wpt-manifest.json'
proposals_path = root / 'artifacts/html5-applicability-proposals.json'
TODAY = '2026-11-05'

WEBMSG = ('Reviewed {} (profile-excluded): the file\'s assertions bind the Web Messaging '
          'dependency itself ([WEBMSG] — MessageEvent/MessageChannel/port messaging), which '
          'the 2026-10-24 scope decision placed outside this profile\'s dependency scope — the '
          'same bucket as the webmessaging/ root entries; the existing postMessage/'
          'JsMessageChannel implementations stay as extensions. Not a 2014 HTML 5.0 obligation.')
WEBWORKERS = ('Reviewed {} (profile-excluded): the file\'s assertions bind the Workers '
              'dependency itself ([WEBWORKERS] — worker/SharedWorker/ServiceWorker global '
              'scopes and their lifecycle), which the 2026-10-24 scope decision placed outside '
              'this profile\'s dependency scope. Not a 2014 HTML 5.0 obligation.')
WEBSTORAGE = ('Reviewed {} (profile-excluded): the file\'s assertions bind the Web Storage '
              'dependency itself ([WEBSTORAGE] — localStorage/sessionStorage storage areas and '
              'events), which the 2026-10-24 scope decision placed outside this profile\'s '
              'dependency scope. Not a 2014 HTML 5.0 obligation.')

# Per-file decisions for every source in the three dep-usage batches.
#   excluded-msg / excluded-workers / excluded-storage — the assertions bind the dep API.
#   post-target — the bound feature postdates the 2014 REC (reason below).
#   queue — infrastructure use of the dep for an in-scope obligation: back to the survey.
DECISIONS = {
    # --- FileAPI: dep use as execution context; the asserted surface is File/Blob/URL ---
    'FileAPI/BlobURL/cross-partition-worker-creation.https.html': 'post-target',
    'FileAPI/BlobURL/cross-partition.https.html': 'post-target',
    'FileAPI/BlobURL/opaque-origin.html': 'queue',
    'FileAPI/FileReader/workers.html': 'queue',
    'FileAPI/blob/Blob-constructor.any.js': 'queue',
    'FileAPI/url/cross-global-revoke.sub.html': 'queue',
    'FileAPI/url/url-in-tags-revoke.window.js': 'post-target',
    'FileAPI/url/url-lifetime.html': 'queue',

    # --- encoding/streams: the whole directory is the Streams API (2019+) ---
    'encoding/streams/decode-utf8.any.js': 'post-target',
    'encoding/streams/stringification-crash.html': 'post-target',

    # --- fetch/api: the dep (Worker) is the execution context, not the subject ---
    'fetch/api/basic/keepalive.any.js': 'post-target',
    'fetch/api/basic/request-referrer-redirected-worker.html': 'queue',
    'fetch/api/cors/data-url-shared-worker.html': 'queue',
    'fetch/api/cors/data-url-worker.html': 'queue',
    'fetch/api/policies/csp-blocked-worker.html': 'post-target',
    'fetch/api/policies/referrer-no-referrer-worker.html': 'queue',
    'fetch/api/policies/referrer-origin-when-cross-origin-worker.html': 'queue',
    'fetch/api/policies/referrer-origin-worker.html': 'queue',
    'fetch/api/policies/referrer-unsafe-url-worker.html': 'queue',
    'fetch/api/request/destination/fetch-destination-frame.https.html': 'post-target',
    'fetch/api/request/destination/fetch-destination-iframe.https.html': 'post-target',
    'fetch/api/request/destination/fetch-destination-no-load-event.https.html': 'post-target',
    'fetch/api/request/destination/fetch-destination-worker.https.html': 'post-target',

    # --- fetch/metadata, nosniff, range, security, stale-while-revalidate ---
    'fetch/metadata/generated/serviceworker.https.sub.html': 'post-target',
    'fetch/metadata/generated/worker-dedicated-constructor.https.sub.html': 'post-target',
    'fetch/metadata/generated/worker-dedicated-constructor.sub.html': 'post-target',
    'fetch/metadata/generated/worker-dedicated-importscripts.https.sub.html': 'post-target',
    'fetch/metadata/generated/worker-dedicated-importscripts.sub.html': 'post-target',
    'fetch/metadata/serviceworker-accessors.https.sub.html': 'post-target',
    'fetch/metadata/sharedworker.https.sub.html': 'post-target',
    'fetch/metadata/worker.https.sub.html': 'post-target',
    'fetch/nosniff/importscripts.html': 'post-target',
    'fetch/nosniff/worker.html': 'post-target',
    'fetch/range/sw-416.https.window.js': 'post-target',
    'fetch/range/sw-intercept-range.https.window.js': 'post-target',
    'fetch/range/sw.https.window.js': 'post-target',
    'fetch/security/dangling-markup/dangling-markup-mitigation-allowed-apis.https.html': 'post-target',
    'fetch/security/dangling-markup/dangling-markup-mitigation.tentative.https.html': 'post-target',
    'fetch/security/embedded-credentials.tentative.sub.html': 'post-target',
    'fetch/stale-while-revalidate/fetch-sw.https.html': 'post-target',

    # --- html/infrastructure/safe-passing-of-structured-data ---
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/broadcastchannel-success-and-failure.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/broadcastchannel-success.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/nested-sharedworker-success.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/nested-worker-success.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/no-coop-coep.https.any.js': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/no-transferring.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-messagechannel-success.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-serviceworker-failure.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-sharedworker-failure.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-simple-success.https.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/transfer-audiodata.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/transfer-errors.window.js': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/transfer-mediasourcehandle.html': 'post-target',
    'html/infrastructure/safe-passing-of-structured-data/transfer-videoframe.html': 'post-target',

    # --- html/semantics: the sandbox model's observable happens to be a storage read ---
    'html/semantics/embedded-content/the-iframe-element/sandbox_026.htm': 'queue',
    'html/semantics/embedded-content/the-iframe-element/sandbox_031.htm': 'queue',
    'html/semantics/embedded-content/the-img-element/list-of-available-images-matching.https.html': 'queue',
    'html/semantics/forms/form-submission-target/form-target-request-header.html': 'post-target',
    'html/semantics/links/links-created-by-a-and-area-elements/htmlanchorelement_noopener.html': 'post-target',
    'html/semantics/links/links-created-by-a-and-area-elements/rel-attribute-clearing.html': 'post-target',
    'html/semantics/links/links-created-by-a-and-area-elements/target_blank_implicit_noopener.html': 'post-target',
    'html/semantics/links/links-created-by-a-and-area-elements/target_blank_implicit_noopener_base.html': 'post-target',
    'html/semantics/scripting-1/the-script-element/css-module/css-module-worker-test.html': 'post-target',
    'html/semantics/scripting-1/the-script-element/json-module/json-module-service-worker-test.https.html': 'post-target',
    'html/semantics/scripting-1/the-script-element/microtasks/checkpoint-after-workerglobalscope-onerror-module.html': 'excluded-workers',
    'html/semantics/scripting-1/the-script-element/microtasks/checkpoint-after-workerglobalscope-onerror.html': 'excluded-workers',
    'html/semantics/scripting-1/the-script-element/module/dynamic-import/alpha/base-url-worker-importScripts.html': 'post-target',
    'html/semantics/scripting-1/the-script-element/module/dynamic-import/alpha/base-url-worker.sub.html': 'post-target',
    'html/semantics/scripting-1/the-script-element/module/dynamic-import/blob-url-workers.window.js': 'post-target',
    'html/semantics/scripting-1/the-script-element/module/import-meta/import-meta-url.html': 'post-target',
    'html/semantics/scripting-1/the-script-element/text-module/service-worker-test.https.html': 'post-target',

    # --- html/webappapis ---
    'html/webappapis/dynamic-markup-insertion/opening-the-input-stream/no-new-global.window.js': 'queue',
    'html/webappapis/dynamic-markup-insertion/opening-the-input-stream/tasks.window.js': 'queue',
    'html/webappapis/scripting/events/event-handler-processing-algorithm-error/synthetic-errorevent-click.html': 'queue',
    'html/webappapis/scripting/events/event-handler-processing-algorithm-error/worker.html': 'excluded-workers',
    'html/webappapis/scripting/events/eventhandler-cancellation.html': 'queue',
    'html/webappapis/scripting/events/messageevent-constructor.https.html': 'excluded-msg',
    'html/webappapis/scripting/processing-model-2/integration-with-the-javascript-agent-formalism/atomics-wait-async.https.any.js': 'post-target',
    'html/webappapis/scripting/processing-model-2/unhandled-promise-rejections/promise-rejection-events.dedicatedworker.html': 'post-target',
    'html/webappapis/scripting/processing-model-2/unhandled-promise-rejections/promise-rejection-events.sharedworker.html': 'post-target',
    'html/webappapis/the-windoworworkerglobalscope-mixin/Worker_Self_Origin.html': 'excluded-workers',
}

POST_TARGET_FEATURES = {
    'FileAPI/BlobURL/cross-partition-worker-creation.https.html':
        'blob-URL partitioning across storage keys was specified in 2020+; the 2014 REC has one origin-bound blob-URL store',
    'FileAPI/BlobURL/cross-partition.https.html':
        'blob-URL partitioning across storage keys was specified in 2020+; the 2014 REC has one origin-bound blob-URL store',
    'FileAPI/url/url-in-tags-revoke.window.js':
        'the BroadcastChannel constructor is the [WEBMSG] Broadcast API specified in 2014-2015+ as part of the messaging surface the scope decision excludes, and the file binds it directly',
    'fetch/api/basic/keepalive.any.js':
        'the keepalive fetch option was specified in 2017+; the 2014 REC\'s fetch model has no keepalive flag',
    'fetch/api/policies/csp-blocked-worker.html':
        'the CSP integration with fetch policy checks through worker contexts postdates the 2014 REC (CSP2 2014-2016+); the worker context itself is the excluded Workers dependency',
    'fetch/api/request/destination/fetch-destination-frame.https.html':
        'Request.destination was specified in 2016+; the 2014 REC\'s fetch model has no destination concept',
    'fetch/api/request/destination/fetch-destination-iframe.https.html':
        'Request.destination was specified in 2016+; the 2014 REC\'s fetch model has no destination concept',
    'fetch/api/request/destination/fetch-destination-no-load-event.https.html':
        'Request.destination was specified in 2016+; the 2014 REC\'s fetch model has no destination concept',
    'fetch/api/request/destination/fetch-destination-worker.https.html':
        'Request.destination was specified in 2016+; the 2014 REC\'s fetch model has no destination concept',
    'fetch/metadata/generated/serviceworker.https.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/generated/worker-dedicated-constructor.https.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/generated/worker-dedicated-constructor.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/generated/worker-dedicated-importscripts.https.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/generated/worker-dedicated-importscripts.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/serviceworker-accessors.https.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/sharedworker.https.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/metadata/worker.https.sub.html':
        'Fetch Metadata request headers (sec-fetch-*) were specified in 2019+; the whole fetch/metadata surface postdates the 2014 REC',
    'fetch/nosniff/importscripts.html':
        'the X-Content-Type-Options blocking integration into fetch/importScripts (the style/script mismatch blocking model) was specified in 2018+ (the CORB/ORB line)',
    'fetch/nosniff/worker.html':
        'the X-Content-Type-Options blocking integration into worker script loads was specified in 2018+ (the CORB/ORB line)',
    'fetch/range/sw-416.https.window.js':
        'range request interception through Service Workers is the [SW] service-worker dependency (2015+) the scope decision excludes',
    'fetch/range/sw-intercept-range.https.window.js':
        'range request interception through Service Workers is the [SW] service-worker dependency (2015+) the scope decision excludes',
    'fetch/range/sw.https.window.js':
        'range request interception through Service Workers is the [SW] service-worker dependency (2015+) the scope decision excludes',
    'fetch/security/dangling-markup/dangling-markup-mitigation-allowed-apis.https.html':
        'the dangling-markup injection mitigation was specified in 2016+ (CSP3-era HTML integration); the 2014 REC predates it',
    'fetch/security/dangling-markup/dangling-markup-mitigation.tentative.https.html':
        'the dangling-markup injection mitigation was specified in 2016+ (CSP3-era HTML integration); the 2014 REC predates it',
    'fetch/security/embedded-credentials.tentative.sub.html':
        'the embedded-credentials fetch policy was specified in 2018+; the 2014 REC predates it',
    'fetch/stale-while-revalidate/fetch-sw.https.html':
        'the stale-while-revalidate cache directive and its fetch integration were specified in 2019+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/broadcastchannel-success-and-failure.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/broadcastchannel-success.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/nested-sharedworker-success.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/nested-worker-success.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/no-coop-coep.https.any.js':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/no-transferring.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-messagechannel-success.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-serviceworker-failure.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-sharedworker-failure.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/shared-array-buffers/window-simple-success.https.html':
        'SharedArrayBuffer structured-clone transfer (and the COOP/COEP cross-origin isolation it requires) was specified in 2018-2020+',
    'html/infrastructure/safe-passing-of-structured-data/transfer-audiodata.html':
        'AudioData/encoded-audio-chunk transfer was specified in 2020+ (WebCodecs); the 2014 REC predates it',
    'html/infrastructure/safe-passing-of-structured-data/transfer-errors.window.js':
        'the DataCloneError transfer surface for post-2014 platform objects postdates the 2014 REC\'s §2.7.5 clone model',
    'html/infrastructure/safe-passing-of-structured-data/transfer-mediasourcehandle.html':
        'MediaSourceHandle transfer was specified in 2022+; the 2014 REC predates it',
    'html/infrastructure/safe-passing-of-structured-data/transfer-videoframe.html':
        'VideoFrame transfer was specified in 2020+ (WebCodecs); the 2014 REC predates it',
    'html/semantics/forms/form-submission-target/form-target-request-header.html':
        'the form-submission-target channel/request-header integration was specified in 2023+; the 2014 REC\'s §4.10.22 submission model predates it',
    'html/semantics/links/links-created-by-a-and-area-elements/htmlanchorelement_noopener.html':
        'the rel=noopener link type was added to the standard in 2015-2016; the 2014 REC\'s §4.12.4 link-type list has no noopener',
    'html/semantics/links/links-created-by-a-and-area-elements/rel-attribute-clearing.html':
        'the rel-attribute token clearing (dynamically removing and re-adding link types) micro-behavior was specified in 2016+',
    'html/semantics/links/links-created-by-a-and-area-elements/target_blank_implicit_noopener.html':
        'implicit noopener for target=_blank was specified in 2019+; the 2014 REC passes the opener through',
    'html/semantics/links/links-created-by-a-and-area-elements/target_blank_implicit_noopener_base.html':
        'implicit noopener for target=_blank was specified in 2019+; the 2014 REC passes the opener through',
    'html/semantics/scripting-1/the-script-element/css-module/css-module-worker-test.html':
        'CSS module scripts were specified in 2019+; the 2014 REC has only classic scripts',
    'html/semantics/scripting-1/the-script-element/json-module/json-module-service-worker-test.https.html':
        'JSON module scripts were specified in 2020+; the 2014 REC has only classic scripts',
    'html/semantics/scripting-1/the-script-element/module/dynamic-import/alpha/base-url-worker-importScripts.html':
        'dynamic import() was specified in 2020+; the 2014 REC has only classic scripts',
    'html/semantics/scripting-1/the-script-element/module/dynamic-import/alpha/base-url-worker.sub.html':
        'dynamic import() was specified in 2020+; the 2014 REC has only classic scripts',
    'html/semantics/scripting-1/the-script-element/module/dynamic-import/blob-url-workers.window.js':
        'dynamic import() was specified in 2020+; the 2014 REC has only classic scripts',
    'html/semantics/scripting-1/the-script-element/module/import-meta/import-meta-url.html':
        'import.meta was specified in 2020+; the 2014 REC has only classic scripts',
    'html/semantics/scripting-1/the-script-element/text-module/service-worker-test.https.html':
        'module scripts in Service Workers are the [SW] dependency (2015+) plus the 2016+ module-script model',
    'html/webappapis/scripting/processing-model-2/integration-with-the-javascript-agent-formalism/atomics-wait-async.https.any.js':
        'Atomics.waitAsync was specified in 2021+ (ES2021); the agent-cluster formalism postdates the 2014 REC',
    'html/webappapis/scripting/processing-model-2/unhandled-promise-rejections/promise-rejection-events.dedicatedworker.html':
        'the unhandledrejection/rejectionhandled events were specified in 2015-2016+; the 2014 REC predates them',
    'html/webappapis/scripting/processing-model-2/unhandled-promise-rejections/promise-rejection-events.sharedworker.html':
        'the unhandledrejection/rejectionhandled events were specified in 2015-2016+; the 2014 REC predates them',
    'encoding/streams/decode-utf8.any.js':
        'the TextDecoderStream/TextEncoderStream Streams API was specified in 2019+',
    'encoding/streams/stringification-crash.html':
        'the TextDecoderStream/TextEncoderStream Streams API was specified in 2019+',
}

SUFFIX = {
    'excluded-msg': WEBMSG,
    'excluded-workers': WEBWORKERS,
    'excluded-storage': WEBSTORAGE,
}


def main():
    proposals = json.loads(proposals_path.read_text(encoding='utf-8-sig'))
    dep = set()
    for k in ('dep-web-messaging-usage', 'dep-web-storage-usage', 'dep-workers-usage'):
        dep.update(proposals['batches'].get(k, {}).get('paths', []))
    undecided = dep - set(DECISIONS)
    if undecided:
        print('ERROR: dep-batch files without decisions:', sorted(undecided))
        sys.exit(1)

    manifest = json.loads(applicability_path.read_text(encoding='utf-8-sig'))
    tests = manifest['tests']
    by_path = {t['path']: t for t in tests}
    counts = Counter()
    requeued = []
    for path, decision in sorted(DECISIONS.items()):
        entry = by_path.get(path)
        prior = entry.get('reason') if entry else None
        if decision.startswith('excluded-'):
            reason = SUFFIX[decision].format(TODAY)
            if entry is None:
                tests.append({'path': path, 'classification': 'profile-excluded', 'reason': reason})
                counts['profile-excluded'] += 1
            elif entry['classification'] == 'post-target' and f'Reviewed {TODAY}' in prior:
                entry['classification'] = 'profile-excluded'
                entry['reason'] = reason
                counts['corrected->profile-excluded'] += 1
            elif entry['classification'] != 'profile-excluded':
                entry['classification'] = 'profile-excluded'
                entry['reason'] = reason
                counts['overwritten->profile-excluded'] += 1
            else:
                counts['already-profile-excluded'] += 1
        elif decision == 'post-target':
            feature = POST_TARGET_FEATURES[path]
            reason = (f'Reviewed {TODAY} (post-target): {feature}. The dependency-usage marker '
                      f'was reviewed per the propose tool\'s low-confidence protocol and the '
                      f'file\'s own assertions bind this later feature. Not a 2014 HTML 5.0 '
                      f'obligation.')
            if entry is None:
                tests.append({'path': path, 'classification': 'post-target', 'reason': reason})
                counts['post-target'] += 1
            elif entry['classification'] == 'post-target' and f'Reviewed {TODAY}' in prior:
                entry['reason'] = reason
                counts['corrected->post-target'] += 1
            elif entry['classification'] != 'post-target':
                entry['classification'] = 'post-target'
                entry['reason'] = reason
                counts['overwritten->post-target'] += 1
            else:
                counts['already-post-target'] += 1
        else:  # queue
            if entry is None:
                requeued.append(path)
                counts['queued'] += 1
            elif entry['classification'] == 'post-target' and f'Reviewed {TODAY}' in prior:
                tests.remove(entry)
                requeued.append(path)
                counts['corrected->queued'] += 1
            else:
                counts['already-classified-kept'] += 1

    # Rewrite the survey queue files without the files this review classified.
    for queue_file in [root / 'artifacts/m1-queue-html.txt', root / 'artifacts/m1-queue-encoding.txt',
                       root / 'artifacts/m1-queue-fetch.txt', root / 'artifacts/m1-queue-dom.txt',
                       root / 'artifacts/m1-queue-rest.txt']:
        if not queue_file.exists():
            continue
        lines = [l for l in queue_file.read_text(encoding='utf-8').splitlines()
                 if l.strip() and l.strip() not in DECISIONS]
        queue_file.write_text('\n'.join(lines) + ('\n' if lines else ''), encoding='utf-8')

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
    print(f'dep-usage per-file review: {dict(counts)}')


if __name__ == '__main__':
    main()
