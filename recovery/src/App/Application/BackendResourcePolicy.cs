#if NEWSdk_MONOLITHIC
using System;
using System.Runtime.InteropServices;

namespace NewModCompiler.Application
{
    // A stream captures its CPU/job ceiling once. Memory is an observation, not
    // a reservation: the scheduler must refresh it before admitting more work
    // and account for all prepared/in-flight/uncommitted bytes itself.
    internal sealed class BackendResourcePolicy
    {
        public const long MiB = 1024L * 1024L;
        public const long MinimumWorkerBytes = 16L * MiB;
        // Win32 thread stacks and scheduler/runtime bookkeeping also consume
        // virtual address space. Do not turn a huge --jobs value or a many-core
        // host into an unbounded thread/queue allocation in this x86 process.
        public const int MaximumWorkers = 32;
        public const long FallbackByteBudget = 16L * MiB;

        public int RequestedJobs { get; private set; }
        public int ProcessorCount { get; private set; }
        public int Capacity { get; private set; }
        public int IoCapacity { get; private set; }
        public long ByteBudget { get; private set; }
        public bool MemoryQuerySucceeded { get; private set; }
        public long AvailablePhysicalBytes { get; private set; }
        public long AvailableVirtualBytes { get; private set; }
        public long TotalVirtualBytes { get; private set; }

        BackendResourcePolicy() { }

        public static BackendResourcePolicy Calculate(int requestedJobs, int processorCount,
            long availablePhysicalBytes, long availableVirtualBytes, long totalVirtualBytes,
            bool memoryQuerySucceeded = true)
        {
            if (requestedJobs < 1) throw new ArgumentOutOfRangeException("requestedJobs");
            var result = new BackendResourcePolicy
            {
                RequestedJobs = requestedJobs,
                ProcessorCount = Math.Max(1, processorCount),
                MemoryQuerySucceeded = memoryQuerySucceeded,
                AvailablePhysicalBytes = Math.Max(0L, availablePhysicalBytes),
                AvailableVirtualBytes = Math.Max(0L, availableVirtualBytes),
                TotalVirtualBytes = Math.Max(0L, totalVirtualBytes)
            };
            if (!memoryQuerySucceeded)
            {
                // Unknown memory cannot certify any worker. This small serial
                // allowance is a conservative fallback, not observed free RAM.
                result.Capacity = 1;
                result.ByteBudget = FallbackByteBudget;
                result.IoCapacity = 1;
            }
            else
            {
                long reserve = Math.Max(128L * MiB, result.TotalVirtualBytes / 4L);
                long available = Math.Min(result.AvailablePhysicalBytes, result.AvailableVirtualBytes);
                long usable = Math.Max(0L, available - reserve);
                result.ByteBudget = Math.Min(usable / 2L, result.TotalVirtualBytes / 4L);
                long memoryWorkers = result.ByteBudget / MinimumWorkerBytes;
                // Zero usable memory means no parallel admission, without
                // fabricating a minimum amount of available physical/virtual RAM.
                result.Capacity = (int)Math.Max(1L, Math.Min(Math.Min((long)requestedJobs,
                    result.ProcessorCount), Math.Min((long)MaximumWorkers, memoryWorkers)));
            }
            // IO has its own ceiling; a completed encoder immediately releases its CPU slot.
            if (memoryQuerySucceeded) result.IoCapacity = Math.Max(1, Math.Min(4, Math.Min(MaximumWorkers, requestedJobs)));
            // A large number of CPUs must not make an otherwise safe individual
            // task inadmissible. Actual retained-byte accounting determines how
            // many such tasks can coexist within the shared budget.
            return result;
        }

        public static BackendResourcePolicy Capture(int requestedJobs)
        {
            return Observe(requestedJobs, Environment.ProcessorCount);
        }

        public BackendResourcePolicy RefreshMemoryBudget()
        {
            // CPU affinity/host changes do not expand this stream's initial CPU
            // ceiling. The owner may additionally retain the lowest observation
            // to avoid oscillation while existing work consumes its allowance.
            return Observe(RequestedJobs, ProcessorCount);
        }

        static BackendResourcePolicy Observe(int requestedJobs, int processorCount)
        {
            var memory = new MemoryStatus();
            memory.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            bool succeeded = false;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try { succeeded = GlobalMemoryStatusEx(ref memory); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (BadImageFormatException) { }
            }
            return Calculate(requestedJobs, processorCount, ToLong(memory.AvailablePhysical),
                ToLong(memory.AvailableVirtual), ToLong(memory.TotalVirtual), succeeded);
        }

        static long ToLong(ulong value)
        {
            return value > (ulong)long.MaxValue ? long.MaxValue : (long)value;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    }
}
#endif
