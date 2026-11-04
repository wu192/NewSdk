using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    // Every build packages the current accepted streams and Additional inputs.
    // BIG/SKUDEF are written once in their final directory, using the writer's
    // existing per-file temporary write and replacement.
    public static class BigPackage
    {
        public static void ValidateRequest(BuildRequest request)
        {
            if(request.FixNeutralAssets && (request.PackageFormat!="big" || request.Content==null || !request.Content.Contains("base"))) throw new BuildFailure(ExitCode.Arguments,"PACKAGE_NEUTRAL_CONTENT","Neutral-asset repair requires BIG packaging with base content selected.");
            if(request.IncludeAdditional && request.PackageFormat!="big") throw new BuildFailure(ExitCode.Arguments,"PACKAGE_ADDITIONAL_FORMAT","Additional files require --package-format big.");
            PackageIdentity.Validate(request);
            if(request.Command=="doctor") return;
            if(request.PackageFormat==null || request.PackageFormat=="loose") return;
            if(request.PackageFormat!="big") throw new BuildFailure(ExitCode.Arguments,"CLI_CAPABILITY","Package format must be loose or big.");
            string name=Path.GetFileName(Path.GetFullPath(request.ModRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
            if(string.IsNullOrWhiteSpace(name) || name.Any(value=>value<32 || value>126 || value=='"' || value=='\r' || value=='\n'))
                throw new BuildFailure(ExitCode.Arguments,"PACKAGE_NAME_UNSUPPORTED","BIG packaging requires an ASCII MOD directory basename for the game skudef.");
        }
        public static IList<Artifact> Create(BuildRequest request,BuildPlan plan,string output,IList<Artifact> artifacts,string buildId,CancellationToken cancellation,Action<string> warning = null)
        {
            ValidateRequest(request);
            if(request.PackageFormat!="big" || plan==null || artifacts==null || artifacts.Count==0) throw new ArgumentException("BIG packaging requires a nonempty current build artifact list.");
            output=Path.GetFullPath(output);
            if(!string.Equals(output,Path.Combine(request.OutputRoot,"published"),StringComparison.OrdinalIgnoreCase)) throw new IOException("BIG delivery must use the published output directory.");
            CacheLayout layout=CacheLayout.ForRequest(request);
            string temporary=layout.BuildTempDirectory(buildId);
            if(Directory.Exists(temporary) || File.Exists(temporary)) throw new IOException("This build's package workspace already exists.");
            bool created=false;
            try {
            layout.PrepareDirectory(temporary); created=true;
            var entries=new List<BigArchiveEntry>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var owners=new Dictionary<BuildTask,HashSet<string>>();
            foreach(var task in plan.Tasks) {
                if(task.Content!="base" && task.Content!="apt" && task.Content!="maps") throw new BuildFailure(ExitCode.Arguments,"PACKAGE_CONTENT_UNSUPPORTED","BIG packaging requires base/apt/maps streams.");
                owners.Add(task,new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            foreach(var artifact in artifacts) {
                cancellation.ThrowIfCancellationRequested();Verify(artifact,layout.Streams);
                BuildTask owner=null;
                foreach(var task in plan.Tasks) {
                    string root=layout.StreamDirectory(task);
                    if(!Paths.Contains(root,artifact.Path)) continue;
                    if(owner!=null) throw new IOException("BIG artifact has ambiguous task ownership.");owner=task;
                }
                if(owner==null) throw new IOException("BIG input is outside this build's stream tasks.");
                string stem=Path.GetFileNameWithoutExtension(owner.InputPath), leaf=Path.GetFileName(artifact.Path), relative;
                string extension=Path.GetExtension(artifact.Path);
                if(new[] {".manifest",".bin",".imp",".relo"}.Contains(extension,StringComparer.OrdinalIgnoreCase)) {
                    if(!string.Equals(leaf,stem+extension,StringComparison.OrdinalIgnoreCase) || !owners[owner].Add(extension)) throw new IOException("BIG stream files must be one exact four-file set per selected task.");
                    relative=stem+extension;
                } else {
                    var directory=new DirectoryInfo(Path.GetDirectoryName(artifact.Path));
                    if(artifact.Kind!="custom-data" || !string.Equals(extension,".cdata",StringComparison.OrdinalIgnoreCase) || !string.Equals(directory.Name,"cdata",StringComparison.OrdinalIgnoreCase) || directory.Parent==null || !string.Equals(directory.Parent.Name,stem,StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected BIG custom-data artifact layout.");
                    relative=stem+"\\cdata\\"+leaf;
                }
                string name="Data\\"+(owner.Content=="base" ? "" : (owner.Content=="apt" ? "AptUI" : "AdditionalMaps")+"\\")+relative;
                if(!names.Add(name)) throw new IOException("Case-insensitive BIG archive path collision.");
                entries.Add(new BigArchiveEntry {Name=name,SourcePath=artifact.Path});
            }
            foreach(var pair in owners) if(pair.Value.Count!=4) throw new IOException("BIG packaging requires all four linked files for every selected stream.");
            IList<BigArchiveEntry> additional;
            using (request.Performance.Measure("package.additional-inputs"))
                additional=request.IncludeAdditional ? AdditionalFiles.Inputs(request.SdkRoot,request.ModRoot,cancellation,warning) : new BigArchiveEntry[0];
            // SDK/defaultscript.cs step 5 runs LoD directly when optional asset
            // repair are disabled. Neutral repair is explicit and modifies only
            // a private package copy, never Core/cache outputs.
            foreach(var task in plan.Tasks) if(task.Content=="base") {
                cancellation.ThrowIfCancellationRequested();
                string manifest=artifacts.Single(value=>string.Equals(Path.GetExtension(value.Path),".manifest",StringComparison.OrdinalIgnoreCase) && Paths.Contains(layout.StreamDirectory(task),value.Path)).Path;
                if(request.FixNeutralAssets) {
                    string originalRoot=Path.GetDirectoryName(manifest);
                    using (request.Performance.Measure("package.neutral"))
                        manifest=BasePostProcessor.Prepare(manifest,request,Path.Combine(temporary,"package-neutral"),cancellation);
                    string preparedStem=Path.Combine(Path.GetDirectoryName(manifest),"mod");
                    foreach(var entry in entries) if(Paths.Contains(originalRoot,entry.SourcePath)) {
                        string extension=Path.GetExtension(entry.SourcePath);
                        entry.SourcePath=string.Equals(extension,".cdata",StringComparison.OrdinalIgnoreCase)
                            ? Path.Combine(preparedStem,"cdata",Path.GetFileName(entry.SourcePath)) : preparedStem+extension.ToLowerInvariant();
                    }
                }
                // Win32 resolves the existing Mod.manifest case-insensitively; the
                // original script passes lowercase mod.manifest, which is also
                // the exact embedded dependency name in both LoD manifests.
                using (request.Performance.Measure("package.lod"))
                foreach(string source in LoDStreamBuilder.Create(Path.Combine(Path.GetDirectoryName(manifest),"mod.manifest"),cancellation,Path.Combine(temporary,"package-lod"))) {
                    string name="Data\\"+Path.GetFileName(source);
                    if(!names.Add(name)) throw new IOException("Case-insensitive LoD archive path collision.");
                    entries.Add(new BigArchiveEntry {Name=name,SourcePath=source});
                }
            }
            // SDK/defaultscript.cs uses echo-dot plus space redirection for each
            // actual APT stream: measured Windows bytes are space + CRLF.
            // MapMetaData has no such rule. Derive these from current tasks only.
            int versionIndex=0;
            foreach(var task in plan.Tasks) if(task.Content=="apt") {
                cancellation.ThrowIfCancellationRequested();
                string directory=Path.Combine(temporary,"package-versions");Directory.CreateDirectory(directory);
                string source=Path.Combine(directory,(versionIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture)+".version");
                using(var file=new FileStream(source,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {file.WriteByte(32);file.WriteByte(13);file.WriteByte(10);}
                string name="Data\\AptUI\\"+Path.GetFileNameWithoutExtension(task.InputPath)+".version";
                if(!names.Add(name)) throw new IOException("Case-insensitive APT version archive path collision.");
                entries.Add(new BigArchiveEntry {Name=name,SourcePath=source});
            }
            string mod=Path.GetFileName(request.ModRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
            string big=Path.Combine(output,mod+"_"+request.ModVersion+".big"),skudef=Path.Combine(output,(request.SkudefName ?? mod)+"_"+request.ModVersion+".skudef");
            using (request.Performance.Measure("package.additional-merge"))
                if(request.IncludeAdditional) AdditionalFiles.Merge(entries,additional,cancellation,warning);
            using (request.Performance.Measure("package.big")) MakeBig.Write(big,entries,cancellation);
            using (request.Performance.Measure("package.skudef")) BigArchiveWriter.WriteSkudef(skudef,Path.GetFileName(big),cancellation,request.GameVersion);
            cancellation.ThrowIfCancellationRequested();
            return new[] {Describe(big,"package.big"),Describe(skudef,"package.skudef")};
            } finally {
                // Only this build owns these generated package inputs. Never
                // sweep other builds' temporary work or reusable Core streams.
                if(created && Directory.Exists(temporary)) {
                    DiagnosticDirectory.CheckDirectory(temporary,layout.CacheRoot);
                    Directory.Delete(temporary,true);
                }
            }
        }
        static void Verify(Artifact artifact,string output)
        {
            if(artifact==null || string.IsNullOrEmpty(artifact.Path) || !Path.IsPathRooted(artifact.Path) || !Paths.Contains(output,artifact.Path)) throw new IOException("BIG input must be an accepted current cache stream file.");
        }
        static Artifact Describe(string path,string kind) { return new Artifact {Path=path,Kind=kind,Size=new FileInfo(path).Length}; }
    }
}
