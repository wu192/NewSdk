using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Script.Serialization;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    internal static class PerformanceTests
    {
        static Dictionary<string, object>[] Rows(BuildPerformance value)
        {
            var serializer = new JavaScriptSerializer();
            var report = serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(value.Snapshot()));
            return ((System.Collections.IEnumerable)report["entries"]).Cast<Dictionary<string, object>>().ToArray();
        }
        static void Work()
        {
            long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            while (Stopwatch.GetTimestamp() < until) { }
        }
        internal static int Run()
        {
            var off = new BuildPerformance("off");
            if (off.Measure("ignored") != null || off.Measure("ignored", true) != null)
                throw new Exception("Disabled performance collection created a scope.");
            off.RecordCounter("ignored");
            var summary = new BuildPerformance("summary");
            using (summary.Measure("phase")) Work();
            if (summary.Measure("detail", true) != null || Rows(summary).Length != 1)
                throw new Exception("Summary collected detailed scopes.");

            var detailed = new BuildPerformance("detailed");
            detailed.StreamId = "base/Mod";
            using (detailed.Measure("parent")) {
                Work();
                using (detailed.Measure("child", true)) Work();
                try { using (detailed.Measure("child", true)) { Work(); throw new InvalidOperationException(); } }
                catch (InvalidOperationException) { }
                Work();
            }
            var rows = Rows(detailed);
            var parent = rows.Single(r => (string)r["name"] == "parent");
            var child = rows.Single(r => (string)r["name"] == "child");
            double total = Convert.ToDouble(parent["totalMs"]), self = Convert.ToDouble(parent["selfMs"]);
            if (rows.Length != 2 || Convert.ToInt64(child["count"]) != 2 ||
                total < self || self <= 0 || Convert.ToDouble(child["maxMs"]) > Convert.ToDouble(child["totalMs"]) ||
                Math.Abs(total - self - Convert.ToDouble(child["totalMs"])) > 0.00001 ||
                rows.Any(r => (string)r["streamId"] != "base/Mod"))
                throw new Exception("Nested timing/count/exception aggregation differs.");
            // Recursive phase names must aggregate, with direct child time
            // subtracted once; a later stream must have its own row.
            using (detailed.Measure("recursive")) using (detailed.Measure("recursive")) Work();
            detailed.StreamId = "apt/UI";
            using (detailed.Measure("child")) Work();
            rows = Rows(detailed);
            if (rows.Length != 4 || Convert.ToInt64(rows.Single(r => (string)r["name"] == "recursive")["count"]) != 2)
                throw new Exception("Recursive or multi-stream aggregation differs.");
            // Counters are independent of timing entries and available at both
            // profiling levels. Repeated visits sum; graph depth takes a max.
            summary.StreamId = "base/Mod";
            summary.RecordCounter("cache.invalidation.contentChanged");
            summary.RecordCounter("cache.invalidation.contentChanged", 2);
            summary.RecordCounter("dependency.include.maxDepth", 3, true);
            summary.RecordCounter("dependency.include.maxDepth", 1, true);
            summary.StreamId = "apt/UI";
            summary.RecordCounter("cache.invalidation.contentChanged", 4);
            var serializer = new JavaScriptSerializer();
            var snapshot = serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(summary.Snapshot()));
            var counters = ((System.Collections.IEnumerable)snapshot["counters"]).Cast<Dictionary<string, object>>().ToArray();
            if (counters.Length != 3 || Rows(summary).Length != 1 ||
                Convert.ToInt64(counters.Single(c => (string)c["streamId"] == "base/Mod" && (string)c["name"] == "cache.invalidation.contentChanged")["value"]) != 3 ||
                Convert.ToInt64(counters.Single(c => (string)c["name"] == "dependency.include.maxDepth")["value"]) != 3 ||
                (string)counters.Single(c => (string)c["name"] == "dependency.include.maxDepth")["aggregation"] != "max")
                throw new Exception("Counter sum/max or stream isolation differs.");
            return 6;
        }
    }
}
