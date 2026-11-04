using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using NewModCompiler.Application;

// Real sequential IStreamCompiler fixture. Cold comparisons use new backend
// instances in this same process; this is not a cold-process or SDK/G3 receipt.
public static class CompilerBackendLifecycle
{
    static readonly List<string> Tests = new List<string>();
    static readonly List<object> BaselineCases = new List<object>();
    static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static string work, sdk, probe;
    static int checks, traceCalls;
    static void Require(bool value, string message) { ++checks; if (!value) throw new Exception(message); }
    static object Field(Type type, string name) { return type.GetField(name, PrivateStatic).GetValue(null); }
    sealed class Ambient
    {
        readonly Settings settings = Settings.Current;
        readonly object names = Field(typeof(HashProvider), "_SchemaNameToBinName");
        readonly object bins = Field(typeof(HashProvider), "_StringHashBins");
        readonly string directory = HashProvider.GetOutputDirectory(), file = HashProvider.StringHashesFile;
        readonly TraceWriteHandler writer = Tracer.TraceWrite;
        readonly object mask = Field(typeof(Tracer), "_TraceMask");
        public void Unchanged(string label)
        {
            Require(Object.ReferenceEquals(Settings.Current, settings), label + ": Settings.Current leaked");
            Require(Object.ReferenceEquals(Field(typeof(HashProvider), "_SchemaNameToBinName"), names), label + ": schema hash map leaked");
            Require(Object.ReferenceEquals(Field(typeof(HashProvider), "_StringHashBins"), bins), label + ": string hash bins leaked");
            Require(HashProvider.GetOutputDirectory() == directory && HashProvider.StringHashesFile == file, label + ": hash path leaked");
            Require(Object.ReferenceEquals(Tracer.TraceWrite, writer) && Object.Equals(Field(typeof(Tracer), "_TraceMask"), mask), label + ": trace writer/mask leaked");
        }
    }
    static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); return path;
    }
    static string Asset(string body) { return "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" + body + "</AssetDeclaration>"; }
    static BuildRequest Request(string mod, string output)
    {
        Directory.CreateDirectory(output);
        var request = new BuildRequest { Command="build", ModRoot=mod, SdkRoot=sdk, OutputRoot=output,
            Content=new[] {"base"}, PackageFormat="loose", Arguments=new string[0] };
        foreach (string key in new[] {"sc", "bc"}) request.Options[key] = new EffectiveOption {Value="false", Source="lifecycle-fixture"};
        return request;
    }
    static BuildTask Task(string mod, string name)
    {
        return new BuildTask { Id="base/"+name, Content="base", InputPath=Path.Combine(mod,"Data",name+".xml"),
            OutputName="base/"+name, Reason="LifecycleFixture", Dependencies=new string[0] };
    }
    static StreamContext Context(BuildRequest request, BuildTask task)
    {
        string staging=CacheLayout.ForRequest(request).StreamDirectory(task);
        Directory.CreateDirectory(staging);
        // Application owns these internal setters in production. Reflection only
        // supplies the real context from this separate validation fixture assembly.
        var context = new StreamContext();
        foreach (var pair in new Dictionary<string,object> { {"BuildId","backend-lifecycle"}, {"Request",request}, {"Task",task}, {"OutputDirectory",staging} })
            typeof(StreamContext).GetProperty(pair.Key).GetSetMethod(true).Invoke(context,new[] {pair.Value});
        return context;
    }
    static CoreStreamCompiler Backend() { return new CoreStreamCompiler(delegate(string source, TraceEventType kind, string message) { ++traceCalls; }); }
    static string Hash(string path)
    {
        using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
    }
    static Dictionary<string,string> Four(IList<Artifact> artifacts, string staging)
    {
        Require(artifacts != null && artifacts.Count != 0, "Backend returned no artifacts");
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (Artifact artifact in artifacts)
        {
            Require(artifact != null && Path.IsPathRooted(artifact.Path) && Paths.Contains(staging,artifact.Path), "Artifact escaped staging");
            Require(File.Exists(artifact.Path) && new FileInfo(artifact.Path).Length == artifact.Size, "Artifact size/path mismatch");
            string hash=Hash(artifact.Path);
            string extension=Path.GetExtension(artifact.Path);
            if (extension==".manifest" || extension==".bin" || extension==".imp" || extension==".relo")
                result.Add(artifact.Path.Substring(staging.Length),hash);
        }
        Require(result.Count==4, "Expected exactly four linked stream artifacts");
        foreach (string extension in new[] {".manifest",".bin",".imp",".relo"})
            Require(result.Keys.Count(path=>Path.GetExtension(path)==extension)==1,"Missing linked artifact "+extension);
        return result;
    }
    // Independent byte oracle: no production ManifestHeader/AssetEntry readers,
    // hash helpers, serializer or recovered plugin is called by these checks.
    // Original layout: recovery/evidence/utility/ilspy-source/{AssetStream,
    // BinaryAssetBuilder.Utility}/{ManifestHeader,AssetEntry}.cs; original XML
    // Plugin.GetAllTypesHash=1424942948, Win32 version=4. Manifest format=6.
    // Armor byte contract is also independently asserted by ArmorOracle.cs,
    // whose original-DLL differential receipts predate this backend.
    static uint Word(byte[] data, int offset)
    {
        Require(offset >= 0 && offset + 4 <= data.Length,"Truncated byte oracle word");
        return (uint)(data[offset] | data[offset+1]<<8 | data[offset+2]<<16 | data[offset+3]<<24);
    }
    static void Put(byte[] data,int offset,uint word)
    {
        for(int i=0;i<4;i++) data[offset+i]=(byte)(word>>(8*i));
    }
    static string CString(byte[] data,int start,int length,int offset)
    {
        Require(offset>=0 && offset<length && start>=0 && start+length<=data.Length,"Invalid manifest string offset");
        int end=start+offset;
        while(end<start+length && data[end]!=0) end++;
        Require(end<start+length,"Unterminated manifest string");
        return Encoding.ASCII.GetString(data,start+offset,end-start-offset);
    }
    static byte[] ExpectedArmor(string name,bool variant)
    {
        bool defaults=name=="RecoveryArmorProbe_Defaults", inherited=name=="RecoveryArmorProbe_Inherited";
        int count=defaults?0:4, tokens=96+count*48;
        var bytes=new byte[128+56*count];
        uint[] fieldNames={unchecked((uint)-1538240),unchecked((uint)-899026358),81996879u,unchecked((uint)-2003558244),unchecked((uint)-1429821187)};
        for(int i=0;i<5;i++) { Put(bytes,12*i,fieldNames[i]); Put(bytes,12*i+4,1683514611u); Put(bytes,12*i+8,(uint)(tokens+4+4*i)); }
        Put(bytes,60,735930353u); Put(bytes,64,617967919u); Put(bytes,68,84u);
        Put(bytes,84,1820487795u); Put(bytes,88,1564774360u); Put(bytes,92,(uint)(tokens+24));
        // IEEE-754 constants from original single-precision percentage parsing,
        // including 0.01f's exact representation; do not use decimal shortcuts.
        uint[] scalars=defaults?new uint[] {0x3f800000,0x3f800000,0x3f800000,0x3f800000,0x3f800000}:
            new uint[] {inherited?0x3f1ccccdu:(variant?0x3e000000u:0x3f399999u),0x3f551eb8,0x3f966666,0x3faf0a3d,0x3ed7ae14};
        for(int i=0;i<5;i++) Put(bytes,tokens+4+4*i,scalars[i]);
        Put(bytes,tokens+24,(uint)count);
        if(count>0) Put(bytes,tokens+28,(uint)(tokens+32));
        uint[] damage={22u,35u,21u,38u}, percent={0x3eb5c28fu,0x3f20a3d7u,0x3f9d9999u,0x3f699999u};
        for(int i=0;i<count;i++)
        {
            int table=84+12*(count+1)+36*i;
            Put(bytes,96+12*i,unchecked((uint)-1320050453)); Put(bytes,104+12*i,(uint)table);
            Put(bytes,table,2083775721u); Put(bytes,table+4,1179152369u); Put(bytes,table+8,(uint)(tokens+32+8*i));
            Put(bytes,table+12,unchecked((uint)-1382012893)); Put(bytes,table+16,1683514611u); Put(bytes,table+20,(uint)(tokens+36+8*i));
            Put(bytes,tokens+32+8*i,damage[i]); Put(bytes,tokens+36+8*i,percent[i]);
        }
        return bytes;
    }
    static void SliceEquals(byte[] actual,int offset,int length,byte[] expected,string label)
    {
        Require(length==expected.Length && offset>=0 && offset+length<=actual.Length,label+": chunk size/range mismatch");
        for(int i=0;i<length;i++) Require(actual[offset+i]==expected[i],label+": byte mismatch at "+i);
    }
    static void InspectArmorStreams(byte[] manifest,byte[] bin,byte[] imports,byte[] relocations,bool variant,string source)
    {
        Require(manifest.Length>=48,"Truncated manifest header");
        Require(manifest[0]==0 && manifest[1]==1 && manifest[2]==6 && manifest[3]==0,"Wrong Win32 linked manifest format/version");
        Require(Word(manifest,8)==1424942948u,"Manifest all-types hash differs from original XML default plugin");
        Require(Word(manifest,12)==3 && Word(manifest,16)==832 && Word(manifest,20)==352,"Armor manifest count/instance size maxima changed");
        Require(Word(manifest,24)==8 && Word(manifest,28)==0,"Armor relocation/import maxima changed");
        Require(Word(manifest,32)==0 && Word(manifest,36)==0,"Unexpected Armor runtime or manifest references");
        int names=(int)Word(manifest,40), sources=(int)Word(manifest,44), nameStart=48+3*48, sourceStart=nameStart+names;
        Require(names==129 && sources==Encoding.ASCII.GetByteCount(source)+1,"Unexpected Armor name/source buffer sizes");
        Require(manifest.Length==sourceStart+sources,"Manifest table/buffer lengths do not exactly consume file");
        uint checksum=Word(manifest,4);
        Require(bin.Length==836 && imports.Length==4 && relocations.Length==20,"Linked stream sizes do not match independent Armor sizes");
        Require(Word(bin,0)==checksum && Word(imports,0)==checksum && Word(relocations,0)==checksum,"Four stream checksums disagree");
        var ids=new Dictionary<string,uint> { {"RecoveryArmorProbe_Defaults",1847731836u}, {"RecoveryArmorProbe_NonDefault",2276198084u}, {"RecoveryArmorProbe_Inherited",450363424u} };
        var seen=new HashSet<string>(); int dataOffset=4,reloOffset=4;
        for(int i=0;i<3;i++)
        {
            int entry=48+i*48;
            Require(Word(manifest,entry)==980180622u && Word(manifest,entry+8)==1679479598u && Word(manifest,entry+44)==1,"Armor type ID/hash/tokenized metadata changed");
            Require(Word(manifest,entry+12)!=0 && Word(manifest,entry+16)==0 && Word(manifest,entry+20)==0,"Armor instance hash/references invalid");
            string fullName=CString(manifest,nameStart,names,(int)Word(manifest,entry+24));
            Require(fullName.StartsWith("ArmorTemplate:",StringComparison.Ordinal),"Unexpected manifest asset type name");
            string name=fullName.Substring("ArmorTemplate:".Length);
            Require(ids.ContainsKey(name) && seen.Add(name) && Word(manifest,entry+4)==ids[name],"Unexpected/duplicate Armor name or independent instance ID");
            Require(CString(manifest,sourceStart,sources,(int)Word(manifest,entry+28))==source,"Unexpected manifest logical source path");
            int dataSize=(int)Word(manifest,entry+32),reloSize=(int)Word(manifest,entry+36),importSize=(int)Word(manifest,entry+40);
            byte[] expected=ExpectedArmor(name,variant);
            SliceEquals(bin,dataOffset,dataSize,expected,name+" flattened Armor payload");
            bool defaults=name=="RecoveryArmorProbe_Defaults";
            var expectedRelo=new byte[defaults?0:8];
            if(!defaults) { Put(expectedRelo,0,316u); Put(expectedRelo,4,uint.MaxValue); }
            SliceEquals(relocations,reloOffset,reloSize,expectedRelo,name+" relocation contract");
            Require(importSize==0,"Armor introduced an import chunk");
            dataOffset+=dataSize; reloOffset+=reloSize;
        }
        Require(seen.Count==3 && dataOffset==bin.Length && reloOffset==relocations.Length,"Asset chunks do not exactly consume streams");
    }
    static bool oracleMutationChecked;
    static void IndependentArmorOracle(IList<Artifact> artifacts,bool variant,string source)
    {
        Func<string,byte[]> read=extension=>File.ReadAllBytes(artifacts.Single(a=>Path.GetExtension(a.Path)==extension).Path);
        byte[] manifest=read(".manifest"),bin=read(".bin"),imports=read(".imp"),relo=read(".relo");
        InspectArmorStreams(manifest,bin,imports,relo,variant,source);
        if(oracleMutationChecked) return;
        Action<byte[],byte[],string> reject=delegate(byte[] changedManifest,byte[] changedBin,string label)
        {
            bool rejected=false; try { InspectArmorStreams(changedManifest,changedBin,imports,relo,variant,source); } catch(Exception) { rejected=true; }
            Require(rejected,"Independent byte oracle accepted "+label);
        };
        byte[] wrong=(byte[])manifest.Clone(); Put(wrong,8,0); reject(wrong,bin,"zero all-types hash");
        wrong=(byte[])manifest.Clone(); wrong[2]=0; reject(wrong,bin,"wrong manifest version");
        wrong=(byte[])bin.Clone(); wrong[4]^=1; reject(manifest,wrong,"corrupt Armor token/payload");
        wrong=(byte[])bin.Clone(); wrong[0]^=1; reject(manifest,wrong,"mismatched linked checksum");
        oracleMutationChecked=true; Tests.Add("independent-raw-manifest-original-metadata-Armor-token-scalar-layout-relocations-and-corruption-rejections");
    }
    static void DefaultMetadata(CoreStreamCompiler backend)
    {
        var providers=(BuiltinCompilers)typeof(CoreStreamCompiler).GetField("compilers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(backend);
        Require(providers.Registry.DefaultPlugin.GetAllTypesHash()==1424942948u && providers.Registry.DefaultPlugin.GetVersionNumber()==4u,"Backend default metadata is not the original Win32 XML contract");
    }
    static Dictionary<string,string> Compile(CoreStreamCompiler backend, BuildRequest request, string name, string slot, Ambient ambient)
    {
        BuildTask task=Task(request.ModRoot,name);
        string staging=CacheLayout.ForRequest(request).StreamDirectory(task);
        var artifacts=backend.Compile(Context(request,task),CancellationToken.None);
        var result=Four(artifacts,staging);
        IndependentArmorOracle(artifacts,request.ModRoot.EndsWith("mod-b",StringComparison.Ordinal),name=="Mod"?"DATA:armor.xml":"DATA:extra.xml");
        DefaultMetadata(backend); ambient.Unchanged(slot);
        if(name=="Mod" && slot=="first")
        {
            var outputs=new Dictionary<string,string>();
            foreach(var artifact in artifacts) if(new[] {".manifest",".bin",".imp",".relo"}.Contains(Path.GetExtension(artifact.Path)))
                outputs.Add(Path.GetExtension(artifact.Path).Substring(1),artifact.Path);
            BaselineCases.Add(new { name=Path.GetFileName(request.ModRoot),modRoot=request.ModRoot,inputPath=Task(request.ModRoot,name).InputPath,
                sdkRoot=request.SdkRoot,stagingDirectory=staging,outputs=outputs });
        }
        return result;
    }
    static void Equal(Dictionary<string,string> left, Dictionary<string,string> right, string label)
    {
        Require(left.Count==right.Count,label+": artifact count changed");
        foreach (var pair in left) Require(right.ContainsKey(pair.Key) && right[pair.Key]==pair.Value,label+": bytes changed "+pair.Key);
    }
    static Dictionary<string,string> Files(string root)
    {
        return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path.Substring(root.Length),Hash,StringComparer.Ordinal);
    }
    static void SameFiles(Dictionary<string,string> expected,string root,string label="source MOD")
    {
        var actual=Files(root); Require(expected.Count==actual.Count,"Backend changed file count in "+label);
        foreach(var pair in expected) Require(actual.ContainsKey(pair.Key) && actual[pair.Key]==pair.Value,"Backend mutated "+label);
    }
    static string MakeMod(string name,string variant)
    {
        string root=Path.Combine(work,name);
        Write(Path.Combine(root,"Data/Armor.xml"),variant);
        Write(Path.Combine(root,"Data/Mod.xml"),Asset("<Includes><Include type='all' source='Armor.xml'/></Includes>"));
        Write(Path.Combine(root,"Data/Extra.xml"),variant);
        Write(Path.Combine(root,"Data/Bad.xml"),Asset("<ArmorTemplate id='Broken' inheritFrom='MissingParent'/>"));
        return root;
    }
    static Dictionary<string,string> Cold(BuildRequest request,Ambient ambient)
    {
        using(var backend=Backend()) { backend.Initialize(request); ambient.Unchanged("cold Initialize"); return Compile(backend,request,"Mod","cold",ambient); }
    }
    static void Run(Ambient ambient)
    {
        string a=MakeMod("mod-a",probe), b=MakeMod("mod-b",probe.Replace("Default=\"72.5%\"","Default=\"12.5%\""));
        Require(File.ReadAllText(Path.Combine(a,"Data/Armor.xml"))!=File.ReadAllText(Path.Combine(b,"Data/Armor.xml")),"Variant fixture did not change payload");
        var originalA=Files(a); var originalB=Files(b);
        var requestA=Request(a,Path.Combine(work,"output-a")); var requestB=Request(b,Path.Combine(work,"output-b"));
        Dictionary<string,string> first,second;
        var backend=Backend();
        try
        {
            backend.Initialize(requestA); ambient.Unchanged("Initialize");
            Require(!String.IsNullOrEmpty(backend.ProviderIdentity),"Backend provider identity is missing");
            using(var competitor=Backend())
            {
                bool rejected=false;
                try { competitor.Initialize(requestB); } catch(InvalidOperationException) { rejected=true; } catch(BuildFailure) { rejected=true; }
                Require(rejected,"Concurrent backend lease was accepted"); ambient.Unchanged("Rejected Initialize");
            }
            first=Compile(backend,requestA,"Mod","first",ambient);
            Compile(backend,requestA,"Extra","second-stream",ambient);
            Tests.Add("single-initialize-multiple-real-core-streams-staging-and-ambient-restoration");
            BuildTask badTask=Task(a,"Bad");
            string failed=CacheLayout.ForRequest(requestA).StreamDirectory(badTask); bool failedAsExpected=false;
            try { backend.Compile(Context(requestA,badTask),CancellationToken.None); }
            catch(BinaryAssetBuilderException e) { failedAsExpected=e.ErrorCode==ErrorCode.InheritFromError; }
            catch(BuildFailure e) { failedAsExpected=e.ExitCode==ExitCode.Compilation && e.Code=="CORE_COMPILE_FAILED" && e.Message.Contains("MissingParent"); }
            Require(failedAsExpected,"Bad inheritance was not a compilation failure"); ambient.Unchanged("Failed Compile");
            Require(!Directory.Exists(failed) || Directory.GetFiles(failed,"*",SearchOption.AllDirectories).Length==0,"Failed stream left partial files");
            Equal(first,Compile(backend,requestA,"Mod","after-failure",ambient),"Failure recovery");
            Tests.Add("bad-inheritance-failure-then-same-backend-legal-stream-byte-equality");
            BuildTask cancelledTask=Task(a,"Mod");
            string cancelled=CacheLayout.ForRequest(requestA).StreamDirectory(cancelledTask); bool wasCancelled=false;
            var beforeCancelled=Files(cancelled);
            using(var source=new CancellationTokenSource())
            {
                source.Cancel();
                try { backend.Compile(Context(requestA,cancelledTask),source.Token); }
                catch(OperationCanceledException) { wasCancelled=true; }
            }
            Require(wasCancelled,"Already cancelled stream reached success"); ambient.Unchanged("Cancelled Compile");
            SameFiles(beforeCancelled,cancelled,"cancelled stream cache");
            Equal(first,Compile(backend,requestA,"Mod","after-cancel",ambient),"Cancellation recovery");
            Tests.Add("cancel-before-core-then-same-backend-legal-stream-byte-equality");
        }
        finally { backend.Dispose(); backend.Dispose(); }
        ambient.Unchanged("Dispose twice");
        using(var other=Backend()) { other.Initialize(requestB); second=Compile(other,requestB,"Mod","first",ambient); }
        ambient.Unchanged("Second MOD Dispose");
        string bin=first.Keys.Single(path=>Path.GetExtension(path)==".bin");
        Require(second.ContainsKey(bin) && second[bin]!=first[bin],"Different MOD same IDs produced the same payload");
        Equal(first,Cold(requestA,ambient),"MOD A cold-context comparison");
        Equal(second,Cold(requestB,ambient),"MOD B cold-context comparison");
        Tests.Add("two-mods-same-type-id-different-payload-independent-same-process-cold-context-four-file-comparison");
        var invalid=Request(a,Path.Combine(work,"invalid-output")); invalid.SdkRoot=Path.Combine(work,"missing-sdk");
        using(var broken=Backend())
        {
            bool rejected=false;
            try { broken.Initialize(invalid); } catch(BuildFailure) { rejected=true; } catch(DirectoryNotFoundException) { rejected=true; } catch(FileNotFoundException) { rejected=true; }
            Require(rejected,"Invalid SDK initialization succeeded");
        }
        using(var fresh=Backend()) { fresh.Initialize(requestA); Compile(fresh,requestA,"Mod","after-init-failure",ambient); }
        ambient.Unchanged("Failed initialization lease release");
        SameFiles(originalA,a); SameFiles(originalB,b);
        Require(traceCalls>0,"Backend trace bridge was not exercised");
        Tests.Add("dispose-idempotent-and-failed-initialize-releases-lease-source-mods-unchanged");
    }
    public static int Main(string[] args)
    {
        int code=0; object result; Settings previous=Settings.Current;
        var original=new Ambient();
        try
        {
            if(args.Length!=5 || IntPtr.Size!=4) throw new Exception("Expected work result sourceCommit runId SDK (x86)");
            work=Path.GetFullPath(args[0]); sdk=Path.GetFullPath(args[4]); Directory.CreateDirectory(work);
            probe=File.ReadAllText(Path.Combine(sdk,"Mods/mymod/Data/RecoveryArmorProbe.xml"));
            Require(typeof(CoreStreamCompiler).Assembly==typeof(DocumentProcessor).Assembly,"Backend/Core are outside the same product module");
            Settings.Current=new Settings { StringHashBinDescriptors=new[] {new StringHashBinDescriptor {SchemaTypeName="Poid",BinName="POID"}}, SessionCacheDirectory=Path.Combine(work,"ambient"), OutputAssetReport=false };
            using(Tracer.BeginScope(delegate {},9))
            using(HashProvider.BeginStringHashScope(Settings.Current.SessionCacheDirectory))
            {
                HashProvider.RecordHash("POID","BackendFixtureBaselineOnly");
                HashProvider.StringHashesFile="ambient-custom.xml";
                var ambient=new Ambient(); Run(ambient); ambient.Unchanged("All scenarios");
                HashProvider.FinalizeStringHashes();
                string baseline=File.ReadAllText(Path.Combine(Settings.Current.SessionCacheDirectory,"StringHashes.xml"));
                Require(baseline.Contains("BackendFixtureBaselineOnly") && !baseline.Contains("RecoveryArmorProbe"),"Backend polluted caller hash table contents");
            }
            Settings.Current=previous; original.Unchanged("Fixture scopes restored");
            result=new {succeeded=true,sourceCommit=args[2],runId=args[3],checks=checks,tests=Tests.ToArray(),baselineCases=BaselineCases.ToArray(),fixtureWorkRoot=work,scope="Real in-process backend only; cold comparisons are same-process new-backend contexts; production CLI, cold-process/G3/SDK and persistent-cache acceptance pending"};
            Console.WriteLine("PASS BackendLifecycle: "+Tests.Count+" groups; "+checks+" checks");
        }
        catch(Exception e)
        {
            code=1; result=new {succeeded=false,sourceCommit=args.Length>2?args[2]:null,runId=args.Length>3?args[3]:null,checks=checks,tests=Tests.ToArray(),baselineCases=BaselineCases.ToArray(),fixtureWorkRoot=work,error=e.ToString()}; Console.Error.WriteLine(e);
        }
        finally { Settings.Current=previous; }
        if(args.Length>1) File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));
        return code;
    }
}
