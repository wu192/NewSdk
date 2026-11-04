using BinaryAssetBuilder.UiCompiler.Fla;

// Synthetic source semantics exercise layouts absent from the small upstream
// FLA samples. This same source case is used to capture the legacy golden.
internal static class LayoutDocument
{
    private static FlaRecord Frame(params FlaRecord[] objects)
    { var r = new FlaRecord("CPicFrame"); r.set("duration", 1); r.set("children", objects.ToList()); return r; }
    private static FlaRecord Page(params FlaRecord[] frames)
    { var r = new FlaRecord("CPicPage"); var layer = new FlaRecord("CPicLayer"); layer.set("children", frames.ToList()); r.set("children", new List<FlaRecord> { layer }); return r; }
    private static FlaRecord Script(string source) { var r = new FlaRecord("actions"); r.set("source", source); return r; }
    private static FlaRecord Shape(int width, int height, bool bitmap = false)
    {
        var fill = new FlaRecord("fill"); fill.set("packedValue", 0x80402010L);
        if (bitmap) { fill.set("flags", 0x41); fill.set("bitmapMediaId", 7); fill.set("matrixWords", new List<int> { 20 * 65536, 65536, -65536, 20 * 65536, 120, -60 }); }
        var edges = new List<FlaRecord>(); int[] x = { 0, width, width, 0, 0 }, y = { 0, 0, height, height, 0 };
        for (int i = 0; i < 4; i++) { var edge = new FlaRecord("edge"); edge.set("startX", x[i] * 256); edge.set("startY", y[i] * 256); edge.set("endX", x[i + 1] * 256); edge.set("endY", y[i + 1] * 256); edge.set("fillIndex1", 1); edges.Add(edge); }
        var data = new FlaRecord("shapeData"); data.set("fills", new List<FlaRecord> { fill }); data.set("edges", edges); var r = new FlaRecord("CPicShape"); r.set("shapeData", data); return r;
    }
    internal static FlaDocument Create()
    {
        var doc = FlaDocument.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "flash8-empty.fla")));
        var button = new FlaRecord("CPicButton"); button.set("libraryId", 1); button.set("instanceName", "Launch"); button.set("actions", Script("on(press){function f(a){return a+1;} trace(f(2));} on(keyPress \"a\"){trace(7);}"));
        var clip = new FlaRecord("CPicSprite"); clip.set("libraryId", 2); clip.set("instanceName", "Panel"); clip.set("matrixWords", new List<int> { 65536, 0, 0, 65536, 320, 640 }); clip.set("actions", Script("onClipEvent(load){var n=0; while(n<2){n++;} trace(n);}"));
        var changed = clip.Copy(); var color = new FlaRecord("colorTransform"); color.set("effectType", 3); color.set("alphaMultiplier", 128); color.set("redOffset", 10); changed.set("colorTransform", color);
        var first = Frame(button, clip); first.set("label", "菜单"); first.set("actions", Script("var n=1.25; if(n>0){trace(n);} stop();"));
        var second = Frame(changed); second.set("label", "next");
        doc.Pages[doc.Scenes.First().Text("streamName")] = Page(first, second);
        var visible = Shape(600, 400); doc.Pages["Button"] = Page(Frame(visible), Frame(visible), Frame(visible), Frame(Shape(1000, 600)));
        var text = new FlaRecord("CPicText"); text.set("textFlags", 1 | 2 | 16); text.set("bounds", new List<int> { 0, 2000, 0, 600 }); text.set("variableName", "status");
        var run = new FlaRecord("run"); var style = new FlaRecord("style"); style.set("fontFamily", "_sans"); style.set("fontSize", 240); style.set("alignment", 2); style.set("colorRgba", 0xff123456L); run.set("style", style); run.set("text", "Ready 中文"); text.set("runs", new List<FlaRecord> { run });
        doc.Pages["Panel"] = Page(Frame(text, Shape(1200, 800, true)));
        // Old editor files can retain clip handlers on a button state frame;
        // Flash publishes only its visual state records, with no frame actions.
        doc.Pages["Button"].getChildren()[0].getChildren()[0].set("actions", Script("on(construct){_type=\"RenderImage\";} onClipEvent(load){trace(1);}"));
        var b = new FlaRecord("CLibButton"); b.set("libraryId", 1); b.set("streamName", "Button"); b.set("symbolType", 1);
        var movie = new FlaRecord("CLibSymbol"); movie.set("libraryId", 2); movie.set("streamName", "Panel"); var link = new FlaRecord("linkage"); link.set("flags", 1); link.set("identifier", "PanelExport"); link.set("className", "ui.Panel"); movie.set("linkage", link); doc.Contents.set("library", new List<FlaRecord> { b, movie });
        var media = new FlaRecord("CMediaBits"); media.set("mediaId", 7); media.set("streamName", "Pixels"); doc.Contents.set("media", new List<FlaRecord> { media });
        var payload = new FlaRecord("bitmapPayload"); var raster = new FlaRecord("raster"); raster.set("revision", 3); raster.set("width", 2); raster.set("height", 2); raster.set("formatCode", 5); raster.set("rowStride", 8); raster.set("hasAlpha", 1); raster.set("pixelData", new BinaryData(new byte[] { 255, 255, 0, 0, 128, 0, 128, 0, 255, 0, 0, 255, 0, 0, 0, 0 })); payload.set("raster", raster); doc.Payloads["Pixels"] = payload;
        return doc;
    }
    internal static string ClassSource => "class ui.Panel extends MovieClip {var n:Number=1; function Panel(){super();} function next(x:Number):Number{return n+x;}}";
}
