using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Web.Script.Serialization;
using NewModCompiler.Application;

namespace NewModCompiler.Diagnostics
{
    // Check writable paths at the boundary. Directory handles are used only
    // to resolve physical aliases, never held for a build or a log's lifetime.
    internal static class DiagnosticDirectory
    {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint length,uint flags);
        internal static void ValidateSyntax(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("Diagnostic path is empty.");
            string separators=path.Replace('/','\\');
            if (separators.StartsWith(@"\\?\",StringComparison.Ordinal) || separators.StartsWith(@"\\.\",StringComparison.Ordinal)) throw new IOException("Device paths are unavailable for diagnostic output: " + path);
            int start=path.Length>=2 && char.IsLetter(path[0]) && path[1]==':' ? 2 : 0;
            if (start==2 && (path.Length==2 || (path[2]!='\\' && path[2]!='/'))) throw new IOException("Drive-relative diagnostic paths are unavailable: " + path);
            foreach (string part in path.Substring(start).Split(new [] {'\\','/'})) {
                if (part.Length==0 || part=="." || part=="..") continue;
                string name=part.Split('.')[0];
                if (part.EndsWith(".",StringComparison.Ordinal) || part.EndsWith(" ",StringComparison.Ordinal) || part.IndexOf(':')>=0 ||
                    string.Equals(name,"CON",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"PRN",StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name,"AUX",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"NUL",StringComparison.OrdinalIgnoreCase) ||
                    (name.Length==4 && (name.StartsWith("COM",StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT",StringComparison.OrdinalIgnoreCase)) && name[3]>='1' && name[3]<='9'))
                    throw new IOException("Ambiguous or device diagnostic path component: " + part);
            }
        }
        internal static void CheckDirectory(string directory, string stopAt = null)
        {
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Output path contains a reparse point: " + current.FullName);
                if (string.Equals(current.FullName, stopAt, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        internal static void PrepareDirectory(string directory, string stopAt = null)
        {
            CheckDirectory(directory, stopAt);
            Directory.CreateDirectory(directory);
        }
        internal static string ResolveInputDirectory(string path)
        {
            using (var handle = CreateFile(Path.GetFullPath(path),0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new IOException("Cannot resolve directory: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
                var value=new StringBuilder(32768);
                uint count=GetFinalPathNameByHandle(handle,value,(uint)value.Capacity,0);
                if (count==0 || count>=value.Capacity) throw new IOException("Cannot resolve physical path: " + path);
                string physical=value.ToString();
                if (physical.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)) return @"\\"+physical.Substring(8);
                return physical.StartsWith(@"\\?\",StringComparison.Ordinal) ? physical.Substring(4) : physical;
            }
        }
        internal static string ResolveOutput(string path)
        {
            ValidateSyntax(path); path=Path.GetFullPath(path);
            string directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            string suffix = Directory.Exists(path) ? "" : Path.GetFileName(path);
            while (!Directory.Exists(directory))
            {
                if (string.IsNullOrEmpty(directory)) throw new IOException("Output volume or directory root is unavailable: " + path);
                if (File.Exists(directory)) throw new IOException("Output ancestor is a file: " + directory);
                string parent=Path.GetDirectoryName(directory);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent,directory,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Output volume or directory root is unavailable: " + path);
                suffix=Path.Combine(Path.GetFileName(directory),suffix); directory=parent;
            }
            CheckDirectory(directory);
            DiagnosticFiles.RejectReparseFile(path);
            return Path.Combine(ResolveInputDirectory(directory),suffix);
        }
    }
    internal static class DiagnosticFiles
    {
        internal static void RejectReparseFile(string path)
        {
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint)!=0) throw new IOException("Output file is a reparse point: " + path); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
        internal static FileStream CreateFresh(string path)
        {
            DiagnosticDirectory.PrepareDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                RejectReparseFile(path);
                // Replace the directory entry instead of truncating an existing
                // inode: a diagnostic destination may be a hard link to input.
                using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { }
                File.Delete(path);
                File.Move(temporary,path);
                return new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.Read);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public interface IBuildEventSink : IDisposable { void Write(IDictionary<string, object> value); }
    internal sealed class EventFlushPolicy
    {
        int nextFlush;
        internal EventFlushPolicy() : this(Environment.TickCount) { }
        internal EventFlushPolicy(int now) { nextFlush = unchecked(now + 250); }
        public bool Due(IDictionary<string, object> value, int now)
        {
            string type = (string)value["eventType"], level = (string)value["level"];
            if (type.EndsWith("Started", StringComparison.Ordinal) || type.EndsWith("Completed", StringComparison.Ordinal) ||
                type == "BuildFinished" || level == "error" || level == "warning" || unchecked(now - nextFlush) >= 0)
            {
                nextFlush = unchecked(now + 250);
                return true;
            }
            return false;
        }
    }
    public sealed class JsonLineSink : IBuildEventSink
    {
        readonly StreamWriter writer; readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly EventFlushPolicy flush = new EventFlushPolicy();
        public JsonLineSink(string path) { writer = new StreamWriter(DiagnosticFiles.CreateFresh(path), new UTF8Encoding(false), 64 * 1024); }
        public void Write(IDictionary<string, object> value) { writer.WriteLine(json.Serialize(value)); if (flush.Due(value, Environment.TickCount)) writer.Flush(); }
        public void Dispose() { writer.Dispose(); }
    }
    public sealed class TextLogSink : IBuildEventSink
    {
        readonly StreamWriter writer;
        readonly EventFlushPolicy flush = new EventFlushPolicy();
        public TextLogSink(string path) { writer = new StreamWriter(DiagnosticFiles.CreateFresh(path), new UTF8Encoding(false), 64 * 1024); }
        public void Write(IDictionary<string, object> value) { writer.WriteLine("{0} [{1}] #{2} {3} {4} {5}", value["timestampUtc"],value["buildId"],value["sequence"],value["level"],value["eventType"],value.ContainsKey("message") ? value["message"] : ""); if (flush.Due(value, Environment.TickCount)) writer.Flush(); }
        public void Dispose() { writer.Dispose(); }
    }
    public sealed class EventDispatcher : IDisposable
    {
        readonly List<IBuildEventSink> sinks; readonly Stopwatch clock = Stopwatch.StartNew();
        long sequence; bool finished; bool outputFailed; bool disposed;
        internal ConsoleProgressRenderer ConsoleOutput { get; set; }
        internal void WriteConsoleLine(string message)
        {
            try { if (ConsoleOutput != null) ConsoleOutput.WriteLine(message); else Console.Error.WriteLine(message); }
            catch (IOException) { }
        }
        public string BuildId { get; private set; }
        public bool OutputFailed { get { return outputFailed; } }
        public bool CloseFailed { get; private set; }
        public string CloseFailureMessage { get; private set; }
        public long ElapsedMilliseconds { get { return clock.ElapsedMilliseconds; } }
        public EventDispatcher(string buildId, IEnumerable<IBuildEventSink> sinks) { BuildId = buildId; this.sinks = new List<IBuildEventSink>(sinks); }
        public void Emit(string type, string level = "info", object payload = null, string message = null, string code = null, string streamId = null, string stageId = null)
        {
            if (disposed) throw new ObjectDisposedException("EventDispatcher");
            if (finished) throw new InvalidOperationException("No event may follow BuildFinished.");
            if (type == "BuildFinished") finished = true;
            var value = new Dictionary<string, object> { {"schemaVersion","1.0"}, {"buildId",BuildId}, {"sequence",++sequence}, {"timestampUtc",DateTime.UtcNow.ToString("o")}, {"elapsedMs",clock.ElapsedMilliseconds}, {"eventType",type}, {"level",level} };
            if (payload != null) value["payload"] = payload;
            if (message != null) value["message"] = message;
            if (code != null) value["diagnosticCode"] = code;
            if (streamId != null) value["streamId"] = streamId;
            if (stageId != null) value["stageId"] = stageId;
            Exception failure = null;
            for (int index = 0; index < sinks.Count; index++)
            {
                var sink = sinks[index];
                try { sink.Write(value); }
                catch (Exception error)
                {
                    if (!(sink is TextLogSink)) { outputFailed = true; failure = error; continue; }
                    // Human-readable text is optional; JSONL and the final
                    // report remain the required machine-readable outputs.
                    try { sink.Dispose(); } catch { }
                    sinks.RemoveAt(index--);
                    WriteConsoleLine("Optional text log disabled: " + error.Message);
                }
            }
            // Console is optional; a disconnected caller must not hide the durable final result.
            if ((type == "BuildFinished" && ConsoleOutput == null) || (type == "Diagnostic" && (level == "error" || level == "warning")))
                WriteConsoleLine(string.Format("[{0}] {1} {2}",level,code ?? type,message ?? ""));
            if (failure != null) throw new BuildFailure(ExitCode.Output,"EVENT_WRITE_FAILED",failure.Message);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed=true;
            foreach (var sink in sinks) {
                try { sink.Dispose(); }
                catch (Exception e) { if (!(sink is TextLogSink)) { outputFailed=true; CloseFailed=true; CloseFailureMessage=e.Message; } }
            }
        }
    }
    public static class Reports
    {
        public static void RemoveStale(string path)
        {
            DiagnosticDirectory.PrepareDirectory(Path.GetDirectoryName(path));
            // Unlinking a hard link cannot modify its source inode.
            File.Delete(path);
        }
        public static void WriteAtomic(string path, IDictionary<string, object> report)
        {
            DiagnosticDirectory.PrepareDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                DiagnosticFiles.RejectReparseFile(path);
                using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using (var writer = new StreamWriter(stream,new UTF8Encoding(false),64 * 1024))
                    writer.Write(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(report));
                if (File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
