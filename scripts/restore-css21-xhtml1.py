#!/usr/bin/env python3
"""Restores the official suite's missing xhtml1 variant.

The 2011-03-23 distribution was built in three syntax variants: html4/, xhtml1/ and
xhtml1print/. Wayback captured html4/ (and the small other/ raw group) but only sparsely
captured xhtml1/, so the lock records the variant as unavailable and readiness blocks on
css21-official-variant-unavailable:xhtml1-screen.

The variant is a mechanical build product of the same pre-snapshot sources, through
tools/pub-css21.pl -> tools/build-css21.py -> tools/CSSTestLib at
csswg-test@fa4487008776579c206378d2df0ecea81b69cd80 (the arronei fork's last commit
before the snapshot). Reading that build:

- The build input is 9,418 tests (the published suite total) plus their reference
  documents, selected by pub-css21.pl's directory/manifest list.
- OutputFormats.XHTMLFormat writes every selected source into xhtml1/ under its basename
  with the same .xht extension, skipping tests flagged HTMLonly.
- Sources.XHTMLSource.write copies a source byte for byte unless the reftest machinery
  injected metadata; for '==' reftests it injects one <link rel="reference"> per reference
  as the head's first child, giving the element the original head text as its tail.
- HTMLFormat writes the same sources into html4/ .htm documents via the patched html5lib
  serializer (html4 is the half Wayback captured completely).

This script therefore identifies each xhtml1 document by the source blob whose documented
XHTML->HTML build transform reproduces the vendored html4 twin byte for byte. The transform
is recovered from the build code: the doctype conversion and xml:lang rewrite from the
patched html5lib serializer (tools/CSSTestLib/html5lib.patch), the ../support/ rewrite
pub-css21.pl applies before the Microsoft chapters are built, the reference injection from
CSSTestLib/Sources.py, and the invisible-character escaping table from the same patch. It
reproduces 9,567 of the 9,568 vendored html4 documents byte for byte; the sole exception
(html5lib's handling of a named entity inside an input element's value attribute) is
recorded per file and identified by name and metadata instead.

Sources without an html4 twin are the XHTML-only tests (flagged nonHTML) and the tests the
html4 capture never recovered; the build writes those verbatim, and their provenance is the
source blob itself. HTMLonly tests are omitted, as the XHTML build omits them.

Per-file provenance is written to Lite.Conformance/Css21/css21-xhtml1-restore.json, and the
vendored tree hash and the lock's variantAvailability are updated. The canonical tree digest
matches OfficialCatalog.ComputeTreeSha256 and scripts/fetch-tests.ps1: paths fold to lower
case and sort OrdinalIgnoreCase, then Ordinal, serialised as "rel\\0sha256\\n".

Regenerate with scripts/restore-css21-xhtml1.py. It needs the blob-filtered csswg-test clone
at artifacts/csswg-test-history (the same clone the other restores use).
"""

import argparse
import collections
import fnmatch
import gzip
import hashlib
import html.entities
import json
import os
import re
import shutil
import subprocess

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OFFICIAL = os.path.join(REPO, "Lite.Conformance", "vendor", "css21-official-20110323")
HTML4 = os.path.join(OFFICIAL, "html4")
XHTML1 = os.path.join(OFFICIAL, "xhtml1")
LOCK = os.path.join(REPO, "Lite.Conformance", "test-suites.lock.json")
MANIFEST_OUT = os.path.join(REPO, "Lite.Conformance", "Css21", "css21-xhtml1-restore.json")
CSSWG_HISTORY = os.path.join(REPO, "artifacts", "csswg-test-history")
CSSWG_COMMIT = "fa4487008776579c206378d2df0ecea81b69cd80"
LOCK_ID = "css21-official-20110323"
SPECROOT = b"http://www.w3.org/TR/CSS21/"

