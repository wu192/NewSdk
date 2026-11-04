"""Paired full Upheaval builds with an exact production change allowlist."""
import argparse
import ctypes
import filecmp
import json
import os
from pathlib import Path
import statistics
import subprocess
import tarfile
import time
from BenchmarkW3xCompression import git
from MeasureCompilerModScale import HISTORICAL, UPHEAVAL_TREE, sha, write

BASELINE = "436115db594c405c1650d27754594454eed54bdc"
OPTIMIZED = "4e9c9a279bef892439822310976e87081ca0ce72"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline-build", required=True, type=Path)
    parser.add_argument("--optimized-build", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--baseline-source", default=BASELINE)
    parser.add_argument("--optimized-source", default=OPTIMIZED)
    parser.add_argument("--production-file", action="append",
                        help="Exact allowed production change; repeat for each file. Default retains the six-file W3X gate.")
    parser.add_argument("--pairs", type=int, default=2)
    args = parser.parse_args()
    if args.pairs < 1:
        parser.error("--pairs must be positive")
    repo = Path(__file__).resolve().parents[2]
    os.chdir(repo)
    if os.name == "nt":
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    work, out = args.work.resolve(), args.output.resolve()
    work.mkdir(parents=True, exist_ok=False)
    out.mkdir(parents=True, exist_ok=False)
    builds = {"baseline": args.baseline_build.resolve(), "optimized": args.optimized_build.resolve()}
    binaries = {}
    for label, source in (("baseline", args.baseline_source), ("optimized", args.optimized_source)):
        build = builds[label]
        receipt = json.loads((build / "build.json").read_text(encoding="utf-8-sig"))
        exe = build / "bin/NewModCompiler.exe"
        if receipt["sourceCommit"] != source or receipt["configuration"] != "Release" or sha(exe) != receipt["exeSha256"]:
            raise RuntimeError("Production source/binary identity differs: " + label)
        binaries[label] = exe
        write(out / (label + "-build.json"), receipt)
    changes = git("diff", "--name-only", args.baseline_source, args.optimized_source, "--", "SDK", "recovery/src").decode().splitlines()
    if args.production_file:
        allowed = set(args.production_file)
        if set(changes) != allowed or len(allowed) != len(args.production_file):
            raise RuntimeError("Production comparison differs from exact file allowlist")
    elif len(changes) != 6 or any(not p.startswith("recovery/src/Native/W3x/") for p in changes):
        raise RuntimeError("Production comparison gained unrelated changes")
    if git("rev-parse", HISTORICAL + ":SDK/Mods/Upheaval").decode().strip() != UPHEAVAL_TREE:
        raise RuntimeError("Historical full MOD tree changed")
    archive = work / "mod.tar"
    with archive.open("wb") as stream:
        subprocess.run(["git", "archive", HISTORICAL, "SDK/Mods/Upheaval"], stdout=stream, check=True)
    with tarfile.open(archive) as stream:
        stream.extractall(work / "input", filter="data")
    archive.unlink()
    mod = work / "input/SDK/Mods/Upheaval"
    state = {"sourceCommit": git("rev-parse", "HEAD").decode().strip(), "runId": os.environ.get("GITHUB_RUN_ID"),
             "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"), "succeeded": False,
             "baselineBinarySource": args.baseline_source, "optimizedBinarySource": args.optimized_source,
             "binarySha256": {k: sha(v) for k, v in binaries.items()}, "productionChanges": changes,
             "inputSource": HISTORICAL, "inputTree": UPHEAVAL_TREE, "jobs": 4,
             "scope": "Complete historical Upheaval build, auto-discovered content, BIG/SKUDEF and Additional; same-VM pairs, caches disabled, fresh output each time; raw published files compared. Not original BAB or game-runtime acceptance.",
             "sampleCaution": f"{args.pairs} samples per implementation; pair order alternates, OS page cache uncontrolled; no warmup.",
             "samples": []}
    write(out / "measurement.json", state)
    published = {}
    for trial in range(args.pairs):
        for label in (("baseline", "optimized") if trial % 2 == 0 else ("optimized", "baseline")):
            name = f"{label}-{trial}"
            evidence = out / name
            evidence.mkdir()
            destination = work / "builds" / name
            command = [str(binaries[label]), "build", "--sdk-root", str(repo / "SDK"), "--mod", str(mod),
                       "--out", str(destination), "--jobs", "4", "--sc=false", "--bc=false", "--performance", "off"]
            start = time.perf_counter()
            with (evidence / "stdout.log").open("wb") as stdout, (evidence / "stderr.log").open("wb") as stderr:
                process = subprocess.run(command, cwd=binaries[label].parent, stdout=stdout, stderr=stderr, timeout=240)
            wall = (time.perf_counter() - start) * 1000
            report = json.loads((destination / "build-report.json").read_text(encoding="utf-8-sig"))
            write(evidence / "build-report.json", report)
            row = {"label": label, "trial": trial, "wallMs": wall, "exitCode": process.returncode,
                   "status": report["status"], "command": command}
            state["samples"].append(row)
            write(out / "measurement.json", state)
            if process.returncode or report["status"] != "succeeded":
                raise RuntimeError("Full MOD failed: " + name)
            directory = destination / "published"
            files = {p.relative_to(directory).as_posix(): p for p in directory.rglob("*") if p.is_file()}
            if not files:
                raise RuntimeError("No published files")
            write(evidence / "published.json", [{"path": k, "bytes": p.stat().st_size, "sha256": sha(p)} for k, p in sorted(files.items())])
            published[(label, trial)] = files
        a, b = published[("baseline", trial)], published[("optimized", trial)]
        if a.keys() != b.keys() or any(not filecmp.cmp(a[k], b[k], shallow=False) for k in a):
            raise RuntimeError("Full raw published output differs")
        state.setdefault("equalPublishedPairs", []).append(trial)
        write(out / "measurement.json", state)
    state["baselineMs"] = statistics.median(r["wallMs"] for r in state["samples"] if r["label"] == "baseline")
    state["optimizedMs"] = statistics.median(r["wallMs"] for r in state["samples"] if r["label"] == "optimized")
    state["speedup"] = state["baselineMs"] / state["optimizedMs"]
    state["succeeded"] = True
    write(out / "measurement.json", state)
    print(json.dumps(state, indent=2))


if __name__ == "__main__":
    main()
