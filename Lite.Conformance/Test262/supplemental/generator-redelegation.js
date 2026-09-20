/*---
description: Each iteration resumes and then starts a fresh yield-star delegation.
esid: sec-generator-function-definitions-runtime-semantics-evaluation
features: [generators]
includes: [compareArray.js]
---*/
function* repeated() {
  for (var i = 0; i < 3; i++) yield* [i, i + 10];
}
assert.compareArray(Array.from(repeated()), [0, 10, 1, 11, 2, 12]);
