# 老 SDK Tokenizer 推土机功能移植方案（第一阶段，仅 Dozer）

## 1. 目标与范围

在**不替换老 SDK 编译流程**的前提下，交付一个可以放入原有 SDK/tools 的 **BinaryAssetBuilder.Tokenizer.dll**，使老 SDK 能编译原生 **DozerAIUpdate** 工程车及相关命令配置。同时准备配套 XSD 与完整示例。

第一阶段只增加 Dozer。**不移植**被动心控、维修停靠、其他自定义模块、FX、缓存优化或新编译器功能；不为建筑新增特殊 XML 标记、名单、专用 Lua 函数或扩展二进制协议。普通建筑仍按原 GameObject 模板编译。

“只换 DLL”指 SDK 编译支持：修改前的 SDK 工具、配置和其他 DLL 保持原样；游戏中实际运行推土机行为仍需配套的 EnhancerCorona 运行时及其 enableNativeDozer 开关。不能将“可编译”宣称为“原版游戏无需扩展就能运行”。

## 2. 已核实的代码基础

- **老 SDK 加载入口**：SDK/tools/BinaryAssetBuilder.exe.config 的 Tokenizer 插件注册为 BinaryAssetBuilder.Tokenizer.Plugin, BinaryAssetBuilder.Tokenizer；负责 GameObject、WeaponTemplate、ArmorTemplate 三类。待替换文件为 SDK/tools/BinaryAssetBuilder.Tokenizer.dll。
- **原 DLL 的恢复资料分支**：recovery/compiler-source；源码证据位于 recovery/decompiled/BinaryAssetBuilder.Tokenizer/，尤其是 BinaryAssetBuilder.Tokenizer/Plugin.cs 与 -Module-.cs。Plugin 的 ProcessInstance 将三类资产分派给各自处理器。原版插件 GetAllTypesHash 为 1424942948；Win32/Xbox360/PlayStation3 的 GetVersionNumber 分别为 4100/4101/4102。
- **必须注意**：recovery/decompiled 中的 -Module-.cs 是 ILSpy 对旧 C++/CLI 混合程序集的机械反编译，不是能直接用 dotnet build 编译的原始 C++/CLI 工程。原生 Tokenize(DozerAIUpdateModuleData) 的二进制合同有证据，但旧 DLL 缺少对应的 XML Marshal 入口。不能只修改一个 XSD 或简单重编机械反编译的 csproj，就声称完成可替换 DLL。
- **Dozer 实现参考**：feature/integration 的 recovery/src/Native/GameObjectDozer.h、GameObject.cpp、recovery/src/Bridge/GameObjectCompiler.cpp、recovery/docs/DOZER_XML_EXTENSIONS.md；仅提取 Dozer 必需的字段、模块分派及 XML 到原生数据的映射，不整批复制该分支的其他新功能。
- **现有 XSD 已具备大部分乃至全部入口**：SDK/Schemas/xsd/Modules/DozerAIUpdate.xsd 定义了三个 SageReal 字段，AssetTypeGameObject.xsd 的 AIModule 已允许 DozerAIUpdate，CnC3Types.xsd 已 include Modules/DozerAIUpdate.xsd，AssetTypeLogicCommand.xsd 已支持 DOZER_CONSTRUCT。recovery/compiler-source 与 feature/integration 中的 DozerAIUpdate.xsd 内容相同。因此 XSD 以整理、校验和打包为主；只有发现真实缺项才修改，不制造无意义差异。

## 3. 实施路线

### 步骤 A：先打通“老 SDK 原样替换 DLL”的构建基线