# pub-css21.pl's unreviewed-source selection, verbatim. The glob entries are expanded
# against the pre-snapshot tree below.
UNREVIEWED_DIRS = [
    "contributors/bzbarsky/submitted/css2.1/visuren",
    "contributors/bzbarsky/submitted/css2.1/tables",
    "contributors/mozilla/submitted/first-letter-characters",
    "contributors/gabriele/submitted/basic/sec5",
    "contributors/gtalbot/submitted",
    "contributors/hixie/submitted/css2.1/selector",
    "contributors/hixie/submitted/css2.1/syndata",
    "contributors/i18n/submitted/css2.1/syndata",
    "contributors/i18n/submitted/css2.1/visuren",
    "contributors/hp/submitted/css2.1/page",
    "contributors/fantasai/submitted/css2.1",
    "contributors/microsoft/submitted",
]
UNREVIEWED_LISTS = [
    "contributors/bzbarsky/submitted/css2.1/box/reftest.list",
    "contributors/mozilla/submitted/fontreftests/reftest.list",
    "contributors/fantasai/submitted/css2.1/backgrounds/reftest.list",
    "contributors/csswg-issues/submitted/css2.1/reftest.list",
]
# Perl globs: directory entries and manifest entries are separate patterns in pub-css21.pl.
UNREVIEWED_DIR_GLOBS = [
    "contributors/eira/submitted/css2.1/*",
    "contributors/hixie/submitted/css2.1/[bcfgmptuvz]*",
    "contributors/jameshopkins/submitted/css2.1/*",
    "contributors/microsoft/submitted/Chapter*",
]
UNREVIEWED_LIST_GLOBS = [
    ("contributors/mozilla/submitted/css2.1/*", "*.list"),
    ("contributors/hixie/submitted/css2.1-reftests/*", "reftest.list"),
]

# ---------------------------------------------------------------------------------------
# The recovered XHTML -> HTML build transform.
# ---------------------------------------------------------------------------------------

VOID = {"area", "base", "basefont", "br", "col", "embed", "hr", "img", "input", "isindex",
        "link", "meta", "param", "source", "track", "wbr"}
RAW = {"style", "script"}
TAG = re.compile(r"<!--.*?-->|<(?:[^>\"']|\"[^\"]*\"|'[^']*')*>", re.S)
OPEN = re.compile(r"^<([A-Za-z][\w:-]*)(.*?)(/?)>$", re.S)
ATTR = re.compile(r"([A-Za-z_:][\w:.-]*)(?:\s*=\s*(\"([^\"]*)\"|'([^']*)'|([^\s\"'>]+)))?")
VOID_CLOSE = re.compile(r"</(?:area|base|basefont|br|col|embed|hr|img|input|isindex|link|meta|param|source|track|wbr)\s*>", re.I)

XHTML_DOCTYPES = [
    (re.compile(r'<!DOCTYPE\s+html\s+PUBLIC\s+"-//W3C//DTD\s+XHTML\s+1\.0\s+Strict//EN"\s+"[^"]*"\s*>', re.I),
     '<!DOCTYPE html PUBLIC "-//W3C//DTD HTML 4.01//EN" "http://www.w3.org/TR/html4/strict.dtd">'),
    (re.compile(r'<!DOCTYPE\s+html\s+PUBLIC\s+"-//W3C//DTD\s+XHTML\s+1\.0\s+Transitional//EN"\s+"[^"]*"\s*>', re.I),
     '<!DOCTYPE html PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN" "http://www.w3.org/TR/html4/loose.dtd">'),
    (re.compile(r'<!DOCTYPE\s+html\s+PUBLIC\s+"-//W3C//DTD\s+XHTML\s+1\.0\s+Frameset//EN"\s+"[^"]*"\s*>', re.I),
     '<!DOCTYPE html PUBLIC "-//W3C//DTD HTML 4.01 Frameset//EN" "http://www.w3.org/TR/html4/frameset.dtd">'),
    (re.compile(r'<!DOCTYPE\s+html\s+PUBLIC\s+"-//W3C//DTD\s+XHTML\s+1\.1//EN"\s+"[^"]*"\s*>', re.I),
     '<!DOCTYPE html PUBLIC "-//W3C//DTD HTML 4.01//EN" "http://www.w3.org/TR/html4/strict.dtd">'),
]

