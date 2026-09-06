"""Refresh the fixed draft's section index without treating headings as requirements.

Usage: python scripts/import-html53-sections.py [--source downloaded-index.html]
Existing reviews survive only when the section number, URL and title still match.
The source is the W3C HTML 5.3 draft, distributed under its permissive document license.
"""
import argparse
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
from urllib.request import urlopen
from urllib.parse import urljoin

TARGET = "https://www.w3.org/TR/2018/WD-html53-20181018/"
DESTINATION = Path(__file__).resolve().parents[1] / "Lite.Conformance/Profile/html53-sections.json"


class TableOfContents(HTMLParser):
    def __init__(self):
        super().__init__()
        self.in_toc = False
        self.parts = None
        self.href = ""
        self.sections = []

    def handle_starttag(self, tag, attributes):
        attributes = dict(attributes)
        if tag == "nav" and attributes.get("id") == "toc":
            self.in_toc = True
        if self.in_toc and tag == "a":
            self.parts = []
            self.href = attributes.get("href", "")

    def handle_data(self, data):
        if self.parts is not None:
            self.parts.append(data)

    def handle_endtag(self, tag):
        if tag == "a" and self.parts is not None:
            label = " ".join("".join(self.parts).split())
            match = re.fullmatch(r"(\d+(?:\.\d+)*) (.+)", label)
            if match:
                self.sections.append(dict(clause=match[1], title=match[2], url=urljoin(TARGET, self.href)))
            self.parts = None
        if tag == "nav":
            self.in_toc = False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path)
    args = parser.parse_args()
    if args.source:
        source = args.source.read_bytes()
    else:
        with urlopen(TARGET, timeout=60) as response:
            source = response.read()
    toc = TableOfContents()
    toc.feed(source.decode("utf-8"))
    sections = toc.sections
    if len(sections) < 500 or len({s["clause"] for s in sections}) != len(sections):
        raise ValueError("Missing, duplicate or unexpectedly short fixed-draft section index")
    index = "".join(f'{s["clause"]}\t{s["url"]}\t{s["title"]}\n' for s in sections)
    old = json.loads(DESTINATION.read_text(encoding="utf-8")) if DESTINATION.exists() else {}
    previous = {s["clause"]: s for s in old.get("sections", [])}
    for section in sections:
        review = previous.get(section["clause"], {})
        if not all(review.get(key) == section[key] for key in ("clause", "url", "title")):
            review = {}
        section.update(classification=review.get("classification", "unreviewed"),
                       rationale=review.get("rationale", ""),
                       requirementIds=review.get("requirementIds", []))
    result = dict(schemaVersion=1, target=TARGET,
                  sourceSha256=hashlib.sha256(source).hexdigest(),
                  sectionIndexSha256=hashlib.sha256(index.encode("utf-8")).hexdigest(),
                  reviewComplete=False, sections=sections)
    DESTINATION.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f'Indexed {len(sections)} sections; index SHA256 {result["sectionIndexSha256"]}')


if __name__ == "__main__":
    main()
