using System;
using System.Collections.Generic;

namespace BinaryAssetBuilder.Core;

public class VerifierPluginRegistry
{
	private class NullVerifierPlugin : IConcurrentAssetBuilderVerifierPlugin
	{
		public void Initialize(object configSection, TargetPlatform platform)
		{
		}

		public void ReInitialize(object configSection, TargetPlatform platform)
		{
		}

		public bool VerifyInstance(InstanceDeclaration instance)
		{
			return true;
		}
	}

	private static Tracer _Tracer = Tracer.GetTracer("VerifierPluginRegistry", "Maps types to verifier plugins");

	private IAssetBuilderVerifierPlugin _DefaultPlugin = new NullVerifierPlugin();

	private Dictionary<uint, IAssetBuilderVerifierPlugin> _PluginMap = new Dictionary<uint, IAssetBuilderVerifierPlugin>();

	public IAssetBuilderVerifierPlugin DefaultPlugin
	{
		get
		{
			return _DefaultPlugin;
		}
		set
		{
			_DefaultPlugin = value;
		}
	}

	public VerifierPluginRegistry(PluginDescriptor[] plugins, TargetPlatform platform)
	{
		foreach (PluginDescriptor pluginDescriptor in plugins)
		{
			pluginDescriptor.Initialize(platform);
			if (!(pluginDescriptor.Plugin is IAssetBuilderVerifierPlugin assetBuilderVerifierPlugin))
			{
				throw new ApplicationException($"'{pluginDescriptor.QualifiedName}' does not implement IAssetBuilderVerifierPlugin");
			}
			if (pluginDescriptor.HandledTypes.Count > 0)
			{
				foreach (uint handledType in pluginDescriptor.HandledTypes)
				{
					AddPlugin(handledType, assetBuilderVerifierPlugin);
				}
			}
			else
			{
				DefaultPlugin = assetBuilderVerifierPlugin;
			}
		}
	}

	public void ReInitialize(PluginDescriptor[] plugins, TargetPlatform platform)
	{
		foreach (PluginDescriptor pluginDescriptor in plugins)
		{
			pluginDescriptor.ReInitialize(platform);
		}
	}

	public void AddPlugin(uint typeId, IAssetBuilderVerifierPlugin plugin)
	{
		_PluginMap.Add(typeId, plugin);
	}

	public IAssetBuilderVerifierPlugin GetPlugin(uint typeId)
	{
		IAssetBuilderVerifierPlugin value = null;
		if (!_PluginMap.TryGetValue(typeId, out value))
		{
			value = _DefaultPlugin;
		}
		return value;
	}
}
