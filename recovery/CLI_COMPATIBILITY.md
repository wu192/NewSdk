# 当前命令行与旧参数范围

适用分支：`recovery/legacy-sdk-cache`。普通入口、仍可直接使用的高级能力、旧脚本兼容值及明确拒绝项如下；隐藏参数不代表删除功能。`plan` 与 `build` 共用参数能力检查，不支持的选项或值在执行前以参数错误退出码 2 返回，不能静默忽略。操作示例见 [使用说明](COMPILER_USAGE.md)。

## 普通入口

保留 `build`、`plan`、`doctor`、`--help`、`-h` 和 `--help-advanced`。`doctor` 仍只要求 SDK 路径，不附加 MOD、输出目录或显式 MOD 版本要求。普通帮助只展示以下 10 个参数：

| 参数 | 默认值 / 用途 |
| --- | --- |
| `--mod <目录>` | 命令行必需，项目预设不能代替 MOD 位置 |
| `--sdk-root <目录>` | SDK 路径，可保存到项目预设 |
| `--out <目录>` | 与输入分离的输出路径，可保存到项目预设 |
| `--package-content` | `auto`，或 `base,apt,global-metadata` 的组合 |
| `--package-format` | `big`，可选 `loose` |
| `--include-additional` | BIG 默认 true，loose 默认 false |
| `--mod-version` | 最终格式为 BIG 的 build/plan 每次必须在 CLI 显式指定；项目预设不能代替 |
| `--skudef-name` | MOD 目录名 |
| `--jobs` | `auto` 或正整数，默认 `auto` |
| `--performance` | `off\|summary\|detailed`，默认 `off` |

默认自动发现 base/APT/global-metadata，输出 BIG + SKUDEF 并包含 Additional。Win32 与 LinkedStreams 固定，正常编译无需显式填写平台、格式或缓存参数。最终格式为 BIG 的 `build`/`plan` 每次必须在命令行显式传入 `--mod-version`，项目预设与旧默认值不能代替。loose（包括 development 派生的 loose）不要求显式版本，显式 loose 的原包身份校验继续保留。布尔值接受 `--include-additional=false`、旧式 `/pc:false` 或裸开关启用；不接受空格分隔的布尔值。

## 高级能力

以下功能保留，由 `--help-advanced` 展示，可直接用于 `build`，无需 `--profile advanced`。

| 用途 | 参数与默认行为 |
| --- | --- |
| 中立修复 | `--fix-neutral-assets=false`；true 仅限含 base 的 BIG，执行 `HashFix → ModAssetResolver → LoD`；可在 MOD JSON 或 EXE appSettings 保存。缺少完整参考流时自动从注册表中的游戏安装目录 `Data/WBData_12.big` 提取到 MOD 专属编译缓存；完整旧参考仍兼容；缺少文件报 `PACKAGE_NEUTRAL_REFERENCE_MISSING` |
| 游戏安装目录 | `--game-root <目录>`，也可保存到 MOD JSON 或 EXE appSettings；显式设置时优先从该目录的 `Data/WBData_12.big` 读取。未设置时自动发现注册表路径。CLI > 项目 > EXE 配置；EXE中的空值表示自动 |
| 游戏版本字段 | `--game-version=1.12`，只控制 SKUDEF 的 `mod-game` 字段 |
| 缓存开关 | `/sc:true`、`/bc:true`、`/csc:true`、`/psc:true`、`/fsc:false`、`/atc:false`；未显式指定 `/bc` 时仍受 SDK `buildCache` 配置覆盖 |
| 缓存目录 | `/scd`（别名 `/scp`）、`/bcd`（别名 `/bcp`）、`/iod`，仅允许当前 `SuperCompilerCache/<ModKey>` 分区内部；原 BAB 格式保留 |
| 旧脚本真实行为 | `/ss:true`、`/pc:true`、`/slowclean:true`，均可显式 false，独立于缓存开关 |
| 资源搜索根 | `/dr`、`/data`、`/art`、`/audio`，保留原根与搜索顺序 |
| 诊断 | `/tl:0..9`，默认 3；`--log`、`--events`、`--report`，默认位于输出根 |

`/pc:true` 使用原 Core 的引用策略：优先尝试预编译引用，缺失时沿原逻辑处理引用而不额外发布它们。它不代表自动导入游戏数据或 WBData；不存在的参考 manifest 不会凭空生成。`/pc:false` 保留编译并输出引用流的旧行为；含多个输出流的 BIG 仍受既有打包合同约束。

