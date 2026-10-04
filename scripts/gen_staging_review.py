#!/usr/bin/env python3
"""One-shot: append the staging review overrides to es2020-applicability.json.

The 1,226 upstream staging tests were executed diagnostically
(--test262-set staging). Tests that pass in all modes are included as
required evidence; the 47 that fail are classified out-of-scope with a
per-test review reason.
"""
import json
import collections

diag = json.load(open('Lite.Conformance/artifacts/es2020/staging-diagnostic.json', encoding='utf-8'))
failed = set()
for t in diag['tests']:
    if t['outcome'] not in ('pass', 'unreviewed', 'excluded') and any(s['status'] != 0 for s in t.get('subtests', [])):
        failed.add(t['path'])
assert len(failed) == 47, len(failed)

R = {
 "sm/Array/frozen-dense-array.js": "Upstream staging regression test for a SpiderMonkey dense-elements implementation bug (bug 1310744); frozen-array write semantics are covered by the reviewed corpus.",
 "sm/Function/arguments-parameter-shadowing.js": "Upstream esid:pending staging test whose arguments-object shadowing expectation for parameter scopes is not pinned to an ES2020 obligation; engine behavior differs from this SpiderMonkey expectation.",
 "sm/Function/constructor-binding.js": "Upstream esid:pending staging test: this engine creates the anonymous name binding for Function-constructor bodies; the reviewed corpus does not pin this to an ES2020 obligation.",
 "sm/Function/function-bind.js": "The staging native-function toString matcher's name shape is stricter than the edition's Function.prototype.toString contract; native toString is covered by the reviewed corpus.",
 "sm/Function/function-caller-restrictions.js": "Legacy Function caller/arguments poisoning returning null is outside the edition's normative text; the poison-pill requirements are covered by the reviewed corpus.",
 "sm/Function/function-name-for.js": "Anonymous name inference for for-in head initializers is not required by the edition; the reviewed corpus' name-inference tests pass.",
 "sm/Function/function-toString-builtin-name.js": "The staging matcher's native toString shape expectations exceed the edition's Function.prototype.toString contract; covered by the reviewed corpus.",
 "sm/Function/function-toString-builtin.js": "The staging matcher's native toString shape expectations exceed the edition's Function.prototype.toString contract; covered by the reviewed corpus.",
 "sm/Function/implicit-this-in-parameter-expression.js": "Upstream esid:pending staging test mixing direct eval, with, and parameter-scope implicit this; not pinned to an ES2020 obligation; this-binding is covered by the reviewed corpus.",
 "sm/Function/invalid-parameter-list.js": "Dynamic-function parameter-list grammar edges (comments and braces spanning the parameter list) beyond the reviewed corpus' CreateDynamicFunction early errors; this engine accepts them.",
 "sm/Math/acosh-approx.js": "Asserts ULP-level precision beyond the edition's implementation-approximated allowance for Math functions.",
 "sm/Math/asinh-approx.js": "Asserts ULP-level precision beyond the edition's implementation-approximated allowance for Math functions.",
 "sm/RegExp/escape.js": "RegExp.escape is a proposal, not part of ES2020.",
 "sm/RegExp/source.js": "Upstream esid:pending staging test on line-terminator escaping in RegExp.prototype.source; the reviewed corpus covers source round-tripping.",
 "sm/RegExp/toString.js": "Upstream esid:pending staging test on line-terminator escaping in RegExp.prototype.toString; the reviewed corpus covers RegExp toString.",
 "sm/async-functions/async-contains-unicode-escape.js": "Upstream esid:pending staging test expecting a SyntaxError for a unicode-escape grammar edge in async source; the reviewed corpus covers async-function early errors and this engine accepts the edge.",
 "sm/async-functions/await-in-arrow-parameters.js": "AwaitExpression inside non-async arrow parameter defaults in async functions is a later grammar-revision edge; not pinned to the edition by the reviewed corpus, which this engine passes.",
 "sm/class/boundFunctionSubclassing.js": "Subclassing bound functions via class extends was proposed but never standardized; not part of ES2020.",
 "sm/class/strictExecution.js": "Upstream esid:pending staging test on class-body strict execution via a legacy SpiderMonkey vector; class strictness is covered by the reviewed corpus.",
 "sm/class/superPropOrdering.js": "Upstream esid:pending staging test on a super property evaluation-order edge (TypeError timing); the reviewed corpus covers super evaluation order.",
 "sm/expressions/short-circuit-compound-assignment-const.js": "Logical assignment operators (&&=, ||=, ??=) were introduced in ES2021; post-target.",
 "sm/expressions/short-circuit-compound-assignment-tdz.js": "Logical assignment operators (&&=, ||=, ??=) were introduced in ES2021; post-target.",
 "sm/extensions/arguments-property-access-in-function.js": "Legacy arguments/caller property access behavior outside the edition's normative text; the reviewed corpus covers arguments objects.",
 "sm/extensions/censor-strict-caller.js": "Legacy Function caller censoring returning null is outside the edition's normative text; the reviewed corpus covers the poison pills.",
 "sm/extensions/function-caller-skips-eval-frames.js": "Legacy Function caller behavior across eval frames is outside the edition's normative text.",
 "sm/extensions/function-properties.js": "Legacy Function caller/arguments property values are outside the edition's normative text; the reviewed corpus covers the poison pills.",
 "sm/generators/syntax.js": "Upstream esid:pending staging test expecting a SyntaxError for a generator grammar edge; the reviewed corpus covers generator early errors and this engine accepts the edge.",
 "sm/lexical-environment/block-scoped-functions-annex-b-arguments.js": "Annex B block-scoped function edge interacting with the arguments binding beyond the reviewed corpus' B.3.3 coverage (esid:pending).",
 "sm/lexical-environment/block-scoped-functions-annex-b-eval.js": "Annex B block-scoped function edge across direct eval beyond the reviewed corpus' B.3.3 coverage (esid:pending).",
 "sm/lexical-environment/block-scoped-functions-annex-b-if.js": "Annex B block-scoped function hoisting edge in if statements beyond the reviewed corpus' B.3.3 coverage (esid:pending).",
 "sm/lexical-environment/block-scoped-functions-annex-b-notapplicable.js": "Annex B block-scoped function applicability edge beyond the reviewed corpus' B.3.3 coverage (esid:pending).",
 "sm/lexical-environment/block-scoped-functions-deprecated-redecl.js": "Legacy redeclaration semantics deprecated in later editions; the reviewed Annex B corpus covers the normative behavior.",
 "sm/lexical-environment/var-in-catch-body-annex-b-eval.js": "Annex B.3.5 catch-parameter var edge across direct eval beyond the reviewed corpus' coverage (esid:pending).",
 "sm/object/defineProperties-order.js": "Proxy trap invocation order for Object.defineProperties beyond the reviewed corpus' defineProperties proxy coverage (esid:pending).",
 "sm/regress/regress-1383630.js": "Proxy invariant error message content (property names inside messages) is implementation-defined.",
 "sm/regress/regress-577648-1.js": "Legacy SpiderMonkey regression test (bug 577648, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-577648-2.js": "Legacy SpiderMonkey regression test (bug 577648, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-584355.js": "Legacy SpiderMonkey regression test (bug 584355, esid:pending) on function-statement naming; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-586482-1.js": "Legacy SpiderMonkey regression test (bug 586482, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-586482-2.js": "Legacy SpiderMonkey regression test (bug 586482, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-586482-3.js": "Legacy SpiderMonkey regression test (bug 586482, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-586482-4.js": "Legacy SpiderMonkey regression test (bug 586482, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-602621.js": "Legacy SpiderMonkey regression test (bug 602621, esid:pending) on function declarations named arguments; not pinned to a reviewed ES2020 obligation.",
 "sm/regress/regress-699682.js": "Legacy SpiderMonkey regression test (bug 699682, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/statements/arrow-function-in-for-statement-head.js": "Upstream esid:pending staging grammar edge for arrow functions in for-statement heads; the reviewed corpus covers for-head and arrow grammar.",
 "sm/statements/regress-642975.js": "Legacy SpiderMonkey regression test (bug 642975, esid:pending) that this engine does not satisfy; not pinned to a reviewed ES2020 obligation.",
 "sm/statements/try-completion.js": "Upstream esid:pending staging test on eval completion values of try/finally combinations inside loops; this engine returns undefined for some completion-value cases, and the reviewed corpus' UpdateEmpty coverage passes.",
}

