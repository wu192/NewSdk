# 老版 Tokenizer 的 Dozer 移植

状态：候选 DLL 已通过 CLR 2/x86 加载、旧类型逐字节对照及必要 XSD 下的真实 SDK 示例编译；严格 DLL-only 门槛失败，整体方案尚未完全验收。原 XSD 缺少 Dozer 声明，不能省略必要的一行修正。

本实现以 `plan/legacy-tokenizer-dozer-only` 的 `103f8ea14e6687884518047a861fc42e76abec0f` 为基线，仅扩展原生 DozerAIUpdate。原生布局、类型、三个原始 float 字段和 AIUpdate 继承合同遵循方案；没有加入其他扩展模块。

`recovery/src/LegacyTokenizer` 使用 CLR 2 C# 插件入口及 C ABI，将现有 GameObject、WeaponTemplate、ArmorTemplate 原生恢复源码链接进同名 DLL。原生代码使用静态 CRT；不使用现有要求 CLR 4 和配置修改的 C++/CLI 插件。构建工具 `SetLegacyTokenizerRuntime.cs` 核对合并输出的 CLR 2 引用、PE/元数据格式后，将现代链接器默认写入的运行时版本设置为 CLR 2；它不改 IL、原生代码或游戏资产格式，也不随 DLL 部署。此链接方案必须经真实 CLR 2/x86 加载测试确认，不能仅凭源码或组件检查宣称可替换。

## 构建与验收

在启用 NetFx3 的 Windows 和 Visual Studio x86 开发者命令行中运行：

```powershell
./recovery/scripts/BuildLegacyTokenizer.ps1 -OutputDirectory "$env:TEMP/legacy-tokenizer-build"
./recovery/scripts/TestLegacyTokenizer.ps1 -BuildDirectory "$env:TEMP/legacy-tokenizer-build"
```

工作流 `legacy-tokenizer-dozer.yml` 只运行一个 Windows x86 作业，一次构建、定向原/新三缓冲区对照、Dozer 布局/继承/重定位检查及真实 SDK 示例编译。不启用缓存；仅在构建、CLR 2 加载、旧类型回归及必要 XSD 的 SDK 集成均成功后上传最小候选包，保留一天。候选包另列严格 DLL-only 门槛结果，失败时不标为已完成替换验收。失败日志保留于 Actions 和 Job Summary。

真实 SDK 检查检出 `recovery/fixtures/legacy-dozer-sdk-files.txt` 列出的原始输入，拒绝编译日志中的 Error/Critical，并从最终 Mod manifest 精确提取 NativeDozer 的三缓冲区，核实原生数据、继承 token 与重定位。基线的 28 个相关 Art 文件原本就是空声明占位文件，保持原样；示例引用 SDK 原版基础 manifest 资源，本次不重新构建模型或贴图。

## XSD 与替换路径

- DLL：`SDK/tools/BinaryAssetBuilder.Tokenizer.dll`，保留原 EXE、配置及其他 DLL。
- 必需修正：`SDK/Schemas/xsd/AssetTypeGameObject.xsd` 的 AIModule 内增加 DozerAIUpdate 声明。
- 已存在且内容未改：`SDK/Schemas/xsd/Modules/DozerAIUpdate.xsd`、`SDK/Schemas/xsd/CnC3Types.xsd`、`SDK/Schemas/xsd/AssetTypeLogicCommand.xsd`。
- 示例与依赖说明：`recovery/examples/legacy-dozer/`。

方案“AssetTypeGameObject.xsd 已声明 Dozer”与基线实际文件不符，因此严格的“原版 SDK 只换 DLL 即可编译 Dozer XML”门槛存在矛盾。验证脚本分别执行保留原 XSD的 DLL-only 编译与补齐必要 XSD 后的编译；若严格门槛失败，即使补齐 XSD 的编译成功也记录未完成，不发布已通过的交付。

取得完整验收前请勿把候选 DLL 用作已验证替换件。运行时推土机仍需 EnhancerCorona；建筑保持普通 STRUCTURE，命令使用 DOZER_CONSTRUCT，不新增建筑标记。

## 实际验证与候选包

测试源码：`6ba78d5b8bc0b8e36e7f13c50c7e3db950e64920`；[Windows x86 运行 43698049060](https://github.com/wu192/NewSdk/actions/runs/43698049060)，attempt 1。CLR 2 真实加载及程序集身份/单模块检查通过；28 个旧类型样本的 84 个缓冲区逐字节一致，8 个 Dozer 样本通过。必要 XSD 的 SDK 编译无 Error/Critical，最终 manifest 中精确定位的 NativeDozer 三缓冲区原生合同通过。

原 XSD 的 SDK 编译 exit `-1`，错误为 AI 不允许 DozerAIUpdate 子元素；因此工作流最终为 failure，PR 保持草稿，未合入。此失败不被其他通过项覆盖。

[最小候选包](https://github.com/wu192/NewSdk/actions/runs/43698049060/artifacts/14154678064) 仅含 DLL、必要 XSD、示例和说明，Actions 保留一天。DLL SHA256：`11528f19e73efce1653236785ab0fe7e74cb9dcd107922e902111996a7f263c0`。先前运行 43697300548 的 SDK 检查曾缺少 Art 输入，已由本次完整依赖检出及严格日志/最终流检查替代；不把旧运行作为完整 SDK 验收依据。
