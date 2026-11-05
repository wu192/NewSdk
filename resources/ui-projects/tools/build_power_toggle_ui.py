#!/usr/bin/env python3
"""Publish the Power Toggle HUD movie and package its five runtime stream files."""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import subprocess
from pathlib import Path


def write_big(path: Path, entries: dict[str, bytes]) -> None:
    """Use the same uncompressed BIG4/L219 layout as Application/MakeBig.cs."""
    ordered = sorted(entries.items())
    header_length = 24 + sum(8 + len(name.encode("ascii")) + 1 for name, _ in ordered)
    total = header_length + sum(len(data) for _, data in ordered)
    if total > 0xFFFFFFFF:
        raise ValueError("BIG archive exceeds its 32-bit format")
    with path.open("wb") as stream:
        stream.write(b"BIG4" + struct.pack("<I", total))
        stream.write(struct.pack(">II", len(ordered), header_length))
        offset = header_length
        for name, data in ordered:
            stream.write(struct.pack(">II", offset, len(data)))
            stream.write(name.encode("ascii") + b"\0")
            offset += len(data)
        stream.write(b"L219\0\0\0\0")
        for _, data in ordered:
            stream.write(data)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--skip-compiler-build", action="store_true",
                        help="Reuse the already built Release compiler; the UI movie is still republished")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[3]
    project = repository / "resources/ui-projects/Tactical/project.json"
    compiler_project = repository / "resources/ui-projects/tools/UiProject.Build/UiProject.Build.csproj"
    compiler = compiler_project.parent / "bin/Release/net8.0/UiProject.Build.dll"
    output = args.out.resolve()
    if not args.skip_compiler_build:
        subprocess.run([args.dotnet, "build", str(compiler_project), "-c", "Release",
                        "-p:UseSharedCompilation=false", "-v:quiet"], check=True)
    subprocess.run([args.dotnet, str(compiler), str(project), "--out", str(output / "streams"),
                    "--movie", "TacticalHUDCommandModeBar"], check=True)
    build = json.loads((output / "streams/build-report.json").read_text(encoding="utf-8"))
    movie = build["movies"][0]
    if build["failed"] or movie["status"] != "passed":
        raise RuntimeError("UI publisher did not produce a successful movie")
    stem = output / "streams/TacticalHUDCommandModeBar/TacticalHUDCommandModeBar"
    entries = {
        "data\\aptui\\tacticalhudcommandmodebar" + extension: stem.with_suffix(extension).read_bytes()
        for extension in (".manifest", ".bin", ".relo", ".imp")
    }
    # This is the exact APT .version payload used by the SDK's existing packager.
    entries["data\\aptui\\tacticalhudcommandmodebar.version"] = b" \r\n"
    package = output / "PowerToggleUI.big"
    write_big(package, entries)
    (output / "PowerToggleUI_1.0.skudef").write_bytes(
        b"mod-game 1.12\r\nadd-big PowerToggleUI.big\r\n")
    receipt = {
        "movie": movie["movie"], "assets": movie["assets"],
        "projectSha256": build["projectSha256"], "masterSha256": build["masterSha256"],
        "compilerSha256": build["compilerSha256"], "streams": movie["streams"],
        "big": {"file": package.name, "bytes": package.stat().st_size,
                "sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
                "entries": [{"name": name, "bytes": len(data)} for name, data in sorted(entries.items())]},
        "nativeProtocol": "PowerToggle v1: Ready,<root> / Toggle / Unload,<root>; SetPowerModeState(active,enabled)",
        "scope": "HUD stream package only; requires matching EnhancerCorona and any separately compiled cursor aliases.",
        "gameRuntimeTested": False,
    }
    (output / "power-toggle-ui-build.json").write_text(
        json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Published {package} ({len(entries)} entries)")


if __name__ == "__main__":
    main()