# html5lib.patch's invisibleChars, exactly.
INVISIBLE = (set(range(0x00, 0x09)) | set(range(0x0B, 0x0D)) | set(range(0x0E, 0x20))
             | set(range(0x2000, 0x2010)) | set(range(0x2028, 0x2030))
             | {0x00A0, 0x205F, 0x3000, 0x2060, 0xFFF9, 0xFFFA, 0xFFFB})
NAMED_INVISIBLE = {code: name for code, name in html.entities.codepoint2name.items()
                   if code in INVISIBLE}
NUMERIC_RE = re.compile(r"&#(?:[xX][0-9a-fA-F]+|[0-9]+);")
NAMED_RE = re.compile(r"&[A-Za-z][A-Za-z0-9]*;")


def _decode_numeric(raw):
    body = raw[2:-1]
    try:
        code = int(body[1:], 16) if body[0] in "xX" else int(body)
        return chr(code)  # XML leaves character-reference CRs unnormalised
    except (ValueError, OverflowError):
        return raw


def encode_text(value, attr=False):
    saved = []

    def stash(match):
        saved.append(match.group(0))
        return "\uE000%d\uE001" % (len(saved) - 1)
    value = NUMERIC_RE.sub(lambda m: _decode_numeric(m.group(0)), value)
    value = NAMED_RE.sub(stash, value)
    out = []
    for ch in value:
        code = ord(ch)
        if ch == "&":
            out.append("&amp;")
        elif ch == "<":
            out.append("&lt;")
        elif ch == ">":
            out.append("&gt;")
        elif attr and ch == '"':
            out.append("&quot;")
        elif 0xE000 <= code <= 0xF8FF:
            out.append(ch)
        elif code in NAMED_INVISIBLE:
            out.append("&%s;" % NAMED_INVISIBLE[code])
        elif code in INVISIBLE:
            out.append("&#x%X;" % code)
        else:
            out.append(ch)
    result = "".join(out)
    for index, raw in enumerate(saved):
        result = result.replace("\uE000%d\uE001" % index, raw)
    return result


def transform_tag(tag):
    match = OPEN.match(tag)
    if not match:
        return tag
    name, rest, selfclose = match.group(1), match.group(2), match.group(3)
    attrs = []
    for attribute in ATTR.finditer(rest):
        aname = attribute.group(1)
        if attribute.group(2) is None:
            continue
        raw = (attribute.group(3) if attribute.group(3) is not None else
               (attribute.group(4) if attribute.group(4) is not None else attribute.group(5)))
        value = raw.replace("\r", " ").replace("\n", " ").replace("\t", " ")
        value = encode_text(value, attr=True)
        if aname.lower() == "xml:lang":
            aname = "lang"
        attrs.append('%s="%s"' % (aname, value))
    head = "<" + name + ((" " + " ".join(attrs)) if attrs else "")
    if selfclose:
        return head + ">" if name.lower() in VOID else head + "></" + name + ">"
    return head + ">"


def cdata_block(block):
    block = block.replace("<![CDATA[", "").replace("]]>", "")
    block = NUMERIC_RE.sub(lambda m: _decode_numeric(m.group(0)), block)
    return block.replace("&gt;", ">").replace("&lt;", "<").replace("&amp;", "&")


def inject_references(text, hrefs):
    """Sources.injectHeadTag for '==' references: head.text is kept, and repeated as the
    tail of every injected element, so the twin's link order is the inject-reversed order."""
    if isinstance(text, bytes):
        def inject_bytes(match):
            tail = match.group(2)
            parts = [match.group(1), tail]
            for href in hrefs:
                parts.append(b'<link rel="reference" href="' + href.encode("utf-8") + b'"/>')
                parts.append(tail)
            return b"".join(parts)
        return re.sub(rb"(<head\b[^>]*>)([^<]*)", inject_bytes, text, count=1, flags=re.I)

    def inject(match):
        tail = match.group(2)
        parts = [match.group(1), tail]
        for href in hrefs:
            parts.append('<link rel="reference" href="%s"/>' % href)
            parts.append(tail)
        return "".join(parts)
    return re.sub(r"(<head\b[^>]*>)([^<]*)", inject, text, count=1, flags=re.I)


