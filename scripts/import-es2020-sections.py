"""Import the section hierarchy of the fixed ECMA-262 11th edition snapshot.

This imports an inventory, not a claim that every obligation has been reviewed.
Usage: python scripts/import-es2020-sections.py [downloaded-spec.html]
"""
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import sys
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
URL = "https://262.ecma-international.org/11.0/"


class Sections(HTMLParser):
    def __init__(self):
        super().__init__()
        self.sections = []
        self.stack = []
        self.heading = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag in ("emu-clause", "emu-annex", "emu-intro"):
            item = dict(id=attrs.get("id"), parent=self.stack[-1]["id"] if self.stack else None,
                        title="", kind=tag, reviewed=False, additionalTests=[])
            self.sections.append(item)
            self.stack.append(item)
        elif tag == "h1" and self.stack and not self.stack[-1]["title"]:
            self.heading = self.stack[-1]

    def handle_data(self, text):
        if self.heading is not None:
            self.heading["title"] += text

    def handle_endtag(self, tag):
        if tag == "h1":
            self.heading = None
        elif tag in ("emu-clause", "emu-annex", "emu-intro") and self.stack:
            self.stack.pop()


data = Path(sys.argv[1]).read_bytes() if len(sys.argv) > 1 else urllib.request.urlopen(URL).read()
parser = Sections()
parser.feed(data.decode("utf-8"))
for section in parser.sections:
    section["title"] = " ".join(section["title"].split())
    section["url"] = URL + "#" + section["id"]
target = ROOT / "Lite.Conformance/Test262/es2020-sections.json"
target.write_text(json.dumps(dict(specification=URL, sourceSha256=hashlib.sha256(data).hexdigest(),
                                 sections=parser.sections), indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Imported {len(parser.sections)} sections to {target}")
