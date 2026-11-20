#!/usr/bin/env python3
"""Split a css21-full screen baseline into the Phase 6 lane work lists.

Reads ExecutionEvidence shard reports (css21-full-screen-*-of-N.json) or the aggregated
baseline CSV and writes, under Lite.Conformance/artifacts/lanes/:

  <lane>.txt      the lane's non-passing engine cases (fail/crash/timeout/empty), one path
                  per line -- directly usable as `--filter @<file>` for `--suite css21-full`
  <lane>.tsv      the same cases with suite, cluster, outcome and the runner's detail
  judges.txt/.tsv the unsupported and manual-pending cases (lane 6.M's route queue)
  canary.txt      a fixed, stratified sample of passing cases every lane re-runs for 0 losses
  summary.md      counts per lane and outcome

Lanes are assigned by test-name stem first (both suites share stems through the WPT twins),
then by WPT css/CSS2 directory; anything unmatched goes to lane L (mixed-corpus triage).
This is a routing aid for the work queue, not a conformance claim.
"""

from __future__ import annotations

import argparse
import csv
import glob
import hashlib
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "Lite.Conformance" / "artifacts"

LANES = {
    "A": "inline and line box",
    "B": "floats and clear",
    "C": "block flow and sizing",
    "D": "positioning and viewport",
    "E": "tables",
    "F": "generated content and lists",
    "G": "bidi (wave 2)",
    "H": "backgrounds",
    "I": "borders, outline and visual effects",
    "J": "fonts",
    "K": "selectors, cascade and syntax",
    "L": "mixed-corpus triage",
}

# First match wins. Patterns run against the file-name stem (lower-cased, extension and
# trailing case number removed).
STEM_RULES: list[tuple[str, str]] = [
    ("G", r"bidi|direction|unicode-bidi|rtl"),
    ("E", r"^(table|border-collapse|border-conflict|border-spacing|caption|col-|column|colgroup|"
          r"empty-cells|fixed-table|row-|rows?$|tbody|thead|tfoot|separated-border|collapsed-border|"
          r"anonymous-table|infer|cell|c5525-fltwidth|c5526-fltclr|inline-table|collapsing-(table-)?border|"
          r"basic-css-table)"),
    ("F", r"^(content-[a-z]*count|content|before-after|after-|before-|quotes|counter|list-style|lists?$|"
          r"marker|generated|first-letter|c541|c542|c543|c544-list|c566|c567)"),
    ("A", r"^(first-line|c5502-imrgn|c5504-imrgn|c5506-ipadn|c5507-ipadn|c5508-ipadn|c5509-ipadn|"
          r"c5510-ipadn|c548|leadin|linebox|line-height|vertical-align|c544-valgn|text-|c56[1-4]|"
          r"white-space|letter-spacing|word-spacing|baseline|strut|inline-formatting|inlines?$|"
          r"inline-non-replaced|inline-box|inline-block-baseline|inline-block-valign|hyphen|"
          r"word-break|float-nowrap|c547-indent|c545-txttrans)"),
    ("H", r"^(background|c5[23][0-9]-bg|bg-)"),
    ("J", r"^(font|c527|ahem|c55-fnt|small-caps)"),
    ("K", r"^(selector|specificity|attribute-|attribute$|cascade|at-import|at-rule|at-charset|import|"
          r"syntax|escapes?|ident|uri|character-encoding|default-attribute|lang|pseudo|link-|"
          r"universal|child-|sibling|descendant|first-child|class-|id-|html-attribute|grouping|"
          r"bad-selector|comments?|case-|keywords?|parsing|invalid|inherit|important|"
          r"user-stylesheet|media|css21-errata|color|units|numbers|integers|c1[0-9]|c2[0-9]-|"
          r"c3[0-9]|sgml|xml-|eof|declaration|rule-|ruleset|block-in-ruleset|malformed|unknown-|"
          r"string|counter-style-name|core-syntax|tokenizer|matching-brackets|quoted-keywords|html-precedence|"
          r"dynamic-sibling|dom-hover|character-representation|c6[0-9]-|c7[0-9]-)"),
    ("B", r"^(floats?|floating|clear|margin-collapse-clear|block-formatting|bfc|c5525|c5526|"
          r"c414-flt|c4[0-9][0-9]-flt)"),
    ("D", r"^(abspos|absolute|fixed|position|containing-block|top-|top$|bottom|left-|left$|right-|"
          r"right$|z-index|zindex|stack|relpos|static-position|viewport|root-|overflow|scroll|"
          r"c43-rpl|visibility|dynamic-top-change)"),
    ("I", r"^(border|outline|clip|c5[0-9]{2}-brdr|c5[0-9]{2}-bor|c5[0-9]{2}-otl)"),
    ("C", r"replaced|^(margin|padding|width|height|min-|max-|blocks?$|block-|box|display|"
          r"normal-flow|auto-|content-height|c55|c5[0-9]{2}-mrgn|c5[0-9]{2}-padn|c5[0-9]{2}-wdth|"
          r"c5[0-9]{2}-hght|anonymous-box|run-in|c4[0-9])"),
]

