using BinaryAssetBuilder.UiCompiler;
using BinaryAssetBuilder.UiCompiler.Compilation;
using BinaryAssetBuilder.UiCompiler.Runtime;
using BinaryAssetBuilder.UiCompiler.Fla;
using System.Security.Cryptography;
using System.Text;

internal static class RuntimeLayoutTests
{
    internal static void Run(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "UiLayout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(directory, "ui"));
        List<CompiledUiAsset> assets;
        try { File.WriteAllText(Path.Combine(directory, "ui", "Panel.as"), LayoutDocument.ClassSource); var scene = new FlaCompiler(LayoutDocument.Create(), new SourceProject(directory), Path.Combine(directory, "Layout.fla")).Compile(); assets = new AssetEncoder(scene).Build("Layout", "Layout.fla"); }
        finally { Directory.Delete(directory, true); }
        var emptyDoc = LayoutDocument.Create();
        var emptyField = new FlaRecord("CPicText");
        emptyField.set("bounds", new List<int> { 0, 800, 0, 400 }); emptyField.set("textFlags", 3);
        var emptyStyle = new FlaRecord("textStyle"); emptyStyle.set("fontFamily", "_sans"); emptyStyle.set("fontSize", 240);
        emptyField.set("defaultStyle", emptyStyle);
        var emptyCompiler = new FlaCompiler(emptyDoc);
        int emptyId = new TextCompiler(emptyCompiler).Compile(emptyField);
        check(emptyCompiler.Model.Symbols[emptyId] is TextSymbol blank && blank.InitialText == "" && blank.HeightTwips == 240, "Empty dynamic text retains its FLA default style");
        int I(byte[] bytes, int at) => BitConverter.ToInt32(bytes, at);
        string Str(byte[] bytes, int at) { int end = at; while (bytes[end] != 0) end++; return Encoding.UTF8.GetString(bytes, at, end - at); }
        var runtime = RuntimeFixture.Payload(assets[0]); var constants = RuntimeFixture.Payload(assets[1]); int main = I(constants, 20), table = I(runtime, main + 24), count = I(runtime, main + 20);
        check(I(runtime, main) == 9 && I(runtime, main + 8) == 2, "Root has two frames");
        check(I(runtime, main + 48) == 1, "Linked class export count"); int exports = I(runtime, main + 52); check(Str(runtime, I(runtime, exports)) == "PanelExport", "Export name pointer");
        bool sawButton = false, sawText = false, sawBitmap = false;
        for (int i = 1; i < count; i++)
        {
            int at = I(runtime, table + i * 4); if (at == 0) continue; check(I(runtime, at + 4) == 0x09876543, "Symbol runtime marker");
            if (I(runtime, at) == 4) { sawButton = true; check(I(runtime, at + 28) == 2 && I(runtime, at + 32) == 4, "Button hit triangles and vertices"); check(I(runtime, at + 44) == 4, "Button four state records"); int parts = I(runtime, at + 48); check(I(runtime, parts) == 1 && I(runtime, parts + 68 * 3) == 8, "Button state masks"); }
            if (I(runtime, at) == 2) { sawText = true; check(I(runtime, at + 28) == 2, "Dynamic text alignment"); check(Str(runtime, I(runtime, at + 52)) == "Ready 中文" && Str(runtime, I(runtime, at + 56)) == "status", "Dynamic text UTF-8 and variable pointers"); }
            if (I(runtime, at) == 7) sawBitmap = true;
        }
        check(sawButton && sawText && sawBitmap, "Button, font/text and bitmap paths present");
        int frames = I(runtime, main + 12), controls = I(runtime, frames + 4), controlCount = I(runtime, frames); bool events = false, initializer = false;
        for (int c = 0; c < controlCount; c++)
        {
            int at = I(runtime, controls + c * 4);
            if (I(runtime, at) == 8) { initializer = true; check(I(runtime, at + 8) > 0, "Linked class initialization code pointer"); }
            if (I(runtime, at) != 3 || (I(runtime, at + 4) & 128) == 0) continue;
            int block = I(runtime, at + 60), n = I(runtime, block), records = I(runtime, block + 4); check(n >= 1, "Instance event count");
            for (int e = 0; e < n; e++) check(I(runtime, records + e * 12 + 8) > 0, "Instance event code pointer"); events = true;
        }
        check(events && initializer, "Event and initializer layouts exercised");
        var texture = RuntimeFixture.Payload(assets.Single(a => a.TypeName == "Texture")); check(I(texture, 0) == 0x20534444 && I(texture, 12) == 4 && I(texture, 16) == 4 && texture.Length == 192, "Direct final texture header and dimensions");
        check(texture[128] == 0 && texture[130] == 255 && texture[131] == 255 && texture[135] == 128, "Final bitmap BGRA/alpha");
        check(texture.Skip(128 + 3 * 4).Take(4).SequenceEqual(texture.Skip(128 + 4).Take(4)), "No-pack texture edge extension");
        using var sha = SHA256.Create(); var goldens = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy-layout-assets.sha256")).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1]);
        foreach (var asset in assets) { string key = asset.TypeName + ":" + asset.Name, hash = BitConverter.ToString(sha.ComputeHash(asset.Buffer.InstanceData)).Replace("-", "").ToLowerInvariant(); check(goldens.TryGetValue(key, out string expected) && hash == expected, "Legacy layout asset identity " + key + ": " + hash); }
    }
}
