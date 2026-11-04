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



internal class TextRecordDecoder {

    private TextRecordDecoder() {
    }

    internal static void read(MfcArchive a, FlaRecord r) {
        DefaultFlaDecoders.picBase(a, r);
        int v = DefaultFlaDecoders.revision(a, r, "revision", 14);
        r.set("matrixWords", DefaultFlaDecoders.integers(a, 6));
        r.set("bounds", DefaultFlaDecoders.integers(a, 4));
        r.set("autoExpand", a.readUI8());
        if (v > 2) {
            
            r.set("reservedTextByte", a.readUI8());
        }
        long flags = 0;
        if (v >= 4) {
            flags = v == 4 ? a.readUI16() : a.readUI32();
            r.set("textFlags", flags);
            r.set("maxCharacters", a.readUI16());
            r.set("variableName", a.readString());
            if ((flags & 0x2000) != 0) {
                r.set("fontLibraryName", a.readString());
            }
        }
        if (v >= 14) {
            r.set("embedVariantGlyphs", a.readUI8());
        }
        if ((flags & 0x4000) != 0) {
            a.readInline(r, "defaultStyle", TextRecordDecoder.style);
        }
        List<FlaRecord> runs = new();
        r.set("runs", Collections.unmodifiableList(runs));
        StringBuilder text = new StringBuilder();
        while (a.peekUI16() != 0) {
            a.checkedCount((long) runs.size() + 1, 0);
            a.readInline("textRun", runs.add, (@in, run) => {
                int count = @in.readUI16();
                run.set("characterCount", count);
                if (count > 0x202) {
                    throw @in.error("Text run exceeds the native 514-unit limit");
                }
                FlaRecord style = @in.readInline(run, "style", TextRecordDecoder.style);
                bool unicode = ((object) style.get("revision")).intValue() >= 10;
                run.set("unicode", unicode);
                BinaryData characters = @in.readData(count * (unicode ? 2 : 1));
                run.set("charactersData", characters);
                string value = decodeCharacters(@in, characters, unicode);
                run.set("text", value);
                text.append(value);
            });
        }
        r.set("runTerminator", a.readUI16());
        r.set("text", text.toString());
        if (v >= 6) {
            r.set("instanceName", a.readString());
        }
        if (v >= 9) {
            a.readInline(r, "accessibility", (@in, access) => InstanceRecordDecoder.accessibility(@in, access, v >= 10));
        }
        if (v >= 8) {
            r.set("scrollable", a.readUI32());
        }
        if (v >= 11) {
            r.set("localizationStringId", a.readString());
        }
        if (v >= 12) {
            r.set("embedRanges", a.readString());
        }
        if (v >= 13) {
            InstanceRecordDecoder.filters(a, r);
            
            r.set("selectedEffectIndex", a.readUI16());
        }
    }

    internal static void style(MfcArchive a, FlaRecord r) {
        int v = DefaultFlaDecoders.revision(a, r, "revision", 15);
        if (v < 2) {
            return;
        }
        r.set("fontSize", a.readUI16());
        r.set("fontFamily", styleString(a, v));
        if (v >= 14) {
            r.set("fontFace", styleString(a, v));
            r.set("fontFlags", a.readUI32());
        }
        r.set("colorRgba", a.readUI32());
        r.set("pitchAndFamily", a.readUI8());
        r.set("charSet", a.readUI8());
        r.set("bold", a.readUI8());
        if (v == 2) {
            int flags = a.readUI8();
            r.set("legacyFlags", flags);
            r.set("italic", flags & 1);
            r.set("useScreenSpacing", (flags >> 1) & 1);
            r.set("autoKern", (flags & 4) == 0 ? 1 : 0);
            r.set("characterPosition", (flags >> 3) & 3);
        } else {
            r.set("italic", a.readUI8());
            r.set("useScreenSpacing", a.readUI8());
            r.set("autoKern", a.readUI8());
            r.set("characterPosition", a.readUI8());
        }
        r.set("alignment", a.readUI8());
        r.set("lineSpacing", a.readI16());
        r.set("indent", a.readI16());
        r.set("leftMargin", a.readI16());
        r.set("rightMargin", a.readI16());
        r.set("letterSpacing", a.readI16());
        if (v >= 5) {
            r.set("url", styleString(a, v));
        }
        if (v >= 6) {
            r.set("vertical", a.readUI8());
            r.set("rightToLeft", a.readUI8());
            r.set("rotation", a.readUI8());
        }
        if (v >= 8) {
            r.set("bitmapRendering", a.readUI8());
        }
        if (v >= 7) {
            r.set("target", styleString(a, v));
        }
        if (v >= 11) {
            a.readInline(r, "antiAlias", TextRecordDecoder.antiAlias);
        }
        if (v >= 12) {
            r.set("urlOverride", styleString(a, v));
        }
        if (v == 13) {
            r.set("legacyFontName", styleString(a, v));
            r.set("legacyFontValue", a.readUI32());
        }
    }

    private static string styleString(MfcArchive a, int revision) {
        if (revision >= 15) {
            return a.readString();
        }
        int count = a.readUI8();
        bool unicode = revision >= 10;
        string value = decodeCharacters(a, a.readData(count * (unicode ? 2 : 1)), unicode);
        int zero = value.indexOf('\0');
        if (!unicode && zero == value.length() - 1 && zero >= 0) {
            return value.substring(0, zero);
        }
        if (zero >= 0) {
            throw a.error("Embedded NUL in compact text-style string");
        }
        return value;
    }

    private static string decodeCharacters(MfcArchive a, BinaryData data, bool unicode) {
        byte[] bytes = data.toByteArray();
        if (!unicode) {
            return PortSupport.Decode(bytes, a.getAnsiCharset());
        }
        
        char[] characters = new char[bytes.Length / 2];
        for (int i = 0; i < characters.Length; i++) {
            characters[i] = (char) ((bytes[2 * i] & 255) | (bytes[2 * i + 1] & 255) << 8);
        }
        return new string(characters);
    }

    private static void antiAlias(MfcArchive a, FlaRecord r) {
        int revision = DefaultFlaDecoders.revision(a, r, "revision", 2);
        r.set("mode", a.readUI8());
        if (revision == 1) {
            r.set("legacyWords", DefaultFlaDecoders.integers(a, 3));
        }
        r.set("thickness", a.readFloat());
        r.set("sharpness", a.readFloat());
    }
}

}
