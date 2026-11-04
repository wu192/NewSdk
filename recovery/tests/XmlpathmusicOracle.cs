using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Security;
using BinaryAssetBuilder.Core;
partial class XmlpathmusicOracle {
 const string Ns="uri:ea.com:eala:asset";
 static int fixtures,comparisons,direct,rejections,layouts;
 static readonly string[] Roots={"PathMusicGameDynamicState","PathMusicGameDynamicStateSet","PathMusicGameDynamicTransition","PathMusicEventRuntime","PathMusicEventSet","PathMusicMapRuntime","MusicPalette"};
 static readonly uint[] TypeIds={97402485u,343094706u,2343360052u,1122327197u,3339496789u,2026048286u,2407383451u};
 static readonly int[] Sizes={20,16,12,16,16,12,16};
 static Assembly recovered;
 static string Escape(string s){return SecurityElement.Escape(s);}
 static string Xml(string root,string attrs,string children){return "<"+root+" xmlns='"+Ns+"' id='Oracle' "+attrs+">"+children+"</"+root+">";}
 static InstanceDeclaration Declaration(XmlDocument xml){var ns=new XmlNamespaceManager(xml.NameTable);ns.AddNamespace("ea",Ns);var doc=new AssetDeclarationDocument();var t=typeof(AssetDeclarationDocument).GetNestedType("CurrentState",BindingFlags.NonPublic);var state=FormatterServices.GetUninitializedObject(t);t.GetField("NamespaceManager").SetValue(state,ns);typeof(AssetDeclarationDocument).GetField("_Current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(doc,state);var x=new InstanceDeclaration(doc);x.XmlNode=xml.DocumentElement;return x;}
 static AssetBuffer Recovered(XmlDocument d,int root,TargetPlatform platform,bool compile){var t=recovered.GetType("Recovered."+Roots[root]+"Compiler",true);try{if(!compile)return (AssetBuffer)t.GetMethod("ProcessInstance").Invoke(null,new object[]{Declaration(d),platform});var ns=new XmlNamespaceManager(d.NameTable);ns.AddNamespace("ea",Ns);return (AssetBuffer)t.GetMethod("CompileXml").Invoke(null,new object[]{d.DocumentElement,ns,platform!=TargetPlatform.Win32});}catch(TargetInvocationException e){throw e.InnerException;}}
 static uint Word(byte[] b,int at,bool big){return big?((uint)b[at]<<24)|((uint)b[at+1]<<16)|((uint)b[at+2]<<8)|b[at+3]:BitConverter.ToUInt32(b,at);}
 static uint FB(float f){return BitConverter.ToUInt32(BitConverter.GetBytes(f),0);}
 static void Equal(string label,byte[] a,byte[] b){if(a==null||b==null||a.Length!=b.Length)throw new Exception(label+" length original="+(a==null?-1:a.Length)+" recovered="+(b==null?-1:b.Length));for(int i=0;i<a.Length;++i)if(a[i]!=b[i])throw new Exception(label+" byte="+i+" original="+a[i]+" recovered="+b[i]);}
 static void Tables(AssetBuffer b,bool big){foreach(byte[] a in new[]{b.RelocationData,b.ImportsData}){if(a.Length==0)continue;if(a.Length%4!=0||Word(a,a.Length-4,big)!=UInt32.MaxValue)throw new Exception("Table terminator");for(int i=0;i<a.Length-4;i+=4){uint at=Word(a,i,big);if(at>b.InstanceData.Length-4)throw new Exception("Table location");if(Object.ReferenceEquals(a,b.RelocationData)&&Word(b.InstanceData,(int)at,big)>=b.InstanceData.Length)throw new Exception("Relocation target");}}}
 static AssetBuffer Test(IAssetBuilderPlugin original,int root,string label,string attrs,string children,TargetPlatform platform){var d=new XmlDocument();d.LoadXml(Xml(Roots[root],attrs,children));return TestDocument(original,root,label,d,platform);}
 static AssetBuffer TestDocument(IAssetBuilderPlugin original,int root,string label,XmlDocument d,TargetPlatform platform){original.Initialize(null,platform);var a=original.ProcessInstance(Declaration(d));var b=Recovered(d,root,platform,false);var c=Recovered(d,root,platform,true);Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relo",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);Equal(label+"/direct-instance",b.InstanceData,c.InstanceData);Equal(label+"/direct-relo",b.RelocationData,c.RelocationData);Equal(label+"/direct-imports",b.ImportsData,c.ImportsData);comparisons+=3;direct+=3;if(b.InstanceData.Length<Sizes[root]||Word(b.InstanceData,0,false)!=0)throw new Exception(label+" root ABI");Tables(b,platform!=TargetPlatform.Win32);++fixtures;++layouts;return b;}
 static void Reject(int root,string text){var d=new XmlDocument();d.LoadXml(text);try{ PluginSchemaBoundary.Validate(d); Recovered(d,root,TargetPlatform.Win32,true);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected refusal: "+text);}
 static void Expected(byte[] data,int at,uint value,bool big){for(int i=0;i<4;++i)data[at+i]=(byte)(value>>(big?24-8*i:8*i));}
 static void DefaultLayout(int root,AssetBuffer b,bool big){var expected=new byte[Sizes[root]];if(root==3)expected[12]=1;Equal("independent default ABI",expected,b.InstanceData);if(b.ImportsData.Length!=0||b.RelocationData.Length!=0)throw new Exception("Default tables must be empty");++layouts;}
 static int Main(string[] args){try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");var original=(IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[0]).GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));recovered=Assembly.LoadFrom(args[1]);foreach(int platformId in new[]{0,1,2,99}){var platform=(TargetPlatform)platformId;bool big=platformId!=0;int sf=fixtures,sc=comparisons,sd=direct,sr=rejections,sl=layouts;
 for(int root=0;root<Roots.Length;++root){DefaultLayout(root,Test(original,root,"defaults", "","",platform),big);foreach(string typeId in new[]{TypeIds[root].ToString(CultureInfo.InvariantCulture),"0x"+TypeIds[root].ToString("X8")})Test(original,root,"BAB-root", "TypeId='"+typeId+"'","",platform);Reject(root,Xml(Roots[root],"Unknown='x'",""));Reject(root,Xml(Roots[root],"xmlns:x='other' x:TypeId='1'",""));if(root!=5)Reject(root,Xml(Roots[root],"","text"));Reject(root,Xml(Roots[root],"","<Unknown/>"));Reject(root,"<"+Roots[root]+" xmlns='other'/>");}
 Cases(original,platform);
 Console.WriteLine("PASS platform={0} fixtures={1} threeArrayComparisons={2} directArrayComparisons={3} rejections={4} independentLayouts={5}",platformId,fixtures-sf,comparisons-sc,direct-sd,rejections-sr,layouts-sl);}
 Console.WriteLine("RESULT fixtures={0} threeArrayComparisons={1} directArrayComparisons={2} rejections={3} independentLayouts={4}",fixtures,comparisons,direct,rejections,layouts);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}


