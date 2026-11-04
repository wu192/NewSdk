#if NEWSdk_MONOLITHIC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    internal sealed class BackendInterval
    {
        public string typeName;
        public long startTicks, endTicks;
        public int threadId;
    }

    // Workers record only private timing data. The owner merges Core timings.
    internal sealed class ParallelBackendMetrics
    {
        readonly object gate = new object();
        readonly bool enabled, detailed;
        int active;
        long nextSample;
        internal int BackendCalls, MaximumConcurrency, EffectiveJobs, MinimumEffectiveJobs;
        internal long PreparationTicks, WaitTicks, WorkTicks, CommitTicks, PeakPrivateBytes;
        internal long FrontendTicks, FrontendComputeTicks, VerificationTicks, IoTicks, CpuWaitTicks, MemoryWaitTicks, IoQueueTicks;
        internal long PeakRetainedBytes;
        internal int PeakCpuTasks, PeakIoTasks, PeakAwaitingOwner, MaximumIoCapacity;
        internal bool ResourceQuerySucceeded = true;
        internal readonly Dictionary<string,int> CallsByType;
        internal readonly Dictionary<string,int> FallbackByReason;
        internal readonly List<BackendInterval> Intervals;
        internal ParallelBackendMetrics(BuildPerformance performance)
        {
            enabled = performance.Enabled; detailed = performance.Detailed;
            if (enabled) { CallsByType = new Dictionary<string,int>(StringComparer.Ordinal); FallbackByReason = new Dictionary<string,int>(StringComparer.Ordinal); }
            if (detailed) Intervals = new List<BackendInterval>();
        }
        internal bool Enabled { get { return enabled; } }
        internal void Fallback(string reason)
        { if (!enabled) return; int count; FallbackByReason.TryGetValue(reason,out count); FallbackByReason[reason]=count+1; }
        internal AssetBuffer Execute(string type, Func<AssetBuffer> execute, out long elapsed)
        {
            elapsed = 0;
            if (!enabled) return execute();
            long start;
            lock (gate) { start=Stopwatch.GetTimestamp(); BackendCalls++; active++; MaximumConcurrency=Math.Max(MaximumConcurrency,active); int count; CallsByType.TryGetValue(type,out count); CallsByType[type]=count+1; }
            try { return execute(); }
            finally {
                lock (gate) {
                    long end=Stopwatch.GetTimestamp(); elapsed=end-start; WorkTicks+=elapsed; active--;
                    if (detailed && Intervals.Count<512) Intervals.Add(new BackendInterval {typeName=type,startTicks=start,endTicks=end,threadId=Thread.CurrentThread.ManagedThreadId});
                }
            }
        }
        internal void SampleMemory()
        {
            if (!enabled) return;
            long now=Stopwatch.GetTimestamp(); if (now<nextSample) return;
            nextSample=now+Math.Max(1,Stopwatch.Frequency/10);
            try { using (var process=Process.GetCurrentProcess()) PeakPrivateBytes=Math.Max(PeakPrivateBytes,process.PrivateMemorySize64); }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        static double Ms(long ticks) { return ticks*(1000.0/Stopwatch.Frequency); }
        internal object Snapshot(BuildRequest request)
        {
            if (!enabled) return null;
            return new { protocol="NewModCompiler.ParallelBackend.v1", requestedJobs=request.JobsAuto ? "auto" : request.RequestedJobs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                effectiveJobs=EffectiveJobs,minimumEffectiveJobs=MinimumEffectiveJobs,backendCalls=BackendCalls,backendCallsByType=CallsByType,maxBackendConcurrency=MaximumConcurrency,
                prepareMs=Ms(PreparationTicks),xmlFrontendMs=Ms(FrontendTicks),frontendComputeMs=Ms(FrontendComputeTicks),cpuResourceWaitMs=Ms(CpuWaitTicks),memoryResourceWaitMs=Ms(MemoryWaitTicks),ioQueueWaitMs=Ms(IoQueueTicks),workerVerificationMs=Ms(VerificationTicks),workerWaitMs=Ms(WaitTicks),workerComputeMs=Ms(WorkTicks),assetIoMs=Ms(IoTicks),ownerCommitMs=Ms(CommitTicks),fallbackByReason=FallbackByReason,
                peakCpuTasks=PeakCpuTasks,peakIoTasks=PeakIoTasks,peakAwaitingOwner=PeakAwaitingOwner,peakRetainedResultBytes=PeakRetainedBytes,ioCapacity=MaximumIoCapacity,sampledPeakPrivateBytes=PeakPrivateBytes,resourceQuerySucceeded=ResourceQuerySucceeded,backendIntervals=Intervals,intervalSampleLimit=512,clockFrequency=Stopwatch.Frequency };
        }
    }

    internal sealed class ParallelBackendScheduler : IAssetExecution, IDisposable
    {
        readonly DocumentProcessor processor;
        readonly string staging;
        readonly CancellationToken cancellation;
        readonly ParallelBackendMetrics metrics;
        readonly BuildPerformance performance;
        readonly Func<BackendResourcePolicy> sampler;
        // OutputManager creates exactly one stable BinaryAsset for a complete
        // FileBase. Its reference is therefore the (OutputManager, FileBase) key.
        readonly Dictionary<BinaryAsset, PhysicalWork> registry = new Dictionary<BinaryAsset, PhysicalWork>();
        readonly List<PhysicalWork> active = new List<PhysicalWork>();
        readonly List<Subscription> verifying = new List<Subscription>();
        readonly List<AssetExecutionDocument> documents = new List<AssetExecutionDocument>();
        readonly List<Task> frontendTasks = new List<Task>();
        BackendResourcePolicy resources;
        int capacity, ioCapacity, nextIdentity;
        long budget, retained, nextSample;
        bool disposed, failed, pumping, completingAcceptedOnly;

        enum WorkState { Running, Ready, Committing, Completed }
        sealed class PhysicalWork
        {
            internal int Identity;
            internal BinaryAsset Asset;
            internal IAssetBuilderPlugin Plugin;
            internal InstanceDeclaration Declaration;
            internal PreparedBackendWork Prepared;
            internal Task<Computed> Compute;
            internal Task IO;
            internal AssetBuffer Buffer;
            internal BinaryAssetCommitWork Commit;
            internal AssetLocation Location;
            internal Action<AssetLocation> Committed;
            internal readonly List<Subscription> Subscribers = new List<Subscription>();
            internal Tracer.DiagnosticCapture Diagnostics;
            internal string Directory, SourcePath;
            internal long ReservedBytes, WorkTicks, IoTicks, ReadySince;
            internal bool MetricsMerged, Released, Encoded, CompileCountMerged;
            internal WorkState State;
        }
        sealed class Computed
        {
            internal bool Verified;
            internal AssetBuffer Buffer;
        }
        sealed class Subscription
        {
            internal AssetExecutionDocument Document;
            internal PhysicalWork Physical;
            internal string SourcePath;
            internal Func<bool> Verify;
            internal Action<bool> PublishVerification;
            internal Task<bool> Verification;
            internal Tracer.DiagnosticCapture Diagnostics;
            internal long VerifyTicks;
            internal bool Verified, Finished;
        }

        internal ParallelBackendScheduler(DocumentProcessor processor, string staging, int requestedJobs,
            ParallelBackendMetrics metrics, BuildPerformance performance, CancellationToken cancellation,
            Func<BackendResourcePolicy> sampler = null)
        {
            this.processor = processor; this.staging = staging; this.metrics = metrics;
            this.performance = performance; this.cancellation = cancellation; this.sampler = sampler;
            resources = sampler == null ? BackendResourcePolicy.Capture(requestedJobs) : sampler();
            capacity = resources.Capacity; ioCapacity = resources.IoCapacity; budget = resources.ByteBudget;
            nextSample = Stopwatch.GetTimestamp() + Math.Max(1, Stopwatch.Frequency / 10);
            RecordResources();
        }

        public bool HasPending { get { return active.Count != 0 || verifying.Count != 0 || frontendTasks.Count != 0; } }
        void Check()
        {
            if (disposed || failed) throw new InvalidOperationException("Backend scheduler is closed.");
            cancellation.ThrowIfCancellationRequested();
        }
        long Start() { return metrics.Enabled ? Stopwatch.GetTimestamp() : 0; }
        long Elapsed(long start) { return metrics.Enabled ? Stopwatch.GetTimestamp() - start : 0; }
        void RecordResources()
        {
            metrics.EffectiveJobs = Math.Max(metrics.EffectiveJobs, capacity);
            metrics.MinimumEffectiveJobs = metrics.MinimumEffectiveJobs == 0 ? capacity : Math.Min(metrics.MinimumEffectiveJobs, capacity);
            metrics.ResourceQuerySucceeded &= resources.MemoryQuerySucceeded;
            metrics.MaximumIoCapacity = Math.Max(metrics.MaximumIoCapacity, ioCapacity);
        }
        void Refresh()
        {
            long now = Stopwatch.GetTimestamp();
            if (sampler == null && now < nextSample) return;
            nextSample = now + Math.Max(1, Stopwatch.Frequency / 10);
            resources = sampler == null ? resources.RefreshMemoryBudget() : sampler();
            capacity = resources.Capacity; ioCapacity = resources.IoCapacity; budget = resources.ByteBudget;
            RecordResources(); metrics.SampleMemory();
        }
        bool Fits(long bytes) { return bytes >= 0 && retained <= budget && bytes <= budget - retained; }
        int RunningCpu()
        {
            int count = 0;
            foreach (var work in active) if (work.Compute != null && !work.Compute.IsCompleted) count++;
            foreach (var request in verifying) if (request.Verification != null && !request.Verification.IsCompleted) count++;
            foreach (var task in frontendTasks) if (!task.IsCompleted) count++;
            return count;
        }
        int RunningIO()
        {
            int count = 0;
            foreach (var work in active) if (work.IO != null && !work.IO.IsCompleted) count++;
            return count;
        }
        List<Task> CpuTasks()
        {
            var tasks = new List<Task>();
            foreach (var work in active) if (work.Compute != null && !work.Compute.IsCompleted) tasks.Add(work.Compute);
            foreach (var request in verifying) if (request.Verification != null && !request.Verification.IsCompleted) tasks.Add(request.Verification);
            foreach (var task in frontendTasks) if (!task.IsCompleted) tasks.Add(task);
            return tasks;
        }
        List<Task> ResourceTasks()
        {
            // A completed task still owns its result until the owner harvests
            // it. Keep it in the wait set: completion can race the preceding
            // DrainReady call, and WhenAny must then return immediately.
            var tasks = new List<Task>();
            foreach (var work in active)
            {
                if (work.Compute != null) tasks.Add(work.Compute);
                if (work.IO != null) tasks.Add(work.IO);
            }
            foreach (var request in verifying) if (request.Verification != null) tasks.Add(request.Verification);
            return tasks;
        }
        void WaitAny(IList<Task> tasks, int resource = 0)
        {
            if (tasks.Count == 0) return;
            long start = Start();
            try { Task.WhenAny(tasks).GetAwaiter().GetResult(); }
            finally { long ticks = Elapsed(start); metrics.WaitTicks += ticks; if (resource == 1) metrics.CpuWaitTicks += ticks; else if (resource == 2) metrics.MemoryWaitTicks += ticks; }
            cancellation.ThrowIfCancellationRequested();
        }
        void AdmitCpu()
        {
            DrainReady(); Refresh();
            while (RunningCpu() >= capacity)
            {
                WaitAny(CpuTasks(), 1); DrainReady(); Refresh();
            }
        }
        void AdmitMemory(long bytes)
        {
            DrainReady(); Refresh();
            while (retained > 0 && !Fits(bytes))
            {
                var tasks = new List<Task>();
                foreach (var work in active)
                {
                    if (work.ReservedBytes == 0) continue;
                    if (work.Compute != null) tasks.Add(work.Compute);
                    if (work.IO != null) tasks.Add(work.IO);
                }
                if (tasks.Count == 0) break;
                WaitAny(tasks, 2); DrainReady(); Refresh();
            }
        }

        public AssetExecutionDocument BeginDocument(AssetDeclarationDocument document, OutputManager outputManager)
        {
            Check(); DrainReady();
            var version = new AssetExecutionDocument { Document = document, OutputManager = outputManager };
            documents.Add(version);
            return version;
        }
        Subscription Subscribe(AssetExecutionDocument document, PhysicalWork physical,
            Func<bool> verify, Action<bool> publishVerification, string sourcePath)
        {
            if (document.IsClosed) throw new InvalidOperationException("Cannot register work after closing a document version.");
            var request = new Subscription { Document = document, Physical = physical,
                Verify = verify, PublishVerification = publishVerification, SourcePath = sourcePath };
            document.PendingAssetWorks++; document.ReadOnlyInputReferences++;
            physical.Subscribers.Add(request);
            return request;
        }
        public void Enqueue(AssetExecutionDocument document, BinaryAsset asset, InstanceDeclaration declaration,
            IAssetBuilderPlugin plugin, Action compileCompatibility, Func<bool> verify,
            Action<bool> publishVerification, Action<AssetLocation> committed, bool parallelVerification = true, bool parallelCompilation = true)
        {
            Check();
            try { Register(document, asset, declaration, plugin, compileCompatibility, AssetLocation.None,
                verify, publishVerification, committed, parallelVerification, parallelCompilation); }
            catch { Fail(); throw; }
        }
        public void EnqueueCached(AssetExecutionDocument document, BinaryAsset asset, InstanceDeclaration declaration,
            AssetLocation location, Func<bool> verify, Action<bool> publishVerification,
            Action<AssetLocation> committed, bool parallelVerification = true)
        {
            Check();
            try { Register(document, asset, declaration, null, null, location,
                verify, publishVerification, committed, parallelVerification, false); }
            catch { Fail(); throw; }
        }
        void Register(AssetExecutionDocument document, BinaryAsset asset, InstanceDeclaration declaration,
            IAssetBuilderPlugin plugin, Action compileCompatibility, AssetLocation location,
            Func<bool> verify, Action<bool> publishVerification, Action<AssetLocation> committed, bool parallelVerification, bool parallelCompilation)
        {
            DrainReady();
            PhysicalWork work;
            if (registry.TryGetValue(asset, out work))
            {
                var duplicate = Subscribe(document, work, verify, publishVerification, declaration.Document.SourcePath);
                ScheduleVerification(duplicate, parallelVerification);
                FinishSubscriptions(work);
                return;
            }
            work = new PhysicalWork { Identity = ++nextIdentity, Asset = asset, Declaration = declaration,
                Location = location, Committed = committed, SourcePath = declaration.Document.SourcePath,
                State = WorkState.Running };
            registry.Add(asset, work); active.Add(work);
            var first = Subscribe(document, work, verify, publishVerification, declaration.Document.SourcePath);
            if (location != AssetLocation.None)
            {
                // Cache transfer never reserves an encoder slot. A loaded
                // declaration may still require its own semantic verifier.
                work.State = WorkState.Ready; work.ReadySince = Start();
                ScheduleVerification(first, parallelVerification);
                DrainReady();
                return;
            }
            Refresh();
            if (parallelCompilation) plugin = BuiltinCompilers.ResolveBackendPlugin(plugin);
            work.Plugin = plugin;
            PreparedBackendWork prepared = null;
            string reason = parallelCompilation ? "resource-capacity" : "frontend-expanded";
            bool accepted = false;
            long preparationBytes = 0;
            if (parallelCompilation && capacity > 1)
            {
                long start = Start();
                try { accepted = PreparedBackendWork.TryPrepareForScheduling(declaration, plugin, budget / 2, out prepared, out reason, out preparationBytes); }
                finally { metrics.PreparationTicks += Elapsed(start); }
            }
            if (accepted)
            {
                work.Prepared = prepared;
                AdmitMemory(prepared.PreparedBytes); AdmitCpu(); Refresh();
                if (capacity <= 1 || prepared.PreparedBytes > budget / 2 || !Fits(prepared.PreparedBytes))
                {
                    prepared.Dispose(); work.Prepared = null; accepted = false; reason = "resource-budget";
                }
            }
            if (!accepted)
            {
                metrics.Fallback(reason);
                // Uncertified code runs on its owner. It does not require
                // unrelated physical assets or documents to finish first.
                if (parallelCompilation) AdmitCpu();
                if (reason == "uncertified-plugin") WaitPlugin(plugin, work);
                PublishVerification(first, RunVerification(first));
                if (parallelCompilation)
                {
                    if (preparationBytes == 0) preparationBytes = PreparedBackendWork.EstimatePreparationBytes(declaration);
                    // An owner encoder competes with retained IO results just
                    // like a worker. An oversized single item may run alone,
                    // after the previous reservations have actually released.
                    AdmitMemory(preparationBytes);
                    work.ReservedBytes = preparationBytes; retained += preparationBytes;
                    SampleState();
                }
                compileCompatibility();
                work.Buffer = asset.Buffer;
                work.Location = AssetLocation.Memory; work.State = WorkState.Ready; work.ReadySince = Start();
                long resultBytes = BufferBytes(work.Buffer);
                retained += resultBytes - work.ReservedBytes; work.ReservedBytes = resultBytes;
                DrainReady(); return;
            }
            if (declaration.HasCustomData)
            {
                work.Directory = Path.Combine(staging, "backend-work", work.Identity.ToString("D8"));
                Directory.CreateDirectory(work.Directory);
                prepared.Declaration.CustomDataPath = Path.Combine(work.Directory, "payload.cdata");
            }
            // Owner-only verifiers run before the certified encoder. A known
            // reentrant verifier runs in the same CPU call before encoding.
            if (!parallelVerification) PublishVerification(first, RunVerification(first));
            work.ReservedBytes = prepared.PreparedBytes; retained += work.ReservedBytes;
            cancellation.ThrowIfCancellationRequested();
            work.Compute = Task.Factory.StartNew(() => {
                work.Diagnostics = Tracer.CaptureDiagnostics();
                using (work.Diagnostics)
                {
                    bool verified = first.Verified || RunVerification(first);
                    if (!verified) return new Computed { Verified = false };
                    work.Encoded = true;
                    return new Computed { Verified = true,
                        Buffer = metrics.Execute(declaration.Handle.TypeName, prepared.Execute, out work.WorkTicks) };
                }
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            SampleState();
        }
        static long BufferBytes(AssetBuffer buffer)
        {
            if (buffer == null) return 0;
            return (long)(buffer.InstanceData == null ? 0 : buffer.InstanceData.Length)
                + (buffer.RelocationData == null ? 0 : buffer.RelocationData.Length)
                + (buffer.ImportsData == null ? 0 : buffer.ImportsData.Length);
        }
        bool RunVerification(Subscription request)
        {
            long start = Start();
            try { return request.Verify == null || request.Verify(); }
            finally { request.VerifyTicks += Elapsed(start); }
        }
        void ScheduleVerification(Subscription request, bool parallel)
        {
            if (!parallel || request.Verify == null)
            {
                PublishVerification(request, RunVerification(request)); return;
            }
            AdmitCpu();
            verifying.Add(request);
            request.Verification = Task.Factory.StartNew(() => {
                request.Diagnostics = Tracer.CaptureDiagnostics();
                using (request.Diagnostics) return RunVerification(request);
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }
        void PublishVerification(Subscription request, bool result)
        {
            metrics.VerificationTicks += request.VerifyTicks;
            if (performance.Detailed) performance.RecordBackend("core.asset.verify", request.VerifyTicks);
            request.VerifyTicks = 0;
            request.PublishVerification?.Invoke(result);
            if (!result) throw new BinaryAssetBuilderException(ErrorCode.GameDataVerification,
                "FATAL: An asset failed the Game Data Verification step. See previous output.");
            request.Verified = true;
        }
        void Replay(Tracer.DiagnosticCapture diagnostics, string sourcePath)
        {
            if (diagnostics == null) return;
            processor.InDocumentContext(sourcePath, diagnostics.Replay);
        }
        public void DrainReady()
        {
            Check();
            if (pumping) return;
            pumping = true;
            SampleState();
            try
            {
                bool progress;
                do
                {
                    progress = false;
                    for (int index = verifying.Count - 1; index >= 0; index--)
                    {
                        var request = verifying[index];
                        if (request.Verification == null || !request.Verification.IsCompleted) continue;
                        bool result;
                        try { result = request.Verification.GetAwaiter().GetResult(); }
                        finally { Replay(request.Diagnostics, request.SourcePath); }
                        request.Diagnostics = null; request.Verification = null; verifying.RemoveAt(index);
                        PublishVerification(request, result); FinishSubscriptions(request.Physical); progress = true;
                    }
                    for (int index = active.Count - 1; index >= 0; index--)
                    {
                        var work = active[index];
                        if (work.Compute != null && work.Compute.IsCompleted)
                        {
                            Computed result;
                            try { result = work.Compute.GetAwaiter().GetResult(); }
                            finally
                            {
                                Replay(work.Diagnostics, work.SourcePath); work.Diagnostics = null;
                                processor.InDocumentContext(work.SourcePath, work.Prepared.FlushDiagnostics); MergeWorkerMetrics(work);
                            }
                            work.Compute = null;
                            if (!work.Subscribers[0].Verified) PublishVerification(work.Subscribers[0], result.Verified);
                            work.Buffer = result.Buffer; work.Location = AssetLocation.Memory; work.State = WorkState.Ready; work.ReadySince = Start();
                            long bytes = Math.Max(work.ReservedBytes, BufferBytes(work.Buffer));
                            retained += bytes - work.ReservedBytes; work.ReservedBytes = bytes;
                            progress = true;
                        }
                        if (work.IO != null && work.IO.IsCompleted)
                        {
                            try { work.IO.GetAwaiter().GetResult(); }
                            finally { processor.InDocumentContext(work.SourcePath, work.Commit.FlushDiagnostics); }
                            metrics.IoTicks += work.IoTicks;
                            if (performance.Detailed) performance.RecordBackend("core.asset.io", work.IoTicks);
                            long start = Start();
                            try
                            {
                                processor.InDocumentContext(work.SourcePath, () => {
                                    AssetLocation source = work.Asset.PublishCommit(work.Commit);
                                    work.Committed?.Invoke(source);
                                });
                                work.State = WorkState.Completed;
                            }
                            finally { metrics.CommitTicks += Elapsed(start); }
                            work.IO = null; work.Commit = null; work.Buffer = null;
                            Release(work); active.RemoveAt(index);
                            FinishSubscriptions(work); progress = true;
                        }
                    }
                    // Completed encoders have already relinquished CPU slots.
                    // Fill IO independently, skipping work still being verified.
                    int slots = ioCapacity - RunningIO();
                    if (slots > 0)
                    {
                        foreach (var work in active)
                        {
                            if (slots == 0) break;
                            if (work.State != WorkState.Ready || !work.Subscribers[0].Verified) continue;
                            if (completingAcceptedOnly && !RequiredForPartialCompletion(work)) continue;
                            string cdata = work.Prepared == null ? work.Declaration.CustomDataPath : work.Prepared.Declaration.CustomDataPath;
                            long start = Start();
                            try { work.Commit = work.Asset.PrepareCommit(work.Location, work.Buffer, cdata); }
                            finally { metrics.CommitTicks += Elapsed(start); }
                            long bytes = Math.Max(work.ReservedBytes, work.Commit.RetainedBytes);
                            retained += bytes - work.ReservedBytes; work.ReservedBytes = bytes;
                            metrics.IoQueueTicks += Elapsed(work.ReadySince);
                            work.State = WorkState.Committing;
                            work.IO = Task.Factory.StartNew(() => {
                                long ioStart = Start();
                                try { work.Commit.ExecuteIO(); }
                                finally { work.IoTicks = Elapsed(ioStart); }
                            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
                            slots--; progress = true;
                        }
                    }
                } while (progress);
                SampleState(); metrics.SampleMemory();
            }
            catch { Fail(); throw; }
            finally { pumping = false; }
        }
        void FinishSubscriptions(PhysicalWork work)
        {
            if (work.State != WorkState.Completed) return;
            for (int index = work.Subscribers.Count - 1; index >= 0; index--)
            {
                var request = work.Subscribers[index];
                if (!request.Verified || request.Finished) continue;
                request.Finished = true; request.Verify = null; request.PublishVerification = null;
                var document = request.Document;
                document.PendingAssetWorks--; document.ReadOnlyInputReferences--;
                work.Subscribers.RemoveAt(index);
                request.Document = null; request.Physical = null;
                CompleteReady(document);
            }
            if (work.Subscribers.Count == 0) { work.Declaration = null; work.Plugin = null; }
        }
        public void CompleteDocument(AssetExecutionDocument document, Action complete)
        {
            Check();
            if (document.IsClosed) throw new InvalidOperationException("Document version is already closed.");
            if (complete != null) document.Completions.Add(complete);
            document.IsClosed = true; DrainReady(); CompleteReady(document);
        }
        public void DeferCompletion(AssetExecutionDocument document, Action complete)
        {
            Check();
            if (!document.IsCompleted) { document.Completions.Add(complete); return; }
            try { complete(); }
            catch { document.IsCompleted = false; document.IsFailed = true; Fail(); throw; }
        }
        void CompleteReady(AssetExecutionDocument document)
        {
            if (!document.IsClosed || document.IsCompleted || document.IsFailed || document.PendingAssetWorks != 0) return;
            document.IsCompleted = true;
            Action[] callbacks = document.Completions.ToArray(); document.Completions.Clear();
            try { foreach (var complete in callbacks) complete(); }
            catch { document.IsCompleted = false; document.IsFailed = true; Fail(); throw; }
        }
        public void WaitDocument(AssetExecutionDocument document)
        {
            Check(); DrainReady();
            while (document.PendingAssetWorks != 0)
            {
                var tasks = new List<Task>();
                bool waitsForIoSlot = false;
                foreach (var work in active)
                {
                    bool required = false;
                    foreach (var request in work.Subscribers) if (ReferenceEquals(request.Document, document) && !request.Finished) { required = true; break; }
                    if (!required) continue;
                    if (work.State == WorkState.Ready)
                    {
                        waitsForIoSlot = true;
                        // A shared cache transfer first verifies its original
                        // declaration, which may belong to another document.
                        // That verifier is a real dependency of every subscriber.
                        var initial = work.Subscribers[0];
                        if (initial.Verification != null) tasks.Add(initial.Verification);
                    }
                    if (work.Compute != null) tasks.Add(work.Compute);
                    if (work.IO != null) tasks.Add(work.IO);
                }
                foreach (var request in verifying)
                    if (ReferenceEquals(request.Document, document) && request.Verification != null) tasks.Add(request.Verification);
                // A required result may be ready for an IO slot. Wait for any
                // current IO that can free that resource, never a FIFO head.
                if (tasks.Count == 0 || waitsForIoSlot)
                    foreach (var work in active) if (work.IO != null) tasks.Add(work.IO);
                if (tasks.Count == 0) throw new InvalidOperationException("Document has pending work without an executable dependency.");
                WaitAny(tasks); DrainReady();
            }
            CompleteReady(document);
        }
        public void WaitOutput(OutputManager outputManager)
        {
            Check();
            foreach (var document in documents.ToArray())
                if (ReferenceEquals(document.OutputManager, outputManager)) WaitDocument(document);
        }
        // An owner XML failure can occur after an included document has closed
        // its frontend. Finish that already accepted work before partial-session
        // saving; failed/cancelled workers still take the discard-and-join path.
        // This boundary never registers work or publishes a stream manifest.
        internal void CompleteAcceptedWorkOnFailure()
        {
            if (disposed || failed || cancellation.IsCancellationRequested) return;
            completingAcceptedOnly = true;
            try
            {
                foreach (var document in documents.ToArray())
                    if (document.IsClosed && !document.IsCompleted && !document.IsFailed) WaitDocument(document);
            }
            finally { completingAcceptedOnly = false; }
        }
        bool RequiredForPartialCompletion(PhysicalWork work)
        {
            foreach (var request in work.Subscribers)
                if (request.Document.IsClosed && !request.Document.IsFailed) return true;
            return false;
        }
        public void Flush()
        {
            Check(); DrainReady();
            while (active.Count != 0 || verifying.Count != 0)
            {
                var tasks = ResourceTasks();
                if (tasks.Count == 0) throw new InvalidOperationException("Asset work has no executable dependency.");
                WaitAny(tasks); DrainReady();
            }
        }
        public IList<T> ExecuteFrontend<T>(IList<Func<T>> work)
        {
            Check();
            var result = new T[work.Count];
            var tasks = new Dictionary<Task<T>, int>();
            long start = Start();
            try
            {
                for (int index = 0; index < work.Count; index++)
                {
                    AdmitCpu();
                    if (capacity <= 1) { long computeStart = Start(); try { result[index] = work[index](); } finally { metrics.FrontendComputeTicks += Elapsed(computeStart); } continue; }
                    Func<T> compute = work[index];
                    var task = Task.Factory.StartNew(() => {
                        long computeStart = Start();
                        try { return compute(); }
                        finally { Interlocked.Add(ref metrics.FrontendComputeTicks, Elapsed(computeStart)); }
                    }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
                    tasks.Add(task, index); frontendTasks.Add(task);
                    CollectFrontend(tasks, result);
                }
                while (tasks.Count != 0)
                {
                    var waits = new List<Task>(); foreach (var task in tasks.Keys) if (!task.IsCompleted) waits.Add(task);
                    WaitAny(waits); CollectFrontend(tasks, result); DrainReady();
                }
                return result;
            }
            catch
            {
                // Owner must not resume mutating this DOM while another pure
                // computation from the failed read-only batch still uses it.
                foreach (var task in tasks.Keys)
                {
                    try { task.GetAwaiter().GetResult(); } catch { }
                    frontendTasks.Remove(task);
                }
                Fail(); throw;
            }
            finally { metrics.FrontendTicks += Elapsed(start); }
        }
        void CollectFrontend<T>(Dictionary<Task<T>, int> tasks, T[] results)
        {
            var done = new List<Task<T>>();
            foreach (var item in tasks)
            {
                if (!item.Key.IsCompleted) continue;
                results[item.Value] = item.Key.GetAwaiter().GetResult(); done.Add(item.Key);
            }
            foreach (var task in done) { tasks.Remove(task); frontendTasks.Remove(task); }
        }
        public AssetBuffer ExecuteCompatibility(IAssetBuilderPlugin plugin, InstanceDeclaration declaration)
        {
            long ticks;
            return metrics.Execute(declaration.Handle.TypeName, () => plugin.ProcessInstance(declaration), out ticks);
        }
        void MergeWorkerMetrics(PhysicalWork work)
        {
            if (work.Encoded && !work.CompileCountMerged) { processor.RecordExecutionCompile(); work.CompileCountMerged = true; }
            if (!performance.Detailed || work.Compute == null || work.MetricsMerged) return;
            work.MetricsMerged = true;
            performance.RecordBackend("core.asset.compile", work.WorkTicks);
            performance.RecordBackend("core.compiler." + work.Declaration.Handle.TypeName, work.WorkTicks);
        }
        void WaitPlugin(IAssetBuilderPlugin plugin, PhysicalWork current)
        {
            while (true)
            {
                var calls = new List<Task>();
                foreach (var work in active)
                    if (!ReferenceEquals(work, current) && ReferenceEquals(work.Plugin, plugin) && work.Compute != null && !work.Compute.IsCompleted)
                        calls.Add(work.Compute);
                if (calls.Count == 0) return;
                WaitAny(calls, 1); DrainReady();
            }
        }
        void SampleState()
        {
            if (!metrics.Enabled) return;
            metrics.PeakCpuTasks = Math.Max(metrics.PeakCpuTasks, RunningCpu());
            metrics.PeakIoTasks = Math.Max(metrics.PeakIoTasks, RunningIO());
            metrics.PeakRetainedBytes = Math.Max(metrics.PeakRetainedBytes, retained);
            int awaiting = 0;
            foreach (var work in active)
                if ((work.Compute != null && work.Compute.IsCompleted) || (work.IO != null && work.IO.IsCompleted)) awaiting++;
            foreach (var request in verifying) if (request.Verification != null && request.Verification.IsCompleted) awaiting++;
            metrics.PeakAwaitingOwner = Math.Max(metrics.PeakAwaitingOwner, awaiting);
        }
        void Release(PhysicalWork work)
        {
            if (work.Released) return;
            work.Released = true;
            retained -= work.ReservedBytes; work.ReservedBytes = 0;
            if (work.Prepared != null) { work.Prepared.Dispose(); work.Prepared = null; }
            work.Buffer = null; work.Compute = null; work.Commit = null; work.Committed = null;
            if (work.Directory != null && Directory.Exists(work.Directory)) Directory.Delete(work.Directory, true);
        }
        void Fail()
        {
            failed = true;
            foreach (var document in documents)
                if (!document.IsCompleted) { document.IsFailed = true; document.Completions.Clear(); }
        }
        // Native calls cannot be interrupted safely. Join every started CPU/IO
        // operation before releasing its input lease or immutable document view.
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Exception failure = null;
            foreach (var request in verifying)
            {
                try { if (request.Verification != null) request.Verification.GetAwaiter().GetResult(); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendVerification"); }
                try { Replay(request.Diagnostics, request.SourcePath); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendDiagnostic"); }
            }
            foreach (var work in active)
            {
                try { if (work.Compute != null) work.Compute.GetAwaiter().GetResult(); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendJoin"); }
                try { Replay(work.Diagnostics, work.SourcePath); if (work.Prepared != null) processor.InDocumentContext(work.SourcePath, work.Prepared.FlushDiagnostics); MergeWorkerMetrics(work); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendDiagnostic"); }
                try { if (work.IO != null) work.IO.GetAwaiter().GetResult(); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendIO"); }
                try { if (work.Commit != null) processor.InDocumentContext(work.SourcePath, work.Commit.FlushDiagnostics); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendDiagnostic"); }
                try { Release(work); }
                catch (Exception error) { Accumulate(ref failure, error, "BackendRelease"); }
            }
            foreach (var document in documents) if (!document.IsCompleted) { document.IsFailed = true; document.PendingAssetWorks = 0; document.ReadOnlyInputReferences = 0; document.Completions.Clear(); }
            active.Clear(); verifying.Clear(); registry.Clear(); documents.Clear(); frontendTasks.Clear();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        static void Accumulate(ref Exception failure, Exception error, string key)
        {
            if (failure == null) failure = error;
            else if (!ReferenceEquals(failure, error)) failure.Data[key + "." + failure.Data.Count] = error;
        }
    }
}
#endif
