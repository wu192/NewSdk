# 老 SDK 原生推土机示例

`NativeDozer.xml` 基于 [EnhancerCorona 的原生示例](https://github.com/wu192/EnhancerCorona/blob/feature/integration/examples/generals-dozer/NativeDozer.xml)。将它和 `Mod.xml` 放入老 SDK 的 `Mods/LegacyDozer/Data/`，按该 SDK 的正常 MOD 编译流程编译。已有 MOD 只需在自己的 `Mod.xml` 中以 `type="all"` 包含 `NativeDozer.xml`。

示例依赖原版 `SageXml` 中的 `GlobalData/GlobalDefines.xml`、`AlliedMiner`、`AlliedPowerPlant`、`AlliedWarFactory` 及其继承依赖，使用标准 `DATA:` 路径和 `static/global/audio` 引用。它保留工程车模型、移动、生命与死亡效果；可在测试地图放置 `NativeDozer` 或可训练它的 `NativeDozerWarFactory`。继承、宏展开和资源引用须由真实老 SDK 处理，直接解析 XML 不能证明编译成功。

工程车配置 `DOZER` 和 `DozerAIUpdate`；命令集以 `DOZER_CONSTRUCT` 引用原版普通建筑，不修改建筑模板、不增加建筑标记。三个属性均为原始 `SageReal`，默认 `0`；`0.02/150/150` 直接写入浮点值，不做百分比或时间转换。游戏运行时仍需支持原生推土机的 EnhancerCorona，并启用 `enableNativeDozer`。

## XSD 清单和当前门槛

- `SDK/Schemas/xsd/Modules/DozerAIUpdate.xsd`：原版已有，沿用，内容未改。
- `SDK/Schemas/xsd/CnC3Types.xsd`：已包含上述类型文件，内容未改。
- `SDK/Schemas/xsd/AssetTypeLogicCommand.xsd`：已声明 `DOZER_CONSTRUCT`，内容未改。
- `SDK/Schemas/xsd/AssetTypeGameObject.xsd`：基线 `103f8ea14e6687884518047a861fc42e76abec0f` 的 `AIModule` **未声明** `DozerAIUpdate`；此处仅新增该元素声明。

方案称原版已经包含 AI 元素声明，但实际基线缺失。因此启用完整 schema 校验时，严格的“原版 SDK 只替换 Tokenizer DLL、其他文件完全不改”门槛尚不成立；还须使用上面补齐声明的 `AssetTypeGameObject.xsd`。这项必要修正不能记为严格 DLL 单文件替换验收通过。示例和 schema 检查也不代表替换 DLL 的真实 SDK 编译已通过。
