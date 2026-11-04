/*
 * Copyright (C) 2026 JPEXS.
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston,
 * MA 02110-1301  USA
 */
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace BinaryAssetBuilder.UiCompiler.Fla
{



internal class BitmapRecordDecoder {

    private BitmapRecordDecoder() {
    }

    internal static void payload(MfcArchive a, FlaRecord r, FlaRecord descriptor) {
        object count = descriptor.get("encodedByteCount");
        payload(a, r, PortSupport.IsNumber(count) ? ((object) count).intValue() : 0);
    }

    internal static void payload(MfcArchive a, FlaRecord r, int encodedByteCount) {
        r.set("encodedByteCount", encodedByteCount);
        if (encodedByteCount > 0) {
            r.set("encodedData", a.readData(encodedByteCount));
            r.set("boundsWords", DefaultFlaDecoders.integers(a, 4));
        } else {
            a.readInline(r, "raster", BitmapRecordDecoder.raster);
        }
    }

    internal static void raster(MfcArchive a, FlaRecord r) {
        int revision = DefaultFlaDecoders.revision(a, r, "revision", 3);
        int format = a.readUI8();
        r.set("formatCode", format);
        int stride = a.readUI16();
        r.set("rowStride", stride);
        r.set("width", a.readUI16());
        int height = a.readUI16();
        r.set("height", height);
        r.set("boundsWords", DefaultFlaDecoders.integers(a, 4));
        if (revision >= 3) {
            r.set("hasAlpha", a.readUI8());
        }
        if (format < 4) {
            int count = a.readUI16();
            r.set("paletteCount", count);
            if (count > 256) {
                throw a.error("Raster palette exceeds the native capacity");
            }
            a.checkedCount(count, revision >= 3 ? 4 : 6);
            List<FlaRecord> palette = new();
            r.set("palette", Collections.unmodifiableList(palette));
            for (int i = 0; i < count; i++) {
                a.readInline("paletteEntry", palette.add, (@in, entry) => {
                    List<int> values = new();
                    entry.set("components", Collections.unmodifiableList(values));
                    for (int component = 0; component < (revision >= 3 ? 4 : 3); component++) {
                        values.add(revision >= 3 ? @in.readUI8() : @in.readUI16());
                    }
                });
            }
        }
        long expected = (long) stride * height;
        r.set("expectedByteCount", expected);
        bool compressed = revision == 1;
        if (revision >= 2) {
            int flag = a.readUI8();
            r.set("compressed", flag);
            compressed = flag != 0;
        }
        r.set("isCompressed", compressed);
        if (!compressed) {
            r.set("pixelData", a.readData(expected));
            return;
        }
        List<FlaRecord> chunks = new();
        r.set("chunks", Collections.unmodifiableList(chunks));
        long total = 0;
        while (true) {
            int count = a.readUI16();
            if (count == 0) {
                r.set("chunkTerminator", count);
                break;
            }
            a.checkedCount((long) chunks.size() + 1, 0);
            a.readInline("zlibChunk", chunks.add, (@in, chunk) => {
                chunk.set("byteCount", count);
                chunk.set("data", @in.readData(count));
            });
            total += count;
        }
        r.set("compressedByteCount", total);
    }
}

}
