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
class XmlrenderassetsOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Mesh="MeshInstanceTemplate",Packed="PackedTextureImage",Demand="OnDemandTextureImage";
    static readonly string[] Types={Mesh,Packed,Demand};
    static readonly uint[] TypeIds={815686049u,1449288224u,4262364347u};
    static readonly int[] Sizes={12,36,36};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Channels={"R","G","B","A"};
    static readonly string[] Destinations={"Normal","Aggressive","","UNKNOWN","normal","aggressive"," Normal","Normal ","0","1","Normal\0tail","Aggressive\0tail","UNKNOWN\0Normal"};
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
        HashSet<uint> seen=new HashSet<uint>();for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at%4==0&&(ulong)at+4<=(ulong)instance.Length,label+" aligned field bounds");Check(seen.Add(at),label+" unique bookmark offsets");if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlrenderassetsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlrenderassetsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] Strings={"","a","ab","abc","abcd","abcde","abcdefg","abcdefgh","abcdefghi","MixedCase","  spaces  ","A\tB\r\nC","<&'\"","资源","éßð","😀","A\0tail","\0tail","A\0B\0C"};
    static readonly string[] Scalars={"","0","-0","1","-1","TRUE","true","True","false","False","FALSE","yes","077","0xff","garbage","2147483647","2147483648","-2147483648","-2147483649","4294967295","4294967296","9999999999999999999999999999","  +17junk","1\0tail","\0TRUE"};
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){AssetBuffer d=Test("ABI/default",Root(Types[i],"",""),p);Check(d.InstanceData.Length==Sizes[i],"independent root size");foreach(byte value in d.InstanceData)Check(value==0,"default byteszero");Check(d.RelocationData.Length==0&&d.ImportsData.Length==0,"default no bookmarks");}
        AssetBuffer m=Test("ABI/mesh",Root(Mesh,"","<Rule MeshID='Ab' Instances='7'/><Rule MeshID='' Instances='-1'/>"),p);Check(m.InstanceData.Length==44&&Word(m.InstanceData,4,big)==2&&Word(m.InstanceData,8,big)==12&&Word(m.InstanceData,12,big)==2&&Word(m.InstanceData,16,big)==36&&Word(m.InstanceData,20,big)==7&&Word(m.InstanceData,24,big)==0&&Word(m.InstanceData,28,big)==40&&Word(m.InstanceData,32,big)==UInt32.MaxValue,"Mesh12/MatchRule12 MeshID String8 plus signedInstances");Check(m.InstanceData[36]==65&&m.InstanceData[37]==98&&m.InstanceData[38]==0&&m.InstanceData[40]==0,"MeshID actual casepreserving string, not an AssetId hash");Offsets(m.RelocationData,new uint[]{8,16,28},big,"mesh root then Ruleowner pointers");Check(m.ImportsData.Length==0,"MeshID names emit no asset imports");
        foreach(string root in new string[]{Packed,Demand}){AssetBuffer a=Test("ABI/image",Root(root,"Texture='T\\17' Rotated='true'","<TextureDimensions x='1024' y='512'/><Coords x='7' y='9'/><Dimensions x='0x01020304' y='-1'/>"),p);Check(a.InstanceData.Length==36&&Word(a.InstanceData,0,big)==0&&Word(a.InstanceData,4,big)==18&&Word(a.InstanceData,8,big)==0x01020304u&&Word(a.InstanceData,12,big)==UInt32.MaxValue&&Word(a.InstanceData,16,big)==7&&Word(a.InstanceData,20,big)==9&&Word(a.InstanceData,24,big)==1024&&Word(a.InstanceData,28,big)==512,"Image36 inline ICoord2Dx3 offsets independent of XML order");Check(a.InstanceData[32]==1&&a.InstanceData[33]==0&&a.InstanceData[34]==0&&a.InstanceData[35]==0,"Rotated byte32 and unswapped padding33..35 on all platforms");Check(a.RelocationData.Length==0,"image inline coordinates do not allocate");Offsets(a.ImportsData,new uint[]{4},big,"Texture only strong import");AssetBuffer empty=Test("ABI/image-presentempty",Root(root,"","<Dimensions/><Coords/><TextureDimensions/>"),p);Check(empty.InstanceData.Length==36&&Word(empty.InstanceData,8,big)==0&&Word(empty.InstanceData,12,big)==0&&empty.RelocationData.Length==0,"presentempty coordinates use zero defaults without allocations");}
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){Test("default",Root(Types[i],"",""),p);Test("metadata",Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='parent'",""),p);Reject("rootattr",Root(Types[i],"Unknown='1'",""));Reject("rootchild",Root(Types[i],"","<Unknown/>"));Reject("roottext",Root(Types[i],"","payload"));Reject("qualifiedrootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("foreignchild",Root(Types[i],"","<Dimensions xmlns='urn:q'/>"));}
        foreach(string root in new string[]{Packed,Demand}){foreach(string value in References)TestDoc("textureref",Set(root,"","Texture",value),p);foreach(string value in Scalars)TestDoc("rotated",Set(root,"","Rotated",value),p);foreach(string path in new string[]{"Dimensions","Coords","TextureDimensions"})foreach(string axis in new string[]{"x","y"}){foreach(string value in Scalars)TestDoc("coordinateCRT",Set(root,path,axis,value),p);foreach(string value in Numbers)TestDoc("coordinatepartialnumber",Set(root,path,axis,value),p);}foreach(string path in new string[]{"Dimensions","Coords","TextureDimensions"}){TestDoc("coordmetadata",Set(root,path,"TypeId","1"),p);Reject("coordattr",Set(root,path,"Unknown","1").OuterXml);Reject("coorduppercaseaxis",Set(root,path,"X","1").OuterXml);Reject("coordchild",Root(root,"","<"+path+"><Unknown/></"+path+">"));Reject("coordtext",Root(root,"","<"+path+">payload</"+path+">"));Reject("coordduplicate",Root(root,"","<"+path+"/><"+path+"/>"));}}
        foreach(string value in Scalars)TestDoc("instancesCRT",Set(Mesh,"Rule","Instances",value),p);foreach(string value in Numbers)TestDoc("instancespartial",Set(Mesh,"Rule","Instances",value),p);foreach(string value in Strings)TestDoc("meshname",Set(Mesh,"Rule","MeshID",value),p);foreach(int count in new int[]{0,1,3,4,7,8,15,16,27,28,31,32,255,256,257,1023,1024,4095,4096,65535}){AssetBuffer a=TestDoc("meshnamegrowth",Set(Mesh,"Rule","MeshID",new string('A',count)),p);Check(a.InstanceData.Length==24+((count+4)&~3)&&Word(a.InstanceData,12,big)==count,"Rule12 plus padded C-string exactgrowth");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder list=new StringBuilder();for(int i=0;i<count;++i)list.Append("<Rule MeshID='A' Instances='"+i+"'/>");AssetBuffer a=Test("rulegrowth",Root(Mesh,"",list.ToString()),p);Check(a.InstanceData.Length==12+16*count&&Word(a.InstanceData,4,big)==count,"Rule12 plus name4 growth");if(count>0){uint[] pointers=new uint[count+1];pointers[0]=8;for(int i=0;i<count;++i)pointers[i+1]=(uint)(16+12*i);Offsets(a.RelocationData,pointers,big,"all Rule name pointers retained in owner/target order");}}
        TestDoc("Rulemetadata",Set(Mesh,"Rule","TypeId","1"),p);Reject("Ruleunknownattr",Set(Mesh,"Rule","Unknown","1").OuterXml);Reject("Ruletext",Root(Mesh,"","<Rule>payload</Rule>"));Reject("Rulechild",Root(Mesh,"","<Rule><Unknown/></Rule>"));Reject("Rulequalified",Root(Mesh,"","<Rule xmlns:q='urn:q' q:MeshID='A'/>"));Reject("unknownroot",Root("Unknown","",""));bool nil=false;try{XmlrenderassetsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Mesh,"","")));dec.Handle.TypeName=Packed;bool wrong=false;try{XmlrenderassetsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
    }
    static int Main(string[] args) {
        Dictionary<string,object> result=new Dictionary<string,object>();result["succeeded"]=false;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Assembly assembly=Assembly.LoadFrom(args[0]);original=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            using(SHA256 sha=SHA256.Create()){string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","");Check(hash=="D4B78E2DA951DD458357BEE7B358E6BC9689D1987D55867254D68B3F87BBF0E6","fixed original DLL");result["originalDllSha256"]=hash;}
            List<object> rows=new List<object>();
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                int f=fixtures,c=comparisons,r=rejections,l=layouts;Matrix(p);Dictionary<string,object> row=new Dictionary<string,object>();row["platform"]=(int)p;row["fixtures"]=fixtures-f;row["arrayComparisons"]=comparisons-c;row["rejections"]=rejections-r;row["layoutAssertions"]=layouts-l;rows.Add(row);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            result["platforms"]=rows;result["fixtures"]=fixtures;result["arrayComparisons"]=comparisons;result["rejections"]=rejections;result["layoutAssertions"]=layouts;result["succeeded"]=true;
            Console.WriteLine("PASS Render assets fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

