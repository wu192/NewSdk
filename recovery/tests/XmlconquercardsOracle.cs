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
class XmlconquercardsOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Base="ConquerCardDefinitionBase",Unit="ConquerCardDefinitionUnit",Structure="ConquerCardDefinitionStructure",Power="ConquerCardDefinitionPower",Deck="ConquerCardPrebuiltDeck";
    static readonly string[] Types={Base,Unit,Structure,Power,Deck};
    static readonly uint[] TypeIds={3258480561u,3562795973u,4289687760u,4164089898u,2806522526u};
    static readonly int[] Sizes={12,32,16,24,36};
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
            Type type=typeof(XmlconquercardsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlconquercardsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] EVeterancyLevel={"ELITE","HEROIC","INVALID","REGULAR","VETERAN"};
    static readonly int[] VVeterancyLevel={2,3,-1,0,1};
    static XmlDocument CardSet(string path,string attr,string value){XmlDocument d=Parse(Root(Deck,"","<Cards><Unit TypeId='849812912'/></Cards>"));XmlElement at=(XmlElement)d.DocumentElement.FirstChild.FirstChild;if(path.Length>0){XmlElement child=d.CreateElement(path,NS);at.AppendChild(child);at=child;}at.SetAttribute(attr,value);return d;}
    static void Attr(string root,string path,string key,IEnumerable<string> values,TargetPlatform p){foreach(string value in values)TestDoc("attr/"+root+"/"+path+"/"+key+"/"+value.Length,Set(root,path,key,value),p);}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){AssetBuffer rootDefault=Test("ABI/defaults",Root(Types[i],"",""),p);Check(rootDefault.InstanceData.Length==Sizes[i],"independent root size");foreach(byte value in rootDefault.InstanceData)Check(value==0,"all default definition/deck roots zero");Check(rootDefault.RelocationData.Length==0&&rootDefault.ImportsData.Length==0,"default roots no bookmarks");}
        AssetBuffer u=Test("ABI/definitionunit",Root(Unit,"Cost='-1' Maximum='5' Unit='U\\9'","<Veterancy Veterancy='VETERAN' Cost='3'/><Upgrade Upgrade='U\\9' Cost='1'/><Veterancy Veterancy='HEROIC' Cost='4'/><Upgrade Upgrade='U\\1' Cost='-2'/>"),p);Check(u.InstanceData.Length==64&&Word(u.InstanceData,4,big)==UInt32.MaxValue&&Word(u.InstanceData,8,big)==5&&Word(u.InstanceData,12,big)==10&&Word(u.InstanceData,16,big)==2&&Word(u.InstanceData,20,big)==32&&Word(u.InstanceData,24,big)==2&&Word(u.InstanceData,28,big)==48,"Unit32 base12 strongref and two list8 fields");Check(Word(u.InstanceData,32,big)==10&&Word(u.InstanceData,36,big)==1&&Word(u.InstanceData,40,big)==2&&Word(u.InstanceData,44,big)==0xfffffffeu,"Upgrade8 strongref/signedcost");Check(Word(u.InstanceData,48,big)==1&&Word(u.InstanceData,52,big)==3&&Word(u.InstanceData,56,big)==3&&Word(u.InstanceData,60,big)==4,"Veterancy8 enum/cost");Offsets(u.RelocationData,new uint[]{20,28},big,"Upgrade list allocated before Veterancy regardless of XML order");Offsets(u.ImportsData,new uint[]{12,40,32},big,"definition root then upgradeowner/index order");
        AssetBuffer power=Test("ABI/power",Root(Power,"Cost='7' Maximum='2' Power='P\\1' Side='Ab'",""),p);Check(power.InstanceData.Length==28&&Word(power.InstanceData,4,big)==7&&Word(power.InstanceData,8,big)==2&&Word(power.InstanceData,12,big)==2&&Word(power.InstanceData,16,big)==2&&Word(power.InstanceData,20,big)==24&&power.InstanceData[24]==65&&power.InstanceData[25]==98,"Power24 Side is String8 preserving case, not AssetId hash");Offsets(power.RelocationData,new uint[]{20},big,"Power Side string");Offsets(power.ImportsData,new uint[]{12},big,"Power only reference");
        AssetBuffer d=Test("ABI/deck",Root(Deck,"DisplayName='A' Description='' Side='Japan'","<Cards><Unit TypeId='849812912' Card='C\\9' Count='2' Veterancy='ELITE'><Upgrade Upgrade='U\\1'/><Upgrade Upgrade='U\\5'/></Unit><Structure Card='C\\3'/><Power TypeId='4294967295' Card='C\\7'/></Cards>"),p);Check(d.InstanceData.Length==112&&Word(d.InstanceData,8,big)==36&&Word(d.InstanceData,16,big)==40&&Word(d.InstanceData,24,big)==44&&Word(d.InstanceData,28,big)==3&&Word(d.InstanceData,32,big)==52,"Deck36 string8x3 then pointerlist4");Check(Word(d.InstanceData,52,big)==64&&Word(d.InstanceData,56,big)==96&&Word(d.InstanceData,60,big)==104,"polymorphic targets allocated sequentially after pointerlist");Check(Word(d.InstanceData,64,big)==849812912u&&Word(d.InstanceData,68,big)==10&&Word(d.InstanceData,72,big)==2&&Word(d.InstanceData,76,big)==2&&Word(d.InstanceData,80,big)==2&&Word(d.InstanceData,84,big)==88&&Word(d.InstanceData,88,big)==2&&Word(d.InstanceData,92,big)==6,"concrete ConquerCardUnit24 type/ref/count/vet/Upgrade4list");Check(Word(d.InstanceData,96,big)==0&&Word(d.InstanceData,100,big)==4&&Word(d.InstanceData,104,big)==0&&Word(d.InstanceData,108,big)==8,"original absent/unknown TypeId branch allocates BaseCard8, writes TypeId0 and preserves strongCard");Offsets(d.RelocationData,new uint[]{8,16,24,32,52,56,60,84},big,"Deck root then pointerlistowner then Unitowner relocation ordering");Offsets(d.ImportsData,new uint[]{68,88,92,100,108},big,"Card and upgrades imports follow owningblock before numeric reference values");
        AssetBuffer empty=Test("ABI/unitdefaults",Root(Deck,"","<Cards><Unit TypeId='849812912'/></Cards>"),p);Check(empty.InstanceData.Length==64&&Word(empty.InstanceData,28,big)==1&&Word(empty.InstanceData,32,big)==36&&Word(empty.InstanceData,36,big)==40&&Word(empty.InstanceData,40,big)==849812912u&&Word(empty.InstanceData,48,big)==1&&Word(empty.InstanceData,52,big)==0,"concrete defaultCount1 Regular0");AssetBuffer fallback=Test("ABI/basefallback",Root(Deck,"","<Cards><Anything TypeId='UNKNOWN' Card='C\\1'/></Cards>"),p);Check(fallback.InstanceData.Length==48&&Word(fallback.InstanceData,40,big)==0&&Word(fallback.InstanceData,44,big)==2,"actual original unknown-polymorphic base8 branch distinct from unsupported root fallback");
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){Test("default",Root(Types[i],"",""),p);Test("metadata",Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='parent'",""),p);Reject("rootattr",Root(Types[i],"Unknown='1'",""));Reject("rootchild",Root(Types[i],"","<Unknown/>"));Reject("roottext",Root(Types[i],"","payload"));Reject("qualifiedrootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("foreignchild",Root(Types[i],"","<Upgrade xmlns='urn:q'/>"));}
        foreach(string root in new string[]{Base,Unit,Structure,Power})foreach(string attr in new string[]{"Cost","Maximum"})Attr(root,"",attr,Scalars,p);Attr(Unit,"Upgrade","Cost",Scalars,p);Attr(Unit,"Veterancy","Cost",Scalars,p);Attr(Unit,"","Unit",References,p);Attr(Structure,"","Structure",References,p);Attr(Power,"","Power",References,p);Attr(Unit,"Upgrade","Upgrade",References,p);Attr(Power,"","Side",Strings,p);foreach(string attr in new string[]{"DisplayName","Description","Side"})Attr(Deck,"",attr,Strings,p);foreach(string value in Scalars)TestDoc("unitcount",CardSet("","Count",value),p);foreach(string value in References){TestDoc("unitcardref",CardSet("","Card",value),p);TestDoc("unitupgraderef",CardSet("Upgrade","Upgrade",value),p);TestDoc("basecardref",Set(Deck,"Cards/Power","Card",value),p);}
        for(int i=0;i<EVeterancyLevel.Length;++i){AssetBuffer def=TestDoc("definitionvetexact",Set(Unit,"Veterancy","Veterancy",EVeterancyLevel[i]),p);Check(Word(def.InstanceData,32,big)==unchecked((uint)VVeterancyLevel[i]),"original definition Veterancy PE enumvalue");AssetBuffer card=TestDoc("cardvetexact",CardSet("","Veterancy",EVeterancyLevel[i]),p);Check(Word(card.InstanceData,52,big)==unchecked((uint)VVeterancyLevel[i]),"original card Veterancy PE enumvalue");foreach(string value in new string[]{EVeterancyLevel[i].ToLowerInvariant()," "+EVeterancyLevel[i],EVeterancyLevel[i]+" ",EVeterancyLevel[i]+"\0tail","+"+EVeterancyLevel[i],"-"+EVeterancyLevel[i],"","UNKNOWN"}){TestDoc("definitionvetboundary",Set(Unit,"Veterancy","Veterancy",value),p);TestDoc("cardvetboundary",CardSet("","Veterancy",value),p);}}
        foreach(string value in new string[]{"","0","-1","1","849812911","849812912","849812913","0x32a71fb0","0x32a71db0","0X32A71DB0","+849812912"," 849812912junk","garbage","4294967295","4294967296","-2147483649","077","999999999999999999999","849812912\0tail","\0849812912"}){XmlDocument doc=Set(Deck,"Cards/Anything","TypeId",value);AssetBuffer b=TestDoc("polymorphic-TypeId",doc,p);bool concrete=value=="849812912"||value=="849812912\0tail"||value=="0x32a71db0"||value=="0X32A71DB0"||value=="+849812912"||value==" 849812912junk";Check(b.InstanceData.Length==(concrete?64:48),"knownconcrete24 versus originalunknownbase8 branchsize");Check(Word(b.InstanceData,40,big)==(concrete?849812912u:0),"output concrete tag versus canonicalbase0");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder defs=new StringBuilder(),cards=new StringBuilder(),basecards=new StringBuilder();for(int i=0;i<count;++i){defs.Append("<Veterancy Veterancy='HEROIC' Cost='7'/><Upgrade Upgrade='U\\"+(count-i)+"' Cost='-1'/>");cards.Append("<Unit TypeId='849812912' Card='C\\"+(count-i)+"'><Upgrade Upgrade='U\\"+(count-i)+"'/></Unit>");basecards.Append("<Power Card='C\\"+(count-i)+"'/>");}AssetBuffer u=Test("definitiongrowth",Root(Unit,"",defs.ToString()),p);Check(u.InstanceData.Length==32+16*count&&Word(u.InstanceData,16,big)==count&&Word(u.InstanceData,24,big)==count,"both complete8byte definition lists");AssetBuffer d=Test("deckgrowth",Root(Deck,"","<Cards>"+cards+"</Cards>"),p);Check(d.InstanceData.Length==36+32*count&&Word(d.InstanceData,28,big)==count,"pointer4+concrete24+upgrade4 growth");AssetBuffer b=Test("basegrowth",Root(Deck,"","<Cards>"+basecards+"</Cards>"),p);Check(b.InstanceData.Length==36+12*count&&Word(b.InstanceData,28,big)==count,"pointer4+BaseCard8 growth");}
        foreach(string root in new string[]{Power,Deck})foreach(int count in new int[]{0,1,3,4,7,8,15,16,27,28,31,32,255,256,257,1023,1024,4095,4096,65535})TestDoc("SideStringgrowth",Set(root,"","Side",new string('A',count)),p);
        foreach(string path in new string[]{"Upgrade","Veterancy"}){TestDoc("definitionmetadata",Set(Unit,path,"TypeId","1"),p);Reject("definitionunknown",Set(Unit,path,"Unknown","1").OuterXml);}TestDoc("cardsmetadata",Set(Deck,"Cards","TypeId","1"),p);Reject("cardsattr",Set(Deck,"Cards","Unknown","1").OuterXml);Reject("unitattr",CardSet("","Unknown","1").OuterXml);Reject("upgradeattr",CardSet("Upgrade","Unknown","1").OuterXml);Reject("duplicateCards",Root(Deck,"","<Cards/><Cards/>"));Reject("cardstext",Root(Deck,"","<Cards>payload</Cards>"));Reject("cardtext",Root(Deck,"","<Cards><Unit TypeId='849812912'>payload</Unit></Cards>"));Reject("unknownunitmember",Root(Deck,"","<Cards><Unit TypeId='849812912'><Unknown/></Unit></Cards>"));Reject("baseunknownmember",Root(Deck,"","<Cards><Power><Unknown/></Power></Cards>"));Reject("baseunknownattr",Root(Deck,"","<Cards><Power Count='2'/></Cards>"));Reject("qualifiedcardattr",Root(Deck,"","<Cards><Unit TypeId='849812912' xmlns:q='urn:q' q:Card='1'/></Cards>"));Reject("foreigncard",Root(Deck,"","<Cards><Unit xmlns='urn:q'/></Cards>"));Reject("unknownroot",Root("Unknown","",""));bool nil=false;try{XmlconquercardsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Unit,"","")));dec.Handle.TypeName=Deck;bool wrong=false;try{XmlconquercardsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
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
            Console.WriteLine("PASS Conquer cards fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

