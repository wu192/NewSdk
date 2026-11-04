"""Compare complete compression outputs and paired timings on one host."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import tempfile


def run(args, **kwargs):
    return subprocess.run([str(a) for a in args], check=True, **kwargs)


def git(*args):
    return subprocess.check_output(["git", *args])


def compare(a, b):
    digest = hashlib.sha256()
    with a.open("rb") as left, b.open("rb") as right:
        while True:
            x, y = left.read(1024 * 1024), right.read(1024 * 1024)
            if x != y:
                raise RuntimeError(f"Complete output differs: {a} / {b}")
            if not x:
                return digest.hexdigest()
            digest.update(x)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline-ref", default="436115db594c405c1650d27754594454eed54bdc")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--compiler", default="cl" if os.name == "nt" else "g++")
    args = parser.parse_args()
    os.chdir(Path(__file__).resolve().parents[2])
    baseline = git("rev-parse", args.baseline_ref).decode().strip()
    source = git("rev-parse", "HEAD").decode().strip()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    sources = ["Timecoded.cpp", "AdaptiveDelta.cpp"]
    files = sources + ["Timecoded.h", "AdaptiveDelta.h"]
    harness = Path("recovery/tests/W3xCompressionPerformance.cpp")
    results = {"sourceCommit": source, "baselineCommit": baseline,
               "workingTreeDirty": bool(git("status", "--porcelain", "--untracked-files=no").strip()),
               "runId": os.environ.get("GITHUB_RUN_ID"),
               "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
               "succeeded": False, "compiler": args.compiler,
               "scope": "native compression; synthetic complete output comparison, not SDK acceptance",
               "warmups": 1, "measuredPairs": 3, "scenarios": {}}
    with tempfile.TemporaryDirectory(prefix="w3x-compression-") as scratch:
        executables = {}
        for label in ("baseline", "optimized"):
            root = Path(scratch) / label
            native = root / "recovery/src/Native/W3x"
            native.mkdir(parents=True)
            for name in files:
                path = Path("recovery/src/Native/W3x") / name
                data = git("show", f"{baseline}:{path.as_posix()}") if label == "baseline" else path.read_bytes()
                (native / name).write_bytes(data)
            test = root / harness
            test.parent.mkdir(parents=True)
            shutil.copyfile(harness, test)
            exe = root / ("benchmark.exe" if os.name == "nt" else "benchmark")
            if Path(args.compiler).stem.lower() == "cl":
                command = [args.compiler, "/nologo", "/EHsc", "/std:c++14", "/O2", "/fp:precise", "/MD",
                           f"/Fe{exe}", f"/Fo{root}{os.sep}", test, *[native / p for p in sources]]
            else:
                command = [args.compiler, "-std=c++14", "-O2", "-ffp-contract=off", test,
                           *[native / p for p in sources], "-o", exe]
            with (out / f"{label}-build.log").open("w") as log:
                run(command, stdout=log, stderr=subprocess.STDOUT)
            executables[label] = exe
        for label, exe in executables.items():
            with (out / f"{label}-matrix.log").open("w") as log:
                run([exe, "matrix", out / f"{label}-matrix.bin", out / f"{label}-matrix.csv"], stdout=log)
        results["matrixSha256"] = compare(out / "baseline-matrix.bin", out / "optimized-matrix.bin")
        samples = {}
        for trial in range(-1, 3):
            order = ("baseline", "optimized") if trial % 2 == 0 else ("optimized", "baseline")
            for label in order:
                prefix = f"{label}-long-{trial}"
                with (out / f"{prefix}.log").open("w") as log:
                    run([executables[label], "long", out / f"{prefix}.bin", out / f"{prefix}.csv"], stdout=log)
                if trial >= 0:
                    with (out / f"{prefix}.csv").open() as csvfile:
                        for row in csv.DictReader(csvfile):
                            samples.setdefault(row["scenario"], {}).setdefault(label, []).append(float(row["milliseconds"]))
            results.setdefault("longOutputSha256", {})[str(trial)] = compare(
                out / f"baseline-long-{trial}.bin", out / f"optimized-long-{trial}.bin")
        for name, pair in samples.items():
            before, after = statistics.median(pair["baseline"]), statistics.median(pair["optimized"])
            results["scenarios"][name] = {"baselineMs": before, "optimizedMs": after,
                                          "speedup": before / after, "samplesMs": pair}
        results["succeeded"] = True
        (out / "summary.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