WPT_DIRS = {
    "floats": "B", "floats-clear": "B",
    "normal-flow": "C", "margin-padding-clear": "C", "box": "C", "box-display": "C",
    "positioning": "D", "abspos": "D", "visudet": "D", "visuren": "D", "zindex": "D",
    "stacking-context": "D", "zorder": "D", "visufx": "I",
    "tables": "E",
    "generated-content": "F", "lists": "F", "generate": "F",
    "backgrounds": "H",
    "borders": "I",
    "fonts": "J",
    "selectors": "K", "cascade": "K", "cascade-import": "K", "syntax": "K", "css21-errata": "K",
    "linebox": "A", "text": "A",
    "bidi-text": "G", "i18n": "G",
}

ENGINE_OUTCOMES = {"fail", "crash", "timeout", "empty"}
ROUTE_OUTCOMES = {"unsupported", "manualpending"}


def stem(path: str) -> str:
    name = path.rsplit("/", 1)[-1].lower()
    name = re.sub(r"\.(htm|html|xht|xhtml|xml|svg)$", "", name)
    return re.sub(r"[-_ ]?\d+[a-z]?$", "", name) or name


def lane_of(path: str) -> str:
    s = stem(path)
    for lane, pattern in STEM_RULES:
        if re.search(pattern, s):
            return lane
    parts = path.split("/")
    if len(parts) >= 4 and parts[0] == "css" and parts[1] == "CSS2":
        return WPT_DIRS.get(parts[2], "L")
    return "L"


def cluster_of(suite: str, path: str) -> str:
    if suite == "css21-wpt":
        parts = path.split("/")
        return "/".join(parts[:3]) if len(parts) >= 4 else path
    return stem(path)


def read_cases(sources: list[str]) -> list[dict]:
    cases = []
    for src in sources:
        if src.endswith(".csv"):
            with open(src, encoding="utf-8", newline="") as fh:
                for row in csv.DictReader(fh):
                    cases.append({"suite": row["suite"], "path": row["path"],
                                  "outcome": row["outcome"], "detail": row.get("detail", "")})
        else:
            report = json.loads(Path(src).read_text(encoding="utf-8"))
            if not report.get("completed"):
                sys.exit(f"{src}: shard report is not completed; refusing to split")
            for t in report["tests"]:
                cases.append({"suite": t.get("suite", ""), "path": t["path"],
                              "outcome": t.get("outcome", "?"), "detail": t.get("detail") or ""})
    return cases


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("sources", nargs="*",
                        help="shard JSON reports or a baseline CSV (default: the newest complete shard set)")
    parser.add_argument("--out", default=str(ARTIFACTS / "lanes"))
    parser.add_argument("--canary-per-lane", type=int, default=25)
    args = parser.parse_args()

    sources = args.sources or sorted(glob.glob(str(ARTIFACTS / "css21-full-screen-*-of-*.json")))
    if not sources:
        sys.exit("no shard reports or CSV given")
    cases = read_cases(sources)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    engine: dict[str, list[dict]] = defaultdict(list)
    routes: list[dict] = []
    passing: dict[str, list[str]] = defaultdict(list)
    counts: dict[str, Counter] = defaultdict(Counter)
    for case in cases:
        lane = lane_of(case["path"])
        counts[lane][(case["suite"], case["outcome"])] += 1
        if case["outcome"] in ENGINE_OUTCOMES:
            engine[lane].append(case)
        elif case["outcome"] in ROUTE_OUTCOMES:
            routes.append(case)
        elif case["outcome"] == "pass":
            passing[lane].append(case["path"])

    def write(name: str, rows: list[dict]) -> None:
        rows = sorted(rows, key=lambda c: (c["suite"], c["path"]))
        (out / f"{name}.txt").write_text("".join(c["path"] + "\n" for c in rows), encoding="utf-8")
        with open(out / f"{name}.tsv", "w", encoding="utf-8", newline="") as fh:
            w = csv.writer(fh, delimiter="\t")
            w.writerow(["suite", "cluster", "path", "outcome", "detail"])
            for c in rows:
                w.writerow([c["suite"], cluster_of(c["suite"], c["path"]), c["path"], c["outcome"],
                            " ".join(c["detail"].split())[:300]])

    for lane in LANES:
        write(lane, engine.get(lane, []))
    write("judges", routes)

    # Canary: a deterministic stratified sample of passing cases (hash order, so the sample is
    # stable across runs while the pass set is), plus the official curated-sensitive families.
    canary = []
    for lane in LANES:
        ranked = sorted(passing.get(lane, []), key=lambda p: hashlib.sha1(p.encode()).hexdigest())
        canary.extend(ranked[:args.canary_per_lane])
    (out / "canary.txt").write_text("".join(p + "\n" for p in sorted(canary)), encoding="utf-8")

    lines = ["# Phase 6 lane lists", "", f"Sources: {len(sources)} report(s), {len(cases)} cases.", "",
             "| lane | scope | official fail | WPT fail | official route | WPT route |", "|---|---|---|---|---|---|"]
    for lane, scope in LANES.items():
        c = counts[lane]
        of = sum(c[("css21-official", o)] for o in ENGINE_OUTCOMES)
        wf = sum(c[("css21-wpt", o)] for o in ENGINE_OUTCOMES)
        orr = sum(c[("css21-official", o)] for o in ROUTE_OUTCOMES)
        wr = sum(c[("css21-wpt", o)] for o in ROUTE_OUTCOMES)
        lines.append(f"| {lane} | {scope} | {of} | {wf} | {orr} | {wr} |")
    lines += ["", f"Judges/routes queue (lane M): {len(routes)} cases. Canary: {len(canary)} passing cases."]
    (out / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
