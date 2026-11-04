"""Paired native compression of every animation channel in historical Upheaval."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import struct
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET
from BenchmarkW3xCompression import compare, git, run

HISTORICAL = "bacfc69fc3e71c8116d1f22b933b8b6173170ecf"
TREE = "5551fc26a6f96b62613443e0d0fa2958c14cbce0"
NS = "{uri:ea.com:eala:asset}"


def fixture(path):
    if git("rev-parse", HISTORICAL + ":SDK/Mods/Upheaval").decode().strip() != TREE:
        raise RuntimeError("Historical Upheaval tree changed")
    process = subprocess.Popen(["git", "archive", HISTORICAL, "SDK/Mods/Upheaval/Art"], stdout=subprocess.PIPE)
    records, animations, sources, expanded = [], 0, [], 0
    with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
        for member in archive:
            if not member.isfile() or not member.name.lower().endswith((".w3x", ".xml")):
                continue
            data = archive.extractfile(member).read()
            if b"<W3DAnimation" not in data:
                continue
            root = ET.fromstring(data)
            for animation in root.iter(NS + "W3DAnimation"):
                count = int(animation.get("NumFrames"))
                settings = animation.find(NS + "CompressionSettings")
                if settings is None:
                    errors, rate, allow = [.002, .003, .01, .001], 1., 3
                else:
                    errors = [float(settings.get(k, str(v))) for k, v in zip(
                        ("MaxTranslationError", "MaxRotationError", "MaxVisibilityError", "MaxAdaptiveDeltaError"),
                        (.001, .01, .01, .03))]
                    rate = float(settings.get("ForceReductionRate", "1"))
                    allow = sum(bit for key, bit in (("AllowTimeCoded", 1), ("AllowAdaptiveDelta", 2))
                                if settings.get(key, "true") in ("true", "1"))
                    if not allow:
                        allow, errors[:3], rate = 1, [0., 0., 0.], 1.
                for channel in animation.find(NS + "Channels"):
                    kind = channel.tag.removeprefix(NS)
                    if kind not in ("ChannelScalar", "ChannelQuaternion"):
                        raise RuntimeError("Unexpected source channel: " + kind)
                    usage = channel.get("Type", "XTranslation")
                    type_id = ("XTranslation", "YTranslation", "ZTranslation", "Orientation", "Visibility").index(usage)
                    width = 4 if kind == "ChannelQuaternion" else 1
                    default = [0., 0., 0., 1.] if width == 4 else [1. if type_id == 4 else 0.]
                    frames = [(False, default) for _ in range(count)]
                    first = int(channel.get("FirstFrame", "0"))
                    authored = channel.findall(NS + "Frame")
                    if not count or first + len(authored) > count:
                        raise RuntimeError("Undefined source frame range")
                    for i, frame in enumerate(authored, first):
                        values = [float(frame.get(k, "0")) for k in ("X", "Y", "Z", "W")] if width == 4 else [float(frame.text)]
                        frames[i] = (frame.get("BinaryMove", "false") in ("true", "1"), values)
                    record = bytearray(struct.pack("<5I5f", type_id, int(channel.get("Pivot", "0")) & 65535,
                                                   count, width, allow, *errors, rate))
                    for binary, values in frames:
                        record.extend(struct.pack("<I" + "f" * width, int(binary), *values))
                    records.append(record)
                    expanded += count
                animations += 1
            sources.append({"path": member.name, "sha256": hashlib.sha256(data).hexdigest()})
    if process.wait() != 0:
        raise RuntimeError("Historical source extraction failed")
    if (animations, len(records), expanded) != (194, 7390, 468946):
        raise RuntimeError("Pinned source coverage changed")
    with path.open("wb") as stream:
        stream.write(struct.pack("<I", len(records)))
        for record in records:
            stream.write(record)
    return {"sourceCommit": HISTORICAL, "tree": TREE, "animations": animations,
            "channels": len(records), "expandedFrames": expanded,
            "fixtureSha256": hashlib.sha256(path.read_bytes()).hexdigest(), "sources": sources}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline-ref", default="436115db594c405c1650d27754594454eed54bdc")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    os.chdir(Path(__file__).resolve().parents[2])
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    provenance = fixture(out / "input.bin")
    (out / "input.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    result = {"succeeded": False, "sourceCommit": git("rev-parse", "HEAD").decode().strip(),
              "baselineCommit": git("rev-parse", args.baseline_ref).decode().strip(),
              "workingTreeDirty": bool(git("status", "--porcelain", "--untracked-files=no").strip()),
              "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
              "input": {k: v for k, v in provenance.items() if k != "sources"},
              "scope": "All historical Upheaval native channel compression and original format selection; XML parsing, preparation and wire encoding excluded from timing. Full selected wire bytes compared in both endian modes; not a full MOD/SDK build.",
              "warmups": 1, "measuredPairs": 3, "samplesMs": {"baseline": [], "optimized": []}}
    with tempfile.TemporaryDirectory(prefix="w3x-historical-") as scratch:
        executables = {}
        for label in ("baseline", "optimized"):
            root = Path(scratch) / label
            native = root / "recovery/src/Native/W3x"
            native.mkdir(parents=True)
            for name in ("Timecoded.cpp", "Timecoded.h", "AdaptiveDelta.cpp", "AdaptiveDelta.h"):
                path = Path("recovery/src/Native/W3x") / name
                (native / name).write_bytes(git("show", f"{result['baselineCommit']}:{path.as_posix()}") if label == "baseline" else path.read_bytes())
            test = root / "recovery/tests/W3xHistoricalCompression.cpp"
            test.parent.mkdir(parents=True)
            shutil.copyfile("recovery/tests/W3xHistoricalCompression.cpp", test)
            exe = root / ("historical.exe" if os.name == "nt" else "historical")
            if os.name == "nt":
                command = ["cl", "/nologo", "/EHsc", "/std:c++14", "/O2", "/fp:precise", "/MD",
                           f"/Fe{exe}", f"/Fo{root}/"] + (["/DW3X_BASELINE"] if label == "baseline" else [])
            else:
                command = ["g++", "-std=c++14", "-O2", "-ffp-contract=off", "-o", exe] + (["-DW3X_BASELINE"] if label == "baseline" else [])
            command += [test, native / "Timecoded.cpp", native / "AdaptiveDelta.cpp"]
            with (out / f"{label}-build.log").open("w") as log:
                run(command, stdout=log, stderr=subprocess.STDOUT)
            executables[label] = exe
        for trial in range(-1, 3):
            for label in (("baseline", "optimized") if trial % 2 == 0 else ("optimized", "baseline")):
                prefix = f"{label}-{trial}"
                with (out / f"{prefix}.log").open("w") as log:
                    run([executables[label], out / "input.bin", out / f"{prefix}.bin", out / f"{prefix}.csv"], stdout=log)
                with (out / f"{prefix}.csv").open() as stream:
                    row = next(csv.DictReader(stream))
                if trial >= 0:
                    result["samplesMs"][label].append(float(row["milliseconds"]))
                result["selectedFormats"] = {k: int(row[k]) for k in ("channels", "timecoded", "delta")}
            result.setdefault("outputSha256", {})[str(trial)] = compare(out / f"baseline-{trial}.bin", out / f"optimized-{trial}.bin")
        result["baselineMs"] = statistics.median(result["samplesMs"]["baseline"])
        result["optimizedMs"] = statistics.median(result["samplesMs"]["optimized"])
        result["speedup"] = result["baselineMs"] / result["optimizedMs"]
        result["succeeded"] = True
        (out / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