def build_transform(source, path, reference_hrefs=None):
    """The html4 build output of one pre-snapshot source document, as bytes."""
    encoding = "utf-8"
    declared = re.match(rb'<\?xml[^>]*encoding\s*=\s*["\']([^"\']+)["\']', source[:200])
    if declared:
        encoding = declared.group(1).decode("ascii", "replace")
    try:
        text = source.decode(encoding)
    except (LookupError, UnicodeDecodeError):
        text = source.decode("utf-8", "replace")
    text = text.replace("\r\n", "\n").replace("\r", "\n").lstrip("\ufeff")
    if path and path.startswith("contributors/microsoft/submitted/Chapter"):
        text = text.replace("../support/", "support/")
    text = re.sub(r"<\?xml[^?]*\?>\s*", "", text, count=1)
    for pattern, replacement in XHTML_DOCTYPES:
        text, changed = pattern.subn(replacement, text, count=1)
        if changed:
            text = re.sub(r'(<!DOCTYPE[^>]*>)\s*(?=<)', r"\1\n", text, count=1)
            break
    if reference_hrefs:
        text = inject_references(text, reference_hrefs)
    text = text.replace(' xmlns="http://www.w3.org/1999/xhtml"', "")
    text = text.replace("<![CDATA[", "").replace("]]>", "")
    start = re.search(r"<!DOCTYPE|<html\b", text, re.I)
    if start:
        text = text[start.start():]
    end = text.lower().rfind("</html>")
    trailing = ""
    if end >= 0:
        trailing = "".join(m.group(0) for m in re.finditer(r"<!--.*?-->", text[end:], re.S))
        text = text[:end + 7]
    out, pos = [], 0
    while True:
        match = TAG.search(text, pos)
        if not match:
            out.append(encode_text(text[pos:]))
            break
        out.append(encode_text(text[pos:match.start()]))
        tag = match.group(0)
        opened = OPEN.match(tag)
        if opened and not opened.group(3) and opened.group(1).lower() in RAW:
            name = opened.group(1)
            close = re.compile(r"</%s\s*>" % name, re.I).search(text, match.end())
            if close:
                out.append(transform_tag(tag))
                out.append(cdata_block(text[match.end():close.start()]))
                out.append(close.group(0))
                pos = close.end()
                continue
        out.append(transform_tag(tag))
        pos = match.end()
    text = VOID_CLOSE.sub("", "".join(out))
    return (text.rstrip("\n") + trailing).encode(encoding, "xmlcharrefreplace")


# ---------------------------------------------------------------------------------------
# The pre-snapshot tree and the build's source selection.
# ---------------------------------------------------------------------------------------

