// APT runtime field layout derived from the GPL-3.0 SWF2APT serializer.
// This writer is bound to the final asset's payload view, never to an APT buffer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BinaryAssetBuilder.UiCompiler.Compilation;

namespace BinaryAssetBuilder.UiCompiler.Runtime
{
    internal sealed class RuntimeOutput
    {
        private readonly Stream stream;
        private readonly BinaryWriter writer;
        private readonly Dictionary<string, int> fixups = new Dictionary<string, int>();
        internal int Pass;
        internal RuntimeOutput(Stream payload) { stream = payload; writer = new BinaryWriter(payload, new UTF8Encoding(false), true); }
        internal int Size => checked((int)stream.Length);
        internal void Begin(string header) { writer.Write(header.ToCharArray()); writer.Write((byte)0); }
        internal void Align(int pass) { if (Pass == pass) while ((stream.Length & 3) != 0) writer.Write((byte)0); }
        internal void Write(int pass, string value) { if (Pass == pass) { if (value != null) writer.Write(value.ToCharArray()); writer.Write((byte)0); } }
        internal void Write(int pass, int value) { if (Pass == pass) writer.Write(value); }
        internal void Write(int pass, uint value) { if (Pass == pass) writer.Write(value); }
        internal void Write(int pass, ushort value) { if (Pass == pass) writer.Write(value); }
        internal void Write(int pass, byte value) { if (Pass == pass) writer.Write(value); }
        internal void Write(int pass, float value) { if (Pass == pass) writer.Write(value); }
        internal void Write(int pass, bool value) => Write(pass, value ? 1 : 0);
        internal void Pointer(int pass, string key) { if (Pass == pass) { fixups.Add(key, Size); writer.Write(0); } }
        internal void Resolve(int pass, string key) => Patch(pass, key, Size);
        internal void Patch(int pass, string key, int value)
        {
            if (Pass != pass) return;
            if (!fixups.TryGetValue(key, out int position)) throw new InvalidDataException("Missing runtime fixup " + key);
            stream.Position = position; writer.Write(value); stream.Position = stream.Length; fixups.Remove(key);
        }
        internal void Rectangle(int pass, double[] twips)
        { Write(pass, (float)(twips[0] / 20)); Write(pass, (float)(twips[2] / 20)); Write(pass, (float)(twips[1] / 20)); Write(pass, (float)(twips[3] / 20)); }
        internal void Matrix(int pass, double[] matrix)
        { for (int i = 0; i < 4; i++) Write(pass, (float)matrix[i]); Write(pass, (float)matrix[4] / 20.0f); Write(pass, (float)matrix[5] / 20.0f); }
        internal void Color(int pass, uint rgba) => Write(pass, (rgba & 0xff000000) | (rgba & 255) << 16 | (rgba & 0xff00) | (rgba >> 16 & 255));
        internal void ByteColor(int pass, ColorEffect effect)
        {
            int[] multipliers = effect?.Multipliers ?? new[] { 256, 256, 256, 256 }, offsets = effect?.Offsets ?? new int[4];
            foreach (int index in new[] { 2, 1, 0, 3 }) Write(pass, unchecked((byte)(multipliers[index] / 256.0f * 255.0f)));
            foreach (int index in new[] { 2, 1, 0, 3 }) Write(pass, unchecked((byte)(offsets[index] / 256.0f * 255.0f)));
        }
        internal void FloatColor(int pass, ColorEffect effect)
        { for (int i = 0; i < 4; i++) Write(pass, (effect?.Multipliers[i] ?? 256) / 256.0f); for (int i = 0; i < 4; i++) Write(pass, (effect?.Offsets[i] ?? 0) / 256.0f); }
        internal void Complete() { writer.Flush(); if (fixups.Count != 0) throw new InvalidDataException("Unresolved runtime asset pointers."); }
    }
}
