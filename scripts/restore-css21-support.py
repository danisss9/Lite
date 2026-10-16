#!/usr/bin/env python3
"""Restores the 2011-03-23 official suite's missing html4/support/ subtree.

The original vendoring (scripts/fetch-tests.ps1) could recover the suite only from
Wayback captures of test.csswg.org, and those captures cover almost none of the
support/ subtree (35 captures, none for a file the executed cases reference). This
script rebuilds the subtree from sources with recorded provenance and re-pins the
tree hash in Lite.Conformance/test-suites.lock.json:

- Primary source: csswg-test@fa448700877 (the arronei fork's last commit before the
  2011-03-23 snapshot, from which the suite was built). Blob identity is decided on
  git blob SHAs, so no bytes move unless needed. Gold validation: the at-charset-045.css
  blob equals both the Wayback capture of the built snapshot and its CDX SHA-1 digest.
- Fallback source: the pinned WPT checkout (the WPT CSS2 corpus was imported from this
  same suite; the generated *_color.png family cross-validates byte-identical against
  the pre-snapshot commit).
- When several same-named copies exist, an unambiguous blob wins; otherwise the
  variant matching the WPT majority wins, then the majority blob.
- Encoding-sensitive stylesheets are served with the charsets the 2011 suite applied
  through its per-directory .htaccess files (recovered from the same commit). The
  conformance server consumes them in the WPT "<file>.headers" convention; where the
  WPT checkout carries a .headers for a byte-identical file it is copied, otherwise
  one is derived from the recovered rules.

Resources are vendored under their URL-DECODED names: browsers resolve
url(ruler-h-50%25.png) to the file "ruler-h-50%.png", and the conformance server
serves decoded paths.

Per-file provenance is written to Lite.Conformance/Css21/css21-support-restore.json.
Resources that tests reference but that are intentionally absent (broken-image and
invalid-URL cases) are reported, not restored; they are allowlisted for the input
scanner in Lite.Conformance/Css21/css21-intentional-missing-inputs.json.
"""

import argparse
import csv
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.parse
from collections import defaultdict

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OFFICIAL = os.path.join(REPO, "Lite.Conformance", "vendor", "css21-official-20110323")
WPT = os.path.join(REPO, "Lite.Conformance", "vendor", "wpt")
LOCK = os.path.join(REPO, "Lite.Conformance", "test-suites.lock.json")
MANIFEST_OUT = os.path.join(REPO, "Lite.Conformance", "Css21", "css21-support-restore.json")
BASELINE_CSV = os.path.join(REPO, "Lite.Conformance", "artifacts", "css21-full-screen-baseline.csv")
CSSWG_HISTORY = os.path.join(REPO, "artifacts", "csswg-test-history")
CSSWG_COMMIT = "fa4487008776579c206378d2df0ecea81b69cd80"

# Encoding-sensitive stylesheets: the HTTP charsets the 2011 suite applied through its
# per-directory .htaccess files (recovered from csswg-test@fa448700877). Files under
# RemoveCharset rules are served bare, which is the server default, so they need nothing.
# Encoding-sensitive TEST DOCUMENTS (the pages themselves): the charsets the 2011
# suite's html4/.htaccess served them with. The conformance server decodes the response
# with this charset (CSS 2.1 4.4 gives the transport encoding priority), which is what
# makes the page's raw-encoded class attributes match the (UTF-8) stylesheet selectors.
PAGE_CHARSETS = {
    "character-encoding-003.htm": "iso-8859-1",
    "character-encoding-006.htm": "iso-8859-1",
    "character-encoding-009.htm": "iso-8859-1",
    "character-encoding-013.htm": "iso-8859-1",
    "character-encoding-014.htm": "iso-8859-15",
    "character-encoding-015.htm": "iso-8859-1",
    "character-encoding-016.htm": "iso-8859-1",
    "character-encoding-017.htm": "iso-8859-15",
}

