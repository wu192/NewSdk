using System;
using System.IO;
using BinaryAssetBuilder.Core;
public sealed class AudioEaLayer3SdkTrace : IAssetBuilderPlugin
{
    readonly IAssetBuilderPlugin inner=new BinaryAssetBuilder.AudioCompiler.Plugin();
    public void Initialize(object c,TargetPlatform p){inner.Initialize(c,p);}
    public void ReInitialize(object c,TargetPlatform p){inner.ReInitialize(c,p);}
    public uint GetAllTypesHash(){return inner.GetAllTypesHash();}
    public uint GetVersionNumber(){return inner.GetVersionNumber();}
    public ExtendedTypeInformation GetExtendedTypeInformation(uint id){return inner.GetExtendedTypeInformation(id);}
    public AssetBuffer ProcessInstance(InstanceDeclaration d){
        var b=inner.ProcessInstance(d);if(b==null)throw new Exception("AudioFile returned null");
        var stem=Path.Combine(Environment.GetEnvironmentVariable("EALAYER3_SDK_TRACE"),d.XmlNode.Attributes["id"].Value);
        File.WriteAllBytes(stem+".runtime",b.InstanceData);File.WriteAllBytes(stem+".relo",b.RelocationData??new byte[0]);File.WriteAllBytes(stem+".imp",b.ImportsData??new byte[0]);File.Copy(d.CustomDataPath,stem+".cdata",true);return b;
    }
}
