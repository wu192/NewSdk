using System;
using System.Threading;

namespace NewModCompiler.Diagnostics
{
    // Only lifecycle transitions notify the renderer. Core work callbacks update
    // counters without allocating, taking locks, or touching terminal output.
    public sealed class BuildProgressState
    {
        sealed class Work
        {
            internal long Assets, Xml, Reused;
        }
        sealed class Context
        {
            internal string Stage, Stream, Phase;
            internal long StageStarted, StreamStarted;
            internal Work Work;
        }
        readonly Func<long> elapsed;
        Context current = new Context { Work = new Work() };
        int completed, total;
        bool finished;
        internal event Action<BuildProgressSnapshot, string, string> Boundary;

        public BuildProgressState(Func<long> elapsedMilliseconds) { elapsed = elapsedMilliseconds; }
        public void SetPlan(int totalStreams) { Volatile.Write(ref total,totalStreams); }
        public void StartStage(string id)
        {
            var next = new Context { Stage=id, StageStarted=elapsed(), Work=new Work() };
            Volatile.Write(ref current,next); Notify("stage-started",null);
        }
        public void CompleteStage(string status)
        {
            BuildProgressSnapshot value=Snapshot();
            Volatile.Write(ref current,new Context { Work=new Work() });
            Notify(value,"stage-completed",status);
        }
        public void StartStream(string id)
        {
            Context previous=Volatile.Read(ref current);
            Volatile.Write(ref current,new Context { Stage=previous.Stage, StageStarted=previous.StageStarted,
                Stream=id, StreamStarted=elapsed(), Work=new Work() });
            Notify("stream-started",null);
        }
        public void CompleteStream(string status)
        {
            if (status=="succeeded") Interlocked.Increment(ref completed);
            BuildProgressSnapshot value=Snapshot();
            Context previous=Volatile.Read(ref current);
            Volatile.Write(ref current,new Context { Stage=previous.Stage, StageStarted=previous.StageStarted, Work=new Work() });
            Notify(value,"stream-completed",status);
        }
        public void SetPhase(string id)
        {
            Context previous=Volatile.Read(ref current);
            if (previous.Stage==null || Volatile.Read(ref finished) || previous.Phase==id) return;
            Volatile.Write(ref current,new Context { Stage=previous.Stage, StageStarted=previous.StageStarted,
                Stream=previous.Stream, StreamStarted=previous.StreamStarted, Phase=id, Work=previous.Work });
            Notify("phase",null);
        }
        public void RecordWork(int assetsCompleted,int xmlParsed,int documentsReused)
        {
            Work work=Volatile.Read(ref current).Work;
            if (assetsCompleted!=0) Interlocked.Add(ref work.Assets,assetsCompleted);
            if (xmlParsed!=0) Interlocked.Add(ref work.Xml,xmlParsed);
            if (documentsReused!=0) Interlocked.Add(ref work.Reused,documentsReused);
        }
        public void Finish(string status)
        {
            Volatile.Write(ref finished,true); Notify("finished",status);
        }
        void Notify(string boundary,string status) { Notify(Snapshot(),boundary,status); }
        void Notify(BuildProgressSnapshot value,string boundary,string status)
        {
            var handler=Boundary;
            if (handler!=null) handler(value,boundary,status);
        }
        public BuildProgressSnapshot Snapshot()
        {
            Context value=Volatile.Read(ref current);
            long now=elapsed();
            return new BuildProgressSnapshot { Stage=value.Stage, Stream=value.Stream, Phase=value.Phase,
                AssetsCompleted=Interlocked.Read(ref value.Work.Assets), XmlParsed=Interlocked.Read(ref value.Work.Xml),
                DocumentsReused=Interlocked.Read(ref value.Work.Reused), StreamsCompleted=Volatile.Read(ref completed),
                StreamsTotal=Volatile.Read(ref total), ElapsedMilliseconds=now,
                StageElapsedMilliseconds=now-value.StageStarted, StreamElapsedMilliseconds=now-value.StreamStarted,
                Finished=Volatile.Read(ref finished) };
        }
    }
    public sealed class BuildProgressSnapshot
    {
        public string Stage { get; internal set; }
        public string Stream { get; internal set; }
        public string Phase { get; internal set; }
        public long AssetsCompleted { get; internal set; }
        public long XmlParsed { get; internal set; }
        public long DocumentsReused { get; internal set; }
        public int StreamsCompleted { get; internal set; }
        public int StreamsTotal { get; internal set; }
        public long ElapsedMilliseconds { get; internal set; }
        public long StageElapsedMilliseconds { get; internal set; }
        public long StreamElapsedMilliseconds { get; internal set; }
        public bool Finished { get; internal set; }
    }
}
