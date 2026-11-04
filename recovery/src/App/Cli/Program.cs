using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Text;
using NewModCompiler.Application;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Progress uses Chinese labels on both redirected and real consoles.
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }
#if NEWSdk_MONOLITHIC
            BinaryAssetBuilder.UiCompiler.UiEmbeddedDependencies.Initialize();
#endif
            if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h")) { Console.WriteLine(Options.Help); return 0; }
            if (args.Length == 1 && args[0] == "--help-advanced") { Console.WriteLine(Options.AdvancedHelp); return 0; }
            var sinks = new List<IBuildEventSink>();
            try {
                BuildRequest request = Options.Parse(args);
                if (request.Command == "doctor") {
#if NEWSdk_MONOLITHIC
                    CoreStreamCompiler.ProbeEnvironment(request.SdkRoot);
                    Console.WriteLine("CORE_ENVIRONMENT_OK: SDK configuration/schema, Win32 providers/verifier, ANTLR and built-in texture initialized. No stream compiled.");
                    return (int)ExitCode.Success;
#else
                    Console.Error.WriteLine("SDK directory exists: " + request.SdkRoot);
                    Console.Error.WriteLine("BACKEND_NOT_INTEGRATED: Standalone application skeleton cannot validate runtime providers or compile assets.");
                    return (int)ExitCode.Environment;
#endif
                }
#if NEWSdk_MONOLITHIC
                CoreStreamCompiler.PreflightInputRoots(request);
#endif
                // Invalidate a previous report before opening any required output channels.
                Reports.RemoveStale(request.ReportPath);
                sinks.Add(new JsonLineSink(request.EventsPath));
                try { sinks.Add(new TextLogSink(request.LogPath)); }
                catch (IOException error) { try { Console.Error.WriteLine("Optional text log unavailable: " + error.Message); } catch (IOException) { } }
                catch (UnauthorizedAccessException error) { try { Console.Error.WriteLine("Optional text log unavailable: " + error.Message); } catch (IOException) { } }
                // Transfer ownership so partial-startup cleanup cannot close successful
                // session sinks a second time after the exit code has been finalized.
                var ownedSinks=sinks.ToArray(); sinks.Clear();
                return RunSession(request,ownedSinks);
            } catch (BuildFailure e) { Console.Error.WriteLine(e.Code + ": " + e.Message); return (int)e.ExitCode; }
            catch (IOException e) { Console.Error.WriteLine("OUTPUT_FAILED: " + e.Message); return (int)ExitCode.Output; }
            catch (UnauthorizedAccessException e) { Console.Error.WriteLine("OUTPUT_FAILED: " + e.Message); return (int)ExitCode.Output; }
            catch (Exception e) { Console.Error.WriteLine("INTERNAL_ERROR: " + e); return (int)ExitCode.Internal; }
            finally { foreach (IBuildEventSink sink in sinks) { try { sink.Dispose(); } catch { } } }
        }
        internal static int RunSession(BuildRequest request,IEnumerable<IBuildEventSink> sinks)
        {
            using (var events = new EventDispatcher(Guid.NewGuid().ToString("N"),sinks))
            using (var cancellation = new CancellationTokenSource()) {
                // Default progress is independent of optional performance sampling.
                // The opt-out also removes Core callbacks for paired measurements.
                var progress = string.Equals(Environment.GetEnvironmentVariable("NEWSDK_PROGRESS"),"off",StringComparison.OrdinalIgnoreCase)
                    ? null : new BuildProgressState(() => events.ElapsedMilliseconds);
                using (var renderer = progress == null ? null : new ConsoleProgressRenderer(progress)) {
                    events.ConsoleOutput = renderer;
                    ConsoleCancelEventHandler handler = (sender,e) => { e.Cancel=true; cancellation.Cancel(); };
                    Console.CancelKeyPress += handler;
                    BuildSession session = null;
                    IStreamCompiler compiler = null;
#if NEWSdk_MONOLITHIC
                    if (request.Command == "build")
                        compiler = new CoreStreamCompiler((source,eventType,message) => session.RecordCoreTrace(source,eventType,message),progress);
#endif
                    session = new BuildSession(request,compiler,events,progress:progress);
                    try { return (int)session.Run(cancellation.Token); }
                    finally { Console.CancelKeyPress -= handler; }
                }
            }
        }
    }
}
