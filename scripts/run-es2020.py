#!/usr/bin/env python3
"""Run eight Windows Test262 shards and report complete, matching ES2020 evidence.

Build Release and fetch the pinned suites before invoking this script. A failing
shard does not prevent the other shards, host checks, or backlog from completing.

The default exit status covers execution: shards, host tests, inventory, and
unchanged runtime files. ``--require-ready`` also gates on the reviewed ES2020
profile. Every invocation gets its own report directory unless --output is
selected explicitly.
"""

import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from datetime import datetime, timezone


ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--output", type=Path, help="Report directory; defaults to a unique run directory")
    parser.add_argument("--dll", type=Path, help="Explicit conformance DLL and adjacent frozen runtime files")
    parser.add_argument("--require-ready", action="store_true", help="Fail unless ES2020 readiness passes")
    parser.add_argument("--evidence", action="append", default=[])
    parser.add_argument("--timeout", type=int, default=6600, help="Hard timeout per shard, in seconds")
    args = parser.parse_args()
    dll = args.dll.resolve() if args.dll else ROOT / "Lite.Conformance/bin" / args.configuration / "net8.0/Lite.Conformance.dll"
    if not dll.is_file() or args.timeout <= 0:
        parser.error("Build Lite.sln first, select an existing --dll and supply a positive timeout")
    output = (args.output or ROOT / "Lite.Conformance/artifacts/es2020" /
        (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + f"-{os.getpid()}")).resolve()
    output.mkdir(parents=True, exist_ok=True)

    def runtime_hashes():
        candidates = [dll, dll.with_suffix(".deps.json"), dll.parent / "Lite.dll",
            dll.parent / "Lite.QuickJs.dll", dll.parent / "runtimes/win-x64/native/litequickjs.dll",
            dll.parent / "runtimes/win-x64/native/litequickjs.dll.build.json"]
        if any(not path.is_file() for path in candidates):
            parser.error("The selected conformance DLL lacks a complete adjacent QuickJS runtime")
        return {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in candidates}

    runtime_before = runtime_hashes()

    def run(name, arguments):
        with (output / (name + ".log")).open("w", encoding="utf-8") as log:
            process = subprocess.Popen(["dotnet", str(dll), *arguments], cwd=ROOT,
                stdout=log, stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            try:
                return process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                if os.name == "nt":
                    subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                        stdout=log, stderr=subprocess.STDOUT, check=False,
                        creationflags=subprocess.CREATE_NO_WINDOW)
                else:
                    process.kill()
                process.wait()
                log.write("\nSupervisor timeout; this run cannot supply complete evidence.\n")
                return 124

    reports = [output / f"test262-{index}.json" for index in range(8)]
    host = output / "host.json"
    # Old reports are never read after an unsuccessful launch in this invocation.
    # Fresh per-invocation filenames are tracked in the status file below as well.
    jobs = [(f"test262-{index}", ["--suite", "test262", "--shard", f"{index}/8",
             "--report", str(report)]) for index, report in enumerate(reports)]
    jobs.append(("host", ["--suite", "es2020-host", "--report", str(host)]))
    started = time.time()
    for report in [*reports, host]:
        # All paths are direct children of the explicitly selected output directory.
        report.unlink(missing_ok=True)
    outcomes = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=9) as pool:
        pending = {pool.submit(run, name, arguments): name for name, arguments in jobs}
        while pending:
            done, _ = concurrent.futures.wait(pending, timeout=30,
                return_when=concurrent.futures.FIRST_COMPLETED)
            for future in done:
                name = pending.pop(future)
                try:
                    outcomes[name] = future.result()
                except Exception as error:
                    outcomes[name] = 125
                    print(f"{name}: supervisor error: {error}", flush=True)
                print(f"{name}: exit {outcomes[name]}", flush=True)
            if pending:
                print(f"ES2020: {len(pending)} runs active, {time.time() - started:.0f}s elapsed", flush=True)
    evidence = [*reports, host, *args.evidence]
    evidence_args = [arg for path in evidence for arg in ("--evidence", str(path))]
    outcomes["inventory"] = run("inventory", ["--suite", "es2020-inventory", "--report",
        str(output / "inventory.json"), *evidence_args])
    # Informational: recorded and printed, deliberately kept out of the exit status.
    readiness = run("profile", ["--suite", "profile", "--require-es2020-ready", "--report",
        str(output / "profile.json"), *evidence_args])
    runtime_unchanged = runtime_hashes() == runtime_before
    if not runtime_unchanged:
        print("ES2020: runtime files changed during execution; evidence is invalid", flush=True)
    print(f"readiness: exit {readiness} ({'gating' if args.require_ready else 'reported'})", flush=True)
    (output / "supervisor.json").write_text(json.dumps({"startedUnix": started,
        "finishedUnix": time.time(), "exitCodes": {**outcomes, "readiness": readiness},
        "gatingRuns": sorted(outcomes) + (["readiness"] if args.require_ready else []),
        "readinessIsGating": args.require_ready, "runtimeUnchanged": runtime_unchanged,
        "runtimeFilesSha256": runtime_before}, indent=2) + "\n",
        encoding="utf-8")
    print(f"ES2020 reports and remaining-work list: {output}", flush=True)
    return 0 if runtime_unchanged and all(code == 0 for code in outcomes.values()) and \
        (not args.require_ready or readiness == 0) else 1


if __name__ == "__main__":
    sys.exit(main())
