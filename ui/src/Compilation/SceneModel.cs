using System;
using System.Collections.Generic;
using System.Linq;

namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    // Compiler objects describe source semantics. They contain no file-format
    // headers, tags, encoded actions, compressed images or serialized payloads.
    internal sealed class SceneModel
    {
        internal int WidthTwips, HeightTwips;
        internal double FrameRate;
        internal readonly SortedDictionary<int, SceneSymbol> Symbols = new SortedDictionary<int, SceneSymbol>();
        internal readonly List<SceneFrame> Frames = new List<SceneFrame>();
        internal readonly List<KeyValuePair<string, int>> Exports = new List<KeyValuePair<string, int>>();
    }
    internal abstract class SceneSymbol { internal int Id; }
    internal sealed class MovieSymbol : SceneSymbol { internal readonly List<SceneFrame> Frames = new List<SceneFrame>(); }
    internal sealed class MeshSymbol : SceneSymbol
    {
        internal double[] Bounds;
        internal readonly List<MeshBatch> Batches = new List<MeshBatch>();
    }
    internal sealed class MeshBatch
    {
        internal Material Material;
        internal readonly List<Triangle> Triangles = new List<Triangle>();
    }
    internal sealed class Material
    {
        internal uint Color;
        internal int ImageId;
        internal bool Repeat;
        internal double[] Matrix;
    }
    internal struct Point2
    {
        internal double X, Y;
        internal Point2(double x, double y) { X = x; Y = y; }
    }
    internal struct Triangle
    {
        internal Point2 A, B, C;
        internal Triangle(Point2 a, Point2 b, Point2 c) { A = a; B = b; C = c; }
    }
    internal sealed class ImageSymbol : SceneSymbol
    {
        internal int Width, Height;
        internal byte[] Pixels; // straight, top-down BGRA pixels, not an image file
        internal bool NoPack;
    }
    internal sealed class FontSymbol : SceneSymbol { internal string Name; }
    internal sealed class TextSymbol : SceneSymbol
    {
        internal double[] Bounds;
        internal int FontId, Alignment, HeightTwips;
        internal uint Color;
        internal bool ReadOnly, Multiline, WordWrap;
        internal string InitialText, VariableName;
    }
    internal sealed class ButtonSymbol : SceneSymbol
    {
        internal bool Menu;
        internal readonly List<ButtonPart> Parts = new List<ButtonPart>();
    }
    internal sealed class ButtonPart
    {
        internal int State, SymbolId, Depth;
        internal double[] Matrix;
        internal ColorEffect Color;
    }
    internal sealed class SceneFrame { internal readonly List<SceneControl> Controls = new List<SceneControl>(); }
    internal enum ControlKind { Actions = 1, Label = 2, Place = 3, Remove = 4, Background = 5, Initialize = 8 }
    internal sealed class SceneControl
    {
        internal ControlKind Kind;
        internal int Depth, SymbolId, ClipDepth;
        internal bool Move, Character;
        internal uint Background;
        internal string Name = "";
        internal double[] Matrix;
        internal ColorEffect Color;
        internal ActionProgram Actions;
        internal IReadOnlyList<EventProgram> Events = Array.Empty<EventProgram>();
    }
    internal sealed class ColorEffect
    {
        internal int[] Multipliers = { 256, 256, 256, 256 };
        internal int[] Offsets = new int[4];
    }
    internal sealed class EventProgram
    {
        internal uint Flags;
        internal byte Key;
        internal ActionProgram Actions;
    }
    internal sealed class ActionLabel { }
    internal sealed class ActionProgram
    {
        internal readonly List<Instruction> Instructions;
        internal ActionProgram(IEnumerable<Instruction> instructions) { Instructions = instructions.ToList(); }
        internal static ActionProgram Join(params ActionProgram[] programs) => new ActionProgram(programs.SelectMany(p => p.Instructions));
    }
    internal sealed class FunctionDefinition
    {
        internal string Name;
        internal IReadOnlyList<string> Parameters;
        internal ActionProgram Body;
    }
    internal sealed class RegisterValue { internal int Index; internal RegisterValue(int index) { Index = index; } }
    internal sealed class UndefinedValue { internal static readonly UndefinedValue Instance = new UndefinedValue(); private UndefinedValue() { } }
    internal sealed class Instruction
    {
        internal int Operation;
        internal object Operand;
        internal Instruction(int operation, object operand = null) { Operation = operation; Operand = operand; }
    }
    internal static class CoordinateMath
    {
        internal static int Round(double value) => checked((int)Math.Floor(value + 0.5));
        internal static double[] Matrix(double[] values)
        {
            var result = (double[])values.Clone();
            for (int i = 0; i < 4; i++) result[i] = checked((int)Math.Round(result[i] * 65536)) / 65536.0;
            result[4] = Round(result[4]); result[5] = Round(result[5]); return result;
        }
    }
}
