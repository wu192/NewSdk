using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using BinaryAssetBuilder.UiCompiler.Fla;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal sealed class TextCompiler
    {
        private readonly FlaCompiler compiler;
        internal TextCompiler(FlaCompiler compiler) { this.compiler = compiler; }
        internal int Compile(FlaRecord r)
        {
            var runs = r.Records("runs");
            // Empty dynamic fields carry their formatting in defaultStyle rather
            // than a zero-length text run in binary FLA.
            if (runs.Count == 0 && r.Record("defaultStyle") != null)
            {
                var run = new FlaRecord("textRun"); run.set("style", r.Record("defaultStyle")); run.set("text", "");
                runs = new List<FlaRecord> { run };
            }
            if (runs.Count == 0) throw new InvalidDataException("Text has no style runs or default style.");
            int[] b = r.Words("bounds", 4); int flags = (int)r.Number("textFlags");
            if ((flags & 1) == 0)
            {
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Static font outlines require Windows.");
#endif
                return Static(r, b);
            }
            if (runs.Count != 1) throw new NotSupportedException("Rich dynamic/input text requires HTML layout.");
            var style = runs[0].Record("style"); string name = style.Text("fontFamily", "_sans"); if (Encoding.UTF8.GetByteCount(name) > 255) throw new InvalidDataException("Font name too long.");
            int fontId = compiler.NextId(); compiler.Define(new FontSymbol { Id = fontId, Name = name });
            int id = compiler.NextId(); bool multiline = (flags & 16) != 0;
            compiler.Define(new TextSymbol { Id = id, FontId = fontId, Bounds = b.Select(v => (double)v).ToArray(), Alignment = (int)style.Number("alignment"), HeightTwips = (int)style.Number("fontSize", 240), Color = unchecked((uint)style.Number("colorRgba", 0xff000000L)), ReadOnly = (flags & 2) != 0, Multiline = multiline, WordWrap = multiline && (flags & 8) == 0, InitialText = runs[0].Text("text"), VariableName = r.Text("variableName") });
            return id;
        }
#if NET8_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        private int Static(FlaRecord r, int[] b)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("Embedding static text uses Windows font outlines; compile this FLA on Windows.");
            var edges = new List<FlaRecord>(); var fills = new List<FlaRecord>(); float x = b[0] / 20f, y = b[2] / 20f;
            using (var scratch = new Bitmap(1, 1)) using (var graphics = Graphics.FromImage(scratch)) using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
                foreach (var run in r.Records("runs"))
                {
                    var style = run.Record("style"); if (style.Number("vertical") != 0 || style.Number("rotation") != 0 || style.Number("characterPosition") != 0 || style.Text("url").Length != 0) throw new NotSupportedException("Vertical, rotated or linked static text.");
                    string name = style.Text("fontFamily", "_serif"); FontFamily family;
                    if (name == "_serif") family = FontFamily.GenericSerif; else if (name == "_sans") family = FontFamily.GenericSansSerif; else if (name == "_typewriter") family = FontFamily.GenericMonospace;
                    else { family = FontFamily.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); if (family == null) throw new InvalidDataException("Required font is not installed: " + name); }
                    var fontStyle = (style.Number("bold") != 0 ? FontStyle.Bold : 0) | (style.Number("italic") != 0 ? FontStyle.Italic : 0); if (!family.IsStyleAvailable(fontStyle)) throw new InvalidDataException("Required font style is unavailable: " + name);
                    float size = (float)style.Real("fontSize", 240) / 20; var fill = new FlaRecord("fill"); fill.set("packedValue", style.Number("colorRgba", 0xff000000L)); fills.Add(fill);
                    using (var font = new Font(family, size, fontStyle, GraphicsUnit.Pixel))
                    {
                        string[] lines = run.Text("text").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                        for (int line = 0; line < lines.Length; line++)
                        {
                            string text = lines[line]; float width = graphics.MeasureString(text, font, int.MaxValue, format).Width + (float)style.Real("letterSpacing") / 20 * Math.Max(0, text.Length - 1); int align = (int)style.Number("alignment");
                            if (align > 2 || align != 0 && r.Records("runs").Count > 1) throw new NotSupportedException("Justified or aligned mixed-style text.");
                            float start = align == 1 ? b[1] / 20f - width : align == 2 ? (b[0] + b[1]) / 40f - width / 2 : x;
                            for (int c = 0; c < text.Length; c++)
                            {
                                float offset = c == 0 ? 0 : graphics.MeasureString(text.Substring(0, c), font, int.MaxValue, format).Width; offset += (float)style.Real("letterSpacing") / 20 * c;
                                using (var path = new GraphicsPath())
                                    { path.AddString(text[c].ToString(), family, (int)fontStyle, size, new PointF(start + offset, y), format); if (path.PointCount == 0) continue; path.Flatten(null, 0.05f); var points = path.PathPoints; var types = path.PathTypes; PointF previous = default(PointF), first = default(PointF); bool have = false;
                                  for (int p = 0; p < points.Length; p++) { if ((types[p] & 7) == 0) { first = previous = points[p]; have = true; } else if (have) { Edge(edges, previous, points[p], fills.Count); previous = points[p]; } if ((types[p] & 128) != 0 && have) { Edge(edges, previous, first, fills.Count); have = false; } } }
                            }
                            x = start + width; if (line + 1 < lines.Length) { x = b[0] / 20f; y += font.GetHeight(graphics) + (float)style.Real("lineSpacing") / 20; }
                        }
                    }
                }
            }
            var shape = new FlaRecord("CPicShape"); var data = new FlaRecord("shapeData"); data.set("edges", edges); data.set("fills", fills); shape.set("shapeData", data); int id = new ShapeCompiler(compiler).Shape(shape);
            if (id != 0) return id; id = compiler.NextId(); var empty = new MovieSymbol { Id = id }; empty.Frames.Add(new SceneFrame()); compiler.Define(empty); return id;
        }
        private static void Edge(List<FlaRecord> edges, PointF a, PointF b, int fill)
        { if (a == b) return; var e = new FlaRecord("edge"); e.set("startX", CoordinateMath.Round(a.X * 20 * 256)); e.set("startY", CoordinateMath.Round(a.Y * 20 * 256)); e.set("endX", CoordinateMath.Round(b.X * 20 * 256)); e.set("endY", CoordinateMath.Round(b.Y * 20 * 256)); e.set("fillIndex1", fill); edges.Add(e); }
    }
}
