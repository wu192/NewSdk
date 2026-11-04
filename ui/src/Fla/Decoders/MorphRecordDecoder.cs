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



internal class MorphRecordDecoder {

    private MorphRecordDecoder() {
    }

    internal static void shape(MfcArchive a, FlaRecord r) {
        int revision = DefaultFlaDecoders.revision(a, r, "revision", 2);
        r.set("matrixWordsA", DefaultFlaDecoders.integers(a, 6));
        r.set("matrixWordsB", DefaultFlaDecoders.integers(a, 6));
        if (revision >= 2) {
            r.set("extensionValue1", a.readUI32());
            r.set("acceleration", a.readUI32());
        }
        objects(a, r, "segments");
        objects(a, r, "hints");
        int fills = a.checkedCount(a.readUI16(), 0);
        r.set("fillCount", fills);
        List<FlaRecord> fillRecords = new();
        r.set("fills", Collections.unmodifiableList(fillRecords));
        for (int i = 0; i < fills; i++) {
            a.readInline("morphFill", fillRecords.add, (@in, fill) => ShapeDataDecoder.fill(@in, fill, 0));
        }
        int strokes = a.checkedCount(a.readUI16(), 0);
        r.set("strokeCount", strokes);
        List<FlaRecord> strokeRecords = new();
        r.set("strokes", Collections.unmodifiableList(strokeRecords));
        for (int i = 0; i < strokes; i++) {
            a.readInline("morphStroke", strokeRecords.add, (@in, stroke) => ShapeDataDecoder.stroke(@in, stroke, 0));
        }
    }

    internal static void segment(MfcArchive a, FlaRecord r) {
        r.set("strokeIndex1", a.readI32());
        r.set("strokeIndex2", a.readI32());
        r.set("fillIndex1", a.readI32());
        r.set("fillIndex2", a.readI32());
        r.set("startPointA", DefaultFlaDecoders.integers(a, 2));
        r.set("startPointB", DefaultFlaDecoders.integers(a, 2));
        objects(a, r, "curves");
    }

    internal static void curve(MfcArchive a, FlaRecord r) {
        r.set("controlPointA", DefaultFlaDecoders.integers(a, 2));
        r.set("anchorPointA", DefaultFlaDecoders.integers(a, 2));
        r.set("controlPointB", DefaultFlaDecoders.integers(a, 2));
        r.set("anchorPointB", DefaultFlaDecoders.integers(a, 2));
        r.set("lineFlags", a.readUI32());
    }

    internal static void hint(MfcArchive a, FlaRecord r) {
        r.set("pointA", DefaultFlaDecoders.integers(a, 2));
        r.set("pointB", DefaultFlaDecoders.integers(a, 2));
        r.set("index", a.readUI32());
    }

    private static void objects(MfcArchive a, FlaRecord r, string field) {
        int count = a.readCount();
        r.set(field + "Count", count);
        a.readObjectList(r, field, count);
    }
}

}
