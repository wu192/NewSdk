using System;

namespace BinaryAssetBuilder.Core;

// The host supplies a request-local collector; standalone Core remains silent.
// A null callback is the disabled path and does not read the clock or allocate.
internal static class PerformanceScope
{
    internal static IDisposable Measure(string name)
    {
        var measure = Settings.Current == null ? null : Settings.Current.MeasurePerformance;
        return measure == null ? null : measure(name);
    }

    internal static bool CountersEnabled => Settings.Current?.RecordPerformanceCounter != null;

    internal static void Count(string name, long value = 1, bool maximum = false)
    {
        var record = Settings.Current?.RecordPerformanceCounter;
        if (record != null) record(name, value, maximum);
    }
}
