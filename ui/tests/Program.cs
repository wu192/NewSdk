using BinaryAssetBuilder.UiCompiler.Fla;
using BinaryAssetBuilder.UiCompiler;
using BinaryAssetBuilder.Core;

int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
void Reject(Action action, string message)
{ bool failed = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException || ex is FileNotFoundException || ex is IOException || ex is NotSupportedException || ex is InvalidOperationException) { failed = true; } Check(failed, message); }
Check(SageHash.Symbol("AptAptData") == 1816258268u, "SAGE type hash");
Check(SageHash.Symbol("AptGeometryData") == 1034587168u, "SAGE geometry type hash");
Check(SageHash.Symbol("Texture") != 0, "Texture type ID");
RuntimeFixture.VerifyArchitecture(Check);
ClassDependencyTests.Run(Check);
GeometryTests.Run(Check);
RuntimeLayoutTests.Run(Check);
void RunScript(string source, string expected)
{
    var machine = new Avm1Machine(); machine.Run(BinaryAssetBuilder.UiCompiler.Compilation.ActionScriptCompiler.Compile(source));
    Check(string.Join(",", machine.Traces) == expected, "AS2 execution: " + source);
}
RunScript("var x=1; x+=2; trace(x); trace(x++); trace(++x);", "3,3,5");
RunScript("var x=0; for(var i=0;i<6;i++){if(i==2)continue; x+=i;} trace(x);", "13");
RunScript("var x=0; while(x<3){x++;} do{x--;}while(x>1); trace(x);", "1");
RunScript("function sum(a,b){return a+b;} trace(sum(4,7));", "11");
RunScript("var a={x:2,y:3}; a.x+=a.y; trace(a.x); var values=[2,4]; trace(values[1]);", "5,4");
RunScript("function f(){var obj={x:1,y:2}; var n=0; for(var key in obj){n+=obj[key];} return n;} trace(f()); trace(typeof key);", "3,undefined");
RunScript("var obj={x:1,y:2,z:3}; var n=0; for(var key in obj){n++; break;} trace(n); trace(7);", "1,7");
RunScript("var n=0; for(var i=0;i<3;i++){switch(i){case 1:continue; default:n++;}} trace(n);", "2");
RunScript("var x=0; false && (x=1); true || (x=2); trace(x); trace(true?7:9);", "0,7");
if (args.Length == 0)
{
    string testRoot = Path.Combine(Path.GetTempPath(), "UiCompiler-" + Guid.NewGuid().ToString("N"));
    try
    {
        string source = Path.Combine(testRoot, "source"); Directory.CreateDirectory(Path.Combine(source, "logic"));
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "flash8-empty.fla");
        File.Copy(fixture, Path.Combine(source, "Main.fla"));
        File.WriteAllText(Path.Combine(source, "Main.as"), "import logic.Controller;\n#include \"setup.as\"\nvar controller:Controller = new Controller(); controller.advance(2); stop();");
        File.WriteAllText(Path.Combine(source, "setup.as"), "var count:Number = 0; for(var i:Number=0;i<3;i++){count+=i;} trace(count);");
        File.WriteAllText(Path.Combine(source, "logic", "Controller.as"), "class logic.Controller extends MovieClip { private var count:Number=0; public static var STEP:Number=2; function Controller(){super();} public function advance(count:Number):Number {this.count += count; return this.count;} public function get value():Number{return count;} public function set value(n:Number):Void{count=n;} public function inspect():Number {var f=function(){var count=42;}; return count;} }");
        var project = new BinaryAssetBuilder.UiCompiler.Compilation.SourceProject(source);
        var cls = project.RequireClass("logic.Controller"); var vm = new Avm1Machine(); vm.Run(cls.Code);
        vm.Run(BinaryAssetBuilder.UiCompiler.Compilation.ActionScriptCompiler.Compile("import logic.Controller; var c=new Controller(); trace(c.advance(2)); c.value=9; trace(c.value); trace(Controller.STEP); trace(c.inspect());"));
        Check(string.Join(",", vm.Traces) == "2,9,2,9", "AS2 members, parameter/nested local scopes, accessors and static fields");
        using var plugin = new Plugin(); plugin.Initialize(null, TargetPlatform.Win32);
        string output = Path.Combine(testRoot, "output", "Mod");
        var first = plugin.CompileFolder(source, output);
        Check(Directory.GetFiles(Path.GetDirectoryName(output)).Length == 4, "Only final four stream files");
        Check(first.InputFiles.Count == 4, "FLA and all AS dependencies");
        File.AppendAllText(Path.Combine(source, "setup.as"), "\ntrace(\"changed\");");
        var second = plugin.CompileFolder(source, output);
        BaselineVerifier.Verify(second, Check);
        Check(!first.Binary.SequenceEqual(second.Binary), "External AS change rebuilds the stream");
        Check(first.Assets.Count == 3, "Script-only movie still emits all three APT assets");
        StreamVerifier.Verify(second, Check);
#if NETFRAMEWORK
        LegacyAbiTests.Verify(source, second, Check);
#endif
        byte[] previous = File.ReadAllBytes(output + ".bin");
        string main = Path.Combine(source, "Main.as"), savedScript = File.ReadAllText(main);
        File.WriteAllText(main, "#include \"missing.as\"");
        Reject(() => plugin.CompileFolder(source, output), "missing AS include fails");
        Check(previous.SequenceEqual(File.ReadAllBytes(output + ".bin")), "failed compile preserves previous final streams");
        File.WriteAllText(main, "#include \"loop.as\""); File.WriteAllText(Path.Combine(source, "loop.as"), "#include \"Main.as\"");
        Reject(() => plugin.CompileFolder(source, output), "include cycle fails");
        File.Delete(Path.Combine(source, "loop.as")); File.WriteAllText(main, savedScript);
        string collision = Path.Combine(testRoot, "collision"); File.WriteAllBytes(collision + ".bin", new byte[] { 1, 2, 3 }); Directory.CreateDirectory(collision + ".relo");
        Reject(() => second.WriteTo(collision), "invalid stream destination fails");
        Check(File.ReadAllBytes(collision + ".bin").SequenceEqual(new byte[] { 1, 2, 3 }), "destination failure preserves existing binary");
        foreach (string rejected in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "rejected"), "*.fla")) Reject(() => FlaDocument.Read(File.ReadAllBytes(rejected)), "unsupported binary FLA revision rejected");
        Reject(() => FlaDocument.Read(new byte[512]), "renamed non-CFB input rejected");
        Console.WriteLine("Folder/AS/include/class/four-stream checks passed.");
    }
    finally { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }
}
string[] samples = args.Length != 0 ? args : Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.fla").OrderBy(p => p, StringComparer.Ordinal).ToArray();
foreach (string file in samples)
{
    FlaDocument document = FlaDocument.Read(File.ReadAllBytes(file));
    Check(document.Contents.Number("revision") == 0x3f || document.Contents.Number("revision") == 0x38, "Flash MX 2004/8 revision");
    Check(document.Scenes.Any(), "Scene list");
    var scene = new BinaryAssetBuilder.UiCompiler.Compilation.FlaCompiler(document, new BinaryAssetBuilder.UiCompiler.Compilation.SourceProject(Path.GetDirectoryName(Path.GetFullPath(file))), file).Compile();
    Check(scene.Frames.Count > 0, "Direct timeline model");
    using var plugin = new Plugin(); plugin.Initialize(null, TargetPlatform.Win32);
    var streams = plugin.CompileMod(file, "UiTest");
    BaselineVerifier.Verify(streams, Check);
    Check(streams.Assets.Count == 3 + scene.Symbols.Values.Count(s => s is BinaryAssetBuilder.UiCompiler.Compilation.MeshSymbol || s is BinaryAssetBuilder.UiCompiler.Compilation.ImageSymbol), "Complete asset group");
    Check(streams.Manifest[1] == 1 && streams.Manifest[2] == 6, "Linked manifest v6");
    Check(BitConverter.ToUInt32(streams.Manifest, 4) == BitConverter.ToUInt32(streams.Binary, 0), "Binary checksum");
    StreamVerifier.Verify(streams, Check);
    Console.WriteLine("Compiled " + Path.GetFileName(file) + ": " + document.Pages.Count + " pages, " + document.Payloads.Count + " media; assets=" + streams.Assets.Count);
}
if (args.Length == 0) BaselineVerifier.Complete(Check);
Console.WriteLine(passed + " checks passed.");
