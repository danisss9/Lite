#!/usr/bin/env python3
"""One-shot: record the CSS 2.1 section, property, and obligation review.

Review policy (recorded per section; the spec's own informative markers decide):
- Informative by explicit spec statement or structural role: chapters 1-2, the
  chapter headers and section introductions that carry no independent normative
  requirement, definitional/example sections, and Appendices A (aural), B, C
  (change history), F, G (grammar; parsing obligations live in the normative
  syntax sections), I, plus E.1/E.3.
- Optional (spec-permitted, not required): 18.2 system colors (deprecated) and
  18.5 magnification (the UA may support it).
- User-agent: every other section in chapters 3-18 plus Appendix D (the
  normative HTML default style sheet) and E.2 (normative painting order).
  Conservative by default: where a section mixes exposition with normative
  requirements it stays user-agent so no requirement silently drops out. Each
  such section is decomposed to obligation css21.<clause>; sections 13.2-13.4
  are print-only. Test mappings land as applicability batches close, so every
  obligation starts untested and readiness stays false.

The 20 aural properties (media group aural) are informative with the appendix;
the other 95 properties are user-agent and map to the obligation of the section
that defines them (titles are parsed for quoted property names; the counter
properties defined in 12.4 and the 8.5 border shorthands are special-cased).

Also rewrites Profile/css21-requirements.json: the four css21.ua.* controls are
preserved; the css21.13.pagination placeholder is replaced by the real 13.2-13.4
decomposition. The file's reviewComplete flips only here.
"""
import collections
import json
import re

TARGET = 'https://www.w3.org/TR/2011/REC-CSS2-20110607/'

sections_path = 'Lite.Conformance/Profile/css21-sections.json'
properties_path = 'Lite.Conformance/Profile/css21-properties.json'
requirements_path = 'Lite.Conformance/Profile/css21-requirements.json'

INFORMATIVE_STRUCTURE = {
    # Chapter headers: structural introductions; normative content lives in children.
    '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14', '15', '16', '17', '18',
    # Section introductions without independent normative requirements.
    '7.1', '9.1', '13.1', '15.1', '17.1',
    # Definitional (exercised through the sections that use the terms).
    '3.1', 'E.1',
    # Examples and informative historical notes.
    '4.1.2.2', '8.2', '16.6.2', 'E.3',
}
OPTIONAL_SECTIONS = {
    '18.2': 'System colors are deprecated by the Recommendation; supporting them is permitted, not required.',
    '18.5': 'Magnification is a user-agent chrome capability the Recommendation permits ("may"), outside the rendering profile.',
}
INFORMATIVE_CHAPTERS = {'1', '2'}
INFORMATIVE_APPENDICES = ('A', 'B', 'C', 'F', 'G', 'I')
USER_AGENT_APPENDICES = {'D': 'the normative default style sheet for HTML 4', 'E': None}
PRINT_ONLY_PREFIXES = ('13.2', '13.3', '13.4')

RATIONALE = {
    'informative-chapter': 'Chapter {clause} is informative by the Recommendation\'s own organization statement (introduction/tutorial material); it states no independently testable user-agent requirement.',
    'informative-structure': 'Structural introduction: its content is carried by the reviewed child sections and their obligations; it states no independently testable user-agent requirement.',
    'informative-definitions': 'Definitional clause: its terms are exercised through the normative sections that use them and those sections\' reviewed obligations.',
    'informative-example': 'Example or historical note: illustrative restatement of behavior specified normatively elsewhere; no independent requirement.',
    'informative-appendix': 'Appendix {clause} is informative ({why}); no speech renderer or binding to normative references is required.',
    'optional': '{why}',
    'user-agent': 'Normative user-agent requirement reviewed against the pinned Recommendation; decomposed to obligation {obligation}, whose test mappings are recorded as applicability batches close.',
}


def informative_kind(clause: str, title: str) -> str | None:
    if clause in INFORMATIVE_STRUCTURE:
        if clause in ('3.1', 'E.1'):
            return 'informative-definitions'
        if clause in ('4.1.2.2', '8.2', '16.6.2', 'E.3'):
            return 'informative-example'
        return 'informative-structure'
    top = clause.split('.')[0]
    if top in INFORMATIVE_CHAPTERS:
        return 'informative-chapter'
    if top in USER_AGENT_APPENDICES:
        return None  # Appendix D and E.2 are normative.
    return None


def is_informative_appendix(clause: str) -> str | None:
    top = clause.split('.')[0]
    if top == 'A':
        return 'aural style sheets; no speech renderer is required'
    if top == 'B':
        return 'bibliography'
    if top == 'C':
        return 'change history and errata; adopted corrections are already part of the pinned REC text'
    if top == 'F':
        return 'property index'
    if top == 'G':
        return 'the grammar is informative; parsing obligations come from the normative syntax, selector, and property definitions'
    if top == 'I':
        return 'index'
    return None


