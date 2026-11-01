#!/usr/bin/env python3
"""Classify the stated pass condition of every self-describing CSS 2.1 case.

The selftest route's no-red scan proves only that no failure red (#ff0000)
painted. That is the WHOLE pass condition for a minority of cases ("Test passes
if there is no red."); most self-describing cases also state geometry, colour or
text conditions ("the blue box is in the upper-left corner of the black box")
the scan cannot see. This generator records, per case, the sentence and a
condition class, plus the WPT reftest twin (same file stem under css/CSS2) that
the runner's twin oracle uses when one exists:

- no-red:    the condition is solely the absence of red; the scan is sufficient.
- same-color: an embedded #test/#reference swatch pair ("same color as").
- compound-no-red: absence of red plus further visual conditions.
- visual:    visual conditions only (shape, position, size, text).
- manual:    needs interaction (WPT manual kind, the "interact" flag, or the
             sentence asks the reader to hover/click/focus/resize/scroll).
- unstated:  no pass sentence found; needs an individual review.

A "check" is attached where the whole visual sentence matches a template that
Css21/VisualChecks.cs evaluates against the render (one-shape, same-dimension,
bar-gap, line-style, look-same, solid-line, border-ring, border-sample,
text-color, text-decoration, align-right, shape-relation, space-between,
stack-three, two-size); unmatched sentences stay unchecked.

Cases covered: official html4 selftests (no reftest.list entry) and WPT
css/CSS2 visual/manual cases. Entries are generated proposals with
"reviewed": false; a reviewed entry (reviewed true) is never overwritten, so
manual corrections survive regeneration.

Run from the repo root after scripts/build-wpt-manifest.ps1:
    python -I scripts/gen_css21_pass_conditions.py
"""
import html
import json
import re
from pathlib import Path

GENERATED = '2026-11-02'
official_root = Path('Lite.Conformance/vendor/css21-official-20110323')
official_catalog = Path('Lite.Conformance/artifacts/css21-official-catalog.json')
wpt_root = Path('Lite.Conformance/vendor/wpt')
wpt_manifest = Path('Lite.Conformance/artifacts/wpt-manifest.json')
output = Path('Lite.Conformance/Css21/css21-pass-conditions.json')
OFFICIAL_PREFIX = 'css21-official/'

paragraph_re = re.compile(r'<p\b[^>]*>(.*?)</p>', re.I | re.S)
tag_re = re.compile(r'<[^>]+>')
comment_re = re.compile(r'<!--.*?-->', re.S)
flags_re = re.compile(r'<meta\s+name=["\']flags["\']\s+content=["\']([^"\']*)["\']', re.I)
passes_re = re.compile(r'\b(?:test\s+)?pass(?:es)?\s+if\b(.*)', re.I | re.S)
NO_RED_ONLY = re.compile(
    r'^(?:there\s+(?:is|should\s+be|must\s+be)\s+)?(?:absolutely\s+)?no\s+red'
    r'(?:\s+(?:visible|below|borders(?:\s+on\s+the\s+table\s+below)?|(?:on|in)\s+(?:the|this)\s+page))*'
    r'(?:,?\s+except\s+for\s+anti-?aliasing(?:\s+issues)?)?$')
# "(do not hover over it)" states a precondition of a visual check, not an interaction.
INTERACTION = re.compile(
    r"(?<!not\s)(?<!n't\s)\b(hover|mouse|click|focus|tab\s+key|press|select\s+the|resize|scroll|drag|zoom|print\s+preview|keyboard)\w*",
    re.I)
STATED = re.compile(r'\b(?:should|must)\b', re.I)


def pass_sentence(text: str) -> str | None:
    text = comment_re.sub('', text)
    for paragraph in paragraph_re.findall(text):
        plain = ' '.join(html.unescape(tag_re.sub(' ', paragraph)).split())
        if passes_re.search(plain) or STATED.search(plain):
            return plain
    return None


def classify(sentence: str | None, kind: str, flags: list[str]) -> str:
    if kind == 'manual' or 'interact' in flags:
        return 'manual'
    if sentence is None:
        return 'unstated'
    if INTERACTION.search(sentence):
        return 'manual'
    match = passes_re.search(sentence)
    clause = (match.group(1) if match else sentence).strip().lower()
    clause = ' '.join(clause.rstrip('.!').split())
    if NO_RED_ONLY.match(clause):
        return 'no-red'
    if re.search(r'\bsame colou?r\b', clause):
        return 'same-color'
    if re.search(r'\bno\s+red\b', clause):
        return 'compound-no-red'
    return 'visual'


COLOR = (r'(maroon|red|orange|yellow|olive|purple|fuchsia|white|lime|green|navy|blue|aqua|teal|black|silver|gr[ae]y)')
COLOR_NAMES = frozenset({'maroon', 'red', 'orange', 'yellow', 'olive', 'purple', 'fuchsia', 'white', 'lime',
                         'green', 'navy', 'blue', 'aqua', 'teal', 'black', 'silver', 'gray', 'grey'})
SHAPE = r'(box|square|rectangle|block|bar)'
NO_RED_CONJUNCT = re.compile(r'[,\s]+(?:and|with)\s+(?:there\s+is\s+)?no\s+red(?:\s+(?:visible|below|on\s+(?:the|this)\s+page))*$', re.I)
ONE_SHAPE = re.compile(
    rf'^there\s+(?:is|should\s+be)\s+(?:an?|only\s+one)\s+(?:{COLOR}\s+)?(?:{SHAPE})'
    r'(?:\s+(?:below(?:\s+this\s+line)?|visible|on\s+the\s+page))?$')
ONE_SHAPE_PREDICATE = re.compile(
    rf'^the\s+{SHAPE}\s+below\s+(?:is|should\s+be)\s+(?:a\s+)?{COLOR}$')
SAME_DIMENSION = re.compile(
    rf'^(?:the|both)\s+(two\s+)?(?:{COLOR}\s+(?:and\s+{COLOR}\s+)?)?(?:boxes|bars|squares)\s+below\s+are\s+the\s+same\s+'
    r'(size|width|height)$')
LINE_STYLE = re.compile(
    rf'^(?:the\s+line\s+below\s+is\s+(dashed|dotted)'
    rf'|there\s+is\s+an?\s+(?:short\s+)?(?:(horizontal|vertical)\s+)?(?:{COLOR}\s+)?(dashed|dotted)\s+(?:{COLOR}\s+)?line(?:\s+below)?)$')
BAR_GAP = re.compile(r'^the\s+middle\s+(?:bar|block|line|box)\s+is\s+as\s+wide\s+as\s+the\s+space\s+between\s+the\s+'
                     r'(?:first\s+and\s+last\s+)?(?:bars|blocks|lines|boxes)$')
LOOK_SAME = re.compile(
    r'^the\s+following\s+two\s+(lines?|columns?|tables?)\s+should\s+(?:look\s+the\s+same|be\s+the\s+same|be\s+identical)[:]?$'
    r'|^the\s+two\s+(lines?|boxes|tables?|columns?)\s+(?:of\s+text\s+below\s+)?(?:below\s+)?look\s+(?:the\s+same|identical)$'
    r'|^the\s+two\s+(lines?|boxes|tables?|columns?)\s+below\s+(?:should\s+)?(?:look\s+the\s+same|are\s+identical|should\s+be\s+identical)[:.]?$'
    r'|^the\s+two\s+tables\s+should\s+be\s+identical[:]?$')
