namespace BinaryAssetBuilder.Core;

public interface IAssetBuilderVerifierPlugin : IAssetBuilderPluginBase
{
	bool VerifyInstance(InstanceDeclaration instance);
}

// Implement only for verifiers that read a finalized declaration without
// changing shared state. Worker diagnostics are captured and replayed by Owner.
public interface IConcurrentAssetBuilderVerifierPlugin : IAssetBuilderVerifierPlugin
{
}