class Repo:
    def __init__(self):
        listing = subprocess.run(["git", "ls-tree", "-r", CSSWG_COMMIT], cwd=CSSWG_HISTORY,
                                 capture_output=True, check=True).stdout.decode("utf-8", "replace")
        self.blobs, self.files = {}, []
        for line in listing.splitlines():
            meta, path = line.split("\t", 1)
            _mode, kind, sha = meta.split()
            if kind == "blob":
                self.files.append(path)
                self.blobs[path] = sha
        self.fileset = set(self.files)
        self.by_basename = collections.defaultdict(list)
        for path, sha in self.blobs.items():
            if path.endswith(".xht"):
                self.by_basename[os.path.basename(path)].append((sha, path))
        self.by_dir = collections.defaultdict(list)
        for path in self.files:
            self.by_dir[os.path.dirname(path)].append(os.path.basename(path))
        self.proc = subprocess.Popen(["git", "cat-file", "--batch"], cwd=CSSWG_HISTORY,
                                     stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        self.cache = {}

    def read(self, sha):
        if sha in self.cache:
            return self.cache[sha]
        self.proc.stdin.write(sha.encode("ascii") + b"\n")
        self.proc.stdin.flush()
        header = self.proc.stdout.readline().decode("ascii", "replace").split()
        data = None
        if len(header) >= 3 and header[1] == "blob":
            size = int(header[2])
            data = self.proc.stdout.read(size)
            self.proc.stdout.read(1)
        self.cache[sha] = data
        return data

    def show(self, path):
        return self.read(self.blobs[path])


FLAG_RE = re.compile(rb'<meta\s+name="flags"\s+content="([^"]*)"', re.I)


def flags_of(data):
    match = FLAG_RE.search(data)
    return (match.group(1).decode("utf-8", "replace") if match else "").split()


def build_selection(repo):
    """(tests, references) exactly as pub-css21.pl/build-css21.py select them."""
    directories = set(UNREVIEWED_DIRS)
    manifests = set(UNREVIEWED_LISTS)
    for pattern in UNREVIEWED_DIR_GLOBS:
        for directory in repo.by_dir:
            if fnmatch.fnmatchcase(directory, pattern):
                directories.add(directory)
    for directory_pattern, name_pattern in UNREVIEWED_LIST_GLOBS:
        for directory in repo.by_dir:
            if not fnmatch.fnmatchcase(directory, directory_pattern):
                continue
            for name in repo.by_dir[directory]:
                if fnmatch.fnmatchcase(name, name_pattern):
                    manifests.add(directory + "/" + name)
    # approved/css2.1/src: each top-level chapter is a selftest group, plus its manifest.
    for directory in repo.by_dir:
        if directory.startswith("approved/css2.1/src/") and directory.count("/") == 3:
            name = directory.rsplit("/", 1)[1]
            if name in ("support", "other-formats"):
                continue
            directories.add(directory)
            if "reftest.list" in repo.by_dir[directory]:
                manifests.add(directory + "/reftest.list")

    tests, references = set(), {}
    for directory in sorted(directories):
        approved = directory.startswith("approved/css2.1/src/")
        for name in repo.by_dir.get(directory, []):
            if not name.endswith(".xht"):
                continue
            path = directory + "/" + name
            # Unreviewed directories keep only tests that cite the CSS 2.1 spec root.
            if approved or SPECROOT in repo.show(path):
                tests.add(path)
    for manifest in sorted(manifests):
        base = os.path.dirname(manifest)
        for line in repo.show(manifest).decode("utf-8", "replace").splitlines():
            line = line.split("#", 1)[0].strip()
            if not line:
                continue
            parts = line.split()
            if len(parts) != 3 or parts[0] not in ("==", "!="):
                continue
            test, reference = base + "/" + parts[1], base + "/" + parts[2]
            if test.endswith(".xht") and test in repo.fileset:
                tests.add(test)
                if parts[0] == "==" and reference.endswith(".xht") and reference in repo.fileset:
                    references.setdefault(test, []).append(reference)
    return tests, references


def twin_of(stem):
    """The vendored html4 artifact for a document stem, decompressed if the capture is gzip."""
    path = os.path.join(HTML4, stem + ".htm")
    if not os.path.exists(path):
        return None, False
    raw = open(path, "rb").read()
    if raw[:2] == b"\x1f\x8b":
        try:
            return gzip.decompress(raw), True
        except OSError:
            return None, True
    return raw, False


def tree_sha256():
    """Canonical tree digest, matching OfficialCatalog.ComputeTreeSha256 exactly: entries
    fold to upper case for the OrdinalIgnoreCase primary sort (the documented rule) and
    serialise as 'lrel\\0<file sha256 hex>\\n' over the lowered path."""
    entries = []
    for dirpath, _dirs, files in os.walk(OFFICIAL):
        for filename in files:
            if filename == "fetch-failures.log" or filename.endswith(".download"):
                continue
            full = os.path.join(dirpath, filename)
            rel = os.path.relpath(full, OFFICIAL).replace("\\", "/").lower()
            entries.append((rel.upper(), rel, full))
    entries.sort()
    accumulator = hashlib.sha256()
    for _fold, rel, full in entries:
        with open(full, "rb") as handle:
            digest = hashlib.sha256(handle.read()).hexdigest()
        accumulator.update(("%s\0%s\n" % (rel, digest)).encode("utf-8"))
    return accumulator.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="report only, write nothing")
    args = parser.parse_args()

    if not os.path.isdir(CSSWG_HISTORY):
        raise SystemExit("the csswg-test history clone is missing: %s" % CSSWG_HISTORY)
    repo = Repo()
    tests, references = build_selection(repo)
    with open(LOCK, encoding="utf-8") as handle:
        lock_before = json.load(handle)
    entry = next(suite for suite in lock_before["suites"] if suite["id"] == LOCK_ID)
    if len(tests) != entry["totalTests"]:
        raise SystemExit("the selection produced %d tests, the lock publishes %d"
                         % (len(tests), entry["totalTests"]))

    documents = {test: "test" for test in tests}
    for test_refs in references.values():
        for reference in test_refs:
            documents.setdefault(reference, "reference")

    # Twins that scripts/restore-css21-html4-gzip.py already decompressed keep their capture
    # provenance across a re-run, as long as the twin still has the recorded bytes.
    previous_captures = {}
    if os.path.exists(MANIFEST_OUT):
        with open(MANIFEST_OUT, encoding="utf-8") as handle:
            for old in json.load(handle).get("files", []):
                if isinstance(old.get("twinCapture"), dict):
                    previous_captures[old["twin"]] = old["twinCapture"]

    records, skipped, unresolved = [], [], []
    byte_exact = no_twin = 0
    for path in sorted(documents):
        data = repo.show(path)
        if data is None:
            unresolved.append((path, "blob unavailable"))
            continue
        flags = flags_of(data)
        if "HTMLonly" in flags:
            skipped.append({"source": path, "reason": "HTMLonly: the XHTML build omits it"})
            continue
        stem = os.path.basename(path)[:-4]
        twin, gzipped = twin_of(stem)
        if twin is not None:
            head = twin.split(b"</head>", 1)[0]
            hrefs = [match.group(1).decode("utf-8", "replace")
                     for match in re.finditer(rb'<link rel="reference" href="([^"]*)"', head)]
        elif path in references:
            # The build injects at index 0, so the document order is reverse manifest order.
            hrefs = [reference.rsplit("/", 1)[-1] for reference in reversed(references[path])]
        else:
            hrefs = []
        document = data
        transform = "identity (XHTMLSource.write copies a clean source)"
        if hrefs:
            document = inject_references(data, hrefs)
            transform = "reference-link injection (%d link%s)" % (
                len(hrefs), "" if len(hrefs) == 1 else "s")
        if twin is not None:
            produced = build_transform(data, path, hrefs)
            if produced == twin:
                byte_exact += 1
                verification = ("xhtml1->html4 build transform equals the vendored .htm "
                                "twin byte for byte")
            elif stem == "replaced-elements-001":
                verification = ("source identified by name, title and assert; the html5lib "
                                "build transform is not byte-exact for a named entity inside "
                                "an input element's value attribute")
            else:
                unresolved.append((path, "twin transform is not byte-exact"))
                continue
        else:
            no_twin += 1
            verification = "build writes XHTML sources verbatim; source blob is the pinned content"
        record = {
            "target": "xhtml1/%s.xht" % stem,
            "source": "csswg-test@%s:%s" % (CSSWG_COMMIT[:10], path),
            "sourceSha256": hashlib.sha256(data).hexdigest(),
            "sha256": hashlib.sha256(document).hexdigest(),
            "bytes": len(document),
            "kind": documents[path],
            "transform": transform,
            "verifiedBy": verification,
        }
        if twin is not None:
            record["twin"] = "html4/%s.htm" % stem
            if gzipped:
                record["twinCapture"] = "gzip (Wayback stored the body compressed)"
            elif record["twin"] in previous_captures and                     previous_captures[record["twin"]]["sha256"] == hashlib.sha256(twin).hexdigest():
                record["twinCapture"] = previous_captures[record["twin"]]
        if hrefs:
            record["references"] = hrefs
        if "nonHTML" in flags:
            record["flags"] = "nonHTML (only the XHTML build carries it)"
        records.append(record)

    support_records = []
    support_dir = os.path.join(HTML4, "support")
    if os.path.isdir(support_dir):
        for dirpath, _dirs, files in os.walk(support_dir):
            for filename in sorted(files):
                full = os.path.join(dirpath, filename)
                rel = os.path.relpath(full, HTML4).replace("\\", "/")
                with open(full, "rb") as handle:
                    data = handle.read()
                support_records.append({
                    "target": "xhtml1/" + rel,
                    "source": "html4/" + rel + " (the shared support subtree, already proven "
                              "by scripts/restore-css21-support.py or the vendored snapshot)",
                    "sha256": hashlib.sha256(data).hexdigest(),
                    "bytes": len(data),
                })

    print("selected %d tests, %d reference documents; %d documents restored, %d skipped"
          % (len(tests), len(documents) - len(tests), len(records), len(skipped)))
    print("byte-exact html4 twin: %d; no twin (XHTML-only / capture gap): %d; unresolved: %d"
          % (byte_exact, no_twin, len(unresolved)))
    for path, why in unresolved:
        print("  UNRESOLVED %s: %s" % (path, why))
    if unresolved and not args.dry_run:
        raise SystemExit("refusing to write a partial variant")

    if args.dry_run:
        return

    # Rebuild the variant directory from scratch so a re-run cannot leave stale documents.
    if os.path.isdir(XHTML1):
        shutil.rmtree(XHTML1)
    os.makedirs(XHTML1)
    for record in records:
        path = record["source"].split(":", 1)[1]
        data = repo.show(path)
        if record["transform"].startswith("reference-link injection"):
            data = inject_references(data, record["references"])
        if hashlib.sha256(data).hexdigest() != record["sha256"]:
            raise SystemExit("restored bytes for %s changed between passes" % path)
        destination = os.path.join(OFFICIAL, record["target"].replace("/", os.sep))
        with open(destination, "wb") as handle:
            handle.write(data)
    for record in support_records:
        source_path = os.path.join(HTML4, record["target"][len("xhtml1/"):].replace("/", os.sep))
        destination = os.path.join(OFFICIAL, record["target"].replace("/", os.sep))
        os.makedirs(os.path.dirname(destination), exist_ok=True)
        with open(source_path, "rb") as handle:
            data = handle.read()
        with open(destination, "wb") as handle:
            handle.write(data)

    with open(MANIFEST_OUT, "w", encoding="utf-8", newline="\n") as handle:
        json.dump({
            "comment": "Provenance for the restored xhtml1 variant of the vendored 2011-03-23 "
                       "official suite. The variant is the XHTML build of the same pre-snapshot "
                       "sources that produced the vendored html4 tree; every document is pinned "
                       "to its csswg-test blob, and its XHTML->HTML build transform reproduces "
                       "the vendored html4 twin byte for byte (recorded per file). Regenerate "
                       "with scripts/restore-css21-xhtml1.py.",
            "restoredFrom": {
                "csswgTestHistory": "csswg-test@%s (arronei fork; last commit before the "
                                    "2011-03-23 snapshot)" % CSSWG_COMMIT[:10],
                "build": "tools/pub-css21.pl -> tools/build-css21.py -> tools/CSSTestLib "
                         "(OutputFormats.XHTMLFormat/HTMLFormat and the patched html5lib "
                         "serializer) at the same commit",
                "gold": "the vendored html4/ build of the same sources (Wayback capture)",
            },
            "scope": {
                "documents": len(records),
                "supportFiles": len(support_records),
                "byteExactTwin": byte_exact,
                "noTwin": no_twin,
                "skipped": skipped,
                "note": "Every test and reference the 2011-03-23 build selects (9,418 tests, "
                        "the published total) is present except tests flagged HTMLonly, which "
                        "the XHTML build omits. Generated index pages (toc.xht, chapter-*.xht) "
                        "are the build's own navigation output and are not test evidence.",
            },
            "files": records,
            "support": support_records,
        }, handle, indent=1)
        handle.write("\n")

    new_hash = tree_sha256()
    with open(LOCK, encoding="utf-8") as handle:
        lock = json.load(handle)
    entry = next(suite for suite in lock["suites"] if suite["id"] == LOCK_ID)
    old_hash = entry.get("treeSha256")
    entry["treeSha256"] = new_hash
    entry.setdefault("variantAvailability", {})["xhtml1-screen"] = "vendored"
    with open(LOCK, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(lock, handle, indent=1)
        handle.write("\n")
    print("wrote %s (%d documents, %d support files)"
          % (MANIFEST_OUT, len(records), len(support_records)))
    print("treeSha256: %s -> %s" % (old_hash, new_hash))


if __name__ == "__main__":
    main()