SOLID_LINE = re.compile(
    rf'^(?:the\s+line(?:\s+below)?\s+is\s+(?:solid\s+)?{COLOR}(?:\s+or\s+shades\s+of\s+{COLOR})?'
    rf'|there\s+is\s+a\s+(?:thin\s+|short\s+)?line(?:\s+below)?'
    rf'|there\s+is\s+a\s+(?:thin\s+|short\s+|solid\s+)?(?:horizontal\s+|vertical\s+)?{COLOR}\s+line(?:\s+below)?)$')
BORDER_RING = re.compile(
    rf'^(?:the\s+box\s+below\s+has\s+a\s+(dashed|dotted)\s+border'
    rf'|there\s+is\s+a\s+box\s+below\s+with\s+a\s+(dashed|dotted)(?:\s+{COLOR})?\s+border'
    rf'|there\s+is\s+a\s+(dashed|dotted)\s+lined\s+box(?:\s+below)?)$')
BORDER_SAMPLE = re.compile(
    r'^the\s+center\s+box\s+in\s+the\s+table\s+below\s+has\s+(?:a\s+border\s+similar\s+to\s+this:?|no\s+border)$')
TEXT_COLOR = re.compile(rf"^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+is\s+{COLOR}$")
TEXT_DECORATION = re.compile(
    r"^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+(does\s+not\s+have|has)\s+a\s+line\s+under\s+it$"
    r"|^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+has\s+a\s+line\s+over\s+it,\s+a\s+line\s+under\s+it\s+and\s+a\s+line\s+through\s+it$")
ALIGN_RIGHT = re.compile(r"^the\s+[\"']filler\s+text[\"'](?:\s+below)?\s+is\s+aligned\s+to\s+the\s+right\s+side\s+of\s+the\s+box$")
CENTERED_RIGHT = re.compile(
    rf'^the\s+{COLOR}\s+square\s+below\s+is\s+vertically\s+cente ?red\s+and\s+on\s+the\s+right\s+inner\s+edge\s+of\s+the\s+{COLOR}\s+(?:box|square)$')
SIZE_RELATION = re.compile(rf'^the\s+{COLOR}\s+box\s+is\s+(larger|smaller)\s+than\s+the\s+{COLOR}\s+box$')
BENEATH = re.compile(
    rf'^the\s+{COLOR}\s+box\s+is\s+directly\s+beneath\s+the\s+{COLOR}\s+box'
    rf'(?:\s+and\s+neither\s+box\s+is\s+touching\s+the\s+{COLOR}\s+line)?$')
HEIGHT_SIMILAR = re.compile(
    rf'^the\s+height\s+of\s+the\s+{COLOR}\s+box\s+is\s+similar\s+to\s+the\s+height\s+of\s+the\s+{COLOR}\s+boxes?'
    r'(?:\.\s*it\s+is\s+acceptable.*)?$')
NO_VERTICAL_GAP = re.compile(
    rf'^there\s+is\s+(?:a|an)\s+{COLOR}\s+and\s+(?:a|an)?\s*{COLOR}\s+square\s+below\s+and\s+there\s+is\s+no\s+vertical\s+gap\s+between\s+the\s+squares$')
SPACE_BETWEEN = re.compile(rf'^there\s+is\s+{COLOR}\s+space\s+between\s+the\s+boxes\s+below$')
STACK_THREE = re.compile(r'^the\s+pattern\s+below\s+should\s+be\s+a\s+stack\s+of\s+three\s+(?:perfect\s+)?rectangles$')
TWO_SIZE = re.compile(r'^there\s+is\s+a\s+(?:small\s+and\s+large|large\s+and\s+small)\s+box\s+on\s+the\s+page$')
OUTLINE_TEXT = re.compile(
    r"^there\s+is\s+a\s+(dashed|dotted)\s+outline\s+that\s+goes\s+around\s+the\s+following\s+[\"']sample\s+text[\"']"
    r'(?:\s+and\s+the\s+[\""]filler\s+text[\""]\s+below)?$')
UNIFORM_RING = re.compile(r'^there\s+is\s+a\s+box\s+below\s+with\s+four\s+sides\s+that\s+are\s+the\s+same\s+width')
LINE_PAIR = re.compile(
    rf'^(?:below\s+there\s+is\s+a\s+(vertical|horizontal)\s+solid\s+{COLOR}\s+line,\s+and\s+to\s+its\s+right\s+side,\s+a\s+(dotted|dashed)\s+{COLOR}\s+line'
    rf'|below\s+there\s+is\s+a\s+(vertical|horizontal)\s+solid\s+{COLOR}\s+line\s+above\s+a\s+(dotted|dashed)\s+{COLOR}\s+line'
    rf'|there\s+are\s+two\s+(dashed|dotted)(?:\s+{COLOR})?\s+lines\s+below)$')
MARKER_BULLET = re.compile(
    rf'^there\s+is\s+(?:a\s+filled\s+{COLOR}\s+square|a\s+(?:box|square)(?:\s+below)?)\s+and\s+there\s+is\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+(?:box|square)$'
    rf'|^there\s+is\s+a\s+(?:box|square)\s+below\s+and\s+there\s+is\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+(?:box|square)$')
LETTER_X = re.compile(r'^there\s+is\s+a\s+letter\s+[\""]x[\""]\s+below$')
BOX_ABOVE_X = re.compile(r'^there\s+is\s+a\s+box\s+above\s+the\s+letter\s+[\""]x[\""]\s+below$')
ROUND_DOT = re.compile(r'^there\s+is\s+a\s+single\s+round\s+dot\s+below$')
DOUBLE_LINE = re.compile(r'^there\s+is\s+a\s+double\s+line\s+below$')
NO_THROUGH = re.compile(r'^there\s+must\s+not\s+be\s+a\s+line\s+through\s+the\s+text\s+below$')
THIS_LINE_COLOR = re.compile(rf'^this\s+(?:line|link)\s+should\s+be\s+{COLOR}(?:\.?\s*\([^)]*\))?$')
APPROX_SAME = re.compile(r'^the\s+following\s+two\s+lines\s+should\s+look\s+approximately\s+the\s+same:?')
NOT_JAGGED = re.compile(r'^there\s+is\s+a\s+single\s+box\s+below\s+and\s+the\s+top\s+edge\s+is\s+not\s+jagged$')
SLANTED = re.compile(r'^there\s+is\s+a\s+box\s+below\s+that\s+appears\s+to\s+be\s+slanted\s+to\s+one\s+side$')

# --- Batch 2 (2 November): the next ~2,000 formerly-unsupported twin-less cases. ---
TO_PASS_PREFIX = re.compile(r'^to\s+pass,\s*', re.I)
TEXT_VISIBLE = re.compile(
    rf'^the\s+{COLOR}\s+text\s+is\s+visible'
    rf'(?:\.\s+it\s+can\s+be\s+partially\s+obscured\s+by\s+the\s+{COLOR}\s+text)?$')
TEXT_BASELINE = re.compile(
    r'^the\s+bottoms?\s+of\s+(?:the\s+)?(?:two\s+sets\s+of\s+)?["\']filler\s+text["\']\s+below\s+(?:is|are)\s+aligned$')
TEXT_BASELINE_TOP = re.compile(
    r'^the\s+["\']filler\s+text["\']\s+below\s+is\s+vertically\s+aligned\s+to\s+the\s+top\s+of\s+their\s+boxes$')
