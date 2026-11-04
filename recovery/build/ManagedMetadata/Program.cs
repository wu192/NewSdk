using Mono.Cecil;
using Mono.Cecil.Cil;

// This writer operates ONLY on an IL netmodule, before MSVC links native code.
// It never writes the mixed executable or any legacy mixed DLL.
if (args.Length != 3 || (args[0] != "repair" && args[0] != "verify" && args[0] != "verify-linked"))
    throw new ArgumentException("Usage: ManagedMetadata repair|verify|verify-linked <module or linked exe> <original Utility DLL>");
using var module = ModuleDefinition.ReadModule(args[1], new ReaderParameters { InMemory = true });
bool linked = args[0] == "verify-linked";
if (!linked && (module.Kind != ModuleKind.NetModule || (module.Attributes & ModuleAttributes.ILOnly) == 0))
    throw new InvalidOperationException("Only pure IL build-time netmodules may be rewritten");
if (linked && module.Kind == ModuleKind.NetModule)
    throw new InvalidOperationException("Linked verification requires the final executable");
using var original = ModuleDefinition.ReadModule(args[2]);
TypeReference ImportModifier(TypeReference source) {
    var own = module.GetTypes().SingleOrDefault(t => t.FullName == source.FullName);
    return own ?? module.ImportReference(source);
}
// The final linker adds its own global <Module> CRT helpers. Only restored
// Utility business types participate in the legacy Utility contract; compiler
// helpers such as DecodePointer are neither inputs nor API promises here.
bool RecoveredUtilityType(TypeDefinition type) => type.Namespace == "BinaryAssetBuilder.Utility" || type.Namespace == "AssetStream";
if (args[0] == "repair") {
foreach (var type in module.Types.Where(RecoveredUtilityType)) {
    if (type.Name.StartsWith("StructWrapper_003CAssetStream_003A_003A")) {
        string name = type.Name.Replace("StructWrapper_003CAssetStream_003A_003A", "").Replace("_003E", "");
        type.Name = original.Types.Single(t => t.Name == "StructWrapper<AssetStream::" + name + ">").Name;
    }
}
string Plain(TypeReference t) {
    if(t is OptionalModifierType om) return Plain(om.ElementType);
    if(t is RequiredModifierType rm) return Plain(rm.ElementType);
    if(t is PointerType p) return Plain(p.ElementType)+"*";
    if(t is ByReferenceType r) return Plain(r.ElementType)+"&";
    if(t is ArrayType a) return Plain(a.ElementType)+"["+new string(',',a.Rank-1)+"]";
    return t.FullName;
}
TypeReference Modifiers(TypeReference source, TypeReference target) {
    while(target is OptionalModifierType || target is RequiredModifierType) target=((TypeSpecification)target).ElementType;
    if(source is OptionalModifierType om) return new OptionalModifierType(ImportModifier(om.ModifierType),Modifiers(om.ElementType,target));
    if(source is RequiredModifierType rm) return new RequiredModifierType(ImportModifier(rm.ModifierType),Modifiers(rm.ElementType,target));
    if(source is PointerType p && target is PointerType tp) return new PointerType(Modifiers(p.ElementType,tp.ElementType));
    if(source is ByReferenceType r && target is ByReferenceType tr) return new ByReferenceType(Modifiers(r.ElementType,tr.ElementType));
    if(source is ArrayType a && target is ArrayType ta) return new ArrayType(Modifiers(a.ElementType,ta.ElementType),a.Rank);
    return target;
}
foreach (var type in module.Types.Where(RecoveredUtilityType)) {
    var match = original.Types.FirstOrDefault(t => t.FullName == type.FullName);
    if (match == null) continue;
    type.Attributes = match.Attributes;
    foreach (var method in type.Methods.Where(m => m.IsPublic || m.IsFamily)) {
        var source = match.Methods.SingleOrDefault(m => m.Name == method.Name && m.Parameters.Count == method.Parameters.Count
            && Plain(m.ReturnType)==Plain(method.ReturnType)
            && m.Parameters.Select(p=>Plain(p.ParameterType)).SequenceEqual(method.Parameters.Select(p=>Plain(p.ParameterType))));
        if(source==null) throw new Exception("No original contract for "+method.FullName);
        method.Attributes=source.Attributes;
        method.CallingConvention=source.CallingConvention;
        method.ReturnType=Modifiers(source.ReturnType,method.ReturnType);
        for(int i=0;i<method.Parameters.Count;i++) method.Parameters[i].ParameterType=Modifiers(source.Parameters[i].ParameterType,method.Parameters[i].ParameterType);
    }
}

    int aliases = 0;
    foreach (var type in module.GetTypes().Where(t => t.Namespace == "Mvp.Xml.Exslt")) foreach (var method in type.Methods) {
        if (method.Name.Contains("_002D")) method.Name = method.Name.Replace("_002D", "-");
        if (method.Name.Contains("-")) aliases++;
    }
    if (aliases != 51) throw new InvalidOperationException("Expected exactly 51 EXSLT aliases, found " + aliases);
    module.Write(args[1]);
}
// Verify the complete recovered Utility public/family surface against the
// original contract, including non-C# custom modifiers, after all renames.
int utilityMethods = 0;
foreach (var type in module.Types.Where(RecoveredUtilityType)) {
    var sourceType = original.Types.FirstOrDefault(t => t.FullName == type.FullName);
    if(sourceType == null) continue;
    if(type.Attributes != sourceType.Attributes) throw new InvalidOperationException("Utility type flags changed: " + type.FullName);
    foreach(var method in type.Methods.Where(m => m.IsPublic || m.IsFamily)) {
        var source = sourceType.Methods.SingleOrDefault(m => m.Name == method.Name && m.Parameters.Count == method.Parameters.Count
            && m.ReturnType.FullName == method.ReturnType.FullName
            && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p=>p.ParameterType.FullName)));
        if(source == null || source.Attributes != method.Attributes || source.CallingConvention != method.CallingConvention)
            throw new InvalidOperationException("Utility member contract changed: " + method.FullName);
        utilityMethods++;
    }
}
if(utilityMethods == 0) throw new InvalidOperationException("No Utility contracts were verified");
Console.WriteLine("PASS Utility exact public/family flags and custom signature contracts: " + utilityMethods);
var allowed = new HashSet<string>(StringComparer.Ordinal) { "mscorlib", "System", "System.Core", "System.Xml", "System.Configuration", "System.Data", "System.Web", "System.Configuration.Install", "System.Drawing", "netstandard", "antlr.runtime", "LibTessDotNet", "Clipper2Lib", "StbImageSharp" };
if(linked) allowed.Add("System.Web.Extensions");
foreach(var reference in module.AssemblyReferences)
    if(!allowed.Contains(reference.Name)) throw new InvalidOperationException("Unexpected runtime AssemblyRef: " + reference.FullName);
