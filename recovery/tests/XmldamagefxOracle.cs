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
class XmldamagefxOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"DamageFX","DamageFXSettings"};
    static readonly uint[] TypeIds={2905958645u,75989905u};
    static readonly int[] Sizes={12,8};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Refs={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483648","Name\\ +17junk","资源\\7","MIXEDCase"};
    static readonly string[] TimeValues={"0s","1s","250ms","1.25s","0.5m","2d1s","-1s","1s junk","2ms","1000ms","garbage","","1e30s","2147483648ms","25,5s"};
    static readonly string[] TypeValues=new string[]{"ALIEN_BOMB","ALIEN_BUZZER","ALIEN_CANNON","ALIEN_DISINTEGRATOR","ALIEN_GRENADE","ALIEN_ION","ALIEN_LASER","ALIEN_MACHINEGUN","ALIEN_MINE","ALIEN_PLASMADISK","ALIEN_RAILGUN","ALIEN_SONIC","ALIEN_TIOXIN","ALLIED_ATHENA","ALLIED_BOMB","ALLIED_BOMBLVL1","ALLIED_BOMBLVL2","ALLIED_BOMBLVL3","ALLIED_CANNON","ALLIED_CHAINGUN","ALLIED_DOG","ALLIED_FIGHTER","ALLIED_INFANTRYMISSILE","ALLIED_MISSILE","ALLIED_PAINTER","ALLIED_PARTICLE","ALLIED_PHALANX","ALLIED_PRESISIONBOMB","ALLIED_PRIMAGECANNON","ALLIED_PRIMAGECANNONVET1","ALLIED_PRIMAGECANNONVET2","ALLIED_PRISM","ALLIED_SAM","ALLIED_SCRAMBLER","ALLIED_SHOTGUN","ALLIED_SONIC","ALLIED_TANYA","CLUBBING","DEFAULT","GDI_BOMB","GDI_CANNON","GDI_FLAMETHROWER","GDI_GRENADE","GDI_ION","GDI_LASER","GDI_MACHINEGUN","GDI_MINE","GDI_MISSILE","GDI_RAILGUN","GDI_RIFLE","GDI_SHOCKWAVE","GDI_SONIC","GENERIC_FIRE","JAPAN_AIRCRAFT","JAPAN_AUTOCANNON","JAPAN_BEAM","JAPAN_BOMB","JAPAN_BONZAI","JAPAN_CANNON","JAPAN_FORCE","JAPAN_GUN","JAPAN_KAMIKAZE","JAPAN_MECHA","JAPAN_MECHAMISSILE","JAPAN_MECHARUSH","JAPAN_MISSILE","JAPAN_PLASMA","JAPAN_PSIONIC","JAPAN_ROCKET","JAPAN_RUSH","JAPAN_SHIRUKEN","JAPAN_TORPEDO","NEUTRAL_CANNON","NOD_BOMB","NOD_CANNON","NOD_FLAMETHROWER","NOD_GRENADE","NOD_HALLUCINE","NOD_ION","NOD_LASER","NOD_MACHINEGUN","NOD_MINE","NOD_MISSILE","NOD_RAILGUN","NOD_RIFLE","NOD_SEED","NOD_SONIC","POISON","RADIATION","REFLECTED","SOVIET_AK47","SOVIET_BEAR","SOVIET_BIGBOMB","SOVIET_BOMB","SOVIET_CANNON","SOVIET_DEBRIS","SOVIET_FLAK","SOVIET_FLAKVET","SOVIET_HARPOON","SOVIET_IRONCURTAIN","SOVIET_LASER","SOVIET_LEECH","SOVIET_MACHINEGUN","SOVIET_MAGNETICBOMB","SOVIET_MISSILE","SOVIET_MOLOTOV","SOVIET_ORBITAL","SOVIET_ORBITAL2","SOVIET_ORBITAL3","SOVIET_REPAIR","SOVIET_SNIPERRIFLE","SOVIET_TESLA","SOVIET_TORPEDO","UNDEFINED","UNKNOWN","default","","0"," GDI_CANNON","GDI_CANNON ","gdi_cannon"};
    static readonly string[] RefNames={"MajorFX","MinorFX","RearFX","SideFX","TopFX","VeteranMajorFX","VeteranMinorFX","VeteranRearFX","VeteranSideFX","VeteranTopFX"};
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
        uint previous=0;for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at+4<=instance.Length,label+" field bounds");Check(i==0||at>previous,label+" sorted unique offsets");previous=at;if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmldamagefxCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmldamagefxCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static void OneAttribute(string name,string value,TargetPlatform p) {
        Test("group/"+name+"/"+value,Root("DamageFX","","<FXGroup "+name+"='"+Escape(value)+"'/>"),p);
    }
    static void Abi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer empty=Test("ABI/empty-group",Root("DamageFX","","<FXGroup/>"),p);
        Check(empty.InstanceData.Length==64,"DamageFX root12/item52");
        Check(Word(empty.InstanceData,4,big)==1&&Word(empty.InstanceData,8,big)==12,"DamageFX list count/offset");
        Check(Word(empty.InstanceData,12,big)==UInt32.MaxValue&&Word(empty.InstanceData,16,big)==0&&Word(empty.InstanceData,20,big)==0,"DamageFX DEFAULT/zero defaults");
        Check(empty.RelocationData.Length==8&&Word(empty.RelocationData,0,big)==8,"DamageFX relocation offset");
        Check(empty.ImportsData.Length==0,"absent references do not import");
        string attr="Type='ALLIED_PHALANX' Amount='12.25' ThrottleTime='250ms' ";
        for(int i=0;i<10;++i)attr+=RefNames[i]+"='Fx\\"+i+"' ";
        AssetBuffer full=Test("ABI/full-group",Root("DamageFX","","<FXGroup "+attr+"/><FXGroup Type='UNKNOWN'/>"),p);
        Check(full.InstanceData.Length==116&&Word(full.InstanceData,4,big)==2,"DamageFX two items and exact size");
        Check(Word(full.InstanceData,12,big)==80&&Word(full.InstanceData,16,big)==0x41440000u&&Word(full.InstanceData,20,big)==0x3e800000u,"DamageFX enum/float/time exact offsets");
        Check(Word(full.InstanceData,64,big)==35,"unknown enum fallback ALIEN_BOMB");
        Check(full.ImportsData.Length==44,"ten imports plus sentinel");
        for(int i=0;i<10;++i) {
            Check(Word(full.InstanceData,24+4*i,big)==i+1,"DamageFX resolved reference index+1");
            Check(Word(full.ImportsData,4*i,big)==24+4*i,"DamageFX sorted import offsets");
        }
        AssetBuffer absent=Test("ABI/settings-absent",Root("DamageFXSettings","",""),p);
        Check(absent.InstanceData.Length==8&&absent.RelocationData.Length==0&&absent.ImportsData.Length==0,"Settings absent nullable reference");
        AssetBuffer present=Test("ABI/settings-present-empty",Root("DamageFXSettings","GroundDamageFX=''",""),p);
        Check(present.InstanceData.Length==12&&Word(present.InstanceData,4,big)==8&&Word(present.InstanceData,8,big)==0,"Settings present-empty allocates nullable reference block");
        Check(present.RelocationData.Length==8&&Word(present.RelocationData,0,big)==4&&present.ImportsData.Length==0,"Settings empty relocation/imports");
        AssetBuffer resolved=Test("ABI/settings-resolved",Root("DamageFXSettings","GroundDamageFX='Fx\\7'",""),p);
        Check(resolved.InstanceData.Length==12&&Word(resolved.InstanceData,8,big)==8,"Settings index+1");
        Check(resolved.ImportsData.Length==8&&Word(resolved.ImportsData,0,big)==8,"Settings import is pointed field");
    }
    static void Matrix(TargetPlatform p) {
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i) {
            AssetBuffer a=Test("default/"+Types[i],Root(Types[i],"",""),p);
            Check(a.InstanceData.Length==Sizes[i],"root exact size");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown root attribute",Root(Types[i],"Unknown='1'",""));
            Reject("qualified root attribute",Root(Types[i],"xmlns:q='urn:other' q:TypeId='1'",""));
            Reject("root text",Root(Types[i],"","payload"));
            Reject("root unknown child",Root(Types[i],"","<Unknown/>"));
            Reject("foreign root child",Root(Types[i],"","<FXGroup xmlns='urn:other'/>"));
        }
        foreach(string v in TypeValues)OneAttribute("Type",v,p);
        foreach(string v in Numbers)OneAttribute("Amount",v,p);
        foreach(string v in TimeValues)OneAttribute("ThrottleTime",v,p);
        foreach(string name in RefNames)foreach(string v in Refs)OneAttribute(name,v,p);
        foreach(string v in Refs)Test("settings/reference/"+v,Root("DamageFXSettings","GroundDamageFX='"+Escape(v)+"'",""),p);
        foreach(int count in new int[]{0,1,2,3,27,28,29,64,257}) {
            StringBuilder groups=new StringBuilder();
            for(int i=0;i<count;++i) {
                groups.Append("<FXGroup Type='GDI_CANNON' Amount='-1.25' ThrottleTime='1.5s' ");
                for(int j=0;j<10;++j)groups.Append(RefNames[j]+"='FX\\"+(i*10+j)+"' ");
                groups.Append("/>");
            }
            AssetBuffer b=Test("many-groups/"+count,Root("DamageFX","",groups.ToString()),p);
            bool big=p!=TargetPlatform.Win32;
            Check(b.InstanceData.Length==12+52*count&&Word(b.InstanceData,4,big)==count,"list size/count independent");
            Check(b.ImportsData.Length==(count==0?0:4*(10*count+1)),"reference bookmark growth");
        }
        foreach(string v in new string[]{"GDI_CANNON\0junk","DEFAULT\0junk","UNKNOWN\0GDI_CANNON"}) {
            XmlDocument d=Parse(Root("DamageFX","","<FXGroup/>"));((XmlElement)d.DocumentElement.FirstChild).SetAttribute("Type",v);TestDoc("enum-native-NUL",d,p);
        }
        foreach(string v in new string[]{"Name\0\\7","Name\\7\0tail","资源\\7","😀\\2147483648","Name\\2147483647","Name\\-2147483648","Name\\999999999999999999999"}) {
            XmlDocument d=Parse(Root("DamageFX","","<FXGroup/>"));foreach(string name in RefNames)((XmlElement)d.DocumentElement.FirstChild).SetAttribute(name,v);TestDoc("reference-native-boundaries",d,p);
            XmlDocument s=Parse(Root("DamageFXSettings","",""));s.DocumentElement.SetAttribute("GroundDamageFX",v);TestDoc("nullable-reference-native-boundaries",s,p);
        }
        Test("nested TypeId",Root("DamageFX","","<FXGroup TypeId='1'/>"),p);
        Reject("group unknown attr",Root("DamageFX","","<FXGroup Unknown='1'/>"));
        Reject("group qualified attr",Root("DamageFX","","<FXGroup xmlns:q='urn:other' q:Type='DEFAULT'/>"));
        Reject("group child",Root("DamageFX","","<FXGroup><Unknown/></FXGroup>"));
        Reject("group text",Root("DamageFX","","<FXGroup>payload</FXGroup>"));
        Reject("wrong group element",Root("DamageFX","","<DamageFXGroup/>"));
        Reject("settings child",Root("DamageFXSettings","","<GroundDamageFX/>"));
        Reject("unknown root",Root("Unknown","",""));
        XmlDocument wrong=Parse(Root("DamageFX","",""));InstanceDeclaration wrongDeclaration=Declaration(wrong);wrongDeclaration.Handle.TypeName="DamageFXSettings";
        bool mismatch=false;try{XmldamagefxCompiler.ProcessInstance(wrongDeclaration,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/TypeId mismatch rejected");++rejections;
        bool nullInput=false;try{XmldamagefxCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
        XmlDocument bare=Parse(Root("DamageFX","",""));var unboundSchemaResult=XmldamagefxCompiler.CompileXml(bare.DocumentElement,new XmlNamespaceManager(bare.NameTable),false);
        var boundSchemaResult=XmldamagefxCompiler.CompileXml(bare.DocumentElement,Namespaces(bare),false);
        Check(Convert.ToBase64String(unboundSchemaResult.InstanceData)==Convert.ToBase64String(boundSchemaResult.InstanceData) &&
            Convert.ToBase64String(unboundSchemaResult.RelocationData)==Convert.ToBase64String(boundSchemaResult.RelocationData) &&
            Convert.ToBase64String(unboundSchemaResult.ImportsData)==Convert.ToBase64String(boundSchemaResult.ImportsData),"plugin does not repeat Core namespace validation");++layouts;
        Abi(p);
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
            Console.WriteLine("PASS DamageFX fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}
