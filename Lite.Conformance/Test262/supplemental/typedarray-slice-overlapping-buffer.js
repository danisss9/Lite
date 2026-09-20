/*---
description: ES2020 slice copies overlapping same-type buffers in byte order.
esid: sec-%typedarray%.prototype.slice
features: [TypedArray, Symbol.species, BigInt]
---*/
// Separately mapped ES2020 coverage for the pinned upstream test whose modern
// testTypedArray.js helper also supplies immutable/resizable/Float16 buffers.
for (var C of [Int8Array, Uint8Array, Uint8ClampedArray, Int16Array, Uint16Array,
               Int32Array, Uint32Array, Float32Array, Float64Array, BigInt64Array, BigUint64Array]) {
  var big = C === BigInt64Array || C === BigUint64Array;
  var input = [10, 20, 30, 40, 50, 60].map(function(x) { return big ? BigInt(x) : x; });
  var source = new C(input);
  source.constructor = { [Symbol.species]: function() { return new C(source.buffer, 2 * C.BYTES_PER_ELEMENT); } };
  var copy = source.slice(1, 4);
  assert.sameValue(copy.buffer, source.buffer);
  assert.sameValue(copy.length, 4);
  for (var index = 0; index < 4; index++) assert.sameValue(copy[index], input[index === 3 ? 5 : 1]);
}
