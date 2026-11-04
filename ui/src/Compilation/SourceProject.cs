using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal sealed class SourceProject
    {
        private readonly string root;
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> classes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, As2Class> compiledClasses = new Dictionary<string, As2Class>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> sourceCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> compiling = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingClasses = new HashSet<string>(StringComparer.Ordinal);
        private bool resolvingPendingClasses;
        internal readonly List<string> Inputs = new List<string>();
        internal readonly List<As2Class> ClassOrder = new List<As2Class>();
        private static readonly Regex Include = new Regex(@"(?m)^[\t ]*#include[\t ]+[""']([^""'\r\n]+)[""'][\t ]*;?[\t ]*(?://[^\r\n]*)?\r?$", RegexOptions.CultureInvariant);
        internal SourceProject(string directory)
        {
            root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => Path.GetExtension(p).Equals(".as", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
            {
                string relative = file.Substring(root.Length).Replace('\\', '/'); if (files.ContainsKey(relative)) throw new InvalidDataException("Ambiguous AS path: " + relative); files.Add(relative, file);
                string source = Read(file); string masked = Mask(source); var match = Regex.Match(masked, @"\bclass\s+([\w.$]+)", RegexOptions.CultureInvariant);
                if (match.Success) { string name = match.Groups[1].Value; if (classes.ContainsKey(name)) throw new InvalidDataException("Duplicate AS2 class " + name); classes.Add(name, file); }
            }
        }
        private string Read(string path)
        { if (sourceCache.TryGetValue(path, out string cached)) return cached; if (!Inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) Inputs.Add(path); byte[] bytes = File.ReadAllBytes(path); string text; if (bytes.Length >= 2 && (bytes[0] == 255 && bytes[1] == 254 || bytes[0] == 254 && bytes[1] == 255)) text = (bytes[0] == 255 ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2); else { int offset = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0; text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); } sourceCache.Add(path, text); return text; }
        internal string TimelineScript(string flaPath)
        {
            string candidate = Path.ChangeExtension(Path.GetFullPath(flaPath), ".as"); string relative = candidate.Substring(root.Length).Replace('\\', '/');
            if (!files.TryGetValue(relative, out string path) || classes.Values.Contains(path, StringComparer.OrdinalIgnoreCase)) return "";
            return Prepare(Expand(Read(path), path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)), path);
        }
        internal string Embedded(string source, string flaPath) => Prepare(Expand(source, Path.GetFullPath(flaPath), new HashSet<string>(StringComparer.OrdinalIgnoreCase)), flaPath);
        private string Prepare(string source, string path)
        {
            string mask = Mask(source); var imports = Regex.Matches(mask, @"\bimport[\t ]+([\w.$]+(?:\.\*)?)[\t ]*;?", RegexOptions.CultureInvariant);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match import in imports)
            {
                string name = import.Groups[1].Value;
                if (name.EndsWith(".*", StringComparison.Ordinal)) { string prefix = name.Substring(0, name.Length - 1); foreach (string type in classes.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.IndexOf('.', prefix.Length) < 0)) { ReferenceClass(type); aliases.Add(type.Substring(type.LastIndexOf('.') + 1), type); } }
                else { ReferenceClass(name); aliases.Add(name.Substring(name.LastIndexOf('.') + 1), name); }
            }
            // Import aliases are lexical compiler input, never global variables.
            foreach (Match import in imports.Cast<Match>().Reverse())
            {
                string name = import.Groups[1].Value;
                if (!name.EndsWith(".*", StringComparison.Ordinal)) continue;
                string prefix = name.Substring(0, name.Length - 1);
                string replacement = string.Join("\n", classes.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.IndexOf('.', prefix.Length) < 0).OrderBy(k => k, StringComparer.Ordinal).Select(k => "import " + k + ";"));
                source = source.Substring(0, import.Index) + replacement + source.Substring(import.Index + import.Length);
            }
            return source;
        }
        internal As2Class RequireClass(string name)
        {
            if (compiledClasses.TryGetValue(name, out var found)) return found;
            if (!classes.TryGetValue(name, out string path))
            { if (As2Class.IsBuiltin(name)) return null; throw new InvalidDataException("Missing AS2 class '" + name + "' under " + root); }
            if (!compiling.Add(name)) throw new InvalidDataException("Circular AS2 class dependency: " + name);
            As2Class result;
            try
            {
                string source = Prepare(Expand(Read(path), path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)), path);
                string prefix = name.LastIndexOf('.') < 0 ? "" : name.Substring(0, name.LastIndexOf('.') + 1);
                var implicitTypes = classes.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.IndexOf('.', prefix.Length) < 0).ToDictionary(k => k.Substring(prefix.Length), k => k, StringComparer.Ordinal);
                result = ActionScriptCompiler.CompileClass(source, ReferenceClass, implicitTypes, RequireClass); if (result.Name != name) throw new InvalidDataException("AS2 class name mismatch.");
                compiledClasses.Add(name, result); ClassOrder.Add(result);
            }
            finally { compiling.Remove(name); }
            // Imports and method references may be mutual. Compile them after
            // the current inheritance chain is complete; only extends is a
            // strict dependency and must reject a cycle.
            if (compiling.Count == 0 && !resolvingPendingClasses)
            {
                resolvingPendingClasses = true;
                try
                {
                    while (pendingClasses.Count != 0)
                    { string next = pendingClasses.OrderBy(p => p, StringComparer.Ordinal).First(); pendingClasses.Remove(next); RequireClass(next); }
                    var ordered = new List<As2Class>(); var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
                    void Order(As2Class type)
                    {
                        if (visited.Contains(type.Name)) return;
                        if (!visiting.Add(type.Name)) throw new InvalidDataException("Circular AS2 class initialization: " + type.Name);
                        foreach (string dependency in new[] { type.BaseName }.Concat(type.InitializerDependencies.OrderBy(p => p, StringComparer.Ordinal)))
                            if (dependency != null && compiledClasses.TryGetValue(dependency, out var required)) Order(required);
                        visiting.Remove(type.Name); visited.Add(type.Name); ordered.Add(type);
                    }
                    foreach (var type in ClassOrder) Order(type);
                    ClassOrder.Clear(); ClassOrder.AddRange(ordered);
                }
                finally { resolvingPendingClasses = false; }
            }
            return result;
        }
        private As2Class ReferenceClass(string name)
        {
            if (compiledClasses.TryGetValue(name, out var result)) return result;
            if (!classes.ContainsKey(name)) { if (As2Class.IsBuiltin(name)) return null; throw new InvalidDataException("Missing AS2 class '" + name + "' under " + root); }
            if (compiling.Count == 0) return RequireClass(name);
            pendingClasses.Add(name); return null;
        }
        private string Expand(string source, string owner, HashSet<string> stack)
        {
            if (!stack.Add(owner)) throw new InvalidDataException("Circular #include: " + owner);
            try
            {
                string mask = Mask(source);
                return Include.Replace(source, match =>
                {
                    int hash = source.IndexOf('#', match.Index, match.Length); if (hash < 0 || mask[hash] != '#') return match.Value;
                    string requested = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner), requested));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("#include is outside the source folder: " + requested);
                    string key = target.Substring(root.Length).Replace('\\', '/'); if (!files.TryGetValue(key, out string path)) throw new FileNotFoundException("Missing AS #include '" + requested + "' from " + owner, target);
                    return "\n" + Expand(Read(path), path, stack) + "\n";
                });
            }
            finally { stack.Remove(owner); }
        }
        internal static string Mask(string source, bool strings = true)
        {
            char[] output = source.ToCharArray(); int state = 0; char quote = '\0';
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i], next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (state == 1) { if (c == '\n') state = 0; else output[i] = ' '; }
                else if (state == 2) { if (c != '\n' && c != '\r') output[i] = ' '; if (c == '*' && next == '/') { output[++i] = ' '; state = 0; } }
                else if (state == 3) { if (strings && c != '\n' && c != '\r') output[i] = ' '; if (c == '\\' && i + 1 < source.Length) { if (strings) output[i + 1] = ' '; i++; } else if (c == quote) state = 0; }
                else if (c == '/' && next == '/') { output[i] = output[++i] = ' '; state = 1; }
                else if (c == '/' && next == '*') { output[i] = output[++i] = ' '; state = 2; }
                else if (c == '"' || c == '\'') { quote = c; if (strings) output[i] = ' '; state = 3; }
            }
            return new string(output);
        }
    }
    internal sealed class As2Class
    {
        internal string Name;
        internal string BaseName;
        internal ActionProgram Code;
        internal readonly HashSet<string> InitializerDependencies = new HashSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> InstanceMembers = new HashSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> StaticMembers = new HashSet<string>(StringComparer.Ordinal);
        internal static bool IsBuiltin(string name) => new[] { "Object", "MovieClip", "Button", "TextField", "TextFormat", "Array", "String", "Number", "Boolean", "Date", "Math", "Color", "XML", "XMLNode", "Sound", "LoadVars", "Video", "MovieClipLoader", "LocalConnection", "SharedObject", "ContextMenu", "ContextMenuItem", "Error", "Key", "Mouse", "Stage", "System", "Selection", "AsBroadcaster" }.Contains(name, StringComparer.Ordinal) || name.StartsWith("flash.", StringComparison.Ordinal);
    }
}
