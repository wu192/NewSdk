using System;
using System.Collections.Generic;

namespace BinaryAssetBuilder.Core;

public class PluginRegistry
{
	private class NullPlugin : IAssetBuilderPlugin, IAssetBuilderPluginBase
	{
		public void Initialize(object configSection, TargetPlatform platform)
		{
		}

		public void ReInitialize(object configSection, TargetPlatform platform)
		{
		}

		public AssetBuffer ProcessInstance(InstanceDeclaration instance)
		{
			_Tracer.TraceError("Couldn't process {0}. No matching plugin found.", instance.Handle);
			return null;
		}

		public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
		{
			ExtendedTypeInformation extendedTypeInformation = new ExtendedTypeInformation();
			extendedTypeInformation.HasCustomData = false;
			extendedTypeInformation.TypeHash = 0u;
			extendedTypeInformation.ProcessingHash = 0u;
			extendedTypeInformation.TypeId = typeId;
			extendedTypeInformation.TypeName = "<Unknown>";
			return extendedTypeInformation;
		}

		public uint GetAllTypesHash()
		{
			return 0u;
		}

		public uint GetVersionNumber()
		{
			return 0u;
		}
	}

	private static Tracer _Tracer = Tracer.GetTracer("PluginRegistry", "Maps types to plugins");

	private IAssetBuilderPlugin _DefaultPlugin = new NullPlugin();

	private Dictionary<uint, IAssetBuilderPlugin> _PluginMap = new Dictionary<uint, IAssetBuilderPlugin>();

	private Dictionary<uint, ExtendedTypeInformation> _TypeInfoMap = new Dictionary<uint, ExtendedTypeInformation>();

	private Dictionary<uint, bool> _BuildCacheMap = new Dictionary<uint, bool>();

	private readonly Dictionary<IAssetBuilderPlugin, uint> _PluginRouteCounts = new Dictionary<IAssetBuilderPlugin, uint>();

	private bool _DefaultUseBuildCache;

	public IAssetBuilderPlugin DefaultPlugin
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

	public Dictionary<uint, IAssetBuilderPlugin>.ValueCollection AllPlugins => _PluginMap.Values;

	public uint AssetBuilderPluginsVersion
	{
		get
		{
			uint num = 0u;
			foreach (var plugin in _PluginRouteCounts)
			{
				num += plugin.Key.GetVersionNumber() * plugin.Value;
			}
			return num + DefaultPlugin.GetVersionNumber();
		}
	}

	public PluginRegistry(PluginDescriptor[] plugins, TargetPlatform platform)
	{
		foreach (PluginDescriptor pluginDescriptor in plugins)
		{
			pluginDescriptor.Initialize(platform);
			if (!(pluginDescriptor.Plugin is IAssetBuilderPlugin assetBuilderPlugin))
			{
				throw new ApplicationException($"'{pluginDescriptor.QualifiedName}' does not implement IAssetBuilderPlugin");
			}
			if (pluginDescriptor.HandledTypes.Count > 0)
			{
				foreach (uint handledType in pluginDescriptor.HandledTypes)
				{
					AddPlugin(handledType, assetBuilderPlugin);
					_BuildCacheMap[handledType] = pluginDescriptor.UseBuildCache;
				}
			}
			else
			{
				DefaultPlugin = assetBuilderPlugin;
				_DefaultUseBuildCache = pluginDescriptor.UseBuildCache;
			}
		}
	}

	public void ReInitialize(PluginDescriptor[] plugins, TargetPlatform platform)
	{
		foreach (PluginDescriptor pluginDescriptor in plugins)
		{
			pluginDescriptor.ReInitialize(platform);
		}
		_TypeInfoMap = new Dictionary<uint, ExtendedTypeInformation>();
	}

	public void AddPlugin(uint typeId, IAssetBuilderPlugin plugin)
	{
		_PluginMap.Add(typeId, plugin);
		_PluginRouteCounts.TryGetValue(plugin, out uint routes);
		_PluginRouteCounts[plugin] = routes + 1;
	}

	public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
	{
		ExtendedTypeInformation value = null;
		if (!_TypeInfoMap.TryGetValue(typeId, out value))
		{
			IAssetBuilderPlugin plugin = GetPlugin(typeId);
			if (plugin != null)
			{
				value = plugin.GetExtendedTypeInformation(typeId);
			}
			bool value2 = _DefaultUseBuildCache;
			_BuildCacheMap.TryGetValue(typeId, out value2);
			value.UseBuildCache = value2;
			_TypeInfoMap.Add(typeId, value);
		}
		return value;
	}

	public IAssetBuilderPlugin GetPlugin(uint typeId)
	{
		IAssetBuilderPlugin value = null;
		if (!_PluginMap.TryGetValue(typeId, out value))
		{
			value = _DefaultPlugin;
		}
		return value;
	}

	public uint GetProcessingHash(InstanceDeclaration instance)
	{
		var policy = GetPlugin(instance.Handle.TypeId) as IInstanceProcessingHash;
		return policy != null ? policy.GetProcessingHash(instance) : GetExtendedTypeInformation(instance.Handle.TypeId).ProcessingHash;
	}

	public bool IsProcessingHashCurrent(InstanceDeclaration instance, bool compilerVersionChanged)
	{
		var policy = GetPlugin(instance.Handle.TypeId) as IInstanceProcessingHash;
		return policy != null ? policy.IsProcessingHashCurrent(instance.Handle.TypeId, instance.ProcessingHash, compilerVersionChanged) :
			instance.ProcessingHash == GetExtendedTypeInformation(instance.Handle.TypeId).ProcessingHash;
	}
}

