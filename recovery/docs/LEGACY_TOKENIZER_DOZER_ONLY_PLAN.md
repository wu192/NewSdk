# 老 SDK 推土机移植方案（仅 Dozer）

## 目标

基于 `recovery/compiler-source` 还原的老编译器代码，修改并构建新的 `BinaryAssetBuilder.Tokenizer.dll`，让原版老 SDK **直接替换同名 DLL** 后可以编译推土机 XML；同时提供所需 XSD 和示例。**一轮完成源码、构建、验证和交付**，不拆分其他功能阶段。

只增加原生 `DozerAIUpdate` 支持。不得顺带加入被动心控、维修停靠、缓存优化、其他模块、建筑专用标记、额外 Lua 或扩展二进制格式。游戏运行时推土机功能仍由 EnhancerCorona 提供，本方案只改编译端。

## 需要修改的内容

1. **Tokenizer DLL**  
   以 `recovery/compiler-source` 的原 DLL 还原资料为基线，参考 `feature/integration` 的 `recovery/src/Native/GameObjectDozer.h`、`GameObject.cpp` 与 `recovery/docs/DOZER_XML_EXTENSIONS.md`，只补 `DozerAIUpdate` 的 XML Marshal、GameObject AI 模块分派和 Tokenize。  
   `recovery/decompiled/BinaryAssetBuilder.Tokenizer/-Module-.cs` 是反编译证据，不能直接当成可编译的原始 C++/CLI 工程。以现有可编译恢复源码建立**与老 SDK 插件入口、程序集身份、x86/CLR 兼容**的同名 DLL。保留 `GameObject`、`WeaponTemplate`、`ArmorTemplate` 原有处理，不把新编译器的其他逻辑移入。

2. **Dozer 二进制合同**  
   - 类型 ID：`0x8D093323`；数据大小：`0x58`。
   - 父层：`AIUpdate`，继承 token `(1, 0x08DBC00E, AIUpdate 子表偏移)`。
   - `RepairHealthPercentPerSecond`：偏移 `0x4C`，token `0x8229777E`。
   - `BoredTime`：偏移 `0x50`，token `0x18C1A7F3`。
   - `BoredRange`：偏移 `0x54`，token `0xC492A6B5`。
   - 三者均为原始 `SageReal/float`，默认 `0`，标量 token 类型 `0x645864F3`；不做百分比或时间单位转换。保留 `AIUpdate` 继承字段及 `UnitAITargetChooserData`，不改变 GameObject 原有根布局。

3. **XSD**  
   老 SDK 已有 `SDK/Schemas/xsd/Modules/DozerAIUpdate.xsd`，`AssetTypeGameObject.xsd` 已声明 Dozer 模块，`CnC3Types.xsd` 已包含它，`AssetTypeLogicCommand.xsd` 已有 `DOZER_CONSTRUCT`。核实这些文件即可；若无缺项**不修改 XSD 内容**，交付时只列出应使用的文件和路径。  
   示例参考 `wu192/EnhancerCorona` 的 `examples/generals-dozer/NativeDozer.xml`：工程车配置 `DOZER` + `DozerAIUpdate`，命令集使用 `DOZER_CONSTRUCT` 引用普通 `STRUCTURE`，建筑不需要新增标记。

## 实施及验收（同一轮）

1. 一次性完成 DLL 源码、构建脚本、XSD 整理和完整示例。
2. 在**未改动其他文件的原版老 SDK 副本**中，只替换 `BinaryAssetBuilder.Tokenizer.dll`；必须能直接加载，不要求修改 EXE、配置或其他 DLL，也不增加额外运行依赖。
3. 对普通 GameObject、WeaponTemplate、ArmorTemplate 分别做原版/新 DLL 的三缓冲区逐字节对照；Dozer 则核对原生类型、字段位模式、继承 token 与重定位，使用真实老 SDK 编译示例。旧 DLL 没有 Dozer XML Marshal，不拿它作为 Dozer 成功结果基准。
4. 实际完成 DLL、必要 XSD、示例和简短替换说明后提交；任一上述门槛失败，则明确记录未完成，不把组件单测通过等同于可替换 DLL。

## 实施约束

- **不要增加防御性编程**：不添加重复路径校验、多层兜底、无意义异常捕获、兼容未发布实现、额外全量扫描或性能热路径判断；真实构建或编译错误直接失败。
- **节省 Actions**：只使用一个 Windows x86 作业，集中构建、运行定向样本；不做 /Od×/O2×多平台矩阵，不跑大型 MOD、完整性能基准或重复构建。先使用不消耗 Actions 的源码/配置检查。
- **节省存储**：不启用 Actions 缓存，不上传 SDK、MOD、完整编译产物或大日志；日志写 Job Summary。只有需要获取构建 DLL 时才上传最小 `DLL + 必需 XSD + 示例 + 使用说明` 工件，保留 1 天，不产生其他归档。

## 参考

- [老编译器源码](https://github.com/wu192/NewSdk/tree/recovery/compiler-source)
- [Dozer 原生数据](https://github.com/wu192/NewSdk/blob/feature/integration/recovery/src/Native/GameObjectDozer.h)
- [Dozer XML 说明](https://github.com/wu192/NewSdk/blob/feature/integration/recovery/docs/DOZER_XML_EXTENSIONS.md)
- [推土机 XML 示例](https://github.com/wu192/EnhancerCorona/blob/feature/integration/examples/generals-dozer/NativeDozer.xml)
