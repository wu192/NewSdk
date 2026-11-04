# NewModCompiler 使用说明

适用开发分支：`recovery/legacy-sdk-cache`（本轮状态见 [续接入口](RESUME.md)，源码依据见 [并行输入说明](docs/TOKENIZER_PARALLEL_INPUT.md)）；历史发行主线为 `compiler-main`。目标平台为 Windows/Win32、.NET Framework 4.8；本工具负责从现有 MOD 源码生成流文件或 BIG/SKUDEF，不包含旧 GUI。最终发行状态与下载链接见 [发行说明](COMPILER_RELEASE.md)。新增包版本参数的最终验收结果以该页为准。

## 后端并行与旧缓存

`--jobs auto|<正整数>` 默认 auto，表示 CPU 和当前 Win32 可用内存共同约束的 worker 上限；`--jobs 1` 保留 owner 串行对照，`--jobs 4` 允许最多四个已认证后端任务。`plan`、`build` 和 `newmodcompiler.json` 的 `"jobs": "auto"` 或整数均支持。修改 jobs 不改变原缓存身份。

并行路由包括 GameObject、WeaponTemplate、ArmorTemplate、W3DAnimation/W3DMesh、精确内建编码器的 NONE/XAS AudioFile（无显式 PCSampleRate）、MP3 passthrough、295 个认证 XML metadata TypeId（含 11 个 Joypad 独立根）、四类 APT，以及本轮接入的 `Texture`、`OnDemandTexture`、`TerrainTextureAtlas`。纹理在 owner 准备只读输入租约并按图像尺寸、DDS 长度或图集工作量计入预算，解码和编码在 worker 执行；S3 临时状态与懒初始化表按线程隔离。资源不足、输入租约失败、纹理头无法识别、APT 非 ASCII 路径或未认证后端回到 owner。Core XML 前端、验证、缓存、诊断与输出提交保持 owner 顺序。源码 `8fe12ca9b` 已通过 [Windows 41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818) 的 Debug/Release 实际四 worker、原元数据完整转录、组件、缓存、完整 mymod、独立 ZIP 及 Upheaval 22 样本验收；[常见格式与16个CLI用例收据](evidence/texture/plugin-validation/41185772818-1/common-summary.json)已入库。

默认 `/sc:true /bc:true /csc:true /psc:true /fsc:false /atc:false` 不变，Texture 继续使用原 BAB 缓存格式，目录统一位于当前 SDK 的 MOD 专属分区，插件版本 `4202` 和三个 ProcessingHash 保留原 DLL 值。工具不会因切换实现自动清理缓存或更换身份；BC1 编码字节可能与既有产物不同，切换时应在无构建运行时清理当前 MOD 的缓存分区。无需关闭 SessionCache 来使用多 worker。`--sc=false --bc=false --jobs 1|4` 可作纯冷态吞吐对照。没有新增动作缓存、额外持久结果或文件哈希体系。

Joypad 新并行范围见 [Joypad 说明](docs/components/xml/XML_JOYPAD.md)。本次只验证定向组件与 45 资产小 MOD，源运行目录和独立三文件 ZIP 的 jobs1/4/auto、缓存、编辑和原 SDK 四流对照已通过；没有运行大型 MOD 或全量 SDK 编译。上述 `8fe12ca9b` 的纹理/完整 MOD 结果为历史证据，未冒充本轮重测。Joypad 并行包已按用户明确批准发布：[2026.11.01 Release](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01)。

## 1. 安装与检查