TEXT_ON_ONE_LINE = re.compile(
    r'^all\s+the\s+["\']filler\s+text["\']\s+below\s+is\s+on\s+the\s+same\s+line$'
    r'|^the\s+["\']filler\s+text["\']\s+below\s+is\s+all\s+on\s+the\s+same\s+line$')
OUTLINE_OVERLAP = re.compile(
    r'^there\s+is\s+a\s+(dashed|dotted)\s+outline\s+that\s+goes\s+around\s+the\s+following\s+["\']sample\s+text["\']'
    r'\s+and\s+the\s+["\']filler\s+text["\']\s+below\s+is\s+overlapped\s+by\s+the\s+outline$')
BORDER_EDGES_DIFFERENT = re.compile(
    r'^there\s+is\s+an\s+outline\s+of\s+a\s+box\s+with\s+each\s+edge\s+looking\s+different\s+from\s+the\s+other\s+edges$')
BORDER_RING_BOX = re.compile(
    rf'^there\s+(?:is\s+a\s+box\s+with\s+a\s+(dashed|dotted|double)(?:\s+{COLOR})?\s+border\s+below'
    rf'|are\s+two\s+boxes\s+with\s+(dashed|dotted|double)(?:\s+{COLOR})?\s+borders\s+below'
    rf'|is\s+a\s+single\s+(dashed|dotted|double)\s+lined\s+box\s+below)$')
FIRST_LINE_MARKER = re.compile(
    rf'^the\s+(?:text\s+below|first\s+line\s+of\s+text)\s+has\s+a\s+(?:{COLOR}\s+)?(?:square\s+)?bullet'
    rf'(?:\s+and\s+the\s+second\s+line\s+of\s+text\s+flows\s+below\s+that(?:\s+{COLOR})?\s+(?:square\s+)?bullet)?$')
MARKER_ITS = re.compile(
    rf'^there\s+is\s+(?:a\s+filled\s+{COLOR}\s+square|a\s+(?:box|square))(?:\s+below)?'
    r'\s+and\s+(?:there\s+is\s+a|a)\s+marker\s+bullet\s+on\s+its\s+left-hand\s+side$')
LINE_MARKER = re.compile(
    rf'^(?:there\s+is\s+(?:an?\s+)?(?:(dashed|dotted)\s+)?{COLOR}?\s*line\s+below'
    rf'|the\s+line\s+below\s+is(?:\s+(dashed|dotted))?(?:\s+{COLOR})?)'
    r'\s+and\s+there\s+is\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+line'
    r'(?:\s+\(note\s+the\s+marker\s+bullet\s+does\s+not\s+need\s+to\s+be\s+aligned\s+vertically\s+to\s+any\s+specific\s+position(?:\.?)\))?$')
BULLET_BESIDE = re.compile(
    rf'^a\s+bullet\s+\(filled-in\s+circle\)\s+must\s+(?:appear\s+)?to\s+the\s+(left|right)\s+of\s+a\s+(?:{COLOR}\s+)?square'
    rf'(?:,\s+and\s+the\s+bullet\s+probably\s+should\s+not\s+appear\s+above\s+the\s+black\s+line)?$'
    rf'|^there\s+must\s+be\s+a\s+bullet\s+\(filled-in\s+circle\)\s+above\s+a\s+{COLOR}\s+bar$'
    rf'|^there\s+must\s+be\s+a\s+{COLOR}\s+rectangle\s+below,\s+preceded\s+by\s+a\s+{COLOR}\s+bullet$')
BAR_STACK = re.compile(
    rf'^there\s+must\s+be\s+a\s+{COLOR}\s+bar\s+stacked\s+on\s+top\s+of\s+a\s+{COLOR}\s+bar$')
TEXT_MARKER = re.compile(
    rf'^there\s+is\s+a\s+number\s+[\'"]([\d-]+)[\'"]\s+below\s+and\s+there\s+is\s+a\s+ma[kr]er\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+number$')
NUMBERS_ARE = re.compile(
    r"^there\s+is\s+a\s+number\s+[\'\"](-?\d+)[\'\"]\s+below$"
    r"|^the\s+numbers?\s+[\'\"](-?\d+(?:\s+-?\d+)*)[\'\"](?:,\s*[\'\"](-?\d+)[\'\"])?(?:\s*(?:,\s*)?and\s*[\'\"](-?\d+)[\'\"])?"
    r"\s+(?:is|are|appear)\s+(?:vertically\s+)?below(?:\s+in\s+the\s+same\s+order)?$")
NUMBERS_ORDERED = re.compile(r'^the\s+numbers\s+below\s+are\s+ordered,\s+from\s+top\s+to\s+bottom,\s*(.+)$')
NUMBER_QUOTES = re.compile(r'[\'"](-?\d+)[\'"]')
WORDS_PRESENT = re.compile(
    r'^there\s+are\s+the\s+words\s+["\'](.+?)["\']\s+below$'
    r'|^the\s+words\s+["\'](.+?)["\']\s+appear\s+below$'
    r'|^the\s+(?:word|number)\s+["\']?(.+?)["\']?\s+appears\s+(?:in\s+the\s+box\s+)?below'
    r'(?:\s+\(followed\s+by\s+a\s+bullet\s+on\s+the\s+right\))?$'
    r'|^the\s+characters\s+["\'](.+?)["\']\s+appear(?:,\s+in\s+the\s+same\s+order)?,?\s+in\s+the\s+box\s+below$')
WORDS_AND_BOX = re.compile(
    rf'^the\s+words\s+["\'](.+?)["\']\s+appear\s+below\s+and\s+there\s+is\s+a\s+{COLOR}\s+box\s+below$')
TEXT_CENTERED = re.compile(
    r'^this\s+text\s+should\s+be\s+cente ?re ?d\s+horizontally\s+on\s+the\s+page$'
    r'|^["\']filler\s+text["\']\s+below\s+is\s+horizontally\s+cente ?re ?d\s+within\s+the\s+box$')
NOT_TOUCHING = re.compile(
    rf'^the\s+{COLOR}\s+and\s+{COLOR}\s+boxes\s+below\s+are\s+not\s+touching'
    r'(?:\s+and\s+there\s+is\s+no\s+red\s+visible\s+on\s+the\s+page)?$')
TOUCHING = re.compile(
    rf'^there\s+is\s+no\s+(?:space|white\s+space)\s+between\s+the\s+{COLOR}\s+(?:and\s+the\s+)?{COLOR}\s+'
    r'(?:boxes|box\s+below\s+and\s+the\s+lines)$')
CORNER = re.compile(
    rf'^the\s+(?:filled\s+)?{COLOR}\s+(?:box|square)\s+is\s+in\s+the\s+(lower-right|upper-left|upper-right|lower-left)\s+corner'
    rf'\s+of\s+the\s+(?:hollow\s+)?{COLOR}\s+(?:box|square)$')
RIGHT_OF = re.compile(rf'^the\s+{COLOR}\s+box\s+is\s+to\s+the\s+right\s+of\s+the\s+{COLOR}\s+box$')
LINES_SPACED = re.compile(
    rf'^there\s+is\s+space\s+between\s+the\s+{COLOR}\s+and\s+{COLOR}\s+lines(?:\s+below)?'
    r'(?:\s+and\s+there\s+is\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side\s+of\s+the\s+lines)?$')