def main() -> int:
    sections_doc = json.load(open(sections_path, encoding='utf-8'))
    properties_doc = json.load(open(properties_path, encoding='utf-8'))
    requirements_doc = json.load(open(requirements_path, encoding='utf-8'))

    sections = sections_doc['sections']
    properties = properties_doc['properties']
    prop_names = {p['name'] for p in properties}

    # --- Sections -----------------------------------------------------------
    obligations = []
    seen_clauses = set()
    informative_n = optional_n = agent_n = 0
    for s in sections:
        clause, title = s['clause'], s['title']
        assert clause not in seen_clauses, f'duplicate section clause {clause}'
        seen_clauses.add(clause)
        if (why := is_informative_appendix(clause)) is not None:
            if clause == 'E.2':
                s['classification'], agent_n = 'user-agent', agent_n + 1
                s['requirementIds'] = ['css21.E.2']
                s['rationale'] = RATIONALE['user-agent'].format(obligation='css21.E.2')
                obligations.append(make_obligation('E.2', title, s['url'], ['screen', 'print']))
                continue
            s['classification'] = 'informative'
            s['rationale'] = RATIONALE['informative-appendix'].format(clause=clause, why=why)
            s['requirementIds'] = []
            informative_n += 1
            continue
        if clause in OPTIONAL_SECTIONS:
            s['classification'] = 'optional'
            s['rationale'] = RATIONALE['optional'].format(why=OPTIONAL_SECTIONS[clause])
            s['requirementIds'] = []
            optional_n += 1
            continue
        if (kind := informative_kind(clause, title)) is not None:
            s['classification'] = 'informative'
            s['rationale'] = RATIONALE[kind].format(clause=clause)
            s['requirementIds'] = []
            informative_n += 1
            continue
        media = ['print'] if clause.startswith(PRINT_ONLY_PREFIXES) else ['screen', 'print']
        s['classification'] = 'user-agent'
        s['requirementIds'] = [f'css21.{clause}']
        s['rationale'] = RATIONALE['user-agent'].format(obligation=f'css21.{clause}')
        agent_n += 1
        obligations.append(make_obligation(clause, title, s['url'], media))
    assert informative_n + optional_n + agent_n == len(sections)

    # --- Obligations --------------------------------------------------------
    by_id = {o['id']: o for o in obligations}
    assert len(by_id) == len(obligations), 'duplicate obligation id'
    seeds = [r for r in requirements_doc['requirements'] if r['id'].startswith('css21.ua.')]
    assert len(seeds) == 4, f'expected the four css21.ua.* seeds, found {len(seeds)}'
    requirements_doc['requirements'] = seeds + obligations
    requirements_doc['reviewComplete'] = True

    # --- Properties ---------------------------------------------------------
    # Parse property-defining sections from titles: quoted property names.
    defines = collections.defaultdict(set)
    for o in obligations:
        clause = o['id'][len('css21.'):]
        for name in re.findall(r"'([^']+)'", o['title']):
            if name in prop_names:
                defines[name].add(clause)
    defines['counter-increment'].add('12.4')
    defines['counter-reset'].add('12.4')
    # Sections whose titles do not quote every property they define.
    for name, clause in [('border-collapse', '17.6.2'), ('border-spacing', '17.6.1'),
                         ('caption-side', '17.4.1'), ('outline-color', '18.4'),
                         ('outline-style', '18.4'), ('outline-width', '18.4')]:
        defines[name].add(clause)
    unmapped = []
    for p in properties:
        if 'aural' in p['mediaGroups'].split(','):
            p['classification'] = 'informative'
            p['rationale'] = ('Aural property defined by informative Appendix A; no speech renderer is required, '
                              'and the appendix records no executable screen or print obligation.')
            p['requirementIds'] = []
            continue
        clauses = sorted(defines.get(p['name'], ()), key=clause_key)
        if not clauses:
            unmapped.append(p['name'])
            continue
        ids = [f'css21.{c}' for c in clauses]
        p['classification'] = 'user-agent'
        p['rationale'] = ('Normative property definition reviewed against the pinned Recommendation record '
                          '(values, initial, applies-to, inheritance, percentage basis); decomposed to obligation '
                          f"{', '.join(ids)}.")
        p['requirementIds'] = ids
    if unmapped:
        raise SystemExit(f'properties without a defining section: {unmapped}')

    sections_doc['reviewComplete'] = True
    properties_doc['reviewComplete'] = True

    for path, doc in ((sections_path, sections_doc), (properties_path, properties_doc),
                      (requirements_path, requirements_doc)):
        with open(path, 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(json.dumps(doc, indent=2, ensure_ascii=False) + '\n')

    media_print = sum(1 for o in obligations if o['media'] == ['print'])
    print(f'sections: {len(sections)} (user-agent {agent_n}, informative {informative_n}, optional {optional_n})')
    print(f'obligations: {len(obligations)} new + {len(seeds)} ua seeds (print-only: {media_print})')
    print(f'properties: {len(properties)} reviewed')
    return 0


def make_obligation(clause: str, title: str, url: str, media: list[str]) -> dict:
    assert url.startswith(TARGET), f'obligation url outside the pinned target: {url}'
    return {'id': f'css21.{clause}', 'url': url, 'title': title,
            'media': media, 'status': 'untested', 'tests': []}


def clause_key(clause: str) -> list:
    parts = re.split(r'(\d+)', clause)
    return [int(p) if p.isdigit() else p for p in parts]


if __name__ == '__main__':
    raise SystemExit(main())
