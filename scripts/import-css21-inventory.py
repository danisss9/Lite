"""Import the pinned CSS 2.1 section/property indexes without claiming obligation review.

The checksummed W3C archive is cached under the ignored vendor directory. Existing
reviews survive only when the corresponding source record has not changed.
"""
import argparse
import hashlib
from html.parser import HTMLParser
import io
import json
from pathlib import Path
import re
from urllib.request import urlopen
from urllib.parse import urljoin
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
TARGET = "https://www.w3.org/TR/2011/REC-CSS2-20110607/"
ARCHIVE_SHA256 = "ef072758dbf8618d2f6fe2fc60afd7cf4a73dc3b566ca39f06f4a6a297dbea60"
DESTINATION = ROOT / "Lite.Conformance/Profile"


def normalized(parts):
    return " ".join("".join(parts).split())


class Contents(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = None
        self.href = ""
        self.sections = {}

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "a" and "tocxref" in attrs.get("class", "").split():
            self.parts = []
            self.href = attrs["href"]

    def handle_data(self, text):
        if self.parts is not None:
            self.parts.append(text)

    def handle_endtag(self, tag):
        if tag != "a" or self.parts is None:
            return
        label = normalized(self.parts)
        match = re.fullmatch(r"(?:Appendix )?([0-9]+(?:\.[0-9]+)*|[A-Z](?:\.[0-9]+)*)\.?\s+(.+)", label)
        if match:
            record = dict(clause=match[1], title=match[2], url=urljoin(TARGET, self.href))
            old = self.sections.get(match[1])
            if old and old != record:
                raise ValueError(f"Conflicting section {match[1]}")
            self.sections[match[1]] = record
        self.parts = None


class Properties(HTMLParser):
    def __init__(self):
        super().__init__()
        self.cells = []
        self.links = []
        self.rows = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "tr":
            self.finish_row()
        elif tag == "td":
            self.cells.append([])
        elif tag == "a" and len(self.cells) == 1:
            href = attrs.get("href", "")
            if "#propdef-" in href:
                self.links.append(href)

    def handle_data(self, text):
        if self.cells:
            self.cells[-1].append(text)

    def handle_endtag(self, tag):
        if tag in ("tr", "table"):
            self.finish_row()

    def finish_row(self):
        if self.links:
            if len(self.cells) != 7:
                raise ValueError("Unexpected property table layout")
            values = [normalized(c) for c in self.cells]
            for link in self.links:
                self.rows.append(dict(name=link.split("#propdef-")[1], url=urljoin(TARGET, link),
                    values=values[1], initial=values[2], appliesTo=values[3] or "all",
                    inherited=values[4] == "yes", percentages=values[5] or "N/A", mediaGroups=values[6]))
        self.cells, self.links = [], []


def merge_reviews(filename, records, key, fields):
    path = DESTINATION / filename
    previous = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    old = {r[key]: r for r in previous.get(fields, [])}
    for record in records:
        review = old.get(record[key], {})
        if any(review.get(k) != v for k, v in record.items()):
            review = {}
        record.update(classification=review.get("classification", "unreviewed"),
                      rationale=review.get("rationale", ""), requirementIds=review.get("requirementIds", []))
    return previous


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, help="Use an offline copy of the pinned css2.zip")
    args = parser.parse_args()
    cached = ROOT / "Lite.Conformance/vendor/css21-spec-20110607/css2.zip"
    source = args.archive or cached
    if source.exists():
        data = source.read_bytes()
    elif args.archive:
        raise FileNotFoundError(source)
    else:
        with urlopen(TARGET + "css2.zip", timeout=45) as response:
            data = response.read()
    if hashlib.sha256(data).hexdigest() != ARCHIVE_SHA256:
        raise ValueError("CSS 2.1 archive checksum differs from the pinned Recommendation")
    cached.parent.mkdir(parents=True, exist_ok=True)
    cached.write_bytes(data)
    with ZipFile(io.BytesIO(data)) as archive:
        contents, properties = Contents(), Properties()
        contents.feed(archive.read("cover.html").decode("utf-8"))
        properties.feed(archive.read("propidx.html").decode("utf-8"))
        sections = list(contents.sections.values())
        props = sorted(properties.rows, key=lambda p: p["name"])
        if len(sections) < 200 or len(props) < 90 or len({p["name"] for p in props}) != len(props):
            raise ValueError("Incomplete or duplicate specification index")
        for filename, records, field, key in [
            ("css21-sections.json", sections, "sections", "clause"),
            ("css21-properties.json", props, "properties", "name"),
        ]:
            # Fingerprint source metadata separately from mutable review annotations.
            canonical = "".join("\t".join(str(v).lower() if isinstance(v, bool) else str(v)
                for v in record.values()) + "\n" for record in records)
            digest = hashlib.sha256(canonical.encode()).hexdigest()
            previous = merge_reviews(filename, records, key, field)
            result = dict(schemaVersion=1, target=TARGET, archiveSha256=ARCHIVE_SHA256,
                indexSha256=digest, reviewComplete=previous.get("reviewComplete", False), **{field: records})
            if any(r["classification"] == "unreviewed" for r in records):
                result["reviewComplete"] = False
            (DESTINATION / filename).write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print(f"{filename}: {len(records)} records; index SHA256 {digest}")


if __name__ == "__main__":
    main()
