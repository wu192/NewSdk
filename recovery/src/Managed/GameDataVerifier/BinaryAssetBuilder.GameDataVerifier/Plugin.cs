using System.Collections.Generic;
using System.Xml;
using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder.GameDataVerifier;

#if NEWSdk_MONOLITHIC
internal class Plugin : IConcurrentAssetBuilderVerifierPlugin
#else
internal class Plugin : IAssetBuilderVerifierPlugin, IAssetBuilderPluginBase
#endif
{
	private static class GameObjectVerifier
	{
		private static readonly Tracer _Tracer = Tracer.GetTracer("GameDataVerifier", "Verifies that Sage RTS data is logic correct");

		public static bool Verify(InstanceDeclaration declaration)
		{
			return Verify(declaration.Node.Attributes["id"].Value, declaration.Document.SourcePath, declaration.Node, declaration.Document.NamespaceManager);
		}

		private static bool Verify(string gameObjectId, string srcFile, XmlNode node, XmlNamespaceManager namespaces)
		{
			bool flag = true;
			flag = VerifyModuleIds(node, gameObjectId, srcFile, namespaces);
			return VerifyDrawModules(node, gameObjectId, namespaces) && flag;
		}

		private static bool VerifyDrawModules(XmlNode node, string gameObjectId, XmlNamespaceManager namespaces)
		{
			bool flag = true;
			XmlNodeList xmlNodeList = node.SelectNodes("ea:Draws/*", namespaces);
			foreach (XmlNode item in xmlNodeList)
			{
				flag = VerifyDrawModules_DuplicateDefaultStates(item, gameObjectId, namespaces) && flag;
				Dictionary<string, int> stateNameAttributes = new Dictionary<string, int>();
				flag = VerifyDrawModules_UniqueConditionsYes(item, gameObjectId, ref stateNameAttributes, namespaces) && flag;
				flag = VerifyDrawModules_StateNameReferences(item, ref stateNameAttributes, gameObjectId, namespaces) && flag;
			}
			return flag;
		}

		private static bool VerifyDrawModules_StateNameReferences(XmlNode mod, ref Dictionary<string, int> stateNameAttributes, string gameObjectId, XmlNamespaceManager namespaces)
		{
			bool result = true;
			XmlNodeList xmlNodeList = mod.SelectNodes("ea:AnimationState/ea:Script", namespaces);
			string text = "CurDrawableSetTransitionAnimState";
			foreach (XmlNode item in xmlNodeList)
			{
				string value = item.FirstChild.Value;
				if (!value.Contains(text))
				{
					continue;
				}
				int num = value.LastIndexOf(text);
				int startIndex = num + text.Length;
				int num2 = value.IndexOf("(\"", startIndex) + 2;
				int num3 = value.IndexOf("\")", num2);
				string text2 = value.Substring(num2, num3 - num2);
				if (stateNameAttributes.ContainsKey(text2))
				{
					if (stateNameAttributes[text2] > 1)
					{
						_Tracer.TraceWarning("GameObject {0} has a script which calls {1} with an ambiguous StateName ({2})", gameObjectId, text, text2);
						result = false;
					}
				}
				else
				{
					_Tracer.TraceWarning("GameObject {0} has a script with an invalid StateName reference to {1}, <Draw> element {2}", gameObjectId, text2, mod.Name);
					result = false;
				}
			}
			return result;
		}

		private static bool VerifyDrawModules_UniqueConditionsYes(XmlNode mod, string gameObjectId, ref Dictionary<string, int> stateNameAttributes, XmlNamespaceManager namespaces)
		{
			bool result = true;
			XmlNodeList xmlNodeList = mod.SelectNodes("ea:AnimationState", namespaces);
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			foreach (XmlNode item in xmlNodeList)
			{
				XmlAttribute xmlAttribute = item.Attributes["ConditionsYes"];
				if (xmlAttribute != null)
				{
					string value = xmlAttribute.Value;
					if (!dictionary.ContainsKey(value))
					{
						string[] array = value.Split(' ');
						bool flag = false;
						foreach (string key in dictionary.Keys)
						{
							string[] array2 = key.Split(' ');
							if (array2.Length != array.Length)
							{
								continue;
							}
							bool flag2 = true;
							foreach (string text in array)
							{
								bool flag3 = false;
								for (int j = 0; j < array2.Length; j++)
								{
									if (text.Equals(array2[j]))
									{
										flag3 = true;
										break;
									}
								}
								if (!flag3)
								{
									flag2 = false;
									break;
								}
							}
							if (flag2)
							{
								flag = true;
							}
						}
						if (!flag)
						{
							dictionary.Add(value, array.Length);
						}
						else
						{
							_Tracer.TraceWarning("GameObject {0} has duplicate ConditionYes entries for {1} in draw element {2}", gameObjectId, value, mod.Name);
							result = false;
						}
					}
					else
					{
						_Tracer.TraceWarning("GameObject {0} has duplicate ConditionYes entries for {1} in draw element {2}", gameObjectId, value, mod.Name);
						result = false;
					}
				}
				XmlAttribute xmlAttribute2 = item.Attributes["StateName"];
				if (xmlAttribute2 != null)
				{
					string value2 = xmlAttribute2.Value;
					if (stateNameAttributes.ContainsKey(value2))
					{
						int num = stateNameAttributes[value2];
						num = (stateNameAttributes[value2] = num + 1);
					}
					else
					{
						stateNameAttributes.Add(value2, 1);
					}
				}
			}
			return result;
		}

		private static bool VerifyDrawModules_DuplicateDefaultStates(XmlNode mod, string gameObjectId, XmlNamespaceManager namespaces)
		{
			bool result = true;
			XmlNodeList xmlNodeList = mod.SelectNodes("ea:AnimationState[@ParseCondStateType=\"PARSE_DEFAULT\"]", namespaces);
			if (xmlNodeList.Count > 1)
			{
				_Tracer.TraceWarning("GameObject {0} has {1} default animation states in draw element {2}", gameObjectId, xmlNodeList.Count, mod.Name);
				result = false;
			}
			return result;
		}

		private static bool VerifyModuleIds(XmlNode node, string gameObjectId, string srcFile, XmlNamespaceManager namespaces)
		{
			bool result = true;
			XmlNodeList xmlNodeList = node.SelectNodes("ea:Behaviors/* | ea:Draws/* | ea:AI/* | ea:Body/*", namespaces);
			Set<string> set = new Set<string>();
			foreach (XmlNode item in xmlNodeList)
			{
				XmlAttribute xmlAttribute = item.Attributes["id"];
				if (xmlAttribute == null || xmlAttribute.Value == "")
				{
					_Tracer.TraceError("GameObject {0} has an unnamed module {1} in file {2}", gameObjectId, item.Name, srcFile);
					result = false;
					continue;
				}
				string a = xmlAttribute.Value.ToLower();
				if (set.Contains(a))
				{
					_Tracer.TraceError("GameObject {0} has 2 modules with the name {1} in file {2}", gameObjectId, xmlAttribute.Value, srcFile);
					result = false;
				}
				else
				{
					set.Add(a);
				}
			}
			return result;
		}
	}

	private static readonly Tracer _Tracer = Tracer.GetTracer("GameDataVerifier", "Verifies that Sage RTS data is logic correct");

	public void Initialize(object configSection, TargetPlatform platform)
	{
	}

	public void ReInitialize(object configSection, TargetPlatform platform)
	{
	}

	public bool VerifyInstance(InstanceDeclaration declaration)
	{
		if (declaration.Node == null)
		{
			return true;
		}
		string name;
		if ((name = declaration.Node.Name) != null && name == "GameObject")
		{
			return GameObjectVerifier.Verify(declaration);
		}
		return true;
	}
}
