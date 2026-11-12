#!/usr/bin/env python3
"""Merge sharded --suite html5 evidence and write the html5 baseline report.

Reads one or more ExecutionEvidence reports (html5-results-<i>-of-<n>.json), verifies
they share one identity and completed cleanly, merges them into the single report the
profile gate and the review scripts consume, and writes:

  - artifacts/html5-results.json    merged evidence (format version 6, one identity)
  - artifacts/html5-baseline.md     executed-outcome summary + failing clusters
  - artifacts/html5-baseline.csv    every reviewed case with suite, cluster, outcome

The reviewed-gate count repeats the runner's own acceptance rule (HtmlApplicability):
an included entry passes only when every subtest passes, and the single mixed entry
passes only when its assertion inventory accounts for every subtest and every target
assertion passed. This is a diagnostic report of executed outcomes, not a readiness
claim; readiness comes from the reviewed obligations and their evidence.

A strict merge refuses reports from more than one build identity, because evidence is
only usable for the sources that produced it. --diagnostic accepts a checkout edited
during the run (a parallel track writing source files drifts the identity between
phases): the report then lists the identity slices and writes no merged evidence, so a
drifted run can still feed the work queue without being mistaken for banked evidence."""

from __future__ import annotations

import argparse
import csv
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "Lite.Conformance" / "artifacts"
APPLICABILITY = ROOT / "Lite.Conformance" / "Wpt" / "html5-applicability.json"
MANIFEST = ARTIFACTS / "wpt-manifest.json"

OUTCOME_ORDER = ["pass", "fail", "crash", "timeout", "empty", "unsupported", "manual-pending"]


def cluster_of(path: str) -> str:
    """Group the review queue the way the engine work needs it: two path segments,
    and the encoding legacy-mb families one segment deeper (each is its own charset)."""
    parts = path.split("/")
    if len(parts) >= 3 and parts[0] == "encoding" and parts[1].startswith("legacy-mb"):
        return "/".join(parts[:3])
    return "/".join(parts[:2]) if len(parts) >= 2 else path


def walk_manifest(node: dict, prefix: str, out: dict[str, str]) -> None:
    for name, child in node.items():
        source = prefix + name
        if isinstance(child, dict):
            walk_manifest(child, source + "/", out)
            continue
        for item in child[1:]:
            out[(item[0] or source).lstrip("/")] = source


def source_index() -> dict[str, str]:
    """URL path -> manifest source, so a variant path finds its reviewed entry."""
    if not MANIFEST.exists():
        return {}
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    out: dict[str, str] = {}
    for kind, tree in manifest.get("items", {}).items():
        if kind in ("support", "spec"):
            continue
        walk_manifest(tree, "", out)
    return out


