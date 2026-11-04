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



internal class DefaultFlaDecoders {

    private DefaultFlaDecoders() {
    }

    internal static Dictionary<string, FlaRecordDecoder> create() {
        Dictionary<string, FlaRecordDecoder> result = new();
        result.put("CDocumentPage", DefaultFlaDecoders.documentPage);
        result.put("CSketchCntrItem", OleRecordDecoder.item);
        result.put("CQTAudioSettings", (a, r) => {
            long length = a.readUI32();
            r.set("byteCount", length);
            r.set("data", a.readData(length));
        });
        result.put("CColorDef", ColorRecordDecoder.color);
        result.put("CColorList", ColorRecordDecoder.list);
        result.put("CPicObj", DefaultFlaDecoders.picBase);
        result.put("CPicPage", DefaultFlaDecoders.picPage);
        result.put("CPicLayer", DefaultFlaDecoders.picLayer);
        result.put("CPicShape", DefaultFlaDecoders.picShape);
        result.put("CPicRect", DefaultFlaDecoders.picRect);
        result.put("CPicOval", DefaultFlaDecoders.picOval);
        result.put("CPicFrame", DefaultFlaDecoders.picFrame);
        result.put("CPicMorphShape", MorphRecordDecoder.shape);
        result.put("CMorphSegment", MorphRecordDecoder.segment);
        result.put("CMorphCurve", MorphRecordDecoder.curve);
        result.put("CMorphHintItem", MorphRecordDecoder.hint);
        result.put("CPicBitmap", DefaultFlaDecoders.picBitmap);
        result.put("CPicSwf", SwfRecordDecoder.instance);
        result.put("CSwfPage", SwfRecordDecoder.media);
        result.put("CPicVideoStream", DefaultFlaDecoders.picVideoStream);
        result.put("CPicASObj", AuxiliaryObjectRecordDecoder.asObject);
        result.put("CPicThreeDObject", AuxiliaryObjectRecordDecoder.asObject);
        result.put("CPicTLFText", AuxiliaryObjectRecordDecoder.tlfText);
        result.put("CPicScreen", AuxiliaryObjectRecordDecoder.screen);
        result.put("CPicShapeObj", InstanceRecordDecoder.symbol);
        result.put("CPicFont", AuxiliaryObjectRecordDecoder.font);
        result.put("CPicSoundCreator", AuxiliaryObjectRecordDecoder.soundCreator);
        result.put("CPicTempClipboardObj", AuxiliaryObjectRecordDecoder.clipboard);
        result.put("CPicVideo", AuxiliaryObjectRecordDecoder.video);
        result.put("CPicOle", AuxiliaryObjectRecordDecoder.ole);
        result.put("CMediaVideo", AuxiliaryObjectRecordDecoder.linkedVideo);
        result.put("CPicText", TextRecordDecoder.read);
        result.put("CPicSymbol", InstanceRecordDecoder.symbol);
        result.put("CPicSprite", InstanceRecordDecoder.sprite);
        result.put("CPicButton", InstanceRecordDecoder.button);
        result.put("CMediaElem", DefaultFlaDecoders.mediaBase);
        result.put("CMediaBits", DefaultFlaDecoders.mediaBits);
        result.put("CMediaSound", DefaultFlaDecoders.mediaSound);
        result.put("CMediaVideoStream", DefaultFlaDecoders.mediaBase);
        result.replaceAll((name, decoder) => (a, r) => {
            if (r.getSchema() != (name.equals("CColorDef") || name.equals("CColorList") || name.equals("CSketchCntrItem") ? 0 : 1)) {
                throw unsupported(name + " MFC schema " + r.getSchema());
            }
            decoder(a, r);
        });
        return result;
    }

    internal static int revision(MfcArchive a, FlaRecord r, string field, int max) {
        int value = a.readUI8();
        r.set(field, value);
        if (value > max) {
            throw unsupported(r.getClassName() + " " + field + " " + value);
        }
        return value;
    }

    private static UnsupportedFlaRecordException unsupported(string what) {
        return new UnsupportedFlaRecordException(what + " is not decoded yet");
    }