HTACCESS_CHARSETS = {
    "at-charset-001.css": "Shift_JIS",
    "at-charset-002.css": "Shift_JIS",
    "at-charset-003.css": "Shift_JIS",
    "at-charset-004.css": "Shift_JIS",
    "at-charset-005.css": "Shift_JIS",
    "at-charset-006.css": "Shift_JIS",
    "character-encoding-031.css": "iso-8859-1",
    "character-encoding-032.css": "iso-8859-11",
    "character-encoding-033.css": "iso-8859-5",
    "character-encoding-034.css": "iso-8859-6",
    "character-encoding-035.css": "iso-8859-7",
    "character-encoding-036.css": "iso-8859-8",
    "character-encoding-037.css": "koi8-r",
    "http1.css": "iso-8859-1",
    "http15.css": "iso-8859-15",
    "http8.css": "utf-8",
    "http8-charset1.css": "utf-8",
    "http1-charset8.css": "iso-8859-1",
    "http1-bom.css": "iso-8859-1",
}

# Resources the suite references but must not exist (failure/fallback tests, invalid URL
# tokens). Matched by test stem and resource value in the input scanner; never restored.
# The three uri-* stems are entirely about invalid url() tokens (flags=invalid), so every
# resource they request is a phantom and allowlisted by stem.
INTENTIONAL_BY_STEM = {"uri-012", "uri-013", "uri-016"}
INTENTIONAL = {
    "list-style-image-004": {"404"},
    "list-style-image-007": {"404"},
    "cursor-020": {"inexistent.cur"},
    "cursor-fallback-001": {"missing.cur"},
    "content-uri-001": {"missingImage.png"},
    "content-138": {"PASS PASS"},
    "list-style-image-available-001": {"missingImage.png"},
    "before-after-images-001": {"missing-image.png"},
    "before-after-table-whitespace-001": {"missing-image.png"},
}

UrlValue = re.compile(r"\burl\(\s*(?P<q>['\"]?)(?P<ref>[^)\"']+)(?P=q)\s*\)", re.IGNORECASE)
Import = re.compile(
    r"@import\s+(?:url\(\s*(?P<q1>['\"]?)(?P<ref1>[^)\"']+)(?P=q1)\s*\)|(?P<q2>['\"])(?P<ref2>[^\"']+)(?P=q2))",
    re.IGNORECASE)


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def decode(value):
    return urllib.parse.unquote(value.strip())


