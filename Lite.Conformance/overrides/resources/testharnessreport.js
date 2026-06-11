/* Lite conformance harness hook. Shadows WPT's stock testharnessreport.js: when the
 * harness completes, serialize all results and hand them to the host via __lite_report,
 * which WptRunner registers on the Jint engine before page scripts run. */
add_completion_callback(function (tests, harness_status) {
  var results = [];
  for (var i = 0; i < tests.length; i++) {
    results.push({
      name: tests[i].name,
      status: tests[i].status,
      message: tests[i].message || null
    });
  }
  var payload = JSON.stringify({
    status: harness_status.status,
    message: harness_status.message || null,
    tests: results
  });
  if (typeof __lite_report === 'function') {
    __lite_report(payload);
  } else if (typeof console !== 'undefined') {
    console.log('LITE-REPORT ' + payload);
  }
});
