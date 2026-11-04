using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// External read-only test executable. Never included in the product source set.
class CompilerDeployment
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static readonly Dictionary<string,string> UiDependencies=new Dictionary<string,string>(StringComparer.Ordinal) {
        {"LibTessDotNet","6f2a8b10515fe328e1c9a28b23660958d8a3eea0ca436042516ff5e0a2ec5921"},
        {"Clipper2Lib","48c351243cdaedc28fc726ae0e4d8fdb39207556fb8caf1a6e4d0bdfe77159f4"},
        {"StbImageSharp","387e42762fafcac4f82cbc2bf2b418f4ada338ea444734b1d444c6a6e2d1cb6e"}
    };
    static byte[] EmbeddedDependency(Assembly own,string name)
    {
        string resource="BinaryAssetBuilder.UiCompiler.Dependencies."+name+".dll";
        using(Stream input=own.GetManifestResourceStream(resource))using(var bytes=new MemoryStream())using(SHA256 sha=SHA256.Create()) {
            Require(input!=null,"Missing pinned UI dependency "+resource);
            input.CopyTo(bytes);byte[] image=bytes.ToArray();
            Require(BitConverter.ToString(sha.ComputeHash(image)).Replace("-","").ToLowerInvariant()==UiDependencies[name],"Embedded UI dependency differs from its package pin: "+name);
            return image;
        }
    }
    static int Main(string[] args)
    {
        try {
            string exe=Path.GetFullPath(args[0]), bin=Path.GetDirectoryName(exe);
            Assembly own=null;
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=(sender,e)=>{
                string name=new AssemblyName(e.Name).Name;
                if(name=="antlr.runtime")return Assembly.ReflectionOnlyLoadFrom(Path.Combine(bin,"antlr.runtime.dll"));
                if(UiDependencies.ContainsKey(name))return Assembly.ReflectionOnlyLoad(EmbeddedDependency(own,name));
                Require(name=="mscorlib" || name=="System" || name=="netstandard" || name.StartsWith("System."),"Unexpected assembly resolution "+name);
                return Assembly.ReflectionOnlyLoad(e.Name);
            };
            own=Assembly.ReflectionOnlyLoadFrom(exe);
            Require(own.GetModules().Length==1,"Product still depends on netmodule files");
            Require(own.EntryPoint.DeclaringType.FullName=="NewModCompiler.Hosting.CompilerEntry","Production entry did not use the CLI host");
            foreach(string name in File.ReadAllLines(args[1]))Require(own.GetType(name,true).Assembly==own,"Business type ownership differs: "+name);
            foreach(string name in new[]{"SingleExeProbe","TextureProbe","CompilerDeployment","CompilerRoutes","NewModCompiler.Validation.ValidationEntry","NewSdk.BuildValidation.ManagedFoundationValidation","NewModCompiler.Tests.AppTests"})Require(own.GetType(name)==null,"Test/probe class entered product: "+name);
            foreach(Type type in own.GetTypes()) {
                Require(type.Assembly==own,"Type is owned by an external module: "+type.FullName);
                Require(type.Namespace!="NewModCompiler.Tests" && type.Namespace!="NewSdk.BuildValidation" && type.Namespace!="NewModCompiler.Validation","Test namespace entered product: "+type.FullName);
            }
            var json=new JavaScriptSerializer();
            var resources=(object[])json.DeserializeObject(File.ReadAllText(args[2]));
            Require(resources.Length==8,"Expected five Foundation resources and three pinned embedded UI libraries");
            Require(own.GetManifestResourceNames().Length==resources.Length,"Resource count differs");
            var expectedResources=new HashSet<string>(StringComparer.Ordinal) {
                "BinaryAssetBuilderProject.xsd","Mvp.Xml.Common.SR.resources","Mvp.Xml.Common.Serialization.PerfCounterInstaller.resources",
                "Mvp.Xml.XPointer.SR.resources","Mvp.Xml.XInclude.SR.resources"
            };
            foreach(string name in UiDependencies.Keys) {
                EmbeddedDependency(own,name);
                expectedResources.Add("BinaryAssetBuilder.UiCompiler.Dependencies."+name+".dll");
            }
            foreach(Dictionary<string,object> resource in resources) {
                string name=(string)resource["logicalName"], expected=(string)resource["sha256"];
                Require(expectedResources.Remove(name),"Unexpected or duplicate product resource "+name);
                using(Stream input=own.GetManifestResourceStream(name))using(SHA256 sha=SHA256.Create()) {
                    Require(input!=null,"Missing "+name);
                    Require(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()==expected,"Resource bytes differ: "+name);
                }
            }
            Require(expectedResources.Count==0,"Product resource inventory is incomplete");
            var seenUi=new HashSet<string>(StringComparer.Ordinal);
            foreach(AssemblyName reference in own.GetReferencedAssemblies()) {
                if(UiDependencies.ContainsKey(reference.Name)){seenUi.Add(reference.Name);continue;}
                Require(reference.Name=="antlr.runtime" || reference.Name=="mscorlib" || reference.Name=="System" || reference.Name=="netstandard" || reference.Name.StartsWith("System."),"Unexpected product AssemblyRef "+reference.FullName);
            }
            Require(seenUi.Count==3,"Product does not declare the three pinned embedded UI libraries");
            Console.WriteLine("PASS product: exact Foundation type inventory belongs to EXE; one physical module; eight exact resources including three package-pinned UI libraries; CLI hosting entry; no probes/tests; declared AssemblyRefs only");return 0;
        } catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}


