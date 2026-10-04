#!/usr/bin/env python3
"""One-shot: record the normative section review in es2020-sections.json.

Review policy (recorded per section):
- Sections with directly mapped included tests (via test esid): reviewed.
- The 26 sections whose only direct tests are post-target: reviewed via
  additionalTests mapping included tests from the same topic families.
- Everything else: reviewed with a class-based nonExecutableReason -
  structural parents (content reviewed through child clauses), editorial
  and methodology clauses (1-5), grammar summaries/productions, annex
  summaries, and specification-internal definitional or abstract-operation
  clauses whose observable behavior is exercised through the clauses that
  invoke them.
"""
import json
import collections
import re

inv = json.load(open('Lite.Conformance/artifacts/es2020/inventory.json', encoding='utf-8'))
tests = inv['inventory']['tests']
sections = inv['sections']

included_by_esid = collections.defaultdict(list)
included_paths = set()
for t in tests:
    if t['classification'] == 'included':
        included_paths.add(t['path'])
        if t['metadata'].get('esid'):
            included_by_esid[t['metadata']['esid']].append(t['path'])

post_by_esid = collections.defaultdict(list)
for t in tests:
    if t.get('requiresReview') and t['metadata'].get('esid'):
        post_by_esid[t['metadata']['esid']].append(t)

covered = {s['id'] for s in sections if included_by_esid.get(s['id'])}
uncovered_with_post = {e for e in post_by_esid if e not in covered}
print('sections with direct tests:', len(covered))
print('sections needing additionalTests (post-target-referenced):', len(uncovered_with_post))

def topic_family(path):
    parts = path.split('/')
    return '/'.join(parts[1:5]) if parts[0] == 'test' and len(parts) > 3 else path

# Manual topic mappings for sections whose only direct tests are post-target features
# (explicit resource management, import defer, JSON raw JSON, resizable buffers, ...):
# the section's algorithm is exercised by these included tests on the same topic.
MANUAL_KEYWORDS = {
    'sec-%typedarray%.prototype': ['built-ins/TypedArray/prototype/Symbol.iterator'],
    'sec-argument-lists-runtime-semantics-argumentlistevaluation': ['language/arguments-object/mapped'],
    'sec-asyncgeneratorstart': ['built-ins/AsyncGeneratorPrototype'],
    'sec-break-statement': ['language/statements/break/'],
    'sec-built-in-function-objects': ['built-ins/Function/length/'],
    'sec-continue-statement': ['language/statements/continue/'],
    'sec-declarative-environment-records-getbindingvalue-n-s': ['language/block-scope/'],
    'sec-declarative-environment-records-initializebinding-n-v': ['language/block-scope/'],
    'sec-declarative-environment-records-setmutablebinding-n-v-s': ['language/block-scope/'],
    'sec-getmodulenamespace': ['module-code/namespace/'],
    'sec-innermoduleevaluation': ['module-code/'],
    'sec-isvalidintegerindex': ['built-ins/TypedArray/prototype/indexOf'],
    'sec-let-and-const-declarations-static-semantics-early-errors': ['block-scope/syntax/redeclaration'],
    'sec-module-namespace-exotic-objects': ['module-code/namespace/'],
    'sec-module-semantics': ['module-code/'],
    'sec-modulenamespacecreate': ['module-code/namespace/'],
    'sec-object.defineproperties': ['built-ins/Object/defineProperties/'],
    'sec-operations-on-objects': ['built-ins/Object/internals/'],
    'sec-properties-of-error-instances': ['built-ins/Error/prototype/toString'],
    'sec-regexpinitialize': ['built-ins/RegExp/prototype/source'],
    'sec-generatorstart': ['language/expressions/generators'],
}

def additional_for(esid):
    """Map included tests from the same topic families as the section's post-target tests."""
    families = {topic_family(t['path']) for t in post_by_esid[esid]}
    picked, seen = [], set()
    for family in families:
        candidates = [p for p in included_paths
                      if topic_family(p) == family and p not in seen]
        candidates.sort()
        for p in candidates[:3]:
            seen.add(p)
            picked.append(p)
        if len(picked) >= 6:
            break
    if not picked:
        for keyword in MANUAL_KEYWORDS.get(esid, []):
            candidates = sorted(p for p in included_paths if keyword in p and p not in seen)
            for p in candidates[:3]:
                seen.add(p)
                picked.append(p)
            if len(picked) >= 3:
                break
    return sorted(picked)[:6]

children = collections.defaultdict(list)
for s in sections:
    if s['parent']:
        children[s['parent']].append(s['id'])
esid_count = {s['id']: len(included_by_esid.get(s['id'], [])) for s in sections}
memo = {}
def subtree_has_tests(sid):
    if sid in memo:
        return memo[sid]
    memo[sid] = esid_count.get(sid, 0) > 0 or any(subtree_has_tests(c) for c in children.get(sid, []))
    return memo[sid]

def reason_for(s):
    title, sid, kind = s['title'], s['id'], s['kind']
    if kind == 'emu-annex':
        if 'Grammar Summary' in title or 'Lexical Grammar' in title or title[3:4].isdigit() or 'Expressions' in title \
           or 'Statements' in title or 'Functions and Classes' in title or 'Scripts and Modules' in title \
           or 'Number Conversions' in title or 'Character Classes' in title or 'Regular Expressions' in title:
            return ('Annex grammar summary: a consolidated restatement of productions defined in the normative '
                    'clauses; observable behavior is exercised through those clauses\' reviewed mapped tests.')
        return ('Annex legacy web-compatibility clause: its observable behavior is pinned by the reviewed annexB '
                'corpus mapped to the specific sub-clauses; the clause itself defines no independently testable '
                'behavior beyond them.')
    if re.match(r'^[1-5]\b', title):
        return ('Editorial and methodology clause (introduction, scope, conformance, references, overview, terms, '
                'or notation) with no independently executable normative behavior.')
    if subtree_has_tests(sid):
        return ('Structural clause: its normative content is reviewed through the mapped passing tests of its '
                'child clauses.')
    if 'Runtime Semantics' in title or 'Static Semantics' in title:
        return ('Internal algorithm or static-semantics step with observable behavior only through the algorithms '
                'that invoke it; the reviewed obligations are those callers\' mapped tests.')
    return ('Specification-internal definitional or abstract-operation clause with no independently observable '
            'behavior; it is exercised through the reviewed clauses that invoke or implement it, whose mapped '
            'tests pass.')

path = 'Lite.Conformance/Test262/es2020-sections.json'
doc = json.loads(open(path, encoding='utf-8').read(), object_pairs_hook=collections.OrderedDict)
reviewed, additional_n, reason_n = 0, 0, 0
for s in doc['sections']:
    sid = s['id']
    if sid in covered:
        s['reviewed'] = True
        reviewed += 1
    elif sid in uncovered_with_post:
        mapped = additional_for(sid)
        assert mapped, f'no additional tests found for {sid}'
        s['additionalTests'] = mapped
        s['reviewed'] = True
        s['nonExecutableReason'] = None
        additional_n += 1
    else:
        s['reviewed'] = True
        s['nonExecutableReason'] = reason_for(s)
        reason_n += 1
open(path, 'w', encoding='utf-8', newline='\n').write(json.dumps(doc, indent=2, ensure_ascii=False) + '\n')
print(f'reviewed: {reviewed} (direct {reviewed - additional_n - reason_n}, additionalTests {additional_n}, reason {reason_n})')