LINES_IDENTICAL = re.compile(
    r'^(?:the\s+(?:lines|two\s+lines|three\s+lines|next\s+(?:2|two|three)\s+lines)\s+below'
    r'|these\s+lines|the\s+next\s+(?:2|two|three)\s+lines|the\s+following)'
    r'.*\bshould\s+be\s+(?:the\s+same|identical)\s*[:]?$'
    r'|^the\s+two\s+(?:lines|sentences|characters|rectangles|pictograms)\s+(?:of\s+["\']filler\s+text["\']\s+)?below\s+(?:should\s+|are\s+)?(?:look\s+)?(?:be\s+)?identical$'
    r'|^the\s+following\s+two\s+(?:pictograms|rectangles|lines)\s+should\s+(?:look\s+)?(?:be\s+)?identical$'
    r'|^the\s+two\s+lines\s+of\s+["\']filler\s+text["\']\s+below\s+look\s+the\s+same$'
    r'|^you\s+should\s+see\s+two\s+identical\s+colored\s+bands\s+below[:]?$')
IMAGE_TILED = re.compile(
    r'^there\s+is\s+a\s+line\s+of\s+cat\s+images(?:,\s+and\s+the\s+image\s+on\s+the\s+right\s+is\s+not\s+cut\s+off)?$'
    r'|^the\s+box\s+below\s+has\s+a\s+cat\s+image\s+repeated\s+across\s+the\s+screen,\s+but\s+not\s+down\s+the\s+screen'
    r'(?:\s+\(there\s+is\s+not\s+a\s+cat\s+on\s+top\s+of\s+a\s+cat\))?$'
    r'|^the\s+box\s+below\s+has\s+a\s+cat\s+image\s+tiled\s+throughout\s+it,\s+and\s+the\s+cat\s+image\s+is\s+not\s+cut\s+off\s+at\s+the\s+bottom'
    r'(?:\s+\(it\s+can\s+appear\s+cut\s+off\s+at\s+the\s+top\))?$'
    r'|^there\s+is\s+a\s+box\s+filled\s+with\s+cat\s+images\s+and\s+the\s+one\s+in\s+the\s+right\s+top\s+corner\s+is\s+not\s+cut\s+off$')
BARS_MATCH = re.compile(
    r'^the\s+colou?red\s+bars\s+on\s+the\s+left\s+should\s+match\s+the\s+colou?red\s+boxes\s+in\s+the\s+(?:black\s+)?box$')
BAR_GRID = re.compile(
    r'^there\s+is\s+a\s+solid\s+bar\s+on\s+top\s+of\s+a\s+(one|two|three|four|five)-by-\1\s+grid\s+of\s+boxes\s+below$')
VIEWPORT_BOTTOM = re.compile(
    rf'^this\s+{COLOR}\s+box\s+should\s+be\s+at\s+the\s+bottom\s+of\s+the\s+viewport(?:\s*/\s*first\s+page)?$')
CELL_BORDER = re.compile(
    rf'^there\s+is\s+a\s+(?:{COLOR}\s+)?border\s+(around|across\s+the\s+top\s+of)\s+the\s+middle\s+(cell|row|column)'
    r'\s+in\s+the\s+three-by-three\s+table\s+below$')
GAPS_EQUAL = re.compile(
    rf'^the\s+(vertical|horizontal)\s+space\s+between\s+the\s+two\s+{COLOR}\s+boxes\s+is\s+the\s+same\s+as\s+the\s+space\s+between\s+the\s+two\s+{COLOR}\s+boxes$')
GAP_BETWEEN = re.compile(
    rf'^there\s+is\s+a\s+{COLOR}\s+and\s+{COLOR}\s+square\s+below\s+and\s+there\s+is\s+a\s+(vertical|horizontal)\s+gap\s+between\s+the\s+squares$'
    rf'|^there\s+is\s+a\s+{COLOR}\s+and\s+{COLOR}\s+square\s+below\s+and\s+there\s+is\s+no\s+horizontal\s+gap\s+between\s+the\s+squares$')
CORNERS = re.compile(
    rf'^there\s+is\s+a\s+{COLOR}\s+square\s+at\s+each\s+corner\s+of\s+the\s+{COLOR}\s+(?:square|box)$')
BOX_COUNT = re.compile(
    rf'^(?:there\s+(?:is|are)\s+)?(?:a\s+single\s+|one\s+|only\s+one\s+|only\s+|two\s+|three\s+|four\s+)'
    rf'(?:{COLOR}\s+)?(separate\s+)?(box|boxes|square|squares|rectangles?)\s+'
    r'(?:appears?\s+)?(?:below(?:\s+this\s+line)?|on\s+the\s+page)$')
SAME_SIZE = re.compile(
    r'^there\s+are\s+two\s+boxes\s+on\s+the\s+page\s+and\s+they\s+are\s+the\s+same\s+height$'
    r'|^the\s+two\s+dots\s+\(boxes\)\s+below\s+are\s+the\s+same\s+size$'
    rf'|^the\s+{COLOR}\s+box\s+is\s+the\s+same\s+size\s+as\s+the\s+{COLOR}\s+box$'
    r'|^the\s+following\s+four\s+boxes\s+are\s+the\s+same\s+size$')
SHADE_BOX = re.compile(rf'^the\s+(?:box|line)\s+(?:below\s+)?is\s+a\s+shade\s+or\s+shades\s+of\s+{COLOR}$')
STACK_EVEN = re.compile(
    r'^there\s+are\s+three\s+evenly-spaced\s+(?:{COLOR}\s+)?boxes\s+stacked\s+on\s+the\s+page$')
THREE_BARS = re.compile(
    rf'^there\s+should\s+be\s+two\s+{COLOR}\s+bars\s+below\s+\(with\s+a\s+{COLOR}\s+one\s+in\s+between\)$')
FIRST_LETTER = re.compile(
    rf'^the\s+first\s+letter\s+of\s+this\s+paragraph(?:,\s+and\s+only\s+that\s+one,)?\s+should\s+be\s+{COLOR}'
    r'(?:\s+\.\s+if\s+this\s+precise\s+combination.*)?$')
FIRST_LINE_COLOR = re.compile(
    rf'^the\s+first\s+line\s+of\s+the\s+text\s+below\s+is\s+{COLOR},\s+but\s+the\s+other\s+lines\s+are\s+not$')
JUSTIFY = re.compile(
    r'^the\s+text\s+in\s+the\s+box\s+below\s+should\s+be\s+justified[:]\s+the\s+text\s+should\s+line\s+up\s+with\s+the\s+box\s+edges'
    r'\s+on\s+both\s+the\s+left\s+and\s+right\s+sides$'
    r'|^the\s+text\s+below\s+should\s+be\s+justified\s+to\s+fit\s+in\s+the\s+box\s+\(straight\s+edges\s+on\s+all\s+sides\)$')
TEXT_BAND_ABOVE = re.compile(
    rf'^this\s+paragraph\s+should\s+have\s+(one|two|three)\s+{COLOR}\s+lines\s+directly\s+(above|below|on\s+its\s+left|on\s+its\s+right)[.]?$')
TEXT_BAND_WORD = re.compile(
    rf'^there\s+should\s+be\s+a\s+solid\s+{COLOR}\s+line\s+directly\s+(over|under|to\s+the\s+left\s+of|to\s+the\s+right\s+of)'
    r'\s+every\s+occur[a-z]+ce\s+of\s+the\s+word\s+["\']\s*(.+?)\s*["\'][.]?$')
