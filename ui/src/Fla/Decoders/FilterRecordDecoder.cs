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



internal class FilterRecordDecoder {

    private FilterRecordDecoder() {
    }

    internal static void list(MfcArchive a, FlaRecord r) {
        int count = a.checkedCount(a.readUI32(), 1);
        r.set("count", count);
        List<FlaRecord> entries = new();
        r.set("entries", Collections.unmodifiableList(entries));
        for (int i = 0; i < count; i++) {
            if (i == 0) {
                r.set("firstType", a.peekUI8());
            }
            a.readInline("filter", entries.add, FilterRecordDecoder.filter);
        }
    }

    internal static void filter(MfcArchive a, FlaRecord r) {
        int type = a.readUI8();
        r.set("type", type);
        switch (type) {
            case 0:
                r.set("kind", "dropShadow");
                shadow(a, r);
                break;
            case 1:
                r.set("kind", "blur");
                blur(a, r);
                break;
            case 2:
                r.set("kind", "glow");
                glow(a, r);
                break;
            case 3:
                r.set("kind", "bevel");
                bevel(a, r);
                break;
            case 4:
                r.set("kind", "gradientGlow");
                gradient(a, r);
                break;
            case 6:
                r.set("kind", "adjustColor");
                if (revision(a, r, 1) != 0) {
                    a.readInline(r, "state", FilterRecordDecoder.state);
                    floats(a, r, "brightness", "contrast", "saturation", "hue");
                }
                break;
            case 7:
                r.set("kind", "gradientBevel");
                if (revision(a, r, 1) != 0) {
                    a.readInline(r, "gradient", FilterRecordDecoder.gradient);
                }
                break;
            default:
                throw new UnsupportedFlaRecordException("Instance filter type " + type);
        }
    }

    private static int revision(MfcArchive a, FlaRecord r, int max) {
        return DefaultFlaDecoders.revision(a, r, "revision", max);
    }

    private static void state(MfcArchive a, FlaRecord r) {
        if (revision(a, r, 1) != 0) {
            words(a, r, "enabled");
        }
    }

    
    private static void shadow(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, 4);
        if (v == 1 || v == 2) {
            floats(a, r, "angleRadians");
            words(a, r, "colorRgba");
            floats(a, r, "blurX", "distance");
            words(a, r, "inner", "knockout");
            if (v == 2) {
                a.readInline(r, "state", FilterRecordDecoder.state);
                words(a, r, "quality");
            }
        } else if (v >= 3) {
            a.readInline(r, "state", FilterRecordDecoder.state);
            words(a, r, "colorRgba");
            floats(a, r, "distance", "blurX", "blurY", "angleRadians");
            words(a, r, "inner", "knockout", "quality", "strengthPercent");
            if (v >= 4) {
                words(a, r, "hideObject");
            }
        }
    }

    private static void blur(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, 3);
        if (v == 1) {
            words(a, r, "legacyColorRgba");
            floats(a, r, "blurX", "blurY");
        } else if (v == 2) {
            floats(a, r, "blurX", "blurY");
            a.readInline(r, "state", FilterRecordDecoder.state);
            words(a, r, "quality");
        } else if (v >= 3) {
            a.readInline(r, "settings", FilterRecordDecoder.shadow);
        }
    }

    private static void glow(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, 3);
        if (v == 1 || v == 2) {
            words(a, r, "colorRgba");
            floats(a, r, "blurX");
            words(a, r, "inner", "knockout");
            if (v == 2) {
                a.readInline(r, "state", FilterRecordDecoder.state);
                words(a, r, "quality");
            }
        } else if (v >= 3) {
            a.readInline(r, "settings", FilterRecordDecoder.shadow);
        }
    }

    
    private static void bevel(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, 3);
        if (v == 2) {
            floats(a, r, "angleRadians");
            words(a, r, "colorRgba");
            floats(a, r, "blurX", "distance");
            words(a, r, "inner", "knockout");
            a.readInline(r, "state", FilterRecordDecoder.state);
            words(a, r, "quality");
            r.set("legacyByte", a.readUI8());
            words(a, r, "highlightColorRgba", "legacyWord", "full");
        } else if (v == 3) {
            a.readInline(r, "settings", FilterRecordDecoder.shadow);
            words(a, r, "highlightColorRgba", "full");
        }
    }

    private static void gradient(MfcArchive a, FlaRecord r) {
        if (revision(a, r, 1) == 0) {
            return;
        }
        a.readInline(r, "settings", FilterRecordDecoder.shadow);
        long count = a.readUI32();
        r.set("stopCount", count);
        
        if (count > 15) {
            throw a.error("Gradient filter exceeds 15 stops");
        }
        words(a, r, "gradientValue", "full");
        int size = a.checkedCount(count, 8);
        List<FlaRecord> stops = new();
        r.set("stops", Collections.unmodifiableList(stops));
        for (int i = 0; i < size; i++) {
            a.readInline("gradientStop", stops.add, (@in, stop) => words(@in, stop, "ratio", "colorRgba"));
        }
    }

    private static void words(MfcArchive a, FlaRecord r, params string[] names) {
        foreach (string name in names) {
            r.set(name, a.readUI32());
        }
    }

    private static void floats(MfcArchive a, FlaRecord r, params string[] names) {
        foreach (string name in names) {
            r.set(name, a.readFloat());
        }
    }
}

}
