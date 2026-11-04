namespace BinaryAssetBuilder.Core;

public interface IAssetBuilderPlugin : IAssetBuilderPluginBase
{
	AssetBuffer ProcessInstance(InstanceDeclaration instance);

	uint GetAllTypesHash();

	uint GetVersionNumber();

	ExtendedTypeInformation GetExtendedTypeInformation(uint typeId);
}

// Optional contract for plugins whose new encoders affect only some instances.
// The ordinary type metadata remains compatible with unchanged legacy formats.
public interface IInstanceProcessingHash
{
	uint GetProcessingHash(InstanceDeclaration instance);

	bool IsProcessingHashCurrent(uint typeId, uint processingHash, bool compilerVersionChanged);
}

