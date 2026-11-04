#!/usr/bin/env python3
"""Copy original TNW 7.1 TGA codec cases; never normalize their image bytes."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mod', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    mod, output = args.mod.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    cases = []

    def add(name, data, source, original=True, candidate=True):
        filename = 'image-%04d.tga' % len(cases)
        (output / filename).write_bytes(data)
        cases.append(dict(name=name, file=filename, source=source,
                          inputSha256=hashlib.sha256(data).hexdigest(),
                          expectedOriginal=original, expectedCandidate=candidate,
                          compareBytes=original is True and candidate))

    base = ('Art/Structures/VIPBunkerAll/VIPEMP.tga',
            'Art/UI/GameSetup_flag_Allied.tga', 'Art/UI/GameSetup_flag_Soviet.tga')
    for name in base:
        add(name, (mod / name).read_bytes(), name)
    apt = []
    for path in sorted(p for p in (mod / 'Data/AptUI').rglob('*') if p.suffix.lower() == '.tga'):
        data = path.read_bytes()
        if len(data) >= 18 and data[1] == 0 and data[2] in (2, 10) and any(data[3:8]):
            name = path.relative_to(mod).as_posix()
            apt.append(name)
            add(name, data, name)
    if len(apt) != 234:
        raise ValueError('Expected all 234 original APT TGA headers, found %d. Do not pass the normalized benchmark copy.' % len(apt))
    # These two streams use packed atlases and previously differed in raw stream
    # bytes. Their textures explicitly request A8R8G8B8, so test the actual large
    # atlas inputs before attributing a difference to a block compressor.
    packed, packed_entries = [], []
    for stem in ('fe_shared_mainMenuLib', 'TacticalHUDRadar'):
        entry = mod / 'Data/AptUI' / stem / (stem + '.xml')
        for element in ET.parse(entry).getroot():
            if element.tag.rsplit('}', 1)[-1] != 'Texture':
                continue
            path = entry.parent / element.attrib['File'].replace('\\', '/')
            name = path.relative_to(mod).as_posix()
            packed_entries.append(name)
            if name in base or name in apt or name in packed:
                continue
            if element.attrib.get('OutputFormat') != 'A8R8G8B8':
                raise ValueError('Expected the original uncompressed packed texture format: ' + name)
            packed.append(name)
            add(name, path.read_bytes(), name)
    if len(packed_entries) != 17 or len(packed) != 8:
        raise ValueError('Expected all 17 original packed-atlas TGA inputs')
    real_cases = len(cases)

    # Real writer with a full, footer-addressed extension whose size word is 0.
    original = (mod / base[0]).read_bytes()
    width, height = struct.unpack_from('<HH', original, 12)
    pixel_end = 18 + original[0] + width * height * (original[16] // 8)
    footer = len(original) - 26
    extension, developer = struct.unpack_from('<II', original, footer)
    if not (original[2] == 2 and extension == pixel_end and footer - extension == 495
            and developer == 0 and original[extension:extension + 2] == b'\0\0'
            and original[-18:] == b'TRUEVISION-XFILE.\0'):
        raise ValueError('VIPEMP does not match the original zero-size TGA extension fixture')
    standard = bytearray(original)
    struct.pack_into('<H', standard, extension, 495)
    add('standard-495-byte-extension', standard, base[0])

    # Existing metadata-region rejection stays explicit. The original loader
    # ignores some malformed metadata, so record its decision without asserting
    # parity for these already out-of-subset inputs.
    for name, at, value, fmt in (
        ('wrong-extension-size', extension, 494, '<H'),
        ('extension-points-into-pixels', footer, pixel_end - 1, '<I'),
        ('extension-points-outside-file', footer, len(original) + 1, '<I'),
        ('developer-points-outside-file', footer + 4, len(original) + 1, '<I'),
    ):
        malformed = bytearray(standard)
        struct.pack_into(fmt, malformed, at, value)
        add(name, malformed, base[0], original=None, candidate=False)
    add('truncated-extension-region', standard[:footer - 1] + standard[footer:],
        base[0], original=None, candidate=False)
    add('truncated-pixel-data', original[:pixel_end - 1], base[0], original=None, candidate=False)
    add('truncated-header', original[:17], base[0], original=False, candidate=False)

    # Original 41259222360 rejects these nonempty palette specifications on
    # 16-bit truecolor and grayscale. Keep the existing rejection boundary.
    for name, image_type, depth, pixels in (
        ('unmapped-16-bit-palette-fields', 2, 16, b'\x1f\xfc' * 16),
        ('unmapped-grayscale-palette-fields', 3, 8, bytes(range(16))),
    ):
        header = bytearray(18)
        header[2] = image_type
        struct.pack_into('<HHB', header, 3, 29, 7, 32)
        struct.pack_into('<HH', header, 12, 4, 4)
        header[16] = depth
        add(name, header + pixels, 'generated nonempty-color-map-specification boundary',
            original=False, candidate=False)
    (output / 'cases.json').write_text(json.dumps(cases, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(status='prepared', realSourceCases=real_cases, boundaryCases=len(cases) - real_cases,
                          totalCases=len(cases), sourceBytesUnchanged=True)))


if __name__ == '__main__':
    main()
