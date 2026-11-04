using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace NewModCompiler.Application
{
    public static class Planner
    {
        public static BuildPlan Resolve(BuildRequest request, IDependencyPlanner dependencyPlanner = null)
        {
            var tasks = new List<BuildTask>(); var skipped = new List<SkippedContent>();
            foreach (string group in request.Content) {
                string[] files;
                if (group == "base") {
                    string file = Path.Combine(request.ModRoot,"Data","Mod.xml");
                    if (!File.Exists(file) && request.AutoDetectContent) { skipped.Add(new SkippedContent { Content = group, Status = "SkippedNoInputs", Reason = "Data/Mod.xml is absent during automatic discovery." }); continue; }
                    if (!File.Exists(file)) throw new BuildFailure(ExitCode.Arguments,"PLAN_BASE_MISSING","Selected base content requires Data/Mod.xml.");
                    files = new [] {file};
                } else {
                    string dir = Path.Combine(request.ModRoot,"Data",group == "apt" ? "AptUI" : "AdditionalMaps");
                    files = Directory.Exists(dir) ? (group == "apt" ? DiscoverAptFiles(dir) : Directory.GetFiles(dir, "MapMetaData_*.xml", SearchOption.TopDirectoryOnly)) : new string[0];
                    files = files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToArray();
                    if (files.Length == 0) skipped.Add(new SkippedContent { Content = group, Status = "SkippedNoInputs", Reason = "No matching optional entries in selected content group." });
                }
                foreach (string input in files) {
                    string name = Path.GetFileNameWithoutExtension(input);
                    string id = group + "/" + name;
                    tasks.Add(new BuildTask { Id = id, Content = group, InputPath = Path.GetFullPath(input), OutputName = group + "/" + name, Reason = request.AutoDetectContent ? "AutomaticContentDiscovery" : "ExplicitContentSelection", Dependencies = new string[0] });
                }
            }
            if (tasks.Any(IsFla)) {
                var referenced = ReferencedFlaFiles(request, tasks.Where(task => !IsFla(task)));
                tasks.RemoveAll(task => IsFla(task) && referenced.Contains(task.InputPath));
            }
            if (request.AutoDetectContent && tasks.Count == 0) throw new BuildFailure(ExitCode.Arguments,"PLAN_NO_INPUTS","No valid MOD inputs found. Expected Data/Mod.xml, Data/AptUI XML/FLA sources or Data/AdditionalMaps/MapMetaData_*.xml. Additional files alone are not a compiled MOD input.");
            if (request.FixNeutralAssets && !tasks.Any(x => x.Content == "base")) throw new BuildFailure(ExitCode.Arguments,"PACKAGE_NEUTRAL_CONTENT","Neutral-asset repair requires an actual base input (Data/Mod.xml).");
            return new BuildPlan { Tasks = OrderTasks(dependencyPlanner == null ? tasks : dependencyPlanner.Expand(request,tasks.AsReadOnly())), Skipped = skipped.ToArray(), PlanKind = dependencyPlanner == null ? "StreamDiscovery" : "ExplicitDependencyGraph" };
        }
        static bool IsFla(BuildTask task) { return string.Equals(Path.GetExtension(task.InputPath), ".fla", StringComparison.OrdinalIgnoreCase); }
        static string[] DiscoverAptFiles(string directory)
        {
            var xml = Directory.GetFiles(directory, "*.xml", SearchOption.TopDirectoryOnly);
            var names = new HashSet<string>(xml.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
            // An existing XML stream with the same name remains the entry point.
            return xml.Concat(Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".fla", StringComparison.OrdinalIgnoreCase) && !names.Contains(Path.GetFileNameWithoutExtension(path)))).ToArray();
        }
        static HashSet<string> ReferencedFlaFiles(BuildRequest request, IEnumerable<BuildTask> roots)
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(roots.Select(task => task.InputPath));
            while (pending.Count != 0) {
                string path = pending.Dequeue();
                if (!visited.Add(path) || !File.Exists(path)) continue;
                var document = new XmlDocument { XmlResolver = null };
                try { document.Load(path); }
                catch (XmlException) { continue; } // Core owns malformed-input diagnostics.
                foreach (XmlNode node in document.SelectNodes("//*[local-name()='AptAptData' and @File]")) {
                    string source = ResolveSourcePath(request, Path.GetDirectoryName(path), node.Attributes["File"].Value);
                    if (source == null) continue;
                    if (Directory.Exists(source)) {
                        foreach (string movie in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
                            if (string.Equals(Path.GetExtension(movie), ".fla", StringComparison.OrdinalIgnoreCase)) referenced.Add(movie);
                    } else referenced.Add(source);
                }
                foreach (XmlNode node in document.SelectNodes("//*[local-name()='Include' and @source]")) {
                    string include = ResolveSourcePath(request, Path.GetDirectoryName(path), node.Attributes["source"].Value);
                    if (include != null && Paths.Contains(request.ModRoot, include)) pending.Enqueue(include);
                }
            }
            return referenced;
        }
        static string ResolveSourcePath(BuildRequest request, string sourceDirectory, string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            source = source.Trim();
            if (source.StartsWith("=", StringComparison.Ordinal)) return null;
            int colon = source.IndexOf(':');
            if (colon > 1) {
                string root;
                switch (source.Substring(0, colon).ToLowerInvariant()) {
                    case "data": root = Path.Combine(request.ModRoot, "Data"); break;
                    case "art": root = Path.Combine(request.ModRoot, "Art"); break;
                    case "audio": root = Path.Combine(request.ModRoot, "Audio"); break;
                    default: return null;
                }
                EffectiveOption configured;
                if (request.Options.TryGetValue(source.Substring(0, colon).ToLowerInvariant(), out configured) && configured.Source != "default") return null;
                return Path.GetFullPath(Path.Combine(root, source.Substring(colon + 1).TrimStart('/', '\\').Replace('\\', Path.DirectorySeparatorChar)));
            }
            return Path.GetFullPath(Path.IsPathRooted(source) ? source : Path.Combine(sourceDirectory, source.Replace('\\', Path.DirectorySeparatorChar)));
        }
        public static BuildTask[] OrderTasks(IEnumerable<BuildTask> input)
        {
            if (input == null) throw new BuildFailure(ExitCode.Arguments,"PLAN_DEPENDENCIES","Dependency planner returned no graph.");
            var tasks = new Dictionary<string,BuildTask>(StringComparer.OrdinalIgnoreCase);
            foreach (BuildTask task in input) {
                if (task == null || string.IsNullOrEmpty(task.Id) || string.IsNullOrEmpty(task.InputPath) || string.IsNullOrEmpty(task.OutputName) || task.Dependencies == null)
                    throw new BuildFailure(ExitCode.Arguments,"PLAN_DEPENDENCIES","Incomplete task in dependency graph.");
                if (task.Content != "base" && task.Content != "apt" && task.Content != "maps") throw new BuildFailure(ExitCode.Arguments,"PLAN_CONTENT","Unsupported dependency content group: " + task.Content);
                if (!File.Exists(task.InputPath)) throw new BuildFailure(ExitCode.Arguments,"PLAN_INPUT_MISSING","Dependency input does not exist: " + task.InputPath);
                BuildTask old;
                if (tasks.TryGetValue(task.Id,out old)) {
                    if (!string.Equals(Path.GetFullPath(old.InputPath),Path.GetFullPath(task.InputPath),StringComparison.OrdinalIgnoreCase) || old.Content != task.Content || old.OutputName != task.OutputName || !old.Dependencies.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).SequenceEqual(task.Dependencies.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))
                        throw new BuildFailure(ExitCode.Arguments,"PLAN_COLLISION","Conflicting task contexts share an id: " + task.Id);
                    continue;
                }
                if (tasks.Values.Any(x=>string.Equals(x.OutputName,task.OutputName,StringComparison.OrdinalIgnoreCase) || (x.Content==task.Content && string.Equals(Path.GetFileNameWithoutExtension(x.InputPath),Path.GetFileNameWithoutExtension(task.InputPath),StringComparison.OrdinalIgnoreCase))))
                    throw new BuildFailure(ExitCode.Arguments,"PLAN_COLLISION","Two task contexts would write the same stream workspace: " + task.OutputName);
                tasks.Add(task.Id,task);
            }
            var ordered = new List<BuildTask>(); var pending = new HashSet<string>(tasks.Keys,StringComparer.OrdinalIgnoreCase);
            while (pending.Count > 0) {
                foreach (string id in pending) foreach (string dependency in tasks[id].Dependencies)
                    if (!tasks.ContainsKey(dependency)) throw new BuildFailure(ExitCode.Arguments,"PLAN_DEPENDENCY_MISSING","Unknown dependency " + dependency + " of " + id);
                var ready = pending.Where(id=>tasks[id].Dependencies.All(d=>!pending.Contains(d))).OrderBy(id=>Priority(tasks[id].Content)).ThenBy(id=>id,StringComparer.OrdinalIgnoreCase).ThenBy(id=>id,StringComparer.Ordinal).ToArray();
                if (ready.Length == 0) throw new BuildFailure(ExitCode.Arguments,"PLAN_CYCLE","Cycle in stream dependency graph.");
                foreach (string id in ready) { ordered.Add(tasks[id]); pending.Remove(id); }
            }
            return ordered.ToArray();
        }
        static int Priority(string content) { return content == "base" ? 0 : content == "apt" ? 1 : content == "maps" ? 2 : 3; }
    }
}
