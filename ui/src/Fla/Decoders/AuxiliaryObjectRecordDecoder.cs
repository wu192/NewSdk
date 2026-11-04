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



internal class AuxiliaryObjectRecordDecoder {

    private AuxiliaryObjectRecordDecoder() {
    }

    internal static void asObject(MfcArchive a, FlaRecord r) {
        DefaultFlaDecoders.picBase(a, r);
        int revision = DefaultFlaDecoders.revision(a, r, "asObjectRevision", 5);
        r.set("matrixWords", DefaultFlaDecoders.integers(a, 6));
        r.set("bounds", DefaultFlaDecoders.integers(a, 4));
        r.set("className", a.readString());
        r.set("source", a.readString());
        if (revision >= 2) {
            InstanceRecordDecoder.filters(a, r);
            r.set("selectedEffectIndex", a.readUI16());
            List<float> matrix = new();
            r.set("matrix3D", Collections.unmodifiableList(matrix));
            for (int i = 0; i < 16; i++) {
                matrix.add(a.readFloat());
            }
            List<double> rotation = new();
            r.set("rotation3D", Collections.unmodifiableList(rotation));
            for (int i = 0; i < 3; i++) {
                rotation.add(a.readDouble());
            }
            r.set("centerPoint3D", DefaultFlaDecoders.integers(a, 3));
        }
        if (revision >= 3) {
            r.set("blendMode", a.readUI8());
        }
        if (revision >= 4) {
            a.readInline(r, "colorTransform", InstanceRecordDecoder.colorTransform);
        }
        if (revision >= 5) {
            a.readInline(r, "instanceRelations", (@in, relations) => {
                int count = @in.readI32();
                relations.set("count", count);
                int size = @in.checkedCount(Math.Max(0, count), 5);
                List<FlaRecord> entries = new();
                relations.set("entries", Collections.unmodifiableList(entries));
                for (int i = 0; i < size; i++) {
                    @in.readInline("relation", entries.add, (entryIn, entry) => {
                        entry.set("value", entryIn.readUI32());
                        entry.set("identifier", entryIn.readZeroTerminatedIdentifier(39));
                    });
                }
            });
        }
    }

    internal static void tlfText(MfcArchive a, FlaRecord r) {
        asObject(a, r);
        r.set("tlfRevision", a.readUI8());
        string localization = a.readString(), variable = a.readString();
        r.set("localizationStringId", localization);
        r.set("variableName", variable);
        
        r.set("xml1", localization);
        r.set("xml2", variable);
        List<FlaRecord> links = new();
        r.set("childLinks", Collections.unmodifiableList(links));
        foreach (FlaRecord child in r.getChildren()) {
            object identifier = child.get("identifier");
            if (identifier is string && !((string) identifier).isEmpty()) {
                a.readInline("childLink", links.add, (@in, link) => {
                    link.set("child", child);
                    link.set("markupChildId", @in.readUI32());
                });
            }
        }
    }

    internal static void screen(MfcArchive a, FlaRecord r) {
        InstanceRecordDecoder.sprite(a, r);
        int revision = DefaultFlaDecoders.revision(a, r, "screenRevision", 5);
        r.set("hidden", a.readUI32());
        r.set("reservedScreenValue", a.readUI32());
        if (revision >= 3) {
            r.set("autoRegUpdate", a.readUI32()); 
        }
        r.set("screenName", a.readString());
    }

    internal static void font(MfcArchive a, FlaRecord r) {
        DefaultFlaDecoders.picBase(a, r);
        int mode = a.readUI8();
        r.set("fontReferenceMode", mode);
        if (mode != 0) {
            r.set("fontId", a.readUI16());
        } else {
            a.readInline(r, "font", DocumentTailRecordDecoder.font);
        }
    }

    internal static void soundCreator(MfcArchive a, FlaRecord r) {
        r.set("revision", a.readUI8());
        r.set("mediaId", a.readUI16());
    }

    internal static void clipboard(MfcArchive a, FlaRecord r) {
        DefaultFlaDecoders.picBase(a, r);
        r.set("revision", a.readUI8());
        r.set("matrixWords", DefaultFlaDecoders.integers(a, 6));
    }

    internal static void video(MfcArchive a, FlaRecord r) {
        clipboard(a, r);
        r.set("mediaId", a.readUI16());
    }

    internal static void linkedVideo(MfcArchive a, FlaRecord r) {
        DefaultFlaDecoders.mediaBase(a, r);
        r.set("revision", a.readUI8());
    }

    internal static void ole(MfcArchive a, FlaRecord r) {
        clipboard(a, r);
        r.set("bounds", DefaultFlaDecoders.integers(a, 4));
        int id = a.readUI16();
        r.set("oleItemId", id);
        if (id == 0) {
            a.readObjectInto(r, "oleItem");
        }
    }
}

}
