using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using EALA.Hash;

// Run in the independent validation host linked from the production objects.
// These checks cover content invalidation, not production cache enablement.
public static class CompilerCacheContent
{
    static int checks;
    static readonly List<string> tests = new List<string>();
    static void Require(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static uint LegacyHash(byte[] data)
    {
        uint result = (uint)data.Length;
        for (int offset = 0; offset < data.Length; offset += 1048576)
        {
            int count = Math.Min(1048576, data.Length - offset);
            byte[] chunk = new byte[count]; Buffer.BlockCopy(data, offset, chunk, 0, count);
            result = FastHash.GetHashCode(result, chunk, count);
        }
        return result;
    }
    static string Serialize(FileHashItem item)
    {
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true })) item.WriteXml(writer);
        return text.ToString();
    }
    static FileHashItem Deserialize(string text)
    {
        var item = new FileHashItem();
        using (var reader = XmlReader.Create(new StringReader(text))) { reader.MoveToContent(); item.ReadXml(reader); }
        return item;
    }
    static void ReplacePreservingTime(string path, byte[] contents)
    {
        DateTime before = File.GetLastWriteTimeUtc(path);
        long length = new FileInfo(path).Length;
        File.WriteAllBytes(path, contents); File.SetLastWriteTimeUtc(path, before);
        Require(File.GetLastWriteTimeUtc(path) == before && new FileInfo(path).Length == length,
            "Fixture must preserve exact length and timestamp: " + path);
    }
    static void ReplacePreservingTime(string path, string text) { ReplacePreservingTime(path, new UTF8Encoding(false).GetBytes(text)); }
    static void FileCases(string work)
    {
        string path = Path.Combine(work, "input.bin");
        foreach (int length in new[] { 0, 1, 1048576, 1048609 })
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)((i * 17 + 5) % 251);
            File.WriteAllBytes(path, data);
            var item = new FileHashItem(path, "configuration", TargetPlatform.Win32);
            Require(item.IsDirty && item.Exists, "A new existing file must require hashing");
            uint first = item.Hash;
            Require(first == LegacyHash(data), "Content identities must not change legacy FastHash or its one-MiB chunking");
            Require(!item.IsDirty, "A freshly hashed file must be clean");
            item.Reset(); Require(!item.IsDirty && item.Hash == first, "Unmodified contents must remain clean after reset");
            string serialized = Serialize(item);
            var xml = new XmlDocument(); xml.LoadXml(serialized);
            Require(!xml.DocumentElement.HasAttribute("sha256"), "BAB hash records must not persist a second content hash");
            var loaded = Deserialize(serialized);
            Require(!loaded.IsDirty && loaded.Hash == first, "Persisted unchanged FastHash must be reusable");
            if (length != 0)
            {
                data[length - 1] ^= 91;
                ReplacePreservingTime(path, data);
                loaded.Reset(); Require(loaded.IsDirty, "Equal-length equal-time changed bytes must invalidate a persisted item");
                Require(loaded.Hash == LegacyHash(data), "Invalidation must update to the new legacy game hash");
                Require(!loaded.IsDirty, "Changed content must establish a new clean baseline");
                loaded.Reset(); Require(!loaded.IsDirty, "Repeated checks must use the updated baseline");
                DateTime validTime = File.GetLastWriteTimeUtc(path);
                File.Delete(path); loaded.Reset(); Require(!loaded.Exists && loaded.IsDirty && loaded.Hash == 0, "Deleted files must invalidate and hash as missing");
                string missing = Serialize(loaded);
                var missingXml = new XmlDocument(); missingXml.LoadXml(missing);
                Require(!missingXml.DocumentElement.HasAttribute("sha256"), "A missing-file zero hash must not retain the previous content identity");
                File.WriteAllBytes(path, data); File.SetLastWriteTimeUtc(path, validTime); loaded.Reset();
                Require(File.GetLastWriteTimeUtc(path) == validTime, "Recreated fixture must preserve the original exact timestamp");
                Require(loaded.Exists && loaded.IsDirty && loaded.Hash == LegacyHash(data), "Recreated files must be hashed correctly");
                var missingRoundtrip = Deserialize(missing);
                Require(missingRoundtrip.IsDirty && missingRoundtrip.Hash == LegacyHash(data), "A serialized missing-file result must not authorize zero hash reuse after recreation");
            }
        }
        tests.Add("real-FileHashItem-zero-small-and-multi-chunk-FastHash-equality-content-reset-XML-roundtrip-legacy-invalidation-delete-recreate");
    }
    static object HostHashes(string output)
    {
        var type = typeof(NewModCompiler.Application.CoreStreamCompiler).Assembly.GetType("NewModCompiler.Application.FileHashCache", true);
        object hashes = Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { null }, null);
        Settings.Current.FileIdentity = (Func<string, FileHashInfo>)Delegate.CreateDelegate(typeof(Func<string, FileHashInfo>), hashes, type.GetMethod("CoreIdentity", BindingFlags.NonPublic | BindingFlags.Instance));
        Settings.Current.FileHashRead = (Action<long>)Delegate.CreateDelegate(typeof(Action<long>), hashes, type.GetMethod("RecordRead", BindingFlags.NonPublic | BindingFlags.Instance));
        return hashes;
    }
    static void HostFileCases(string work)
    {
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, "input.bin"), output = Path.Combine(work, "receipts");
        byte[] data = new byte[1048609]; data[data.Length - 1] = 17;
        File.WriteAllBytes(path, data);
        Settings.Current = new Settings();
        object hashes = HostHashes(output);
        var item = new FileHashItem(path, "", TargetPlatform.Win32);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(item.Exists && item.IsDirty, "Known dirty input must use host metadata without reading content");
        uint first = item.Hash;
        Require(first == LegacyHash(data), "Host identity path changed multi-chunk FastHash");
        item.Reset();
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(!item.IsDirty && item.Hash == first, "Hot FileHashItem must reuse identity and metadata without opening content");
        string saved = Serialize(item);
        hashes = HostHashes(output);
        var loaded = Deserialize(saved);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(!loaded.IsDirty && loaded.Hash == first, "Cold-process BAB metadata must reuse FileHashItem content identity");
        Thread.Sleep(30); data[data.Length - 1] ^= 91; ReplacePreservingTime(path, data);
        hashes.GetType().GetMethod("SchemaIdentities", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hashes, new object[] { new[] { path } });
        loaded.Reset(); Require(loaded.IsDirty, "Host identity must invalidate a same-size restored-time edit");
        Require(loaded.Hash == LegacyHash(data) && !loaded.IsDirty, "Changed file must calculate the original FastHash once when requested");
        int reads = (int)hashes.GetType().GetProperty("Reads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hashes, null);
        Require(reads == 1, "Changed input should record exactly one FastHash content read");
        Settings.Current = null;
        tests.Add("host-FileHashItem-directory-metadata-locked-hot-and-persisted-metadata-restored-time-invalidation-single-FastHash-read");
    }
    const string Header = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:a='uri:ea.com:eala:asset' targetNamespace='uri:ea.com:eala:asset'>";
    static string Root(string kind)
    {
        return Header + "<xs:include schemaLocation='helpers.xsd'/><xs:simpleType name='" + kind + "'><xs:restriction base='xs:string'/></xs:simpleType></xs:schema>";
    }
    static string Helpers(string item)
    {
        return Header + "<xs:complexType name='BaseAssetType'/><xs:complexType name='BaseInheritableAsset'><xs:complexContent><xs:extension base='a:BaseAssetType'/></xs:complexContent></xs:complexType>" +
            "<xs:simpleType name='AssetReference'><xs:restriction base='xs:string'/></xs:simpleType><xs:simpleType name='WeakReference'><xs:restriction base='xs:string'/></xs:simpleType><xs:simpleType name='FileReference'><xs:restriction base='xs:string'/></xs:simpleType>" +
            "<xs:simpleType name='" + item + "'><xs:restriction base='xs:string'/></xs:simpleType></xs:schema>";
    }
    static void RejectedSchema(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (BinaryAssetBuilderException e) { rejected = e.ErrorCode == ErrorCode.ReadingSchema; }
        Require(rejected, label + " must reject rather than reuse partial schema state");
    }
    static void SchemaCases(string work, bool identityProvider)
    {
        Directory.CreateDirectory(work);
        string root = Path.Combine(work, "root.xsd"), helper = Path.Combine(work, "helpers.xsd");
        File.WriteAllText(root, Root("KindAlpha"), new UTF8Encoding(false));
        File.WriteAllText(helper, Helpers("ItemAlpha"), new UTF8Encoding(false));
        Settings.Current = new Settings { SchemaPath = root, StringHashBinDescriptors = new StringHashBinDescriptor[0] };
        if (identityProvider)
        {
            var type = typeof(NewModCompiler.Application.CoreStreamCompiler).Assembly.GetType("NewModCompiler.Application.FileHashCache", true);
            object hashes = Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { null }, null);
            Settings.Current.FileIdentity = (Func<string, FileHashInfo>)Delegate.CreateDelegate(typeof(Func<string, FileHashInfo>), hashes, type.GetMethod("CoreIdentity", BindingFlags.NonPublic | BindingFlags.Instance));
            var stamp = type.GetMethod("SchemaIdentities", BindingFlags.NonPublic | BindingFlags.Instance);
            Settings.Current.SchemaFileIdentities = (Func<IEnumerable<string>, IDictionary<string, string>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<string>, IDictionary<string, string>>), hashes, stamp);
        }
        var schemas = new SchemaSet(false);
        Require(schemas.GetXmlType("KindAlpha") != null && schemas.GetXmlType("ItemAlpha") != null, "Initial real schema closure must load");
        var original = schemas.Schemas; schemas.ReloadIfChanged(false);
        Require(Object.ReferenceEquals(original, schemas.Schemas), "Unmodified schema closure should be reused");
        if (identityProvider)
        {
            File.SetLastWriteTimeUtc(root, File.GetLastWriteTimeUtc(root).AddSeconds(2));
            schemas.ReloadIfChanged(false);
            Require(Object.ReferenceEquals(original, schemas.Schemas), "Metadata-only schema change must retain the compiled closure");
            using (var rootLock = new FileStream(root, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var includeLock = new FileStream(helper, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                schemas.ReloadIfChanged(false);
            Require(Object.ReferenceEquals(original, schemas.Schemas), "Unchanged identity must not open schema bodies, including after a metadata-only refresh");
        }
        string rootIdentity = identityProvider ? Settings.Current.SchemaFileIdentities(new[] { root })[root] : null;
        if (identityProvider) Thread.Sleep(30); // Match the existing FileIdentity fixture's filesystem clock boundary.
        ReplacePreservingTime(root, Root("KindBravo"));
        if (identityProvider) Require(Settings.Current.SchemaFileIdentities(new[] { root })[root] != rootIdentity, "Equal-time schema edit fixture did not change file identity");
        schemas.ReloadIfChanged(false);
        Require(!Object.ReferenceEquals(original, schemas.Schemas) && schemas.GetXmlType("KindBravo") != null && schemas.GetXmlType("KindAlpha") == null,
            "Root schema changes with equal size and mtime must reload");
        original = schemas.Schemas;
        if (identityProvider) Thread.Sleep(30);
        ReplacePreservingTime(helper, Helpers("ItemBravo")); schemas.ReloadIfChanged(false);
        Require(!Object.ReferenceEquals(original, schemas.Schemas) && schemas.GetXmlType("ItemBravo") != null && schemas.GetXmlType("ItemAlpha") == null,
            "Included schema changes with equal size and mtime must reload");
        string alternate = Path.Combine(work, "alternate.xsd");
        File.WriteAllText(alternate, Root("KindOther"), new UTF8Encoding(false));
        Settings.Current.SchemaPath = alternate; schemas.ReloadIfChanged(false);
        Require(schemas.GetXmlType("KindOther") != null && schemas.GetXmlType("KindBravo") == null, "A changed root path outside the previous closure must reload");
        File.Delete(helper); RejectedSchema(delegate { schemas.ReloadIfChanged(false); }, "Deleted include");
        RejectedSchema(delegate { schemas.ReloadIfChanged(false); }, "Repeated missing include");
        File.WriteAllText(helper, Helpers("ItemThird"), new UTF8Encoding(false)); schemas.ReloadIfChanged(false);
        Require(schemas.GetXmlType("ItemThird") != null && schemas.GetXmlType("KindOther") != null, "Failed include load must recover on the same SchemaSet");
        string valid = Root("KindFixed");
        if (identityProvider) Thread.Sleep(30);
        File.WriteAllText(alternate, valid.Replace("<xs:include", "<xx:include"), new UTF8Encoding(false));
        RejectedSchema(delegate { schemas.ReloadIfChanged(false); }, "Malformed root");
        RejectedSchema(delegate { schemas.ReloadIfChanged(false); }, "Repeated malformed root");
        if (identityProvider) Thread.Sleep(30);
        ReplacePreservingTime(alternate, valid); schemas.ReloadIfChanged(false);
        Require(schemas.GetXmlType("KindFixed") != null, "Same-size same-time repair must retry after failed load");
        original = schemas.Schemas; schemas.ReloadIfChanged(false);
        Require(Object.ReferenceEquals(original, schemas.Schemas), "Recovered unchanged closure must become reusable");
        schemas.ReloadIfChanged(true);
        Require(schemas.AssetDependencies != null, "Dependency-counting request must reload a closure loaded without dependency counts");
        tests.Add("real-SchemaSet-" + (identityProvider ? "file-identity" : "standalone-content") + "-root-and-include-equal-time-invalidation-root-switch-delete-malformed-repeat-repair-dependency-counting");
    }
    public static int Main(string[] args)
    {
        Settings previous = Settings.Current;
        try
        {
            Directory.CreateDirectory(args[0]); FileCases(args[0]); HostFileCases(Path.Combine(args[0], "host-files"));
            SchemaCases(Path.Combine(args[0], "standalone"), false); SchemaCases(Path.Combine(args[0], "identity"), true);
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { succeeded = true, sourceCommit = args[2], runId = args[3], checks, tests }));
            Console.WriteLine("PASS CacheContent: " + checks + " assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Settings.Current = previous; }
    }
}