def accepted(test: dict, review: dict | None) -> bool:
    """Mirror of HtmlApplicability.HasPassingAssertions for the reviewed-gate count."""
    if review is None:
        return False
    subtests = test.get("subtests") or []
    if review.get("classification") != "mixed":
        return (test.get("outcome") == "pass" and bool(subtests)
                and all(subtest.get("status") == 0 for subtest in subtests))
    if test.get("outcome") not in ("pass", "fail") or review.get("assertionInventoryComplete") is not True:
        return False
    expected = review.get("assertions") or []
    if not expected or len(expected) != len(subtests):
        return False
    if len({subtest.get("name") for subtest in subtests}) != len(subtests):
        return False
    if len({assertion.get("name") for assertion in expected}) != len(expected):
        return False
    if not any(assertion.get("classification") == "included" for assertion in expected):
        return False
    actual = {subtest.get("name"): subtest for subtest in subtests}
    for assertion in expected:
        found = actual.get(assertion.get("name"))
        classification = assertion.get("classification")
        if found is None or not assertion.get("reason") or classification not in (
                "included", "post-target", "regression-only", "profile-excluded"):
            return False
        if found.get("status") not in (0, 1):
            return False
        if classification == "included" and found.get("status") != 0:
            return False
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reports", nargs="+", type=Path, help="html5 shard evidence JSON files")
    parser.add_argument("--output", type=Path, default=ARTIFACTS / "html5-baseline.md")
    parser.add_argument("--evidence", type=Path, default=ARTIFACTS / "html5-results.json")
    parser.add_argument("--note", action="append", default=[],
                        help="coverage note printed in the report header (phase label, excluded family)")
    parser.add_argument("--diagnostic", action="store_true",
                        help="read every report even when several build identities are present "
                             "(a concurrently edited checkout drifts the identity); the report "
                             "then lists the identity slices and no merged evidence is written")
    parser.add_argument("--expected", type=int, default=0,
                        help="size of the reviewed queue's expanded path set, for a coverage line")
    args = parser.parse_args()

    reports: list[dict] = []
    problems: list[str] = []
    for report_path in args.reports:
        try:
            report = json.loads(report_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            problems.append(f"unreadable report {report_path.name}: {error}")
            continue
        if report.get("formatVersion") != 6:
            problems.append(f"{report_path.name}: unexpected format version {report.get('formatVersion')}")
            continue
        report["_name"] = report_path.name
        reports.append(report)
    if not reports:
        for problem in problems:
            print(f"problem: {problem}", file=sys.stderr)
        print("no html5 evidence reports found", file=sys.stderr)
        return 2

    identity = reports[0].get("identity")
    for report in reports:
        if not report.get("completed"):
            problems.append(f"{report['_name']}: evidence is incomplete (sources changed or the shard died)")
        if report.get("identity") != identity:
            problems.append(f"{report['_name']}: identity differs from the first report; "
                            "evidence must come from one build")
    if problems and not args.diagnostic:
        for problem in problems:
            print(f"problem: {problem}", file=sys.stderr)
        print("refusing to merge: no single completed build identity. Use --diagnostic for an "
              "executed-outcome report across slices.", file=sys.stderr)
        return 1

    # A completed report outranks a checkpoint for the same path; within one identity the
    # outcome is the same either way, across slices the completed run is the usable one.
    ordered = sorted(reports, key=lambda report: (not report.get("completed"),
                                                  report.get("finishedUtc", "")))
    tests: list[dict] = []
    seen: dict[str, dict] = {}
    sources: dict[str, str] = {}
    for report in ordered:
        for test in report.get("tests", []):
            prior = seen.get(test["path"])
            if prior is not None:
                # Phases overlap at root boundaries (dom/ also matches html/dom, encoding/ also
                # matches fetch/content-encoding). A re-run that reached the same verdict is
                # redundant, not conflicting; a differing verdict would make the queue
                # ambiguous and stays visible in the report.
                if (prior["outcome"] != test["outcome"] or prior["detail"] != test["detail"]
                        or prior["subtests"] != test["subtests"]):
                    problems.append(f"conflicting outcomes for {test['path']} "
                                    f"({prior['outcome']} in {sources.get(test['path'], '?')} vs "
                                    f"{test['outcome']} in {report['_name']})")
                continue
            seen[test["path"]] = {"outcome": test["outcome"], "detail": test["detail"],
                                  "subtests": test["subtests"]}
            sources[test["path"]] = report["_name"]
            tests.append(test)
    if not tests:
        print("no html5 evidence rows found", file=sys.stderr)
        return 2

    tests.sort(key=lambda test: test["path"])
    started = min(report.get("startedUtc", "") for report in reports)
    finished = max(report.get("finishedUtc", "") for report in reports)
    if not args.diagnostic:
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        merged = {
            "formatVersion": 6,
            "identity": identity,
            "startedUtc": started,
            "finishedUtc": finished,
            "completed": not problems,
            "tests": tests,
        }
        args.evidence.write_text(json.dumps(merged, ensure_ascii=False, separators=(",", ":")) + "\n",
                                 encoding="utf-8")

    slices: dict[str, dict] = {}
    for report in reports:
        key = report.get("identity", {}).get("sourceSha256", "?")[:12]
        entry = slices.setdefault(key, {"revision": report.get("identity", {}).get("sourceRevision", "?")[:12],
                                        "reports": 0, "completed": 0, "cases": 0, "started": started,
                                        "finished": ""})
        entry["reports"] += 1
        entry["completed"] += 1 if report.get("completed") else 0
        entry["cases"] += len(report.get("tests", []))
        entry["started"] = min(entry["started"], report.get("startedUtc", ""))
        entry["finished"] = max(entry["finished"], report.get("finishedUtc", ""))

    reviews = {entry["path"]: entry for entry in
               json.loads(APPLICABILITY.read_text(encoding="utf-8"))["tests"]}
    source_of = source_index()

    rows: list[tuple[str, str, str, str, str, str]] = []
    suite_counts: Counter[str] = Counter()
    cluster_counts: dict[str, Counter[str]] = defaultdict(Counter)
    slice_counts: dict[str, Counter[str]] = defaultdict(Counter)
    gate_accepted = 0
    for test in tests:
        path = test["path"]
        review = reviews.get(path) or reviews.get(source_of.get(path, ""))
        outcome = test.get("outcome", "?")
        if accepted(test, review):
            gate_accepted += 1
        source = next((report for report in reports if report["_name"] == sources.get(path)), {})
        slice_key = source.get("identity", {}).get("sourceSha256", "?")[:12]
        rows.append((test["suite"], cluster_of(path), path, outcome,
                     (test.get("detail") or "")[:160], slice_key))
        suite_counts[outcome] += 1
        cluster_counts[cluster_of(path)][outcome] += 1
        slice_counts[slice_key][outcome] += 1

    csv_path = args.output.with_suffix(".csv")
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    with csv_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["suite", "cluster", "path", "outcome", "detail", "identity"])
        writer.writerows(rows)

    total = len(rows)
    revision = identity["sourceRevision"][:12] if identity else "?"
    lines: list[str] = ["# html5 baseline", ""]
    coverage = f" of {args.expected} in the reviewed queue ({100.0 * total / args.expected:.1f}%)" \
        if args.expected else ""
    lines.append(f"Cases: {total} reviewed applicable tests{coverage} "
                 f"(source {revision}; run {started} .. {finished}).")
    lines.append("")
    lines.append("Executed diagnostics only - applicability review and obligation coverage are")
    lines.append("separate readiness gates; this report feeds the HTML5 engine work queue.")
    lines.append("")
    for note in args.note:
        lines.append(f"- {note}")
    if args.note:
        lines.append("")
    if len(slices) > 1:
        lines.append("## Identity slices")
        lines.append("")
        lines.append("A concurrently edited checkout changes the evidence identity between phases;")
        lines.append("a slice is only usable as profile evidence when every report in it completed.")
        lines.append("")
        lines.append("| identity | source | reports | completed | cases | window (UTC) |")
        lines.append("|---|---|---|---|---|---|")
        for key, entry in sorted(slices.items(), key=lambda item: item[1]["started"]):
            lines.append(f"| {key} | {entry['revision']} | {entry['reports']} | {entry['completed']} | "
                         f"{entry['cases']} | {entry['started'][11:19]}..{entry['finished'][11:19]} |")
        lines.append("")
    # A shard writes a checkpoint every 100 outcomes, so an unfinished report whose case count
    # is an exact multiple of 100 most likely stopped at a checkpoint; a drifted-but-finished
    # report fails verification with a non-multiple count. The distinction is reported rather
    # than acted on, because both are diagnostics here.
    truncated = [(report["_name"], len(report.get("tests", []))) for report in reports
                 if not report.get("completed") and len(report.get("tests", [])) % 100 == 0]
    if truncated:
        lines.append("## Truncated shards (queue stopped at a 100-outcome checkpoint)")
        lines.append("")
        lines.append("| report | cases saved |")
        lines.append("|---|---|")
        for name, cases in sorted(truncated):
            lines.append(f"| {name} | {cases} |")
        lines.append("")
    lines.append(f"## Reviewed gate: {gate_accepted}/{total} accepted "
                 f"({100.0 * gate_accepted / total:.1f}%)")
    lines.append("")
    lines.append("An included entry counts only when every subtest passes; the mixed entry counts")
    lines.append("only when its assertion inventory accounts for every subtest.")
    lines.append("")
    lines.append("| " + " | ".join(OUTCOME_ORDER) + " |")
    lines.append("|" + "---|" * len(OUTCOME_ORDER))
    lines.append("| " + " | ".join(str(suite_counts.get(outcome, 0)) for outcome in OUTCOME_ORDER) + " |")
    lines.append("")
    failing = [(cluster, counts) for cluster, counts in cluster_counts.items()
               if counts.get("pass", 0) < sum(counts.values())]
    worst = sorted(failing, key=lambda item: item[1]["pass"] / sum(item[1].values()))[:40]
    biggest = sorted(failing, key=lambda item: sum(item[1].values()) - item[1]["pass"],
                     reverse=True)[:40]
    for title, selection in (("Largest failing clusters (most non-passing cases first, top 40)", biggest),
                             ("Worst failing clusters (lowest pass rate first, top 40)", worst)):
        lines.append(f"## {title}")
        lines.append("")
        lines.append("| cluster | pass/total | pass % | fail | timeout | empty | crash | unsupported |")
        lines.append("|---|---|---|---|---|---|---|---|")
        for cluster, counts in selection:
            cases = sum(counts.values())
            lines.append(f"| {cluster} | {counts.get('pass', 0)}/{cases} | "
                         f"{100.0 * counts.get('pass', 0) / cases:.1f} | {counts.get('fail', 0)} | "
                         f"{counts.get('timeout', 0)} | {counts.get('empty', 0)} | "
                         f"{counts.get('crash', 0)} | {counts.get('unsupported', 0)} |")
        lines.append("")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"{total} cases; reviewed gate {gate_accepted}/{total}; report: {args.output}")
    if not args.diagnostic:
        print(f"evidence: {args.evidence}")
    print(f"csv: {csv_path}")
    for problem in problems:
        print(f"problem: {problem}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
