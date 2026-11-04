using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Xml;
using BinaryAssetBuilder.Core;

internal static class AudioEaLayer3Host
{
    static InstanceDeclaration Declaration(XmlElement node,string custom)
    {
        var xml=new XmlDocument();xml.LoadXml(node.OuterXml);
        var ns=new XmlNamespaceManager(xml.NameTable);ns.AddNamespace("ea","uri:ea.com:eala:asset");
        var document=new AssetDeclarationDocument();
        var current=typeof(AssetDeclarationDocument).GetNestedType("CurrentState",BindingFlags.NonPublic);
        object state=FormatterServices.GetUninitializedObject(current);
        current.GetField("NamespaceManager").SetValue(state,ns);
        typeof(AssetDeclarationDocument).GetField("_Current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(document,state);
        return new InstanceDeclaration(document){XmlNode=xml.DocumentElement,CustomDataPath=custom};
    }
    public static int Main(string[] args)
    {
        try {
            var assembly=Assembly.LoadFrom(args[0]);
            var plugin=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType(args[1],true));
            plugin.Initialize(null,TargetPlatform.Win32);
            var method=plugin.GetType().GetMethod("ProcessAudioFileInstance",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            var xml=new XmlDocument();xml.Load(args[2]);
            Directory.CreateDirectory(args[3]);
            var nodes=new System.Collections.Generic.List<XmlElement>();
            foreach(XmlNode node in xml.DocumentElement.ChildNodes) if(node is XmlElement) nodes.Add((XmlElement)node);
            Parallel.ForEach(nodes,new ParallelOptions{MaxDegreeOfParallelism=int.Parse(args[4])},delegate(XmlElement node) {
                string name=node.GetAttribute("id"), stem=Path.Combine(args[3],name);
                var declaration=Declaration(node,stem+".cdata");
                var b=(AssetBuffer)method.Invoke(plugin,new object[]{declaration});
                if(b==null || !File.Exists(stem+".cdata"))throw new Exception("Missing compiled asset: "+name);
                File.WriteAllBytes(stem+".runtime",b.InstanceData);
                File.WriteAllBytes(stem+".relo",b.RelocationData??new byte[0]);
                File.WriteAllBytes(stem+".imp",b.ImportsData??new byte[0]);
                if(node.GetAttribute("IsStreamedOnPC")=="true") {
                    int size=BitConverter.ToInt32(b.InstanceData,24),offset=BitConverter.ToInt32(b.InstanceData,20);
                    var header=new byte[size];Buffer.BlockCopy(b.InstanceData,offset,header,0,size);
                    File.WriteAllBytes(stem+".snr",header);File.Copy(stem+".cdata",stem+".sns");
                } else File.Copy(stem+".cdata",stem+".snr");
            });
            Console.WriteLine("PASS AudioFile compilation: "+nodes.Count+" assets, jobs="+args[4]);return 0;
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
