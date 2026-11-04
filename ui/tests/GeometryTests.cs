using BinaryAssetBuilder.UiCompiler.Fla;
using BinaryAssetBuilder.UiCompiler;
using BinaryAssetBuilder.UiCompiler.Compilation;
using BinaryAssetBuilder.UiCompiler.Runtime;
using System.Globalization;
using System.Text;

internal static class GeometryTests
{
    private static FlaRecord Edge(int x0, int y0, int x1, int y1, int fill0, int fill1, int stroke = 0)
    { var r = new FlaRecord("edge"); r.set("startX", x0 * 256); r.set("startY", y0 * 256); r.set("endX", x1 * 256); r.set("endY", y1 * 256); r.set("fillIndex0", fill0); r.set("fillIndex1", fill1); r.set("strokeIndex", stroke); return r; }
    private static List<FlaRecord> Box(int left, int top, int right, int bottom, bool hole)
    { int f0 = hole ? 1 : 0, f1 = hole ? 0 : 1; return new List<FlaRecord> { Edge(left, top, right, top, f0, f1), Edge(right, top, right, bottom, f0, f1), Edge(right, bottom, left, bottom, f0, f1), Edge(left, bottom, left, top, f0, f1) }; }
    private static FlaDocument Document(out FlaRecord frame)
    { var doc = FlaDocument.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "flash8-empty.fla"))); frame = doc.Page(doc.Scenes.First()).getChildren().First().getChildren().First(); frame.set("children", new List<FlaRecord>()); return doc; }
    private static List<CompiledUiAsset> Movie(FlaRecord data)
    { var doc = Document(out var frame); frame.set("shapeData", data); return new AssetEncoder(new FlaCompiler(doc).Compile()).Build("GeometryTest", "GeometryTest.fla"); }
    private static double Area(List<CompiledUiAsset> movie, Action<bool, string> check, bool hole = false)
    {
        double total = 0; foreach (byte[] bytes in movie.Where(a => a.TypeName == "AptGeometryData").Select(RuntimeFixture.Payload))
        {
            string text = Encoding.UTF8.GetString(bytes); check(!text.Split('\n').Any(l => l.StartsWith("l ", StringComparison.Ordinal)), "strokes encoded as filled geometry");
            foreach (string line in text.Split('\n').Where(l => l.StartsWith("t ", StringComparison.Ordinal)))
            { double[] p = line.Substring(2).Trim().Split(':').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray(); total += Math.Abs((p[2] - p[0]) * (p[5] - p[1]) - (p[4] - p[0]) * (p[3] - p[1])) / 2; if (hole) { double x = (p[0] + p[2] + p[4]) / 3, y = (p[1] + p[3] + p[5]) / 3; check(!(x > 25 && x < 75 && y > 25 && y < 75), "hole has no triangles"); } }
        }
        return total;
    }
    internal static void Run(Action<bool, string> check)
    {
        var fill = new FlaRecord("fill"); fill.set("packedValue", 0xff0000ffL); var data = new FlaRecord("shapeData"); var edges = Box(0, 0, 2000, 2000, false); edges.AddRange(Box(500, 500, 1500, 1500, true)); data.set("edges", edges); data.set("fills", new List<FlaRecord> { fill });
        var holeMovie = Movie(data); double holeArea = Area(holeMovie, check, true); check(Math.Abs(holeArea - 7500) < 0.001, "filled square minus hole area: " + holeArea + "; geometry=" + string.Join("|", holeMovie.Where(a => a.TypeName == "AptGeometryData").Select(b => Encoding.UTF8.GetString(RuntimeFixture.Payload(b)))));
        foreach (int cap in new[] { 0, 1, 2 })
        {
            var stroke = new FlaRecord("stroke"); stroke.set("width", 200); stroke.set("capStyle", cap); stroke.set("joinStyle", 1); stroke.set("colorRgba", 0xff00ff00L);
            data = new FlaRecord("shapeData"); data.set("edges", new List<FlaRecord> { Edge(0, 0, 2000, 0, 0, 0, 1) }); data.set("strokes", new List<FlaRecord> { stroke });
            double expected = cap == 0 ? 1000 : cap == 2 ? 1100 : 1000 + Math.PI * 25;
            check(Math.Abs(Area(Movie(data), check) - expected) < (cap == 1 ? 1 : 0.001), "Flash 8 stroke cap outline area " + cap);
        }
        var raster = new FlaRecord("raster"); raster.set("revision", 3); raster.set("width", 2); raster.set("height", 1); raster.set("formatCode", 5); raster.set("rowStride", 8); raster.set("hasAlpha", 1); raster.set("pixelData", new BinaryData(new byte[] { 255, 255, 0, 0, 128, 0, 128, 0 }));
        var payload = new FlaRecord("bitmapPayload"); payload.set("raster", raster); byte[] pixels = BitmapDecoder.Decode(payload, out int width, out int height); check(width == 2 && height == 1 && pixels.SequenceEqual(((BinaryData)raster.get("pixelData")).toByteArray()), "native FLA premultiplied ARGB raster");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var doc = Document(out var frame); var text = new FlaRecord("CPicText"); text.set("bounds", new List<int> { 0, 2000, 0, 1000 }); var run = new FlaRecord("run"); var style = new FlaRecord("style"); style.set("fontFamily", "Arial"); style.set("fontSize", 240); style.set("colorRgba", 0xff000000L); run.set("style", style); run.set("text", "AO"); text.set("runs", new List<FlaRecord> { run }); frame.set("children", new List<FlaRecord> { text });
            var movie = new AssetEncoder(new FlaCompiler(doc).Compile()).Build("FontTest", "FontTest.fla"); check(movie.Any(a => a.TypeName == "AptGeometryData" && Encoding.UTF8.GetString(RuntimeFixture.Payload(a)).Contains("t ")), "Windows static font outlines");
        }
    }
}
