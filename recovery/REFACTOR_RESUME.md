# 当前：Joypad 编译已验收，PR #44 待审

基于用户指定的 `recovery/legacy-sdk-cache` / `a58df102`，新增 11 个 Joypad 根和
34 个按钮 Data 分支。[Windows 41270490335](https://github.com/wu162/NewSdk/actions/runs/41270490335)
通过组件原字节差分、生产 D/R、310 路由及自制 43 资产小 MOD 原 SDK 四流对照；
没有运行性能测试或完整大型 MOD。当前入口与收据见 [RESUME](RESUME.md)、
[Joypad 说明](docs/components/xml/XML_JOYPAD.md)。[PR #44](https://github.com/wu162/NewSdk/pull/44)
待审，未合并。以下保留历史交接。

# 此前：冷/热缓存性能优化已验收，PR #38 待审

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

# 当前插件优化入口

用户已批准 [PR #33](https://github.com/wu162/NewSdk/pull/33) 合入 `recovery/legacy-sdk-cache`。已同步目标 `0d7b685c`，组合源码 `1a3e4bc6` 的新 [Windows/Core/SDK 41152199159](https://github.com/wu162/NewSdk/actions/runs/41152199159) 整体 success；未变插件/XML 组件复用独立成功范围。以 [RESUME](RESUME.md) 和[第二阶段报告](docs/TOKENIZER_NATIVE_STAGE2.md) 为准。禁止强推。

# 当前：冗余安全清理完成，PR #28 待审

`cleanup/redundant-defenses` 基于刷新后的 `b89fbadb`，生产/测试源码 `ecd2bf1d`。[Windows 41062149232](https://github.com/wu162/NewSdk/actions/runs/41062149232) 全部 success：Debug/Release、Core/CLI、SessionCache 108×2、两类原缓存/导入、Audio/Texture/W3X、BIG/SKUDEF/完整 mymod 原字节、并行/热态依赖/协议与 40 次配对输出均通过。后续仅文档/证据。

已删除 CacheInputBoundary、运行目录身份扫描、session.identity、.file-identities.json/SHA 收据、ANTLR 固定审核、诊断句柄固定及重复后端状态。原缓存语义、实际依赖、ChangeTime、窄 XSD 约束失效及必要 worker 预算保留；没有新侧车/发布事务。混合冷 1010.75→969.61 ms、热 652.69→634.27 ms；准备 5.29→7.79 ms、目录查询各 +3 的不利数据也保留。

源码依据、完整计量/范围和原始失败见 [DIRECT_OUTPUT](docs/DIRECT_OUTPUT.md)。[PR #28](https://github.com/wu162/NewSdk/pull/28) 未合并；未强推或修改授权/合入规则。无正确性阻塞问题，准备阶段及目录查询仍可继续精简。

以下为历史交接，不代表本轮实现或验收结果。

# 当前：直接产物与原 SDK 缓存（已验证，PR #27 待审）

分支 `perf/direct-sdk-output`，基于 `recovery/legacy-sdk-cache` 的 `5c1843ae`。正式产物直接写 `OutputRoot/published`；删除整代 `.build`、发布事务/备份/回滚、独立四流快照、输出 SHA256 与发布收据，以及未接入生产的 App 事务缓存。原 BAB SessionCache/BuildAssetCache、中间资产、依赖失效和 CDATA 复用保留。日志/报告/事件失败仍报错，已生成 MOD 和缓存不回滚。

最终生产/测试来源 `784920357d1a268360a082cd3e51b42a5fa69356`，[41049481674](https://github.com/wu162/NewSdk/actions/runs/41049481674)：Debug/Release 完整 Win32 构建、App/Core/CLI、原缓存与载荷、BIG/LoD/Additional/SKUDEF 原字节、完整 mymod 原 SDK 差分、热缓存编辑及协议矩阵通过。后续仅文档和收据；无新游戏内验收或 Release 发布。

同 VM 基线 `5c1843ae` 对主实现 `f6c3c009` 的36配对样本原字节一致；16 MiB Additional 冷态逻辑读 67.14→18.42 MiB、写 49.35→17.09 MiB，完整进程中位数1098.70→1028.40 ms；小输入冷态约+0.22%，如实保留。最终清理只删除不使用的元数据辅助方法。删除清单、真正需要的目录、计量边界及所有失败/成功来源见[直接产物说明](docs/DIRECT_OUTPUT.md)。

[PR #27](https://github.com/wu162/NewSdk/pull/27) 待用户审查；没有合入或强推。

# 原 SDK 完整缓存恢复：历史交接

本轮性能实现以 [RESUME](RESUME.md) 为当前入口：`codex/legacy-cache-completion` / PR #20，完整生产源码 `4ad5d0d9`，最终验收运行 `40836177604` 和同输入配对基准 `40837902446` 均已 success；当前实现与证据以 RESUME 为准。下面保留原缓存恢复及优化前证据；不能用旧源码通过结果代替新文件身份、哈希收据、BIG/发布复用和缓冲日志的验收。

## 目标与位置

恢复原 SDK 的完整缓存能力，包括默认行为、三种纹理、XML/Deflate、部分/冻结会话、touch、原缓存目录及共享资产。不能以部分增量缓存代替完成。

- 分支：`recovery/legacy-sdk-cache`；基于 `compiler-main` 的 `e7396d3b6fb302682d3610ebbc6e99ce87eb867f`。
- 实现及测试源码：`39039961847cd1ca9cb93743fd51741a4e5c9f49`。后续纯文档提交不改变该验证来源。
- [PR #19](https://github.com/wu162/NewSdk/pull/19)：验收通过，待审查，未合并。
- 当前整体状态：交接列出的生产缓存矩阵、完整 mymod 原版差分和独立五文件 ZIP 门槛均已通过；覆盖边界见下文。

## 验证快照

核对时间：2026-10-29 12:17（北京时间），归档及性能结果于 12:55 后补核。[运行 40814401237](https://github.com/wu162/NewSdk/actions/runs/40814401237) 已于 12:08 完成，整体及全部验收步骤均为 success；对应上述源码，Debug/Release 均由该运行重建。生产及原验收输入未变，完整结果复用该运行，没有重复运行长测试；新增性能基准单独记录来源。[核对摘要](evidence/refactor/legacy-cache/40814401237-1/summary.json)。

| 范围 | 快照状态 |
| --- | --- |
| Debug/Release 应用、Foundation、完整构建与部署检查 | 通过 |
| Core/后端/应用管线、原 BAB 基线、Texture 组件差分 | 通过 |
| 实际 CLI、冷进程、CtrlBreak 取消与报告协议 | 通过 |
| 资产缓存冷热/失效/重试、外部输入根原版差分、Audio CDATA/W3X 重放 | 通过 |
| 会话缓存矩阵：纹理三路、高级模式、原 BAB XML 缓存导入等 | 通过 |
| 会话协议、BIG/Additional/中立零插入及协议 | 通过 |
| 完整未精简 mymod 原版四流及 BIG/SKUDEF 差分 | Debug/Release 均通过，四流差异为 0 |
| 独立五文件 ZIP 部署、生产 BIG 与协议 | 通过 |

早期增量子集的成功运行不能替代本次完整缓存验收。

## 产物与覆盖边界

[原始产物](https://github.com/wu162/NewSdk/actions/runs/40814401237/artifacts/12871674302) 中的 Release ZIP 为 `artifacts/final-release/40814401237-1/NewModCompiler-Win32-Release.zip`，Debug 同目录。外层归档 SHA256 `6c395456add02dec40c7759e9cc5a26d34ea353f5e3ee588a12a78df3bfab044`；GitHub 元数据、上传日志及性能基准续接时本地重新计算均一致。最初 CLI 下载 HTTP 403 的记录保留；后续通过 GitHub 产物下载工具取得原始 ZIP，已读取会话、完整 mymod、发行原收据，并校验 Release 五文件及 Texture 绑定。

部分保存直接验证 XML 失败后完成文档的保存及重试；真实 CtrlBreak 用例使用 `/psc:false`。原 BAB 目录导入直接验证 XML，新宿主另行验证 Deflate 冷热及切换；未增加原 BAB Deflate 导入或 `/psc:true` 取消后持久化的独立生产证明。完整游戏数据、WBData、游戏内运行和中文纹理路径未新增验收。用例映射见[缓存说明](docs/LEGACY_INCREMENTAL_CACHE.md)。

## 从哪里继续

| 内容 | 入口 |
| --- | --- |
| 原缓存接线、选项应用、失败/冻结/touch 行为 | `src/App/Application/CoreStreamCompiler.cs` |
| 原格式会话、CDATA 恢复、部分保存与提交/回滚 | `src/App/Application/LegacyCacheStore.cs` |
| 默认值、目录别名、项目预设 | `src/App/Cli/Options.cs` |
| 生产模式矩阵与原版互操作 | `scripts/TestCompilerSessionCache.ps1`、`scripts/TestCompilerBackendBaseline.ps1` |
| 原版纹理元数据探针 | `tests/CompilerLegacyCacheMetadata.cs` |
| 流程及报告契约 | `../.github/workflows/refactor-core-lifecycle.yml`、`protocol/build-report.schema.json` |

路径相对本目录。行为边界见 [缓存说明](docs/LEGACY_INCREMENTAL_CACHE.md)。

## 下一步

交接中的验收跟进和状态整理已完成，PR 等待用户审查。用户追加的有界性能基准见[说明](docs/CACHE_BENCHMARK.md)：复用原验收 Release，生产输入及原验收测试不变；benchmark 自己的源码/运行身份单独记录。若修改生产或原验收测试输入，按受影响范围重新验证；仅文档更新不触发 Windows/SDK 构建。用户明确批准合入后才可合并至 `compiler-main`。

性能运行 `40820325416` 已 success，基准源码 `c39ad93eb5df7f7b7cd78f7028155d14a5094f2b`，54 样本约 39.6 秒（含单列启动诊断），全部输出一致。mymod 默认热态约减少 19% 耗时，合成纹理资产热态约减少 35%；Upheaval Armor 小子集会话热态略慢。未测完整 Upheaval、BIG 或输入编辑的性能；保留首轮适用性假设失败及一次启动超时，均不冒充有效成绩。

新增原 SDK 同输入配对性能[运行 40823369828](https://github.com/wu162/NewSdk/actions/runs/40823369828) 已 success，基准源码 `1c7ff21f098e972105c410eb586c4f6c8ba32d29`；54 对/108 次计量及两次预热约 91.2 秒，两方实际共用输入、Schema、cwd 和有序资源根，输出逐字节一致。默认热态新版比原版多约 25%～29% 端到端耗时；新版报告/事务发布也在计量内，未作纯 Core 分段归因。明细及原版无会话 `StringHashes.xml` 状态隔离见[对照报告](docs/SDK_BENCHMARK.md)，未重新编译生产。

随后[原因分析](docs/CACHE_PERFORMANCE_ANALYSIS.md)复算已有阶段和旧日志，并取得只读 I/O 诊断 `40826280008`（源码 `f7d69ff6f2aeefae1a32642d9854de853a95b82f`）：身份工作涉及 826 文件/6.66 MB，独立稳定复现约 59.5 ms，非生产方法直接计时；publish 本身仅 11–30.5 ms。缓存复用成功，旧热链接跳过而新 staging 重新生成并三次哈希/复制；优化优先级及未量化部分见报告。原反射诊断的 CLR 加载失败保留，生产未改。

保留失败记录，不降低原版字节差分与取消/失败断言；禁止强推，合入仍需用户明确批准。
