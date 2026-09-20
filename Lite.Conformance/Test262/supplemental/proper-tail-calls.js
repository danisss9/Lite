/*---
description: Strict mutual tail calls do not accumulate execution contexts.
esid: sec-tail-position-calls
flags: [onlyStrict]
features: [tail-call-optimization]
---*/
function even(n) { if (n === 0) return true; return odd(n - 1); }
function odd(n) { if (n === 0) return false; return even(n - 1); }
assert.sameValue(even(50000), true);
