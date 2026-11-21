#!/usr/bin/env python3
"""Decompresses the vendored html4 artifacts that Wayback stored as gzip bodies.

25 documents of the vendored 2011-03-23 html4/ build were captured with their response body
still gzip-compressed (Wayback stored the compressed payload, and the fetch's transport
decompression does not undo a payload that is itself a gzip stream). A browser loading those
bytes without Content-Encoding sees binary garbage, so the cases ran against noise.

Each artifact is proven before it is rewritten: gunzip(captured bytes) must equal, byte for
byte, the recovered XHTML->HTML build transform (scripts/restore-css21-xhtml1.py) applied to
the csswg-test@fa44870087 source blob that css21-xhtml1-restore.json pins for the same stem.
The source blob must still hash to the recorded sourceSha256. Anything that does not prove
is reported and nothing is written.

Provenance lands in the existing restore records: each affected xhtml1 record's twinCapture
becomes an object with the captured and decompressed digests. The vendored tree hash is then
re-pinned with the documented OrdinalIgnoreCase canonical rule (the same tree_sha256 the
xhtml1 restore uses, matching OfficialCatalog.ComputeTreeSha256).

The step is idempotent: a re-run finds no gzip bodies and verifies the recorded decompressed
digests instead. Run it after any refetch of the official tree, after the xhtml1 restore.
"""

import argparse
import gzip
import hashlib
import importlib.util
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location(
    "restore_css21_xhtml1", os.path.join(HERE, "restore-css21-xhtml1.py"))
xhtml1 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(xhtml1)

GZIP_MAGIC = b"\x1f\x8b"
CAPTURE = "gzip (Wayback stored the body compressed)"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def gzip_bodies():
    """Every file in the vendored official tree whose bytes start with the gzip magic."""
    found = []
    for dirpath, _dirs, files in os.walk(xhtml1.OFFICIAL):
        for filename in files:
            if filename == "fetch-failures.log" or filename.endswith(".download"):
                continue
            full = os.path.join(dirpath, filename)
            with open(full, "rb") as handle:
                if handle.read(2) == GZIP_MAGIC:
                    found.append(os.path.relpath(full, xhtml1.OFFICIAL).replace("\\", "/"))
    return sorted(found)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="prove and report, write nothing")
    args = parser.parse_args()

    with open(xhtml1.MANIFEST_OUT, encoding="utf-8") as handle:
        manifest = json.load(handle)
    by_twin = {record["twin"]: record for record in manifest["files"] if "twin" in record}

    bodies = gzip_bodies()
    print("gzip bodies in the vendored tree: %d" % len(bodies))
    repo = xhtml1.Repo()
    proven, unresolved = [], []
    for rel in bodies:
        record = by_twin.get(rel)
        if record is None:
            unresolved.append((rel, "no xhtml1 restore record names it as a twin"))
            continue
        source_path = record["source"].split(":", 1)[1]
        source = repo.show(source_path)
        if source is None or sha256(source) != record["sourceSha256"]:
            unresolved.append((rel, "source blob missing or not the recorded sourceSha256"))
            continue
        full = os.path.join(xhtml1.OFFICIAL, rel.replace("/", os.sep))
        captured = open(full, "rb").read()
        try:
            body = gzip.decompress(captured)
        except OSError as error:
            unresolved.append((rel, "gzip stream does not decompress: %s" % error))
            continue
        head = body.split(b"</head>", 1)[0]
        hrefs = [match.group(1).decode("utf-8", "replace")
                 for match in re.finditer(rb'<link rel="reference" href="([^"]*)"', head)]
        if hrefs != record.get("references", []):
            unresolved.append((rel, "reference links differ from the restore record"))
            continue
        if xhtml1.build_transform(source, source_path, hrefs) != body:
            unresolved.append((rel, "build transform of the source is not byte-exact"))
            continue
        proven.append((rel, record, full, captured, body))

    # Re-run: records already decompressed must still match their recorded digests.
    drift = []
    for record in manifest["files"]:
        capture = record.get("twinCapture")
        if isinstance(capture, dict) and record["twin"] not in bodies:
            full = os.path.join(xhtml1.OFFICIAL, record["twin"].replace("/", os.sep))
            if not os.path.exists(full) or sha256(open(full, "rb").read()) != capture["sha256"]:
                drift.append(record["twin"])

    print("proven byte-exact against the source build transform: %d; unresolved: %d; "
          "drifted: %d" % (len(proven), len(unresolved), len(drift)))
    for rel, why in unresolved:
        print("  UNRESOLVED %s: %s" % (rel, why))
    for rel in drift:
        print("  DRIFT %s: no longer the recorded decompressed bytes" % rel)
    if unresolved or drift:
        raise SystemExit("refusing to rewrite a partially proven tree")
    if args.dry_run or not proven:
        for rel, _record, _full, captured, body in proven:
            print("  %s: %d -> %d bytes" % (rel, len(captured), len(body)))
        return

    for rel, record, full, captured, body in proven:
        with open(full, "wb") as handle:
            handle.write(body)
        record["twinCapture"] = {
            "capture": CAPTURE,
            "capturedSha256": sha256(captured),
            "capturedBytes": len(captured),
            "decompressedBy": "scripts/restore-css21-html4-gzip.py",
            "sha256": sha256(body),
            "bytes": len(body),
            "verifiedBy": "gunzip(captured body) equals the xhtml1->html4 build transform of "
                          "the pinned source blob byte for byte",
        }
        print("  %s: %d -> %d bytes" % (rel, len(captured), len(body)))

    with open(xhtml1.MANIFEST_OUT, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=1)
        handle.write("\n")

    new_hash = xhtml1.tree_sha256()
    with open(xhtml1.LOCK, encoding="utf-8") as handle:
        lock = json.load(handle)
    entry = next(suite for suite in lock["suites"] if suite["id"] == xhtml1.LOCK_ID)
    old_hash = entry.get("treeSha256")
    entry["treeSha256"] = new_hash
    with open(xhtml1.LOCK, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(lock, handle, indent=1)
        handle.write("\n")
    print("treeSha256: %s -> %s" % (old_hash, new_hash))


if __name__ == "__main__":
    main()
