#!/usr/bin/env python3
"""Restores the official suite's missing html4/*.xht reference documents.

The 2011-03-23 distribution contains every test and reference in two syntaxes: .xht
(XHTML) and .htm (the same document converted to HTML 4.01). The Wayback captures that
fed the original vendoring recovered the .htm half only, so 200 rel=reference targets
(requested by 299 tests) are absent and those cases report "No reference path passed".

This script restores the .xht half from csswg-test@fa448700877 (the last commit before
the snapshot). The distribution's .htm is a mechanical transform of the same source —
XHTML 1.0 Strict doctype swapped for the HTML 4.01 one, the root xmlns attribute
dropped, " />"-style self-closers shortened to ">" — so every candidate is verified by
applying that transform to the source and requiring byte equality with the vendored
.htm twin. A candidate that does not transform to its twin is never vendored; provenance
(including the verification) is recorded per file in
Lite.Conformance/Css21/css21-xht-restore.json, and the tree hash is re-pinned in
Lite.Conformance/test-suites.lock.json.
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OFFICIAL = os.path.join(REPO, "Lite.Conformance", "vendor", "css21-official-20110323")
LOCK = os.path.join(REPO, "Lite.Conformance", "test-suites.lock.json")
MANIFEST_OUT = os.path.join(REPO, "Lite.Conformance", "Css21", "css21-xht-restore.json")
CSSWG_HISTORY = os.path.join(REPO, "artifacts", "csswg-test-history")
CSSWG_COMMIT = "fa4487008776579c206378d2df0ecea81b69cd80"

LinkTag = re.compile(r"<link\b[^>]*>", re.IGNORECASE)
TagAttr = re.compile(r"\b(rel|href)\s*=\s*\"([^\"]*)\"", re.IGNORECASE)


def missing_xht_references():
    """rel=reference targets (and reftest.list right-column .xht entries) absent from
    the vendored html4 tree."""
    missing = set()
    html4 = os.path.join(OFFICIAL, "html4")
    for fn in os.listdir(html4):
        if not fn.endswith((".htm", ".html")):
            continue
        text = open(os.path.join(html4, fn), encoding="utf-8", errors="replace").read()
        for tag in LinkTag.finditer(text):
            attrs = dict((a.group(1).lower(), a.group(2)) for a in TagAttr.finditer(tag.group(0)))
            if "reference" not in attrs.get("rel", "").lower().split():
                continue
            href = attrs.get("href", "")
            if href.endswith(".xht") and not os.path.exists(os.path.join(html4, href.replace("/", os.sep))):
                missing.add(href)
    reftest = os.path.join(html4, "reftest.list")
    if os.path.exists(reftest):
        for line in open(reftest, encoding="utf-8", errors="replace"):
            parts = line.split()
            if len(parts) == 3 and parts[0] in ("==", "!=") and parts[2].endswith(".xht"):
                if not os.path.exists(os.path.join(html4, parts[2].replace("/", os.sep))):
                    missing.add(parts[2])
    return sorted(missing)


def index_csswg():
    result = subprocess.run(["git", "ls-tree", "-r", CSSWG_COMMIT],
                            cwd=CSSWG_HISTORY, capture_output=True, check=True)
    byname = {}
    for line in result.stdout.decode("utf-8", "replace").splitlines():
        meta, path = line.split("\t", 1)
        _mode, kind, sha = meta.split()
        if kind == "blob" and path.endswith(".xht"):
            byname.setdefault(os.path.basename(path), []).append((sha, path))
    return byname


class Blobs:
    def __init__(self):
        self.proc = subprocess.Popen(["git", "cat-file", "--batch"], cwd=CSSWG_HISTORY,
                                     stdin=subprocess.PIPE, stdout=subprocess.PIPE)

    def read(self, sha):
        self.proc.stdin.write(sha.encode("ascii") + b"\n")
        self.proc.stdin.flush()
        header = self.proc.stdout.readline().decode("ascii", "replace").split()
        if len(header) >= 3 and header[1] == "blob":
            size = int(header[2])
            data = self.proc.stdout.read(size)
            self.proc.stdout.read(1)
            return data
        self.proc.stdout.readline()
        return None


def xht_to_html(source):
    """The distribution's XHTML→HTML transform, recovered from twin pairs: the doctype
    becomes HTML 4.01 strict, the root xmlns attribute is dropped, and " />"-style
    self-closers shorten to ">". Applied to a source .xht it must reproduce the vendored
    .htm twin byte for byte."""
    text = source.decode("utf-8", "replace")
    # Contributor sources carry CRLF/CR endings; the distribution is LF throughout.
    text = text.replace(chr(13) + chr(10), chr(10)).replace(chr(13), chr(10))
    text = re.sub(r"<\?xml[^?]*\?>\s*", "", text)
    text = re.sub(
        r'<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1\.[01][^"]*//EN"\s*"[^"]*">',
        '<!DOCTYPE html PUBLIC "-//W3C//DTD HTML 4.01//EN" "http://www.w3.org/TR/html4/strict.dtd">',
        text, count=1)
    # The build emits the doctype on its own line even when the source had "<html>"
    # follow it on the same line.
    text = re.sub(r'(strict\.dtd">)(?=<)', r"\1\n", text, count=1)
    # Self-closers drop the slash whether or not a space preceded it; the trailing
    # slash inside a quoted URL value is not touched because it is not followed by ">".
    text = text.replace(" />", ">")
    text = text.replace("/>", ">")
    # Numeric character references re-serialize to the named form where one exists.
    text = text.replace("&#160;", "&nbsp;")
    # Attribute values serialize with newlines folded to spaces (the continuation
    # indentation the source carried is kept).
    text = re.sub(r"\"([^\"]*)\"", lambda m: '"' + m.group(1).replace("\n", " ") + '"', text)
    # HTML <style> content is CDATA: the XHTML-escaped comparators become raw characters.
    def unescape_style(match):
        block = match.group(2)
        block = block.replace("<![CDATA[", "").replace("]]>", "")
        block = block.replace("&gt;", ">").replace("&lt;", "<").replace("&amp;", "&")
        return match.group(1) + block + match.group(3)
    text = re.sub(r"(<style\b[^>]*>)(.*?)(</style>)", unescape_style, text,
                  flags=re.IGNORECASE | re.DOTALL)
    text = text.replace(' xmlns="http://www.w3.org/1999/xhtml"', "")
    return text


def tree_sha256():
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
        digest = hashlib.sha256(open(full, "rb").read()).hexdigest()
        accumulator.update(f"{rel}\0{digest}\n".encode("utf-8"))
    return accumulator.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    missing = missing_xht_references()
    print(f"{len(missing)} missing .xht reference documents")
    byname = index_csswg()
    blobs = Blobs()
    records, unresolved = [], []

    for href in missing:
        stem = os.path.basename(href)
        twin = os.path.join(OFFICIAL, "html4", href[:-4] + ".htm")
        if not os.path.exists(twin):
            unresolved.append((href, "no .htm twin"))
            continue
        twin_bytes = open(twin, "rb").read().decode("utf-8", "replace")
        candidates = byname.get(stem, [])
        verified = None
        for sha, path in candidates:
            data = blobs.read(sha)
            if data is None:
                continue
            # The distribution strips the source's trailing newline; compare sans it.
            if xht_to_html(data).rstrip("\n") == twin_bytes.rstrip("\n"):
                verified = (sha, path, data)
                break
        if verified is None:
            unresolved.append((href, f"no candidate transforms to the twin ({len(candidates)} tried)"))
            continue
        sha, path, data = verified
        records.append({
            "target": f"html4/{href}",
            "source": f"csswg-test@{CSSWG_COMMIT[:10]}:{path}",
            "verifiedBy": "xht->htm transform equals the vendored .htm twin byte for byte",
            "sha256": hashlib.sha256(data).hexdigest(),
            "bytes": len(data),
        })
        if not args.dry_run:
            dest = os.path.join(OFFICIAL, "html4", href.replace("/", os.sep))
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with open(dest, "wb") as f:
                f.write(data)

    print(f"restored {len(records)}; {len(unresolved)} unresolved")
    for href, why in unresolved:
        print(f"  UNRESOLVED {href}: {why}")
    if args.dry_run:
        return

    with open(MANIFEST_OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump({
            "comment": "Provenance for the restored html4/*.xht reference documents of the "
                       "vendored 2011-03-23 official suite. Each file's XHTML→HTML transform "
                       "equals the vendored .htm twin byte for byte, pinning it to the exact "
                       "distribution content. Regenerate with scripts/restore-css21-xht-refs.py.",
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