三种 Texture 路由继续支持原资产与会话缓存。缓存统一写入传入 SDK 的 [MOD 专属分区](docs/SUPERCOMPILER_CACHE.md)，不采用 SDK `buildCacheRoot` 或旧输出目录缓存。BIG/SKUDEF 每次正常打包；本次参数整理不改变 Core 编译、缓存、调度、四流、包名或 Additional 覆盖顺序。

## 仅供旧脚本兼容

这些入口继续接受，不再作为普通操作模式或新脚本用法推荐。

| 参数 / 值 | 处理 |
| --- | --- |
| `--profile standard` / `advanced` | 均保持 BIG 默认行为；不设置高级功能门槛 |
| `--profile development` | 仅在项目及 CLI 均未显式设置 `package-format` 时提供 loose 默认值；保留默认值来源 |
| `/od` | `--out` 的别名 |
| `maps` | `global-metadata` 的别名；内部任务 ID/报告/输出目录仍为 `maps` |
| `/tp:Win32`、`/ls:true` | 接受固定兼容值，内部仍固定 Win32/LinkedStreams；其他值拒绝 |
| `/el:0` | 接受；`/el:1` 在 CLI 阶段拒绝 |
| `/oof:false`、`/vf:false`、`/oar:false`、`/osh:false`、`/oix:false`、`/mr:false` | 接受既有无效 false 值；true 明确拒绝，不新增对应功能 |

包版本/名称只用于 BIG，显式 `--package-format loose` 时仍拒绝非默认包身份设置。`development` 自动派生 loose 时允许保留项目预存的版本/名称及 CLI 版本覆盖供以后 BIG 打包使用，不将它们写入流，也不将派生 loose 改记为显式设置。显式格式始终优先于 profile 默认值。

## 明确拒绝

`/gui`、`/res`、`/pers`、`/mp`、`/poe`、`/sf`、`/sh` 维持拒绝。`/sp`、`/bcn`、`/bps`、`/cpf` 不属于可用选项：自定义 Schema、BuildConfiguration、基础补丁流及自定义后缀均在 CLI 阶段以退出码 2 拒绝，`plan` 与 `build` 一致。未知选项、非固定平台/链接值、`/el:1` 及上述无效布尔开关的 true 同样拒绝。

## 项目预设与包身份

`newmodcompiler.json` 既有键继续有效，完整列表为：`jobs`、`profile`、`sdk-root`、`game-root`、`out`、`package-content`、`package-format`、`include-additional`、`fix-neutral-assets`、`mod-version`、`game-version`、`skudef-name`、`tp`、`ss`、`pc`、`slowclean`、`sc`、`bc`、`csc`、`psc`、`fsc`、`atc`、`session-cache-directory`（`scd`/`scp` 别名）、`build-cache-directory`（`bcd`/`bcp` 别名）和 `iod`。`od` 仍为 `out` 的别名。

优先级保持命令行 ＞ 项目预设 ＞ 默认值，同一来源同一参数指定冲突值时报错。`mod-version` 项目键继续被识别，但 BIG 的必填要求只能由本次 CLI 显式版本满足，须用命令行覆盖项目版本。JSON 相对路径以 MOD 目录为基准，命令行路径以调用目录为基准；生效值及来源进入报告与事件。SDK 的 `buildCache` 仅覆盖未显式指定的 `/bc` 默认值。

```json
{"package-content":"base,apt,global-metadata","mod-version":"2.7-beta","skudef-name":"ExampleLauncher"}
```

使用上例项目预设并在 BIG `build`/`plan` 命令行显式传入 `--mod-version 2.7-beta`，MOD 目录名为 Example 时发布 `Example_2.7-beta.big` 和 `ExampleLauncher_2.7-beta.skudef`；SKUDEF 精确包含 ASCII/CRLF 的 `mod-game 1.12` 与 `add-big Example_2.7-beta.big`。MOD 版本允许 ASCII 字母、数字、点、下划线及连字符；游戏版本为点分十进制；SKUDEF 名称为单个 ASCII 文件基名。均不能注入目录或换行指令。APT 内部 `.version` 仍为原脚本的空格+CRLF，与 MOD 包版本不同。