EDGE_LETTER = re.compile(
    rf'^the\s+(rightmost|leftmost)\s+letter\s+\S+\s+in\s+the\s+line\s+below\s+should\s+be\s+{COLOR}$'
    rf'|^the\s+(\S+)\s+below\s+should\s+be\s+{COLOR}$')
TEXT_DECORATION_EXTENT = re.compile(
    r'^the\s+["\']filler\s+text["\']\s+below\s+has\s+a\s+line\s+(above|below|through|under)\s+it'
    r'(?:\s+which\s+starts\s+at\s+the\s+beginning\s+of\s+the\s+text\s+and\s+continues\s+until\s+the\s+end'
    r'(?:\.\s+the\s+line\s+also\s+cannot\s+extend\s+beyond\s+the\s+text)?)?'
    r'(?:,\s+spaces\s+as\s+well\s+as\s+characters)?$')
TEXT_DECORATION_PAIR = re.compile(
    r'^the\s+["\']filler\s+text["\']\s+below\s+has\s+a\s+line\s+(over|under|through)\s+it\s+and\s+a\s+line\s+(over|under|through)\s+it$')
STACK_ORDER = re.compile(
    rf'^the\s+boxes\s+are\s+partially\s+stacked\s+on\s+top\s+of\s+each\s+other,\s+and\s+they\s+are\s+stacked\s+in\s+the\s+following\s+order\s+from\s+bottom\s+to\s+top:\s+{COLOR},\s+{COLOR},\s+{COLOR}$')
HALF_HEIGHT = re.compile(
    rf'^the\s+{COLOR}\s+and\s+{COLOR}\s+boxes\s+below\s+are\s+half\s+the\s+height\s+of\s+the\s+{COLOR}\s+box$')
NO_RING = re.compile(
    r'^there\s+are\s+no\s+borders\s+around\s+the\s+text\s+below$'
    r'|^the\s+["\']filler\s+text["\']\s+below\s+has\s+no\s+box\s+around\s+it$'
    r'|^the\s+["\']filler\s+text["\']\s+has\s+no\s+box\s+around\s+it$'
    r'|^there\s+are\s+no\s+lines\s+or\s+boxes\s+around\s+the\s+["\']filler\s+text["\']\s+below$')
TEXT_BELOW_BOX = re.compile(
    r'^the\s+["\']filler\s+text["\']\s+appears\s+(below|above)\s+the\s+box$')
TEXT_MARKER_MATCH = re.compile(
    rf'^the\s+lines\s+of\s+["\']filler\s+text["\']\s+below\s+match\s+styles\s+and\s+the\s+bottom\s+["\']filler\s+text["\']\s+has\s+a\s+marker\s+bullet\s+on\s+the\s+left-hand\s+side$')
TEXT_ALL_COLOR = re.compile(
    rf'^the\s+following\s+lines\s+should\s+all\s+be\s+{COLOR}\s+\(assuming[^)]*\)?$'
    rf'|^all\s+the\s+characters\s+below\s+are\s+{COLOR}$')

# Per-side border templates (border-color-*, border-style-*, border-style-shorthand-*): each
# pattern is whole-sentence and captures the side specs in stated order. A spec is a color, a
# style, or a style followed by a color ("dashed blue"); "single/double line" map to solid/double.
BORDER_SIDES_PATTERNS = [
    (re.compile(rf'^a\s+box(?:\s+below)?\s+with\s+top\s+and\s+bottom\s+borders\s+that\s+are\s+({COLOR})'
                rf'\s+and\s+left\s+and\s+right\s+borders\s+that\s+are\s+({COLOR})$'),
     [('top', 1, None), ('bottom', 1, None), ('left', 2, None), ('right', 2, None)]),
    (re.compile(rf'^a\s+box(?:\s+below)?\s+with\s+top\s+border\s+that\s+is\s+({COLOR})'
                rf',\s+left\s+and\s+right\s+borders\s+that\s+are\s+({COLOR})\s+and\s+a\s+bottom\s+border\s+that\s+is\s+({COLOR})$'),
     [('top', 1, None), ('left', 2, None), ('right', 2, None), ('bottom', 3, None)]),
    (re.compile(rf'^a\s+box(?:\s+below)?\s+with\s+top\s+border\s+that\s+is\s+({COLOR})'
                rf',\s+right\s+border\s+that\s+is\s+({COLOR}),\s+a\s+bottom\s+border\s+that\s+is\s+({COLOR})'
                rf'\s+and\s+a\s+left\s+border\s+that\s+is\s+({COLOR})$'),
     [('top', 1, None), ('right', 2, None), ('bottom', 3, None), ('left', 4, None)]),
    (re.compile(rf'^a\s+box\s+below\s+with\s+top\s+and\s+bottom\s+({COLOR})\s+borders\s+and\s+left\s+and\s+right\s+({COLOR})\s+borders$'),
     [('top', 1, None), ('bottom', 1, None), ('left', 2, None), ('right', 2, None)]),
    (re.compile(rf'^a\s+box\s+below\s+with\s+a\s+({COLOR})\s+top\s+border,\s+({COLOR})\s+left\s+and\s+right\s+borders'
                rf'\s+and\s+an?\s+({COLOR})\s+bottom\s+border$'),
     [('top', 1, None), ('left', 2, None), ('right', 2, None), ('bottom', 3, None)]),
    (re.compile(rf'^a\s+box\s+below\s+with\s+a\s+({COLOR})\s+top\s+border,\s+a\s+({COLOR})\s+right\s+border,'
                rf'\s+an?\s+({COLOR})\s+bottom\s+border\s+and\s+a\s+({COLOR})\s+left\s+border$'),
     [('top', 1, None), ('right', 2, None), ('bottom', 3, None), ('left', 4, None)]),
    (re.compile(rf'^the\s+top\s+and\s+bottom\s+borders\s+are\s+({COLOR})\s+and\s+the\s+left\s+and\s+right\s+borders\s+are\s+({COLOR})$'),
     [('top', 1, None), ('bottom', 1, None), ('left', 2, None), ('right', 2, None)]),
    (re.compile(rf'^the\s+top\s+border\s+is\s+({COLOR}),\s+the\s+bottom\s+border\s+is\s+({COLOR})'
                rf'\s+and\s+the\s+left\s+and\s+right\s+borders\s+are\s+({COLOR})$'),
     [('top', 1, None), ('bottom', 2, None), ('left', 3, None), ('right', 3, None)]),
    (re.compile(rf'^the\s+top\s+border\s+is\s+({COLOR}),\s+the\s+right\s+border\s+is\s+({COLOR}),\s+the\s+bottom\s+border\s+is\s+({COLOR})'
                rf',\s+and\s+the\s+left\s+border\s+is\s+({COLOR})$'),
     [('top', 1, None), ('right', 2, None), ('bottom', 3, None), ('left', 4, None)]),
    (re.compile(rf'^a\s+box\s+below\s+with\s+(dashed|dotted)\s+top\s+and\s+bottom\s+borders\s+and\s+(dashed|dotted)\s+left\s+and\s+right\s+borders$'),
     [('top', None, 1), ('bottom', None, 1), ('left', None, 2), ('right', None, 2)]),
    (re.compile(rf'^a\s+box\s+below\s+with\s+a\s+(dashed|dotted)\s+top\s+border,\s+left\s+and\s+right\s+borders\s+that\s+are\s+(dashed|dotted)'
                rf'\s+and\s+a\s+(double|single)\s+line\s+for\s+the\s+bottom\s+border$'),
     [('top', None, 1), ('left', None, 2), ('right', None, 2), ('bottom', None, 3)]),
    (re.compile(rf'^a\s+box\s+below\s+with\s+a\s+(dashed|dotted)\s+top\s+border,\s+a\s+(dashed|dotted)\s+right\s+border,'
                rf'\s+a\s+(double|single)\s+line\s+bottom\s+border\s+and\s+a\s+(double|single)\s+line\s+left\s+border$'),
     [('top', None, 1), ('right', None, 2), ('bottom', None, 3), ('left', None, 4)]),
    (re.compile(rf'^the\s+border\s+is\s+the\s+same\s+(dashed|dotted)\s+style\s+on\s+all\s+edges$'),
     [('top', None, 1), ('bottom', None, 1), ('left', None, 1), ('right', None, 1)]),
    (re.compile(rf'^the\s+top\s+and\s+bottom\s+borders\s+are\s+(dashed|dotted)\s+and\s+the\s+left\s+and\s+right\s+are\s+a\s+double\s+line$'),
     [('top', None, 1), ('bottom', None, 1), ('left', None, 'double'), ('right', None, 'double')]),
    (re.compile(rf'^the\s+top\s+border\s+is\s+(dashed|dotted),\s+the\s+left\s+and\s+right\s+borders\s+are\s+a\s+double\s+line,'
                rf'\s+and\s+the\s+bottom\s+border\s+is\s+(dashed|dotted)$'),
     [('top', None, 1), ('left', None, 'double'), ('right', None, 'double'), ('bottom', None, 2)]),
    (re.compile(rf'^the\s+top\s+border\s+is\s+(dashed|dotted),\s+the\s+right\s+border\s+is\s+a\s+double\s+line,'
                rf'\s+the\s+bottom\s+border\s+is\s+(dashed|dotted)\s+and\s+the\s+left\s+border\s+is\s+solid$'),
     [('top', None, 1), ('right', None, 'double'), ('bottom', None, 2), ('left', None, 'solid')]),
]


