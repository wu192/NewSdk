# 本分支：百合子魅惑模块移植

基于 `compiler-main/f0adccd6`，配套 EnhancerCorona `feature/yuriko-domination`。生产编译支持、原生输入 token、公开字段和限制见 [本轮说明](docs/YURIKO_DOMINATION.md)。本地实际 32 位组件 LE/BE 与八组旧资产三缓冲兼容检查通过；Win32 Debug/Release、无 TypeId 注入的 XSD 路由以本分支 Actions 最终状态为准。未新增真实 SDK 四流或游戏内验收。

以下历史入口保留。

# 当前：Patch / FX / 配置版本已通过定向验证，PR #11 待审

FX 随包工具补充已通过：生产源码 `fd32a0a5`，[Windows 42341470222](https://github.com/wu192/NewSdk/actions/runs/42341470222) 全部 success。新包加入 Windows SDK 10.0.26100.0 的 x86 `fxc.exe` 与 `d3dcompiler_47.dll`，默认使用 EXE 同目录工具。D/R 五文件 ZIP 独立解压，在 PATH 为空、SDK 无 FXC 且不传 --fxc 时真实编译成功，自动工具路径及 FXO 入 BIG 均确认；原15成员 patch/LoD、冷热/基准修改/损坏输入也通过。16份报告、438个事件协议复核通过。[下载新 D/R ZIP](https://github.com/wu192/NewSdk/actions/runs/42341470222/artifacts/13562280288)，[收据](evidence/patch-fx-bundle/42341470222-1/summary.json)。历史归档和固定旧发布脚本保留原布局；以下 `64d2aeb3` 是补充前的验收来源。

[PR #11](https://github.com/wu192/NewSdk/pull/11)，分支 `feat/patch-fx-config-version`。
新增 base-patch-stream（兼容 /bps）、独立 patch-version / Patch WB 无后缀布局、
LoD/.version 与可选 SKUDEF add-config；合并 Additional 后编译 FX，处理 include 覆盖与失败/取消。
BIG mod-version 支持 MOD JSON 和 EXE appSettings，CLI 优先。用法见 [使用说明](COMPILER_USAGE.md#patch-与-fx)。
生产源码 `64d2aeb3`，[Windows App 42338796635](https://github.com/wu192/NewSdk/actions/runs/42338796635) 的 2012 项合同通过。
[D/R 真实回放 42339277759](https://github.com/wu192/NewSdk/actions/runs/42339277759) 复用本轮同源二进制：
全部15个原 BAB/LoD/版本成员逐字节一致；普通/patch/热态、同路径基准修改、损坏基准拒绝及真实 fxc 编译均通过。
16份报告、438个事件（含 FX 工具记录及配置来源）通过原协议校验。[简明收据](evidence/patch-fx/42339277759-1/summary.json)。
重复基准磁盘检查已收敛到 CLI 输入入口；删除错误包装、无 FX 时的字典及无版本 LoD 快照。
完整原生回归 [42338796649](https://github.com/wu192/NewSdk/actions/runs/42338796649) 的其余独立门槛均通过：Core/CLI/取消、性能/热缓存、FLA/Texture、资产/会话缓存、BIG/原 MakeBig 及完整 mymod 原 SDK 对照。旧对照控制器的路径差异失败由上述修正回放独立覆盖；原失败保留，断言未放宽。该运行的正式 ZIP 发布和大型 Upheaval 性能步骤跳过；提供本轮已核对并完成真实回放的 D/R 运行文件。
未合并、未发布 Release；本轮不新增完整大型 MOD 性能或游戏内结论。

# 此前：compiler-main CI 五工作流通过，PR #1 待审

[PR #1](https://github.com/wu192/NewSdk/pull/1)，`fix/compiler-main-ci` 基于 `72aa8f0c`。
验证分支头 `12b0cd99`，PR 检出源码 `1ac655d5`：
[HashFix](https://github.com/wu192/NewSdk/actions/runs/41818840198)、
[LoD](https://github.com/wu192/NewSdk/actions/runs/41818839929)、
[MakeBig](https://github.com/wu192/NewSdk/actions/runs/41818840222)、
[XML guards](https://github.com/wu192/NewSdk/actions/runs/41818839919)、
[基础后处理](https://github.com/wu192/NewSdk/actions/runs/41818840241) 均 success。

补齐五份稀疏工作流的汇总脚本检出；MakeBig 宿主引用现有 BuildApp 完整依赖构建；
XML 测试同步 300 路由/64 分组，并为当前五项增加 PR 自动检查。
D/R HashFix 各9、LoD 各17、MakeBig 各15用例通过；XML 原有拒绝/传输检查通过。
联合后处理十组按[已定 canonical 合同](P7_LOD_ORIGINAL_TAIL.md)通过，五组已知末尾
`01/00` 差异及原始 raw 失败收据保留；原始断言未改。精简归档已下载复核。
本地六个 canonical 接受/拒绝用例、actionlint 和二十段工作流 PowerShell 语法检查通过。
生产源码未改，无全量 SDK/大型 MOD 新结论；后续文档提交不改变上述验证输入。
未合并，禁止强推，合入仍须获用户批准。

# 此前：BIG 超过 2 GiB 内存修复已验证并打包，PR #50 待审

基于用户指定的 `fix/neutral-worldbuilder-config` / `9a1c93a1`，修复中立资源处理
整文件读取/多份复制 MOD 载荷导致的 2 GiB 限制及 x86 内存溢出，改为流式插入和复制。
生产/测试 `788566c0` 的 [Windows x86 41433492176](https://github.com/wu162/NewSdk/actions/runs/41433492176)
整体 success：Debug/Release App、超过 2 GiB 的实际 resolver→BIG 和原工具字节差分通过。
[范围与验收](docs/BIG_LARGE_FILES.md)、[PR #50](https://github.com/wu162/NewSdk/pull/50)。
按用户要求提供[修复版 D/R 运行包](https://github.com/wu162/NewSdk/actions/runs/41435748800/artifacts/13137145304)：
原 `41434055130` 新构建/Core 成功，保留后续历史 UI blob 缺失失败；完整检出重放
`41435748800` 的真实 CLI、小 MOD 原工具 BIG 及独立 ZIP 验证整体 success。
归档/三个运行文件/修复源码身份已核对。未运行大型 MOD/全量 SDK或游戏验证；未发布 GitHub Release、未合并，禁止强推。

# 当前：地编缓存改为SDK共享，已快测发布

分支fix/neutral-worldbuilder-config；生产57c0c783。
游戏地编四流位于SDK/SuperCompilerCache/shared/neutral-worldbuilder，全SDK所有Mod复用一份。
共享锁覆盖提取至消费，支持取消；当前Mod有效旧cache移动迁入或清理冗余，其它Mod在下次使用时处理。
普通Mod缓存边界保持分区隔离。
[Windows41324686715](https://github.com/wu162/NewSdk/actions/runs/41324686715)全success：D/R新构建、App各1901、
原9个游戏用例、第二Mod热复用、双Mod并发冷提取仅1份payload，原SDK整个BIG与独立ZIP共享冷编译通过。
[compiler-2026.11.02-shared-worldbuilder-cache](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.02-shared-worldbuilder-cache)
已公开/latest；发布41325895676复用同一验收二进制，6附件大小/SHA全核对，一次性工作流已移除。
[测试收据](evidence/shared-worldbuilder/41324686715-1/summary.json)、[发布收据](evidence/releases/compiler-2026.11.02-shared-worldbuilder-cache/published.json)。
未做完整Core/CLI大矩阵、完整大Mod或游戏内新验证；没有合入其它开发分支，禁止强推。

# 当前：按老SDK自动读取游戏地编，已快测发布

分支 `fix/neutral-worldbuilder-config`；生产源码 `3cf65b6d`。
开启fix-neutral-assets后，缺完整旧参考时按HKLM EA Install Dir的32/64位视图自动找游戏Data/WBData_12.big；
可用CLI/JSON/EXE的game-root指定。首次流式提取到Mod专属SuperCompilerCache/neutral-worldbuilder，后续按来源元数据复用。
游戏/SDK/MOD输入只读，保留旧完整四流/pack兼容，不把资产内嵌EXE，不再附500MB参考包。
[Windows41322569300](https://github.com/wu162/NewSdk/actions/runs/41322569300)全success：D/R新构建，各App1894、
9个游戏布局小MOD用例（两注册表视图恢复、配置、冷热提取、缺失重试）、原完整处理链BIG字节及独立ZIP冷编译通过。
[发行compiler-2026.11.01-game-worldbuilder](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01-game-worldbuilder)
已公开并为latest；[发布41323676291](https://github.com/wu162/NewSdk/actions/runs/41323676291)复用已验收二进制，6附件大小/摘要全核对。
Release EXE5022720B，仅增10.5KiB；未重跑完整Core/CLI大矩阵、完整大型MOD或游戏内验证。
[逻辑与边界](docs/NEUTRAL_WORLDBUILDER.md)、[测试收据](evidence/game-worldbuilder/41322569300-1/summary.json)、
[发行收据](evidence/releases/compiler-2026.11.01-game-worldbuilder/published.json)。一次性发布工作流已移除，原草稿查询失败记录保留。
未合入其他开发分支；禁止强推，后续合入仍须用户批准。

# 当前：官方中立地编修复与配置已验收

分支 `fix/neutral-worldbuilder-config`，从用户指定的 `recovery/legacy-sdk-cache` / `680c4f0c` 开始，
已按用户纠正把 `release/compiler-2026.11.01-harness` / `feadec6e` 的修改带入本分支；未修改该发布分支。
完整官方15931资产四流已入库，1.3GB bin以156个独立Deflate块保存，按引用随机读取。
MOD JSON和EXE appSettings均可设置fix-neutral-assets，CLI优先。
生产/测试 `f74da90f`，[Windows 41319194413](https://github.com/wu162/NewSdk/actions/runs/41319194413) 全success：
D/R新构建、App各1879、10组原修复差分、12个Core fixture、31个CLI用例及各8次真实小MOD通过；
实际改1哈希/补5资产，12个BIG成员与原HashFix→Resolver→LoD→MakeBig原字节相同。
两个三文件ZIP独立解压冷编译通过；未完整大型MOD/游戏验收，旧LoD合成尾字节差异按原确定合同保留。
[说明与边界](docs/NEUTRAL_WORLDBUILDER.md)、[收据](evidence/neutral-worldbuilder/41319194413-1/summary.json)。
已公开发布 [compiler-2026.11.01-neutral](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01-neutral)，
[发布运行41320643988](https://github.com/wu162/NewSdk/actions/runs/41320643988) 成功；两个三文件ZIP、完整官方参考ZIP及说明/清单共7附件已核对API大小与SHA256，已设latest。
沿用独立只含README/metadata的源码归档标签，复用已验收二进制；一次性发布工作流已移除。
禁止强推，未获另行批准不把本分支合入其他开发分支。

# 当前：Joypad 并行版已发布（PR #49 未合并）

11 个 Joypad 根已显式认证到 worker，metadata 路由 284→295；原 TypeId/版本/字节布局与缓存身份不变。
生产 `a75840ef` 的 D/R 只构建一次；[最终 41300821811](https://github.com/wu162/NewSdk/actions/runs/41300821811) success，严格复用已通过的组件/CLI 范围，45 资产小 MOD、实际 worker 重叠、串并行/缓存/原 SDK 四流与独立三文件 ZIP 均通过。
按用户要求未运行大型 MOD 或完整 mymod/全量 SDK 编译，没有新性能或游戏内结论。
用户已明确批准发布：[compiler-2026.11.01](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01) 已公开并设为 Latest，两个 ZIP 与确认包逐字节相同，七附件和自动文档归档均复核通过；[发行收据](evidence/releases/compiler-2026.11.01/published.json)、[范围与来源](docs/components/xml/XML_JOYPAD.md)。源码和修复仍在 [PR #49](https://github.com/wu162/NewSdk/pull/49)，未合并；禁止强推，后续合入仍须用户明确批准。

# 此前：发行验收误报已修复，待确认包（PR #49）

基线 `recovery/legacy-sdk-cache` / `680c4f0c`，仅修复验收脚本的显式 BIG 版本、集中缓存冷态隔离、SDK 缓存输出审计和 Joypad 主线程集合预期。生产源码及构建输入不变。
[最终 Windows 41297316677](https://github.com/wu162/NewSdk/actions/runs/41297316677) success：复用本次 D/R 构建及独立成功组，完整 mymod 原 SDK、独立三文件 ZIP 与完整 Upheaval 22 样本通过，原失败终态保留。
用户要求修复后先打包，明确确认后才能发布；本轮没有创建 Release 或发行标签。[待确认运行包](https://github.com/wu162/NewSdk/actions/runs/41298119902/artifacts/13079722889) 已通过纯运行文件及校验检查。[说明与来源](docs/RELEASE_VALIDATION_FIXES.md)、[PR #49](https://github.com/wu162/NewSdk/pull/49)（未合并）。禁止强推，合入仍须用户明确批准。

# 此前：CLI 单行编译进度已验收（PR #47）

分支 `feat/cli-build-progress`，基线 `recovery/legacy-sdk-cache` / `52d44c6`。
默认单行阶段/数据流进度、真实资产/XML/文档复用计数、警告/错误协调和重定向降级已接通；
缓存、调度、算法及 JSONL Progress 的数据流语义保持原合同。
源码 `6a69c0e8` / [Windows 41281916951](https://github.com/wu162/NewSdk/actions/runs/41281916951)
全部 success：Release、App/真实 Core/应用流水线、CtrlBreak/失败/重试、冷/热计数及原字节通过。
同机完整历史 Upheaval 的 base loose 热缓存真实控制台三对：开启中位 2754.3291ms、
关闭 2766.9984ms，开销 -0.4579%，达到本轮 <=1% 目标；不作为普遍提速结论。
1859 条警告的逐条重画已合并为每秒恢复；首轮 25.8046% 超标及早期失败保留。
最终归档单次下载后复核 ZIP/三运行文件/同对象宿主身份；未测 Debug、全 APT/maps、原 SDK 或游戏验收。
入口：[实现与验证范围](docs/CLI_PROGRESS.md)、[收据](evidence/cli-progress/41281916951-1/summary.json)。
[PR #47](https://github.com/wu162/NewSdk/pull/47) 待审，未合并；禁止强推，合入仍需用户明确批准。

# 此前：SuperCompilerCache 集中缓存改造完成（PR #46）

分支 `feature/supercompiler-cache`，基线 `recovery/legacy-sdk-cache` / `5c0c993f`；
[PR #46](https://github.com/wu162/NewSdk/pull/46) 未合并。所有内部缓存、中间资产、可复用四流、
FLA 合成声明及打包临时输入位于本次 SDK 的 MOD 专属分区；MOD 源码只读、完整 build 锁、
分区内缓存覆盖、BIG/loose 正式交付已接通。BIG 仍要求 CLI 显式 `--mod-version`。

生产 `f8e8f4a0` 已成功 Release 构建；[最终 Windows 核验 41275643703](https://github.com/wu162/NewSdk/actions/runs/41275643703)
严格验证输入闭包和运行文件后复用该 EXE，没有再次编译。mymod 与完整历史 Upheaval 的 base loose 冷/热、
热态 BIG，以及小型直接 FLA 冷/热共 8 次 build 通过；热态全部 0 解析/0 资产编译。
三个 MOD 写事件为 0，SDK 输入 7 个监听边界无越区事件/溢出，字节、mtime、属性快照零差异。
[App 41275643739](https://github.com/wu162/NewSdk/actions/runs/41275643739) 的 1846 项合同与全部 PS 语法预检通过；
相关 73 项 Python 小测试通过。未运行原 SDK 全字节差分、完整 APT/maps/缓存开关大矩阵或游戏验收。

[布局与验证边界](docs/SUPERCOMPILER_CACHE.md)、[收据及保留的失败记录](evidence/refactor/supercompiler-cache/summary.json)。
首轮 MSVC 链接内部错误已以显式 catch 修复；SDK 审计改用输入子树监听及直接元数据读取后通过，
原失败终态保留。后续从本分支/PR 续接；禁止强推，合入仍需用户明确批准。

# 当前：Joypad 11 类资产恢复已验收（PR #44）

用户指定 `recovery/legacy-sdk-cache` / `a58df102`；实现位于
`fix/joypad-type-compilation` / [PR #44](https://github.com/wu162/NewSdk/pull/44)。
11 个独立 Joypad 根、34 个运行时按钮 Data 分支及生产路由已接入；XML 注册范围为
300 类（278 全局/22 非全局），仅排除 TestGameObject。历史 82/207 类目录与证据保留。

源码 `1f0cea03` / [Windows 41270490335](https://github.com/wu162/NewSdk/actions/runs/41270490335)
整体 success：组件 Od/O2、四平台三缓冲差分、生产 Debug/Release 和全部 310 路由、
自制 43 资产小 MOD（11 根/29 个 schema Data 分支）原 SDK 四流逐字节一致。
归档仅下载一次，D/R 三份运行文件、同生产对象的验证入口及八对原始四流已复核，
[收据](evidence/joypad/41270490335-1/summary.json)已入库。最初两次脚本预检失败保留。
没有运行性能测试、完整大型 MOD、缓存/并行新矩阵或游戏内验证。实现与边界见
[Joypad 说明](docs/components/xml/XML_JOYPAD.md)。禁止强推，PR 待审，未合并。

# 此前：TGA/Wave8已合入性能分支，修复版全15流冷计量完成

用户授权的PR#43已以 `a15e3c1e` 合入本分支。生产 `24f83d2b` / [41260620635](https://github.com/wu162/NewSdk/actions/runs/41260620635) 组件与小CLI success；未额外编译整个MOD或mymod做codec验收。TGA原图246三缓冲、边界9、原三路/31格式、D/R每配置4347音频转录、100次EALayer3独立解码及52资产小CLI通过。[组件身份收据](evidence/codecs/new-world/41260620635-1/summary.json)。

[修复后41261694766](https://github.com/wu162/NewSdk/actions/runs/41261694766) 已完成新旧全部1+13+1流冷编译，各4058文件：原1921.681秒→新410.182秒（少78.66%，4.68倍）。双方原始媒体不改，只剔除89Joypad；固定已验收Release5aa008d，计量源299286c5，无额外完整预检。整体outputs-differ，仅mod.bin的330个DXT1/BC1块不同，59846863字节差异与旧基线完全相同；其他4057文件身份一致，真实8声道CDATA两方原SHA相同。n1、OS页缓存未控、不含BIG，不能拿别VM的旧94条基线算修复前后提速。[完整报告/边界/证据](docs/NEW_WORLD_71_SDK_COMPARISON.md)。

本地SDK/Mods源码已恢复原贴图与8声道声明，仅89Joypad仍剔除；原包/备份保留。完整MOD热继承缓存历史失败及BC1字节边界未修复，不冒充整组原字节等价或全热态验收。用户于2026-11-01明确批准[PR #42](https://github.com/wu162/NewSdk/pull/42)合入 `recovery/legacy-sdk-cache`；合并状态及提交以GitHub为准。合入包含已验收的TGA/Wave8修复与性能脚本、证据，BC1差异和完整MOD热继承缓存边界继续保留。Mod源码仍仅在本地SDK/Mods，Actions运行时从ModDB下载。禁止强推。

# 当前：音频身份差异已修复，原 SDK 全输入配对通过（PR #41）

生产源码 `8050f22d`，[Windows 41244132060](https://github.com/wu162/NewSdk/actions/runs/41244132060) 整体 success：Debug/Release 新构建、Core/CLI/取消、后端/缓存/会话、混合音频与旧会话升级、完整 mymod 原 SDK、两个三文件 ZIP、完整 Upheaval 22 样本均通过。[正式归档/D/R/ZIP 单次下载复算](evidence/refactor/audio-identity/41244132060-1/accepted-summary.json) 已保存。

[修复后原 SDK 对照 41245972797](https://github.com/wu162/NewSdk/actions/runs/41245972797) **success**，20 配对/40 构建全部原字节相同。完整 Upheaval 输入的 base 流共1295文件/75,093,395字节（含1289音频CDATA、四个链接流），mymod共4文件/219,199字节；冷/热/禁缓存及跨工具均一致。新归档已单次下载复算，并直接比较其中四流原始字节，没有归一化或放宽门槛。[全部计时与核验](evidence/refactor/original-mod/41245972797-1/summary.json)、[原始报告](evidence/refactor/original-mod/41245972797-1/report.md)、[原始归档](https://github.com/wu162/NewSdk/actions/runs/41245972797/artifacts/13059725013)。

NONE/XAS/默认音频恢复原ProcessingHash 2414318214；仅EALayer3实例保留2414318215，EXE版本548405254按原有会话编译器版本迁移。9个混合音频用例收据已补收：46资产中44个EALayer3名称/负载哈希不变，升级仅重编译2个旧格式资产；升级后热态0解析/0编译，改WAV只重编3个依赖资产并与46资产独立重建相同。独立Audio DLL保留原BAB类型级策略，编码器未改。[音频缓存规则](audio/EaLayer3.md)。

本次同VM默认冷n3/热n6中位秒：mymod 0.556325→0.458682（少17.55%）、热0.354725→0.362145（多2.09%）；Upheaval 50.839589→18.607911（少63.40%，2.73×）、热1.487778→0.866732（少41.74%，1.72×）。仅base loose计时，新版jobs4/原版串行，OS页缓存未控、无排除预热；与旧41212813051的VM不同，不能跨运行归因速度变化。旧失败保留为历史，不覆盖。后续仅测试/证据，没有再次重建。[PR #41](https://github.com/wu162/NewSdk/pull/41) 本轮已获用户明确合入批准，合并状态以 GitHub 为准。禁止强推。

# 当前：原 SDK 与当前编译器 Benchmark 已执行，PR #41

用户指定 `recovery/legacy-sdk-cache` / `1a73017a`（PR #40 已合入）。复用生产 `12c82011` / `41204304969` 的已验收 Release，生产/SDK/构建输入闭包和三份运行文件核对一致，没有重建。[本轮 41212813051](https://github.com/wu162/NewSdk/actions/runs/41212813051) 已完成 20 配对/40 进程，全部编译退出0；工作流 **failure / outputs-differ**，不能称整组通过。[PR #41](https://github.com/wu162/NewSdk/pull/41) 保存复现脚本与证据，未合并。

同一4逻辑CPU Windows VM、完整MOD输入、base loose流，新版jobs4/原版串行；默认压缩双缓存冷n3/热n6中位秒：mymod冷0.858703→0.711169（少17.18%）、热0.547668→0.548666（多0.18%）；Upheaval冷92.655592→28.497811（少69.24%，3.25×）、热2.399294→1.332181（少44.48%，1.80×）。无缓存各n1、CPU/内存/缓存占用、首次mymod较慢样本和所有原始计时均保留。

mymod 10/10原字节相同。Upheaval两边均编译3235资产，CDATA按资产身份分组后1291份负载的长度/SHA256多重集相同，但1289份AudioFile文件名身份及四流原字节不同；已登记的AudioFile ProcessingHash升级见 `audio/EaLayer3.md`，**未证明四流差异仅由该升级引起或归一化后等价**。各自冷/热/无缓存原字节稳定，SDK/MOD/运行文件/搜索根存在性未变。[复算摘要/全部40样本](evidence/refactor/original-mod/41212813051-1/summary.json)、[原始报告](evidence/refactor/original-mod/41212813051-1/report.md)、[原始归档](https://github.com/wu162/NewSdk/actions/runs/41212813051/artifacts/13047262637)。只测base，不含APT/全局元数据/BIG/SKUDEF，OS页缓存未控、无排除预热；不冒充全MOD端到端或原字节等价性能验收。禁止强推，合入仍需用户批准。

# 当前：资产全流程并行已验收，PR #40 待审

分支 `feat/asset-pipeline-parallel`，基线 `1810fff`；[PR #40](https://github.com/wu162/NewSdk/pull/40) 待审，未合并。生产源码 `12c82011` 的 [Windows 41204304969](https://github.com/wu162/NewSdk/actions/runs/41204304969) 整体 success：Debug/Release 新构建、Core/CLI/取消、完整缓存/会话（各108用例，含部分保存重试）、原 SDK、BIG、两份三文件 ZIP 与完整 Upheaval 22样本均通过。XML 资产纯计算、认证验证/编码、独立 IO、完成优先调度、物理去重及文档版本已接通。[实现/身份/原始计量与范围](docs/ASSET_PIPELINE_PARALLEL.md)。

两份正式归档、D/R三文件与两个ZIP已单次下载复算。[同机旧新配对 41206438783](https://github.com/wu162/NewSdk/actions/runs/41206438783) 24/24样本、1313个发布文件原字节相同；默认off冷n3：26.870→23.931秒（减少10.93%），热n6：1.368→1.381秒（增加0.92%）。单次detailed中XML阶段变慢、CPU成本及全部边界如实保留，不作为普遍提速结论。后续仅文档/证据，不代表再次构建。禁止强推，合入仍需用户明确批准。

# 当前：缓存优化与源码纹理组合验收通过，PR #38 已获合入批准

已同步目标 `recovery/legacy-sdk-cache` / `53ace331`（PR #39源码Texture/原身份/三路并行），保留此前会话/Schema共享、XML哈希与冷热优化，以及用户要求的 `c85d4a97` 回退；父快照、窄instance依赖和新增引用去重保持撤回。组合生产源码 `b385337e463bddebdb267946d9edb1b3b6ee5f0a`，新的 [Windows验收41192817627](https://github.com/wu162/NewSdk/actions/runs/41192817627) 全success。

Debug/Release新构建、Core/CLI/CtrlBreak、64worker、完整缓存/搜索根/会话、Texture原三路与常见格式、完整mymod原SDK、两份三文件ZIP、Upheaval22均通过。新full归档仅下载核验一次；六份运行文件、两个ZIP、build/link身份与doctor/BIG部署核对通过。[组合身份收据](evidence/refactor/cache-texture-merge/41192817627-1/runtime-package-verification.json)。后续仅验收记录，不代表再次构建；旧e2与8fe独立产物不冒充当前组合EXE。

用户已明确批准PR #38合入，生产与必要验收已完成；后续从 `recovery/legacy-sdk-cache` 续接，合并状态以 [PR #38](https://github.com/wu162/NewSdk/pull/38) 为准。新前端并行方案尚未实现。以下两边历史记录保留，禁止强推。

# 已合入：Texture 原身份与并行编译（PR #39）

分支 `feat/texture-plugin`，[PR #39](https://github.com/wu162/NewSdk/pull/39)，
基线 `recovery/legacy-sdk-cache` / `074bf768`。用户要求保持原 DLL 身份：版本
`4202`，Texture/OnDemand/Atlas ProcessingHash 分别为 `2673862832`、
`4165508788`、`1182597302`，已恢复；原 DLL 对照不再使用身份替换包装。

三路 Texture 已纳入现有有界并行调度：输入只读租约和头部预算、私有 CDATA、
S3 可变状态 TLS、工作线程消息由 owner 顺序回放。无法准备或预算不足沿用 owner。
生产/测试源码 `8fe12ca9b`，
[Windows 41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818) 整体 success：
Debug/Release 构建、Core/worker/失败回收、64并行缓存矩阵、原三路完整转录、
真实 CLI/取消及常见格式通过。D/R 均实测4个贴图任务重叠，jobs1/4/auto输出相同，
31个格式用例原始结果与上轮全等，16个贴图CLI用例通过；已下载复算独立
[common收据](evidence/texture/plugin-validation/41185772818-1/common-summary.json)。
完整缓存、完整mymod原BAB四流/BIG/SKUDEF、两个独立三文件ZIP与完整Upheaval22样本通过。
最终归档、六份运行文件及两个ZIP已下载复算；原三路4202完整转录逐字节相同。
[完整收据](evidence/texture/plugin-validation/41185772818-1/full-summary.json) 与
[正式产物](https://github.com/wu162/NewSdk/actions/runs/41185772818/artifacts/13037167160)
已保存，包目录为 `artifacts/final-release/41185772818-1/`。

此前生产源码 `4c74bf64b` 的
[Windows 41180859852](https://github.com/wu162/NewSdk/actions/runs/41180859852) 已整体通过，
只代表之前串行和新身份版本；历史证据保留，不覆盖。范围、来源及当前证据见
[TEXTURE_PLUGIN](docs/TEXTURE_PLUGIN.md)。后续仅文档/收据，没有再次修改生产源码。
运行目录仍为 EXE、config、ANTLR 三文件；七种 schema 输出和已知 BC1 字节差异
范围不变。保持原身份意味着既有缓存可复用，切换编码实现需使用干净或独立目录
来验证新字节；不自动清理缓存。未经用户批准不合并，禁止强推。

# 此前：已按用户要求回退本轮编辑优化，保留上一轮冷热成果

仅回退 `e2dba3c3→8e8cf766` 的instance依赖收窄、父DOM快照和引用处理优化。五个Core文件恢复为 `e2dba3c321504752438b4e940d651bbfcdb58fff`；完整生产/SDK/构建输入闭包与该已验收提交一致，复用 [41169755141](https://github.com/wu162/NewSdk/actions/runs/41169755141)，没有重建二进制或重跑完整MOD。此前会话/Schema共享、XML哈希缓冲及冷热性能成果保留。v19会话由恢复后的v18逻辑安全判失效并重建。

同步移除仅属于本轮的三个专用探针和修复工作流/脚本，恢复原失效断言；通用Core/CLI/SDK门槛、后续完整Upheaval比较脚本和全部历史证据保留。检查：生产闭包零差异、变动Python/YAML语法、专用接口清理、diff检查通过。后述8e8及性能结果均为历史状态，不代表当前代码。新并行方案尚未实现。PR #38仍待审，禁止强推和未经批准合并。

# 历史：完整 Upheaval 大幅编辑热缓存比较已完成，未证明普遍提速

用户追加的大型MOD八类真实大改已完成：[41181143063](https://github.com/wu162/NewSdk/actions/runs/41181143063) A/B均success，计量源 `a4682714`，生产仍为已验收 `8e8cf766`，基线 `e2dba3c3`，没有重建。完整原MOD保留，SDK父本地覆盖与8个生命周期补充资产明确标注。每场景off三对、detailed单列；272/272构建0错误/1859原警告、每代全发布原字节相同、192次恢复/热检查0解析0编译。sourceCommit/runId、实际输入、生产树和运行五文件身份通过核对，两个新归档各核验一次；既有非法拒绝/失败取消/SDK门槛按未变二进制复用。

无关父兄弟解析16→1，三对均快，中位少2.47%；有效父解析16/编译304不变，中位少4.29%但逐对方向混合。GameObject少0.19%、Weapon多0.74%、Define多2.15%（解析16→12）、增删重命名少0.35%、80媒体多2.08%、32W3X多1.93%。纯热无普遍改善。引用阶段单次detailed减少工作，但所有真实修改均无snapshot hits/reloadAvoided；Define继承重载0→1。本轮未证明普遍编辑提速或完整MOD父快照收益，不追加测试改善数字。[完整结果/原始三对/边界/证据](docs/UPHEAVAL_LARGE_EDIT_COMPARISON.md)。下一步优先查真实MOD快照为何未命中及Define新增重载，再按Schema/会话/输出与整文档前端热点推进。未新增生产代码；PR #38 未合并，禁止强推。

# 上轮：缓存后编辑的依赖/父快照/引用优化已验收，PR #38 待审

`perf/compiler-cold-hot-cache` / [PR #38](https://github.com/wu162/NewSdk/pull/38)，未合并。生产二进制 `8e8cf766`，后续仅测试/文档。组合验收 [41178094135](https://github.com/wu162/NewSdk/actions/runs/41178094135) success：原D/R与12既有Core/CLI/取消/缓存等有效组复用，Include D/R各140检查/4组、完整mymod原SDK、五文件ZIP、Upheaval22全通过；父快照/引用D/R成功结果按实际输入闭包复用。原失败终态和修复来源保留。

实际父实例/祖先/文件物理闭包替代instance整文件内容依赖（XInclude/真实文件仍保留）；旧v18相关文档安全重载、写v19；请求内成功原节点快照和引用DOM/Schema/类型对/路径复用均已落地。无关父兄弟2→1解析，受控父快照3→2解析/1→0继承重载、后端不变；快照仍保留整父DOM，局部XML仍按整文档前端。

同机编辑配对 [41178837554](https://github.com/wu162/NewSdk/actions/runs/41178837554) 80/80、每代原字节及再热0/0通过。off各三对：有效父518.893→511.192ms（减少1.48%），无关兄弟490.765→493.781ms（增加0.61%）；小负载纯热n12增加2.52%。**没有稳定端到端编辑提速结论**，不以解析减少代替总耗时收益，不与旧完整MOD纯热数字混算。必要验收已完成，不再扩大测试。

[本轮实现/数据/验收/未测](docs/HOT_CACHE_EDIT_OPTIMIZATION.md)、[下一步入口](docs/HOT_CACHE_REFERENCE_RECONCILIATION.md)。后续优先实例级表达式/继承/引用阶段复用与实际XSD/启动成本，父DOM持有成本另做证据；完整XSD、失败/取消和跨流清理保留。禁止强推，未经用户明确批准不合并。

# 上轮：冷/热缓存性能优化已验收，PR #38 待审

分支 `perf/compiler-cold-hot-cache` / [PR #38](https://github.com/wu162/NewSdk/pull/38)。已同步目标FLA基线 `074bf768`，组合源码 `e2dba3c3`，[Windows 41169755141](https://github.com/wu162/NewSdk/actions/runs/41169755141) 全success：Debug/Release、真实FLA/AS、Core/CLI/取消、64worker/缓存、完整mymod原BAB、五文件ZIP及22个完整Upheaval样本。原归档、十运行文件和两个ZIP已下载核验。

最新UI基线同机jobs4：完整MOD冷36.723→33.501秒(-8.78%)、热2.273→1.691秒(-25.63%)，24/24样本通过；扩容+49152Armor正式off冷70.806→61.259秒(-13.48%)、热10.099→6.594秒(-34.71%)。扩容全部18正式+2补充通过，随后基线detailed在720秒总预算耗尽时终止，运行failed保留，候选detailed未测；不把整组标通过。38进程增量诊断通过，含未使用Define与仅丢会话元数据。全部已完成对照产物原字节一致，冷n=3/热n=6、OS页缓存未控。

[实现/全部原始计量与限制](docs/COMPILER_COLD_HOT_OPTIMIZATION.md)、[深入源码优先级](docs/COMPILER_CACHE_SOURCE_ANALYSIS.md)、[上传热缓存文档对照](docs/HOT_CACHE_REFERENCE_RECONCILIATION.md)。共享schema/成功会话、原512字符/ANSI哈希缓冲、Schema事实缓存和纯查询/纯写索引精简已落地；继承粒度、父DOM与稳定排序的后续边界另列。未经用户批准不合并，禁止强推。

下一轮优先优化修改后的前端范围：收窄instance依赖→请求内父实例快照→引用处理；[待办与执行节奏](docs/HOT_CACHE_REFERENCE_RECONCILIATION.md)、[可直接使用的提示词](docs/HOT_CACHE_NEXT_ROUND_PROMPT.md)。多数有效时间用于成组实现，中途短场景、收口集中验收；必要检查通过后停止扩测，复用有效证据，压缩重复归档。无修改纯热态收益不代表编辑收益，现有增量诊断尚无稳定多样本提速结论。

# 当前：FLA / AS 直接 MOD 四流插件（验收通过，PR #36 待审）

用户指定 `recovery/legacy-sdk-cache` / `665468cb`；成果在 `feat/fla-direct-mod`，
[PR #36](https://github.com/wu162/NewSdk/pull/36)。生产/测试源码 `75d29895`，
[Windows 41165126334](https://github.com/wu162/NewSdk/actions/runs/41165126334) 整体 success：
Debug/Release 新构建、精确五文件部署、两份原附件（3 / 62 资产）、混合 MOD 与裸 FLA
自动发现、30 个真实 CLI 源依赖/缓存/失败清理重试用例、原 Core/CLI/会话缓存及完整
mymod 原 SDK 差分、两个独立 ZIP 通过。归档、十个运行文件及两个 ZIP 已下载复算。

支持二进制 MX2004 / Flash8 FLA 和 AS1/AS2，直接进入 Core 多资产链接；旧二进制
APT 及其 worker 资格保留。三个固定 UI 库嵌入 EXE。操作、边界及失败合同见
[FLA UI 文档](docs/FLA_UI_COMPILER.md)，[验收身份及原收据](evidence/ui/fla-validation/41165126334-1/summary.json)。
首轮按钮兼容问题和错误测试预期的失败记录保留；未新增游戏内 UI 播放验收。
未经用户批准不合并，禁止强推。以下为历史交接。

# 当前：PR #33 组合验收通过，已获用户合入批准

已同步 `recovery/legacy-sdk-cache` 的 `0d7b685c`（含已合入 PR #32、#34），只解决续接文档冲突，保留两边生产实现。组合源码 `1a3e4bc6`，[Windows 41152199159](https://github.com/wu162/NewSdk/actions/runs/41152199159) 整体 success：Debug/Release 新构建、Core/CLI/取消、64 worker/cache、完整缓存/载荷/输入根/BIG、完整 mymod 原 SDK、五文件 ZIP 及 Upheaval22样本原字节通过。归档已下载复算，十个运行文件/两个 ZIP 身份已核对。

20个插件生产文件及63组 XML 组件依赖未变，复用下方独立组件矩阵；新组合产品没有冒用旧 EXE。新增组合收据与依赖身份见 [验收目录](evidence/refactor/tokenizer-native-stage2/41152199159/archive.json)，详细来源见 [第二阶段报告](docs/TOKENIZER_NATIVE_STAGE2.md)。用户已明确批准 PR #33 合入，无需再次确认；禁止强推。

# 当前：六项插件原生优化已验收，PR #33 待审

分支 `perf/gameobject-weapon-plugins` / [PR #33](https://github.com/wu162/NewSdk/pull/33)，最终生产源码 `d60904bc`。透明属性查找、member 地址索引、mask 范围解析、标量默认值预解码、Draw 子节点分组及最终缓冲直写均已接入。以 [第二阶段报告](docs/TOKENIZER_NATIVE_STAGE2.md) 为准。

[七类组件 41146357838](https://github.com/wu162/NewSdk/actions/runs/41146357838) success，原 DLL/前后组件大小端三缓冲一致、真实退出0。相对上一阶段，Schema 默认 GO/Weapon 中位耗时减少25.14%/26.95%，32 Nuggets 减少57.37%；是插件负载，不是完整 MOD 收益。

[最终产品 41146357892](https://github.com/wu162/NewSdk/actions/runs/41146357892) 整体 success：Debug/Release、GO/Weapon 完整四平台原字节/tokenizer、Core/CLI/取消/缓存/64 worker矩阵、完整 mymod 原 SDK、五文件包、Upheaval22样本通过。十个运行文件和两个ZIP身份已下载核对。

[共享 XML63 41145352024](https://github.com/wu162/NewSdk/actions/runs/41145352024) 的路由及完整矩阵步骤 success（1,918,904样本/11,064,384比较）；后续重复产品检查停止，整体 cancelled。仅复用其独立 XML 范围，d457f99a→d60904bc 的63组依赖闭包和实际工具链/托管宿主身份未变；最终产品另取上面的完整成功运行。

[完整 MOD 三轮配对 41148441137](https://github.com/wu162/NewSdk/actions/runs/41148441137) success，全部发布字节相同、六次0错误/1859警告；中位34.766→34.659秒（减少0.31%，基本持平），第二对稍慢。原始证据均已下载复算，较慢样本保留。未经批准不合并，禁止强推。

# 当前：GameObject / Weapon 插件编译优化（验收通过，PR #33 待审）

用户指定 `recovery/legacy-sdk-cache` / `8f854606`，成果在 `perf/gameobject-weapon-plugins` / [PR #33](https://github.com/wu162/NewSdk/pull/33)。移动嵌套输入树、只读模块索引、Weapon 单遍读取和枚举表复用已接入；仅五个插件 Bridge/Native 生产文件改变。原生 InitOnce 修复混合运行时关闭，失败均保留。

组件源码 `25aedf8b`，[41138099602](https://github.com/wu162/NewSdk/actions/runs/41138099602) success；同生产源码的完整产品 `9f9d1767`，[41138338003](https://github.com/wu162/NewSdk/actions/runs/41138338003) success：Debug/Release、完整原字节/tokenizer、64 worker/cache、Core/CLI/缓存/取消、完整 mymod 原 SDK、两个五文件 ZIP、完整 Upheaval 22 样本通过。归档、十个运行文件及 ZIP 已下载复算。

同 VM 完整 Upheaval 三轮前后配对 [41141062587](https://github.com/wu162/NewSdk/actions/runs/41141062587) success，全部发布字节相同；中位数 22.185→21.638 秒，耗时减少 2.47%，第三对较慢及 OS 页缓存边界保留。插件合成负载中空 GameObject 慢约 6%，复杂 GameObject/Weapon 改善；详见[实现与计量](docs/TOKENIZER_PLUGIN_PERFORMANCE.md)。

追加的[深入源码研究](docs/TOKENIZER_PLUGIN_NEXT_OPTIMIZATIONS.md)保留原型与 Linux 算法数据；六个方向随后已实施，当前状态以上方第二阶段入口为准。未经用户批准不合并，禁止强推。
# 当前：RA3 EALayer3 V1（Windows 验收通过，PR #34 已合入）

用户指定基线 `recovery/legacy-sdk-cache` / `eb503513`；开发分支 `feature/ra3-ealayer3`，
[PR #34](https://github.com/wu162/NewSdk/pull/34)。源码/二进制 `4ddac274`，
[Windows 41149785200](https://github.com/wu162/NewSdk/actions/runs/41149785200) **success**：
176 实际编码/88 候选解码、jobs1/4、原有格式 D/R 各521原字节、正式 BAB 44资产、
正常产品 D/R、热态0编译/修改WAV仅1编译/独立重建、完整 mymod/LoD/BIG/SKUDEF通过。

EALAYER3 选择真实 EL31/V1b/SNR5，复用选定 sagetools/LAME3.96 源码，保留现有
输入/重采样/Loop/资产/SessionCache/worker；旧 AudioFile processing hash 已更新。
1/2/4/6声道、六种16–48kHz MPEG采样率、0–100质量、Loop、RAM/streamed通过；
8/11.025/12kHz、V2及游戏内播放未完成。原插件随附库 codec31 实测为 V2；显式 V1
LSF 参照不可用的25条数据明确保留，只有4条可用 V1 参照计为波形对照通过。

范围/来源/限制见 [audio/EaLayer3.md](audio/EaLayer3.md)，原收据和下载复算身份见
[ealayer3 summary](evidence/audio/ealayer3-validation/41149785200-1/summary.json)。
许可只在源码保留，不嵌入或加入部署包。后续提交仅文档/收据；禁止强推，未经用户批准不合并。

# 当前：L8 热缓存 Benchmark（Common 通过，PR #32 已合入）

分支 `benchmark/l8-hot-cache`，基于 `recovery/legacy-sdk-cache` / `8f854606`；[PR #32](https://github.com/wu162/NewSdk/pull/32)。Benchmark 脚本/工作流已实施，未修改生产算法。Windows/Linux 各 33 项预检通过；[Common 41139062989](https://github.com/wu162/NewSdk/actions/runs/41139062989) success，测量源 `a286062f`、Release 源 `cc10391b`，整个 Windows job 18 分 20 秒，C00–C12 全部完成，75/75 正式样本 wall/elapsed 均 >8 秒，另有 3 个独立 detailed 样本。实际纯热态约 9.34 秒，未达 10–14 秒软目标，未把调参点当成绩。详见 [报告/原始计时/范围](docs/HOT_CACHE_BENCHMARK.md)。旧失败/预算耗尽结果保留，不作跨 VM A/B。Full 未运行，Environment 配置受集成权限限制，且 28 个扩展条件的双重复理论下限 >22.4 分钟、超过其 16 分钟阶段预算，不能声称整组完成。禁止强推，未经用户批准不合并。

# 当前：AudioFile 全设置并行与低预算（组合验收通过）

[PR #30](https://github.com/wu162/NewSdk/pull/30) 已获用户批准合入 `recovery/legacy-sdk-cache`。成果在 `perf/audiofile-parallel`，现已同步目标分支 `d85b8c8c`（已合入的 PR #28）。保留其重复状态清理和预算估算，适配 AudioFile 测试；显式采样率及压缩诊断可并行，外部输入预算由 64 倍降至 4 倍，压缩提示进程内仅一次。

组合源码/二进制/测试来源 `cc10391be67dda57c7efa7fc06256b99e9372d9a`，[41068958541](https://github.com/wu162/NewSdk/actions/runs/41068958541) **success**：Debug/Release 新构建，八种音频设置 worker 三缓冲/CDATA、64 生产缓存/jobs 用例、CLI/CtrlBreak、全部缓存组、完整 mymod 原 SDK 和五文件包通过；完整 Upheaval 22 样本原字节一致，均 0 错误/1859 原警告。新归档、两个发布 ZIP/十个运行文件均已下载复算；14 份原收据身份见[组合验收摘要](evidence/refactor/audiofile-parallel/41068958541-1/summary.json)。独立 AudioFile 分支的 `6c2f0869`/41058436061 证据作为历史保留。后续只补文档/证据，无新游戏内验收。详见[实现与验证范围](docs/AUDIOFILE_PARALLEL.md)。禁止强推。

# 当前：冗余安全清理已合入（PR #28）

`cleanup/redundant-defenses` 基于刷新后的 `b89fbadb`，生产/测试源码 `ecd2bf1d`。[Windows 41062149232](https://github.com/wu162/NewSdk/actions/runs/41062149232) 全部 success：Debug/Release、Core/CLI、SessionCache 108×2、两类原缓存/导入、Audio/Texture/W3X、BIG/SKUDEF/完整 mymod 原字节、并行/热态依赖/协议与 40 次配对输出均通过。后续仅文档/证据。

已删除 CacheInputBoundary、运行目录身份扫描、session.identity、.file-identities.json/SHA 收据、ANTLR 固定审核、诊断句柄固定及重复后端状态。原缓存语义、实际依赖、ChangeTime、窄 XSD 约束失效及必要 worker 预算保留；没有新侧车/发布事务。混合冷 1010.75→969.61 ms、热 652.69→634.27 ms；准备 5.29→7.79 ms、目录查询各 +3 的不利数据也保留。

源码依据、完整计量/范围和原始失败见 [DIRECT_OUTPUT](docs/DIRECT_OUTPUT.md)。[PR #28](https://github.com/wu162/NewSdk/pull/28) 已合入；未强推或修改授权/合入规则。无正确性阻塞问题，准备阶段及目录查询仍可继续精简。

以下为历史交接，不代表本轮实现或验收结果。

# 当前：插件重复 schema 校验已移除（验收通过，PR #26 待审）

用户指定基线 `recovery/legacy-sdk-cache` / `5c1843ae`；成果在 `refactor/remove-plugin-schema-validation` / [PR #26](https://github.com/wu162/NewSdk/pull/26)。删除 XML、GameObject、Weapon、Effects/Particle、Locomotor 及生成代码中的属性/子节点白名单、文本、单例、结构和命名空间校验及专用遍历/数组分配。Core 统一 XSD、依赖元数据及类型路由继续工作；Audio/W3X 的编码器数据检查保留。

生产源码验收入口 `e3272ffd`；最新 `822007a5` 仅补齐测试源清单/流程和旧断言，生产源码、SDK、构建目录和类型目录的 Git tree 均相同。Core 的专门用例验证非法 XSD 在派发前失败，直接插件调用不重复校验。组件拒绝用例使用统一 SDK schema 测试边界，原始字节与 ABI 断言继续保留。

- [Core 41037021490](https://github.com/wu162/NewSdk/actions/runs/41037021490) **success**：Debug/Release、CLI、缓存、完整 mymod 原 SDK 差分、独立五文件包和 Upheaval 22 样本对照通过。
- [XML 41037565236](https://github.com/wu162/NewSdk/actions/runs/41037565236) 的 XML 步骤及原收据 **passed**：Debug/Release 各 63 组、四平台，合计 1,918,904 样本/11,064,384 缓冲区比较。该运行整体 **failure** 来自随后 GameObject/Weapon 测试源清单未登记共享助手；此部分已在下面的独立运行修复验收，XML 成功范围及输入未变。
- [GameObject/Weapon 41042830679](https://github.com/wu162/NewSdk/actions/runs/41042830679) **success**：完整原版字节/tokenizer 矩阵、Debug/Release、完整既有样本集及隔离运行通过；该专项复用上面的独立 XML 成功范围。

三份归档已实际下载并复算 SHA256；原收据、来源、运行文件身份及复用边界见 [验收摘要](evidence/refactor/plugin-schema-removal/summary.json)。本地路由审计、26 项组选择测试、测试源清单及两个 UIConfig 生成器精确复现通过。未经用户批准不合并，禁止强推。

# 当前：有界热缓存扩展与失效/依赖计数（已验证，PR #23 待审）

分支 `test/hot-cache-coverage`，基于 `8a6b7a70`。扩展三档 Armor 规模（32/256/2048 资产、4/32/128 文档），中档混合 Texture/PCM Audio/W3X，代表性缓存模式、压缩、jobs、BIG、等长同mtime编辑与输出/会话修复。单进程15秒、整轮120秒，不做全维度笛卡尔积。

summary/detailed 新增按流聚合的 `performance.counters`：实际缓存失效原因、唯一失效文档和原 self-instance 数、Include 传播、定义/继承/文件依赖检查及传播重载；off 不建计数字典，不追加文件读取或依赖扫描。见 [性能说明](docs/PERFORMANCE.md)。[PR #23](https://github.com/wu162/NewSdk/pull/23)，禁止强推，未经批准不合并。

生产二进制/测试来源 `91b040d2`，[41010073140](https://github.com/wu162/NewSdk/actions/runs/41010073140) success：重建Release、App合同、本地及Windows五项Python预检、原14样本（9.98秒）及扩展56样本（42.85秒）通过。全输出与每代独立无缓存原字节相同，定义修改实际记录 contentChanged→definesChanged→inheritanceChanged，3失效文档/2依赖传播文档；Include最大深度3，访问与去重文档分别统计，修改后热态零失效/解析/编译。五运行文件和原归档SHA256已下载复算，69份报告Schema通过。[原收据与摘要](evidence/refactor/hot-cache/41010073140-1)。未新增Debug、完整MOD或游戏内验收；后续仅文档/证据更新。

首轮 `90e81710` / [41009339596](https://github.com/wu162/NewSdk/actions/runs/41009339596) 已保留失败：App、当前Release、原14样本通过，扩展48样本的字节检查通过后因继承用例误要求晚于文件依赖检查的原因而拒绝。实际输出相等、直接失效1文档并传播至第2文档；控制器已按真实先后顺序修复，并分离定义文件以独立触发定义与继承链。原收据及归档复算身份在 [证据目录](evidence/refactor/hot-cache/41009339596-1)。生产源码未随测试修复改变。

# 当前：W3DAnimation 压缩优化（功能及真实通道计量已通过）

分支 `optimize/w3d-animation-compression` / [PR #22](https://github.com/wu162/NewSdk/pull/22)，基于 `recovery/legacy-sdk-cache` 的 `436115db`。Timecoded 邻接索引/局部最小堆、AdaptiveDelta 有界搜索/常量快路/步长复用、生产逐帧去拷贝已完成，精度及输出不变。

生产/功能验收源码 `4e9c9a27`，Windows [41003661518](https://github.com/wu162/NewSdk/actions/runs/41003661518) success：Od/O2原版组件、正式插件、13376新旧完整输出、Debug/Release产品完整三缓冲区重放及真实CLI缓存矩阵通过。第一轮漏建验证宿主的失败已保留并修复。

真实源计量源码 `12848f8e`，[41004166786](https://github.com/wu162/NewSdk/actions/runs/41004166786) success：194动画/7390通道/468946展开帧，同VM三对中位数25.161→8.449秒，约2.98倍；选中编码全部逐字节相同。它是压缩阶段，不能当作完整MOD收益。生产源码及既有功能验收输入随后未变；分别保留功能与计量来源。

完整未精简Upheaval端到端对照 [41004795406](https://github.com/wu162/NewSdk/actions/runs/41004795406) success：jobs4/双缓存关闭/全新输出，两对中位数33.832→29.616秒，减少12.46%；全部BIG/SKUDEF实际字节相同，四次均0错误及相同1859警告。仅两样本/实现、未控OS页缓存，不作通用保证。原始收据、完整输出对照及artifact哈希已下载核对并归档；[详细说明](docs/W3X_COMPRESSION_PERFORMANCE.md)。禁止强推，未经用户批准不合并。

# 当前：AudioFile XAS 性能优化（已验收，PR 待审）

基于 `recovery/legacy-sdk-cache` 的 `45d485d8`，成果在 `perf/audio-xas-ranges` / [PR #21](https://github.com/wu162/NewSdk/pull/21)。六项优化一次完成：原数组偏移读取、最终数组直写、完整128帧块零复制、Loop区间独立分段、复用float[30]、四预测器共用一次转换。只修改 Audio 三个编码器，未修改其它插件。

源码/二进制/测试来源 `b8e37a2e957f7a2465ad303110b92eec3269df4d`；后续只有结果归档。Windows [40996371215](https://github.com/wu162/NewSdk/actions/runs/40996371215) success：Debug/Release x86 各8924原版完整输出一致、既有拒绝和块/精度矩阵通过；同机3280边界+21长音频完整输出对照通过。未执行完整SDK。完整ZIP已下载复算SHA256，原版输出压缩保留于仓库。

同机CLR x86三轮中位数：120秒双声道RAM 46.243→2.788秒，分配336.078→6.523MiB，GC33/3/1→0/0/0；Loop RAM约16倍。600秒streamed分配1081.368→32.744MiB、GC184/2/0→0/0/0，耗时13.629→13.936秒（增加2.3%，如实保留）。[完整方法和计量](docs/AUDIO_XAS_PERFORMANCE.md)。禁止强推；未经用户批准不合并。

## 上一批：Tokenizer 并行与输入准备去拷贝（已验收）

成果分支 `recovery/legacy-sdk-cache`；生产源码/二进制/测试来源 `5be40ac421fa4562654d34cc3d62050eca7a389e`。本轮基于 `fbf51cee`，后续提交仅归档文档和证据。禁止强推；未合并其它主线，未发布 Release。

已开放精确内建 GameObject、WeaponTemplate、ArmorTemplate worker 资格；按 Core 生命周期直接删除 XML DOM、NameTable、namespace bindings、句柄和引用集合深拷贝。普通任务使用原声明，CDATA 任务只浅拷贝状态以隔离输出路径。删除调度器重复整树估算和无文件任务的租约集合。三种可选 trace 共享索引写入已同步，旧缓存/哈希及 owner 验证提交/失败取消 join 保留。

统一新验收 [40983673225](https://github.com/wu162/NewSdk/actions/runs/40983673225) **success**：Debug/Release 完整构建、三 tokenizer×四平台 worker/trace/三缓冲、XML284×四平台、64生产缓存/jobs用例、原BAB、CtrlBreak、缓存冷热/失效/重试、完整未裁剪mymod原SDK四流/BIG/Additional和五文件包全部通过，没有复用旧二进制。原归档8b4be284…已实际下载复算匹配。

完整 Upheaval 同VM 22样本全部0错误、1859原警告、输出原字节一致。默认冷jobs1/4/auto为73.489/43.611/43.632秒，jobs4比jobs1耗时减少40.7%；无缓存冷70.480/41.610/41.716秒。三类实际调用121/108/12全部取得worker资格，未知插件owner回退从旧记录358降至117；10个大任务预算回退保留。准备阶段本轮jobs4/auto为2.126/2.029秒；旧4.561/4.710秒来自不同VM，不作为严格前后性能对照。热态零解析/编译/准备，单动画修改仅编译1资产并与独立串行重建一致。

[源码依据和完整新结果](docs/TOKENIZER_PARALLEL_INPUT.md)；[新收据摘要](evidence/refactor/tokenizer-parallel/40983673225-1/summary.json)；[计量](evidence/refactor/tokenizer-parallel/40983673225-1/measurement.md)。冷每档一次/热两次，OS缓存未控；不宣称游戏内验收。旧实现与证据保留于[历史迁移记录](docs/BACKEND_PARALLEL_MIGRATION.md)。
