using System;
using System.Collections.Generic;
using System.IO;
using BinaryAssetBuilder.UiCompiler.Fla;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal static class MorphCompiler
    {
        internal static void Apply(FlaCompiler compiler, ShapeCompiler shapeCompiler, List<FlaCompiler.Placement> output, FlaRecord frame, double fraction)
        {
            var raw = frame.Record("morphShape") ?? throw new InvalidDataException("Shape tween lacks correspondence."); double t = TweenCompiler.Ease(frame, fraction, 0);
            var palette = raw.Records("fills"); var strokes = raw.Records("strokes"); var fills = new List<FlaRecord>(); var lines = new List<FlaRecord>(); var edges = new List<FlaRecord>();
            FlaRecord Get(List<FlaRecord> list, long index) { if (index < 0) return null; if (index >= list.Count) throw new InvalidDataException("Morph style index."); return list[(int)index]; }
            int[] Point(FlaRecord r, string a, string b) { int[] p = r.Words(a, 2), q = r.Words(b, 2); return new[] { CoordinateMath.Round(TweenCompiler.Lerp(p[0], q[0], t)), CoordinateMath.Round(TweenCompiler.Lerp(p[1], q[1], t)) }; }
            foreach (var segment in raw.Records("segments"))
            {
                int fill = 0, line = 0; var a = Get(palette, segment.Number("fillIndex1", -1)); var b = Get(palette, segment.Number("fillIndex2", -1));
                if (a != null || b != null) { fills.Add(Paint(a, b, t)); fill = fills.Count; }
                a = Get(strokes, segment.Number("strokeIndex1", -1)); b = Get(strokes, segment.Number("strokeIndex2", -1));
                if (a != null || b != null) { var style = (a ?? b).Copy(); style.set("width", CoordinateMath.Round(TweenCompiler.Lerp(a?.Real("width") ?? b.Real("width"), b?.Real("width") ?? a.Real("width"), t))); style.set("colorRgba", TweenCompiler.ColorLerp(a?.Number("colorRgba") ?? 0, b?.Number("colorRgba") ?? 0, t)); lines.Add(style); line = lines.Count; }
                int[] start = Point(segment, "startPointA", "startPointB");
                foreach (var curve in segment.Records("curves")) { int[] end = Point(curve, "anchorPointA", "anchorPointB"), control = Point(curve, "controlPointA", "controlPointB"); var e = new FlaRecord("edge"); e.set("startX", (long)start[0] * 256); e.set("startY", (long)start[1] * 256); e.set("endX", (long)end[0] * 256); e.set("endY", (long)end[1] * 256); e.set("controlX", (long)control[0] * 256); e.set("controlY", (long)control[1] * 256); e.set("curve", curve.Number("lineFlags") == 0); e.set("fillIndex0", fill); e.set("strokeIndex", line); edges.Add(e); start = end; }
            }
            var data = new FlaRecord("shapeData"); data.set("fills", fills); data.set("strokes", lines); data.set("edges", edges); var shape = new FlaRecord("CPicShape"); shape.set("shapeData", data); int[] ma = raw.Words("matrixWordsA", 6), mb = raw.Words("matrixWordsB", 6); var matrix = new List<int>(); for (int i = 0; i < 6; i++) matrix.Add(CoordinateMath.Round(TweenCompiler.Lerp(ma[i], mb[i], t))); shape.set("matrixWords", matrix); int id = shapeCompiler.Shape(shape); if (id != 0) output.Add(new FlaCompiler.Placement(id, shape));
        }
        private static FlaRecord Paint(FlaRecord a, FlaRecord b, double t)
        {
            var source = a ?? b; int flags = (int)source.Number("flags"); var result = source.Copy();
            if ((flags & 0x50) == 0) { result.set("packedValue", TweenCompiler.ColorLerp(a?.Number("packedValue") ?? 0, b?.Number("packedValue") ?? 0, t)); return result; }
            if (a == null || b == null || a.Number("flags") != b.Number("flags")) throw new NotSupportedException("Morph changes paint type.");
            var ma = a.Words("matrixWords", 6); var mb = b.Words("matrixWords", 6); var matrix = new List<int>(); for (int i = 0; i < 6; i++) matrix.Add(CoordinateMath.Round(TweenCompiler.Lerp(ma[i], mb[i], t))); result.set("matrixWords", matrix);
            if ((flags & 0x10) != 0) { var sa = a.Records("stops"); var sb = b.Records("stops"); if (sa.Count != sb.Count) throw new NotSupportedException("Morph gradient stop correspondence."); var stops = new List<FlaRecord>(); for (int i = 0; i < sa.Count; i++) { var stop = sa[i].Copy(); stop.set("ratio", CoordinateMath.Round(TweenCompiler.Lerp(sa[i].Real("ratio"), sb[i].Real("ratio"), t))); stop.set("colorRgba", TweenCompiler.ColorLerp(sa[i].Number("colorRgba"), sb[i].Number("colorRgba"), t)); stops.Add(stop); } result.set("stops", stops); }
            else if (a.Number("bitmapMediaId") != b.Number("bitmapMediaId")) throw new NotSupportedException("Morph changes bitmap."); return result;
        }
    }
}
