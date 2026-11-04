using BinaryAssetBuilder.UiCompiler;
using BinaryAssetBuilder.UiCompiler.Compilation;
using BinaryAssetBuilder.UiCompiler.Runtime;
using System.Reflection;

internal static class RuntimeFixture
{
    // Tests may inspect final assets. No format encoder/parser is included in
    // the product merely to make these tests work.
    internal static byte[] Payload(CompiledUiAsset asset)
    {
        var data = asset.Buffer.InstanceData; int offset = BitConverter.ToInt32(data, 4), length = BitConverter.ToInt32(data, 8);
        var bytes = new byte[length]; Array.Copy(data, offset, bytes, 0, length); return bytes;
    }
    internal static List<CompiledUiAsset> Actions(ActionProgram program)
    {
        var scene = new SceneModel { WidthTwips = 2000, HeightTwips = 2000, FrameRate = 12 }; var frame = new SceneFrame();
        // Class bootstraps omit End because they normally join other programs.
        frame.Controls.Add(new SceneControl { Kind = ControlKind.Actions, Actions = new ActionProgram(program.Instructions.Concat(new[] { new Instruction(0) })) });
        scene.Frames.Add(frame); return new AssetEncoder(scene).Build("ScriptTest", "ScriptTest.fla");
    }
    internal static void VerifyArchitecture(Action<bool, string> check)
    {
        var assembly = typeof(Plugin).Assembly;
        check(!assembly.GetTypes().Any(t => t.Namespace != null && (t.Namespace.StartsWith("SwfLib", StringComparison.Ordinal) || t.Namespace.StartsWith("eaf2apt", StringComparison.Ordinal))), "No intermediate format readers/writers in product");
        check(typeof(FlaCompiler).GetMethod("Compile", BindingFlags.Instance | BindingFlags.NonPublic).ReturnType == typeof(SceneModel), "FLA compile returns semantic objects");
        check(typeof(ActionScriptCompiler).GetMethod("Compile", BindingFlags.Static | BindingFlags.NonPublic).ReturnType == typeof(ActionProgram), "AS compile returns operations, not encoded AVM1");
        check(!typeof(CompiledUiAsset).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(byte[]))), "Asset writer accepts a direct stream writer, not a serialized intermediate");
    }
}
