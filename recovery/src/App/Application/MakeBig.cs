using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// Recovered from SDK/tools/MakeBig.exe (EA Los Angeles, 2007), pure managed
// mscorlib/System 2.0. Method RVAs and intentional integration differences are
// recorded in recovery/MAKEBIG_RECOVERY.md. No original MakeBig process is used.
namespace NewModCompiler.Packaging.MakeBigRecovery
{
    sealed class FixupRecord:IComparable
    {
        readonly object key;readonly uint offset;readonly bool endianFlip;uint fixupValue=uint.MaxValue;
        public object Key {get{return key;}}
        public uint Offset {get{return offset;}}
        public uint Value {get{return fixupValue;}set{if(IsValid)throw new Exception("Fixup value already specified");fixupValue=value;}}
        public bool EndianFlip {get{return endianFlip;}}
        public bool IsValid {get{return fixupValue!=uint.MaxValue;}}
        public FixupRecord(object key,uint offset,bool endianFlip) {this.key=key;this.offset=offset;this.endianFlip=endianFlip;}
        public int CompareTo(object other) {return unchecked((int)(Offset-((FixupRecord)other).Offset));}
    }
    public sealed class FixupBinaryWriter
    {
        readonly BinaryWriter outputWriter;readonly Hashtable fixups=new Hashtable();bool endianFlip;
        public bool EndianFlip {get{return endianFlip;}set{endianFlip=value;}}
        public FixupBinaryWriter(Stream output) {outputWriter=new BinaryWriter(output,Encoding.UTF8,true);}
        public void WriteBookmark(object key,Type type) {
            if(type!=typeof(uint))throw new Exception("Only uint's current supported as bookmarks for FixupBinaryWriter");
            if(fixups.ContainsKey(key))throw new Exception("Bookmark reference already reserved");
            fixups.Add(key,new FixupRecord(key,(uint)outputWriter.Seek(0,SeekOrigin.Current),endianFlip));outputWriter.Write(0u);
        }
        public void ResolveBookmark(object key,object value) {
            if(value.GetType()!=typeof(uint))throw new Exception("Only uint's current supported as bookmarks for FixupBinaryWriter");
            var record=(FixupRecord)fixups[key];if(record==null)throw new Exception("Invalid bookmark reference");record.Value=(uint)value;
        }
        public long Seek(int offset,SeekOrigin origin) {return outputWriter.Seek(offset,origin);}
        public void Write(uint value) {if(endianFlip)value=((value&255)<<24)+((value&65280)<<8)+((value&16711680)>>8)+((value&4278190080)>>24);outputWriter.Write(value);}
        public void Write(byte[] bytes,int index,int count) {outputWriter.Write(bytes,index,count);}
        public void Write(char[] chars) {outputWriter.Write(chars);}
        public void Write(char value) {outputWriter.Write(value);}
        public void WriteBookmarks() {
            bool previous=endianFlip;var records=new FixupRecord[fixups.Count];fixups.Values.CopyTo(records,0);Array.Sort(records);
            foreach(var record in records) {if(!record.IsValid)throw new Exception("Fixup not set! - "+record.Key);endianFlip=record.EndianFlip;Seek(unchecked((int)record.Offset),SeekOrigin.Begin);Write(record.Value);}
            endianFlip=previous;
        }
        internal void Flush() {outputWriter.Flush();}
    }
    internal sealed class MakeBigInput {public string Name,SourcePath,BookmarkKey;public long Length;}
    public sealed class Lumpy
    {
        readonly List<string> files=new List<string>();byte[] rawDataBuffer;
        public string BaseDirectory {get;set;}
        public string Filename {get;set;}
        public List<string> Files {get{return files;}}
        public bool Silent {get;set;}
        public bool Verbose {get;set;}
        public byte[] RawDataBuffer(int size) {if(rawDataBuffer==null || rawDataBuffer.Length<size)rawDataBuffer=new byte[checked(size*2)];return rawDataBuffer;}
        public void BuildLumpy() {BuildLumpy(CancellationToken.None,Console.Out);}
        public void BuildLumpy(CancellationToken cancellation,TextWriter log) {
            if(File.Exists(Filename))throw new Exception(string.Format("File {0} already exists",Filename));
            string directory=Path.GetDirectoryName(Filename);if(!Directory.Exists(directory))Directory.CreateDirectory(directory);
            var inputs=new List<MakeBigInput>();
            foreach(string file in files) inputs.Add(new MakeBigInput {SourcePath=file,Name=file.Substring(BaseDirectory.Length+1).ToLower().Replace('/','\\'),Length=new FileInfo(file).Length});
            using(var output=new FileStream(Filename,FileMode.Create))WriteArchive(output,inputs,cancellation,Silent,Verbose,log);
        }
        // Recovered Lumpy's bookmark/endian/UTF8 format is the production writer.
        // The integrated copy streams bounded chunks rather than allocating twice
        // the largest file. Entry order is supplied, never changed by this emitter.
        internal static void WriteArchive(Stream output,IList<MakeBigInput> inputs,CancellationToken cancellation,bool silent=true,bool verbose=false,TextWriter log=null)
        {
            var writer=new FixupBinaryWriter(output);writer.Write("BIG4".ToCharArray());writer.WriteBookmark("filelength",typeof(uint));writer.EndianFlip=true;writer.Write((uint)inputs.Count);writer.WriteBookmark("headerlength",typeof(uint));
            foreach(var input in inputs) {
                cancellation.ThrowIfCancellationRequested();writer.WriteBookmark("offset."+(input.BookmarkKey??input.SourcePath),typeof(uint));writer.WriteBookmark("size."+(input.BookmarkKey??input.SourcePath),typeof(uint));writer.Write(input.Name.ToCharArray());writer.Write('\0');
                if(verbose && log!=null)log.WriteLine("File: {0}",input.Name);
            }
            writer.Write("L219".ToCharArray());writer.Write(0u);writer.ResolveBookmark("headerlength",checked((uint)output.Position));
            var buffer=new byte[64*1024];int copied=0;
            foreach(var input in inputs) {
                cancellation.ThrowIfCancellationRequested();
                writer.ResolveBookmark("offset."+(input.BookmarkKey??input.SourcePath),checked((uint)output.Position));
                using(var source=new FileStream(input.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    if(source.Length!=input.Length)throw new IOException("A BIG input changed length before it was copied: "+input.Name);
                    writer.ResolveBookmark("size."+(input.BookmarkKey??input.SourcePath),checked((uint)input.Length));long remaining=input.Length;
                    while(remaining!=0) {cancellation.ThrowIfCancellationRequested();int read=source.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(read==0)throw new EndOfStreamException("Failed to read file "+input.SourcePath);writer.Write(buffer,0,read);remaining-=read;}
                    if(source.ReadByte()!=-1)throw new IOException("A BIG input grew while it was copied.");
                    if(verbose && log!=null)log.WriteLine(input.SourcePath);
                }
                copied++;if(!silent && copied%20==0 && log!=null)log.WriteLine("{0} Files added",copied);
            }
            long length=output.Position;writer.ResolveBookmark("filelength",checked((uint)length));writer.WriteBookmarks();writer.Flush();output.Position=length;
        }
    }
    public sealed class ParseArguments
    {
        string baseDirectory="",filename="";readonly List<Regex> excludeList=new List<Regex>();
        public string BaseDirectory {get{return baseDirectory;}}
        public string Filename {get{return filename;}}
        public bool PrintHelp {get;private set;}
        public bool Silent {get;private set;}
        public bool Verbose {get;private set;}
        public bool Force {get;private set;}
        public List<Regex> ExcludeList {get{return excludeList;}}
        public static string WildcardToRegex(string wildcard) {return "^"+Regex.Escape(wildcard).Replace("\\*",".*").Replace("\\?",".")+"$";}
        public void parse(string[] args) {
            char[] trim={' ','"'};
            foreach(string arg in args) {
                if(arg.StartsWith("-")) {
                    int colon=arg.IndexOf(':');string key=colon<0?arg.Substring(1).Trim(trim):arg.Substring(1,colon-1);string value=colon<0?"":arg.Substring(colon+1).Trim(trim);
                    switch(key.ToLower()) {
                        case "h":case "-help":PrintHelp=true;break;
                        case "q":case "-quiet":Silent=true;break;
                        case "v":case "-verbose":Verbose=true;break;
                        case "f":case "-force":Force=true;break;
                        case "x":case "-exclude":excludeList.Add(new Regex(WildcardToRegex(value.ToLower())));break;
                        case "o":case "-outputfile":filename=Path.GetFullPath(value);break;
                    }
                } else {baseDirectory=Path.GetFullPath(arg);if(baseDirectory[baseDirectory.Length-1]=='\\')baseDirectory=baseDirectory.Substring(0,baseDirectory.Length-1);}
            }
        }
    }
    public static class Program
    {
        // Embedding adaptation: Run rather than a second executable entrypoint;
        // fresh parser per call, explicit console channels, cooperative cancellation.
        public static int Run(string[] args,TextReader input,TextWriter log,CancellationToken cancellation=default(CancellationToken)) {
            var arguments=new ParseArguments();arguments.parse(args);
            if(arguments.PrintHelp){PrintHelp(log);return 0;}
            if(arguments.BaseDirectory=="" || arguments.Filename=="") {if(!arguments.Silent)log.WriteLine("Please provide a directory and name for the big file.  See help (-h) for more information.");return 1;}
            if(!Directory.Exists(arguments.BaseDirectory)) {if(!arguments.Silent)log.WriteLine("{0} No such directory",arguments.BaseDirectory);return 1;}
            if(File.Exists(arguments.Filename)) {
                if(arguments.Force)File.Delete(arguments.Filename);
                else {
                    if(arguments.Silent)return 1;
                    log.Write("{0} File Exists, do you want to overwrite [y/n][n]:",arguments.Filename);
                    // Preserve the original two-read uppercase-Y quirk.
                    if(input.ReadLine()=="y" || input.ReadLine()=="Y")File.Delete(arguments.Filename);
                    else {log.WriteLine("Unable to complete operation: {0} file already exists",arguments.Filename);return 1;}
                }
            }
            var lumpy=new Lumpy();GetDirectories(new DirectoryInfo(arguments.BaseDirectory),lumpy,arguments,cancellation);
            if(arguments.Verbose) {log.WriteLine("Big File: {0}",arguments.Filename);log.WriteLine("Base Dir: {0}",arguments.BaseDirectory);}
            lumpy.Filename=arguments.Filename;lumpy.BaseDirectory=arguments.BaseDirectory;lumpy.Silent=arguments.Silent;lumpy.Verbose=!arguments.Silent && arguments.Verbose;lumpy.BuildLumpy(cancellation,log);return 0;
        }
        public static void GetDirectories(DirectoryInfo directory,Lumpy lumpy,ParseArguments arguments,CancellationToken cancellation=default(CancellationToken)) {
            cancellation.ThrowIfCancellationRequested();
            if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("MakeBig directory discovery cannot follow reparse points.");
            foreach(var child in directory.GetDirectories())GetDirectories(child,lumpy,arguments,cancellation);
            foreach(var file in directory.GetFiles()) {
                cancellation.ThrowIfCancellationRequested();if((file.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("MakeBig discovery cannot read reparse-point files.");
                string name=file.FullName.ToLower();bool excluded=false;foreach(Regex pattern in arguments.ExcludeList)if(pattern.IsMatch(name)){excluded=true;break;}
                if(!excluded)lumpy.Files.Add(name);
            }
        }
        public static void PrintHelp(TextWriter log) {
            log.WriteLine("Create a big file from a directory");log.WriteLine("usage: MakeBig.exe [-fhqvxo] [directory]");
            log.WriteLine("-f --force\tforces yes to all questions");log.WriteLine("-h --help\tprints this help screen");log.WriteLine("-q --quiet\tsupresses all output (overrides verbose)");log.WriteLine("-v --verbose\tverbose mode");log.WriteLine("-x --exclude\t exclude via wildcard for example -x:*.txt");log.WriteLine("-o --outputFile\t the name of the big File you want to create for example: -o:c:\\cnc3mods\\samplemod.big");log.WriteLine("directory\tPath to the directory to be archived into a big file");
        }
    }
}
namespace NewModCompiler.Application
{
    public static class MakeBig
    {
        // Production uses the confirmed exact artifact inventory, never directory
        // discovery over shared staging/intermediate/cache or old output trees.
        public static void Write(string path,IEnumerable<BigArchiveEntry> entries,CancellationToken cancellation=default(CancellationToken)) {BigArchiveWriter.Write(path,entries,cancellation);}
    }
}
