using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    internal static class PatchPackageTests
    {
        static int checks;
        static void Check(bool value,string message) { checks++; if(!value) throw new Exception("Patch package: " + message); }
        static void Reject(Action action,string code)
        {
            try { action(); }
            catch(BuildFailure error) { Check(error.Code==code,"Expected " + code + ", received " + error.Code); return; }
            throw new Exception("Patch package accepted invalid input: " + code);
        }
        static void Put(string path,byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,bytes); }
        static byte[] Manifest()
        {
            byte[] bytes=new byte[48];bytes[1]=1;bytes[2]=6;
            bytes[4]=17;bytes[5]=34;bytes[6]=51;bytes[7]=68;return bytes;
        }
        static uint BigEndian(BinaryReader reader)
        {
            byte[] bytes=reader.ReadBytes(4);return ((uint)bytes[0]<<24)|((uint)bytes[1]<<16)|((uint)bytes[2]<<8)|bytes[3];
        }
        static Dictionary<string,byte[]> ReadBig(string path)
        {
            using(var reader=new BinaryReader(File.OpenRead(path))) {
                Check(Encoding.ASCII.GetString(reader.ReadBytes(4))=="BIG4","Archive header");
                reader.ReadUInt32();uint count=BigEndian(reader);BigEndian(reader);
                var result=new Dictionary<string,byte[]>(StringComparer.Ordinal);
                for(uint i=0;i<count;i++) {
                    uint offset=BigEndian(reader),length=BigEndian(reader);
                    var name=new List<byte>();byte current;while((current=reader.ReadByte())!=0)name.Add(current);
                    long position=reader.BaseStream.Position;reader.BaseStream.Position=offset;
                    result.Add(Encoding.ASCII.GetString(name.ToArray()),reader.ReadBytes((int)length));
                    reader.BaseStream.Position=position;
                }
                return result;
            }
        }
        static Dictionary<string,byte[]> Build(BuildRequest request,BuildPlan plan,IList<Artifact> artifacts)
        {
            string output=Path.Combine(request.OutputRoot,"published");
            var result=BigPackage.Create(request,plan,output,artifacts,Guid.NewGuid().ToString("N"),CancellationToken.None);
            return ReadBig(result.Single(value=>value.Kind=="package.big").Path);
        }
        public static int Run(string root)
        {
            checks=0;
            string sdk=Path.Combine(root,"sdk"),mod=Path.Combine(root,"Patch"),output=Path.Combine(root,"out");
            Directory.CreateDirectory(sdk);Directory.CreateDirectory(mod);
            Put(Path.Combine(mod,"Data","Mod.xml"),Encoding.ASCII.GetBytes("<AssetDeclaration/>"));
            string baseStem=Path.Combine(root,"base","mod");
            foreach(string extension in new[]{".manifest",".bin",".imp",".relo"})
                Put(baseStem+extension,extension==".manifest" ? Manifest() : new byte[]{17,34,51,68});
            var request=new BuildRequest {Command="build",ModRoot=mod,SdkRoot=sdk,OutputRoot=output,Content=new[]{"base"},PackageFormat="big",BasePatchStream=baseStem+".manifest",PatchConfig="Base_1.0.skudef",PatchVersion="1.01"};
            var task=new BuildTask {Id="base/Mod",Content="base",InputPath=Path.Combine(mod,"Data","Mod.xml"),OutputName="base/Mod",Dependencies=new string[0]};
            var plan=new BuildPlan {Tasks=new[]{task},Skipped=new SkippedContent[0]};
            string stem=Path.Combine(CacheLayout.ForRequest(request).StreamDirectory(task),"patch-streams","mods","Patch","Data","Mod");
            var artifacts=new List<Artifact>();
            foreach(string extension in new[]{".manifest",".bin",".imp",".relo"}) {
                Put(stem+extension,extension==".manifest" ? Manifest() : new byte[]{17,34,51,68});
                artifacts.Add(new Artifact {Path=stem+extension,Kind="stream"+extension,Size=new FileInfo(stem+extension).Length});
            }
            var files=Build(request,plan,artifacts);
            Check(files.Count==15,"Expected four base files, eight LoD files and three version selectors");
            Check(files["data\\mod_1.01.manifest"].SequenceEqual(Manifest()),"Patch packaging changed the linked manifest");
            foreach(string lod in new[]{"_m","_l"}) {
                byte[] manifest=files["data\\mod"+lod+"_1.01.manifest"];
                Check(Encoding.ASCII.GetString(manifest,48,manifest.Length-48)=="\x02mod_1.01.manifest\0","LoD must refer to the versioned base leaf");
            }
            foreach(string name in new[]{"mod","mod_m","mod_l"})
                Check(files["data\\"+name+".version"].SequenceEqual(Encoding.ASCII.GetBytes("_1.01")),"Version selectors must have no BOM or newline");
            string skudef=Path.Combine(output,"published","Patch_1.0.skudef");
            Check(File.ReadAllText(skudef)=="mod-game 1.12\r\nadd-big Patch_1.0.big\r\nadd-config Base_1.0.skudef\r\n","Patch SKUDEF must load its BIG before the base configuration");
            Check(File.ReadAllBytes(stem+".manifest").SequenceEqual(Manifest()),"Packaging changed cached Core output");
            request.PatchVersion="2.5";files=Build(request,plan,artifacts);
            Check(files.ContainsKey("data\\mod_2.5.bin") && !files.ContainsKey("data\\mod_1.01.bin"),"Stream version must remain independent of package version");
            request.PatchVersion="none";Reject(()=>PatchStreams.Validate(request),"PATCH_BASE_SELF_REFERENCE");
            string sameStem=Path.Combine(root,"base","mod_1.01"),wbStem=Path.Combine(root,"base","worldbuilder.11");
            foreach(string extension in new[]{".manifest",".bin",".imp",".relo"}) {
                File.Copy(baseStem+extension,sameStem+extension);
                File.Copy(baseStem+extension,wbStem+extension);
            }
            request.PatchVersion="1.01";request.BasePatchStream=sameStem+".manifest";
            Reject(()=>PatchStreams.Validate(request),"PATCH_BASE_SELF_REFERENCE");
            request.PatchVersion="none";request.BasePatchStream=wbStem+".manifest";request.PatchConfig=null;files=Build(request,plan,artifacts);
            Check(files.Count==12 && files.ContainsKey("data\\mod.manifest") && !files.Keys.Any(value=>value.EndsWith(".version",StringComparison.Ordinal)),"WorldBuilder patch must allow unversioned streams");
            Check(!File.ReadAllText(skudef).Contains("add-config"),"Optional base SKUDEF leaked from a previous build");
            request.BasePatchStream=baseStem+".manifest";
            request.PatchVersion="../1";Reject(()=>PatchStreams.Validate(request),"PATCH_VERSION_INVALID");request.PatchVersion="1.01";
            request.PatchConfig="../Base.skudef";Reject(()=>PatchStreams.Validate(request),"PATCH_CONFIG_INVALID");
            request.PatchConfig="Patch_1.0.skudef";Reject(()=>PatchStreams.Validate(request),"PATCH_CONFIG_SELF");request.PatchConfig=null;
            File.Delete(baseStem+".relo");Reject(()=>PatchStreams.Validate(request),"PATCH_BASE_MISSING");Put(baseStem+".relo",new byte[4]);
            Put(baseStem+".manifest",new byte[48]);Reject(()=>PatchStreams.Validate(request),"PATCH_BASE_INVALID");Put(baseStem+".manifest",Manifest());
            request.PackageFormat="loose";Reject(()=>PatchStreams.Validate(request),"PATCH_PACKAGE_FORMAT");request.PackageFormat="big";
            request.FixNeutralAssets=true;Reject(()=>PatchStreams.Validate(request),"PATCH_NEUTRAL_UNSUPPORTED");request.FixNeutralAssets=false;
            request.Content=new[]{"apt"};Reject(()=>PatchStreams.Validate(request),"PATCH_BASE_CONTENT");request.Content=new[]{"base"};
            request.BasePatchStream=null;request.PatchConfig="Base.skudef";Reject(()=>PatchStreams.Validate(request),"PATCH_BASE_REQUIRED");
            return checks;
        }
    }
}