1. 从 recovery/compiler-source 独立派生旧 Tokenizer 开发分支。保留原 DLL 为只读对照，不修改 compiler-main 或 feature/integration。
2. 以现有恢复的 Native/Bridge/Plugin 代码为实现基础，建立一个**单程序集** Tokenizer 工程；输出名称、托管类型、接口、位数、程序集身份与原插件加载配置一致。外部可见类型至少为 BinaryAssetBuilder.Tokenizer.Plugin，实现 IAssetBuilderPlugin 的 Initialize、ReInitialize、ProcessInstance、GetAllTypesHash、GetVersionNumber 和 GetExtendedTypeInformation。
3. 完整保留原插件三类入口：GameObject（TypeId 2486173485）、WeaponTemplate（2496977262）、ArmorTemplate（980180622）。不能只写一个 Dozer 插件使其他已有资产类型失效。武器和护甲的输出行为保持原版；已有非 Dozer GameObject 也必须保持原始序列化结果。
4. 先用**未修改的老 SDK 临时副本**加载不含 Dozer 的重建 DLL，核对正常三类资产。未达到原版兼容前不要开始宣称推土机已完成。
5. **运行时兼容是硬门槛**：检查原 SDK EXE、原 Tokenizer 的 CLR/PE/依赖信息。VS2022 的 /clr 构建不自动等于原老 SDK 可直接加载；历史恢复测试曾在临时应用配置中引入 CLR4 legacy mixed-mode activation。最终版不得要求用户修改 BinaryAssetBuilder.exe.config、增加 CLR4 startup 配置或另装 Recovered.* 辅助 DLL。若现代 C++/CLI 路线无法满足，应使用兼容旧 CLR 的构建路线，或把所需逻辑实现为可在旧宿主加载的独立程序集；验证不过时明确标记未完成，不以“测试配置改一下能跑”替代交付。

这一步只是为旧 Tokenizer 建立真正可替换的构建基础，**不算新增其他功能**。不得把机械反编译项目能否编译作为唯一判断。

### 步骤 B：只补 Dozer 的 XML Marshal 和 Tokenize 分派

在 GameObject 的 AI 模块处理中仅新增 DozerAIUpdate 分支，复用 AIUpdate 的已有属性、默认值、继承和 UnitAITargetChooserData 处理，再写入三个 Dozer 字段。保持原版 GameObject 根布局、模块容器及序列化规则。

固定二进制合同如下：

| 项目 | 值 |
| --- | --- |
| Dozer 多态类型 ID | 0x8D093323 |
| Dozer 数据大小 | 0x58（88 字节） |
| AIUpdate 父层 token | (1, 0x08DBC00E, AIUpdate 子表偏移) |
| RepairHealthPercentPerSecond | offset 0x4C；token 0x8229777E |
| BoredTime | offset 0x50；token 0x18C1A7F3 |
| BoredRange | offset 0x54；token 0xC492A6B5 |
| 三个字段的标量 token 类型 | 0x645864F3 |

三个值按 **SageReal 原始 float** 写入，默认 0：RepairHealthPercentPerSecond 不除以 100；BoredTime 不执行 Time 单位转换。TypeId 继续交由 Core 的标准流程生成，MOD XML 无须手写。继承和 XML 合并也由老 Core 原流程负责。

GameObject 固有的 592 字节结构布局和固定 127 项根 token 容量不变，不增加布尔字段、建筑侧表或扩展协议。非 Dozer 路径不新建全局扫描、反射查找或运行时判断分支。

原版本元数据对 WeaponTemplate、ArmorTemplate 保持不变。GameObject 如需因序列化能力变化刷新旧缓存，只改变其必要的处理修订标识，并核实 Core 实际的缓存失效规则；不要简单提高全插件版本导致无关资产重编译，也不能复用可能误命中的旧 GameObject 缓存。

### 步骤 C：准备 XSD 和示例

先核对以下现成文件，不要求全盘重写：

1. SDK/Schemas/xsd/Modules/DozerAIUpdate.xsd：DozerAIUpdateModuleData 继承 AIUpdateModuleData，三个 SageReal 字段默认 0。
2. SDK/Schemas/xsd/AssetTypeGameObject.xsd：AIModule 能容纳 DozerAIUpdate。
3. SDK/Schemas/xsd/CnC3Types.xsd：确实 include 对应 Dozer 模块文件。
4. SDK/Schemas/xsd/AssetTypeLogicCommand.xsd：LogicCommandType 包含 DOZER_CONSTRUCT、Object 引用可指向普通建筑。

对原老 SDK 的 XSD 做一次内容校验；如以上文件已经满足要求，原样作为“可用 XSD 包”交付即可，**不为提交改动而改动**。若用户手里的老 SDK 版本不一致，按相对目录提供必要 XSD 文件及覆盖位置；不要把其他新功能的 XSD 混进去。

示例直接以 wu192/EnhancerCorona 的 feature/integration 分支 examples/generals-dozer/NativeDozer.xml 为基础精简，保留：
- 工程车的 KindOf 包含 DOZER，AI 中使用 DozerAIUpdate；
- LogicCommand Type=DOZER_CONSTRUCT 的 Object 指向普通 STRUCTURE 模板；
- 工程车命令集含对应建造命令及按钮；普通建筑无需新标记；
- 工厂的 UNIT_BUILD 只是生产工程车，不能代替工程车自身的 DOZER_CONSTRUCT。

