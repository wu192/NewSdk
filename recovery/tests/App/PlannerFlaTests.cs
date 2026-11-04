using System;
using System.IO;
using System.Linq;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    static class PlannerFlaTests
    {
        static int checks;
        static void Need(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        static void Write(string path, string content = "fixture") { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content); }
        internal static int Run(string root)
        {
            var request = new BuildRequest { ModRoot = Path.Combine(root, "mod"), SdkRoot = Path.Combine(root, "sdk"), AutoDetectContent = true, Content = new[] { "apt" } };
            string apt = Path.Combine(request.ModRoot, "Data", "AptUI");
            string movie = Path.Combine(apt, "sources", "Movie.FLA");
            Write(movie);
            var plan = Planner.Resolve(request);
            Need(plan.Tasks.Length == 1 && plan.Tasks[0].Id == "apt/Movie" && plan.Tasks[0].InputPath == movie, "Nested bare FLA must create a named APT stream");
            Write(Path.Combine(apt, "Movie.xml"), "<AssetDeclaration/>");
            Need(Planner.Resolve(request).Tasks.Single().InputPath.EndsWith("Movie.xml", StringComparison.Ordinal), "An existing same-name XML stream must keep ownership");
            File.Delete(Path.Combine(apt, "Movie.xml"));
            Write(Path.Combine(apt, "Entry.xml"), "<AssetDeclaration xmlns='uri:ea.com:eala:asset'><Includes><Include source='includes/nested.xml'/></Includes></AssetDeclaration>");
            Write(Path.Combine(apt, "includes", "nested.xml"), "<AssetDeclaration xmlns='uri:ea.com:eala:asset'><AptAptData id='Movie_apt' File='../sources/Movie.FLA'/></AssetDeclaration>");
            Need(Planner.Resolve(request).Tasks.Single().Id == "apt/Entry", "A FLA referenced through a selected XML include must not produce a second stream");
            Write(Path.Combine(apt, "includes", "nested.xml"), "<AssetDeclaration xmlns='uri:ea.com:eala:asset'><AptAptData id='Movie_apt' File='data:AptUI/sources'/></AssetDeclaration>");
            Need(Planner.Resolve(request).Tasks.Single().Id == "apt/Entry", "Folder FLA declarations must suppress bare-source duplication");
            Write(Path.Combine(apt, "duplicate", "Movie.fla"));
            Need(Planner.Resolve(request).Tasks.Length == 2, "Referenced sources must be removed before checking bare FLA name collisions");
            File.Delete(Path.Combine(apt, "Entry.xml"));
            try { Planner.Resolve(request); throw new Exception("Duplicate FLA stream names were accepted"); }
            catch (BuildFailure error) { Need(error.Code == "PLAN_COLLISION", "FLA names from two folders must keep the existing output collision error"); }
            return checks;
        }
    }
}
