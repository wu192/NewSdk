using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    internal static class EventBufferingTests
    {
        static int checks;
        static void Check(bool value,string message) { checks++;if(!value)throw new Exception("Event buffering: "+message); }
        static IDictionary<string,object> Event(string type,string level="info")
        { return new Dictionary<string,object> {{"eventType",type},{"level",level}}; }
        static string ReadVisible(string path)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var text=new StreamReader(stream))return text.ReadToEnd();
        }
        public static int Run(string root)
        {
            checks=0;
            var policy=new EventFlushPolicy(100);
            for(int tick=100;tick<350;tick++)Check(!policy.Due(Event("Diagnostic"),tick),"Routine trace forced a per-event flush");
            Check(policy.Due(Event("Progress"),350),"Periodic progress did not become visible");
            Check(!policy.Due(Event("Progress"),351),"Periodic flush did not advance its deadline");
            foreach(string type in new[] {"BuildStarted","StageStarted","StreamStarted","StreamCompleted","StageCompleted","BuildFinished"})
                Check(policy.Due(Event(type),352),type+" boundary was buffered");
            Check(policy.Due(Event("Diagnostic","warning"),353) && policy.Due(Event("Diagnostic","error"),354),"Warning/error was buffered");
            policy=new EventFlushPolicy(int.MaxValue-100);
            Check(!policy.Due(Event("Diagnostic"),unchecked(int.MaxValue+100)),"Tick wrap prematurely flushed a trace");
            Check(policy.Due(Event("Diagnostic"),unchecked(int.MaxValue+150)),"Tick wrap hid overdue progress");

            Directory.CreateDirectory(root);string json=Path.Combine(root,"events.jsonl"),text=Path.Combine(root,"build.log");
            using(var events=new EventDispatcher("buffered-events",new IBuildEventSink[] {new JsonLineSink(json),new TextLogSink(text)})) {
                events.Emit("BuildStarted");
                Check(ReadVisible(json).Contains("BuildStarted") && ReadVisible(text).Contains("BuildStarted"),"Fresh build identity was not immediately visible");
                events.Emit("Diagnostic",message:"buffered trace before stream");events.Emit("StreamStarted");
                Check(ReadVisible(json).Contains("buffered trace before stream") && ReadVisible(text).Contains("StreamStarted"),"Stream cancellation boundary did not flush preceding records");
                for(int index=0;index<256;index++)events.Emit("Diagnostic",message:"trace "+index);
                events.Emit("BuildFinished");
                Check(ReadVisible(json).Contains("BuildFinished") && ReadVisible(text).Contains("trace 255"),"Terminal boundary did not publish buffered output");
            }
            string[] rows=File.ReadAllLines(json);Check(rows.Length==260 && File.ReadAllLines(text).Length==260,"Batching omitted or duplicated events");
            var serializer=new JavaScriptSerializer();int sequence=0;
            foreach(string row in rows)Check(Convert.ToInt32(serializer.Deserialize<Dictionary<string,object>>(row)["sequence"])==++sequence,"Batching changed event order");
            var optional = new TextLogSink(Path.Combine(root,"optional.log")); optional.Dispose();
            string required = Path.Combine(root,"required.jsonl");
            using (var dispatcher = new EventDispatcher("optional-log-failure",new IBuildEventSink[] {optional,new JsonLineSink(required)})) {
                dispatcher.Emit("BuildStarted"); dispatcher.Emit("BuildFinished");
                dispatcher.Dispose();
                Check(!dispatcher.OutputFailed && !dispatcher.CloseFailed,"Optional text failure changed the required output outcome");
            }
            Check(File.ReadAllLines(required).Length==2,"Optional text failure lost required JSONL events");
            TextWriter previous=Console.Error;var console=new StringWriter();
            try {
                Console.SetError(console);
                using(var events=new EventDispatcher("console-summary",new IBuildEventSink[0])) {
                    events.Emit("Diagnostic",message:"routine trace",code:"CORE_TRACE");
                    events.Emit("Diagnostic","debug",message:"verbose trace",code:"CORE_TRACE");
                    events.Emit("Diagnostic","warning",message:"visible warning");
                    events.Emit("Diagnostic","error",message:"visible error");
                    events.Emit("BuildFinished",message:"visible completion");
                }
            } finally { Console.SetError(previous); }
            string visible=console.ToString();
            Check(!visible.Contains("routine trace") && !visible.Contains("verbose trace"),"Routine traces still performed synchronous console writes");
            Check(visible.Contains("visible warning") && visible.Contains("visible error") && visible.Contains("visible completion"),"Console summary hid a warning/error or terminal outcome");
            return checks;
        }
    }
}
