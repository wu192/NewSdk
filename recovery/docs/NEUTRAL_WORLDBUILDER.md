# 当前：同SDK共用地编参考缓存

共享目录：`SDK/SuperCompilerCache/shared/neutral-worldbuilder/`，不再按Mod复制约1.33GB。
共享锁覆盖提取与消费，两个Mod并发首次使用也只保存一份；不同来源切换不会混代，等待可取消。
当前Mod的有效旧cache直接迁入；共享已有效时清理当前Mod同来源有效旧副本，其它Mod在各自下次使用时处理。
普通Mod编译缓存仍按ModKey隔离。

生产 `57c0c783`，[Windows41324686715](https://github.com/wu162/NewSdk/actions/runs/41324686715)全通过：D/R各1901项App检查，
原9个游戏布局用例、第二Mod共享热命中不改mtime、两个Mod并发冷提取仅1份payload、原SDK整包字节与独立ZIP共享冷编译。
Release EXE=5024256字节，三运行文件ZIP约1.61MB。未重跑完整Core/CLI大矩阵、完整大型Mod或游戏内验证。
[验收收据](../evidence/shared-worldbuilder/41324686715-1/summary.json)、
[发行](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.02-shared-worldbuilder-cache)、
[发布收据](../evidence/releases/compiler-2026.11.02-shared-worldbuilder-cache/published.json)。

以下为自动发现游戏Data的前版逻辑与历史验收，其中旧的Mod专属地编路径已由以上共享目录替代。

---

# 当前：自动使用游戏目录的 SDK 地编

启用 `fix-neutral-assets` 后，缺少完整参考流时按老 SDK 规则查询 HKLM 的
`SOFTWARE\Electronic Arts\Electronic Arts\Red Alert 3` 的 `Install Dir`，覆盖32/64位视图，
读取 `<游戏目录>/Data/WBData_12.big`。该文件由SDK地编安装到游戏目录，普通游戏不保证自带。

优先级为显式 `game-root` > 完整已有 SDK 四流/分块包 > 注册表游戏路径 > 当前目录的 `Data/WBData_12.big`。
用户已有数据继续兼容；自动读取游戏数据时无需另外下载500MB参考包，也不把大资产内嵌到EXE。

便携游戏可使用 `--game-root C:\Games\RedAlert3`，或在 MOD `newmodcompiler.json` 中配置：

```json
{
  "fix-neutral-assets": true,
  "game-root": "C:/Games/RedAlert3"
}
```

EXE 配置的 `appSettings` 也支持 `game-root`，出厂空值表示自动发现；CLI > MOD JSON > EXE配置。
JSON相对路径以MOD目录为基准，EXE配置相对路径以编译器目录为基准。
BIG仍要求CLI显式 `--mod-version`，开关仍仅用于包含base的BIG。

首次使用会流式提取四流到 `SDK/SuperCompilerCache/<ModKey>/neutral-worldbuilder/`，
不修改游戏/SDK输入；后续按来源路径、长度、mtime复用内部缓存，不反复哈希或搬运1.3GB。
文件变化或缓存缺件时重提取；失败/取消清理半成品。普通SDK的Static/Global/Audio参考仍需存在。
HashFix优先使用SDK原manifest，缺失时使用同一游戏BIG提取的manifest。
原HashFix→Resolver→LoD算法与BIG输出合同保持原行为。

新版本已发布：[compiler-2026.11.01-game-worldbuilder](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01-game-worldbuilder)。
生产源码 `3cf65b6d`，[Windows 41322569300](https://github.com/wu162/NewSdk/actions/runs/41322569300) 全success：
D/R各1894项App检查、9个真实小MOD用例及独立ZIP冷提取通过，注册表32/64视图均实际测试并恢复；
缓存热命中不改mtime，缺BIG失败后恢复可重试，游戏/SDK/MOD输入不变。
实际修正1个哈希、补5资产，全部12个BIG成员及整包与原SDK链原字节一致。
Release EXE=5022720字节，只增加10752字节，不内嵌资产。
本轮按用户要求快速定向验证，没有重新执行完整Core/CLI大矩阵、完整大型MOD或游戏内验证。
[验收收据](../evidence/game-worldbuilder/41322569300-1/summary.json)、[发布收据](../evidence/releases/compiler-2026.11.01-game-worldbuilder/published.json)。
以下为上一版参考数据来源和验收历史。

---

# 中立与 Worldbuilder 资产修复

本分支从用户指定的 `recovery/legacy-sdk-cache` / `680c4f0c` 开始，带入
`release/compiler-2026.11.01-harness` / `feadec6e` 的修改；不会修改该发布分支。

## 使用

下载 [中立地编修复版 Release](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01-neutral)。
通常选择 `NewModCompiler-Win32-Release.zip`；将参考附件 `RA3-Worldbuilder-1.12-References.zip`
解压到现有 SDK 根目录。编译器 ZIP 保持三个运行文件，参考数据另包提供。

BIG 包含 base 时使用 `--fix-neutral-assets=true`，或在 MOD 的
`newmodcompiler.json` 中加入 `"fix-neutral-assets": true`。
也可修改 `NewModCompiler.exe.config` 已有的 `appSettings`：

```xml
<add key="fix-neutral-assets" value="true"/>
```

优先级：CLI > MOD 项目 JSON > EXE 配置 > false。报告中的来源分别为
`commandLine`、`project`、`applicationConfig`、`default`。出厂 EXE 配置为 false。
只需配置一个位置；CLI `--fix-neutral-assets=false` 可临时关闭。
BIG 仍要求每次在 CLI 指定 `--mod-version`。

```powershell
./NewModCompiler.exe build --sdk-root C:\NewSdk\SDK --mod C:\Mods\Example --out C:\Build\Example --mod-version 1.0 --fix-neutral-assets=true
```

修复顺序为 HashFix、递归 ModAssetResolver、LoD，然后 MakeBig。操作仅改变打包私有副本；
编译缓存、MOD 源码及官方参考输入不被修改。未开启时沿用原来的打包路径。
依赖参考丢失时使用 `PACKAGE_NEUTRAL_REFERENCE_MISSING`。

## 官方参考文件

来源：[CNCNZ WorldBuilder Mod SDK 1.12](https://files.cncnz.com/red_alert_3/modding/CCRA3WorldBuilder_ModSDKv112.zip)。
包内 InstallShield 安装程序的 `WBData_12.big` 包含完整四流，15931 个资产。
官方 manifest 与仓库原有 `SDK/builtmods/sagexml/Worldbuilder.manifest` 完全相同。
压缩只改变存储方式，不删资产、重写资产表或替换原始 payload。

文件位于 `SDK/builtmods/sagexml/`：

- `Worldbuilder.manifest`：官方原始资产表，SHA256 `1f621187352425571d72a0761fa56ca11cba525b12220cca27c94345080eeedd`。
- `Worldbuilder.imp`、`Worldbuilder.relo`：官方原始字节。
- `Worldbuilder.bin.parts/index.json` 与156个 `block-*.deflate`：完整 `.bin` 的8 MiB独立 raw Deflate 块，共498047426字节。
- `Worldbuilder.provenance.json`：ZIP、安装程序、BIG和原四流的长度与SHA256，以及完整还原核验。

原始 `.bin` 长1308421704字节，超过 GitHub 普通文件限制。块读取保持原始偏移，
只解压当前资产涉及的块，缓存当前块；无需先展开完整1.3GB。
索引的块SHA256对应解压后的字节。运行热路径不重复哈希；导入和完整还原负责校验。
完整还原SHA256：`682b7c654c589a61e90eae565860497e5847e02bcee4ab303d6161ecc7280e36`。

SDK原有 `builtmods/Static.manifest`、`Global.manifest`、`Audio.manifest` 仍需存在。
用户已有 `builtmods/Worldbuilder.*` 完整原始四流时优先采用该组；否则采用 sagexml 组。
同一组的普通 `.bin` 优先于 `.bin.parts`，不跨目录混合不完整输入。

从解出的官方BIG重新导入：

```powershell
python recovery/scripts/ImportWorldbuilderReferences.py import --big C:\Extracted\WBData_12.big --output C:\CleanSDK\builtmods\sagexml
```

运行新编译器不需要展开。仅原版工具对照或第三方工具需要原始 `.bin` 时：

```powershell
python recovery/scripts/ImportWorldbuilderReferences.py unpack --parts SDK/builtmods/sagexml/Worldbuilder.bin.parts --output-bin C:\OracleSDK\BuiltMods\Worldbuilder.bin
```

## 验证范围

`recovery/fixtures/neutral-worldbuilder` 是两个 GameObject 的小型 MOD：覆盖 `AM_Box01`
触发真实官方身份修正，另一个对象通过 `GatherSlavesUpdate.SlaveTemplate` 强引用
Worldbuilder 专属 `AM_Bench`，触发模型及三张纹理递归补入。没有人为改参考哈希或伪造 payload。

`neutral-worldbuilder.yml` 构建 Debug/Release，执行 App、真实 Core/CLI及原版
HashFix/Resolver组件差分与确定性 LoD 合同；随后验证全局配置、项目配置和CLI覆盖、关闭开关、
缺参考失败与恢复重试，比较官方原工具 HashFix→Resolver→LoD→MakeBig 的全部BIG成员原始字节。
[Windows 41319194413](https://github.com/wu162/NewSdk/actions/runs/41319194413)
整体通过，生产与测试源码均为 `f74da90fd8461fcb983acebcb694d66aeb8dbfdd`。
Debug/Release 各1879项 App 合同、10组原版修复组件、12个真实 Core fixture、31个CLI用例通过。
每种配置8个小MOD用例：实际修正1项哈希，递归补入5个官方资产，BIG的全部12个成员原始字节
与原版完整处理链一致。两个三文件ZIP均独立解压后完成冷编译，BIG/SKUDEF与生产结果相同。
归档和运行文件身份见 [验收收据](../evidence/neutral-worldbuilder/41319194413-1/summary.json)。
[发布运行41320643988](https://github.com/wu162/NewSdk/actions/runs/41320643988) 已成功，7个附件的API大小与SHA256均与本地一致，
见 [发布收据](../evidence/releases/compiler-2026.11.01-neutral/published.json)。发布复用已验收二进制，没有重新构建。
本轮不代表完整大型MOD或游戏内验收。

历史联合合成 LoD 的5个单资产样例仍保留原字节失败：原工具越界将来源表末尾 NUL
写成01，新编译器按已确定合同保留00，见 [原始诊断与修复合同](../P7_LOD_ORIGINAL_TAIL.md)。
原默认联合测试及断言未删；本轮相关组件使用 `--hashfix-resolver-only`，对全部10个样例
的两个修改相关阶段逐字节比较。LoD 的完整逻辑表复制由 Debug/Release App 合同校验，
真实官方小MOD及独立ZIP部署继续要求原版 LoD/MakeBig 输出全部成员字节相同。
