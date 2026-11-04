using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    internal static class AdditionalFilesTests
    {
        static int checks;
        static void Check(bool value,string message) {++checks;if(!value)throw new Exception("Additional files: "+message);}
        static void Put(string path,string value) {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value);}
        static void Reject(Action action,string message) {
            try {action();throw new Exception("Expected Additional rejection: "+message);}
            catch(IOException) {++checks;}
        }
        public static int Run(string root)
        {
            checks=0;
            string sdk=Path.Combine(root,"sdk"),mod=Path.Combine(root,"mod"),staging=Path.Combine(root,"staging");
            Directory.CreateDirectory(sdk);Directory.CreateDirectory(mod);Directory.CreateDirectory(staging);
            string compiled=Path.Combine(staging,"current.manifest");Put(compiled,"current compiled");
            var entries=new List<BigArchiveEntry>{new BigArchiveEntry{Name="Data/Mod.manifest",SourcePath=compiled}};
            AdditionalFiles.Merge(sdk,mod,entries,CancellationToken.None);
            Check(entries.Count==1 && !Directory.Exists(Path.Combine(staging,".package-additional")),"Absent Additional trees changed current inventory");
            Put(Path.Combine(sdk,"Additional","Data","MOD.manifest"),"SDK overlay");
            Put(Path.Combine(sdk,"Additional","Config","shared.ini"),"SDK shared");
            Put(Path.Combine(sdk,"Additional","Config","sdk-only.ini"),"SDK only");
            Put(Path.Combine(mod,"Additional","data","mod.manifest"),"MOD overlay");
            Put(Path.Combine(mod,"Additional","config","SHARED.ini"),"MOD shared");
            Put(Path.Combine(mod,"Additional","Shaders","nested","arbitrary.bin"),"arbitrary mod bytes");
            Put(Path.Combine(mod,"Additional","ignored.asset"),"precompiled asset must not enter MakeBig");
            Put(Path.Combine(mod,"Additional","ignored.ASSET"),"same MakeBig exclusion ignoring Windows case");
            var warnings=new List<string>();
            AdditionalFiles.Merge(sdk,mod,entries,CancellationToken.None,warnings.Add);
            Check(warnings.Count==3 && warnings.Any(x=>x.Contains("compiled package member")) && warnings.Any(x=>x.Contains("shared.ini")),"Overwrite warnings must include SDK/MOD and compiled-member conflicts, excluding .asset");
            Check(entries.Count==4,"Additional merge omitted SDK-only input or retained excluded/overwritten members");
            var actual=entries.ToDictionary(entry=>entry.Name.Replace('/','\\').ToLowerInvariant(),entry=>File.ReadAllText(entry.SourcePath));
            Check(actual["data\\mod.manifest"]=="MOD overlay","MOD did not overwrite SDK and current compiled content in old step-8 order");
            Check(actual["config\\shared.ini"]=="MOD shared" && actual["config\\sdk-only.ini"]=="SDK only","SDK and MOD relative-root overlays differ");
            Check(actual["shaders\\nested\\arbitrary.bin"]=="arbitrary mod bytes","Additional relative path changed");
            Check(entries.All(entry=>Paths.Contains(sdk,entry.SourcePath) || Paths.Contains(mod,entry.SourcePath)),"Additional inputs were copied into a redundant snapshot");
            Check(File.ReadAllText(compiled)=="current compiled" && File.ReadAllText(Path.Combine(sdk,"Additional","Data","MOD.manifest"))=="SDK overlay","Overlay modified original stream/SDK input");
            Put(Path.Combine(mod,"Additional","data","mod.manifest"),"later input mutation");
            Check(File.ReadAllText(entries.Single(entry=>entry.Name=="data\\mod.manifest").SourcePath)=="later input mutation","BIG input did not use the current Additional source");
            string big=Path.Combine(staging,"additional.big");BigArchiveWriter.Write(big,entries);
            Check(File.Exists(big),"Merged current Additional inventory did not reach the real BIG writer");
            string cancelled=Path.Combine(root,"cancelled");Directory.CreateDirectory(cancelled);
            var cancelledEntries=new List<BigArchiveEntry>{new BigArchiveEntry{Name="Data/Mod.manifest",SourcePath=compiled}};
            try {AdditionalFiles.Merge(sdk,mod,cancelledEntries,new CancellationToken(true));throw new Exception("Cancelled Additional snapshot succeeded");}
            catch(OperationCanceledException) {Check(cancelledEntries.Count==1 && !Directory.Exists(Path.Combine(cancelled,".package-additional")),"Cancelled Additional changed inventory or copied inputs");}
            string conflictSdk=Path.Combine(root,"conflict-sdk"),conflictMod=Path.Combine(root,"conflict-mod"),conflictStage=Path.Combine(root,"conflict-stage");
            Directory.CreateDirectory(conflictMod);Directory.CreateDirectory(conflictStage);
            Put(Path.Combine(conflictSdk,"Additional","Data","child.ini"),"file under a directory");
            Put(Path.Combine(conflictMod,"Additional","Data"),"file cannot overwrite SDK directory");
            var conflictEntries=new List<BigArchiveEntry>();
            Reject(()=>AdditionalFiles.Merge(conflictSdk,conflictMod,conflictEntries,CancellationToken.None),"directory overwritten by file");
            Check(conflictEntries.Count==0 && !Directory.Exists(Path.Combine(conflictStage,".package-additional")),"Directory collision changed the accepted inventory");
            string fileConflictMod=Path.Combine(root,"file-conflict-mod");Directory.CreateDirectory(Path.Combine(fileConflictMod,"Additional","Data","Mod.manifest"));
            var fileConflictEntries=new List<BigArchiveEntry>{new BigArchiveEntry{Name="Data/Mod.manifest",SourcePath=compiled}};
            Reject(()=>AdditionalFiles.Merge(mod,fileConflictMod,fileConflictEntries,CancellationToken.None),"empty directory over current stream file");
            return checks;
        }
    }
}
