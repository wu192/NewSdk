// Runtime layouts derived from SWF2APT (GPL-3.0). Source compiler objects are
// encoded directly in the final AssetBuffers; no converter/file-format IR.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BinaryAssetBuilder.UiCompiler.Compilation;

namespace BinaryAssetBuilder.UiCompiler.Runtime
{
    internal sealed class AssetEncoder
    {
        private readonly SceneModel scene;
        private readonly ActionEncoder actions = new ActionEncoder();
        private readonly Dictionary<string, ActionEncoder.Site> sites = new Dictionary<string, ActionEncoder.Site>();
        private readonly HashSet<int> hitShapes = new HashSet<int>(), referenced = new HashSet<int>();
        private readonly Dictionary<int, HitMesh> buttonHits = new Dictionary<int, HitMesh>();
        private int mainCharacter, characterTable, exports;
        private sealed class HitMesh
        {
            internal double[] Bounds = { 0, -20, 0, -20 };
            internal readonly List<float> Vertices = new List<float>();
            internal readonly List<ushort> Indices = new List<ushort>();
            internal void Add(Point2 point)
            {
                float x = (float)point.X / 20.0f, y = (float)point.Y / 20.0f; int index = -1;
                for (int i = 0; i < Vertices.Count; i += 2) if (Vertices[i] == x && Vertices[i + 1] == y) { index = i / 2; break; }
                if (index < 0) { index = Vertices.Count / 2; if (index > ushort.MaxValue) throw new InvalidDataException("Button hit mesh is too large."); Vertices.Add(x); Vertices.Add(y); }
                Indices.Add((ushort)index);
            }
        }
        internal AssetEncoder(SceneModel scene)
        {
            this.scene = scene;
            foreach (var export in scene.Exports) referenced.Add(export.Value);
            PlanMovie(scene.Frames, "0");
            foreach (SceneSymbol symbol in scene.Symbols.Values)
            {
                if (symbol is MovieSymbol movie) PlanMovie(movie.Frames, symbol.Id.ToString());
                if (symbol is ButtonSymbol button)
                {
                    var hit = new HitMesh(); buttonHits.Add(button.Id, hit);
                    foreach (var part in button.Parts)
                    {
                        if ((part.State & 7) != 0) referenced.Add(part.SymbolId);
                        if ((part.State & 8) == 0) continue;
                        hitShapes.Add(part.SymbolId);
                        if (!scene.Symbols.TryGetValue(part.SymbolId, out var source) || !(source is MeshSymbol mesh)) continue;
                        hit.Bounds = mesh.Bounds;
                        foreach (var batch in mesh.Batches) foreach (var triangle in batch.Triangles) { hit.Add(triangle.A); hit.Add(triangle.B); hit.Add(triangle.C); }
                    }
                }
            }
            foreach (var mesh in scene.Symbols.Values.OfType<MeshSymbol>().Where(m => !hitShapes.Contains(m.Id) || referenced.Contains(m.Id)))
                foreach (var batch in mesh.Batches)
                    if (batch.Material.ImageId != 0 && (batch.Material.Matrix[1] != 0 || batch.Material.Matrix[2] != 0))
                        ((ImageSymbol)scene.Symbols[batch.Material.ImageId]).NoPack = true;
        }
        private void PlanMovie(List<SceneFrame> frames, string key)
        {
            for (int f = 0; f < frames.Count; f++) for (int c = 0; c < frames[f].Controls.Count; c++)
            {
                var control = frames[f].Controls[c]; string path = key + "/f" + f + "/c" + c;
                if (control.Actions != null) sites.Add(path + "/action", actions.Plan(control.Actions, path + "/action"));
                if (control.Kind == ControlKind.Initialize || control.Kind == ControlKind.Place && control.Character) referenced.Add(control.SymbolId);
                for (int e = 0; e < control.Events.Count; e++) sites.Add(path + "/e" + e, actions.Plan(control.Events[e].Actions, path + "/e" + e));
            }
        }
        internal List<CompiledUiAsset> Build(string name, string source)
        {
            var assets = new List<CompiledUiAsset> {
                new CompiledUiAsset("AptAptData", name + "_apt", source, WriteScene),
                new CompiledUiAsset("AptConstData", name + "_const", source, s => actions.WriteConstants(new RuntimeOutput(s), mainCharacter)),
                new CompiledUiAsset("AptDatData", name + "_dat", source, WriteImageMap)
            };
            foreach (var mesh in scene.Symbols.Values.OfType<MeshSymbol>()) assets.Add(new CompiledUiAsset("AptGeometryData", name + "_" + mesh.Id, source, s => WriteMesh(s, mesh), mesh.Id));
            foreach (var image in scene.Symbols.Values.OfType<ImageSymbol>()) assets.Add(new CompiledUiAsset("Texture", "apt_" + name + "_" + image.Id, source, s => TextureBuilder.Write(s, image)));
            return assets;
        }
        private int CharacterCount => scene.Symbols.Count == 0 ? 1 : checked(scene.Symbols.Keys.Max() + 1);
        private void WriteScene(Stream stream)
        {
            var output = new RuntimeOutput(stream); output.Begin("Apt Data:84\u001a\0");
            output.Pass = 1; output.Align(1); exports = output.Size;
            for (output.Pass = 1; output.Pass <= 3; output.Pass++)
            {
                for (int i = 0; i < scene.Exports.Count; i++)
                {
                    string key = "export/" + i; output.Pointer(2, key); output.Write(2, scene.Exports[i].Value);
                    output.Align(3); output.Resolve(3, key); output.Write(3, scene.Exports[i].Key);
                }
            }
            for (output.Pass = 1; output.Pass <= 11; output.Pass++)
            {
                output.Align(1); if (output.Pass == 1) characterTable = output.Size;
                for (int i = 0; i < CharacterCount; i++)
                {
                    SceneSymbol symbol = null;
                    if (i != 0 && !scene.Symbols.TryGetValue(i, out symbol)) { output.Write(2, 0); continue; }
                    string key = i.ToString(); output.Pointer(2, "char/" + key); output.Align(3); output.Resolve(3, "char/" + key);
                    if (i == 0)
                    {
                        if (output.Pass == 3) mainCharacter = output.Size;
                        output.Write(3, 9); output.Write(3, 0x09876543); WriteMovie(output, scene.Frames, key);
                        output.Write(3, CharacterCount); output.Write(3, characterTable); output.Write(3, scene.WidthTwips / 20); output.Write(3, scene.HeightTwips / 20); output.Write(3, (int)(1000 / scene.FrameRate));
                        output.Write(3, 0); output.Write(3, 16); output.Write(3, scene.Exports.Count); output.Write(3, exports); output.Write(3, 0);
                    }
                    else WriteSymbol(output, symbol, key);
                }
            }
            output.Complete();
        }
        private void WriteSymbol(RuntimeOutput output, SceneSymbol symbol, string key)
        {
            switch (symbol)
            {
                case MovieSymbol movie:
                    output.Write(3, 5); output.Write(3, 0x09876543); WriteMovie(output, movie.Frames, key); break;
                case MeshSymbol mesh:
                    output.Write(3, 1); output.Write(3, 0x09876543); output.Rectangle(3, mesh.Bounds); output.Write(3, mesh.Id); break;
                case ImageSymbol image:
                    output.Write(3, 7); output.Write(3, 0x09876543); output.Write(3, image.Id); break;
                case FontSymbol font:
                    output.Write(3, 3); output.Write(3, 0x09876543);
                    if (font.Name.Length == 0) output.Write(3, 0); else Text(output, 3, key + "/font", font.Name);
                    output.Write(3, 0); output.Write(3, 0); break;
                case TextSymbol text:
                    output.Write(3, 2); output.Write(3, 0x09876543); output.Rectangle(3, text.Bounds);
                    output.Write(3, text.FontId); output.Write(3, text.Alignment); output.Color(3, text.Color); output.Write(3, text.HeightTwips / 20.0f);
                    output.Write(3, text.ReadOnly); output.Write(3, text.Multiline); output.Write(3, text.WordWrap);
                    Text(output, 3, key + "/initial", text.InitialText); Text(output, 3, key + "/variable", text.VariableName); break;
                case ButtonSymbol button:
                    WriteButton(output, button, key); break;
                default: throw new InvalidDataException("Unknown compiler symbol.");
            }
        }
        private static void Text(RuntimeOutput output, int pass, string key, string text)
        { output.Pointer(pass, key); output.Align(pass + 1); output.Resolve(pass + 1, key); output.Write(pass + 1, text); }
        private void WriteButton(RuntimeOutput output, ButtonSymbol button, string key)
        {
            HitMesh hit = buttonHits[button.Id]; output.Write(3, 4); output.Write(3, 0x09876543); output.Write(3, button.Menu); output.Rectangle(3, hit.Bounds);
            output.Write(3, hit.Indices.Count / 3); output.Write(3, hit.Vertices.Count / 2); output.Pointer(3, key + "/vertices"); output.Pointer(3, key + "/indices");
            output.Align(4); output.Resolve(4, key + "/vertices"); foreach (float v in hit.Vertices) output.Write(4, v);
            output.Align(4); output.Resolve(4, key + "/indices"); foreach (ushort i in hit.Indices) output.Write(4, i);
            output.Write(3, button.Parts.Count); output.Pointer(3, key + "/parts"); output.Write(3, 0); output.Pointer(3, key + "/conditions"); output.Write(3, 0);
            output.Align(4); output.Resolve(4, key + "/parts");
            foreach (var part in button.Parts)
            { output.Align(4); output.Write(4, part.State); output.Write(4, part.SymbolId); output.Write(4, part.Depth); output.Matrix(4, part.Matrix); output.FloatColor(4, part.Color); }
            output.Align(4); output.Resolve(4, key + "/conditions");
        }
        private void WriteMovie(RuntimeOutput output, List<SceneFrame> frames, string key)
        {
            output.Write(3, frames.Count);
            if (frames.Count == 0) output.Write(3, 0);
            else
            {
                output.Pointer(3, key + "/frames"); output.Align(4); output.Resolve(4, key + "/frames");
                for (int f = 0; f < frames.Count; f++)
                {
                    string frame = key + "/f" + f; var controls = frames[f].Controls; output.Align(4); output.Write(4, controls.Count);
                    if (controls.Count == 0) { output.Write(4, 0); continue; }
                    output.Pointer(4, frame); output.Align(5); output.Resolve(5, frame);
                    for (int c = 0; c < controls.Count; c++) WriteControl(output, controls[c], frame + "/c" + c, f);
                }
            }
            output.Write(3, 0);
        }
        private void WriteControl(RuntimeOutput output, SceneControl control, string key, int frameIndex)
        {
            output.Pointer(5, key); output.Align(6); output.Resolve(6, key); output.Write(6, (int)control.Kind);
            switch (control.Kind)
            {
                case ControlKind.Actions: actions.Write(output, 6, sites[key + "/action"]); break;
                case ControlKind.Initialize: output.Write(6, control.SymbolId); actions.Write(output, 6, sites[key + "/action"]); break;
                case ControlKind.Label:
                    output.Pointer(6, key + "/name"); output.Write(6, (ushort)0); output.Write(6, (ushort)7); output.Write(6, frameIndex);
                    output.Align(7); output.Resolve(7, key + "/name"); output.Write(7, control.Name); break;
                case ControlKind.Remove: output.Write(6, control.Depth); break;
                case ControlKind.Background: output.Color(6, control.Background); break;
                case ControlKind.Place:
                    int flags = (control.Events.Count > 0 ? 128 : 0) | (control.ClipDepth > 0 ? 64 : 0) | (control.Name.Length > 0 ? 32 : 0) | (control.Color != null ? 8 : 0) | 4 | (control.Character ? 2 : 0) | (control.Move ? 1 : 0);
                    output.Write(6, flags); output.Write(6, control.Depth); output.Write(6, control.Character ? control.SymbolId : -1); output.Matrix(6, control.Matrix); output.ByteColor(6, control.Color); output.Write(6, 0.0f);
                    if (control.Name.Length > 0) Text(output, 6, key + "/name", control.Name); else output.Write(6, 0);
                    output.Write(6, control.ClipDepth > 0 ? control.ClipDepth : -1);
                    if (control.Events.Count == 0) output.Write(6, 0);
                    else
                    {
                        output.Pointer(6, key + "/events"); output.Align(7); output.Resolve(7, key + "/events"); output.Write(7, control.Events.Count); output.Pointer(7, key + "/eventTable");
                        output.Align(8); output.Resolve(8, key + "/eventTable");
                        for (int e = 0; e < control.Events.Count; e++) { output.Write(8, control.Events[e].Flags); output.Write(8, (int)control.Events[e].Key); actions.Write(output, 8, sites[key + "/e" + e]); }
                    }
                    break;
                default: throw new InvalidDataException("Unknown compiler control.");
            }
        }
        private void WriteImageMap(Stream destination)
        {
            using (var text = new StreamWriter(destination, new UTF8Encoding(false), 1024, true))
            {
                text.Write("; Created by AptToXML.\r\n");
                foreach (var image in scene.Symbols.Values.OfType<ImageSymbol>().Where(i => i.NoPack)) text.Write(image.Id + "=0 0 " + image.Width + " " + image.Height + "\r\n");
                text.Write("; Created by AptToXML.\r\n");
                foreach (var image in scene.Symbols.Values.OfType<ImageSymbol>().Where(i => !i.NoPack)) text.Write(image.Id + "->" + image.Id + "\r\n");
            }
        }
        private string MaterialKey(Material material)
        {
            if (material.ImageId == 0) { uint c = material.Color; return "s:" + (c & 255) + ":" + (c >> 8 & 255) + ":" + (c >> 16 & 255) + ":" + (c >> 24); }
            double[] m = material.Matrix.Select(v => v / 20.0f).ToArray();
            // Array stores force the same Single rounding on the x86 CLR/x87.
            float[] translation = { (float)material.Matrix[4] / 20.0f, (float)material.Matrix[5] / 20.0f };
            m[4] = translation[0]; m[5] = translation[1];
            double det = m[0] * m[3] - m[1] * m[2];
            if (det == 0) throw new InvalidDataException("Singular bitmap matrix.");
            var inv = new[] { m[3] / det, -m[1] / det, -m[2] / det, m[0] / det, 0.0, 0.0 };
            int offset = ((ImageSymbol)scene.Symbols[material.ImageId]).NoPack ? 0 : 1;
            inv[4] = -(inv[0] * m[4] + inv[2] * m[5]) + offset; inv[5] = -(inv[1] * m[4] + inv[3] * m[5]) + offset;
            return (material.Repeat ? "tw" : "tc") + ":255:255:255:255:" + material.ImageId + ":" + string.Join(":", inv.Select(v => ((float)(v == 0 ? 0 : v)).ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)));
        }
        private void WriteMesh(Stream destination, MeshSymbol mesh)
        {
            using (var text = new StreamWriter(destination, new UTF8Encoding(false), 1024, true))
            {
                if (mesh.Batches.Count == 0 || hitShapes.Contains(mesh.Id) && !referenced.Contains(mesh.Id)) { text.Write("c\r\ns s:0:0:0:0\r\nt 0:0:0:0:0:0\r\n"); return; }
                // Group object references by material; never accumulate RU text.
                var groups = new List<KeyValuePair<string, List<Triangle>>>();
                foreach (var batch in mesh.Batches)
                {
                    string key = MaterialKey(batch.Material); int i = groups.FindIndex(g => g.Key == key);
                    if (i < 0) { i = groups.Count; groups.Add(new KeyValuePair<string, List<Triangle>>(key, new List<Triangle>())); }
                    groups[i].Value.AddRange(batch.Triangles);
                }
                text.Write("c\r\n");
                foreach (var group in groups)
                {
                    text.Write("s "); text.Write(group.Key); text.Write("\r\n");
                    foreach (var triangle in group.Value)
                    {
                        text.Write("t "); bool first = true;
                        foreach (var p in new[] { triangle.A, triangle.B, triangle.C }) foreach (double v in new[] { p.X, p.Y }) { if (!first) text.Write(':'); first = false; text.Write(v / 20.0f); }
                        text.Write("\r\n");
                    }
                }
            }
        }
    }
}