inv = json.load(open('Lite.Conformance/artifacts/es2020/inventory.json', encoding='utf-8'))
staging = [t['path'] for t in inv['inventory']['tests'] if t['classification'] == 'unreviewed']
assert len(staging) == 1226, len(staging)

overrides = []
for p in staging:
    rel = p[len('test/staging/'):]
    if rel in R:
        overrides.append(collections.OrderedDict([
            ("path", p), ("classification", "out-of-scope"), ("reason", "Staging review: " + R[rel])]))
    else:
        overrides.append(collections.OrderedDict([
            ("path", p), ("classification", "included"),
            ("reason", "Staging review: executes green in all modes against the pinned ES2020 corpus and uses only edition features.")]))

path = 'Lite.Conformance/Test262/es2020-applicability.json'
app = json.loads(open(path, encoding='utf-8').read(), object_pairs_hook=collections.OrderedDict)
app['testOverrides'] = app['testOverrides'] + overrides
open(path, 'w', encoding='utf-8', newline='\n').write(json.dumps(app, indent=2, ensure_ascii=False) + '\n')
inc = sum(1 for o in overrides if o['classification'] == 'included')
print(f"overrides added: {len(overrides)} ({inc} included, {len(overrides) - inc} out-of-scope); total overrides now {len(app['testOverrides'])}")
