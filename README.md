# NewSdk — NewModCompiler

`compiler-main` 是 2026-10-27 起的重构结果与后续开发主线。保留旧分支历史，不将本次成果合入 `recovery/compiler-source`；旧 PR #13 关闭且不合并。此处的开发主线约定不等同于 GitHub 仓库网页默认分支设置。

## 获取工具

当前共享地编缓存版：[compiler-2026.11.02-shared-worldbuilder-cache](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.02-shared-worldbuilder-cache)。
同一SDK的所有Mod共用一份地编数据，旧有效缓存自动迁移/清理；D/R两Mod复用、并发首次提取和独立ZIP快测通过。

此前自动读取游戏地编版：[compiler-2026.11.01-game-worldbuilder](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01-game-worldbuilder)。
按老 SDK 规则自动使用游戏 `Data/WBData_12.big`，也可设置 `game-root`；已安装 SDK 地编时无需另下载500MB参考包。
[Windows 41322569300](https://github.com/wu162/NewSdk/actions/runs/41322569300) 的D/R App、小MOD原字节与独立ZIP快测通过。
Release EXE约5MB，仅比上一中立版增加10.5KiB；发布附件保持三运行文件ZIP与说明/清单。

上一中立地编修复版见 [compiler-2026.11.01-neutral Release](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01-neutral)。
已带入 `release/compiler-2026.11.01-harness` 的修改；[Windows 41319194413](https://github.com/wu162/NewSdk/actions/runs/41319194413)
的 Debug/Release、Core/CLI、小MOD原处理链及独立ZIP均通过。编译器 ZIP 保持三个运行文件，官方参考数据另附独立ZIP。
配置和验证边界见 [中立地编修复说明](recovery/docs/NEUTRAL_WORLDBUILDER.md)。

Joypad 并行版已发布：[NewModCompiler 2026.11.01](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01)。普通使用选择 `NewModCompiler-Win32-Release.zip`，Debug 供调试。11 个独立根已纳入 worker，四平台与 45 资产小 MOD、缓存、原 SDK 四流及独立 ZIP 验收通过；本轮未运行大型 MOD 或全量 SDK 编译。[范围与二进制来源](recovery/docs/components/xml/XML_JOYPAD.md)。发行附件及自动标签归档均不含编译器源码；源码变更仍在 PR #49，未合并。以下 `8fe12ca9b` 记录保留为历史。

生产/测试源码 `8fe12ca9b` 的 [Windows 41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818) 已整体通过：Debug/Release 构建、原元数据完整转录、组件与实际四 worker 并行、缓存、完整 mymod 原版对照、两个独立三文件 ZIP，以及完整 Upheaval 22 样本。插件版本 `4202` 和三个 ProcessingHash 保留原 DLL 值。
[常见格式与并行 CLI 收据](recovery/evidence/texture/plugin-validation/41185772818-1/common-summary.json)已入库：D/R 各31组件结果与上一轮原字节相同，16个真实CLI用例通过。本轮 Debug/Release 包见[本轮产物归档](https://github.com/wu162/NewSdk/actions/runs/41185772818/artifacts/13037167160)；最终归档已下载复算，Debug/Release 六份运行文件及两个三文件 ZIP 均与生产构建和发布清单核对通过，详见[完整验收摘要](recovery/evidence/texture/plugin-validation/41185772818-1/full-summary.json)。上一轮 `4c74bf64b` 的[验收记录](https://github.com/wu162/NewSdk/actions/runs/41180859852)保留为历史，历史 `compiler-main` 发行见[发行说明](recovery/COMPILER_RELEASE.md)。

本分支将 Texture 编译器源码内建到 EXE，新构建的工具 ZIP 只有三个运行文件：NewModCompiler.exe、NewModCompiler.exe.config、antlr.runtime.dll。SDK、MOD 源码和系统运行库单独准备。要求 Windows x86 兼容环境、.NET Framework 4.8 及与构建工具链匹配的 x86 VC 运行库；Texture 不再依赖原 DLL、VC80 或 DirectX 9/D3DX。先运行 doctor 检查环境。更早使用原 DLL 的历史包仍保留五文件布局；本次能力与边界见 [Texture 说明](recovery/docs/TEXTURE_PLUGIN.md)。

## 使用

完整操作步骤见 [新编译器使用说明](recovery/COMPILER_USAGE.md)，开发和历史资料见 [文档入口](recovery/README.md)。

本分支将 [Texture 编译插件](recovery/docs/TEXTURE_PLUGIN.md) 直接编入 EXE，覆盖常见图像输入及七种 SDK 输出格式。`Texture`、`OnDemandTexture`、`TerrainTextureAtlas` 根据输入租约与内存预算进入 worker，S3 状态按线程隔离；资源不足或无法识别输入头时回到 owner 执行。切换纹理实现时可使用干净或独立的缓存、中间目录，避免复用具有相同原身份但 BC1 编码字节不同的既有产物；当前验收状态见该文档。

本分支内建 [FLA/AS UI 编译插件](recovery/docs/FLA_UI_COMPILER.md)：可自动发现 `Data/AptUI/` 下的二进制 Flash MX 2004/8 FLA，与对应 AS 直接编译为 MOD 四流，也可由 `AptAptData` XML 声明与其他资产混合编译。

```powershell
./NewModCompiler.exe doctor --sdk-root C:\NewSdk\SDK
./NewModCompiler.exe plan --sdk-root C:\NewSdk\SDK --mod C:\Mods\Example --out C:\Build\Example --mod-version 1.0
./NewModCompiler.exe build --sdk-root C:\NewSdk\SDK --mod C:\Mods\Example --out C:\Build\Example --mod-version 1.0
```

`--mod` 指向 MOD 文件夹。默认自动发现 base、APT UI、全局元数据，输出 BIG + SKUDEF 并包含 Additional；需要松散流时显式使用 `--package-format loose`。`--package-content global-metadata` 兼容旧名 `maps`。成功构建的最终文件位于输出根下 `published`，上例显式传入版本 `1.0`，BIG/SKUDEF 名称为 `<MOD目录名>_1.0`。构建同时输出日志、JSONL 事件及最终报告，适合后续界面调用；不启动旧 GUI 或 BAB 子进程。

普通帮助 `--help` 列出 MOD/SDK/输出路径、内容与格式、Additional、MOD 版本、SKUDEF 名称、并行任务数及性能统计共 10 个参数。`--jobs auto`、`--performance off` 为默认值。

- `--mod-version 2.7-beta`、`--skudef-name ExampleLauncher`：设置 BIG/SKUDEF 身份。最终格式为 BIG 的 `build`/`plan` 每次必须在命令行显式传入版本，项目预设不能代替；名称默认为 MOD 目录名。
- `--include-additional=false`：关闭默认 Additional；开启时先取 SDK/Additional，再由 MOD/Additional 覆盖，排除 `.asset`，覆盖冲突记录警告。

loose（包括兼容 development 派生的 loose）和 `doctor` 不要求显式版本；显式 loose 仍保留原包身份校验。

MOD缓存、四流和编译临时文件放在传入 SDK 的 `SuperCompilerCache/<ModKey>/`；地编参考则统一放在 `SuperCompilerCache/shared/neutral-worldbuilder/`，同SDK所有Mod共享。MOD 源码只读；BIG 输出仅保留包和诊断，loose 将本轮四流/CDATA 导出到 `published`。更换 `--out` 不改变缓存分区。路径覆盖、并发锁及本次验证范围见 [集中缓存说明](recovery/docs/SUPERCOMPILER_CACHE.md)。

高级能力仍可直接使用，通过 `--help-advanced` 和[高级选项说明](recovery/COMPILER_USAGE.md#6-高级选项)查看。`--profile` 仅保留旧脚本兼容行为，详见[参数兼容表](recovery/CLI_COMPATIBILITY.md)。

布尔值使用等号，或单独裸开关启用；不要写成 `--include-additional true`。详细包布局见 [P7 打包合同](recovery/P7_PACKAGING.md)。

## 已完成与范围

核心编译、单进程 base/apt/maps、日志/协议、构建与会话缓存、默认 BIG/SKUDEF、Additional、纯 C# MakeBig 入 EXE 已有历史验收。当前源码 Texture 三文件产品及并行改动的状态见 [Texture 说明](recovery/docs/TEXTURE_PLUGIN.md)。原工具的特定 LoD 来源表尾字节差异见 [说明](recovery/P7_LOD_ORIGINAL_TAIL.md)。

AssetMerge 已按用户要求排除。本分支按最新请求补充[官方 Worldbuilder 1.12 中立地编参考数据与配置支持](recovery/docs/NEUTRAL_WORLDBUILDER.md)：`--fix-neutral-assets=true` 可由 MOD JSON 或 EXE 配置设置，完整官方 payload 以分块压缩形式保存在 Git，按实际引用解压。真实验证结果及边界见该说明。完整 UI/runtime、中文纹理路径仍有未覆盖范围；原 SDK 缓存完整恢复的当前验证状态见 [缓存说明](recovery/docs/LEGACY_INCREMENTAL_CACHE.md)。

版本与必要旧参数已形成[明确合同](recovery/CLI_COMPATIBILITY.md)，LoD 对原工具越界采用完整表复制的确定性修复，专项与最终生产/ZIP验收已通过；不把这些未完成项目表述为全功能兼容。当前不支持的参数明确拒绝。压缩/部分/冻结会话、touch、原缓存目录和 `/iod` 已接入本分支，完整生产缓存矩阵、mymod 原版差分及独立 ZIP 门槛已通过[运行 40814401237](https://github.com/wu162/NewSdk/actions/runs/40814401237)，准确覆盖边界见缓存说明；额外预编译及替代输出/资产报告等选项尚未开放；并非每个旧 BAB 参数都已支持。开发续接见 [REFACTOR_RESUME](recovery/REFACTOR_RESUME.md)，历史实施设计见 [重构方案](recovery/NewSdk_ModCompiler_Refactor_Plan.md)。
