#!/usr/bin/env python3
"""Import the official WorldBuilder 1.12 reference stream into Git-sized blocks.

The input is WBData_12.big extracted from the linked InstallShield installer.
`unpack` restores the exact original .bin for the original SDK resolver oracle;
the production resolver reads compressed blocks directly and needs no restore.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import tempfile
import zlib

ROOT = Path(__file__).resolve().parents[2]
SOURCE_URL = 'https://files.cncnz.com/red_alert_3/modding/CCRA3WorldBuilder_ModSDKv112.zip'
BLOCK_SIZE = 8 * 1024 * 1024
EXTENSIONS = ('.manifest', '.bin', '.relo', '.imp')


def file_identity(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(BLOCK_SIZE), b''):
            digest.update(chunk)
    return {'file': path.name, 'bytes': path.stat().st_size, 'sha256': digest.hexdigest()}


def read_big_table(path):
    with path.open('rb') as stream:
        header = stream.read(16)
        if len(header) != 16 or header[:4] not in (b'BIG4', b'BIGF'):
            raise ValueError('Expected an EA BIG4/BIGF archive')
        count, table_end = struct.unpack_from('>II', header, 8)
        table = stream.read(table_end - 16)
    result, cursor, length = {}, 0, path.stat().st_size
    for _ in range(count):
        offset, size = struct.unpack_from('>II', table, cursor)
        cursor += 8
        end = table.index(0, cursor)
        name = table[cursor:end].decode('ascii').replace('\\', '/').lower()
        cursor = end + 1
        if offset + size > length or name in result:
            raise ValueError('Invalid or duplicate BIG entry: ' + name)
        result[name] = (offset, size)
    return result


def read_entry(big, entry):
    offset, length = entry
    with big.open('rb') as stream:
        stream.seek(offset)
        data = stream.read(length)
    if len(data) != length:
        raise ValueError('Incomplete BIG entry')
    return data


def unpack(parts, destination):
    """Restore, authenticate, and atomically install the original binary stream."""
    parts, destination = Path(parts), Path(destination)
    index = json.loads((parts / 'index.json').read_text(encoding='utf-8'))
    if index['schemaVersion'] != 1 or index['blockSize'] != BLOCK_SIZE:
        raise ValueError('Unsupported WorldBuilder block index')
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + '.unpacking')
    digest, length = hashlib.sha256(), 0
    try:
        with temporary.open('wb') as output:
            for number, block in enumerate(index['blocks']):
                name = 'block-%05d.deflate' % number
                if block['file'] != name:
                    raise ValueError('Unexpected block filename')
                compressed = (parts / name).read_bytes()
                if len(compressed) != block['compressedBytes']:
                    raise ValueError('Compressed length mismatch: ' + name)
                inflater = zlib.decompressobj(wbits=-15)
                raw = inflater.decompress(compressed, BLOCK_SIZE + 1)
                if (not inflater.eof or inflater.unused_data or inflater.unconsumed_tail
                        or len(raw) != block['uncompressedBytes']
                        or len(raw) > BLOCK_SIZE
                        or hashlib.sha256(raw).hexdigest() != block['sha256']):
                    raise ValueError('Invalid compressed block: ' + name)
                output.write(raw)
                digest.update(raw)
                length += len(raw)
        if length != index['uncompressedLength'] or digest.hexdigest() != index['sha256']:
            raise ValueError('Restored binary stream identity mismatch')
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)
    return {'file': destination.name, 'bytes': length, 'sha256': digest.hexdigest()}


def import_references(args):
    big, destination = args.big.resolve(), args.output.resolve()
    table = read_big_table(big)
    entries = {ext: table['data/worldbuilder' + ext] for ext in EXTENSIONS}
    manifest = read_entry(big, entries['.manifest'])
    if manifest[:4] != b'\0\1\6\0':
        raise ValueError('Expected a linked little-endian RA3 v6 manifest')
    asset_count, payload_bytes = struct.unpack_from('<II', manifest, 12)
    if payload_bytes + 4 != entries['.bin'][1]:
        raise ValueError('Manifest payload length does not match .bin')
    destination.mkdir(parents=True, exist_ok=True)
    existing = destination / 'Worldbuilder.manifest'
    if existing.exists() and existing.read_bytes() != manifest:
        raise ValueError('Existing Worldbuilder.manifest differs from the official input')
    if (destination / 'Worldbuilder.bin.parts').exists():
        raise ValueError('Remove the existing Worldbuilder.bin.parts before reimporting')
    with tempfile.TemporaryDirectory(prefix='worldbuilder-import-', dir=destination) as staging:
        staging = Path(staging)
        parts = staging / 'Worldbuilder.bin.parts'
        parts.mkdir()
        offset, length = entries['.bin']

        def compress_block(number):
            amount = min(BLOCK_SIZE, length - number * BLOCK_SIZE)
            raw = read_entry(big, (offset + number * BLOCK_SIZE, amount))
            encoder = zlib.compressobj(level=6, wbits=-15)
            compressed = encoder.compress(raw) + encoder.flush()
            name = 'block-%05d.deflate' % number
            (parts / name).write_bytes(compressed)
            return {'file': name, 'uncompressedBytes': len(raw),
                    'compressedBytes': len(compressed),
                    'sha256': hashlib.sha256(raw).hexdigest()}

        count = (length + BLOCK_SIZE - 1) // BLOCK_SIZE
        with ThreadPoolExecutor(max_workers=args.jobs) as pool:
            blocks = list(pool.map(compress_block, range(count)))
        raw_files = {}
        for extension in EXTENSIONS:
            raw_path = staging / ('Worldbuilder' + extension)
            if extension == '.bin':
                digest = hashlib.sha256()
                with big.open('rb') as source:
                    source.seek(offset)
                    remaining = length
                    while remaining:
                        chunk = source.read(min(BLOCK_SIZE, remaining))
                        if not chunk:
                            raise ValueError('Truncated binary payload')
                        digest.update(chunk)
                        remaining -= len(chunk)
                raw_files[extension] = {'file': raw_path.name, 'bytes': length,
                                         'sha256': digest.hexdigest()}
            else:
                raw_path.write_bytes(read_entry(big, entries[extension]))
                if extension != '.manifest' and raw_path.read_bytes()[:4] != manifest[4:8]:
                    raise ValueError('Official stream checksum mismatch: ' + extension)
                raw_files[extension] = file_identity(raw_path)
        index = {'schemaVersion': 1, 'uncompressedLength': length,
                 'blockSize': BLOCK_SIZE, 'sha256': raw_files['.bin']['sha256'],
                 'blocks': blocks}
        (parts / 'index.json').write_text(json.dumps(index, indent=2) + '\n', encoding='utf-8')
        # Verify the complete packed representation once at the input boundary.
        restored = staging / 'verified.bin'
        restored_identity = unpack(parts, restored)
        restored.unlink()
        provenance = {'schemaVersion': 1, 'sourceUrl': SOURCE_URL,
                      'archive': file_identity(big), 'assetCount': asset_count,
                      'streamChecksum': '%08x' % struct.unpack_from('<I', manifest, 4)[0],
                      'existingManifestMatchesOfficial': True,
                      'rawStreams': raw_files,
                      'packing': {'format': 'independent raw Deflate blocks',
                                  'level': 6, 'blockSize': BLOCK_SIZE, 'blockCount': count,
                                  'compressedBytes': sum(b['compressedBytes'] for b in blocks),
                                  'blockSha256Meaning': 'uncompressed block bytes',
                                  'restoredSha256': restored_identity['sha256'],
                                  'byteExactReconstructionVerified': True}}
        for key, path in (('sourceZip', args.source_zip), ('installer', args.installer)):
            if path is not None:
                provenance[key] = file_identity(path.resolve())
        (staging / 'Worldbuilder.provenance.json').write_text(
            json.dumps(provenance, indent=2) + '\n', encoding='utf-8')
        for extension in ('.manifest', '.relo', '.imp'):
            shutil.copyfile(staging / ('Worldbuilder' + extension),
                            destination / ('Worldbuilder' + extension))
        shutil.move(str(parts), str(destination / parts.name))
        shutil.copyfile(staging / 'Worldbuilder.provenance.json',
                        destination / 'Worldbuilder.provenance.json')
    return {'succeeded': True, 'assetCount': asset_count, 'blockCount': count,
            'compressedBytes': provenance['packing']['compressedBytes'],
            'sha256': raw_files['.bin']['sha256']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    pack = commands.add_parser('import', help='Import extracted official WBData_12.big')
    pack.add_argument('--big', type=Path, required=True)
    pack.add_argument('--output', type=Path, default=ROOT / 'SDK/builtmods/sagexml')
    pack.add_argument('--source-zip', type=Path)
    pack.add_argument('--installer', type=Path)
    pack.add_argument('--jobs', type=int, default=min(4, os.cpu_count() or 1))
    restore = commands.add_parser('unpack', help='Restore original .bin for SDK oracle')
    restore.add_argument('--parts', type=Path, required=True)
    restore.add_argument('--output-bin', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'import':
        if args.jobs < 1:
            parser.error('--jobs must be positive')
        result = import_references(args)
    else:
        result = unpack(args.parts, args.output_bin)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
