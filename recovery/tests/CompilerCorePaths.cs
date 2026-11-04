using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using NewModCompiler.Application;

// Loaded by the production-object validation EXE. These checks exercise the
// actual Core methods and both consumers of data-root-relative output paths.
public static class CompilerCorePaths
{
    static int checks;
    static readonly List<string> tests = new List<string>();
    static void Require(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (BinaryAssetBuilderException error) { Require(error.ErrorCode == ErrorCode.IllegalPath, name + " returned the wrong error code"); rejected = true; }
        Require(rejected, name + " was not rejected as an illegal path");
    }
    static void Equal(string actual, string expected, string name)
    {
        Require(String.Equals(actual, expected, StringComparison.Ordinal), name + ": expected [" + expected + "], got [" + actual + "]");
    }
    static void RootCase(string root, string file, string relative, bool inspectDocument)
    {
        Settings.Current.DataPaths = new[] { root };
        string matched = FileNameResolver.GetDataRoot(file);
        Require(!String.IsNullOrEmpty(matched), "Data root did not match " + root + " / " + file);
        Equal(FileNameResolver.GetSafeRelativePath(matched, file), relative, "relative path");
        string stem = Path.Combine(Path.GetDirectoryName(relative), Path.GetFileNameWithoutExtension(relative));
        var processor = (DocumentProcessor)FormatterServices.GetUninitializedObject(typeof(DocumentProcessor));
        Equal(processor.GetExpectedOutputManifest(file), stem + ".manifest", "expected output manifest");
        if (inspectDocument)
        {
            Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
            ConstructorInfo constructor = stateType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(DocumentProcessor), typeof(FileHashItem), typeof(string), typeof(string) }, null);
            object state = constructor.Invoke(new object[] { null, new FileHashItem(file, "", TargetPlatform.Win32), "original-logical", "" });
            Equal((string)stateType.GetField("SourcePathFromRoot").GetValue(state), stem, "document output stem");
            Equal((string)stateType.GetField("LogicalSourcePath").GetValue(state), "DATA:" + relative.Replace('\\', '/'), "document logical name");
        }
    }
    static void Run(string work)
    {
        Require(Path.DirectorySeparatorChar == '\\', "This is the Windows path matrix");
        Require(typeof(FileNameResolver).Assembly == Assembly.GetEntryAssembly(), "Core paths must come from the validation entry EXE");
        Settings old = Settings.Current;
        Settings.Current = new Settings { DataPaths = new string[0], ArtPaths = new[] { work }, Postfix = "" };
        try
        {
            string unique = "__core_path_" + Guid.NewGuid().ToString("N");
            string drive = Path.GetPathRoot(Path.GetFullPath(work));
            string root = Path.Combine(drive, unique, "Data");
            string file = Path.Combine(root, "Sub", "文件 name.xml");
            RootCase(root, file, "Sub\\文件 name.xml", true);
            RootCase(root + "\\", file, "Sub\\文件 name.xml", true);
            RootCase(root.Replace('\\', '/') + "/", file.Replace('\\', '/'), "Sub\\文件 name.xml", true);
            RootCase(drive, Path.Combine(drive, unique, "Mods", "mod.xml"), unique + "\\Mods\\mod.xml", true);
            RootCase(drive.ToLowerInvariant(), Path.Combine(drive, unique, "Mods", "mod.xml"), unique + "\\Mods\\mod.xml", true);
            tests.Add("drive-root-trailing-separators-forward-slashes-unicode-two-real-consumers");

            // No File.Exists calls against the fake UNC server: these methods
            // are lexical and the real document constructor is checked above.
            RootCase(@"\\core-path-test.invalid\share\", @"\\core-path-test.invalid\share\Sub\mod.xml", @"Sub\mod.xml", false);
            RootCase(@"\\core-path-test.invalid\share\Data\", @"\\core-path-test.invalid\share\Data\Sub\mod.xml", @"Sub\mod.xml", false);
            tests.Add("unc-share-and-directory-roots");

            Settings.Current.DataPaths = new[] { root };
            string sibling = Path.Combine(drive, unique, "Data2", "X.xml");
            Equal(FileNameResolver.GetDataRoot(sibling), "", "same-prefix sibling root");
            var processor = (DocumentProcessor)FormatterServices.GetUninitializedObject(typeof(DocumentProcessor));
            Equal(processor.GetExpectedOutputManifest(sibling), "X.manifest", "sibling output remains a relative basename");
            Equal(FileNameResolver.GetDataRoot(root), "", "directory itself is not an input file below root");
            Settings.Current.DataPaths = new[] { "*", root + "\\Sub", root };
            Equal(FileNameResolver.GetDataRoot(file), root + "\\Sub", "configured root search order and wildcard skip");
            tests.Add("directory-boundaries-and-configured-root-order");

            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, sibling); }, "same-prefix sibling");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, root); }, "root itself");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, Path.Combine(root, "..", "outside.xml")); }, "parent traversal");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, "Z:\\outside.xml"); }, "different root");
            Equal(FileNameResolver.GetSafeRelativePath(root, Path.Combine(root, "Sub", "..", "mod.xml")), "mod.xml", "normalized safe traversal");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(null, file); }, "null data root");
            Reject(delegate { FileNameResolver.GetSafeRelativePath("", file); }, "empty data root");
            Reject(delegate { FileNameResolver.GetSafeRelativePath("Data", file); }, "relative data root");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, null); }, "null source path");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, ""); }, "empty source path");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, "mod.xml"); }, "relative source path");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, @"C:mod.xml"); }, "drive-relative source path");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, @"\mod.xml"); }, "root-relative source path");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(@"\\?\C:\Data", file); }, "device data root");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, @"\\.\C:\Data\mod.xml"); }, "device source path");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root, file + ":stream"); }, "alternate source data stream");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(root + ":stream", file); }, "alternate root data stream");
            Reject(delegate { FileNameResolver.GetSafeRelativePath(@"\\server-only", file); }, "incomplete UNC data root");
            tests.Add("safe-relative-output-rejections-and-canonicalization");

            Directory.CreateDirectory(work);
            string shortArt = Path.Combine(work, "a");
            File.WriteAllText(shortArt, "one-character art input");
            Reject(delegate { FileNameResolver.ResolvePath(work, "art:"); }, "empty art alias");
            Reject(delegate { FileNameResolver.ResolvePath(work, "art:a"); }, "short non-wildcard art alias");
            Settings.Current.ArtPaths = new[] { "*" };
            Reject(delegate { FileNameResolver.ResolvePath(work, "art:"); }, "empty wildcard art alias");
            Equal(FileNameResolver.ResolvePath(work, "art:a"), shortArt, "wildcard resolves an existing one-character file");
            Equal(FileNameResolver.ResolvePath(work, "art:b"), Path.Combine(work, "b"), "missing wildcard-only short name keeps last probe path");
            Settings.Current.ArtPaths = new[] { "*", work };
            Equal(FileNameResolver.ResolvePath(work, "art:a"), shortArt, "successful wildcard stops before non-wildcard bucket search");
            Reject(delegate { FileNameResolver.ResolvePath(work, "art:b"); }, "missing wildcard short name reaches non-wildcard bucket");
            Settings.Current.ArtPaths = new[] { work, "*" };
            Reject(delegate { FileNameResolver.ResolvePath(work, "art:a"); }, "non-wildcard first preserves search order even with a later existing wildcard file");
            Reject(delegate { FileNameResolver.ResolvePath(work, "art:b"); }, "non-wildcard first rejects a missing short name");
            tests.Add("one-character-art-wildcard-existing-missing-and-ordered-mixed-searches");
            Settings.Current.ArtPaths = new[] { work };
            Equal(FileNameResolver.ResolvePath(work, "art:ab"), Path.Combine(work, "ab", "ab"), "two-character art filename keeps legacy prefix search");
            Equal(FileNameResolver.ResolvePath(work, "art:sub/a"), Path.Combine(work, "sub", "a"), "explicit art subdirectory keeps legacy search");
            tests.Add("art-alias-explicit-rejection-and-existing-search-rules");
        }
        finally { Settings.Current = old; }
    }
    public static int Main(string[] args)
    {
        try
        {
            Run(args[0]);
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { succeeded = true, sourceCommit = args[2], runId = args[3], checks, tests }));
            Console.WriteLine("PASS CorePaths: " + checks + " assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