本分支新构建将 Texture 编译器源码内建到 EXE，ZIP 只有三个运行文件：`NewModCompiler.exe`、`NewModCompiler.exe.config`、`antlr.runtime.dll`。完整解压到单独目录，保留三个文件。本轮源码 `8fe12ca9b` 的完整 Windows 及独立 ZIP 验收已通过，Debug/Release 包见[本轮产物归档](https://github.com/wu162/NewSdk/actions/runs/41185772818/artifacts/13037167160)中的 `artifacts/final-release/41185772818-1/`，通常使用 `NewModCompiler-Win32-Release.zip`；最终归档已下载复算，六份运行文件和两个三文件 ZIP 已与生产构建、发布清单核对，见[完整验收摘要](evidence/texture/plugin-validation/41185772818-1/full-summary.json)。[范围与原收据](docs/TEXTURE_PLUGIN.md)。历史五文件包仍可从 [功能验收归档40915444929](https://github.com/wu162/NewSdk/actions/runs/40915444929/artifacts/12916669037) 内 `artifacts/final-release/40915444929-1/NewModCompiler-Win32-Release.zip` 获取（生产源码6d39f833）；该包使用原 Texture DLL 和补丁清单，保留其原布局。

目标机器需要 Windows x86 兼容环境、.NET Framework 4.8，以及与编译工具链匹配的 x86 VC 运行库。新构建的 Texture 不再需要原 DLL、VC80 CRT 或 DirectX 9/D3DX；历史五文件包仍需要这些依赖。SDK 资源和 MOD 源码单独提供；工具 ZIP 不包含完整 SDK，也不下载游戏文件。

在 PowerShell 中进入工具目录，先执行：

```powershell
./NewModCompiler.exe --help
./NewModCompiler.exe doctor --sdk-root "C:\NewSdk\SDK"
```

`doctor` 只要求 SDK 路径，检查 SDK 配置、Schema 和实际编译依赖，不要求 MOD、输出目录或显式 MOD 版本，也不编译资产。成功会输出 `CORE_ENVIRONMENT_OK`；失败时先按标准错误中的诊断代码处理依赖。

## 2. 准备 MOD 文件夹

`--mod` 必须在命令行中指定 MOD 目录，不是单个 XML、manifest 或 babproj；项目预设不能代替 MOD 位置。默认自动识别以下入口：

| 内容 | MOD 内入口 | 行为 |
| --- | --- | --- |
| `base` | `Data/Mod.xml` | 自动模式下存在才编译；显式选择后必须存在 |
| `apt` | `Data/AptUI/*.xml` 和递归 FLA | 顶层 XML 与未被 XML 引用的 FLA 按稳定顺序逐流编译 |
| `global-metadata`（兼容旧名 `maps`） | `Data/AdditionalMaps/MapMetaData_*.xml` | 只发现该目录直接子文件 |

被入口引用的数据、贴图、模型、音频等资源仍须完整；选择内容不会自动修复缺失的资产依赖。APT 同时支持 XML 入口与二进制 FLA/AS 直接编译，入口去重和源依赖规则见 [FLA UI 说明](docs/FLA_UI_COMPILER.md)。

输出目录必须与 SDK、MOD 输入目录分离。例如 `C:\Build\Example`，不要放在 `C:\Mods\Example` 内部。为不同 MOD 使用各自输出根。

## 3. 查看计划与编译

先看计划（只发现流，不实际编译）：

```powershell
./NewModCompiler.exe plan --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" --mod-version 1.0
```

编译成 BIG：

```powershell
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" --mod-version 1.0
```

仅编译基础数据并输出松散流：

```powershell
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\ExampleLoose" --package-content base --package-format loose
```

默认 `--package-content auto`、`--package-format big`，输出 BIG + SKUDEF 并自动包含 Additional。只有 APT 或全局元数据的 MOD 也能直接编译，无需创建空的 Mod.xml。自动发现没有任何有效编译入口时以 `PLAN_NO_INPUTS` 报错；仅有 Additional 不算编译入口。正常编译无需填写平台或缓存开关。最终格式为 BIG 的 `build` 和 `plan` 每次必须在命令行显式传入 `--mod-version`，项目预设或旧默认值均不能代替；loose（包括 development 派生的 loose）不要求显式版本，并保留原有包身份校验。

普通帮助 `--help`（或 `-h`）只列出以下 10 个编译与性能参数，以及 `build`、`plan`、`doctor` 和帮助入口。高级能力通过 `--help-advanced` 查看，可直接与 `build` 使用。

| 参数 | 默认值 / 用途 |
| --- | --- |
| `--mod` | 必需的 MOD 源码目录，每次在命令行指定 |
| `--sdk-root` | SDK 目录，可保存到项目预设 |
| `--out` | 独立输出目录，可保存到项目预设 |
| `--package-content` | `auto`，或 `base,apt,global-metadata` 的组合 |
| `--package-format` | `big`，也可显式选择 `loose` |
| `--include-additional` | BIG 默认 true，loose 默认 false |
| `--mod-version` | 最终格式为 BIG 的 build/plan 每次必须在 CLI 显式指定；项目预设不能代替 |
| `--skudef-name` | MOD 目录名 |
| `--jobs` | `auto` 或正整数，默认 `auto` |
| `--performance` | `off`、`summary`、`detailed`，默认 `off` |

`--package-content base|apt|global-metadata` 或逗号组合仍提供原有的部分编译，旧名 `maps` 完全兼容。显式选择为空的可选组时继续保留上一代，不生成空 BIG；没有改变部分编译和完整打包机制。

成功产物位于输出根的 `published` 目录；本轮产物路径、类别和长度从 `build-report.json` 的 `artifacts` 获取。BIG/SKUDEF 在最终目录逐文件替换同名文件；Core 四流在 SDK 缓存分区中增量复用，loose 仅将本轮四流/CDATA 导出到 `published`。编译器不递归清空输出根，也不维护整个 MOD 的事务回滚；日志或报告失败仍报错，但保留已生成的文件。见[集中缓存与交付目录说明](docs/SUPERCOMPILER_CACHE.md)。

## 4. 包名、版本和附加文件

```powershell
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" --mod-version 2.7-beta --skudef-name ExampleLauncher --include-additional=true
```

| 选项 | 默认值 / 效果 |
| --- | --- |
| `--mod-version` | BIG build/plan 每次在 CLI 显式指定，生成 `<MOD目录名>_<所传版本>.big`；项目值不能代替 |
| `--skudef-name` | MOD 目录名，生成 `<基名>_<版本>.skudef`，其中 add-big 引用真实 BIG 文件名 |
| `--include-additional=false` | BIG 默认自动包含，手动关闭时使用 false；先取 `SDK/Additional`，后由 `MOD/Additional` 同名文件覆盖，排除 `.asset` |

以上示例发布 `Example_2.7-beta.big` 和 `ExampleLauncher_2.7-beta.skudef`。版本/名称选项仅用于 BIG；MOD 版本使用 ASCII 字母/数字/点/下划线/连字符，SKUDEF 名称为单个 ASCII 文件基名。当前 BIG 也要求 MOD 目录基名为 ASCII。高级游戏版本字段见下文。

Additional 的相对目录进入 BIG 根；它能够覆盖已编译文件及 APT 版本文件，这是保留的旧脚本行为。发生 SDK/MOD 同名覆盖或 Additional 覆盖已编译包成员时，日志、JSONL 和报告记录 `ADDITIONAL_OVERWRITE` 警告；仍按旧顺序继续打包。默认 base BIG 包含 `data/mod.*` 与低/中 LoD 流；APT、maps 和 CDATA 布局见 [打包说明](P7_PACKAGING.md)。APT 的 `.version` 空格/CRLF 标记与 MOD 包版本是两件事。

布尔值使用 `--include-additional=true`、`--include-additional=false`，或单独裸开关启用；不要写成 `--include-additional true`。

## 5. 项目预设

可在 MOD 目录放置 `newmodcompiler.json`：

```json
{
  "sdk-root": "C:/NewSdk/SDK",
  "out": "C:/Build/Example",
  "mod-version": "2.7-beta"
}
```

SDK 和输出路径可由项目提供；BIG `build`/`plan` 仍须每次在命令行传入 MOD 路径与版本号：

```powershell
./NewModCompiler.exe build --mod "C:\Mods\Example" --mod-version 2.8
```

命令行覆盖项目预设，预设覆盖默认值；同一来源的冲突参数会报错。项目中的 `mod-version` 键继续被识别，但仅有预设版本不足以满足 BIG 的必填要求，必须用 CLI 版本覆盖。SDK 与输出路径可保存到项目，JSON 中相对路径以 MOD 目录为基准；命令行路径仍以调用目录为基准。还可保存内容选择、BIG 设置、`jobs` 及既有高级缓存与旧脚本设置（完整键列表见 [参数兼容表](CLI_COMPATIBILITY.md)）。不扩充项目格式；不要将其他旧 BAB 参数写入 JSON。`profile` 仍可供已有项目兼容使用，规则见下文。

## 6. 高级选项

这些选项继续可用，直接与 `build` 使用，不要求任何 `--profile` 设置；`--help-advanced` 列出高级帮助。

### 游戏版本与中立修复

`--game-version` 默认 `1.12`，只控制 SKUDEF 的 `mod-game` 字段，不改变资产编译，也不代表已验证其他游戏版本的实际运行。

`--fix-neutral-assets=false` 为出厂默认值。启用后仅用于包含 base 的 BIG，依次执行 `HashFix → ModAssetResolver → LoD`，修正中立/地编资产身份，并递归补入 MOD 引用的 Worldbuilder 资产。修改在本轮打包私有副本中完成。缺少参考文件报告 `PACKAGE_NEUTRAL_REFERENCE_MISSING`。

在 MOD 目录的 `newmodcompiler.json` 中保存：

```json
{
  "package-format": "big",
  "fix-neutral-assets": true
}
```

也可以修改编译器旁的 `NewModCompiler.exe.config`，让全部 MOD 默认启用：

```xml
<appSettings>
  <add key="fix-neutral-assets" value="true"/>
</appSettings>
```

修改现有 `<configuration>` 内的 `appSettings`，保留其 `startup` 节点。优先级为命令行 > MOD 项目 JSON > EXE 配置 > false；例如 `--fix-neutral-assets=false` 可临时关闭配置中启用的修复。BIG 仍要求命令行显式传入 `--mod-version`。

启用后，缺少完整 Worldbuilder 参考流时按老 SDK 方式查询注册表游戏安装目录，自动读取 `Data/WBData_12.big`。
该文件由 SDK 地编安装到游戏目录，普通游戏不保证自带。提取结果进入 `SDK/SuperCompilerCache/shared/neutral-worldbuilder/`，同一SDK下的所有Mod共用一份，游戏和SDK输入保持只读；后续构建复用缓存。
首次使用时会迁入当前Mod的有效旧地编缓存；共享副本已存在时会清理当前Mod的同来源有效旧副本。其它Mod遗留副本在它们下次使用时处理，不扫描删除其它分区。
完整的已有参考四流或分块包继续兼容，日常使用无需另下载500MB参考包。

便携游戏或注册表未登记时，可在 CLI 使用 `--game-root C:\Games\RedAlert3`，或在 MOD JSON / EXE appSettings 中设置 `game-root`。
CLI > 项目 JSON > EXE 配置；JSON相对路径以MOD目录为准，EXE配置相对路径以编译器目录为准，EXE配置空值表示自动发现。
显式游戏路径优先于旧参考输入。目录和验证边界见 [中立地编修复说明](docs/NEUTRAL_WORLDBUILDER.md)。未启用时不读取或提取地编数据。

### 缓存与资源根

稳定排序 `/ss`、预编译引用策略 `/pc` 和慢清理 `/slowclean` 默认开启，与旧编译脚本一致，通常不必手动填写；确有需要可分别传入 `/ss:false`、`/pc:false`、`/slowclean:false`。这些选项不依赖缓存开关。

本分支恢复原 SDK 缓存默认值：会话 `/sc`、资产 `/bc`、部分会话 `/psc`、压缩 `/csc` 均默认 true；冻结 `/fsc` 和始终 touch `/atc` 默认 false。SDK 配置中的 `buildCacheRoot` 不再决定写入目录；`/bc` 未由命令行或项目预设显式指定时，默认值继续受 SDK `buildCache` 配置覆盖，命令行优先于项目预设。旧 defaultscript 显式使用 `/csc:false`，可用相同参数输出 XML：

```powershell
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" /csc:false --mod-version 1.0
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" /sc:false /bc:false --mod-version 1.0
```

`/sc` 按原文档、文件依赖、定义和继承复用解析与中间结果；`/bc` 按原类型/实例哈希复用资产，包括三种 Texture 路由。修改、新增、删除 MOD 输入均交由原 Core 判断，不再将整个 SDK/MOD 内容指纹作为冷失效开关。四流按原清单/流校验和原地复用；BIG 每次重新打包。

`/csc:false` 使用原 `BinaryAssetBuilder.SessionCache.xml`，true 使用 `.xml.deflate`。MOD缓存和编译工作文件放在传入 SDK 的 `SuperCompilerCache/<ModKey>/`：会话为 `session`，资产为 `build-cache`，中间资产为 `intermediate`，可复用四流为 `streams`。`/scd`（别名 `/scp`）、`/bcd`（别名 `/bcp`）、`/iod` 及项目预设中的对应值仅能指定当前分区内部目录，超界以 `CLI_CACHE_PATH` 拒绝。会话格式和会话根 `.NewModCompiler/cdata` 的恢复逻辑保留；旧 `--out` 下的缓存不读取、不迁移或自动删除。布局与路径边界见 [集中缓存说明](docs/SUPERCOMPILER_CACHE.md)。

同一个物理 MOD 更换 `--out` 或版本仍使用同一缓存分区；同名但路径不同的 MOD 各自隔离。切换原 DLL 与源码纹理实现时，原版本与 ProcessingHash 保持不变，既有 BC1 产物可能直接命中；需要冷缓存时，应在无构建运行时清理当前 MOD 分区。更换输出目录不再清空缓存。构建器不引入额外指纹、缓存代次或自动清理。

`/psc:true` 允许中止后保留已完成的会话工作，失败或取消仍返回失败/取消。`/psc:false` 不在失败流中保存部分会话；成功流的会话保存与日志/报告输出解耦。`/fsc:true` 读取旧会话但不更新会话文件，资产与中间文件仍按原 Core 行为处理。`/atc:true` 即使复用中间资产也更新对应网络资产缓存文件的时间。详细实现与验证状态见 [原 SDK 缓存恢复说明](docs/LEGACY_INCREMENTAL_CACHE.md)。

BIG/SKUDEF 包缓存已移除；每次构建都会从本轮编译结果重新打包，`/sc`、`/bc` 不会跳过打包。打包后直接替换同名最终文件，不做整包哈希或发布去重，也不回滚整个 MOD。

资源搜索 `/dr`、`/data`、`/art`、`/audio` 保留原根与顺序，包含配置的外部资源根。MOD 源码只读，位于 SDK 的 `Mods` 内也适用；`--out` 仍必须在 SDK/MOD 外。`plan`、`doctor` 不创建缓存目录。同一 MOD 的并发 build 以 `CACHE_BUSY` 拒绝，锁覆盖编译至 BIG 打包或 loose 导出；不同 MOD 的分区锁互不阻塞。

### 旧脚本兼容入口

`--profile` 仅为已有脚本和项目保留，不再作为操作模式推荐，也不控制高级参数的使用权限。`standard`、`advanced` 均保持 BIG 默认值；`development` 仅在项目预设和命令行均未显式设置 `package-format` 时提供 loose 默认值，选项来源仍为默认值。由它派生的 loose 允许保留包版本/名称供以后 BIG 打包使用；显式 `--package-format loose` 仍按原规则拒绝非默认包版本/名称。profile 不改变缓存设置。

`/od` 是 `--out` 别名，`maps` 是 `global-metadata` 别名，内部任务 ID/报告/输出目录仍使用 `maps`。`/tp:Win32`、`/ls:true` 和 `/el:0` 仅为固定行为兼容接受。其他固定值、未支持的旧能力及无效布尔值的处理见 [参数兼容表](CLI_COMPATIBILITY.md)；`plan` 和 `build` 均在进入执行前以参数错误退出码 2 拒绝。

## 7. 高级诊断、取消与自动化

`/tl:0..9` 设置诊断级别，默认 3。默认诊断文件均在输出根：`build.log`、`build.events.jsonl`、`build-report.json`。可以使用 `--log <文件>`、`--events <文件>`、`--report <文件>` 改位置，但不得覆盖输入、published、工作区或彼此冲突。当前不支持用 `--events -` 向标准输出发送事件。

自动化应同时核对退出码、最终报告的 `status/exitCode` 和本次 `buildId`。JSONL 按 `sequence` 排序，以 `BuildFinished` 为正常终态；不要把上次文件或仅“进程消失”当作构建成功。协议细节见 [协议文档](protocol/README.md)。

| 退出码 | 含义 |
| --- | --- |
| 0 | 成功；是否产生新产物还要看报告的 publicationStatus |
| 2 | 参数或计划错误 |
| 3 | 环境或必需依赖缺失 |
| 4 | 输入、编译或打包失败 |
| 5 | 内部异常 |
| 6 | 合作式取消 |
| 7 | 必需日志、报告或输出提交失败 |

可用 Ctrl+C/Ctrl+Break 请求合作式取消；原生调用可能需要返回安全边界后才响应。强制终止或崩溃可能没有终态，调用方应识别为异常中断。工具不会自动启动游戏、安装 MOD 或写 CurrentMOD.ini。

## 8. 常见问题与已知范围

| 诊断 / 现象 | 处理 |
| --- | --- |
| `PLAN_BASE_MISSING` | 补齐 Data/Mod.xml，或只选择实际存在的 apt/maps 内容 |
| `CLI_OUTPUT_OVERLAP` | 把 --out 改到 SDK/MOD 外的独立目录 |
| `PACKAGE_IDENTITY_FORMAT` | 包版本/名称选项必须与 --package-format big 一起使用 |
| `PACKAGE_IDENTITY_INVALID` | 检查版本或文件基名是否含目录、换行或非法字符 |
| `PACKAGE_NAME_UNSUPPORTED` | 将 MOD 文件夹基名改为 ASCII 后使用 BIG |
| `CLI_UNSUPPORTED` | 按 CLI 兼容表移除未支持的旧选项；plan/build 均在执行前拒绝 |
| doctor 失败 | 保留标准错误诊断，检查 SDK 路径、.NET/VC 运行库和包内三个运行文件是否完整；历史五文件包另需原 Texture 依赖 |

LoD 明确修复原工具的表尾越界：完整保留逻辑名称/来源表，不复制越界产生的尾字节；详见 [兼容策略](P7_LOD_ORIGINAL_TAIL.md)。完整 UI/runtime、中文纹理路径、混合纹理会话缓存仍有未覆盖范围。AssetMerge 不属于新流程。

## 性能耗时分析

默认关闭，使用 `--performance summary` 查看阶段耗时，`--performance detailed` 查看内部文档/资产/哈希耗时；`--performance off` 关闭。结果聚合到构建报告的 `performance.entries`，不自动循环构建。字段口径、示例和短测试预算见 [性能打点](docs/PERFORMANCE.md)。
