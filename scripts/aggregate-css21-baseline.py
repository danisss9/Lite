#!/usr/bin/env python3
"""Aggregate css21-full screen baseline shards into a failure-cluster report.

Reads one or more ExecutionEvidence reports (css21-full-screen-*.json), verifies they
share one identity and completed cleanly, and writes:

  - artifacts/css21-full-screen-baseline.md   human-readable summary + top failing clusters
  - artifacts/css21-full-screen-baseline.csv  every case with suite, cluster, outcome

The cluster column groups cases the way the engine work queue needs them: WPT cases by
their css/CSS2 subdirectory, official-suite cases by the test-name stem (the shared
prefix before the trailing number, e.g. abspos-containing-block-initial-004a.htm ->
abspos-containing-block). This is a diagnostic report of executed outcomes, not a
conformance claim; readiness comes from the reviewed inventory gates."""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "Lite.Conformance" / "artifacts"

OUTCOME_ORDER = ["pass", "fail", "crash", "timeout", "empty", "unsupported"]


def official_cluster(path: str) -> str:
    name = path.rsplit("/", 1)[-1]
    name = re.sub(r"\.(htm|html)$", "", name)
    stem = re.sub(r"[-_ ]?\d+[a-z]?$", "", name)
    return stem or name


def wpt_cluster(path: str) -> str:
    parts = path.split("/")
    if len(parts) >= 3 and parts[0] == "css" and parts[1] == "CSS2":
        return "/".join(parts[:3])
    return "/".join(parts[:-1]) or path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reports", nargs="+", type=Path, help="css21-full screen evidence JSON files")
    parser.add_argument("--output", type=Path, default=ARTIFACTS / "css21-full-screen-baseline.md")
    args = parser.parse_args()

    identity = None
    started = ""
    finished = ""
    rows: list[tuple[str, str, str, str, str]] = []
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
        if not report.get("completed"):
            problems.append(f"{report_path.name}: evidence is incomplete (sources changed during the run)")
        if identity is None:
            identity = report.get("identity")
            started = report.get("startedUtc", "")
            finished = report.get("finishedUtc", "")
        elif report.get("identity") != identity:
            problems.append(f"{report_path.name}: identity differs from the first report; "
                            "evidence must come from one build")
        for test in report.get("tests", []):
            if test.get("css", {}).get("media") != "screen":
                continue
            suite = test["suite"]
            path = test["path"]
            cluster = wpt_cluster(path) if suite == "css21-wpt" else official_cluster(path)
            rows.append((suite, cluster, path, test.get("outcome", "?"), test.get("detail", "")))
    if problems:
        for problem in problems:
            print(f"problem: {problem}", file=sys.stderr)
    if not rows:
        print("no screen evidence rows found", file=sys.stderr)
        return 2

    rows.sort(key=lambda row: (row[0], row[1], row[2]))
    csv_path = args.output.with_suffix(".csv")
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    with csv_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["suite", "cluster", "path", "outcome", "detail"])
        writer.writerows(rows)

    suites: dict[str, Counter] = defaultdict(Counter)
    clusters: dict[tuple[str, str], Counter] = defaultdict(Counter)
    for suite, cluster, _path, outcome, _detail in rows:
        suites[suite][outcome] += 1
        clusters[(suite, cluster)][outcome] += 1

    lines: list[str] = []
    lines.append("# css21-full screen baseline")
    lines.append("")
    lines.append(f"Cases: {len(rows)} across {len(suites)} suites"
                 f" (source {identity['sourceRevision'][:12] if identity else '?'};"
                 f" run {started} .. {finished}).")
    lines.append("")
    lines.append("Executed diagnostics only - applicability review and obligation coverage are")
    lines.append("separate readiness gates; this report feeds the engine work queue.")
    lines.append("")
    for suite in sorted(suites):
        counts = suites[suite]
        total = sum(counts.values())
        passed = counts["pass"]
        lines.append(f"## {suite}: {passed}/{total} passed ({100.0 * passed / total:.1f}%)")
        lines.append("")
        lines.append("| " + " | ".join(OUTCOME_ORDER) + " |")
        lines.append("|" + "---|" * len(OUTCOME_ORDER))
        lines.append("| " + " | ".join(str(counts.get(outcome, 0)) for outcome in OUTCOME_ORDER) + " |")
        lines.append("")
        failing = [(cluster, counts2) for (suite2, cluster), counts2 in clusters.items()
                   if suite2 == suite and counts2.get("pass", 0) < sum(counts2.values())]
        failing.sort(key=lambda item: item[1]["pass"] / sum(item[1].values()))
        lines.append(f"Top failing clusters in {suite} (lowest pass rate first, worst 40):")
        lines.append("")
        lines.append("| cluster | pass/total | pass % |")
        lines.append("|---|---|---|")
        for cluster, counts2 in failing[:40]:
            total2 = sum(counts2.values())
            lines.append(f"| {cluster} | {counts2.get('pass', 0)}/{total2} | {100.0 * counts2.get('pass', 0) / total2:.1f} |")
        lines.append("")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(rows)} cases; report: {args.output}")
    print(f"csv: {csv_path}")
    if problems:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
