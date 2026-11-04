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
class XmlsystemsettingsOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Network="NetworkSettings",Pathfind="PathfindSettings",Resource="ResourceSettings",Graphics="GraphicsHardware";
    static readonly string[] Types={Network,Pathfind,Resource,Graphics};
    static readonly uint[] TypeIds={4272453293u,970663416u,3279338544u,2846460739u};
    static readonly int[] Sizes={8,64,8,12};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
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
            Type type=typeof(XmlsystemsettingsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlsystemsettingsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" ownerblock/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static XmlDocument Attr(string type,string key,string value){XmlDocument d=Parse(Root(type,"",""));d.DocumentElement.SetAttribute(key,value);return d;}
    static readonly string[] PathFields={"MaxPathfindCellsPerFrame","MaxPathfindCellsPerPhase","MaxCellsFindMeleeEngagementLocation","MaxCellsAdjustDestination","MaxCellsAdjustHordeMeleeDestination","MaxCellsAdjustTargetDestination","MaxCellsAdjustToPossibleDestination","MaxCellsAdjustToMeleeDestination","MaxCellsAdjustToNearestGroundCell","MaxCellsAdjustToNearestValidCell","MaxCellsPatchPath","MaxCellsToExamineTowardsGoal","MaxExtraCollisionResponsePathsPerFrame","MaxExtraMoveAwayFromPathPathsPerFrame","LayerZCloseEnough"};
    static readonly string[] MoneyTransactionCategoryNames={"AI_MONEY_LENDER","AUTO_DEPOSIT","BOUNTY","BUILD_WALLSPAN","CANCEL_BUILD","CAPTURE_BONUS","CASH_CHEAT","CASH_HACK","DEATH_REFUND","DEVASTATE","DOZER_CONSTRUCT","HARVESTER","INFILTRATOR","MONEY_CRATE","PILLAGE","PLAYERKILLED","PLAYER_CLONE","PLAYER_GIVE_MONEY","PRODUCTION","SCRIPTED_MONEY_ACTION","SELF_REPAIR","SELL_OBJECT","SHARED_DEPOSIT","SLAUGHTER","SPECIAL_POWER","STARTING_CASH","STEAL_MONEY","TERRAIN_RESOURCE","TRANSFER_ASSETS","UNKNOWN"};
    static readonly int[] MoneyTransactionCategoryValues={22,4,6,16,9,3,7,26,11,8,17,2,27,10,28,19,24,23,15,20,18,5,14,12,21,1,29,13,25,0};
    static readonly string[] GameLODTypeNames={"CUSTOM","HIGH","LOW","MEDIUM","ULTRA_HIGH","UNKNOWN","VERY_LOW"};
    static readonly int[] GameLODTypeValues={5,3,1,2,4,-1,0};
    static XmlDocument DeviceAttr(string name,string value){XmlDocument d=Parse(Root(Graphics,"","<Device/>"));((XmlElement)d.GetElementsByTagName("Device",NS)[0]).SetAttribute(name,value);return d;}
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        for(int i=0;i<Types.Length;++i){AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");for(int at=0;at<Sizes[i];at+=4)Check(Word(a.InstanceData,at,big)==(i==0&&at==4?30u:0u),"all absent words zero except Network30");Offsets(a.RelocationData,new uint[0],big,"defaults no pointers");Offsets(a.ImportsData,new uint[0],big,"no imports");Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='Ignored'",""),p);
            Reject("unknown attr",Root(Types[i],"Unknown='1'",""));Reject("qualified attr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("child",Root(Types[i],"","<Unknown/>"));Reject("text",Root(Types[i],"","payload"));Reject("foreign child",Root(Types[i],"","<Device xmlns='urn:q'/>"));}
        List<string> integers=new List<string>(Numbers);integers.AddRange(new[]{"-4294967296","-4294967297","18446744073709551615","-18446744073709551615","0Xff","08","0b10","+ 1","--1","2\0tail","\0-5","-999999999999999999999"});
        foreach(string value in integers){TestDoc("network signed",Attr(Network,"RunAheadSlack",value),p);foreach(string field in PathFields)TestDoc("path unsigned/"+field,Attr(Pathfind,field,value),p);foreach(string field in new[]{"VendorId","DeviceId"})TestDoc("device unsigned/"+field,DeviceAttr(field,value),p);}
        XmlDocument allPath=Parse(Root(Pathfind,"",""));for(int i=0;i<PathFields.Length;++i)allPath.DocumentElement.SetAttribute(PathFields[i],(0x12340000u+(uint)i).ToString(CultureInfo.InvariantCulture));AssetBuffer path=TestDoc("ABI/full Pathfind",allPath,p);Check(path.InstanceData.Length==64,"path root64");for(int i=0;i<15;++i)Check(Word(path.InstanceData,4+4*i,big)==0x12340000u+(uint)i,"15 full fields exact ordinal offsets");
        AssetBuffer network=Test("ABI/network",Root(Network,"RunAheadSlack='-17'",""),p);Check(Word(network.InstanceData,4,big)==0xffffffefu,"signed Network offset4");
        for(int i=0;i<MoneyTransactionCategoryNames.Length;++i){AssetBuffer a=TestDoc("money category",Attr(Resource,"SharedMoneyTransactionCategories",MoneyTransactionCategoryNames[i]),p);Check(Word(a.InstanceData,4,big)==(1u<<MoneyTransactionCategoryValues[i]),"independent transaction30 bit map");TestDoc("money NUL",Attr(Resource,"SharedMoneyTransactionCategories",MoneyTransactionCategoryNames[i]+"\0tail"),p);a=TestDoc("money XOR",Attr(Resource,"SharedMoneyTransactionCategories","+"+MoneyTransactionCategoryNames[i]+" -"+MoneyTransactionCategoryNames[i]+" -"+MoneyTransactionCategoryNames[i]),p);Check(Word(a.InstanceData,4,big)==(1u<<MoneyTransactionCategoryValues[i]),"removal XOR toggle twice");}
        foreach(string value in new[]{"","ALL","all","+ALL","-ALL","ALL -HARVESTER","unknown","+unknown -unknown","+","-","\0HARVESTER","HARVESTER\0 PILLAGE","é","東京"})TestDoc("resource bit boundaries",Attr(Resource,"SharedMoneyTransactionCategories",value),p);
        AssetBuffer resource=TestDoc("ABI/resource ALL",Attr(Resource,"SharedMoneyTransactionCategories","ALL"),p);Check(Word(resource.InstanceData,4,big)==UInt32.MaxValue,"ALL includes two padding bits");resource=TestDoc("ABI/resource fallback",Attr(Resource,"SharedMoneyTransactionCategories","unknown"),p);Check(Word(resource.InstanceData,4,big)==(1u<<22),"unknown uses AI_MONEY_LENDER22");
        foreach(string field in new[]{"Level","MacLevel"}){
            for(int i=0;i<GameLODTypeNames.Length;++i){AssetBuffer a=TestDoc("LOD/"+field,DeviceAttr(field,GameLODTypeNames[i]),p);int at=field=="Level"?28:(int)Word(a.InstanceData,32,big);Check(Word(a.InstanceData,at,big)==unchecked((uint)GameLODTypeValues[i]),"GameLOD7 numeric values incl UNKNOWN-1");if(field=="MacLevel")Check(a.InstanceData.Length==40&&at==36,"nullable MacLevel present allocation4");TestDoc("LOD NUL/"+field,DeviceAttr(field,GameLODTypeNames[i]+"\0tail"),p);}
            foreach(string value in new[]{"","unknown","low"," LOW","LOW ","0","unknown\0LOW"}){AssetBuffer a=TestDoc("LOD fallback/"+field,DeviceAttr(field,value),p);int at=field=="Level"?28:(int)Word(a.InstanceData,32,big);Check(Word(a.InstanceData,at,big)==5,"unknown present CUSTOM5, absent remains zero/null");}
        }
        foreach(string value in new[]{"","ABC","A\0B","\0ABC","a b c","é","東京",new string('x',1024)})TestDoc("device Name",DeviceAttr("Name",value),p);
        AssetBuffer graphics=Test("ABI/graphics",Root(Graphics,"","<Device VendorId='0x1234' DeviceId='0x5678' Name='ABC' Level='MEDIUM' MacLevel='HIGH'/><Device Name='' MacLevel='UNKNOWN'/><Device MacLevel=''/>"),p);
        Check(graphics.InstanceData.Length==104&&Word(graphics.InstanceData,4,big)==3&&Word(graphics.InstanceData,8,big)==12,"graphics root12/list24/all dynamic allocs");Check(Word(graphics.InstanceData,12,big)==0x1234&&Word(graphics.InstanceData,16,big)==0x5678&&Word(graphics.InstanceData,20,big)==3&&Word(graphics.InstanceData,24,big)==84&&Word(graphics.InstanceData,28,big)==2&&Word(graphics.InstanceData,32,big)==88,"first Device complete field offsets");Check(Word(graphics.InstanceData,44,big)==0&&Word(graphics.InstanceData,48,big)==92&&Word(graphics.InstanceData,56,big)==96&&Word(graphics.InstanceData,72,big)==0&&Word(graphics.InstanceData,80,big)==100,"presentempty string vs absent and nullable MacLevel");Check(Word(graphics.InstanceData,88,big)==3&&Word(graphics.InstanceData,96,big)==UInt32.MaxValue&&Word(graphics.InstanceData,100,big)==5,"MacLevel exact scalar child values");Check(graphics.InstanceData[84]==65&&graphics.InstanceData[85]==66&&graphics.InstanceData[86]==67&&graphics.InstanceData[87]==0,"string payload bytes");Offsets(graphics.RelocationData,new uint[]{8,24,32,48,56,80},big,"root-owner then Device list target-block order");Offsets(graphics.ImportsData,new uint[0],big,"devices no imports");
        foreach(int count in new[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder items=new StringBuilder();for(int i=0;i<count;++i)items.Append("<Device VendorId='1' DeviceId='2' Name='X' Level='LOW' MacLevel='HIGH'/>");AssetBuffer a=Test("device growth",Root(Graphics,"",items.ToString()),p);Check(a.InstanceData.Length==12+32*count&&Word(a.InstanceData,4,big)==count,"Device24 plus padded4 string plus Mac4");}
        Test("nested metadata",Root(Graphics,"","<Device TypeId='1'/>"),p);foreach(string child in new[]{"<Device Unknown='1'/>","<Device>text</Device>","<Device><Unknown/></Device>"})Reject("device unsupported",Root(Graphics,"",child));Reject("wrong root field",Root(Graphics,"RunAheadSlack='1'",""));Reject("unknown root",Root("Unknown","",""));
        bool rejected=false;try{XmlsystemsettingsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(Network,"",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName=Resource;rejected=false;try{XmlsystemsettingsCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"mismatch TypeId");++rejections;
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
            Console.WriteLine("PASS System settings fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

