using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BinaryAssetBuilder.UiCompiler.Fla;

namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal sealed class FlaCompiler
    {
        private readonly FlaDocument document;
        internal readonly SceneModel Model = new SceneModel();
        private readonly List<SceneControl> initializers = new List<SceneControl>();
        private readonly ShapeCompiler shapes;
        private readonly Dictionary<long, FlaRecord> library, media;
        private readonly Dictionary<string, int> symbols = new Dictionary<string, int>();
        private readonly HashSet<FlaRecord> compiling = new HashSet<FlaRecord>();
        private readonly Dictionary<FlaRecord, int> shapeIds = new Dictionary<FlaRecord, int>();
        private readonly Dictionary<FlaRecord, int> textIds = new Dictionary<FlaRecord, int>();
        private readonly Dictionary<long, BitmapAsset> bitmaps = new Dictionary<long, BitmapAsset>();
        private readonly Dictionary<FlaRecord, Timeline> timelines = new Dictionary<FlaRecord, Timeline>();
        private int lastId;
        private readonly SourceProject project;
        private readonly string flaPath;
        internal FlaCompiler(FlaDocument document, SourceProject project = null, string flaPath = null)
        {
            this.document = document; this.project = project; this.flaPath = flaPath; shapes = new ShapeCompiler(this);
            library = document.Library.Where(d => d.get("streamName") != null).ToDictionary(d => d.Number("libraryId", -1));
            media = document.Media.Where(d => d.get("streamName") != null).ToDictionary(d => d.Number("mediaId", -1));
            if (library.ContainsKey(-1) || media.ContainsKey(-1)) throw new InvalidDataException("Missing library identifier.");
        }
        internal int NextId() { if (lastId == 65535) throw new InvalidDataException("Too many library symbols."); return ++lastId; }
        internal void Define(SceneSymbol symbol) => Model.Symbols.Add(symbol.Id, symbol);
        internal int DefineBitmap(byte[] argb, int width, int height)
        {
            int id = NextId(); var pixels = new byte[argb.Length];
            for (int i = 0; i < pixels.Length; i += 4) { int alpha = argb[i], divisor = Math.Max(1, alpha); pixels[i] = (byte)Math.Min(255, argb[i + 3] * 255 / divisor); pixels[i + 1] = (byte)Math.Min(255, argb[i + 2] * 255 / divisor); pixels[i + 2] = (byte)Math.Min(255, argb[i + 1] * 255 / divisor); pixels[i + 3] = (byte)alpha; }
            Define(new ImageSymbol { Id = id, Width = width, Height = height, Pixels = pixels }); return id;
        }
        internal SceneModel Compile()
        {
            var previous = new List<Placement>(); string external = project?.TimelineScript(flaPath) ?? "";
            foreach (var scene in document.Scenes)
            {
                var timeline = GetTimeline(scene);
                for (int f = 0; f < timeline.Length; f++)
                {
                    var frame = new SceneFrame(); var current = timeline.Frame(f); Display(frame.Controls, previous, current);
                    if (Model.Frames.Count == 0 && !string.IsNullOrWhiteSpace(external)) frame.Controls.Add(new SceneControl { Kind = ControlKind.Actions, Actions = ActionScriptCompiler.Compile(external) });
                    timeline.Events(frame.Controls, f); Model.Frames.Add(frame); previous = current;
                    if (Model.Frames.Count > 65535) throw new InvalidDataException("Too many frames.");
                }
            }
            if (Model.Frames.Count == 0) throw new InvalidDataException("FLA contains no scene.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in library.Values.Concat(media.Values))
            {
                var link = descriptor.Record("libraryMetadata")?.Record("linkage") ?? descriptor.Record("linkage"); if (link == null) continue;
                int flags = (int)link.Number("flags"); if ((flags & 2) != 0 || link.Text("url").Length != 0) throw new NotSupportedException("Runtime shared-library imports require an external library resolver.");
                if (link.Text("className").Length != 0) { if (project == null) throw new InvalidDataException("AS2 linkage requires the source folder."); project.RequireClass(link.Text("className")); }
                if ((flags & 1) == 0) continue;
                string name = link.Text("identifier"); if (name.Length == 0 || !names.Add(name)) throw new InvalidDataException("Missing/duplicate linkage identifier.");
                int id = library.ContainsValue(descriptor) ? Symbol(descriptor, -1, descriptor.Number("symbolType") == 1) : descriptor.ClassName == "CMediaBits" ? Bitmap(descriptor.Number("mediaId")).Id : Sound(descriptor.Number("mediaId"));
                Model.Exports.Add(new KeyValuePair<string, int>(name, id));
            }
            int[] bounds = document.Contents.Words("boundsWords", 4); Model.WidthTwips = bounds[1] - bounds[0]; Model.HeightTwips = bounds[3] - bounds[2]; double rate = document.Contents.Real("frameRateWord") / 65536;
            if (Model.WidthTwips <= 0 || Model.HeightTwips <= 0 || rate <= 0 || rate >= 256) throw new InvalidDataException("Invalid stage dimensions/frame rate.");
            Model.FrameRate = CoordinateMath.Round(rate * 256) / 256.0;
            var prologue = new List<SceneControl> { new SceneControl { Kind = ControlKind.Background, Background = unchecked((uint)document.Contents.Number("backgroundColorRgba", 0xffffffffL)) | 0xff000000u } };
            prologue.AddRange(initializers);
            if (project != null && project.ClassOrder.Count > 0) prologue.Add(new SceneControl { Kind = ControlKind.Actions, Actions = End(ActionProgram.Join(project.ClassOrder.Select(c => c.Code).ToArray())) });
            Model.Frames[0].Controls.InsertRange(0, prologue); return Model;
        }
        private static ActionProgram End(ActionProgram program) => new ActionProgram(program.Instructions.Concat(new[] { new Instruction(0) }));
        private Timeline GetTimeline(FlaRecord descriptor)
        { if (!timelines.TryGetValue(descriptor, out Timeline result)) { result = new Timeline(this, document.Page(descriptor)); timelines.Add(descriptor, result); } return result; }
        private int Shape(FlaRecord record) { if (!shapeIds.TryGetValue(record, out int id)) shapeIds.Add(record, id = shapes.Shape(record)); return id; }
        private int Symbol(FlaRecord descriptor, int frame = -1, bool button = false, bool menu = false)
        {
            string key = descriptor.Number("libraryId") + ":" + frame + ":" + button + ":" + menu;
            if (compiling.Contains(descriptor)) throw new NotSupportedException("Recursive symbol graph.");
            if (symbols.TryGetValue(key, out int id)) return id;
            if (descriptor.Record("component")?.Number("componentEnabled") != null && descriptor.Record("component").Number("componentEnabled") != 0) throw new NotSupportedException("Precompiled components require source symbols.");
            compiling.Add(descriptor); id = NextId(); symbols.Add(key, id);
            try
            {
                var t = GetTimeline(descriptor);
                if (button)
                {
                    if (t.Length > 4) throw new InvalidDataException("Button has more than four states.");
                    // Button state frames are editor-only visual states, not a
                    // movie timeline. Flash ignores archived frame scripts here;
                    // instance on(...) handlers are compiled at placement. This
                    // also preserves old buttons containing stale clip handlers.
                    var symbol = new ButtonSymbol { Id = id, Menu = menu };
                    for (int f = 0; f < t.Length; f++) foreach (var placement in t.Frame(f))
                    { RejectVisualEffects(placement.Record); if (Source(placement.Record).Length != 0) throw new NotSupportedException("Button child actions."); symbol.Parts.Add(new ButtonPart { State = 1 << f, SymbolId = placement.Id, Depth = placement.Depth, Matrix = CoordinateMath.Matrix(placement.Matrix), Color = Color(placement.Record) }); }
                    Define(symbol);
                }
                else
                {
                    var symbol = new MovieSymbol { Id = id }; int length = frame < 0 ? t.Length : 1; var previous = new List<Placement>();
                    for (int f = 0; f < length; f++) { var destination = new SceneFrame(); var current = t.Frame(frame < 0 ? f : frame); Display(destination.Controls, previous, current); t.Events(destination.Controls, frame < 0 ? f : frame); if (frame >= 0) destination.Controls.Add(new SceneControl { Kind = ControlKind.Actions, Actions = ActionScriptCompiler.Compile("stop();") }); symbol.Frames.Add(destination); previous = current; }
                    Define(symbol);
                }
                if (descriptor.Number("scaleGridEnabled") != 0) throw new NotSupportedException("Scale-9 grids are not supported by the RA3 APT runtime.");
                var link = descriptor.Record("libraryMetadata")?.Record("linkage") ?? descriptor.Record("linkage"); string className = link?.Text("className") ?? "";
                if (className.Length != 0)
                {
                    if (project == null) throw new InvalidDataException("Missing AS2 source folder for " + className); project.RequireClass(className); string identifier = link.Text("identifier"); if (identifier.Length == 0) throw new InvalidDataException("AS2 class linkage needs an export identifier.");
                    string quoted = "\"" + identifier.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    var programs = project.ClassOrder.Select(c => c.Code).Concat(new[] { ActionScriptCompiler.Compile("Object.registerClass(" + quoted + ", _global." + className + ");") }).ToArray();
                    initializers.Add(new SceneControl { Kind = ControlKind.Initialize, SymbolId = id, Actions = ActionProgram.Join(programs) });
                }
                return id;
            }
            finally { compiling.Remove(descriptor); }
        }
        private void Objects(List<Placement> output, FlaRecord r, int age)
        {
            int id;
            switch (r.ClassName)
            {
                case "CPicFrame": case "CPicShape": foreach (var child in r.getChildren()) Objects(output, child, age); id = Shape(r); if (id != 0) output.Add(new Placement(id, r)); return;
                case "CPicSymbol": case "CPicShapeObj": case "CPicSprite": case "CPicButton":
                    if (!library.TryGetValue(r.Number("libraryId", -1), out var descriptor)) throw new InvalidDataException("Missing library symbol.");
                    bool graphic = r.ClassName == "CPicSymbol" || r.ClassName == "CPicShapeObj";
                    int frame = -1;
                    if (graphic) { int length = GetTimeline(descriptor).Length, first = (int)r.Number("firstFrame"), mode = (int)r.Number("loopMode"); if (first < 0 || first >= length || mode < 0 || mode > 2) throw new InvalidDataException("Invalid graphic playback settings."); frame = mode == 2 ? first : mode == 1 ? Math.Min(first + age, length - 1) : (first + age) % length; }
                    id = Symbol(descriptor, frame, r.ClassName == "CPicButton", r.Number("trackAsMenu") != 0); break;
                case "CPicBitmap": if (r.Record("raster") != null) throw new NotSupportedException("Inline bitmap replacement."); id = Bitmap(r.Number("mediaId", -1)).ShapeId; break;
                case "CPicText": if (!textIds.TryGetValue(r, out id)) textIds.Add(r, id = new TextCompiler(this).Compile(r)); break;
                default: throw new NotSupportedException("Display object " + r.ClassName);
            }
            output.Add(new Placement(id, r));
        }
        internal sealed class Placement
        {
            internal int Id, Depth, ClipDepth; internal FlaRecord Record; internal double[] Matrix;
            internal Placement(int id, FlaRecord record) { Id = id; Record = record; Matrix = record.Matrix(); }
        }
        private void Display(List<SceneControl> output, List<Placement> before, List<Placement> after)
        {
            var old = before.ToDictionary(p => p.Depth); var depths = new HashSet<int>(after.Select(p => p.Depth));
            foreach (var p in before) if (!depths.Contains(p.Depth)) output.Add(new SceneControl { Kind = ControlKind.Remove, Depth = p.Depth });
            foreach (var p in after)
            { old.TryGetValue(p.Depth, out var previous); if (previous != null && Equivalent(previous, p)) continue; output.Add(Place(p, previous != null, previous == null || previous.Id != p.Id)); }
        }
        private bool Equivalent(Placement a, Placement b)
        {
            if (a.Id != b.Id || a.ClipDepth != b.ClipDepth || a.Record.Text("instanceName") != b.Record.Text("instanceName") || !CoordinateMath.Matrix(a.Matrix).SequenceEqual(CoordinateMath.Matrix(b.Matrix))) return false;
            if ((a.Record.ClassName == "CPicFrame" ? "" : SourceText(a.Record)) != (b.Record.ClassName == "CPicFrame" ? "" : SourceText(b.Record))) return false;
            var ac = Color(a.Record); var bc = Color(b.Record); return ac == null ? bc == null : bc != null && ac.Multipliers.SequenceEqual(bc.Multipliers) && ac.Offsets.SequenceEqual(bc.Offsets);
        }
        private SceneControl Place(Placement placement, bool move, bool character)
        {
            RejectVisualEffects(placement.Record);
            return new SceneControl { Kind = ControlKind.Place, Depth = placement.Depth, SymbolId = placement.Id, Move = move, Character = character, ClipDepth = placement.ClipDepth, Name = placement.Record.Text("instanceName"), Matrix = CoordinateMath.Matrix(placement.Matrix), Color = Color(placement.Record), Events = ActionScriptCompiler.CompileHandlers(placement.Record.ClassName == "CPicFrame" ? "" : SourceText(placement.Record), placement.Record.ClassName == "CPicButton") };
        }
        private static void RejectVisualEffects(FlaRecord r)
        { if (r.Number("blendMode", 1) > 1 || (r.Record("filters")?.Records("entries") ?? new List<FlaRecord>()).Any(f => f.Number("enabled", 1) != 0)) throw new NotSupportedException("Flash filters/blend modes are not representable by the RA3 APT runtime."); }
        private static ColorEffect Color(FlaRecord owner)
        {
            var r = owner.Record("colorTransform"); if (r == null) return null; int type = (int)r.Number("effectType", owner.Number("colorEffectType", 3)); if (type == 0) return null;
            int[] mult = { 256, 256, 256, 256 }, add = new int[4]; string[] channel = { "red", "green", "blue", "alpha" };
            for (int i = 0; i < 4; i++) { mult[i] = (int)r.Number(channel[i] + "Multiplier", 256); add[i] = (int)r.Number(channel[i] + "Offset"); }
            if (type == 1 || type == 2) { double amount = r.Real("effectPercent", owner.Real("colorEffectPercent")) / 100; if (Math.Abs(amount) > 1) throw new InvalidDataException("Color transform percentage."); long tint = r.Number("effectColorRgba", owner.Number("colorEffectRgba")); for (int i = 0; i < 3; i++) { mult[i] = CoordinateMath.Round((1 - Math.Abs(amount)) * 256); add[i] = CoordinateMath.Round(type == 1 ? Math.Max(0, amount) * 255 : (tint >> (8 * i) & 255) * amount); } }
            else if (type == 4) { for (int i = 0; i < 3; i++) { mult[i] = 256; add[i] = 0; } add[3] = 0; }
            bool hm = mult.Any(v => v != 256), ha = add.Any(v => v != 0); if (!hm && !ha) return null; if (mult.Concat(add).Any(v => v < -16384 || v > 16383)) throw new InvalidDataException("Color transform overflow.");
            return new ColorEffect { Multipliers = mult, Offsets = add };
        }
        internal static string Source(FlaRecord r)
        { var actions = r.Record("actions"); if (actions == null) return ""; if (actions.get("source") is string source) return source; if (actions.Records("commands").Count != 0 || actions.Record("compiledCommands") != null) throw new NotSupportedException("Archived editor-command actions need source reconstruction."); return ""; }
        private string SourceText(FlaRecord r) => project?.Embedded(Source(r), flaPath) ?? Source(r);
        private sealed class Timeline
        {
            private readonly FlaCompiler compiler; private readonly List<FlaRecord> layers; private readonly int[] bases, capacities;
            internal readonly int Length;
            internal Timeline(FlaCompiler compiler, FlaRecord page)
            {
                this.compiler = compiler; layers = page.getChildren().ToList(); int length = 1, depth = 1;
                foreach (var mask in layers.ToArray()) if (mask.getLayerType() == 4) { int first = layers.IndexOf(mask); foreach (var child in layers) if (child.getLayerParent() == mask) first = Math.Min(first, layers.IndexOf(child)); layers.Remove(mask); layers.Insert(first, mask); }
                bases = new int[layers.Count]; capacities = new int[layers.Count];
                for (int i = 0; i < layers.Count; i++)
                { if (layers[i].ClassName != "CPicLayer") throw new InvalidDataException("Invalid timeline layer."); int end = 0, capacity = 1; foreach (var f in layers[i].getChildren()) { int d = (int)f.Number("duration", 1); if (d <= 0 || end + d > 65535) throw new InvalidDataException("Invalid frame duration."); end += d; capacity = Math.Max(capacity, ObjectCount(f)); } bases[i] = depth; capacities[i] = capacity; depth += capacity; length = Math.Max(length, end); }
                if (depth > 65535) throw new InvalidDataException("Too many display depths."); Length = length;
            }
            private static int ObjectCount(FlaRecord r) => r.ClassName == "CPicFrame" || r.ClassName == "CPicShape" ? r.getChildren().Sum(ObjectCount) + (r.Record("shapeData")?.Records("edges").Count > 0 ? 1 : 0) : 1;
            internal List<Placement> Frame(int index)
            {
                var result = new List<Placement>();
                for (int i = 0; i < layers.Count; i++)
                {
                    int type = layers[i].getLayerType(); if (type == 1 || type == 3) continue; if (type != 0 && type != 2 && type != 4 && type != 5) throw new NotSupportedException("Layer type " + type);
                    int start = 0; var frames = layers[i].getChildren();
                    for (int k = 0; k < frames.Count; k++)
                    {
                        var f = frames[k]; int duration = (int)f.Number("duration", 1);
                        if (index >= start && index < start + duration)
                        {
                            var items = new List<Placement>(); int mode = (int)f.Number("keyMode") & 255;
                            if (mode == 2 && index > start) compiler.Morph(items, f, (index - start) / (double)duration); else compiler.Objects(items, f, index - start);
                            if (mode == 1 && k + 1 < frames.Count && index > start)
                            { if (layers[i].getLayerParent()?.getLayerType() == 1) throw new NotSupportedException("Guided motion paths."); var target = new List<Placement>(); compiler.Objects(target, frames[k + 1], 0); TweenCompiler.Apply(items, target, f, (index - start) / (double)duration); }
                            if (items.Count > capacities[i]) throw new InvalidDataException("Unexpected display-list expansion.");
                            for (int j = 0; j < items.Count; j++) { items[j].Depth = bases[i] + j; result.Add(items[j]); } break;
                        }
                        start += duration;
                    }
                }
                for (int i = 0; i < layers.Count; i++) if (layers[i].getLayerType() == 4)
                { int end = 0; for (int j = 0; j < layers.Count; j++) if (layers[j].getLayerParent() == layers[i]) end = Math.Max(end, bases[j] + capacities[j] - 1); var masks = result.Where(p => p.Depth >= bases[i] && p.Depth < bases[i] + capacities[i]).ToList(); if (end > 0 && masks.Count > 0) { if (masks.Count != 1 || end <= masks[0].Depth) throw new NotSupportedException("Mask requires a contiguous depth range."); masks[0].ClipDepth = end; } }
                return result;
            }
            internal void Events(List<SceneControl> output, int index)
            {
                var labels = new HashSet<string>();
                foreach (var layer in layers)
                {
                    if (layer.getLayerType() == 1 || layer.getLayerType() == 3) continue; int start = 0;
                    foreach (var f in layer.getChildren())
                    {
                        if (index == start)
                        { string label = f.Text("label"); if (label.Length > 0 && labels.Add(label)) { output.Add(new SceneControl { Kind = ControlKind.Label, Name = label }); } string source = compiler.SourceText(f); if (source.Trim().Length > 0) output.Add(new SceneControl { Kind = ControlKind.Actions, Actions = ActionScriptCompiler.Compile(source) }); if (f.Number("soundMediaId") != 0) compiler.SoundEvent(f); }
                        start += (int)f.Number("duration", 1);
                    }
                }
            }
        }
        internal sealed class BitmapAsset { internal int Id, ShapeId; internal BitmapAsset(int id, int shape) { Id = id; ShapeId = shape; } }
        internal BitmapAsset Bitmap(long mediaId)
        {
            if (bitmaps.TryGetValue(mediaId, out var old)) return old;
            if (!media.TryGetValue(mediaId, out var descriptor) || descriptor.ClassName != "CMediaBits") throw new InvalidDataException("Missing bitmap " + mediaId);
            byte[] argb = BitmapDecoder.Decode(document.Payload(descriptor), out int width, out int height); int id = DefineBitmap(argb, width, height);
            var edge = new FlaRecord("edge"); var data = new FlaRecord("shapeData"); var shape = new FlaRecord("CPicShape"); var fill = new FlaRecord("fill"); fill.set("flags", 0x42); fill.set("bitmapMediaId", mediaId); fill.set("matrixWords", new List<int> { 20 * 65536, 0, 0, 20 * 65536, 0, 0 });
            var edges = new List<FlaRecord>(); int[] xs = { 0, width * 20, width * 20, 0, 0 }, ys = { 0, 0, height * 20, height * 20, 0 };
            for (int i = 0; i < 4; i++) { var e = new FlaRecord("edge"); e.set("startX", xs[i] * 256); e.set("startY", ys[i] * 256); e.set("endX", xs[i + 1] * 256); e.set("endY", ys[i + 1] * 256); e.set("fillIndex1", 1); edges.Add(e); }
            data.set("edges", edges); data.set("fills", new List<FlaRecord> { fill }); shape.set("shapeData", data);
            // Cache before the rectangle's bitmap fill resolves this same media ID.
            var result = new BitmapAsset(id, 0); bitmaps.Add(mediaId, result); result.ShapeId = shapes.Shape(shape); return result;
        }
        private int Sound(long mediaId) => throw new NotSupportedException("Embedded audio is not supported by the RA3 UI asset compiler.");
        private void SoundEvent(FlaRecord frame) => throw new NotSupportedException("Embedded audio is not supported by the RA3 UI asset compiler.");
        private void Morph(List<Placement> output, FlaRecord frame, double t) => MorphCompiler.Apply(this, shapes, output, frame, t);
    }
}
