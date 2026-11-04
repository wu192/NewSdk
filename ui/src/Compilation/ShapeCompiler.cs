// FLA vector topology -> neutral material/triangle objects. No SWF shape records.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BinaryAssetBuilder.UiCompiler.Fla;
using LibTessDotNet;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal sealed class ShapeCompiler
    {
        private readonly FlaCompiler compiler;
        private struct Edge { internal Point2 A, B; internal int Left, Right; }
        internal ShapeCompiler(FlaCompiler compiler) { this.compiler = compiler; }
        internal int Shape(FlaRecord owner)
        {
            var data = owner.Record("shapeData"); if (data == null || data.Records("edges").Count == 0) return 0;
            data = StrokeExpander.Expand(data); var edges = data.Records("edges"); var fills = data.Records("fills");
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var edge in edges) foreach (string p in Equals(edge.get("curve"), true) ? new[] { "start", "control", "end" } : new[] { "start", "end" })
            { var v = Point(edge, p); left = Math.Min(left, v.X); right = Math.Max(right, v.X); top = Math.Min(top, v.Y); bottom = Math.Max(bottom, v.Y); }
            int id = compiler.NextId(); var bounds = new[] { left, right, top, bottom }; var materials = fills.Select(f => Material(f, bounds)).ToArray(); var flat = new List<Edge>();
            foreach (var edge in edges)
            {
                int f0 = (int)edge.Number("fillIndex0") & 0x7fff, f1 = (int)edge.Number("fillIndex1") & 0x7fff;
                if (f0 > fills.Count || f1 > fills.Count) throw new InvalidDataException("Missing fill style.");
                var a = Point(edge, "start"); var b = Point(edge, "end");
                if (Equals(edge.get("curve"), true)) Curve(flat, b, Point(edge, "control"), a, f0, f1, 0);
                else flat.Add(new Edge { A = a, B = b, Left = f0, Right = f1 });
            }
            var symbol = new MeshSymbol { Id = id, Bounds = bounds };
            var order = new List<int>(); foreach (var edge in flat) { if (edge.Left > 0 && !order.Contains(edge.Left)) order.Add(edge.Left); if (edge.Right > 0 && !order.Contains(edge.Right)) order.Add(edge.Right); }
            foreach (int index in order)
            {
                var segments = new List<Tuple<Point2, Point2>>();
                foreach (var edge in flat) { bool l = edge.Left == index, r = edge.Right == index; if (l == r || Equal(edge.A, edge.B)) continue; segments.Add(l ? Tuple.Create(edge.B, edge.A) : Tuple.Create(edge.A, edge.B)); }
                var tess = new Tess();
                while (segments.Count > 0)
                {
                    var first = segments[0]; segments.RemoveAt(0); Point2 origin = first.Item1, next = first.Item2;
                    var contour = new List<ContourVertex> { Vertex(origin) };
                    while (!Equal(next, origin)) { contour.Add(Vertex(next)); int n = segments.FindIndex(s => Equal(s.Item1, next)); if (n < 0) break; next = segments[n].Item2; segments.RemoveAt(n); }
                    if (contour.Count >= 3) tess.AddContour(contour.ToArray());
                }
                tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3); var batch = new MeshBatch { Material = materials[index - 1] };
                for (int i = 0; i < tess.ElementCount; i++)
                { int a = tess.Elements[i * 3], b = tess.Elements[i * 3 + 1], c = tess.Elements[i * 3 + 2]; if (a < 0 || b < 0 || c < 0) continue; Point2 P(int n) => new Point2(tess.Vertices[n].Position.X, tess.Vertices[n].Position.Y); batch.Triangles.Add(new Triangle(P(a), P(b), P(c))); }
                if (batch.Triangles.Count > 0) symbol.Batches.Add(batch);
            }
            compiler.Define(symbol); return symbol.Id;
        }
        private Material Material(FlaRecord record, double[] bounds)
        {
            int flags = (int)record.Number("flags"); int image = 0; bool repeat = false; double[] matrix = null;
            if ((flags & 0x40) != 0) { image = compiler.Bitmap(record.Number("bitmapMediaId")).Id; repeat = (flags & 1) == 0; matrix = record.Matrix(); }
            else if ((flags & 0x10) != 0) image = GradientRasterizer.Compile(compiler, record, bounds, out matrix);
            else if ((flags & 0x20) != 0) throw new NotSupportedException("Extended fill style.");
            if (image == 0) return new Material { Color = unchecked((uint)record.Number("packedValue")) };
            matrix = CoordinateMath.Matrix(matrix);
            return new Material { ImageId = image, Repeat = repeat, Matrix = matrix };
        }
        private static Point2 Point(FlaRecord record, string field) => new Point2(CoordinateMath.Round(record.Real(field + "X") / 256), CoordinateMath.Round(record.Real(field + "Y") / 256));
        private static bool Equal(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
        private static ContourVertex Vertex(Point2 point) => new ContourVertex { Position = new Vec3 { X = (float)point.X, Y = (float)point.Y } };
        private static Point2 Mid(Point2 a, Point2 b) => new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        private static void Curve(List<Edge> output, Point2 end, Point2 control, Point2 start, int left, int right, int depth)
        {
            double width = Math.Max(end.X, Math.Max(control.X, start.X)) - Math.Min(end.X, Math.Min(control.X, start.X)), height = Math.Max(end.Y, Math.Max(control.Y, start.Y)) - Math.Min(end.Y, Math.Min(control.Y, start.Y));
            if (width > 30 && height > 30 && depth < 32)
            { var a = Mid(end, control); var b = Mid(start, control); var m = Mid(a, b); Curve(output, end, a, m, left, right, depth + 1); Curve(output, m, b, start, left, right, depth + 1); return; }
            output.Add(new Edge { A = start, B = control, Left = left, Right = right }); output.Add(new Edge { A = control, B = end, Left = left, Right = right });
        }
    }
}