    internal static List<int> integers(MfcArchive a, int count) {
        List<int> result = new();
        for (int i = 0; i < count; i++) {
            result.add(a.readI32());
        }
        return Collections.unmodifiableList(result);
    }

    internal static void documentPage(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, "revision", 25);
        r.set("streamName", a.readStreamName(v >= 21, v < 21));
        r.set("name", a.readString());
        if (v >= 2) {
            r.set("libraryId", a.readUI32());
        }
        if (v >= 3) {
            r.set("symbolType", a.readUI8());
        }
        if (v >= 6) {
            r.set("legacyName", a.readString());
        }
        if (v >= 7) {
            r.set("placeholder", a.readUI32());
        }
        if (v >= 5) {
            a.readInline(r, "libraryMetadata", DefaultFlaDecoders.libraryMetadata);
        }
        if (v >= 8) {
            r.set("sourceTime", a.readUI32());
        }
        if (v == 9) {
            List<FlaRecord> lists = new();
            r.set("historicalActionLists", Collections.unmodifiableList(lists));
            for (int i = 0; i < 4; i++) {
                a.readInline("legacyActions", lists.add, ActionRecordDecoder.legacyList);
            }
        }
        if (v >= 10) {
            r.set("legacyCustomUISourceFilename", a.readString());
            r.set("legacySymbolDescription", a.readString());
            a.readInline(r, "properties", PropertyRecordDecoder.list);
        }
        if (v >= 11) {
            r.set("legacyParametersAreLocked", a.readUI32());
        }
        if (v >= 12) {
            a.readInline(r, "linkage", DefaultFlaDecoders.linkage);
        }
        if (v >= 14 && v <= 17) {
            r.set("legacyComponentName", a.readString());
        }
        if (v >= 15 && v <= 17) {
            List<FlaRecord> movies = new();
            r.set("legacyMovies", Collections.unmodifiableList(movies));
            for (int i = 0; i < 2; i++) {
                a.readInline("smartClipMovie", movies.add, DefaultFlaDecoders.smartClipMovie);
            }
        }
        if (v >= 16 && v <= 17) {
            r.set("legacyDescriptionType", a.readUI32());
        }
        if (v == 17) {
            r.set("legacyComponentEnabled", a.readUI32());
            r.set("legacyComponentTooltip", a.readString());
            r.set("legacyComponentIconId", a.readUI32());
        }
        if (v >= 18) {
            a.readInline(r, "component", DefaultFlaDecoders.component);
        }
        if (v >= 20) {
            r.set("templateName", a.readString());
            r.set("templateVersion", a.readString());
        }
        if (v >= 22) {
            r.set("lastUniqueIdentifier", a.readUI32());
            r.set("dataBindingXml", a.readString());
        }
        if (v >= 23) {
            r.set("scaleGridEnabled", a.readUI32());
            r.set("scaleGridBounds", integers(a, 4));
        }
        if (v >= 24) {
            r.set("hasValidCenterPoint", a.readUI8());
            r.set("transformCenterPointTwips", integers(a, 2));
        }
        if (v >= 25) {
            r.set("isFlexComponent", a.readUI8());
            r.set("isSpriteSubclass", a.readUI8());
        }
    }

    internal static void libraryMetadata(MfcArchive a, FlaRecord r) {
        long v = a.readUI32();
        r.set("revision", v);
        if (v > 7) {
            throw unsupported("Library-item metadata revision " + v);
        }
        if (v <= 3) {
            r.set("legacyParentId", a.readUI32());
        } else {
            a.readInline(r, "parentItem", DefaultFlaDecoders.itemId);
        }
        if (v > 1) {
            if (v <= 3) {
                r.set("legacyItemId", a.readUI32());
            } else {
                a.readInline(r, "item", DefaultFlaDecoders.itemId);
            }
        }
        if (v > 2) {
            r.set("attributes", a.readUI32());
        }
        if (v >= 5) {
            a.readInline(r, "linkage", DefaultFlaDecoders.linkage);
        }
        if (v >= 6) {
            int present = a.readUI8();
            r.set("optionalRecordPresent", present);
            if (present != 0) {
                a.readInline(r, "persistentData", DocumentTailRecordDecoder.persistentData);
            }
        }
    }

    private static void itemId(MfcArchive a, FlaRecord r) {
        long marker = a.readUI32();
        r.set("marker", marker);
        if (marker > 0) {
            r.set("firstPart", a.readUI32());
            r.set("secondPart", a.readUI32());
        }
    }

    private static void linkage(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, "revision", 7);
        r.set("flags", a.readUI32());
        r.set("identifier", a.readString());
        r.set("url", a.readString());
        if (v >= 5) {
            r.set("className", a.readString());
        }
        if (v >= 1) {
            r.set("exportFlags", a.readUI8());
        }
        if (v >= 2) {
            a.readInline(r, "auxiliary", (@in, aux) => {
                long av = @in.readUI32();
                aux.set("revision", av);
                if (av > 3) {
                    throw unsupported("Auxiliary linkage revision " + av);
                }
                aux.set("sourcePath", @in.readString());
                aux.set("sourceItemName", @in.readString());
                aux.set("sourceLastModified", @in.readUI32());
                if (av >= 1) {
                    aux.set("sourceAutoUpdate", @in.readUI32());
                }
                @in.readInline(aux, "item", DefaultFlaDecoders.itemId);
            });
        }
        if (v >= 6) {
            r.set("importType", a.readUI8());
        }
        if (v >= 7) {
            r.set("baseClass", a.readString());
        }
    }

    internal static void component(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, "revision", 8);
        r.set("componentEnabled", a.readUI32());
        r.set("tooltip", a.readString());
        r.set("customIconId", a.readUI32());
        r.set("parametersAreLocked", a.readUI32());
        r.set("descriptionType", a.readUI32());
        r.set("actionScriptClass", a.readString());
        List<FlaRecord> movies = new();
        r.set("movies", Collections.unmodifiableList(movies));
        for (int i = 0; i < 3; i++) {
            a.readInline("smartClipMovie", movies.add, DefaultFlaDecoders.smartClipMovie);
        }
        if (v >= 1) {
            r.set("symbolDescription", a.readString());
            a.readInline(r, "properties", PropertyRecordDecoder.list);
        }
        if (v >= 2) {
            r.set("classPropertiesXml", a.readString());
        }
        if (v >= 4) {
            int count = a.readI32();
            r.set("taskCount", count);
            List<FlaRecord> tasks = new();
            r.set("tasks", Collections.unmodifiableList(tasks));
            int entries = a.checkedCount(Math.Max(0, count), 2);
            for (int i = 0; i < entries; i++) {
                a.readInline("componentTask", tasks.add, (@in, task) => {
                    task.set("description", @in.readString());
                    task.set("script", @in.readString());
                });
            }
        }
        if (v >= 5) {
            long present = a.readUI32();
            r.set("mediaPresent", present);
            if (present != 0) {
                a.readInline(r, "embeddedMedia", (@in, media) => {
                    mediaBits(@in, media);
                    @in.readInline(media, "payload", (payloadIn, payload) => BitmapRecordDecoder.payload(payloadIn, payload, media));
                });
            }
        }
        if (v >= 6) {
            r.set("reserved", a.readUI32());
        }
        if (v >= 7) {
            r.set("editFrameIndex", a.readUI32());
            r.set("requiredMinimumPlayerVersionName", a.readString());
            r.set("requiredMinimumPlayerVersion", a.readUI32());
            r.set("requiredMinimumASVersion", a.readUI32());
        }
        if (v >= 8) {
            a.readInline(r, "livePreview11Movie", DefaultFlaDecoders.smartClipMovie);
        }
    }

    
    private static void smartClipMovie(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, "revision", 3);
        if (v >= 1) {
            r.set("name", a.readString());
            long length = a.readUI32();
            r.set("byteCount", length);
            r.set("data", a.readData(length));
        }
        if (v >= 2) {
            r.set("reserved", a.readUI32());
        }
    }

    internal static void picBase(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, "baseRevision", 6);
        r.set("flags", a.readUI8());
        a.readObjectList(r, "children");
        if (v != 0) {
            r.set("transformationPointX", a.readI32());
            r.set("transformationPointY", a.readI32());
        }
        if (v >= 3) {
            
            r.set("drawingObjectFlags", a.readUI8());
        }
        if (v >= 4) {
            r.set("cacheAsBitmap", a.readUI8());
        }
        if (v >= 6) {
            r.set("identifier", a.readZeroTerminatedIdentifier(36));
        }
    }

    private static void picPage(MfcArchive a, FlaRecord r) {
        picBase(a, r);
        int v = revision(a, r, "revision", 7);
        if (v != 4) {
            r.set("nextLayerId", a.readUI16());
        }
        if (v >= 5) {
            r.set("nextFolderId", a.readUI16());
        }
        if (v >= 7) {
            r.set("currentFrame", a.readUI32());
        }
        if (v >= 3) {
            int count = a.checkedCount(a.readUI32(), 8);
            List<List<int>> guides = new();
            for (int i = 0; i < count; i++) {
                guides.add(integers(a, 2));
            }
            r.set("guides", Collections.unmodifiableList(guides));
        }
    }

    private static void picLayer(MfcArchive a, FlaRecord r) {
        picBase(a, r);
        int v = revision(a, r, "revision", 13);
        r.set("name", a.readString());
        if (v <= 3) {
            r.set("legacyState", a.readUI8());
        } else {
            r.set("stateFlags", a.readData(3));
        }
        if (v >= 5) {
            r.set("heightLiteral", a.readUI32());
        }
        if (v >= 6) {
            r.set("outlineColorRgb", a.readUI32());
            r.set("outline", a.readUI32());
        }
        if (v >= 8) {
            r.set("heightMultiplier", a.readUI32());
        }
        int type = a.readUI8();
        r.set("type", type);
        a.readObjectInto(r, "relatedLayer");
        if (v == 7 || v == 8) {
            a.readObjectInto(r, "legacyRelatedLayer");
        }
        if (v >= 2 && v <= 5) {
            r.set("legacyFlag", a.readUI8());
        }
        if (v >= 3) {
            r.set(v < 9 ? "legacyMask" : "open", a.readUI8());
        }
        if (v >= 10) {
            r.set("autoNamed", a.readUI8());
        }
        if (v >= 12) {
            int animationType = v >= 13 ? a.readUI8() : type == 6 ? 2 : type == 5 ? 1 : 0;
            r.set("animationType", animationType);
            if (animationType == 2) {
                int count = a.checkedCount(a.readUI32(), 4);
                List<long> values = new();
                for (int i = 0; i < count; i++) {
                    values.add(a.readUI32());
                }
                r.set("animationValues", Collections.unmodifiableList(values));
            } else if (animationType > 2) {
                throw unsupported("Layer animation type " + animationType);
            }
        }
    }

    private static void picShape(MfcArchive a, FlaRecord r) {
        picBase(a, r);
        int v = revision(a, r, "shapeRevision", 6);
        r.set("matrixWords", integers(a, 6));
        int mode = v >= 3 ? v - 2 : 0;
        a.readInline(r, "shapeData", (@in, shape) => ShapeDataDecoder.read(@in, shape, mode));
    }

    private static void picRect(MfcArchive a, FlaRecord r) {
        picShape(a, r);
        revision(a, r, "revision", 3);
        r.set("primitiveFlags", a.readUI8());
        r.set("cornerRadii", integers(a, 4));
    }

    private static void picOval(MfcArchive a, FlaRecord r) {
        picShape(a, r);
        revision(a, r, "revision", 3);
        r.set("primitiveFlags", a.readUI8());
        r.set("bounds", integers(a, 4));
        r.set("startAngleDegrees", a.readFloat());
        r.set("endAngleDegrees", a.readFloat());
        r.set("innerRadiusPercent", a.readFloat());
    }

    private static void picFrame(MfcArchive a, FlaRecord r) {
        picShape(a, r);
        int v = revision(a, r, "revision", 29);
        r.set("duration", a.readUI16());
        r.set("keyMode", v <= 2 ? a.readUI8() : a.readUI16());
        if (v >= 2) {
            r.set("acceleration", a.readUI16());
        }
        if (v >= 5) {
            r.set("soundMediaId", a.readUI16());
        }
        if (v >= 6) {
            int count = a.checkedCount(a.readUI16(), 8);
            List<List<long>> envelope = new();
            for (int i = 0; i < count; i++) {
                List<long> point = new();
                point.add(a.readUI32());
                point.add((long) a.readUI16());
                point.add((long) a.readUI16());
                envelope.add(Collections.unmodifiableList(point));
            }
            r.set("soundEnvelope", Collections.unmodifiableList(envelope));
        }
        if (v >= 7) {
            r.set("soundLoopCount", a.readUI16());
            r.set("soundSync", a.readUI8());
            r.set("soundInPoint", a.readUI32());
            r.set("soundOutPoint", a.readUI32());
        }
        if (v >= 8) {
            r.set("soundZoom", a.readUI16());
        }
        if (v >= 9) {
            r.set("name", a.readString());
        }
        if (v >= 4 && v <= 9) {
            a.readInline(r, "actions", ActionRecordDecoder.historical);
        }
        if (v >= 10 && v <= 18) {
            FlaRecord actions = a.readInline(r, "actions", ActionRecordDecoder.legacyList);
            r.set("legacyActionCount", actions.get("count"));
        }
        if (v >= 19) {
            a.readInline(r, "actions", DefaultFlaDecoders.frameActions);
        }
        if (v >= 11) {
            r.set("rotationMode", a.readUI32());
            r.set("rotationCount", a.readUI32());
        }
        if (v >= 12) {
            r.set("labelType", a.readUI32());
        }
        if (v >= 13) {
            a.readObjectInto(r, "morphShape");
        }
        if (v >= 14) {
            r.set("shapeTweenBlend", a.readUI32());
        }
        if (v >= 15) {
            a.readObjectInto(r, "soundSettings");
        }
        if (v >= 16) {
            r.set("extensionString", a.readString());
        }
        if (v == 17) {
            a.readInline(r, "historicalExtension", (@in, extension) => {
                extension.set("flagsWord", @in.readUI16());
                extension.set("matrixWords", integers(@in, 6));
                extension.set("beforeColorFlag", @in.readUI8());
                @in.readInline(extension, "colorTransform", InstanceRecordDecoder.colorTransform);
                extension.set("afterColorFlag", @in.readUI8());
            });
        }
        if (v >= 20) {
            r.set("reserved", a.readUI32());
        }
        if (v >= 21) {
            r.set("soundEffect", a.readUI32());
        }
        if (v >= 22) {
            r.set("bookmark", a.readUI32());
        }
        if (v >= 24) {
            r.set("useSingleEaseCurve", a.readUI32());
            long present = a.readUI32();
            r.set("customEasePresent", present);
            if (present != 0) {
                List<List<List<double>>> curves = new();
                for (int i = 0; i < 6; i++) {
                    int count = a.checkedCount(a.readUI32(), 48);
                    List<List<double>> curve = new();
                    for (int j = 0; j < count; j++) {
                        List<double> values = new();
                        for (int k = 0; k < 6; k++) {
                            values.add(a.readDouble());
                        }
                        curve.add(Collections.unmodifiableList(values));
                    }
                    curves.add(Collections.unmodifiableList(curve));
                }
                r.set("customEaseCurves", Collections.unmodifiableList(curves));
            }
        }
        int kind = ((object) r.get("keyMode")).intValue() & 255;
        if (v >= 27 && kind == 3) {
            r.set("motionXml", a.readString());
            r.set("visibleAnimationKeyframes", a.readUI32());
        }
        if (v >= 28 && kind == 3) {
            r.set("tweenInstanceName", a.readString());
        }
        if (v >= 29 && kind == 4) {
            int count = a.checkedCount(a.readUI32(), 0);
            long matrixCount = (long) count * ((object) r.get("duration")).intValue();
            int total = a.checkedCount(matrixCount, 24);
            List<List<int>> matrices = new();
            for (int i = 0; i < total; i++) {
                matrices.add(integers(a, 6));
            }
            r.set("poseMatrices", Collections.unmodifiableList(matrices));
        }
    }

    internal static void frameActions(MfcArchive a, FlaRecord r) {
        ActionRecordDecoder.source(a, r);
    }

    private static void picBitmap(MfcArchive a, FlaRecord r) {
        picBase(a, r);
        int v = revision(a, r, "revision", 2);
        r.set("matrixWords", integers(a, 6));
        if (v == 0) {
            a.readInline(r, "raster", BitmapRecordDecoder.raster);
        }
        if (v >= 1) {
            r.set("mediaId", a.readUI16());
        }
        if (v >= 2) {
            int present = a.readUI8();
            r.set("optionalRecordPresent", present);
            if (present != 0) {
                a.readInline(r, "persistentData", DocumentTailRecordDecoder.persistentData);
            }
        }
    }

    private static void picVideoStream(MfcArchive a, FlaRecord r) {
        picBase(a, r);
        int v = revision(a, r, "revision", 4);
        r.set("matrixWords", integers(a, 6));
        r.set("bounds", integers(a, 4));
        r.set("reservedByte", a.readUI8());
        r.set("reservedString", a.readString());
        r.set("instanceName", a.readString());
        long present = 1;
        if (v >= 3) {
            present = a.readUI32();
            r.set("mediaPresent", present);
        }
        if (present != 0) {
            r.set("mediaId", a.readUI16());
        }
    }

    internal static void mediaBase(MfcArchive a, FlaRecord r) {
        int v = revision(a, r, "mediaBaseRevision", 7);
        r.set("streamName", a.readStreamName(v >= 6, false));
        r.set("name", a.readString());
        r.set("mediaId", a.readUI16());
        if (v >= 2) {
            r.set("sourcePath", a.readString());
        }
        if (v >= 3) {
            r.set("sourceTime", a.readUI32());
        }
        if (v >= 4) {
            a.readInline(r, "libraryMetadata", DefaultFlaDecoders.libraryMetadata);
        }
        if (v >= 5) {
            r.set("sourcePlatform", a.readUI32());
        }
    }

    internal static void mediaBits(MfcArchive a, FlaRecord r) {
        mediaBase(a, r);
        int v = revision(a, r, "revision", 4);
        if (v < 2) {
            r.set("legacySourcePath", a.readString());
        }
        if (v >= 1) {
            r.set("compressionMode", a.readUI8());
            r.set("jpegQuality", a.readUI8());
        }
        if (v >= 2) {
            r.set("allowSmoothing", a.readUI8());
        }
        if (v >= 3) {
            r.set("encodedByteCount", a.readI32());
        }
        if (v >= 4) {
            r.set("useDeblocking", a.readUI8());
        }
    }

    private static void mediaSound(MfcArchive a, FlaRecord r) {
        mediaBase(a, r);
        int v = revision(a, r, "revision", 10);
        if (v == 2) {
            r.set("legacySourcePath", a.readString());
        }
        r.set("formatFlags", a.readUI16());
        r.set("sampleCount", a.readUI32());
        if (v >= 4) {
            r.set("exportFormat", a.readI16());
            r.set("exportBits", a.readI16());
        }
        if (v >= 6) {
            r.set("encodedByteCount", a.readUI32());
            r.set("importedMP3QualityFlag", a.readUI8());
        }
        if (v >= 7) {
            r.set("secondaryFormatFlags", a.readUI16());
            r.set("secondarySampleCount", a.readUI32());
        }
        if (v >= 9) {
            r.set("deviceSoundHRef", a.readString());
        }
        if (v >= 10) {
            r.set("use8kSampleRateFlag", a.readUI32());
        }
    }
}

}
