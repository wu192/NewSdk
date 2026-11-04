using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
class XmltesttexturesOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Texture="TestTexture",Collection="TestTextureCollection";
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static IAssetBuilderPlugin original;
    static int fixtures,comparisons,rejections,layouts;
    static XmlDocument Parse(string text){XmlDocument d=new XmlDocument();d.LoadXml(text);return d;}
    static string Escape(string text){return text.Replace("&","&amp;").Replace("'","&apos;").Replace("<","&lt;").Replace("\"","&quot;");}
    static string Root(string type,string attrs,string children){return "<"+type+" xmlns='"+NS+"' id='Oracle' "+attrs+">"+children+"</"+type+">";}
    static XmlNamespaceManager Namespaces(XmlDocument d){XmlNamespaceManager n=new XmlNamespaceManager(d.NameTable);n.AddNamespace("ea",NS);return n;}
    static InstanceDeclaration Declaration(XmlDocument d) {
        AssetDeclarationDocument document=new AssetDeclarationDocument();
        Type stateType=typeof(AssetDeclarationDocument).GetNestedType("CurrentState",BindingFlags.NonPublic);
        object state=FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state,Namespaces(d));
        typeof(AssetDeclarationDocument).GetField("_Current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(document,state);
        InstanceDeclaration declaration=new InstanceDeclaration(document);declaration.XmlNode=d.DocumentElement;return declaration;
    }
    static void Equal(string label,byte[] a,byte[] b){
        if(a==null||b==null||a.Length!=b.Length)throw new Exception(label+" length original="+(a==null?-1:a.Length)+" recovered="+(b==null?-1:b.Length));
        for(int i=0;i<a.Length;++i)if(a[i]!=b[i])throw new Exception(label+" byte="+i+" original="+a[i]+" recovered="+b[i]);++comparisons;
    }
    static void Check(bool test,string label){if(!test)throw new Exception(label);++layouts;}
    static uint Word(byte[] bytes,int offset,bool big){return big?((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|((uint)bytes[offset+2]<<8)|bytes[offset+3]:BitConverter.ToUInt32(bytes,offset);}
    static AssetBuffer Call(Type type,string method,object[] args){try{return (AssetBuffer)type.GetMethod(method).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void SideArray(byte[] bytes,byte[] instance,bool big,bool relocation,string label){
        if(bytes.Length==0)return;
        Check(bytes.Length%4==0,label+" alignment");Check(Word(bytes,bytes.Length-4,big)==UInt32.MaxValue,label+" sentinel");
        HashSet<uint> seen=new HashSet<uint>();for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at%4==0&&(ulong)at+4<=(ulong)instance.Length,label+" aligned field bounds");Check(seen.Add(at),label+" unique bookmark offsets");if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&(target<instance.Length||(target==instance.Length&&(ulong)at+8<=(ulong)instance.Length&&Word(instance,(int)at+4,big)==0)),label+" target bounds/alignment; end target only zero-sized DataBlob");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmltesttexturesCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmltesttexturesCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static XmlDocument Set(string root,string path,string attribute,string value){
        XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;
        if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement c=d.CreateElement(part,NS);node.AppendChild(c);node=c;}
        node.SetAttribute(attribute,value);return d;
    }
    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" ownerblock/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static string fixtureDirectory;
    static readonly int[] BlobSizes={0,1,2,3,4,5,7,8,15,16,17,27,28,29,31,32,33,63,64,65,255,256,257,1023,1024,1025,4095,4096,4097,65535,65536,65537};
    static readonly Dictionary<int,string> BlobFiles=new Dictionary<int,string>();
    static byte[] Bytes(int size){byte[] data=new byte[size];for(int i=0;i<size;++i)data[i]=(byte)(i*73+19);return data;}
    static void CreateFixtures(){fixtureDirectory=Path.Combine(Path.GetTempPath(),"XmlTestTexturesOracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixtureDirectory);foreach(int size in BlobSizes){string file=Path.Combine(fixtureDirectory,"file ASCII spaces "+size+".bin");File.WriteAllBytes(file,Bytes(size));BlobFiles.Add(size,file);}}
    static byte[] Ansi(string value){IntPtr ptr=System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(value);try{List<byte> bytes=new List<byte>();for(int i=0;;++i){byte b=System.Runtime.InteropServices.Marshal.ReadByte(ptr,i);if(b==0)break;bytes.Add(b);}return bytes.ToArray();}finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);}}
    static XmlDocument Files(params string[] paths){XmlDocument d=Parse(Root(Texture,"",""));foreach(string path in paths){XmlElement f=d.CreateElement("File",NS);f.InnerText=path;d.DocumentElement.AppendChild(f);}return d;}
    static XmlDocument Refs(params string[] refs){XmlDocument d=Parse(Root(Collection,"",""));foreach(string value in refs){XmlElement f=d.CreateElement("Texture",NS);f.InnerText=value;d.DocumentElement.AppendChild(f);}return d;}
    static void Abi(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer a=TestDoc("ABI/file-one-empty",Files(BlobFiles[1],BlobFiles[0]),p);
        Check(a.InstanceData.Length==40&&Word(a.InstanceData,12,big)==2&&Word(a.InstanceData,16,big)==20,"root20/file2x8/payload4/zeroalloc0");
        Check(Word(a.InstanceData,20,big)==36&&Word(a.InstanceData,24,big)==1&&Word(a.InstanceData,28,big)==40&&Word(a.InstanceData,32,big)==0,"DataBlob pointer then bytecount/emptyblock pointer=end");
        Check(a.InstanceData[36]==19&&a.InstanceData[37]==0&&a.InstanceData[38]==0&&a.InstanceData[39]==0,"opaque data byte and zero padding");Offsets(a.RelocationData,new uint[]{16,20,28},big,"file pointers ownerblock then targetblock");Offsets(a.ImportsData,new uint[0],big,"raw blobs no imports");
        XmlDocument mixed=Files(BlobFiles[3],BlobFiles[2]);mixed.DocumentElement.SetAttribute("CustomData","AB");AssetBuffer b=TestDoc("ABI/customdata-first",mixed,p);
        Check(b.InstanceData.Length==48&&Word(b.InstanceData,4,big)==2&&Word(b.InstanceData,8,big)==20&&Word(b.InstanceData,12,big)==2&&Word(b.InstanceData,16,big)==24,"CustomData string before full bloblist8stride");
        Check(Word(b.InstanceData,24,big)==40&&Word(b.InstanceData,28,big)==3&&Word(b.InstanceData,32,big)==44&&Word(b.InstanceData,36,big)==2,"each file block after list wholeallocation");Offsets(b.RelocationData,new uint[]{8,16,24,32},big,"custom/list/file pointers");
        AssetBuffer c=TestDoc("ABI/references",Refs("T\\9","T\\1","T\\7","T\\3"),p);Check(c.InstanceData.Length==28&&Word(c.InstanceData,4,big)==4&&Word(c.InstanceData,8,big)==12,"collection root12/list4stride");uint[] indices={10,2,8,4};for(int i=0;i<4;++i)Check(Word(c.InstanceData,12+4*i,big)==indices[i],"strong ref index+1");Offsets(c.RelocationData,new uint[]{8},big,"collection pointer");Offsets(c.ImportsData,new uint[]{16,24,20,12},big,"collection reverse-index imports");
        AssetBuffer missing=TestDoc("ABI/missing-file",Files(Path.Combine(fixtureDirectory,"does-not-exist.bin")),p);Check(missing.InstanceData.Length==28&&Word(missing.InstanceData,20,big)==0&&Word(missing.InstanceData,24,big)==0,"fopen failure keeps null DataBlob within still-present list");Offsets(missing.RelocationData,new uint[]{16},big,"failed file no data pointer");
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        string[] roots={Texture,Collection};int[] sizes={20,12};for(int i=0;i<roots.Length;++i){AssetBuffer a=Test("defaults/"+roots[i],Root(roots[i],"",""),p);Check(a.InstanceData.Length==sizes[i],"independent rootsize");foreach(byte b in a.InstanceData)Check(b==0,"empty roots allzero");Test("metadata/"+roots[i],Root(roots[i],"TypeId='1' inheritFrom='Ignored'",""),p);
            Reject("unknownattr",Root(roots[i],"Unknown='1'",""));Reject("qualifiedattr",Root(roots[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("unknownchild",Root(roots[i],"","<Unknown/>"));Reject("roottext",Root(roots[i],"","payload"));
        }
        foreach(string value in References)TestDoc("reference/"+value.Length,Refs(value),p);
        foreach(int size in BlobSizes){AssetBuffer a=TestDoc("filegrowth/"+size,Files(BlobFiles[size]),p);int padded=(size+3)/4*4;Check(a.InstanceData.Length==28+padded&&Word(a.InstanceData,20,big)==28&&Word(a.InstanceData,24,big)==size,"file exact DataBlob length/alignment including zero");byte[] bytes=Bytes(size);for(int i=0;i<size;++i)Check(a.InstanceData[28+i]==bytes[i],"opaque payload every byte independent");for(int i=size;i<padded;++i)Check(a.InstanceData[28+i]==0,"file padding zero");}
        foreach(string value in new string[]{"","AB","ABCDE","资源","😀","A\0tail","\0tail","A\\17","a\tb","a\nb"}){AssetBuffer a=TestDoc("CustomData/"+value.Length,Set(Texture,"","CustomData",value),p);byte[] bytes=Ansi(value);Check(a.InstanceData.Length==20+((bytes.Length+4)/4)*4&&Word(a.InstanceData,4,big)==bytes.Length&&Word(a.InstanceData,8,big)==20,"CustomData exact ANSI C-string length/paddedsize");for(int i=0;i<bytes.Length;++i)Check(a.InstanceData[20+i]==bytes[i],"CustomData each ANSI byte");for(int i=bytes.Length;i<a.InstanceData.Length-20;++i)Check(a.InstanceData[20+i]==0,"CustomData terminator/padding");}
        foreach(int count in new int[]{0,1,2,3,4,5,15,16,17,27,28,29,64,257,1024,65535}){AssetBuffer a=TestDoc("customgrowth/"+count,Set(Texture,"","CustomData",new string('X',count)),p);Check(a.InstanceData.Length==20+((count+4)/4)*4&&Word(a.InstanceData,4,big)==count,"customdata exact growth");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            string[] files=new string[count],refs=new string[count];for(int i=0;i<count;++i){files[i]=BlobFiles[1];refs[i]="T\\"+(count-i);}
            AssetBuffer a=TestDoc("file-listgrowth/"+count,Files(files),p);Check(a.InstanceData.Length==20+12*count&&Word(a.InstanceData,12,big)==count,"filelist8 plus file4 each exactgrowth");if(count>0){uint[] ptrs=new uint[count+1];ptrs[0]=16;for(int i=0;i<count;++i){ptrs[i+1]=(uint)(20+8*i);Check(Word(a.InstanceData,20+8*i,big)==20+8*count+4*i,"file bytes allocation after complete list");}Offsets(a.RelocationData,ptrs,big,"all file data pointer bookmarks");}
            AssetBuffer b=TestDoc("ref-listgrowth/"+count,Refs(refs),p);Check(b.InstanceData.Length==12+4*count&&Word(b.InstanceData,4,big)==count,"collection exactgrowth");if(count>0){uint[] imports=new uint[count];for(int i=0;i<count;++i)imports[i]=(uint)(12+4*(count-1-i));Offsets(b.ImportsData,imports,big,"all descending strong import indices");}
        }
        foreach(string file in new string[]{"",Path.Combine(fixtureDirectory,"missing.bin"),BlobFiles[3]+"\0ignored",BlobFiles[3]+"nope","\0"+BlobFiles[3]})TestDoc("file-failure-or-NUL/"+file.Length,Files(file),p);
        Test("nested metadata",Root(Texture,"","<File TypeId='1'/>"),p);Test("ref metadata",Root(Collection,"","<Texture TypeId='1'>T\\1</Texture>"),p);Reject("fileattr",Root(Texture,"","<File Unknown='1'/>"));Reject("filechild",Root(Texture,"","<File><Unknown/></File>"));Reject("refattr",Root(Collection,"","<Texture Unknown='1'/>"));Reject("refchild",Root(Collection,"","<Texture><Unknown/></Texture>"));Reject("foreignchild",Root(Texture,"","<File xmlns='urn:q'/>"));Reject("wrongnamespace","<TestTexture xmlns='urn:q'/>");Reject("unknownroot",Root("Unknown","",""));
        bool nullInput=false;try{XmltesttexturesCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration");++rejections;XmlDocument wrong=Parse(Root(Texture,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Collection;bool mismatch=false;try{XmltesttexturesCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/TypeId mismatch");++rejections;
        Abi(p);
    }
    static int Main(string[] args) {
        Dictionary<string,object> result=new Dictionary<string,object>();result["succeeded"]=false;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Assembly assembly=Assembly.LoadFrom(args[0]);original=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            using(SHA256 sha=SHA256.Create()){string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","");Check(hash=="D4B78E2DA951DD458357BEE7B358E6BC9689D1987D55867254D68B3F87BBF0E6","fixed original DLL");result["originalDllSha256"]=hash;}
            CreateFixtures();List<object> rows=new List<object>();
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                int f=fixtures,c=comparisons,r=rejections,l=layouts;Matrix(p);Dictionary<string,object> row=new Dictionary<string,object>();row["platform"]=(int)p;row["fixtures"]=fixtures-f;row["arrayComparisons"]=comparisons-c;row["rejections"]=rejections-r;row["layoutAssertions"]=layouts-l;rows.Add(row);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            result["platforms"]=rows;result["fixtures"]=fixtures;result["arrayComparisons"]=comparisons;result["rejections"]=rejections;result["layoutAssertions"]=layouts;result["succeeded"]=true;
            Console.WriteLine("PASS Test textures fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(fixtureDirectory!=null&&Directory.Exists(fixtureDirectory))Directory.Delete(fixtureDirectory,true);if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

