using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using NewModCompiler.Application;
namespace NewModCompiler.Tests
{
    public static class BasePostProcessorTests
    {
        static int checks;
        static void Check(bool value,string message){checks++;if(!value)throw new Exception("Neutral package copy: "+message);}
        static void Stream(string stem,bool asset,uint instanceHash)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stem));byte[] name=asset?Encoding.ASCII.GetBytes("Test:item\0"):new byte[0],source=asset?Encoding.ASCII.GetBytes("Data/item.xml\0"):new byte[0];
            using(var writer=new BinaryWriter(File.Create(stem+".manifest"))) {
                writer.Write((byte)0);writer.Write((byte)1);writer.Write((ushort)6);
                foreach(uint word in new uint[]{0x11223344,0x12345678,asset?1u:0u,asset?4u:0u,asset?4u:0u,asset?4u:0u,asset?4u:0u,0,0,(uint)name.Length,(uint)source.Length})writer.Write(word);
                if(asset)foreach(uint word in new uint[]{1,2,3,instanceHash,0,0,0,0,4,4,4,0})writer.Write(word);
                writer.Write(name);writer.Write(source);
            }
            foreach(string ext in new[]{".bin",".imp",".relo"})using(var writer=new BinaryWriter(File.Create(stem+ext))) {writer.Write(0x11223344u);if(asset)writer.Write(0x55667788u);}
        }
        static Dictionary<string,byte[]> Snapshot(string path){return Directory.GetFiles(path,"*",SearchOption.AllDirectories).ToDictionary(file=>file,File.ReadAllBytes,StringComparer.OrdinalIgnoreCase);}
        static bool Same(Dictionary<string,byte[]> before,string root){var after=Snapshot(root);return before.Count==after.Count && before.All(pair=>after.ContainsKey(pair.Key) && after[pair.Key].SequenceEqual(pair.Value));}
        static void Pack(string file,int blockSize)
        {
            byte[] bytes=File.ReadAllBytes(file);string directory=file+".parts";Directory.CreateDirectory(directory);
            var blocks=new List<WorldbuilderPayload.Block>();
            for(int offset=0;offset<bytes.Length;offset+=blockSize) {
                string name="block-"+blocks.Count.ToString("D5")+".deflate",part=Path.Combine(directory,name);int count=Math.Min(blockSize,bytes.Length-offset);
                using(var output=File.Create(part))using(var encoder=new DeflateStream(output,CompressionMode.Compress))encoder.Write(bytes,offset,count);
                blocks.Add(new WorldbuilderPayload.Block {file=name,uncompressedBytes=count,compressedBytes=new FileInfo(part).Length});
            }
            File.WriteAllText(Path.Combine(directory,"index.json"),new JavaScriptSerializer().Serialize(new WorldbuilderPayload.Index {schemaVersion=1,uncompressedLength=bytes.Length,blockSize=blockSize,blocks=blocks.ToArray()}));
        }
        static void ReferencingStream(string stem)
        {
            Stream(stem,true,3);string file=stem+".manifest";byte[] original=File.ReadAllBytes(file);
            byte[] data=original.Take(96).Concat(BitConverter.GetBytes(1u)).Concat(BitConverter.GetBytes(2u)).Concat(original.Skip(96)).ToArray();
            Buffer.BlockCopy(BitConverter.GetBytes(8u),0,data,32,4); // one reference
            Buffer.BlockCopy(BitConverter.GetBytes(9u),0,data,48,4); // distinct local type
            Buffer.BlockCopy(BitConverter.GetBytes(10u),0,data,52,4);
            Buffer.BlockCopy(BitConverter.GetBytes(1u),0,data,68,4);
            File.WriteAllBytes(file,data);
        }
        public static int Run(string root)
        {
            checks=0;string sdk=Path.Combine(root,"SDK"),built=Path.Combine(sdk,"builtmods"),sage=Path.Combine(built,"sagexml");
            Stream(Path.Combine(sage,"Worldbuilder"),true,7);foreach(string name in new[]{"Static","Global","Audio"})Stream(Path.Combine(built,name),false,0);
            string source=Path.Combine(root,"accepted","Mod");Stream(source,true,3);string custom=Path.Combine(source,"cdata","00000000.11111111.22222222.33333333.cdata");Directory.CreateDirectory(Path.GetDirectoryName(custom));File.WriteAllText(custom,"current-cdata");
            BasePostProcessor.ValidateReferences(sdk);
            var sdkBefore=Snapshot(sdk);var acceptedBefore=Snapshot(Path.GetDirectoryName(source));
            string work=Path.Combine(root,"package-neutral"),result=BasePostProcessor.Prepare(source+".manifest",sdk,work,CancellationToken.None);
            Check(result==Path.Combine(work,"mod.manifest") && BitConverter.ToUInt32(File.ReadAllBytes(result),60)==7,"HashFix did not repair the private copy from the original SageXml reference");
            Check(File.ReadAllText(Path.Combine(work,"mod","cdata",Path.GetFileName(custom)))=="current-cdata","Accepted CDATA did not survive package repair");
            Check(Same(sdkBefore,sdk) && Same(acceptedBefore,Path.GetDirectoryName(source)),"Repair modified SDK or accepted/cache stream inputs");
            string partial=Path.Combine(built,"Worldbuilder.manifest");File.Copy(Path.Combine(sage,"Worldbuilder.manifest"),partial);
            string missingWork=Path.Combine(root,"partial-root");
            try {BasePostProcessor.Prepare(source+".manifest",sdk,missingWork,CancellationToken.None);throw new Exception("Partial root Worldbuilder silently mixed with SageXml payloads");}
            catch(BuildFailure error){Check(error.Code=="PACKAGE_NEUTRAL_REFERENCE_MISSING" && error.Message.Contains(Path.Combine(built,"Worldbuilder.bin")) && !Directory.Exists(missingWork),"Partial root dependency refusal lost its missing-file diagnostic or mutated workspace");}
            File.Delete(partial);
            string missing=Path.Combine(sage,"Worldbuilder.bin");File.Delete(missing);
            try {BasePostProcessor.Prepare(source+".manifest",sdk,Path.Combine(root,"missing"),CancellationToken.None);throw new Exception("Missing original WB payload accepted");}
            catch(BuildFailure error){Check(error.Code=="PACKAGE_NEUTRAL_REFERENCE_MISSING" && error.Message.Contains(missing),"Missing fallback payload was not clearly reported");}
            Stream(Path.Combine(sage,"Worldbuilder"),true,7);
            string insertion=Path.Combine(root,"insert-source","Mod");ReferencingStream(insertion);
            string raw=BasePostProcessor.Prepare(insertion+".manifest",sdk,Path.Combine(root,"raw-insertion"),CancellationToken.None);
            Pack(missing,5); // the referenced asset's four bytes cross a block boundary
            File.Delete(missing);
            string packed=BasePostProcessor.Prepare(insertion+".manifest",sdk,Path.Combine(root,"packed-insertion"),CancellationToken.None);
            foreach(string ext in new[]{".manifest",".bin",".imp",".relo"})Check(File.ReadAllBytes(Path.ChangeExtension(raw,ext)).SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(packed,ext))),"Packed Worldbuilder changed resolver insertion bytes for "+ext);
            Check(BitConverter.ToUInt32(File.ReadAllBytes(packed),12)==2,"Packed fixture did not actually insert a Worldbuilder asset");
            string part=Path.Combine(missing+".parts","block-00001.deflate");File.Delete(part);
            try {BasePostProcessor.ValidateReferences(sdk);throw new Exception("Missing indexed Worldbuilder block accepted");}
            catch(BuildFailure error){Check(error.Code=="PACKAGE_NEUTRAL_REFERENCE_MISSING" && error.Message.Contains(part),"Missing indexed block was not clearly reported");}
            using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();string cancelled=Path.Combine(root,"cancelled");try{BasePostProcessor.Prepare(source+".manifest",sdk,cancelled,cancellation.Token);throw new Exception("Cancelled repair accepted");}catch(OperationCanceledException){Check(!Directory.Exists(cancelled),"Cancelled repair created staging");}}
            checks+=GameWorldbuilderDataTests.Run(Path.Combine(root,"game-data"));
            return checks;
        }
    }
}
