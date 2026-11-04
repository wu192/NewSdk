using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NewModCompiler.Diagnostics
{
    // One synchronous build owns this collector. Names are fixed phase names
    // (or asset types), never file/instance names, so memory does not grow per call.
    internal sealed class BuildPerformance
    {
        readonly string level;
        readonly Dictionary<string, Entry> entries;
        readonly Dictionary<string, Dictionary<string, Counter>> counters;
        Scope current;
        internal string StreamId { get; set; }
        internal bool Enabled { get { return level != "off"; } }
        internal bool Detailed { get { return level == "detailed"; } }

        internal BuildPerformance(string level)
        {
            this.level = level ?? "off";
            if (Enabled) {
                entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
                counters = new Dictionary<string, Dictionary<string, Counter>>(StringComparer.Ordinal);
            }
        }

        // Owner-only observations from existing cache decisions. No clocks,
        // paths, per-asset records or additional dependency walks are collected.
        internal void RecordCounter(string name, long value = 1, bool maximum = false)
        {
            if (!Enabled) return;
            string stream = StreamId ?? "";
            Dictionary<string, Counter> group;
            if (!counters.TryGetValue(stream, out group))
                counters.Add(stream, group = new Dictionary<string, Counter>(StringComparer.Ordinal));
            Counter counter;
            if (!group.TryGetValue(name, out counter))
                group.Add(name, counter = new Counter { Maximum = maximum });
            counter.Value = maximum ? Math.Max(counter.Value, value) : counter.Value + value;
        }

        internal IDisposable Measure(string name, bool detailed = false)
        {
            // Off: no timestamp, scope allocation, dictionary lookup or output.
            if (!Enabled || (detailed && !Detailed)) return null;
            string key = (StreamId ?? "") + "\n" + name;
            Entry entry;
            if (!entries.TryGetValue(key, out entry))
                entries.Add(key, entry = new Entry { Name = name, StreamId = StreamId });
            var scope = new Scope(this, current, entry);
            current = scope;
            return scope;
        }

        // Owner merge of worker intervals; never alters the synchronous scope stack.
        internal void RecordBackend(string name, long ticks)
        {
            if (!Detailed) return;
            string key=(StreamId ?? "")+"\n"+name;
            Entry entry;
            if (!entries.TryGetValue(key,out entry)) entries.Add(key,entry=new Entry {Name=name,StreamId=StreamId});
            entry.Count++; entry.Total+=ticks; entry.Self+=ticks; entry.Max=Math.Max(entry.Max,ticks);
        }

        internal object Snapshot()
        {
            return new {
                level, clock = "Stopwatch", frequency = Stopwatch.Frequency,
                boundary = "before-report-write",
                entries = entries.Values.OrderBy(e => e.StreamId, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal).Select(e => new {
                    name = e.Name, streamId = e.StreamId, count = e.Count,
                    totalMs = Milliseconds(e.Total), selfMs = Milliseconds(e.Self), maxMs = Milliseconds(e.Max)
                }).ToArray(),
                counters = counters.OrderBy(g => g.Key, StringComparer.Ordinal).SelectMany(g =>
                    g.Value.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new {
                        streamId = g.Key.Length == 0 ? null : g.Key, name = c.Key,
                        value = c.Value.Value, aggregation = c.Value.Maximum ? "max" : "sum"
                    })).ToArray()
            };
        }
        static double Milliseconds(long ticks) { return ticks * (1000.0 / Stopwatch.Frequency); }

        sealed class Entry
        {
            internal string Name, StreamId;
            internal long Count, Total, Self, Max;
        }
        sealed class Counter
        {
            internal long Value;
            internal bool Maximum;
        }
        sealed class Scope : IDisposable
        {
            readonly BuildPerformance owner;
            readonly Scope parent;
            readonly Entry entry;
            readonly long started;
            long children;
            bool disposed;
            internal Scope(BuildPerformance owner, Scope parent, Entry entry)
            { this.owner = owner; this.parent = parent; this.entry = entry; started = Stopwatch.GetTimestamp(); }
            public void Dispose()
            {
                if (disposed) return;
                long elapsed = Stopwatch.GetTimestamp() - started;
                disposed = true;
                entry.Count++; entry.Total += elapsed; entry.Self += Math.Max(0, elapsed - children);
                entry.Max = Math.Max(entry.Max, elapsed);
                if (parent != null) parent.children += elapsed;
                owner.current = parent;
            }
        }
    }
}
