#!/usr/bin/env python3
"""Rebuild the raw RA3 74 APT/CONST from instruction XML; no FLA/AS input."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import tempfile


def word(data: bytes, offset: int) -> int:
    return struct.unpack_from('<I', data, offset)[0]


def constants(values: list[dict], movie_offset: int) -> bytes:
    result = bytearray(b'Apt constant file\x1a\0\0')
    assert len(result) == 20
    result += struct.pack('<III', movie_offset, len(values), 32)
    result += bytes(8 * len(values))
    for i, item in enumerate(values):
        kind, value = item['type'], item['value']
        if kind == 1:
            while len(result) % 4:
                result.append(0)
            value_offset = len(result)
            result += value.encode('utf-8') + b'\0'
            encoded = struct.pack('<I', value_offset)
        elif kind == 6:
            encoded = struct.pack('<f', value)
        elif kind == 7:
            encoded = struct.pack('<i', value)
        elif kind in (0, 2, 3, 4, 5):
            encoded = struct.pack('<I', value)
        else:
            raise ValueError(f'Unsupported APT constant type: {kind}')
        result[32 + 8 * i:40 + 8 * i] = struct.pack('<I', kind) + encoded
    return bytes(result)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--xml2apt', type=Path, required=True,
                        help='RA3 cc3tools xml2apt executable (pinned version documented in editing/migration.json)')
    parser.add_argument('--runner', type=Path,
                        help='Optional executable runner, for example qemu-i386 for the Linux x86 tool')
    parser.add_argument('--out', type=Path, required=True,
                        help='Directory for rebuilt .apt/.const and build receipt; use movie directory to update tracked assets')
    args = parser.parse_args()
    movie = Path(__file__).resolve().parents[1]
    name = 'TacticalHUDCommandModeBar'
    xml = movie / 'editing' / (name + '.apt.xml')
    table_file = movie / 'editing' / (name + '.const.json')
    table = json.loads(table_file.read_text(encoding='utf-8'))
    command = ([str(args.runner.resolve())] if args.runner else []) + [str(args.xml2apt.resolve())]
    with tempfile.TemporaryDirectory(prefix='ra3-power-apt-') as temporary:
        stem = Path(temporary) / name
        stem.with_suffix('.xml').write_bytes(xml.read_bytes())
        subprocess.run(command + [str(stem)], check=True)
        apt = stem.with_suffix('.apt').read_bytes()
    if apt[:12] != b'Apt Data:74\x1a':
        raise ValueError('xml2apt did not emit the expected RA3 Apt Data:74 layout')
    signature = struct.pack('<II', 9, 0x09876543)
    candidates = [i for i in range(16, len(apt) - 60, 4) if apt[i:i + 8] == signature]
    if len(candidates) != 1:
        raise ValueError(f'Expected one main movie record, found {len(candidates)}')
    movie_offset = candidates[0]
    const = constants(table['values'], movie_offset)
    output = args.out.resolve()
    output.mkdir(parents=True, exist_ok=True)
    for extension, data in (('.apt', apt), ('.const', const)):
        (output / (name + extension)).write_bytes(data)
    def identity(path: Path) -> dict:
        data = path.read_bytes()
        return {'file': path.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
    receipt = {
        'format': 'Apt Data:74', 'movieOffset': movie_offset,
        'sourceXml': identity(xml), 'sourceConstants': identity(table_file),
        'xml2apt': identity(args.xml2apt.resolve()),
        'assets': [identity(output / (name + ext)) for ext in ('.apt', '.const')],
        'characters': word(apt, movie_offset + 20),
        'exports': word(apt, movie_offset + 48),
        'imports': word(apt, movie_offset + 40),
        'constants': len(table['values']),
        'inputFormat': 'APT instruction XML and typed CONST values; no FLA/AS',
        'sdkEntryPoint': name + '.xml (AptAptData/AptConstData/AptDatData/AptGeometryData)',
        'gameRuntimeTested': False,
    }
    (output / 'power-toggle-apt-build.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(f'Rebuilt {name}: {len(apt)} APT bytes, {len(const)} CONST bytes, {receipt["characters"]} characters')


if __name__ == '__main__':
    main()
