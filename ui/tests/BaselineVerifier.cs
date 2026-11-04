using BinaryAssetBuilder.UiCompiler;
using System.Security.Cryptography;

internal static class BaselineVerifier
{
    // Captured before removal of the old FLA->SWF->APT path (commit e235cede).
    // These are final AssetBuffer digests, including runtime root and padding.
    private static readonly Dictionary<string, string> Expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy-assets.sha256")).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1]);
    private static readonly HashSet<string> Seen = new HashSet<string>();
    internal static void Complete(Action<bool, string> check) => check(Seen.SetEquals(Expected.Keys), "All 54 legacy final assets verified");
    internal static void Verify(ModStreams streams, Action<bool, string> check)
    {
        using var sha = SHA256.Create();
        foreach (var asset in streams.Assets)
        {
            string key = asset.Source + ":" + asset.TypeName + ":" + asset.Name;
            if (!Expected.TryGetValue(key, out string expected)) continue;
            Seen.Add(key);
            string hash = BitConverter.ToString(sha.ComputeHash(asset.Buffer.InstanceData)).Replace("-", "").ToLowerInvariant();
            string detail = hash != expected && asset.TypeName == "AptGeometryData" && asset.Buffer.InstanceData.Length < 256 ? " "+System.Text.Encoding.UTF8.GetString(RuntimeFixture.Payload(asset)) : "";
            check(hash == expected, "Legacy final asset identity " + key + ": " + hash + detail);
        }
    }
}