不在本阶段新增脚本 API、建筑专用 XML 节点或额外数据文件。

### 步骤 D：Windows 定向验证（尽量复用已有组件）

只需一次集中 Windows x86 Actions 作业，不使用本地 PC，也不启动完整大型 MOD 性能基准：

1. **替换兼容性**：在原始 SDK 工具目录的临时副本里只替换 Tokenizer.dll，且不修改 EXE/config/Core/XmlCompiler/其他 DLL；实际启动 BAB 并检查插件加载、三类资产识别及编译成功。明确记录 CLR、位数、程序集依赖和结果。
2. **无 Dozer 回归**：用同一组普通 GameObject、WeaponTemplate、ArmorTemplate 输入，对原 DLL 和新 DLL 的 InstanceData、RelocationData、ImportsData 做逐字节比较；不同则失败。比较时关闭影响对比的旧缓存或使用全新输出。
3. **Dozer 专项**：使用包含 AIUpdate 继承属性、UnitAITargetChooserData、三个参数及全默认值的 Dozer XML，走**真实老 SDK 输入**（不是只调用独立 Native 单测），核对类型 ID、三个字段的 float 位模式、父层 token、根布局、重定位/导入数据；此处按原生合同验收，**不能**把没有 Dozer XML Marshal 的原 DLL 误作 Dozer 成功输出的比较对象。
4. **XSD 与完整示例**：实际用随包提供的 XSD 编译一份 NativeDozer 示例，确认 DOZER_CONSTRUCT/普通建筑引用走原 SDK 既有处理链；不靠绕过 XSD 的手工构造 Node 冒充成功。
5. **交付检查**：发布目录只允许新增的 Tokenizer.dll、确有必要的 XSD、示例与说明。需要额外运行 DLL、修改启动配置、或使正常资产产物变化时，判定“单 DLL 替换”尚未达标。

保留必要构建日志、关键二进制摘要、Windows x86 作业结论即可；不运行游戏内测试，不引入用于虚构异常场景的额外防御代码。

## 4. 预期交付

- **BinaryAssetBuilder.Tokenizer.dll**：完成同名、同加载入口的实际重建 DLL，必须经未改配置的老 SDK 验收。
- **XSD 包**：已核实可用的 Dozer 相关 XSD，保持 SDK 原目录结构；没有实际差异时提供原件而非伪修改。
- **NativeDozer.xml**：一份可编译的工程车、建造命令和按钮配置示例。
- **README**：只讲老 SDK 用户如何备份原 DLL、替换新 DLL、放置 XSD、编译示例及如何恢复旧 DLL。
- **构建脚本及收据**：可在 GitHub Actions 上复现生成 DLL 的配置、输入版本和最小验收结果。

## 5. 约束与完成标准

本轮只负责**编译端推土机支持**，不重新设计或修改 EnhancerCorona 的游戏运行时。最终声明完成，必须同时满足：无 Dozer 的原版编译输出保持一致；Dozer XML 可按原生二进制合同编译；用户仅替换一个 Tokenizer DLL（如原 SDK XSD 不齐则同时放置随包 XSD），其他原 SDK 工具和配置不动。

如果发现旧宿主的 CLR/混合程序集限制使新 DLL 无法原样加载，应先解决这一构建兼容问题，不能以要求用户修改 SDK 配置作为完成方案。此要求优先于直接复用 feature/integration 的现成 DLL 构建方式。

## 6. 依据

- 旧 DLL 恢复分支：https://github.com/wu192/NewSdk/tree/recovery/compiler-source
- 推土机原生编译资料：https://github.com/wu192/NewSdk/blob/feature/integration/recovery/docs/DOZER_XML_EXTENSIONS.md
- Dozer token 合同：https://github.com/wu192/NewSdk/blob/feature/integration/recovery/src/Native/GameObjectDozer.h
- 已有 XSD：https://github.com/wu192/NewSdk/blob/recovery/compiler-source/SDK/Schemas/xsd/Modules/DozerAIUpdate.xsd
- 实例 XML：https://github.com/wu192/EnhancerCorona/blob/feature/integration/examples/generals-dozer/NativeDozer.xml
