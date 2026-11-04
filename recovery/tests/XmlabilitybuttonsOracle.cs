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
class XmlabilitybuttonsOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Unit="UnitAbilityButtonTemplate",Player="PlayerPowerButtonTemplate";
    static readonly string[] Types={Unit,Player};
    static readonly uint[] TypeIds={3517887782u,4044322798u};
    static readonly int[] Sizes={12,12};
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
            Type type=typeof(XmlabilitybuttonsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlabilitybuttonsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" ownerblock/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static XmlDocument Attr(string type,string key,string value){XmlDocument d=Parse(Root(type,"",""));d.DocumentElement.SetAttribute(key,value);return d;}
    static readonly object[][] Variants={
        new object[]{"BuildWallUnitAbilityButtonTemplateData",826563063u,8,"Single",false},
        new object[]{"ObjectUpgradeUnitAbilityButtonTemplateData",694619649u,8,"Single",false},
        new object[]{"SpecialPowerUnitAbilityButtonTemplateData",230292782u,8,"Single",false},
        new object[]{"AssembleStructureUnitAbilityButtonTemplateData",935244829u,8,"Single",false},
        new object[]{"EvacuateUnitAbilityButtonTemplateData",1397188496u,8,"Single",false},
        new object[]{"ProduceStructureUnitAbilityButtonBaseTemplateData",1257363675u,8,"Single",false},
        new object[]{"BuildUnitUnitAbilityButtonTemplateData",988387268u,4,"Empty",false},
        new object[]{"ToggleSpecialPowerUnitAbilityButtonTemplateData",1606819420u,12,"Toggle",false},
        new object[]{"UnpackUnitAbilityButtonTemplateData",2708328405u,8,"Single",false},
        new object[]{"PlayerUpgradeUnitAbilityButtonTemplateData",2581602373u,8,"Single",false},
        new object[]{"TargetedSpecialPowerUnitAbilityButtonTemplateData",2297297736u,28,"Targeted",false},
        new object[]{"SingleStateUnitAbilityButtonTemplateData",3134958229u,8,"Single",false},
        new object[]{"MultiplePowersTargetedSpecialPowerUnitAbilityButtonTemplateData",3329794654u,52,"MultiPowers",false},
        new object[]{"BaseSpecialPowerUnitAbilityButtonTemplateData",3302649080u,4,"Empty",false},
        new object[]{"TargetedPlayerPowerButtonTemplateData",2890263052u,28,"Targeted",true},
        new object[]{"MultipleTargetsTargetedPlayerPowerButtonTemplateData",2761490207u,52,"MultiTargets",true},
        new object[]{"MultiplePowersTargetedPlayerPowerButtonTemplateData",2622883154u,52,"MultiPowers",true},
    };
    static uint FloatBits(float value){return BitConverter.ToUInt32(BitConverter.GetBytes(value),0);}
    static string Data(uint type,string attrs,string children){return "<Data><Payload TypeId='"+type+"' "+attrs+">"+children+"</Payload></Data>";}
    static XmlDocument PayloadAttr(string root,uint type,string attr,string value){XmlDocument d=Parse(Root(root,"",Data(type,"","")));((XmlElement)d.GetElementsByTagName("Payload",NS)[0]).SetAttribute(attr,value);return d;}
    static XmlDocument PayloadText(string root,uint type,string tag,string value){XmlDocument d=Parse(Root(root,"",Data(type,"","<"+tag+"/>")));d.GetElementsByTagName(tag,NS)[0].InnerText=value;return d;}
    static readonly string[] TargetAttrs={"ValidTargetCursor","InvalidTargetCursor","RadiusCursor","StateData"};
    static bool IsTarget(string kind){return kind=="Targeted"||kind=="MultiPowers"||kind=="MultiTargets";}
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        foreach(string root in Types){AssetBuffer a=Test("defaults/"+root,Root(root,"",""),p);Check(a.InstanceData.Length==12,"root12");foreach(byte value in a.InstanceData)Check(value==0,"root defaults allzero");Offsets(a.RelocationData,new uint[0],big,"no data no pointer");Offsets(a.ImportsData,new uint[0],big,"default no imports");Test("empty Data",Root(root,"","<Data/>"),p);Test("metadata",Root(root,"TypeId='"+(root==Unit?3517887782u:4044322798u)+"' inheritFrom='Ignored'",""),p);foreach(string value in References)TestDoc("root reference/"+root,Attr(root,root==Unit?"LogicCommand":"SpecialPower",value),p);
            AssetBuffer unknown=Test("ABI/original unknown fallback",Root(root,"",Data(0,"","")),p);Check(unknown.InstanceData.Length==(root==Unit?16:20)&&Word(unknown.InstanceData,8,big)==12&&Word(unknown.InstanceData,12,big)==0,"documented original fallback4/8 runtime0, not a recovered concrete type");
            Reject("unknown attr",Root(root,"Unknown='1'",""));Reject("qualified attr",Root(root,"xmlns:q='urn:q' q:TypeId='1'",""));Reject("unknown child",Root(root,"","<Unknown/>"));Reject("root text",Root(root,"","payload"));Reject("foreign child",Root(root,"","<Data xmlns='urn:q'/>"));Reject("duplicate Data",Root(root,"","<Data/><Data/>"));Reject("multiple polymorphic children",Root(root,"","<Data><A/><B/></Data>"));Reject("Data attr",Root(root,"","<Data Unknown='1'/>"));Reject("Data text",Root(root,"","<Data>text</Data>"));
        }
        foreach(object[] row in Variants){string name=(string)row[0],kind=(string)row[3],root=(bool)row[4]?Player:Unit;uint type=(uint)row[1];int size=(int)row[2];bool target=IsTarget(kind);
            AssetBuffer defaults=Test("default variant/"+name,Root(root,"",Data(type,"","")),p);Check(defaults.InstanceData.Length==12+size&&Word(defaults.InstanceData,8,big)==12,"exact payload size/target");for(int at=0;at<size;at+=4){uint expected=at==0?type:target&&at==20?3u:kind=="MultiTargets"&&at==36?1u:0u;Check(Word(defaults.InstanceData,12+at,big)==expected,"independent default payload fields / runtimeId "+name+" at="+at);}Offsets(defaults.RelocationData,new uint[]{8},big,"default payload pointer only");Offsets(defaults.ImportsData,new uint[0],big,"raw Arrow/Invalid defaults do not import");
            Test("hex TypeId/"+name,Root(root,"","<Data><Payload TypeId='0x"+type.ToString("X8")+"'/></Data>"),p);
            if(kind=="Single"||target)foreach(string value in References)TestDoc("StateData/"+name,PayloadAttr(root,type,"StateData",value),p);
            if(target){
                foreach(string field in new[]{"ValidTargetCursor","InvalidTargetCursor","RadiusCursor"})foreach(string value in References)TestDoc("target reference/"+name+"/"+field,PayloadAttr(root,type,field,value),p);
                string[] names={"NONE","SELECTED","HOVER_SELECT","HOVER_ATTACK","UNKNOWN"};uint[] values={0,1,2,3,UInt32.MaxValue};for(int i=0;i<names.Length;++i){AssetBuffer a=TestDoc("outline enum/"+name,PayloadAttr(root,type,"ValidTargetOutline",names[i]),p);Check(Word(a.InstanceData,32,big)==values[i],"independent outline enum");TestDoc("outline NUL",PayloadAttr(root,type,"ValidTargetOutline",names[i]+"\0tail"),p);}foreach(string value in new[]{"","unknown","none"," NONE","NONE ","0","unknown\0NONE"}){AssetBuffer a=TestDoc("outline fallback",PayloadAttr(root,type,"ValidTargetOutline",value),p);Check(Word(a.InstanceData,32,big)==3,"outline unknown fallback HOVER_ATTACK3");}
                AssetBuffer color=Test("ABI/empty tint",Root(root,"",Data(type,"","<ValidTargetTintColor/>")),p);Check(color.InstanceData.Length==12+size+12&&Word(color.InstanceData,36,big)==12+size,"nullable12-byte RGB tint presentempty");for(int i=0;i<3;++i)Check(Word(color.InstanceData,12+size+4*i,big)==0,"empty RGB zeros");
                foreach(string channel in new[]{"R","G","B"})foreach(string value in Numbers){XmlDocument d=Parse(Root(root,"",Data(type,"","<ValidTargetTintColor/>")));((XmlElement)d.GetElementsByTagName("ValidTargetTintColor",NS)[0]).SetAttribute(channel,value);TestDoc("tint float/"+name+"/"+channel,d,p);}
                Test("tint metadata",Root(root,"",Data(type,"","<ValidTargetTintColor TypeId='1'/>")),p);Reject("duplicate tint",Root(root,"",Data(type,"","<ValidTargetTintColor/><ValidTargetTintColor/>")));Reject("tint notARGB",Root(root,"",Data(type,"","<ValidTargetTintColor A='255'/>")));Reject("tint text",Root(root,"",Data(type,"","<ValidTargetTintColor>text</ValidTargetTintColor>")));
            }
            string[] lists=kind=="Toggle"?new[]{"StateData"}:kind=="MultiPowers"?new[]{"AdditionalSpecialPowerTemplate","AdditionalRadiusCursor","AdditionalValidTargetCursor"}:kind=="MultiTargets"?new[]{"AdditionalRadiusCursor"}:new string[0];
            foreach(string tag in lists){foreach(string value in References)TestDoc("list reference/"+name+"/"+tag,PayloadText(root,type,tag,value),p);foreach(int count in new[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder items=new StringBuilder();for(int i=0;i<count;++i)items.Append("<"+tag+">R\\"+(count-i)+"</"+tag+">");AssetBuffer a=Test("listgrowth/"+name+"/"+tag+"/"+count,Root(root,"",Data(type,"",items.ToString())),p);int off=kind=="Toggle"?4:kind=="MultiTargets"?44:tag=="AdditionalSpecialPowerTemplate"?28:tag=="AdditionalRadiusCursor"?36:44;Check(a.InstanceData.Length==12+size+4*count&&Word(a.InstanceData,12+off,big)==count,"list stride4/exact count");if(count>0){Check(Word(a.InstanceData,12+off+4,big)==12+size,"sole child list target");uint[] offsets=new uint[count];for(int i=0;i<count;++i)offsets[i]=(uint)(12+size+4*(count-i-1));Offsets(a.ImportsData,offsets,big,"descending refs sorted within one list owner");}}
                Reject("list item attr",Root(root,"",Data(type,"","<"+tag+" Unknown='1'/>")));Reject("list item child",Root(root,"",Data(type,"","<"+tag+"><Unknown/></"+tag+">")));
            }
            if(kind=="MultiTargets"){
                foreach(string field in new[]{"TargetCount","MinTargetDistance"})foreach(string value in Numbers)TestDoc("multi-target scalar/"+field,PayloadAttr(root,type,field,value),p);
                foreach(string field in new[]{"TargetMarkerTemplate","TargetMarkerFXList"}){foreach(string value in new[]{"","ABC","abc","Name\\7","ABC\0tail","\0ABC","é","東京"})TestDoc("weak marker hash/"+field,PayloadAttr(root,type,field,value),p);AssetBuffer upper=TestDoc("ABI/hash upper",PayloadAttr(root,type,field,"ABC"),p),lower=TestDoc("ABI/hash lower",PayloadAttr(root,type,field,"abc"),p);int off=field=="TargetMarkerTemplate"?40:44;Check(Word(upper.InstanceData,off,big)==Word(lower.InstanceData,off,big),"weak TypedAssetId hash folds ASCII case");}
            }
            Reject("payload attr",Root(root,"",Data(type,"Unknown='1'","")));Reject("payload child",Root(root,"",Data(type,"","<Unknown/>")));Reject("payload text",Root(root,"","<Data><Payload TypeId='"+type+"'>text</Payload></Data>"));
        }
        foreach(string root in Types){uint type=root==Unit?3329794654u:2622883154u;string rootRef=root==Unit?"LogicCommand":"SpecialPower";
            AssetBuffer full=Test("ABI/complete multi-powers "+root,Root(root,rootRef+"='R\\9'",Data(type,"StateData='S\\7' ValidTargetCursor='C\\5' InvalidTargetCursor='C\\1' RadiusCursor='C\\3' ValidTargetOutline='SELECTED'","<ValidTargetTintColor R='0.25' G='0.5' B='0.75'/><AdditionalValidTargetCursor>C\\2</AdditionalValidTargetCursor><AdditionalRadiusCursor>C\\4</AdditionalRadiusCursor><AdditionalSpecialPowerTemplate>P\\9</AdditionalSpecialPowerTemplate><AdditionalSpecialPowerTemplate>P\\1</AdditionalSpecialPowerTemplate>")),p);
            Check(full.InstanceData.Length==92&&Word(full.InstanceData,4,big)==10&&Word(full.InstanceData,8,big)==12&&Word(full.InstanceData,12,big)==type,"root12+payload52+lists16+tint12");uint[] fields={8,6,2,4,1};for(int i=0;i<5;++i)Check(Word(full.InstanceData,16+4*i,big)==fields[i],"state/cursors/outline offsets");Check(Word(full.InstanceData,40,big)==2&&Word(full.InstanceData,44,big)==64&&Word(full.InstanceData,48,big)==1&&Word(full.InstanceData,52,big)==72&&Word(full.InstanceData,56,big)==1&&Word(full.InstanceData,60,big)==76&&Word(full.InstanceData,36,big)==80,"derived lists allocated before inherited RGB tint");Check(Word(full.InstanceData,80,big)==FloatBits(0.25f)&&Word(full.InstanceData,84,big)==FloatBits(0.5f)&&Word(full.InstanceData,88,big)==FloatBits(0.75f),"RGB three floats");Offsets(full.RelocationData,new uint[]{8,44,52,60,36},big,"ownerblock/targetblock order differs from field offset");Offsets(full.ImportsData,new uint[]{4,24,28,20,16,68,64,72,76},big,"owner root then payload then lists, each sorted reference index");
        }
        AssetBuffer multi=Test("ABI/multiple targets",Root(Player,"",Data(2761490207u,"TargetMarkerTemplate='ABC' TargetMarkerFXList='FX' TargetCount='3' MinTargetDistance='2.5'","<ValidTargetTintColor R='1' G='2' B='3'/><AdditionalRadiusCursor>C\\7</AdditionalRadiusCursor><AdditionalRadiusCursor>C\\1</AdditionalRadiusCursor>")),p);Check(multi.InstanceData.Length==84&&Word(multi.InstanceData,48,big)==3&&Word(multi.InstanceData,52,big)==FloatBits(2.5f)&&Word(multi.InstanceData,56,big)==2&&Word(multi.InstanceData,60,big)==64&&Word(multi.InstanceData,36,big)==72,"multitarget52+radius8 before tint12 complete offsets");Offsets(multi.RelocationData,new uint[]{8,60,36},big,"multiple targets derived allocation before tint");Offsets(multi.ImportsData,new uint[]{68,64},big,"weak marker hashes do not import");
        AssetBuffer basePlayer=Test("ABI/base Power",Root(Player,"",Data(0,"StateData='S\\7'","")),p);Check(basePlayer.InstanceData.Length==20&&Word(basePlayer.InstanceData,12,big)==0&&Word(basePlayer.InstanceData,16,big)==8,"documented original base Power fallback8/stateData");
        Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlabilitybuttonsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(Unit,"",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName=Player;rejected=false;try{XmlabilitybuttonsCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"mismatch TypeId");++rejections;
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
            Console.WriteLine("PASS Ability buttons fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

