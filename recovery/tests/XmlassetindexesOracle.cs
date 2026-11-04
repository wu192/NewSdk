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
class XmlassetindexesOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Content="DLContent",Reports="AssetReportTable",Table="StringHashTable";
    static readonly string[] Types={Content,Reports,Table};
    static readonly uint[] TypeIds={212695140u,255713734u,300273508u};
    static readonly int[] Sizes={24,12,16};
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
            Type type=typeof(XmlassetindexesCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlassetindexesCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] EStringHashBin={"INSTANCEID","POID","STRINGHASH","TYPEID"};
    static readonly int[] VStringHashBin={1,2,3,0};
    static XmlDocument SetText(string root,string path,string value){XmlDocument d=Set(root,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string part in path.Split('/'))n=n.FirstChild;n.InnerText=value;return d;}
    static void Attr(string root,string path,string key,IEnumerable<string> values,TargetPlatform p){foreach(string value in values)TestDoc("attr/"+root+"/"+path+"/"+key+"/"+value.Length,Set(root,path,key,value),p);}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){AssetBuffer d=Test("ABI/default",Root(Types[i],"",""),p);Check(d.InstanceData.Length==Sizes[i],"independent root size");foreach(byte value in d.InstanceData)Check(value==0,"default absent fields zero");Check(d.RelocationData.Length==0&&d.ImportsData.Length==0,"default no bookmarks");}
        AssetBuffer c=Test("ABI/content",Root(Content,"PackageNumber='257'","<Patch Name='A' PatchNumber='258'/><License LicenseMask='0x01020304'><Asset>ABC</Asset><Asset>abc</Asset></License><Patch Name='' PatchNumber='-1'/><License LicenseMask='7'><Asset>OTHER</Asset></License>"),p);Check(c.InstanceData.Length==92&&Word(c.InstanceData,4,big)==2&&Word(c.InstanceData,8,big)==24&&Word(c.InstanceData,12,big)==2&&Word(c.InstanceData,16,big)==60&&c.InstanceData[20]==1,"DLContent24 bytePackage and license/patch lists source order");Check(Word(c.InstanceData,24,big)==0x01020304u&&Word(c.InstanceData,28,big)==2&&Word(c.InstanceData,32,big)==48&&Word(c.InstanceData,36,big)==7&&Word(c.InstanceData,40,big)==1&&Word(c.InstanceData,44,big)==56,"License12 unsignedmask and weak list4");Check(Word(c.InstanceData,48,big)==Word(c.InstanceData,52,big)&&Word(c.InstanceData,48,big)!=0,"weak DL assets lowercase before hashing");Check(Word(c.InstanceData,60,big)==1&&Word(c.InstanceData,64,big)==84&&c.InstanceData[68]==2&&Word(c.InstanceData,72,big)==0&&Word(c.InstanceData,76,big)==88&&c.InstanceData[80]==255,"Patch12 String8 plus bytePatchNumber truncation independent of endian");Offsets(c.RelocationData,new uint[]{8,16,32,44,64,76},big,"DL root/licensearray/patcharray owner order");Check(c.ImportsData.Length==0,"DL typedweak hashes create no imports");
        AssetBuffer r=Test("ABI/report",Root(Reports,"","<AssetReport Id='ABC' Type='abc' AssetSize='0xff'><Reference Id='ABC' Type='abc'/><Reference Id='abc' Type='ABC'/></AssetReport>"),p);Check(r.InstanceData.Length==48&&Word(r.InstanceData,4,big)==1&&Word(r.InstanceData,8,big)==12&&Word(r.InstanceData,20,big)==255&&Word(r.InstanceData,24,big)==2&&Word(r.InstanceData,28,big)==32,"Report20 rawId/rawType/uintSize/referenceList8");Check(Word(r.InstanceData,12,big)!=Word(r.InstanceData,16,big)&&Word(r.InstanceData,12,big)!=0&&Word(r.InstanceData,16,big)!=0,"StringHash Id/Type are raw case-sensitive hashes");Check(Word(r.InstanceData,32,big)==Word(r.InstanceData,12,big)&&Word(r.InstanceData,36,big)==Word(r.InstanceData,16,big)&&Word(r.InstanceData,40,big)==Word(r.InstanceData,16,big)&&Word(r.InstanceData,44,big)==Word(r.InstanceData,12,big),"Reference8 rawhash field order");Offsets(r.RelocationData,new uint[]{8,28},big,"report outer then nested array pointers");Check(r.ImportsData.Length==0,"raw report hashes are not asset import refs");
        AssetBuffer s=Test("ABI/table",Root(Table,"StringHashBin='POID' Version='999'","<StringAndHash Hash='0xff' Text='A'/><StringAndHash Hash='7' Text=''/>"),p);Check(s.InstanceData.Length==48&&Word(s.InstanceData,4,big)==2&&Word(s.InstanceData,8,big)==2&&Word(s.InstanceData,12,big)==16&&Word(s.InstanceData,16,big)==255&&Word(s.InstanceData,20,big)==1&&Word(s.InstanceData,24,big)==40&&Word(s.InstanceData,28,big)==7&&Word(s.InstanceData,32,big)==0&&Word(s.InstanceData,36,big)==44,"StringHashTable16 enum/list12; stored Hash uint not hash of textual numeric");Check(s.InstanceData[40]==65&&s.InstanceData[41]==0&&s.InstanceData[44]==0,"StringAndHash exact text bytes");Offsets(s.RelocationData,new uint[]{12,24,36},big,"table ownerblocks and string targets");AssetBuffer unknown=Test("ABI/bin-fallback",Root(Table,"StringHashBin=''",""),p);Check(Word(unknown.InstanceData,4,big)==1,"presentunknown binINSTANCEID1 distinct absentTYPEID0");AssetBuffer pipeline=Test("ABI/version-pipelineonly",Root(Table,"Version='garbage'",""),p);Check(pipeline.InstanceData.Length==16&&Word(pipeline.InstanceData,4,big)==0,"pipelineOnlyVersion ignored by original Handler");
        XmlDocument nul=Set(Reports,"AssetReport","Id","ABC\0tail");AssetBuffer n=TestDoc("ABI/rawNUL",nul,p);Check(Word(n.InstanceData,12,big)==Word(r.InstanceData,12,big),"rawhash truncates at C-string terminator");
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){Test("default",Root(Types[i],"",""),p);Test("metadata",Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='parent'",""),p);Reject("rootattr",Root(Types[i],"Unknown='1'",""));Reject("rootchild",Root(Types[i],"","<Unknown/>"));Reject("roottext",Root(Types[i],"","payload"));Reject("qualifiedrootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("foreignchild",Root(Types[i],"","<License xmlns='urn:q'/>"));}
        Attr(Content,"","PackageNumber",Scalars,p);Attr(Content,"Patch","PatchNumber",Scalars,p);Attr(Content,"License","LicenseMask",Scalars,p);Attr(Content,"Patch","Name",Strings,p);Attr(Reports,"AssetReport","AssetSize",Scalars,p);Attr(Table,"StringAndHash","Hash",Scalars,p);Attr(Table,"StringAndHash","Text",Strings,p);Attr(Table,"","Version",Strings,p);foreach(string path in new string[]{"AssetReport","AssetReport/Reference"})foreach(string key in new string[]{"Id","Type"}){Attr(Reports,path,key,Strings,p);Attr(Reports,path,key,References,p);}foreach(string value in Strings)TestDoc("DL-assetweak",SetText(Content,"License/Asset",value),p);foreach(string value in References)TestDoc("DL-assetweak-reference-looking",SetText(Content,"License/Asset",value),p);foreach(string value in new string[]{"255","256","257","65535","65536","-255","-256","-257"}){TestDoc("package-byte",Set(Content,"","PackageNumber",value),p);TestDoc("patch-byte",Set(Content,"Patch","PatchNumber",value),p);}
        for(int i=0;i<EStringHashBin.Length;++i){AssetBuffer b=TestDoc("Binexact",Set(Table,"","StringHashBin",EStringHashBin[i]),p);Check(Word(b.InstanceData,4,big)==VStringHashBin[i],"original PE StringHashBin enumvalue");foreach(string value in new string[]{EStringHashBin[i].ToLowerInvariant()," "+EStringHashBin[i],EStringHashBin[i]+" ",EStringHashBin[i]+"\0tail","+"+EStringHashBin[i],"-"+EStringHashBin[i],"","UNKNOWN"})TestDoc("Binboundary",Set(Table,"","StringHashBin",value),p);}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder content=new StringBuilder(),reports=new StringBuilder(),table=new StringBuilder(),assets=new StringBuilder(),refs=new StringBuilder();for(int i=0;i<count;++i){content.Append("<Patch Name='A' PatchNumber='"+i+"'/><License LicenseMask='7'><Asset>A</Asset></License>");reports.Append("<AssetReport Id='A' Type='B' AssetSize='1'><Reference Id='B' Type='A'/></AssetReport>");table.Append("<StringAndHash Hash='"+i+"' Text='A'/>");assets.Append("<Asset>A"+i+"</Asset>");refs.Append("<Reference Id='A' Type='B'/>");}AssetBuffer c=Test("contentgrowth",Root(Content,"",content.ToString()),p);Check(c.InstanceData.Length==24+32*count&&Word(c.InstanceData,4,big)==count&&Word(c.InstanceData,12,big)==count,"License12+weak4+Patch12+name4 growth");AssetBuffer r=Test("reportgrowth",Root(Reports,"",reports.ToString()),p);Check(r.InstanceData.Length==12+28*count&&Word(r.InstanceData,4,big)==count,"report20+reference8 growth");AssetBuffer t=Test("tablegrowth",Root(Table,"",table.ToString()),p);Check(t.InstanceData.Length==16+16*count&&Word(t.InstanceData,8,big)==count,"StringAndHash12+text4 growth");AssetBuffer a=Test("weakassetgrowth",Root(Content,"","<License>"+assets+"</License>"),p);Check(a.InstanceData.Length==36+4*count,"nested weak assets4 growth");AssetBuffer n=Test("nestedreferencegrowth",Root(Reports,"","<AssetReport>"+refs+"</AssetReport>"),p);Check(n.InstanceData.Length==32+8*count,"nested Reference8 growth");}
        foreach(string[] field in new string[][]{new string[]{Content,"Patch","Name"},new string[]{Table,"StringAndHash","Text"},new string[]{Reports,"AssetReport","Id"}})foreach(int count in new int[]{0,1,3,4,7,8,15,16,27,28,31,32,255,256,257,1023,1024,4095,4096,65535})TestDoc("longtext/hash",Set(field[0],field[1],field[2],new string('A',count)),p);
        foreach(string[] path in new string[][]{new string[]{Content,"License"},new string[]{Content,"License/Asset"},new string[]{Content,"Patch"},new string[]{Reports,"AssetReport"},new string[]{Reports,"AssetReport/Reference"},new string[]{Table,"StringAndHash"}}){TestDoc("nestedmetadata",Set(path[0],path[1],"TypeId","1"),p);Reject("nestedunknown",Set(path[0],path[1],"Unknown","1").OuterXml);}Reject("licensetext",Root(Content,"","<License>payload</License>"));Reject("patchchild",Root(Content,"","<Patch><Unknown/></Patch>"));Reject("assetchild",Root(Content,"","<License><Asset><Unknown/></Asset></License>"));Reject("reporttext",Root(Reports,"","<AssetReport>payload</AssetReport>"));Reject("referencechild",Root(Reports,"","<AssetReport><Reference><Unknown/></Reference></AssetReport>"));Reject("tabletext",Root(Table,"","<StringAndHash>payload</StringAndHash>"));Reject("unknownroot",Root("Unknown","",""));bool nil=false;try{XmlassetindexesCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Content,"","")));dec.Handle.TypeName=Table;bool wrong=false;try{XmlassetindexesCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
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
            Console.WriteLine("PASS Asset indexes fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

