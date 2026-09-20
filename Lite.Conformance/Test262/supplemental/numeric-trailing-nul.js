/*---
description: A NUL is not ECMAScript whitespace in StringNumericLiteral.
esid: sec-tonumber-applied-to-the-string-type
---*/
assert.sameValue(Number.isNaN(Number('42\u0000')), true);
assert.sameValue(Number.isNaN(Number('\u0000')), true);
assert.sameValue(Number('42\u0020'), 42);
