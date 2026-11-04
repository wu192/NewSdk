using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BinaryAssetBuilder.UiCompiler.Fla;
using Clipper2Lib;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal static class StrokeExpander
    {
        // APT's RU line primitive has no Flash 8 cap/join/material fields.
        // Expand strokes to filled outlines before encoding the SWF model.
        internal static FlaRecord Expand(FlaRecord source)
        {
            var strokes = source.Records("strokes"); if (strokes.Count == 0) return source;
            var fills = source.Records("fills").ToList(); var edges = new List<FlaRecord>();
            foreach (var edge in source.Records("edges")) { long index = edge.Number("strokeIndex") & 0x7fff; if (index > strokes.Count) throw new InvalidDataException("Missing stroke style."); var copy = edge.Copy(); copy.set("strokeIndex", 0); edges.Add(copy); }
            for (int i = 0; i < strokes.Count; i++)
            {
                var segments = source.Records("edges").Where(e => (e.Number("strokeIndex") & 0x7fff) == i + 1).ToList(); if (segments.Count == 0) continue; var style = strokes[i];
                if (style.Number("styleType") != 0) throw new NotSupportedException("Procedural dashed/dotted stroke style.");
                if (style.Number("scaleMode") != 0 || style.Number("pixelHinting") != 0) throw new NotSupportedException("Non-scaling or pixel-hinted strokes require placement-aware outlines.");
                double width = style.Real("width"); if (width <= 0) throw new NotSupportedException("Device-pixel hairline strokes.");
                int cap = (int)style.Number("capStyle", 1), join = (int)style.Number("joinStyle", 1); if (cap < 0 || cap > 2 || join < 0 || join > 2) throw new InvalidDataException("Unknown stroke cap/join.");
                FlaRecord paint = style.Record("paint")?.Copy() ?? new FlaRecord("fill"); if (style.Record("paint") == null) paint.set("packedValue", style.Number("colorRgba", 0xff000000L)); fills.Add(paint); int fill = fills.Count;
                while (segments.Count > 0)
                {
                    var first = segments[0]; segments.RemoveAt(0); var path = new PathD { Point(first, "start") }; Append(path, first); PointD start = path[0], end = path[path.Count - 1];
                    while (!Equal(start, end))
                    { int next = segments.FindIndex(e => Equal(Point(e, "start"), end)); bool reversed = false; if (next < 0) { next = segments.FindIndex(e => Equal(Point(e, "end"), end)); reversed = true; } if (next < 0) break; var segment = segments[next]; segments.RemoveAt(next); Append(path, segment, reversed); end = path[path.Count - 1]; }
                    bool closed = Equal(start, end); if (closed) path.RemoveAt(path.Count - 1); if (path.Count < 2) continue;
                    var outline = Clipper.InflatePaths(new PathsD { path }, width / 2, join == 0 ? JoinType.Miter : join == 1 ? JoinType.Round : JoinType.Bevel, closed ? EndType.Joined : cap == 0 ? EndType.Butt : cap == 1 ? EndType.Round : EndType.Square, Math.Max(1, style.Real("miterLimitWord", 768) / 256), 2, 0.25);
                    foreach (var polygon in outline) for (int p = 0; p < polygon.Count; p++)
                    { PointD a = polygon[p], b = polygon[(p + 1) % polygon.Count]; var edge = new FlaRecord("edge"); edge.set("startX", CoordinateMath.Round(a.x * 256)); edge.set("startY", CoordinateMath.Round(a.y * 256)); edge.set("endX", CoordinateMath.Round(b.x * 256)); edge.set("endY", CoordinateMath.Round(b.y * 256)); edge.set("fillIndex1", fill); edges.Add(edge); if (edges.Count > 1000000) throw new InvalidDataException("Stroke outline edge limit."); }
                }
            }
            var result = source.Copy(); result.set("fills", fills); result.set("edges", edges); result.set("strokes", new List<FlaRecord>()); return result;
        }
        private static PointD Point(FlaRecord edge, string field) => new PointD(CoordinateMath.Round(edge.Real(field + "X") / 256), CoordinateMath.Round(edge.Real(field + "Y") / 256));
        private static bool Equal(PointD a, PointD b) => a.x == b.x && a.y == b.y;
        private static void Append(PathD path, FlaRecord edge, bool reversed = false)
        { PointD a = Point(edge, reversed ? "end" : "start"), b = Point(edge, reversed ? "start" : "end"); if (Equals(edge.get("curve"), true)) Curve(path, a, Point(edge, "control"), b, 0); else path.Add(b); }
        private static void Curve(PathD path, PointD a, PointD c, PointD b, int depth)
        {
            double dx = b.x - a.x, dy = b.y - a.y, length = dx * dx + dy * dy, t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((c.x - a.x) * dx + (c.y - a.y) * dy) / length));
            double ex = c.x - a.x - t * dx, ey = c.y - a.y - t * dy;
            if (ex * ex + ey * ey <= 0.0625 || depth >= 20) { path.Add(b); return; }
            var ac = new PointD((a.x + c.x) / 2, (a.y + c.y) / 2); var cb = new PointD((c.x + b.x) / 2, (c.y + b.y) / 2); var middle = new PointD((ac.x + cb.x) / 2, (ac.y + cb.y) / 2); Curve(path, a, ac, middle, depth + 1); Curve(path, middle, cb, b, depth + 1);
        }
    }
}