def collect_needs():
    """Unique missing support resources from the baseline CSV, URL-decoded, with the
    requesting test stems."""
    needs = defaultdict(set)
    if not os.path.exists(BASELINE_CSV):
        sys.exit(f"baseline CSV not found: {BASELINE_CSV}")
    with open(BASELINE_CSV, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            detail = row.get("detail") or ""
            if "Missing suite input:" not in detail:
                continue
            m = re.search(r"->\s*(.+?)\s*$", detail)
            if not m:
                continue
            resource = decode(m.group(1))
            case = row.get("path") or ""
            stem = os.path.splitext(os.path.basename(case))[0]
            needs[resource].add(stem)
    return needs


LinkTag = re.compile(r"<link\b[^>]*>", re.IGNORECASE)
LinkAttr = re.compile(r"\b(rel|href|src)\s*=\s*\"([^\"]*)\"", re.IGNORECASE)
StyleTag = re.compile(r"<style\b[^>]*>(.*?)</style>", re.IGNORECASE | re.DOTALL)
StyleAttr = re.compile(r"\bstyle\s*=\s*\"([^\"]*)\"", re.IGNORECASE)
CssComment = re.compile(r"/\*.*?\*/", re.DOTALL)


def render_inputs(text):
    """Render-relevant stylesheet imports of an HTML document, decoded: rel~=stylesheet
    link hrefs, src attributes, and url()/@import inside style blocks and style
    attributes (comments stripped first, so commented-out references do not count).
    Mirrors the semantics of Wpt.SuiteInputs so the restore covers exactly what the
    runner's input scanner can flag, including cases that currently pass vacuously."""
    refs = []
    for m in LinkTag.finditer(text):
        attrs = dict((a.group(1).lower(), a.group(2)) for a in LinkAttr.finditer(m.group(0)))
        if "stylesheet" in attrs.get("rel", "").lower() and attrs.get("href"):
            refs.append(attrs["href"])
    for m in LinkAttr.finditer(text):
        if m.group(1).lower() == "src" and m.group(2):
            refs.append(m.group(2))
    css_chunks = [m.group(1) for m in StyleTag.finditer(text)]
    css_chunks += [m.group(1) for m in StyleAttr.finditer(text)]
    for chunk in css_chunks:
        cleaned = CssComment.sub(" ", chunk)
        for m in UrlValue.finditer(cleaned):
            refs.append(m.group("ref"))
        for m in Import.finditer(cleaned):
            refs.append(m.group("ref1") or m.group("ref2"))
    return [decode(r) for r in refs]


def scan_official_needs():
    """Resources referenced by every official html4 test document that are absent from
    the tree: {resource: {test stem, ...}}."""
    needs = defaultdict(set)
    html4 = os.path.join(OFFICIAL, "html4")
    for fn in os.listdir(html4):
        if not fn.endswith((".htm", ".html")):
            continue
        path = os.path.join(html4, fn)
        try:
            text = open(path, encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        stem = os.path.splitext(fn)[0]
        for ref in render_inputs(text):
            if not ref or ref.startswith(("http:", "https:", "data:", "#", "/")):
                continue
            resolved = os.path.normpath(os.path.join(html4, ref.replace("/", os.sep)))
            if not resolved.startswith(html4) or os.path.exists(resolved):
                continue
            needs[ref].add(stem)
    return needs


def index_wpt():
    byname = defaultdict(list)
    for dirpath, _dirs, files in os.walk(os.path.join(WPT, "css")):
        for fn in files:
            byname[fn].append(os.path.join(dirpath, fn))
    return byname


def index_csswg():
    """basename -> {blob sha: [repo path, ...]} at the pre-snapshot commit, from the
    tree objects alone (no blob fetches)."""
    result = subprocess.run(
        ["git", "ls-tree", "-r", CSSWG_COMMIT], cwd=CSSWG_HISTORY,
        capture_output=True, check=True)
    byname = defaultdict(lambda: defaultdict(list))
    for line in result.stdout.decode("utf-8", "replace").splitlines():
        meta, path = line.split("\t", 1)
        _mode, kind, sha = meta.split()
        if kind == "blob":
            byname[os.path.basename(path)][sha].append(path)
    return byname


class CsswgBlobs:
    """Reads blobs from the blob-filtered csswg-test clone through one persistent
    'git cat-file --batch' process; promisor blobs lazy-fetch on demand. A transient
    fetch failure reports the blob as missing, so a failed read reopens and retries."""

    def __init__(self, attempts=4):
        self.cwd = CSSWG_HISTORY
        self.attempts = attempts
        self._open()

    def _open(self):
        self.proc = subprocess.Popen(
            ["git", "cat-file", "--batch"], cwd=self.cwd,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE)

    def read(self, sha):
        for attempt in range(self.attempts):
            try:
                self.proc.stdin.write(sha.encode("ascii") + b"\n")
                self.proc.stdin.flush()
                header = self.proc.stdout.readline().decode("ascii", "replace").split()
                if len(header) >= 3 and header[1] == "blob":
                    size = int(header[2])
                    data = self.proc.stdout.read(size)
                    self.proc.stdout.read(1)
                    return data
                if header and header[-1] == "missing":
                    raise IOError(f"lazy fetch failed for {sha}")
                raise IOError(f"unexpected cat-file response for {sha}: {header}")
            except (IOError, OSError):
                if attempt == self.attempts - 1:
                    raise
                self.proc.kill()
                self._open()
        raise IOError(sha)


def wpt_majority(candidates):
    by_digest = defaultdict(list)
    for path in candidates:
        with open(path, "rb") as f:
            by_digest[hashlib.sha1(f.read()).hexdigest()].append(path)
    ranked = sorted(by_digest.items(), key=lambda kv: -len(kv[1]))
    if len(ranked) > 1:
        print(f"  note: ambiguous WPT variant, majority ({len(ranked[0][1])} copies) wins "
              f"over {len(ranked[1][1])}: {os.path.basename(ranked[0][1][0])}")
    return ranked[0][1][0], ranked[0][0]


def write_with_headers(dest, data, headers_text, records, source_kind, source_ref,
                       headers_kind, headers_ref):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "wb") as f:
        f.write(data)
    records.append({
        "target": os.path.relpath(dest, OFFICIAL).replace("\\", "/"),
        "source": source_ref,
        "sourceKind": source_kind,
        "sha256": sha256_bytes(data),
        "bytes": len(data),
    })
    if headers_text is not None:
        with open(dest + ".headers", "wb") as f:
            f.write(headers_text)
        records.append({
            "target": os.path.relpath(dest + ".headers", OFFICIAL).replace("\\", "/"),
            "source": headers_ref,
            "sourceKind": headers_kind,
            "sha256": sha256_bytes(headers_text),
            "bytes": len(headers_text),
        })


def css_imports(css_path):
    """url()/@import targets inside a CSS file, decoded (second-level render inputs)."""
    try:
        text = open(css_path, encoding="utf-8", errors="replace").read()
    except OSError:
        return []
    refs = [m.group("ref") for m in UrlValue.finditer(text)]
    refs += [m.group("ref1") or m.group("ref2") for m in Import.finditer(text)]
    return [decode(r) for r in refs]


def tree_sha256():
    """Canonical tree digest, matching OfficialCatalog.ComputeTreeSha256 exactly. The C#
    verifier orders entries with StringComparer.OrdinalIgnoreCase, which upper-case folds
    before the ordinal comparison, so fold to upper case here too. Entries are serialized
    as 'rel\\0<file sha256 hex>\\n'."""
    entries = []
    for dirpath, _dirs, files in os.walk(OFFICIAL):
        for fn in files:
            if fn == "fetch-failures.log" or fn.endswith(".download"):
                continue
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, OFFICIAL).replace("\\", "/").lower()
            entries.append((rel.upper(), rel, full))
    entries.sort()
    accumulator = hashlib.sha256()
    for _fold, rel, full in entries:
        with open(full, "rb") as f:
            digest = hashlib.sha256(f.read()).hexdigest()
        accumulator.update(f"{rel}\0{digest}\n".encode("utf-8"))
    return accumulator.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="report only, copy nothing")
    args = parser.parse_args()

    needs = collect_needs()
    csv_count = len(needs)
    for resource, stems in scan_official_needs().items():
        needs[resource] |= stems
    print(f"{csv_count} unique missing resources from the baseline CSV, "
          f"{len(needs)} after scanning every official test document directly")
    records = []
    byname_wpt = index_wpt()
    byname_csswg = index_csswg()
    blobs = CsswgBlobs()
    restored, unresolved = 0, []

    for resource in sorted(needs):
        stems = needs[resource]
        name = os.path.basename(resource)
        if any(stem in INTENTIONAL_BY_STEM or name in INTENTIONAL.get(stem, ()) for stem in stems):
            print(f"  intentional (allowlisted, not restored): {resource} for {sorted(stems)[0]}")
            continue
        if not resource.startswith("support/"):
            # WPT-side inputs (root-absolute references and css/ paths) are not official
            # support files; the /css/reference gap is a WPT sparse-checkout matter.
            unresolved.append((resource, sorted(stems)))
            continue
        dest = os.path.join(OFFICIAL, "html4", resource.replace("/", os.sep))
        if os.path.exists(dest):
            continue

        data = None
        source_kind = source_ref = None
        wpt_majority_digest = None
        if name in byname_wpt:
            wpt_src, wpt_majority_digest = wpt_majority(byname_wpt[name])

        csswg_variants = byname_csswg.get(name, {})
        if len(csswg_variants) == 1:
            blob_sha = next(iter(csswg_variants))
            data = blobs.read(blob_sha)
            source_kind = "csswg-test-pre-snapshot"
            source_ref = f"csswg-test@{CSSWG_COMMIT[:10]}:{csswg_variants[blob_sha][0]}"
        elif len(csswg_variants) > 1:
            blob_sha = None
            if wpt_majority_digest:
                for candidate_sha in csswg_variants:
                    candidate = blobs.read(candidate_sha)
                    if hashlib.sha1(candidate).hexdigest() == wpt_majority_digest:
                        blob_sha, data = candidate_sha, candidate
                        break
            if data is None:
                blob_sha = sorted(csswg_variants, key=lambda s: -len(csswg_variants[s]))[0]
                data = blobs.read(blob_sha)
            source_kind = "csswg-test-pre-snapshot"
            source_ref = f"csswg-test@{CSSWG_COMMIT[:10]}:{csswg_variants[blob_sha][0]}"

        if data is None and name in byname_wpt:
            src = wpt_majority(byname_wpt[name])[0]
            with open(src, "rb") as f:
                data = f.read()
            source_kind = "wpt-pinned-checkout"
            source_ref = os.path.relpath(src, WPT).replace("\\", "/")

        if data is None:
            unresolved.append((resource, sorted(stems)))
            continue

        # .headers: prefer the WPT copy for a byte-identical file, else derive from the
        # recovered .htaccess rules; encoding-less files are served bare (the default).
        headers_text = None
        headers_kind = headers_ref = None
        wpt_same_bytes = None
        if name in byname_wpt:
            for path in byname_wpt[name]:
                with open(path, "rb") as f:
                    if f.read() == data:
                        wpt_same_bytes = path
                        break
        if wpt_same_bytes and os.path.exists(wpt_same_bytes + ".headers"):
            with open(wpt_same_bytes + ".headers", "rb") as f:
                headers_text = f.read()
            headers_kind = "wpt-pinned-checkout"
            headers_ref = os.path.relpath(wpt_same_bytes + ".headers", WPT).replace("\\", "/")
        elif name in HTACCESS_CHARSETS:
            headers_text = f"Content-Type: text/css; charset={HTACCESS_CHARSETS[name]}".encode("ascii")
            headers_kind = "recovered-htaccess-rule"
            headers_ref = f"contributors .htaccess at csswg-test@{CSSWG_COMMIT[:10]}"

        if not args.dry_run:
            write_with_headers(dest, data, headers_text, records, source_kind, source_ref,
                               headers_kind, headers_ref)
        restored += 1

    # Third pass: the encoding-sensitive test documents themselves (see PAGE_CHARSETS).
    for name, charset in sorted(PAGE_CHARSETS.items()):
        dest = os.path.join(OFFICIAL, "html4", name)
        if not os.path.exists(dest):
            print(f"  page headers skipped (document absent): {name}")
            continue
        headers_path = dest + ".headers"
        headers_text = f"Content-Type: text/html; charset={charset}".encode("ascii")
        if not args.dry_run:
            if not os.path.exists(headers_path):
                with open(headers_path, "wb") as f:
                    f.write(headers_text)
            records.append({
                "target": name + ".headers",
                "source": f"contributors .htaccess at csswg-test@{CSSWG_COMMIT[:10]}",
                "sourceKind": "recovered-htaccess-rule",
                "sha256": hashlib.sha256(headers_text).hexdigest(),
                "note": f"the page itself is served as {charset}",
            })

    # Second-level inputs: resources referenced from the restored CSS files themselves
    # (the input scanner now follows linked CSS, so these must exist for honest execution).
    scanned = 0
    while scanned < len(records):
        record = records[scanned]
        scanned += 1
        target = record["target"]
        if not target.endswith(".css"):
            continue
        css_path = os.path.join(OFFICIAL, "html4", target.replace("/", os.sep))
        base = os.path.dirname(css_path)
        for ref in css_imports(css_path):
            if ref.startswith(("http:", "https:", "data:", "#", "/")):
                continue
            nested = os.path.normpath(os.path.join(base, ref.replace("/", os.sep)))
            if os.path.exists(nested) or not nested.startswith(os.path.join(OFFICIAL, "html4")):
                continue
            nested_name = os.path.basename(ref)
            if nested_name in byname_csswg or nested_name in byname_wpt:
                if nested_name in byname_csswg:
                    variants = byname_csswg[nested_name]
                    blob_sha = next(iter(variants))
                    data = blobs.read(blob_sha)
                    source_kind = "csswg-test-pre-snapshot"
                    source_ref = f"csswg-test@{CSSWG_COMMIT[:10]}:{variants[blob_sha][0]}"
                else:
                    src = wpt_majority(byname_wpt[nested_name])[0]
                    with open(src, "rb") as f:
                        data = f.read()
                    source_kind = "wpt-pinned-checkout"
                    source_ref = os.path.relpath(src, WPT).replace("\\", "/")
                if not args.dry_run:
                    write_with_headers(nested, data, None, records, source_kind, source_ref,
                                       None, None)
                restored += 1
            else:
                unresolved.append((os.path.relpath(nested, OFFICIAL).replace("\\", "/"), ["<nested import>"]))

    print(f"restored {restored} files; {len(unresolved)} unresolved")
    for resource, stems in unresolved:
        print(f"  UNRESOLVED {resource} (requested by {stems})")

    if args.dry_run:
        return

    with open(MANIFEST_OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump({
            "comment": "Provenance for the restored html4/support/ subtree of the vendored "
                       "2011-03-23 official suite. Regenerate with scripts/restore-css21-support.py. "
                       "Gold validation: at-charset-045.css equals both the Wayback capture of the "
                       "built snapshot and its CDX SHA-1 digest; the WPT *_color.png family equals "
                       "the pre-snapshot blobs byte for byte.",
            "restoredFrom": {
                "csswgTestHistory": f"csswg-test@{CSSWG_COMMIT[:10]} (arronei fork; last commit "
                                    "before the 2011-03-23 snapshot)",
                "wpt": "vendor/wpt (pinned in test-suites.lock.json; fallback)",
                "encodingHeaders": "recovered from the suite's per-directory .htaccess files at "
                                   "the pre-snapshot commit; consumed as <file>.headers",
            },
            "intentionalMissing": {
                "byStem": sorted(INTENTIONAL_BY_STEM),
                "byStemAndResource": {k: sorted(v) for k, v in sorted(INTENTIONAL.items())},
            },
            "files": records,
        }, f, indent=1)
        f.write("\n")
    print(f"wrote {MANIFEST_OUT} ({len(records)} entries)")

    new_hash = tree_sha256()
    with open(LOCK, encoding="utf-8") as f:
        lock = json.load(f)
    entry = next(s for s in lock["suites"] if s["id"] == "css21-official-20110323")
    old_hash = entry.get("treeSha256")
    entry["treeSha256"] = new_hash
    with open(LOCK, "w", encoding="utf-8", newline="\n") as f:
        json.dump(lock, f, indent=1)
        f.write("\n")
    print(f"treeSha256: {old_hash} -> {new_hash}")


if __name__ == "__main__":
    main()