if(!linked && module.Resources.Count != 0) throw new InvalidOperationException("Resources must be embedded by the final linker");
if(linked && module.Resources.Count < 5) throw new InvalidOperationException("Final linked resources are missing");
var names = module.GetTypes().Where(t => t.Namespace == "Mvp.Xml.Exslt").SelectMany(t => t.Methods).Count(m => m.Name.Contains("-"));
if(names != 51) throw new InvalidOperationException("Missing EXSLT aliases");
foreach (var type in module.GetTypes().Where(t => t.FullName == "BinaryAssetBuilder.Core.ExpressionEvaluatorWrapper")) foreach (var method in type.Methods) {
    if (!method.HasBody) continue;
    foreach (var instruction in method.Body.Instructions) {
        if (!(instruction.Operand is MethodReference call)) continue;
        if(type.FullName == "BinaryAssetBuilder.Core.ExpressionEvaluatorWrapper" && call.DeclaringType.FullName == "System.Reflection.Assembly" && call.Name == "Load")
            throw new InvalidOperationException("Production expression wrapper still discovers an old assembly");
    }
}
Console.WriteLine("PASS Foundation IL module: only declared AssemblyRefs, 51 EXSLT aliases, resources deferred to linker, direct expression binding");

if(!linked) File.WriteAllLines(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "Foundation.types.txt"),
    module.GetTypes().Where(t => !t.Name.StartsWith("<")).Select(t => t.FullName.Replace('/', '+')).OrderBy(n => n, StringComparer.Ordinal));
Console.WriteLine(linked ? "PASS final linked EXE: read-only Utility ABI and EXSLT contract verification" : "PASS Foundation business type inventory emitted");
