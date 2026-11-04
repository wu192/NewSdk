using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using NewModCompiler.Application;
using NewModCompiler.Cli;

namespace NewModCompiler.Tests
{
    internal static class BuildDefaultsTests
    {
        static int checks;
        static void Check(bool value,string message) { ++checks; if(!value) throw new Exception("Build defaults: "+message); }
        static void Put(string path,string text="<AssetDeclaration/>") { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,text); }
        static void Reject(string code,Action action) { try { action(); throw new Exception("Accepted "+code); } catch(BuildFailure e) { Check(e.Code==code,"Expected "+code+", got "+e.Code); } }
        static void RejectCli(string[] arguments)
        {
            var previous=Console.Error;var error=new StringWriter();
            try {
                Console.SetError(error);
                Check(Program.Main(arguments)==(int)ExitCode.Arguments,"Unsupported options must return CLI exit code 2");
                Check(error.ToString().Contains("CLI_"),"Unsupported options must fail before Core execution");
            } finally { Console.SetError(previous); }
        }
        public static int Run(string root)
        {
            checks=0;
            string sdk=Path.Combine(root,"sdk"),mod=Path.Combine(root,"mod"),output=Path.Combine(root,"out");
            Directory.CreateDirectory(sdk);Directory.CreateDirectory(mod);
            Func<string[],BuildRequest> parse=extra=>Options.Parse(new[]{"plan","--mod",mod,"--sdk-root",sdk,"--out",output}.Concat(extra).ToArray());
            Func<string[],BuildRequest> parseBig=extra=>parse(new[]{"--mod-version","1.0"}.Concat(extra).ToArray());
            var standard=parseBig(new string[0]);
            Check(standard.PackageFormat=="big" && standard.IncludeAdditional && !standard.FixNeutralAssets,"Standard package defaults");
            Check(standard.AutoDetectContent && standard.GameVersion=="1.12" && standard.ModVersion=="1.0","Automatic content and identity defaults");
            Check(standard.JobsAuto && standard.RequestedJobs==0 && standard.PerformanceLevel=="off","Automatic jobs and disabled performance defaults");
            string[] ordinary={"mod","sdk-root","out","package-content","package-format","include-additional","mod-version","skudef-name","jobs","performance"};
            foreach(string key in ordinary) Check(Options.Help.Contains("--"+key),"Ordinary help includes "+key);
            foreach(string key in new[]{"profile","fix-neutral-assets","game-version","log","events","report"})
                Check(!Options.Help.Contains("--"+key),"Ordinary help hides "+key);
            foreach(string key in new[]{"fix-neutral-assets","game-version","log","events","report"})
                Check(Options.AdvancedHelp.Contains("--"+key),"Advanced help includes "+key);
            foreach(string key in new[]{"ss","pc","slowclean","sc","bc","csc","psc","fsc","atc","scd","scp","bcd","bcp","iod","dr","data","art","audio","tl"})
                Check(Options.AdvancedHelp.Contains("/"+key),"Advanced help includes "+key);
            var allOrdinary=parse(new[]{"--package-content","base,apt,global-metadata","--package-format","big","--include-additional=false","--mod-version","2.0","--skudef-name","Launcher","--jobs","2","--performance","detailed"});
            Check(allOrdinary.Content.SequenceEqual(new[]{"base","apt","maps"}) && allOrdinary.ModVersion=="2.0" && allOrdinary.SkudefName=="Launcher" && !allOrdinary.IncludeAdditional && !allOrdinary.JobsAuto && allOrdinary.RequestedJobs==2 && allOrdinary.PerformanceLevel=="detailed","All ten ordinary parameters remain usable together");
            foreach(string key in new[]{"ss","pc","slowclean","sc","bc","csc","psc"}) {
                var disabled=parseBig(new[]{"/"+key+":false"});
                Check(disabled.Options[key].Value=="false" && disabled.Options[key].Source=="commandLine","Direct advanced override preserves value and source: "+key);
            }
            Check(parseBig(new[]{"--fix-neutral-assets"}).FixNeutralAssets && !parseBig(new[]{"--fix-neutral-assets=false"}).FixNeutralAssets,"Neutral repair is directly available and explicitly disabled");
            Reject("PACKAGE_NEUTRAL_CONTENT",()=>parse(new[]{"--package-format","loose","--fix-neutral-assets=true"}));
            Reject("PACKAGE_NEUTRAL_CONTENT",()=>parse(new[]{"--package-content","apt","--fix-neutral-assets=true"}));
            foreach(string command in new[]{"plan","build"}) {
                string[] prefix={command,"--mod",mod,"--sdk-root",sdk,"--out",output,"--mod-version","1.0"};
                foreach(string option in new[]{"/sp:ignored.xsd","/bcn:default","/bps:4096","/cpf:asset","/el:1","/ls:false","/tp:Xbox360","/gui:false","/res:false","/pers:false","/mp:false","/poe:false","/sf:false","/sh:false","--unknown=value"})
                    RejectCli(prefix.Concat(new[]{option}).ToArray());
                foreach(string key in new[]{"oof","vf","oar","osh","oix","mr"}) {
                    Check(Options.Parse(prefix.Concat(new[]{"/"+key+":false"}).ToArray()).Options[key].Value=="false","Legacy false remains accepted: "+key);
                    RejectCli(prefix.Concat(new[]{"/"+key+":true"}).ToArray());
                }
                Check(Options.Parse(prefix.Concat(new[]{"/tp:Win32","/ls:true","/el:0"}).ToArray()).Options["tp"].Value=="Win32","Fixed compatibility values remain accepted");
            }
            Check(!Directory.Exists(output),"Rejected CLI requests do not create outputs or diagnostic files");
            foreach(string key in new[]{"BC","scp"}) {
                var manual=new BuildRequest();manual.Options[key]=new EffectiveOption{Value="false",Source="commandLine"};
                Reject("CLI_UNKNOWN",()=>Options.ValidateCapabilities(manual));
            }
            foreach(string profile in new[]{"standard","development","advanced"}) {
                var request=(profile=="development"?parse:parseBig)(new[]{"--profile",profile});
                Check(request.PackageFormat==(profile=="development"?"loose":"big"),"Profile output format");
                Check(request.IncludeAdditional==(profile!="development"),"Additional follows format");
                foreach(string key in new[]{"sc","bc","psc","csc"}) Check(request.Options[key].Value=="true","Original SDK cache default "+key);
                foreach(string key in new[]{"fsc","atc"}) Check(request.Options[key].Value=="false","Original SDK cache default "+key);
                foreach(string key in new[]{"ss","pc","slowclean"}) Check(request.Options[key].Value=="true","Legacy script default "+key);
            }
            Check(!parse(new[]{"--package-format","loose"}).IncludeAdditional,"Explicit loose remains usable");
            Check(!parseBig(new[]{"--include-additional=false"}).IncludeAdditional,"Additional can be disabled");
            Check(parseBig(new[]{"--profile","development","--package-format","big"}).IncludeAdditional,"Explicit format overrides profile");
            Reject("CLI_PROFILE",()=>parse(new[]{"--profile","unknown"}));
            Reject("PLAN_NO_INPUTS",()=>Planner.Resolve(standard));
            Put(Path.Combine(mod,"Additional","only.ini"),"additional only");
            Reject("PLAN_NO_INPUTS",()=>Planner.Resolve(standard));
            Put(Path.Combine(mod,"Data","AptUI","One.xml"));
            var apt=Planner.Resolve(standard);Check(apt.Tasks.Length==1 && apt.Tasks[0].Content=="apt" && apt.Tasks[0].Reason=="AutomaticContentDiscovery","APT-only MOD without base");
            Put(Path.Combine(mod,"Data","AdditionalMaps","MapMetaData_One.xml"));
            File.Delete(Path.Combine(mod,"Data","AptUI","One.xml"));
            Check(Planner.Resolve(standard).Tasks.Single().Content=="maps","Metadata-only MOD without base");
            Put(Path.Combine(mod,"Data","Mod.xml"));Put(Path.Combine(mod,"Data","AptUI","One.xml"));
            Check(Planner.Resolve(standard).Tasks.Length==3,"Automatic mixed MOD discovery");
            var current=parseBig(new[]{"--package-content","global-metadata"});var legacy=parseBig(new[]{"--package-content","maps"});
            Check(current.Content.SequenceEqual(legacy.Content) && Planner.Resolve(current).Tasks.Single().Id=="maps/MapMetaData_One","Metadata alias preserves task IDs and paths");
            Check(parseBig(new[]{"--package-content","maps,global-metadata"}).Content.Length==1,"Metadata alias deduplication");
            Check(Planner.Resolve(parseBig(new[]{"--package-content","apt"})).Tasks.Length==1,"Explicit selection stays partial");
            File.Delete(Path.Combine(mod,"Data","Mod.xml"));
            Reject("PLAN_BASE_MISSING",()=>Planner.Resolve(parseBig(new[]{"--package-content","base"})));
            Reject("PACKAGE_NEUTRAL_CONTENT",()=>Planner.Resolve(parseBig(new[]{"--fix-neutral-assets=true"})));
            File.Delete(Path.Combine(mod,"Data","AptUI","One.xml"));
            Check(Planner.Resolve(parseBig(new[]{"--package-content","apt"})).Tasks.Length==0,"Explicit empty optional selection preserves old behavior");
            var preset=new Dictionary<string,object>{{"sdk-root","../sdk"},{"out","../project-out"},{"profile","development"},{"package-content","global-metadata"},{"pc",false}};
            string project=Path.Combine(mod,"newmodcompiler.json");File.WriteAllText(project,new JavaScriptSerializer().Serialize(preset));
            var saved=Options.Parse(new[]{"plan","--mod",mod});
            Check(saved.SdkRoot==sdk && saved.OutputRoot==Path.Combine(root,"project-out") && saved.PackageFormat=="loose","MOD-only invocation uses project paths relative to MOD");
            Check(saved.Options["pc"].Value=="false" && saved.Options["pc"].Source=="project","Advanced compatibility setting in project");
            var overridden=Options.Parse(new[]{"plan","--mod",mod,"--profile","standard","--out",output,"/pc:true","--mod-version","1.0"});
            Check(overridden.PackageFormat=="big" && overridden.OutputRoot==output && overridden.Options["pc"].Source=="commandLine","CLI overrides project");
            File.WriteAllText(project,"{\"mod-version\":2.7,\"game-version\":1.12}");
            Check(parse(new[]{"--profile","development"}).ModVersion=="2.7", "Existing numeric project versions remain accepted for derived loose output");
            var development=parse(new[]{"--profile","development","--mod-version","3.1"});
            Check(development.PackageFormat=="loose" && development.ModVersion=="3.1" && development.GameVersion=="1.12", "Development can reuse project package identity and CLI version overrides");
            Check(development.Options["package-format"].Source=="default","Development-derived loose retains default provenance");
            Reject("PACKAGE_IDENTITY_FORMAT",()=>parse(new[]{"--package-format","loose","--mod-version","3.1"}));
            Reject("PACKAGE_IDENTITY_FORMAT",()=>parse(new[]{"--profile","development","--package-format","loose","--mod-version","3.1"}));
            File.WriteAllText(project,"{\"profile\":\"development\",\"package-format\":\"big\",\"mod-version\":\"2.7\"}");
            Check(parseBig(new string[0]).PackageFormat=="big","Project format overrides compatibility profile default");
            Reject("PACKAGE_IDENTITY_FORMAT",()=>parse(new[]{"--package-format","loose"}));
            File.WriteAllText(project,"{\"profile\":\"development\",\"mod-version\":\"2.7\",\"skudef-name\":\"SavedName\"}");
            Check(parse(new string[0]).PackageFormat=="loose" && parseBig(new[]{"--profile","advanced"}).PackageFormat=="big","Saved identity survives derived loose and CLI profile override");
            File.WriteAllText(project,"{\"tp\":\"Xbox360\"}");Reject("CLI_PLATFORM",()=>parse(new string[0]));
            File.WriteAllText(project,"null");Reject("PROJECT_INVALID",()=>parse(new string[0]));
            File.WriteAllText(project,"{\"out\":null}");Reject("PROJECT_OPTION",()=>parse(new string[0]));
            File.Delete(project);
            Check(Options.Parse(new[]{"doctor","--sdk-root",sdk}).Command=="doctor","Doctor has no MOD/package input");
            RunVersionRequirement(Path.Combine(root,"version-requirement"));
            return checks;
        }
        public static int RunVersionRequirement(string root)
        {
            int previousChecks=checks;
            string sdk=Path.Combine(root,"sdk"),mod=Path.Combine(root,"mod"),output=Path.Combine(root,"out"),project=Path.Combine(mod,"newmodcompiler.json");
            Directory.CreateDirectory(sdk);Directory.CreateDirectory(mod);
            Action<string[]> missing=arguments=> {
                try { Options.Parse(arguments); throw new Exception("Accepted BIG output without an explicit CLI MOD version"); }
                catch(BuildFailure failure) { Check(failure.Code=="CLI_MOD_VERSION" && failure.ExitCode==ExitCode.Arguments,"Missing CLI BIG version returns CLI_MOD_VERSION and exit code 2"); }
            };
            foreach(string command in new[]{"build","plan"}) {
                string[] prefix={command,"--mod",mod,"--sdk-root",sdk,"--out",output};
                missing(prefix);
                var explicitVersion=Options.Parse(prefix.Concat(new[]{"--mod-version","1.0"}).ToArray());
                Check(explicitVersion.PackageFormat=="big" && explicitVersion.ModVersion=="1.0" && explicitVersion.Options["mod-version"].Source=="commandLine","Explicit CLI version enables BIG output");
                File.WriteAllText(project,"{\"mod-version\":\"2.7\"}");
                missing(prefix);
                var overridden=Options.Parse(prefix.Concat(new[]{"--mod-version","1.0"}).ToArray());
                Check(overridden.ModVersion=="1.0" && overridden.Options["mod-version"].Source=="commandLine","CLI version overrides the project for BIG output");
                File.Delete(project);
                var loose=Options.Parse(prefix.Concat(new[]{"--package-format","loose"}).ToArray());
                Check(loose.PackageFormat=="loose" && loose.Options["mod-version"].Source=="default","Explicit loose still accepts no CLI version");
                var development=Options.Parse(prefix.Concat(new[]{"--profile","development"}).ToArray());
                Check(development.PackageFormat=="loose" && development.Options["package-format"].Source=="default" && development.Options["mod-version"].Source=="default","Development-derived loose still accepts no CLI version");
            }
            Reject("PACKAGE_IDENTITY_FORMAT",()=>Options.Parse(new[]{"plan","--mod",mod,"--sdk-root",sdk,"--out",output,"--package-format","loose","--mod-version","1.0"}));
            Check(Options.Parse(new[]{"doctor","--sdk-root",sdk}).Command=="doctor","Doctor still accepts no CLI version");
            Check(!Directory.Exists(output),"Version validation does not create output files");
            return checks-previousChecks;
        }
    }
}
