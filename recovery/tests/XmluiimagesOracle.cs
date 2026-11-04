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
class XmluiimagesOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"ImageSequence","UnitTypeIcon","DrawGroupInfo"};
    static readonly uint[] TypeIds={565855655u,3614134471u,2913856066u};
    static readonly int[] Sizes={12,12,52};
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
            original.Initialize(null,p);InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmluiimagesCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmluiimagesCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static byte[] AnsiCString(string text){IntPtr memory=System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(text);try{int length=0;while(System.Runtime.InteropServices.Marshal.ReadByte(memory,length)!=0)++length;byte[] bytes=new byte[length+1];System.Runtime.InteropServices.Marshal.Copy(memory,bytes,0,bytes.Length);return bytes;}finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);}}
    const string Sequence="ImageSequence",Icon="UnitTypeIcon",Draw="DrawGroupInfo";
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        foreach(string t in new string[]{Sequence,Icon}){AssetBuffer d=Test("ABI/default/"+t,Root(t,"",""),p);Check(d.InstanceData.Length==12,"empty12");for(int at=0;at<12;at+=4)Check(Word(d.InstanceData,at,big)==0,"default zeros");Offsets(d.RelocationData,new uint[0],big,"default no pointers");Offsets(d.ImportsData,new uint[0],big,"default no imports");}
        AssetBuffer s=Test("ABI/sequence",Root(Sequence,"","<Image>I\\9</Image><Image>I\\1</Image><Image>I\\5</Image>"),p);Check(s.InstanceData.Length==24&&Word(s.InstanceData,4,big)==3&&Word(s.InstanceData,8,big)==12&&Word(s.InstanceData,12,big)==10&&Word(s.InstanceData,16,big)==2&&Word(s.InstanceData,20,big)==6,"sequence list4 index+1");Offsets(s.RelocationData,new uint[]{8},big,"sequence pointer");Offsets(s.ImportsData,new uint[]{16,20,12},big,"sequence imports by index");
        AssetBuffer icon=Test("ABI/icon",Root(Icon,"Image='I\\7'","<Offset x='0.5' y='-2'/>"),p);Check(icon.InstanceData.Length==20&&Word(icon.InstanceData,4,big)==8&&Word(icon.InstanceData,8,big)==12&&Word(icon.InstanceData,12,big)==0x3f000000u&&Word(icon.InstanceData,16,big)==0xc0000000u,"icon12 nullablefloat2@12");Offsets(icon.RelocationData,new uint[]{8},big,"icon offsetptr");Offsets(icon.ImportsData,new uint[]{4},big,"icon image");AssetBuffer emptyOffset=Test("ABI/emptyOffset",Root(Icon,"","<Offset/>"),p);Check(emptyOffset.InstanceData.Length==20&&Word(emptyOffset.InstanceData,12,big)==0&&Word(emptyOffset.InstanceData,16,big)==0,"presentempty offset alloc8 defaults0");
        AssetBuffer dflt=Test("ABI/defaultDraw",Root(Draw,"",""),p);Check(dflt.InstanceData.Length==60&&Word(dflt.InstanceData,4,big)==5&&Word(dflt.InstanceData,8,big)==52&&Word(dflt.InstanceData,12,big)==12,"draw52 defaultArial padded8");Check(Encoding.ASCII.GetString(dflt.InstanceData,52,5)=="Arial"&&dflt.InstanceData[57]==0&&dflt.InstanceData[58]==0&&dflt.InstanceData[59]==0,"Arial nullandpadding");Check(Word(dflt.InstanceData,16,big)==UInt32.MaxValue&&Word(dflt.InstanceData,20,big)==UInt32.MaxValue,"default shadow -1");Check(dflt.InstanceData[48]==0&&dflt.InstanceData[49]==1&&dflt.InstanceData[50]==0&&dflt.InstanceData[51]==0,"bool48/49/50 padding51");for(int at=24;at<48;at+=4)Check(Word(dflt.InstanceData,at,big)==0,"other default words0");Offsets(dflt.RelocationData,new uint[]{8},big,"default fontptr");
        AssetBuffer draw=Test("ABI/fullDraw",Root(Draw,"FontName='abc' FontSize='17' IsBold='true' UsePlayerColor='false' PixelOffsetAsPercentage='true' DropShadowOffsetX='-3' DropShadowOffsetY='7' PixelOffetXInteger='-9' PixelOffetYInteger='11' PixelOffetXReal='0.5' PixelOffetYReal='-2'","<TextDropShadowColor R='1' G='2' B='3'/><TextColor R='17' G='34' B='51' A='68'/>"),p);Check(draw.InstanceData.Length==56&&Word(draw.InstanceData,4,big)==3&&Word(draw.InstanceData,8,big)==52&&Word(draw.InstanceData,12,big)==17,"drawfont and fontsize");uint[] values={unchecked((uint)-3),7,unchecked((uint)-9),11,0x3f000000u,0xc0000000u,0x44112233u,0xff010203u};for(int i=0;i<values.Length;++i)Check(Word(draw.InstanceData,16+4*i,big)==values[i],"draw scalaroffset "+i);Check(draw.InstanceData[48]==1&&draw.InstanceData[49]==0&&draw.InstanceData[50]==1&&draw.InstanceData[51]==0,"draw bool bytes no endian");
        AssetBuffer empty=Test("ABI/emptyFont",Root(Draw,"FontName=''","<TextColor/><TextDropShadowColor/>"),p);Check(empty.InstanceData.Length==56&&Word(empty.InstanceData,4,big)==0&&Word(empty.InstanceData,8,big)==52&&Word(empty.InstanceData,40,big)==0xff000000u&&Word(empty.InstanceData,44,big)==0xff000000u,"presentempty fontandcolors");
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        foreach(string v in References){XmlDocument d=Parse(Root(Sequence,"","<Image/>"));d.DocumentElement.FirstChild.InnerText=v;TestDoc("sequence refs",d,p);TestDoc("icon refs",Set(Icon,"","Image",v),p);}
        foreach(string attr in new string[]{"x","y"})foreach(string v in Numbers)TestDoc("icon offset",Set(Icon,"Offset",attr,v),p);
        foreach(string attr in new string[]{"FontSize","DropShadowOffsetX","DropShadowOffsetY","PixelOffetXInteger","PixelOffetYInteger","PixelOffetXReal","PixelOffetYReal"})foreach(string v in Numbers)TestDoc("draw scalar/"+attr,Set(Draw,"",attr,v),p);
        foreach(string attr in new string[]{"IsBold","UsePlayerColor","PixelOffsetAsPercentage"})foreach(string v in new string[]{"true","false","TRUE","FALSE","1","0","-1","t","T","yes",""," true","true\0false","1junk","2"})TestDoc("drawbool",Set(Draw,"",attr,v),p);
        foreach(string path in new string[]{"TextColor","TextDropShadowColor"})foreach(string attr in new string[]{"R","G","B","A"})foreach(string v in Numbers)TestDoc("packed color/"+path+"/"+attr,Set(Draw,path,attr,v),p);
        List<string> texts=new List<string>(References);texts.AddRange(new string[]{"Arial","MIXEDCase","abc\0tail","\0abc","é","东京","😀"});foreach(int count in new int[]{0,1,2,3,4,5,6,7,8,15,16,17,27,28,29,31,32,33,64,257,1024,4097})texts.Add(new string('x',count));foreach(string v in texts){AssetBuffer font=TestDoc("fontstring/"+v.Length,Set(Draw,"","FontName",v),p);byte[] ansi=AnsiCString(v);int length=ansi.Length-1;Check(Word(font.InstanceData,4,big)==length&&font.InstanceData.Length==52+((length+4)&~3),"font exact Windows ANSI C-string length andpadding");for(int i=0;i<ansi.Length;++i)Check(font.InstanceData[52+i]==ansi[i],"independent ANSI fontbytes andNUL");for(int i=52+ansi.Length;i<font.InstanceData.Length;++i)Check(font.InstanceData[i]==0,"independent fontalignment padding");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder list=new StringBuilder();for(int i=0;i<count;++i)list.Append("<Image>I\\"+(count-i)+"</Image>");AssetBuffer b=Test("sequence growth",Root(Sequence,"",list.ToString()),p);Check(b.InstanceData.Length==12+4*count&&Word(b.InstanceData,4,big)==count,"sequence exact list4growth");if(count>0){uint[] imports=new uint[count];for(int i=0;i<count;++i)imports[i]=(uint)(12+4*(count-i-1));Offsets(b.ImportsData,imports,big,"sequence descending refs");}}
        foreach(string t in Types){Test("metadata",Root(t,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("unknown attr",Root(t,"Unknown='1'",""));Reject("unknown child",Root(t,"","<Unknown/>"));Reject("text",Root(t,"","payload"));Reject("foreign root",Root(t,"","").Replace(NS,"urn:wrong"));Reject("qualified attr",Root(t,"xmlns:q='urn:q' q:Unknown='1'",""));}
        foreach(string[] row in new string[][]{new string[]{Sequence,"Image"},new string[]{Icon,"Offset"},new string[]{Draw,"TextColor"},new string[]{Draw,"TextDropShadowColor"}}){TestDoc("childmetadata",Set(row[0],row[1],"TypeId","1"),p);Reject("child unknownattr",Set(row[0],row[1],"Unknown","1").OuterXml);Reject("child unknownnode",Root(row[0],"","<"+row[1]+"><Unknown/></"+row[1]+">"));Reject("child foreign",Root(row[0],"","<"+row[1]+" xmlns='urn:wrong'/>"));if(row[1]!="Image"){Reject("childtext",Root(row[0],"","<"+row[1]+">payload</"+row[1]+">"));Reject("duplicate singleton",Root(row[0],"","<"+row[1]+"/><"+row[1]+"/>"));}}
        Reject("offset uppercase",Set(Icon,"Offset","X","1").OuterXml);Reject("correctedmisspelling unsupported",Root(Draw,"PixelOffsetXInteger='1'",""));Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmluiimagesCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(Icon,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Sequence;rejected=false;try{XmluiimagesCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS UI images fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

