using System;
using System.Collections.Generic;

namespace BinaryAssetBuilder.Core;

// One owner-published, read-only document version. A repeated include subscribes
// to this version; a later mutable version receives a new scope.
public sealed class AssetExecutionDocument
{
    public AssetDeclarationDocument Document { get; internal set; }
    public OutputManager OutputManager { get; internal set; }
    public int PendingAssetWorks { get; internal set; }
    public int ReadOnlyInputReferences { get; internal set; }
    public bool IsClosed { get; internal set; }
    public bool IsCompleted { get; internal set; }
    public bool IsFailed { get; internal set; }
    internal readonly List<Action> Completions = new List<Action>();
}

public interface IAssetExecution
{
    bool HasPending { get; }
    AssetExecutionDocument BeginDocument(AssetDeclarationDocument document, OutputManager outputManager);
    void Enqueue(AssetExecutionDocument document, BinaryAsset asset, InstanceDeclaration declaration,
        IAssetBuilderPlugin plugin, Action compileCompatibility, Func<bool> verify,
        Action<bool> publishVerification, Action<AssetLocation> committed, bool parallelVerification = true, bool parallelCompilation = true);
    void EnqueueCached(AssetExecutionDocument document, BinaryAsset asset, InstanceDeclaration declaration,
        AssetLocation location, Func<bool> verify, Action<bool> publishVerification,
        Action<AssetLocation> committed, bool parallelVerification = true);
    IList<T> ExecuteFrontend<T>(IList<Func<T>> work);
    AssetBuffer ExecuteCompatibility(IAssetBuilderPlugin plugin, InstanceDeclaration declaration);
    void CompleteDocument(AssetExecutionDocument document, Action complete);
    void DeferCompletion(AssetExecutionDocument document, Action complete);
    void WaitDocument(AssetExecutionDocument document);
    void WaitOutput(OutputManager outputManager);
    void DrainReady();
    void Flush();
}
