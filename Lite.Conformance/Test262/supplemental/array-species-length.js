/*---
description: Array map and slice pass ToLength to species without uint truncation.
esid: sec-arrayspeciescreate
features: [Proxy, Symbol.species]
---*/
for (var method of ['map', 'slice']) {
  var observed;
  var sentinel = {};
  var array = [];
  array.constructor = { [Symbol.species]: function(length) {
    observed = length;
    throw sentinel;
  }};
  var receiver = new Proxy(array, { get: function(target, key) {
    return key === 'length' ? 4294967297 : target[key];
  }});
  try {
    if (method === 'map') Array.prototype.map.call(receiver, function() {});
    else Array.prototype.slice.call(receiver);
    throw new Test262Error('Species constructor was not called');
  } catch (error) { assert.sameValue(error, sentinel); }
  assert.sameValue(observed, 4294967297, method);
}
