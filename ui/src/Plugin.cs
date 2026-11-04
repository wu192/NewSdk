using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BinaryAssetBuilder.Core;
using BinaryAssetBuilder.UiCompiler.Fla;
using BinaryAssetBuilder.UiCompiler.Compilation;
using BinaryAssetBuilder.UiCompiler.Runtime;

namespace BinaryAssetBuilder.UiCompiler
{
    /// <summary>The host collects the whole sibling group before writing its manifest.</summary>
    public interface IUiAssetCollector
    {
        void Collect(UiAssetGroup assets);
    }
    public sealed class UiCompilerConfiguration
    {
        public IUiAssetCollector Collector { get; set; }
    }
    public interface IUiFileCompiler
    {
        UiAssetGroup CompileUiFile(InstanceDeclaration declaration, TargetPlatform platform);
    }
    public interface IUiFolderCompiler
    {
        ModStreams CompileFolder(string sourceDirectory, string outputBase);
    }
    /// <summary>AudioCompiler-compatible lifecycle plus a multi-asset FLA build entry.</summary>
    public class Plugin : IAssetBuilderPlugin, IUiFileCompiler, IUiFolderCompiler, IDisposable
    {
        private bool initialized, disposed;
        private UiCompilerConfiguration configuration;
        public virtual void Initialize(object configSection, TargetPlatform platform)
        {
            Alive(); RequireWin32(platform);
            if (configSection != null && !(configSection is UiCompilerConfiguration)) throw new ArgumentException("Expected UiCompilerConfiguration.", nameof(configSection));
            configuration = (UiCompilerConfiguration)configSection; initialized = true;
        }
        public virtual void ReInitialize(object configSection, TargetPlatform platform) => Initialize(configSection, platform);
        public virtual uint GetAllTypesHash() { Alive(); return UiCatalog.AllTypesHash; }
        public virtual uint GetVersionNumber() { Alive(); return 0x00010001; }
        public virtual ExtendedTypeInformation GetExtendedTypeInformation(uint typeId) { Alive(); return UiCatalog.Find(typeId); }
        public virtual AssetBuffer ProcessInstance(InstanceDeclaration declaration)
        {
            Ready(); if (declaration == null) throw new ArgumentNullException(nameof(declaration));
            if (declaration.Handle.TypeId != UiCatalog.Find("AptAptData").TypeId) throw new NotSupportedException("Submit an AptAptData declaration referencing the source FLA, or use CompileMod.");
            if (configuration?.Collector == null) throw new InvalidOperationException("FLA produces multiple assets. Configure IUiAssetCollector, use CompileUiFile in the host's expansion stage, or call CompileMod to write the final four streams.");
            var assets = CompileUiFile(declaration, TargetPlatform.Win32);
            // One AssetBuffer cannot contain sibling assets. The host explicitly
            // collects their real type IDs and buffers; no synthetic runtime type.
            configuration.Collector.Collect(new UiAssetGroup(assets.Skip(1).ToArray(), assets.InputFiles)); return assets[0].Buffer;
        }
        public UiAssetGroup CompileUiFile(InstanceDeclaration declaration, TargetPlatform platform)
        {
            Ready(); RequireWin32(platform); if (declaration == null) throw new ArgumentNullException(nameof(declaration));
            if (declaration.Handle.TypeId != UiCatalog.Find("AptAptData").TypeId) throw new NotSupportedException("FLA source declaration must be AptAptData.");
            string name = declaration.XmlNode.Attributes["id"]?.Value ?? throw new InvalidDataException("Missing UI asset id.");
            if (!name.EndsWith("_apt", StringComparison.Ordinal)) throw new InvalidDataException("FLA declaration id must end in _apt (for example MainMenu_apt).");
            string file = declaration.XmlNode.Attributes["File"]?.Value ?? throw new InvalidDataException("Missing source FLA File.");
            return CompileAssets(file, name.Substring(0, name.Length - 4));
        }
        /// <summary>Compiles directly to the final in-memory Mod streams.</summary>
        public ModStreams CompileMod(string flaPath, string movieName)
        { Ready(); return new ModStreams(CompileAssets(flaPath, movieName)); }
        /// <summary>Compiles and writes only the final manifest/bin/relo/imp files.</summary>
        public ModStreams CompileMod(string flaPath, string movieName, string outputBase)
        { var streams = CompileMod(flaPath, movieName); streams.WriteTo(outputBase); return streams; }
        /// <summary>Builds the FLA movies and their AS sources in one source folder.</summary>
        public ModStreams CompileFolder(string sourceDirectory, string outputBase)
        {
            Ready(); string directory = Path.GetFullPath(sourceDirectory);
            var files = Directory.GetFiles(directory).Where(f => Path.GetExtension(f).Equals(".fla", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No Flash 8 FLA found in " + directory);
            var assets = new List<CompiledUiAsset>(); var inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files) { var group = CompileAssets(file, Path.GetFileNameWithoutExtension(file)); assets.AddRange(group); inputs.UnionWith(group.InputFiles); }
            var streams = new ModStreams(new UiAssetGroup(assets, inputs.OrderBy(p => p, StringComparer.Ordinal).ToArray())); streams.WriteTo(outputBase); return streams;
        }
        private UiAssetGroup CompileAssets(string path, string name)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (Directory.Exists(path)) { string[] files = Directory.GetFiles(path).Where(p => Path.GetExtension(p).Equals(".fla", StringComparison.OrdinalIgnoreCase)).ToArray(); if (files.Length != 1) throw new InvalidDataException("A single movie declaration requires one FLA in the folder. Use CompileFolder for multiple movies."); path = files[0]; }
            if (!Path.GetExtension(path).Equals(".fla", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The UI plugin accepts binary Flash 8 .fla source files.");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A movie name is required.", nameof(name)); SageHash.Symbol(name);
            string source = Path.GetFileName(path); if (source.IndexOf('\0') >= 0) throw new ArgumentException("Invalid source path.", nameof(path));
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                var document = FlaDocument.Read(File.ReadAllBytes(path)); var project = new SourceProject(Path.GetDirectoryName(Path.GetFullPath(path)));
                var scene = new FlaCompiler(document, project, path).Compile();
                var assets = new AssetEncoder(scene).Build(name, source);
                return new UiAssetGroup(assets, new[] { Path.GetFullPath(path) }.Concat(project.Inputs).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToArray());
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException || ex is EndOfStreamException)
            { throw new InvalidDataException("Could not compile Flash 8 FLA '" + path + "': " + ex.Message, ex); }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture; }
        }
        private void Ready() { Alive(); if (!initialized) throw new InvalidOperationException("Call Initialize before compiling UI assets."); }
        private void Alive() { if (disposed) throw new ObjectDisposedException(nameof(Plugin)); }
        private static void RequireWin32(TargetPlatform platform) { if (platform != TargetPlatform.Win32) throw new PlatformNotSupportedException("RA3 UI streams target Win32."); }
        public void Dispose() { disposed = true; configuration = null; }
    }
}