def border_sides_check(clause: str) -> dict | None:
    for pattern, mapping in BORDER_SIDES_PATTERNS:
        if m := pattern.match(clause):
            sides = {}
            for side, color_group, style_group in mapping:
                color = m.group(color_group) if isinstance(color_group, int) else color_group
                style = m.group(style_group) if isinstance(style_group, int) else style_group
                spec = {'color': color, 'shape': style}
                sides[side] = {k: v for k, v in spec.items() if v}
            return {'kind': 'border-sides', 'sides': sides}
    return None


def numbers_check(clause: str) -> dict | None:
    """Counter/list numbers whose presence is readable from the layout tree's text
    (content-generated text is layout text; list markers painted by the drawer are not,
    and those cases report unsupported, never a pass)."""
    if m := NUMBERS_ARE.match(clause):
        quoted = m.group(1) or m.group(2) or ''
        needles = quoted.split() if ' ' in quoted else [g for g in (m.group(1), m.group(2), m.group(3), m.group(4)) if g]
        if not needles:
            return None
        return {'kind': 'numbers-present', 'needles': needles,
                'ordered': 'vertically' in clause or 'same order' in clause}
    if m := NUMBERS_ORDERED.match(clause):
        needles = NUMBER_QUOTES.findall(m.group(1))
        if len(needles) < 2:
            return None
        return {'kind': 'numbers-present', 'needles': needles, 'ordered': True}
    return None



