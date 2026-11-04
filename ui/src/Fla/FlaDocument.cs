// Binary Flash MX 2004 / Flash 8 container decoding. Protocol derived from JPEXS FLA Convert 3.0.0.
// LGPL-2.1-or-later; see licenses/JPEXS-LGPL-2.1.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BinaryAssetBuilder.UiCompiler.Fla
{
    internal sealed class FlaDocument
    {
        internal FlaRecord Contents;
        internal readonly Dictionary<string, FlaRecord> Pages = new Dictionary<string, FlaRecord>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, FlaRecord> Payloads = new Dictionary<string, FlaRecord>(StringComparer.OrdinalIgnoreCase);
        internal IEnumerable<FlaRecord> Scenes => Contents.Records("scenes");
        internal IEnumerable<FlaRecord> Library => Contents.Records("library");
        internal IEnumerable<FlaRecord> Media => Contents.Records("media");
        internal FlaRecord Page(FlaRecord descriptor) => Pages[descriptor.Text("streamName")];
        internal FlaRecord Payload(FlaRecord descriptor) => Payloads[descriptor.Text("streamName")];
        internal static FlaDocument Read(byte[] bytes)
        {
#if NET8_0_OR_GREATER
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
#endif
            var streams = new CompoundFile(bytes).Streams;
            if (!streams.TryGetValue("Contents", out byte[] contents) || contents.Length == 0 || (contents[0] != 0x38 && contents[0] != 0x3f))
                throw new InvalidDataException("Only binary Macromedia Flash MX 2004/8 FLA (Contents revisions 0x38/0x3F) is supported.");
            var doc = new FlaDocument();
            MfcArchive Archive(string name) { if (!streams.TryGetValue(name, out byte[] data)) throw new InvalidDataException("Missing FLA stream: " + name); return new MfcArchive(new BinaryData(data), name, DefaultFlaDecoders.create()); }
            var a = Archive("Contents");
            var root = new FlaRecord("Contents", 0, 0, 0, new BinaryData(contents));
            doc.Contents = root;
            root.set("revision", a.readUI8()); root.set("compatibilityMarker", a.readUI32());
            root.set("oleBaseHeader", a.readUI32()); a.readObjectList(root, "oleItems", a.checkedCount(root.Number("oleBaseHeader"), 2));
            root.set("reserved1", a.readUI8()); root.set("reserved2", a.readUI8());
            foreach (int threshold in new[] { 0x21, 0x2b, 0x3b }.Where(t => t <= contents[0])) root.set("reservedAt" + threshold, a.readData(4));
            a.readObjectList(root, "scenes"); root.set("nextSceneId", a.readUI16()); root.set("nextItemTag", a.readUI16()); a.readObjectInto(root, "selectedScene");
            a.readObjectList(root, "library"); root.set("libraryCounter", a.readUI16());
            a.readObjectList(root, "media"); root.set("mediaCounter", a.readUI16()); root.set("rulerUnitType", a.readUI16());
            root.set("gridDisplayMode", a.readUI8()); root.set("boundsWords", new List<int> { a.readI32(), a.readI32(), a.readI32(), a.readI32() });
            root.set("gridSpacingXFixed16Twips", a.readUI32()); root.set("previewAndRulerFlags", a.readData(3)); root.set("viewPlayAndPrinterFlags", a.readData(2));
            foreach (string field in new[] { "printMarginLeftTwips", "printMarginRightTwips", "printMarginTopTwips", "printMarginBottomTwips" }) root.set(field, a.readI32());
            foreach (string field in new[] { "printCenterHorizontal", "printCenterVertical", "printLayout", "printDisablePostscript" }) root.set(field, a.readUI8());
            root.set("printScaleFixed16", a.readI32()); root.set("printerModeData", a.readData(a.readUI16())); root.set("printerNamesData", a.readData(a.readUI16()));
            root.set("backgroundColorRgba", a.readUI32()); root.set("gridColorRgba", a.readUI32()); root.set("frameRateWord", a.readUI32());
            root.set("printAllFrames", a.readUI8()); root.set("printLabelPages", a.readUI8()); root.set("printFrameCountAcrossPage", a.readUI8()); root.set("printStoryboardMarginTwips", a.readI32());
            ContentsRecordDecoder.extensions(a, root, contents[0]);
            if (a.getRemaining() != 0 && root.get("writerIdentity") == null) a.readInline(root, "compatibilityTail", (input, tail) => ContentsRecordDecoder.compatibilityTail(input, tail, root.get("sharedLibraries") == null));
            Finish(a, root);
            foreach (FlaRecord descriptor in doc.Scenes.Concat(doc.Library))
            {
                string name = descriptor.Text("streamName"); if (name.Length == 0) continue; if (doc.Pages.ContainsKey(name)) continue;
                a = Archive(name); int kind = a.readUI8(); var page = a.readObject();
                if (kind > 1 || page == null || page.ClassName != (kind == 0 ? "CPicShape" : "CPicPage")) throw a.error("Invalid page root.");
                Finish(a, page); doc.Pages.Add(name, page);
            }
            foreach (FlaRecord descriptor in doc.Media)
            {
                string name = descriptor.Text("streamName"); if (name.Length == 0) continue; if (doc.Payloads.ContainsKey(name)) continue;
                a = Archive(name); var payload = new FlaRecord("mediaPayload", 0, 0, 0, new BinaryData(streams[name]));
                switch (descriptor.ClassName)
                {
                    case "CMediaBits":
                        int count = (int)descriptor.Number("encodedByteCount"); byte[] image = streams[name];
                        if (count == 0 && image.Length > 16 && image[0] == 255 && image[1] == 216 && image[2] == 255 && image[3] == 224) count = image.Length - 16;
                        BitmapRecordDecoder.payload(a, payload, count); break;
                    case "CMediaSound": MediaPayloadRecordDecoder.sound(a, payload, descriptor); break;
                    case "CSwfPage": payload.set("opaque", a.readData(a.getRemaining())); break;
                    case "CMediaVideoStream": MediaPayloadRecordDecoder.video(a, payload); break;
                    default: throw a.error("Unsupported library media: " + descriptor.ClassName);
                }
                Finish(a, payload); doc.Payloads.Add(name, payload);
            }
            return doc;
        }
        private static void Finish(MfcArchive a, FlaRecord record) { if (a.getRemaining() != 0) throw a.error("Unconsumed stream data."); record.finish(a.getPosition(), true); }
    }
}
