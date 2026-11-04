using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    // SDK/defaultscript.cs step 8: SDK/additional, then MOD/additional,
    // recursive File.Copy(..., true) into the built MOD root. Step 9's
    // MakeBig -x:*.asset excludes asset files from the final archive.
    public static class AdditionalFiles
    {
        sealed class Input { public string Name, Path; }
        sealed class InputList : List<BigArchiveEntry>
        {
            internal readonly HashSet<string> Directories, Names;
            internal InputList(HashSet<string> directories,IEnumerable<string> names)
            { Directories=directories;Names=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase); }
        }
        public static void Merge(string sdkRoot,string modRoot,IList<BigArchiveEntry> entries,CancellationToken cancellation,Action<string> warning = null)
        {
            if(entries==null) throw new ArgumentNullException("entries");
            Merge(entries,Inputs(sdkRoot,modRoot,cancellation,warning),cancellation,warning);
        }
        internal static IList<BigArchiveEntry> Inputs(string sdkRoot,string modRoot,CancellationToken cancellation,Action<string> warning = null)
        {
            cancellation.ThrowIfCancellationRequested();
            var files=new Dictionary<string,Input>(StringComparer.OrdinalIgnoreCase);
            var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string root in new[]{sdkRoot,modRoot}) {
                string additional=Path.Combine(Path.GetFullPath(root),"Additional");
                if(Directory.Exists(additional)) Discover(additional,"",files,directories,cancellation,warning);
                else if(File.Exists(additional)) throw new IOException("Additional input must be a directory.");
            }
            var result=new InputList(directories,files.Keys);
            ValidateNames(result.Names,directories);
            foreach(var file in files.Values.OrderBy(value=>value.Name,StringComparer.Ordinal))
                if(!string.Equals(Path.GetExtension(file.Name),".asset",StringComparison.OrdinalIgnoreCase))
                    result.Add(new BigArchiveEntry{Name=file.Name,SourcePath=file.Path});
            return result;
        }
        internal static void Validate(IList<BigArchiveEntry> inputs,IEnumerable<string> archiveNames,Action<string> warning = null)
        {
            var discovered=(InputList)inputs;
            var archive=archiveNames.Select(Canonical).ToArray();
            var names=new HashSet<string>(archive,StringComparer.OrdinalIgnoreCase);
            names.UnionWith(discovered.Names);
            ValidateNames(names,discovered.Directories);
            if(warning!=null) {
                var replacements=new HashSet<string>(inputs.Select(input=>input.Name),StringComparer.OrdinalIgnoreCase);
                foreach(string name in archive)
                    if(replacements.Remove(name))warning("Additional overwrites compiled package member: "+name);
            }
        }
        static void ValidateNames(ISet<string> names,ISet<string> directories)
        {
            // CopyFolders cannot copy a directory over a file, or a file over
            // a directory. Include empty source directories in this check.
            foreach(string name in names) {
                if(directories.Contains(name)) throw new IOException("Additional file/directory collision: "+name);
                for(int split=name.LastIndexOf('\\');split>=0;split=name.LastIndexOf('\\',split-1)) {
                    if(names.Contains(name.Substring(0,split))) throw new IOException("Additional file/directory collision: "+name);
                    if(split==0) break;
                }
            }
        }
        internal static void Merge(IList<BigArchiveEntry> entries,IList<BigArchiveEntry> inputs,CancellationToken cancellation,Action<string> warning = null)
        {
            cancellation.ThrowIfCancellationRequested();
            var discovered=(InputList)inputs;
            if(discovered.Names.Count==0 && discovered.Directories.Count==0)return;
            Validate(inputs,entries.Select(entry=>entry.Name),warning);
            var replacements=inputs.ToDictionary(file=>file.Name,file=>file,StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<entries.Count;i++) {
                BigArchiveEntry replacement;
                string name=Canonical(entries[i].Name);
                if(replacements.TryGetValue(name,out replacement)) {entries[i]=replacement;replacements.Remove(name);}
            }
            foreach(var item in replacements.OrderBy(pair=>pair.Key,StringComparer.Ordinal)) entries.Add(item.Value);
        }
        static void Discover(string directory,string relative,IDictionary<string,Input> files,ISet<string> directories,CancellationToken cancellation,Action<string> warning)
        {
            cancellation.ThrowIfCancellationRequested();
            if(relative.Length!=0) {
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Additional input directory cannot be a reparse point.");
                directories.Add(Canonical(relative));
            }
            foreach(string file in Directory.GetFiles(directory).OrderBy(value=>value,StringComparer.Ordinal)) {
                cancellation.ThrowIfCancellationRequested();
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new IOException("Additional input cannot be a reparse point.");
                string name=Canonical(relative+Path.GetFileName(file));
                Input old;
                if(warning!=null && files.TryGetValue(name,out old) && !string.Equals(Path.GetExtension(name),".asset",StringComparison.OrdinalIgnoreCase))
                    warning("Additional overwrite: " + name + " (" + old.Path + " -> " + file + ")");
                files[name]=new Input{Name=name,Path=file};
            }
            foreach(string child in Directory.GetDirectories(directory).OrderBy(value=>value,StringComparer.Ordinal))
                Discover(child,relative+Path.GetFileName(child)+"\\",files,directories,cancellation,warning);
        }
        static string Canonical(string name) { return name.Replace('/','\\').TrimEnd('\\').ToLowerInvariant(); }
    }
}
