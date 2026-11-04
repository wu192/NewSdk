using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    // Optional original step 7. Only this package's private copy is modified;
    // compiled/cache four-file streams and SDK reference files remain read-only.
    public static class BasePostProcessor
    {
        static readonly string[] Extensions={".manifest",".bin",".imp",".relo"};
        public static string Prepare(string manifest,string sdkRoot,string workDir,CancellationToken cancellation)
        {return Prepare(manifest,sdkRoot,workDir,cancellation,null);}
        public static string Prepare(string manifest,BuildRequest request,string workDir,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            ReferenceInputs inputs=ResolveReferences(request,true,cancellation);
            try {return Prepare(manifest,request.SdkRoot,workDir,cancellation,inputs);}
            finally {if(inputs.Lease!=null)inputs.Lease.Dispose();}
        }
        static string Prepare(string manifest,string sdkRoot,string workDir,CancellationToken cancellation,ReferenceInputs inputs)
        {
            cancellation.ThrowIfCancellationRequested();
            manifest=Path.GetFullPath(manifest);sdkRoot=Path.GetFullPath(sdkRoot);workDir=Path.GetFullPath(workDir);
            if(!string.Equals(Path.GetExtension(manifest),".manifest",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Neutral asset repair requires a base manifest.");
            string sourceDirectory=Path.GetDirectoryName(manifest),sourceStem=Path.Combine(sourceDirectory,Path.GetFileNameWithoutExtension(manifest));
            if(Paths.Contains(sourceDirectory,workDir) || Paths.Contains(workDir,sourceDirectory) || string.Equals(workDir,sourceDirectory,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Neutral repair must use a separate package workspace.");
            if(Directory.Exists(workDir) || File.Exists(workDir))throw new IOException("Neutral repair workspace already exists.");
            if(inputs==null)inputs=ResolveReferences(sdkRoot);
            string builtMods=inputs.BuiltMods,hashReference=inputs.HashReference,world=inputs.World;
            var references=inputs.Files;
            var guards=new List<IDisposable>();bool created=false;
            try {
                foreach(string path in references.Distinct(StringComparer.OrdinalIgnoreCase)) {RejectFile(path);guards.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));}
                string[] sourceFiles=Extensions.Select(ext=>sourceStem+ext).ToArray();foreach(string file in sourceFiles)RejectFile(file);
                string cdata=Path.Combine(sourceStem,"cdata");string[] custom=new string[0];
                if(Directory.Exists(cdata)) {
                    if(Directory.GetDirectories(cdata).Length!=0)throw new IOException("Neutral repair CDATA must contain only current stream files.");
                    custom=Directory.GetFiles(cdata);foreach(string file in custom) {RejectFile(file);if(!string.Equals(Path.GetExtension(file),".cdata",StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected neutral repair CDATA member.");}
                } else if(File.Exists(cdata))throw new IOException("Neutral repair CDATA root is not a directory.");
                cancellation.ThrowIfCancellationRequested();DiagnosticDirectory.PrepareDirectory(workDir);created=true;
                string targetStem=Path.Combine(workDir,"mod");
                for(int i=0;i<Extensions.Length;i++)Copy(sourceFiles[i],targetStem+Extensions[i],cancellation);
                foreach(string file in custom)Copy(file,Path.Combine(targetStem,"cdata",Path.GetFileName(file)),cancellation);
                string target=targetStem+".manifest";
                HashFix.Apply(target,hashReference,cancellation);
                ModAssetResolver.Apply(target,builtMods,world,inputs.Payload,cancellation);
                cancellation.ThrowIfCancellationRequested();return target;
            }
            catch {
                // Release directory handles before deleting the private copy.
                for(int i=guards.Count-1;i>=0;i--)guards[i].Dispose();guards.Clear();
                if(created && Directory.Exists(workDir))Directory.Delete(workDir,true);
                throw;
            }
            finally {for(int i=guards.Count-1;i>=0;i--)guards[i].Dispose();}
        }
        sealed class ReferenceInputs {public string BuiltMods,HashReference,World;public List<string> Files;public WorldbuilderPayload Payload;public IDisposable Lease;}
        public static void ValidateReferences(string sdkRoot) {ResolveReferences(Path.GetFullPath(sdkRoot));}
        public static void ValidateReferences(BuildRequest request) {ResolveReferences(request,false,CancellationToken.None);}
        static ReferenceInputs ResolveReferences(BuildRequest request,bool extract,CancellationToken cancellation)
        {
            string sdk=Path.GetFullPath(request.SdkRoot);
            if(string.IsNullOrEmpty(request.GameRoot)) {
                try {return ResolveReferences(sdk);}
                catch(BuildFailure error) {if(error.Code!="PACKAGE_NEUTRAL_REFERENCE_MISSING")throw;}
            }
            // Static/Global/Audio belong to ordinary SDK linking and are not
            // supplied by the WorldBuilder archive.
            string built=Path.Combine(sdk,"builtmods");
            var missing=new[]{"Static.manifest","Global.manifest","Audio.manifest"}.Select(name=>Path.Combine(built,name)).Where(path=>!File.Exists(path)).ToArray();
            if(missing.Length!=0)throw new BuildFailure(ExitCode.Environment,"PACKAGE_NEUTRAL_REFERENCE_MISSING","Neutral repair requires SDK reference files: "+string.Join(", ",missing));
            string archive=GameWorldbuilderData.FindArchive(request.GameRoot);
            if(!extract) {GameWorldbuilderData.Validate(archive);return null;}
            IDisposable lease=GameWorldbuilderData.Acquire(request,cancellation);
            try {
                string world=GameWorldbuilderData.PrepareLocked(request,archive,cancellation);
                string hash=Path.Combine(built,"sagexml","Worldbuilder.manifest");
                ReferenceInputs inputs=ResolveReferences(sdk,world,File.Exists(hash)?hash:world);
                inputs.Lease=lease;
                return inputs;
            }
            catch {lease.Dispose();throw;}
        }
        static ReferenceInputs ResolveReferences(string sdkRoot) {
            string builtMods=Path.Combine(sdkRoot,"builtmods"),sage=Path.Combine(builtMods,"sagexml");
            string hashReference=Path.Combine(sage,"Worldbuilder.manifest");
            // Original HashFix uses SageXml, while ModAssetResolver uses the
            // builtmods root (normally populated by its WBData extraction).
            // A complete SageXml quartet is an explicit fallback only when no
            // root quartet member exists. Never mix generations across roots.
            string[] rootWorld=Extensions.Select(ext=>Path.Combine(builtMods,"Worldbuilder"+ext)).ToArray();
            string world= rootWorld.Any(path=>File.Exists(path) || Directory.Exists(path)) || Directory.Exists(Path.Combine(builtMods,"Worldbuilder.bin.parts")) ? rootWorld[0] : Path.Combine(sage,"Worldbuilder.manifest");
            return ResolveReferences(sdkRoot,world,hashReference);
        }
        static ReferenceInputs ResolveReferences(string sdkRoot,string world,string hashReference) {
            string builtMods=Path.Combine(sdkRoot,"builtmods");
            var references=new List<string> {hashReference,Path.Combine(builtMods,"Static.manifest"),Path.Combine(builtMods,"Global.manifest"),Path.Combine(builtMods,"Audio.manifest")};
            references.AddRange(Extensions.Where(ext=>ext!=".bin").Select(ext=>Path.ChangeExtension(world,ext)));
            string bin=Path.ChangeExtension(world,".bin");
            if(!File.Exists(bin) && !File.Exists(Path.Combine(bin+".parts","index.json")))references.Add(bin);
            var missing=references.Distinct(StringComparer.OrdinalIgnoreCase).Where(path=>!File.Exists(path)).ToArray();
            if(missing.Length!=0)throw new BuildFailure(ExitCode.Environment,"PACKAGE_NEUTRAL_REFERENCE_MISSING","Neutral asset repair requires original reference files: "+string.Join(", ",missing)+". Worldbuilder.bin may also be supplied as its indexed .bin.parts blocks.");
            WorldbuilderPayload payload;
            try {payload=new WorldbuilderPayload(bin);}
            catch(FileNotFoundException error) {throw new BuildFailure(ExitCode.Environment,"PACKAGE_NEUTRAL_REFERENCE_MISSING",error.Message);}
            references.AddRange(payload.Files);
            return new ReferenceInputs {BuiltMods=builtMods,HashReference=hashReference,World=world,Files=references,Payload=payload};
        }
        static void RejectFile(string path) {
            FileAttributes attributes=File.GetAttributes(path);
            if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new IOException("Neutral repair inputs must be plain files: "+path);
        }
        static void Copy(string source,string destination,CancellationToken cancellation) {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                byte[] buffer=new byte[64*1024];int count;
                while((count=input.Read(buffer,0,buffer.Length))!=0) {cancellation.ThrowIfCancellationRequested();output.Write(buffer,0,count);}
            }
        }
    }
}
