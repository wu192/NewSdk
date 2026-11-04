using BinaryAssetBuilder.UiCompiler.Compilation;

internal static class ClassDependencyTests
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "UiClassDependencies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Parent.as"), "import Child\nclass Parent { public function value():Number { return 7; } public function create():Object { return new Child(); } }");
            File.WriteAllText(Path.Combine(root, "Child.as"), "import Parent; class Child extends Parent { public function read():Number { return value(); } }");
            var project = new SourceProject(root);
            project.RequireClass("Parent");
            check(project.ClassOrder.Count == 2 && project.ClassOrder[0].Name == "Parent" && project.ClassOrder[1].Name == "Child", "mutual imports compile with base before subclass");
            var machine = new Avm1Machine();
            foreach (var type in project.ClassOrder) machine.Run(type.Code);
            machine.Run(ActionScriptCompiler.Compile("var p=new Parent(); trace(p.create().read());"));
            check(string.Join(",", machine.Traces) == "7", "mutual class references execute after initialization: " + string.Join(",", machine.Traces));

            File.WriteAllText(Path.Combine(root, "Config.as"), "import Values; class Config { public static var VALUE:Number=Values.VALUE; }");
            File.WriteAllText(Path.Combine(root, "Values.as"), "class Values { public static var VALUE:Number=9; }");
            var initialized = new SourceProject(root); initialized.RequireClass("Config");
            var staticMachine = new Avm1Machine();
            foreach (var type in initialized.ClassOrder) staticMachine.Run(type.Code);
            staticMachine.Run(ActionScriptCompiler.Compile("trace(Config.VALUE);"));
            check(string.Join(",", staticMachine.Traces) == "9", "imported static initializer dependencies run before the importing class");

            File.WriteAllText(Path.Combine(root, "Parent.as"), "import Child; class Parent extends Child {}");
            bool rejected = false;
            try { new SourceProject(root).RequireClass("Parent"); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "inheritance cycles remain rejected");
        }
        finally { Directory.Delete(root, true); }
    }
}
