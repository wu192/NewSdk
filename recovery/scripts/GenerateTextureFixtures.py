#!/usr/bin/env python3
"""Small, deterministic texture inputs. No image package is needed."""
import argparse
import json
from pathlib import Path
import struct
import zlib


def pixels(width=16, height=16, alpha=False):
    colors = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)]
    return bytes(c for y in range(height) for x in range(width)
                 for c in (*colors[((x // 4) + (y // 4)) % 4],
                           (x * 17 if alpha else 255)))


def png(width, height, rgba):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    scan = b''.join(b'\0' + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(scan)) + chunk(b'IEND', b''))


def tga(width, height, rgba, rle=False):
    header = struct.pack('<BBBHHBHHHHBB', 0, 0, 10 if rle else 2, 0, 0, 0, 0, 0, width, height, 32, 0x28)
    bgra = b''.join(bytes((rgba[i + 2], rgba[i + 1], rgba[i], rgba[i + 3])) for i in range(0, len(rgba), 4))
    if rle:
        # The legacy D3DX TGA reader requires every packet to stay in one row.
        bgra = b''.join(bytes((min(128, width - x) - 1,)) +
                        bgra[(y * width + x) * 4:(y * width + min(x + 128, width)) * 4]
                        for y in range(height) for x in range(0, width, 128))
    return header + bgra


def check_rle_tga(data, rgba):
    width, height = struct.unpack_from('<HH', data, 12)
    at = 18
    pixels = bytearray()
    for _ in range(height):
        remaining = width
        while remaining:
            packet = data[at]
            at += 1
            count = (packet & 127) + 1
            assert count <= remaining, 'TGA RLE packet crosses a scanline'
            size = 4 if packet & 128 else count * 4
            payload = data[at:at + size]
            assert len(payload) == size, 'TGA RLE packet is truncated'
            pixels.extend(payload * count if packet & 128 else payload)
            at += size
            remaining -= count
    assert at == len(data), 'TGA RLE raster has trailing data'
    expected = b''.join(bytes((rgba[i + 2], rgba[i + 1], rgba[i], rgba[i + 3])) for i in range(0, len(rgba), 4))
    assert pixels == expected, 'TGA RLE decoded pixels differ'


def bmp(width, height, rgba, dib=False):
    stride = (width * 3 + 3) & ~3
    data = b''.join(b''.join(bytes((rgba[(y * width + x) * 4 + 2], rgba[(y * width + x) * 4 + 1],
                                    rgba[(y * width + x) * 4])) for x in range(width))
                    + bytes(stride - width * 3) for y in reversed(range(height)))
    info = struct.pack('<IiiHHIIiiII', 40, width, height, 1, 24, 0, len(data), 2835, 2835, 0, 0)
    return (b'' if dib else b'BM' + struct.pack('<IHHI', 14 + len(info) + len(data), 0, 0, 54)) + info + data


def indexed(width=16, height=16, kind='png'):
    palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)]
    rows = [bytes(((x // 4) + (y // 4)) % 4 for x in range(width)) for y in range(height)]
    if kind == 'png':
        def chunk(name, data):
            return struct.pack('>I', len(data)) + name + data + struct.pack('>I', zlib.crc32(name + data))
        return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 3, 0, 0, 0))
                + chunk(b'PLTE', bytes(c for color in palette for c in color))
                + chunk(b'IDAT', zlib.compress(b''.join(b'\0' + row for row in rows))) + chunk(b'IEND', b''))
    stride = (width + 3) & ~3
    data = b''.join(row + bytes(stride - width) for row in reversed(rows))
    info = struct.pack('<IiiHHIIiiII', 40, width, height, 1, 8, 0, len(data), 2835, 2835, len(palette), 0)
    table = bytes(c for r, g, b in palette for c in (b, g, r, 0))
    offset = 14 + len(info) + len(table)
    return (b'' if kind == 'dib' else b'BM' + struct.pack('<IHHI', offset + len(data), 0, 0, offset)) + info + table + data


def dds(width, height, rgba):
    header = bytearray(128)
    header[:4] = b'DDS '
    for offset, value in {4: 124, 8: 0x100F, 12: height, 16: width, 20: width * 4,
                          76: 32, 80: 0x41, 88: 32, 92: 0xFF0000, 96: 0xFF00,
                          100: 0xFF, 104: 0xFF000000, 108: 0x1000}.items():
        struct.pack_into('<I', header, offset, value)
    return header + b''.join(bytes((rgba[i + 2], rgba[i + 1], rgba[i], rgba[i + 3])) for i in range(0, len(rgba), 4))


def generate(destination):
    destination.mkdir(parents=True, exist_ok=True)
    opaque, alpha = pixels(), pixels(alpha=True)
    binary_alpha = bytes(value if i % 4 != 3 else (255 if (i // 4) % 4 < 2 else 0)
                         for i, value in enumerate(opaque))
    files = {'opaque.rgba': opaque, 'alpha.rgba': alpha,
             'opaque.png': png(16, 16, opaque), 'alpha.png': png(16, 16, alpha),
             'binary-alpha.png': png(16, 16, binary_alpha), 'binary-alpha.rgba': binary_alpha,
             'opaque.tga': tga(16, 16, opaque), 'rle.tga': tga(16, 16, opaque, True),
             'opaque.bmp': bmp(16, 16, opaque), 'opaque.dib': bmp(16, 16, opaque, True),
             'preserve.dds': dds(16, 16, opaque), 'preserve.DDS': dds(16, 16, opaque),
             'npot.png': png(13, 9, pixels(13, 9)), 'broken.png': b'\x89PNG\r\n\x1a\nnot an image'}
    solid = bytes((71, 131, 191, 255)) * 256
    files.update({'solid.bmp': bmp(16, 16, solid), 'solid.png': png(16, 16, solid), 'solid.rgba': solid})
    files.update({'indexed.png': indexed(), 'indexed.bmp': indexed(kind='bmp'), 'indexed.dib': indexed(kind='dib')})
    for name, data in files.items():
        (destination / name).write_bytes(data)
    (destination / 'fixtures.json').write_text(json.dumps({'width': 16, 'height': 16, 'files': list(files)}, indent=2) + '\n')
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    files = generate(args.destination)
    assert files['preserve.dds'][:4] == b'DDS '
    assert zlib.decompress(files['opaque.png'][41:-16]) == b''.join(b'\0' + pixels()[i:i + 64] for i in range(0, 1024, 64))
    check_rle_tga(files['rle.tga'], files['opaque.rgba'])
    print('Generated %d deterministic texture fixtures' % len(files))


if __name__ == '__main__':
    main()