def visual_check(sentence: str | None) -> dict | None:
    """A machine check for the stated condition, when the whole sentence matches a known
    template (Css21/VisualChecks.cs evaluates it). Anything unmatched stays unchecked."""
    if not sentence:
        return None
    match = passes_re.search(sentence)
    if match:
        clause = ' '.join(match.group(1).strip().lower().rstrip('.!').split())
    else:
        # Some cases state the condition without "passes if": sentences that begin
        # "To pass, ..." or "This ..." (the test element speaks about itself).
        stripped = TO_PASS_PREFIX.sub('', sentence.strip())
        if re.match(r'^(?:the|there|both|this|you|only|a)\b', stripped, re.I):
            clause = ' '.join(stripped.lower().rstrip('.!').split())
        else:
            return None
    clause = NO_RED_CONJUNCT.sub('', clause)
    # Trailing "(Note the marker bullet ...)" parentheticals are meta-commentary, not
    # part of the stated condition.
    clause = re.sub(r'\s*\(\s*note\b[^)]*\)\s*$', '', clause).rstrip('. ')
    if m := ONE_SHAPE.match(clause):
        shape = 'box' if m.group(2) == 'block' else m.group(2)
        return {k: v for k, v in {'kind': 'one-shape', 'shape': shape, 'color': m.group(1)}.items() if v}
    if m := ONE_SHAPE_PREDICATE.match(clause):
        shape = 'box' if m.group(1) == 'block' else m.group(1)
        return {'kind': 'one-shape', 'shape': shape, 'color': m.group(2)}
    if m := SAME_DIMENSION.match(clause):
        count = 2 if m.group(1) or clause.startswith('both') else None
        return {k: v for k, v in {'kind': 'same-dimension', 'dimension': m.group(4), 'count': count}.items() if v}
    if m := LINE_STYLE.match(clause):
        style = m.group(1) or m.group(4)
        color = m.group(3) or m.group(5)
        return {k: v for k, v in {'kind': 'line-style', 'shape': style, 'color': color,
                                  'dimension': m.group(2)}.items() if v}
    if BAR_GAP.match(clause):
        return {'kind': 'bar-gap'}
    if m := LOOK_SAME.match(clause):
        return {'kind': 'look-same'}
    if m := SOLID_LINE.match(clause):
        return {k: v for k, v in {'kind': 'solid-line', 'color': m.group(1) or m.group(3)}.items() if v}
    if m := BORDER_RING.match(clause):
        style = m.group(1) or m.group(2) or m.group(3)
        color = m.group(4) if m.group(2) else None
        return {k: v for k, v in {'kind': 'border-ring', 'shape': style, 'color': color}.items() if v}
    if m := BORDER_SAMPLE.match(clause):
        return {'kind': 'border-sample', 'variant': 'none' if 'no border' in clause else 'similar'}
    if m := TEXT_COLOR.match(clause):
        return {'kind': 'text-color', 'color': m.group(1)}
    if m := TEXT_DECORATION.match(clause):
        if m.group(1):  # "does not have a line under it"
            return {'kind': 'text-decoration', 'under': False}
        return {'kind': 'text-decoration', 'over': True, 'under': True, 'through': True}
    if ALIGN_RIGHT.match(clause):
        return {'kind': 'align-right'}
    if m := CENTERED_RIGHT.match(clause):
        return {'kind': 'shape-relation', 'relation': 'centered-right', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := SIZE_RELATION.match(clause):
        return {'kind': 'shape-relation', 'relation': m.group(2), 'colorA': m.group(1), 'colorB': m.group(3)}
    if m := BENEATH.match(clause):
        check = {'kind': 'shape-relation', 'relation': 'beneath', 'colorA': m.group(1), 'colorB': m.group(2)}
        if m.group(3):
            check['lineColor'] = m.group(3)
        return check
    if m := HEIGHT_SIMILAR.match(clause):
        # Some variants add "it is acceptable to have the blue box height be shorter or
        # taller" — the clause cancels the comparison, so no machine check exists.
        if re.search(r'acceptable', clause):
            return None
        return {'kind': 'shape-relation', 'relation': 'height-similar', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := NO_VERTICAL_GAP.match(clause):
        return {'kind': 'shape-relation', 'relation': 'no-vertical-gap', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := SPACE_BETWEEN.match(clause):
        return {'kind': 'space-between', 'color': m.group(1)}
    if STACK_THREE.match(clause):
        return {'kind': 'stack-three'}
    if TWO_SIZE.match(clause):
        return {'kind': 'two-size'}
    if m := OUTLINE_TEXT.match(clause):
        return {'kind': 'outline-text', 'shape': m.group(1)}
    if UNIFORM_RING.match(clause):
        return {'kind': 'border-ring', 'shape': 'uniform'}
    if m := LINE_PAIR.match(clause):
        if m.group(1):
            return {'kind': 'line-pair', 'firstShape': 'solid', 'firstColor': m.group(2),
                    'secondShape': m.group(3), 'secondColor': m.group(4), 'arrangement': 'right-of' if m.group(1) == 'vertical' else 'below'}
        if m.group(5):
            return {'kind': 'line-pair', 'firstShape': 'solid', 'firstColor': m.group(6),
                    'secondShape': m.group(7), 'secondColor': m.group(8), 'arrangement': 'below'}
        return {'kind': 'line-pair', 'firstShape': m.group(9), 'secondShape': m.group(9),
                'secondColor': m.group(10), 'arrangement': 'either'}
    if m := MARKER_BULLET.match(clause):
        return {'kind': 'marker-bullet', 'color': m.group(1)}
    if LETTER_X.match(clause):
        return {'kind': 'letter-x'}
    if BOX_ABOVE_X.match(clause):
        return {'kind': 'letter-x', 'variant': 'box-above'}
    if ROUND_DOT.match(clause):
        return {'kind': 'round-dot'}
    if DOUBLE_LINE.match(clause):
        return {'kind': 'double-line'}
    if NO_THROUGH.match(clause):
        return {'kind': 'text-decoration', 'through': False}
    if m := THIS_LINE_COLOR.match(clause):
        return {'kind': 'text-color', 'color': m.group(1), 'anchor': 'instruction'}
    if APPROX_SAME.match(clause):
        return {'kind': 'look-same', 'approx': True}
    if NOT_JAGGED.match(clause):
        return {'kind': 'one-shape', 'shape': 'box'}
    if SLANTED.match(clause):
        return {'kind': 'slanted-box'}
    # --- Batch 2: the stated conditions of the next ~2,000 formerly-unsupported cases. ---
    if m := TEXT_VISIBLE.match(clause):
        return {'kind': 'text-color', 'color': m.group(1)}
    if m := TEXT_BASELINE.match(clause) or TEXT_BASELINE_TOP.match(clause):
        return {'kind': 'text-baseline', 'edge': 'top' if 'vertically aligned to the top' in clause else 'bottom'}
    if TEXT_ON_ONE_LINE.match(clause):
        return {'kind': 'one-line'}
    if BORDER_EDGES_DIFFERENT.match(clause):
        return {'kind': 'border-sides', 'mode': 'distinct'}
    if m := BORDER_RING_BOX.match(clause):
        styles = [g for g in m.groups() if g]
        style = next(s for s in styles if s in ('dashed', 'dotted', 'double'))
        color = next((g for g in m.groups() if g in COLOR_NAMES), None)
        count = 2 if clause.startswith('there are two') else 1
        return {'kind': 'border-ring', 'shape': style, 'color': color, 'count': count}
    if m := FIRST_LINE_MARKER.match(clause):
        return {k: v for k, v in {'kind': 'first-line-marker', 'color': m.group(1)}.items() if v}
    if MARKER_ITS.match(clause):
        return {k: v for k, v in {'kind': 'marker-bullet', 'color': MARKER_ITS.match(clause).group(1)}.items() if v}
    if m := LINE_MARKER.match(clause):
        return {k: v for k, v in {'kind': 'line-marker', 'shape': m.group(1) or m.group(3),
                                  'color': m.group(2) or m.group(4)}.items() if v}
    if m := BULLET_BESIDE.match(clause):
        if m.group(1):  # bullet to the left/right of a square
            return {'kind': 'bullet-beside', 'side': m.group(1)}
        if m.group(3):  # bullet above a bar
            return {'kind': 'bullet-beside', 'side': 'above', 'color': m.group(3)}
        return {'kind': 'marker-bullet', 'color': m.group(4), 'bulletColor': m.group(5)}
    if m := BAR_STACK.match(clause):
        return {'kind': 'bar-stack', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := TEXT_MARKER.match(clause):
        return {'kind': 'text-marker', 'needle': m.group(1)}
    if check := numbers_check(clause):
        return check
    if m := WORDS_PRESENT.match(clause):
        return {'kind': 'text-ink', 'needle': next(g for g in m.groups() if g),
                **({'bullet': 'right'} if 'followed by a bullet' in clause else {})}
    if m := WORDS_AND_BOX.match(clause):
        return {'kind': 'words-and-box', 'needle': m.group(1), 'color': m.group(2)}
    if TEXT_CENTERED.match(clause):
        return {'kind': 'align-center', 'anchor': 'instruction' if clause.startswith('this text') else None}
    if m := NOT_TOUCHING.match(clause):
        return {'kind': 'shape-relation', 'relation': 'not-touching', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := TOUCHING.match(clause):
        return {'kind': 'shape-relation', 'relation': 'touching', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := CORNER.match(clause):
        return {'kind': 'shape-relation', 'relation': f'corner-{m.group(2)}',
                'colorA': m.group(1), 'colorB': m.group(3)}
    if m := RIGHT_OF.match(clause):
        return {'kind': 'shape-relation', 'relation': 'right-of', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := LINES_SPACED.match(clause):
        return {'kind': 'lines-spaced', 'colorA': m.group(1), 'colorB': m.group(2),
                'marker': 'marker bullet' in clause}
    if m := LINES_IDENTICAL.match(clause):
        count = 3 if 'three' in clause else 2
        return {'kind': 'lines-same', 'count': count}
    if m := IMAGE_TILED.match(clause):
        variant = ('row-right' if 'not cut off' in clause and 'line of' in clause
                   else 'grid-topright' if 'right top corner' in clause
                   else 'grid-bottom' if 'tiled throughout' in clause
                   else 'row-only')
        return {'kind': 'image-tiled', 'variant': variant}
    if BARS_MATCH.match(clause):
        return {'kind': 'bars-match'}
    if m := BAR_GRID.match(clause):
        n = {'one': 1, 'two': 2, 'three': 3, 'four': 4, 'five': 5}[m.group(1)]
        return {'kind': 'bar-grid', 'rows': n, 'cols': n}
    if m := VIEWPORT_BOTTOM.match(clause):
        return {'kind': 'viewport-bottom', 'color': m.group(1)}
    if m := CELL_BORDER.match(clause):
        return {'kind': 'cell-border', 'color': m.group(1),
                'edge': 'top' if 'across the top' in m.group(2) else 'all',
                'target': m.group(3)}
    if m := GAPS_EQUAL.match(clause):
        return {'kind': 'shape-relation', 'relation': 'gaps-equal', 'dimension': m.group(1),
                'colorA': m.group(2), 'colorB': m.group(3)}
    if m := GAP_BETWEEN.match(clause):
        if 'no horizontal gap' in clause:
            return {'kind': 'shape-relation', 'relation': 'no-horizontal-gap', 'colorA': m.group(3), 'colorB': m.group(4)}
        return {'kind': 'shape-relation', 'relation': f'{"vertical" if m.group(3) == "vertical" else "horizontal"}-gap',
                'colorA': m.group(1), 'colorB': m.group(2)}
    if m := CORNERS.match(clause):
        return {'kind': 'corners', 'color': m.group(1), 'boxColor': m.group(2)}
    if m := BOX_COUNT.match(clause):
        body = re.sub(r'^there\s+(?:is|are)\s+', '', clause)
        head = re.match(r'^(a\s+single|only\s+one|only\s+|one|two|three|four)\b', body)
        count = {'a single': 1, 'only one': 1, 'only ': 1, 'one': 1, 'two': 2, 'three': 3, 'four': 4}.get(
            head.group(1) if head else None)
        return {k: v for k, v in {'kind': 'box-count', 'count': count, 'color': m.group(1)}.items() if v}
    if m := SAME_SIZE.match(clause):
        if 'same height' in clause:
            return {'kind': 'same-dimension', 'dimension': 'height', 'count': 2}
        if m.group(1):
            return {'kind': 'same-dimension', 'dimension': 'size', 'count': 2,
                    'colorA': m.group(1), 'colorB': m.group(2)}
        return {'kind': 'same-dimension', 'dimension': 'size', 'count': 4 if 'four' in clause else 2}
    if m := SHADE_BOX.match(clause):
        return {'kind': 'one-shape' if clause.startswith('the box') else 'solid-line',
                'color': m.group(1), 'shades': True}
    if STACK_EVEN.match(clause):
        return {'kind': 'stack-three', 'even': True}
    if m := THREE_BARS.match(clause):
        return {'kind': 'three-bars', 'colorA': m.group(1), 'colorB': m.group(2)}
    if m := FIRST_LETTER.match(clause):
        return {'kind': 'first-letter-color', 'color': m.group(1)}
    if m := FIRST_LINE_COLOR.match(clause):
        return {'kind': 'first-line-color', 'color': m.group(1)}
    if JUSTIFY.match(clause):
        return {'kind': 'justify'}
    if m := TEXT_BAND_ABOVE.match(clause):
        return {'kind': 'text-band', 'count': {'one': 1, 'two': 2, 'three': 3}[m.group(1)],
                'color': m.group(2), 'side': {'above': 'above', 'below': 'below',
                                              'on its left': 'left', 'on its right': 'right'}[m.group(3)],
                'anchor': 'instruction'}
    if m := TEXT_BAND_WORD.match(clause):
        return {'kind': 'text-band', 'count': None, 'color': m.group(1),
                'side': {'over': 'above', 'under': 'below', 'to the left of': 'left',
                         'to the right of': 'right'}[m.group(2)],
                'needle': m.group(3)}
    if m := EDGE_LETTER.match(clause):
        return {k: v for k, v in {'kind': 'edge-letter', 'side': m.group(1),
                                  'needle': m.group(3), 'color': m.group(2) or m.group(4)}.items() if v}
    if m := TEXT_DECORATION_EXTENT.match(clause):
        span = ('text' if 'cannot extend beyond the text' in clause
                else 'beyond' if 'spaces as well as characters' in clause else None)
        check = {'kind': 'text-decoration',
                 {'above': 'over', 'below': 'under', 'under': 'under', 'through': 'through'}[m.group(1)]: True}
        if span:
            check['span'] = span
        return check
    if m := TEXT_DECORATION_PAIR.match(clause):
        first, second = m.group(1), m.group(2)
        return {'kind': 'text-decoration',
                **{name: True for name in ({'over': 'over', 'under': 'under', 'through': 'through'}[first],
                                           {'over': 'over', 'under': 'under', 'through': 'through'}[second])}}
    if check := border_sides_check(clause):
        return check
    if m := STACK_ORDER.match(clause):
        return {'kind': 'stack-order', 'colors': [m.group(1), m.group(2), m.group(3)]}
    if m := HALF_HEIGHT.match(clause):
        return {'kind': 'shape-relation', 'relation': 'half-height',
                'colorA': m.group(1), 'colorB': m.group(2), 'colorC': m.group(3)}
    if NO_RING.match(clause):
        return {'kind': 'no-ring', 'needle': None if 'the text below' in clause else 'filler text'}
    if m := TEXT_BELOW_BOX.match(clause):
        return {'kind': 'text-below-box', 'side': m.group(1)}
    if TEXT_MARKER_MATCH.match(clause):
        return {'kind': 'text-marker-match'}
    if m := TEXT_ALL_COLOR.match(clause):
        return {'kind': 'text-color', 'color': m.group(1), 'scope': 'following'}
    return None


def walk(tree: dict, prefix: str, kind: str, into: list) -> None:
    for name, node in tree.items():
        path = prefix + name
        if isinstance(node, dict):
            walk(node, path + '/', kind, into)
        elif path.startswith('css/CSS2/'):
            into.append((kind, path))


def read(path: Path) -> str:
    try:
        return path.read_text(encoding='utf-8', errors='replace')
    except OSError:
        return ''


def main() -> None:
    manifest = json.loads(wpt_manifest.read_text(encoding='utf-8'))
    wpt_cases: list = []
    for kind in ('reftest', 'visual', 'manual'):
        walk(manifest['items'].get(kind, {}), '', kind, wpt_cases)
    reftest_by_stem: dict[str, list[str]] = {}
    for kind, path in wpt_cases:
        if kind == 'reftest':
            reftest_by_stem.setdefault(Path(path).stem, []).append(path)

    existing = {}
    if output.exists():
        existing = {e['path']: e for e in json.loads(output.read_text(encoding='utf-8-sig'))['cases']}

    entries = []

    def add(suite: str, path: str, file: Path, kind: str) -> None:
        if existing.get(path, {}).get('reviewed'):
            entries.append(existing[path])
            return
        text = read(file)
        flags = (flags_re.search(text).group(1).split() if flags_re.search(text) else [])
        sentence = pass_sentence(text)
        twins = sorted(reftest_by_stem.get(Path(path).stem, []))
        condition = classify(sentence, kind, flags)
        entry = {
            'path': path,
            'suite': suite,
            'condition': condition,
            'sentence': sentence,
            'twin': twins[0] if twins else None,
            'reviewed': False,
        }
        if condition in ('visual', 'compound-no-red') and (check := visual_check(sentence)):
            entry['check'] = check
        entries.append(entry)

    catalog = json.loads(official_catalog.read_text(encoding='utf-8-sig'))
    for case in catalog['cases']:
        if case['kind'] != 'selftest':
            continue
        add('css21-official', case['path'], official_root / case['path'][len(OFFICIAL_PREFIX):], 'selftest')
    for kind, path in wpt_cases:
        if kind in ('visual', 'manual'):
            add('css21-wpt', path, wpt_root / path, kind)

    entries.sort(key=lambda e: e['path'])
    output.write_text(json.dumps({
        'schemaVersion': 1,
        'generated': GENERATED,
        'description': 'Stated pass condition per self-describing CSS 2.1 case; see scripts/gen_css21_pass_conditions.py.',
        'cases': entries,
    }, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')

    summary: dict = {}
    for e in entries:
        key = (e['suite'], e['condition'], 'twin' if e['twin'] else 'no-twin')
        summary[key] = summary.get(key, 0) + 1
    for key in sorted(summary):
        print(f'{summary[key]:6}  {key[0]:15} {key[1]:16} {key[2]}')


if __name__ == '__main__':
    main()
