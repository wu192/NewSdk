> 当前缓存与编译路径见 [集中缓存说明](SUPERCOMPILER_CACHE.md)。本页保留直接产物及冗余清理阶段的历史实现、计量和验收；旧输出根缓存布局不适用于新实现。

# 直接产物、原 SDK 缓存与冗余安全清理

本轮基线为重新获取的 `recovery/legacy-sdk-cache` / `b89fbadb`，交付前远程头仍相同。分支 `cleanup/redundant-defenses`，生产/测试源码 `ecd2bf1d`；后续只整理文档和证据。[Windows 运行 41062149232](https://github.com/wu162/NewSdk/actions/runs/41062149232) 全部 success。[PR #28](https://github.com/wu162/NewSdk/pull/28) 待用户审查，未合并、未强推、未修改授权及合入规则。

## 实际删除

- 删除 `CacheInputBoundary.cs`、`CompilerCacheBoundary.cs`；生产从未调用 Begin，删除 resolver、SchemaSet、FileHashItem、文档加载的空 Validate 调用，以及 Core 未初始化的 inputBoundary 和清理逻辑。历史归档的固定 fixture 名仅在历史重放检查中保留并标注。
- 删除 LegacyCacheIdentity 的 SDK config/运行目录 EXE/DLL/config 扫描和整会话身份盐；**session.identity 与 .file-identities.json 的全部生产读写彻底删除**。旧文件不会读取或复用，不扫描输出树做迁移清理。
- 删除 FileHashCache 的 previous/used SHA 收据、JSON 导入/保存、SHA256/hex/base64 包装；删除 FileHashItem.sha256 及对应状态/回调。保留原游戏 FastHash 和已有的请求内目录批量元数据查询。Windows 文件 ID、ChangeTime、mtime、长度直接记入原 BAB 会话项；正常热态不读内容，旧 BAB 项按需建立元数据。没有替代侧车或新缓存框架。
- 删除 VerifyAntlrRuntime 固定 SHA/路径部署审核。生产仅剩 CacheLock 对**路径字符串**取 SHA 生成命名互斥量名称，不哈希文件。Texture 混合 DLL 构建、绑定、PE 与发行验收保持不变。
- 删除诊断目录长期原生句柄、逐祖先 Pin、发布 inode 确认、日志 MoveFileEx 包装；日志开启时 unlink 旧目录项再 move 新文件，避免截断硬链接源。CLI 输入根只解析一次，默认诊断路径复用已解析输出根。删除 BIG 每个只读成员的重复祖先/链接遍历和重复 File.Exists 验证；保留写入点边界、最终 reparse 文件检查、BIG 名称/长度/四流集合及普通 I/O 错误。
- 最终报告在 BuildFinished 和必需通道关闭后只序列化/写入一次，64 KiB 写缓冲；必需 JSONL/报告错误返回 7，已生成 BIG/SKUDEF 和 BAB 缓存不回滚。文本日志失败非致命。终端事件之后的关闭/报告错误仍须结合进程退出码和报告判断，坏通道本身不保证有最终记录。
- 删除 CanPrepare 的重复资格入口、恒真 CanRunParallel、未使用的 FamilyCount/PerTaskBudget/PreparationBudget/RecalculateMemory；W3X 节点计数合并到已有估算，去掉重复解析帧数和立即重复内存采样。未开放未审查插件，也未引入新认证系统。

## 必须保留的真实依赖

| 保留项 | 源码与实际证据 |
| --- | --- |
| 文件 ID/ChangeTime/mtime/长度与原 FastHash | [FileHashItem](../src/Managed/Core/BinaryAssetBuilder.Core/FileHashItem.cs) / [FileIdentity](../src/App/Application/FileIdentity.cs)。等长恢复 mtime 的修改、Include/XInclude、定义/继承/文件依赖传播、删除与恢复均通过；最终冷 FastHash 内容读取 39→39，热态 0→0。关闭 SessionCache 时仍须读取输入产生游戏哈希，不能把它误当 SHA 安全审核。 |
| 窄 Schema 内容失效，写在已有 BAB 会话根，无侧车 | [Reinitialize](../src/Managed/Core/BinaryAssetBuilder.Core/AssetDeclarationDocument.cs#L549) 可直接复用完成状态而略过 [Validate](../src/Managed/Core/BinaryAssetBuilder.Core/AssetDeclarationDocument.cs#L901)；[ArmorPlugin](../src/Plugin/ArmorPlugin.cs#L30) 的 TypeHash/ProcessingHash 为常量。等长恢复 mtime 的 XSD 编辑删除 DamageScalar 默认值并设为必填，修改后 Schema 本身有效；缓存与无缓存两配置都明确返回 4、定位 DamageScalar，恢复后缓存命中、零 XML 解析且原字节一致。只对已经读入的 Schema 字节计算 FastHash，无重复打开。见 [原报告](../evidence/refactor/defensive-cleanup/41062149232-1/Debug-schema-required-invalidated.json)。 |
| 原 BAB 格式/编译器版本/类型/实际依赖、会话互斥与 worker join | 原版 XML/Deflate、部分/冻结/touch、旧缓存导入、共享资产和 CDATA 用例通过。共享 SessionCache 是原文件写入，进程排他仍有实际用途；失败/取消必须 join 后端再保存有用完成状态。 |
| 输入覆盖/路径越界和格式边界 | 原 MOD/SDK junction、诊断 hardlink、晚出现的 output junction、目录拒绝及文件锁定矩阵保留；完整原 MakeBig/BAB 字节对照通过。没有为了通过测试重新允许覆盖输入。 |
| 内建 worker 范围、读租约和 Win32 内存预算 | [PreparedBackendWork](../src/App/Application/PreparedBackendWork.cs)、[PcmWaveAudioCompiler](../src/Managed/Audio/PcmWaveAudioCompiler.cs#L12)：非 NONE/XAS 分支写共享 compressionWarning，其它 Audio Tracer 回调直接写未同步的诊断列表/序号/流；采样率转换可有 400→96000 的输出放大，不能套原 64 倍外部输入预算。Native fopen 重开输入及 x86 地址空间约束保留。 |

## 同机计量

Release，基线 `b89fbadb` 对 `ecd2bf1d`，同一 Windows VM、同一 SDK/有序资源根/合成混合输入。每场景每档 3 对交替顺序计时，共 18 对；另有 2 对独立 Process Monitor 跟踪，共 **40 次 BIG/SKUDEF 全字节一致**。跟踪必须包含成对 Process Start/Exit，排空不计入耗时样本。含 CLR/程序集/诊断；OS 页缓存未控制，不是物理磁盘计量，也不代表完整 MOD 的普遍加速。

| 输入 | 缓存 | 完整进程 ms：前→后 | 逻辑读 MiB：前→后 | 逻辑写 MiB：前→后 |
| --- | --- | ---: | ---: | ---: |
| 混合资产 | 关闭缓存 | 1000.37→944.59 | 2.266→2.266 | 0.937→0.745 |
| 混合资产 | 冷 | 1010.75→969.61 | 2.425→2.425 | 1.117→0.922 |
| 混合资产 | 热 | 652.69→634.27 | 2.067→1.873 | 0.321→0.321 |
| 混合资产 + 16 MiB Additional | 关闭缓存 | 983.06→952.82 | 18.266→18.266 | 16.938→16.745 |
| 混合资产 + 16 MiB Additional | 冷 | 1017.28→974.94 | 18.425→18.425 | 17.117→16.922 |
| 混合资产 + 16 MiB Additional | 热 | 661.00→641.78 | 18.067→17.873 | 16.321→16.321 |

两种输入的冷态：原输入 FastHash 读取 **39→39**，完整 SHA256 计算 **854→0**（815 次独立 SHA 打点，加上与游戏 FastHash 合并的 39 次；原始 sha256Reads 字段只计独立打点）；热态两者均 **0→0**。Schema 原本就要读取，冷态逻辑读取量没有减少；删除侧车并没有造成额外内容读取。原请求内 metadata.directory 计量都是 32；它不覆盖 CLR 或旧 LegacyCacheIdentity 的直接扫描。

独立跟踪为混合输入 + 16 MiB Additional，每档/实现各一次，文件/目录句柄都计入 CreateFile。侧车包含临时写入路径；0 表示新实现没有任何该类操作。

| 缓存 | CreateFile 调用 | 成功打开 | 完整进程 QueryDirectory | 侧车 ReadFile/WriteFile |
| --- | ---: | ---: | ---: | ---: |
| 冷 | 2774→2651 | 2627→2540 | 141→144 | 0/2→0/0 |
| 热 | 1511→1383 | 1377→1284 | 135→138 | 54/0→0/0 |

实测并发预算与峰值均 **4→4**，每个冷样本都只有 8 次 Texture 的 uncertified-plugin owner 回退，未扩大资格范围。准备时间中位数 **5.2903→7.7944 ms**，完整进程目录查询每档增加 **3**，如实保留；不能宣称所有细分阶段/I/O 都减少。整体冷/热耗时下降，目录边界合并与删除 SHA 收据达到目标，但准备阶段仍有优化余地。

## 必要验收与交接

最终运行的 Debug/Release 应用合同、完整 Win32 产品/Texture、Core 生命周期/后端资源、真实 CLI 取消与必需/可选日志、SessionCache **108×2** 用例、BuildAssetCache/Audio/W3X 载荷、原 BAB 缓存导入、BIG/LoD/Additional/中立修复/SKUDEF 原工具字节、完整未精简 mymod 原 SDK 差分、并行矩阵、56 样本热态编辑/依赖传播和所有报告协议均通过。未发现正确性阻塞问题；上述准备时间和目录查询增量保留为性能问题。

[验收摘要与归档身份](../evidence/refactor/defensive-cleanup/41062149232-1/summary.json)、[完整计量](../evidence/refactor/defensive-cleanup/41062149232-1/measurement.json)。ZIP 已下载复算 SHA256 `a88d26f529934db89bb7fc3c154714fdb72c457f88a870cef4fa740c0fa6c927`，两配置 EXE 与 Texture DLL 均匹配构建记录。

[中间失败](../evidence/refactor/defensive-cleanup/intermediate/failures.json) 和 [a90c0f08 实测](../evidence/refactor/defensive-cleanup/intermediate/a90c0f08-measurement.json) 保留：曾出现 16 MiB Additional 热态 +3.5%、默认小缓冲写入过多、错误的默认值输出假设、旧侧车/必需日志断言与 Procmon 启动/尾部问题；修正后取得本轮完整新成功结果，没有降低原字节、失败/取消和依赖检测断言。

# 历史：直接产物阶段（不代表本轮实现）


基于 `recovery/legacy-sdk-cache` 的 `5c1843ae`，实现分支 `perf/direct-sdk-output`。本轮删除整代 Staging 与 MOD 发布事务；最终验证及同机性能计量见本页下方，早期失败记录保留。

## 当前路径与完成条件

- `OutputRoot/published/<content>/<entry>/streams`：Core 实际四流与 CDATA 输出，也用于下次链接复用。它不是另一个待复制或整体搬走的 Staging。BIG 模式保留这些工作流文件，但最终报告只列本轮 BIG/SKUDEF。
- `OutputRoot/published/*.big`、`*.skudef`：正式 MOD 文件。BigArchiveWriter 在同一最终目录用既有 `.tmp-*` 写入，完成后 `File.Replace` 覆盖目标，无备份文件。一次 BIG 内容写入，无完整 BIG 发布复制。
- `/iod`（默认 `OutputRoot`）下按 Core 相对流路径分开的 `assets/`：原 BAB 的逐流中间资产，例如 `<iod>/mod/assets`，具体层级由数据根决定，供依赖、增量编译和重新链接使用。
- `published/<content>/<entry>/backend-work/<sequence>`：并行后端生成 CDATA 时需要的原有单任务隔离目录；owner 完成后把单个 CDATA 搬到真实流目录，任务目录在完成/取消时删除，不保存整套 MOD。
- `session-cache-directory`，默认 `OutputRoot`：原 `BinaryAssetBuilder.SessionCache.xml[.deflate]`、原序列化的 `.tmp/.old` 和字符串哈希。原 BAB 的单缓存文件临时写入及 `.old` 保留；它们不属于 MOD 发布事务。
- `build-cache-directory`：原 BAB `BuildAssetCache` 原生类型/实例键与 Win32 资产，不引入新包缓存。保留原 `/bc`、`/atc` 行为。
- `<session-root>/.NewModCompiler/cdata`：已有的原生键 CDATA 存储，支持不同输出目录复用共享会话、重建丢失输出和导入原 BAB 缓存。仅新键创建快照，已有键不逐字节比较；缺失当前 CDATA 时按需导入，供 Core 修改的副本独立存储。
- `OutputRoot/.file-identities.json`：仅保留输入与 Schema 的文件身份和必要内容哈希，维持等长/恢复 mtime 编辑的失效能力。已移除发布收据、整代目录拓扑记录、输出 inode 身份缓存。
- `.package-lod`、`.package-versions`，以及显式中立修复需要的 `.package-neutral`：仅保存确实生成的 LoD、APT 三字节版本文件和中立修复输入，打包后删除各自目录。没有共同的每次构建暂存根，没有完整 MOD 复制。Additional 直接读取 SDK/MOD 当前文件，沿原顺序覆盖同名成员。

Core 在流开始时登记所触及的流，在四流链接全部完成时接受它们；CDATA 在实际提交后登记。本轮返回列表不依赖任何输出目录扫描。未选择的流、已移除的引用及任意旧文件不会加入新 BIG。Core 原有资产/CDATA 清理仍限定于实际处理的流；失败仅移除本轮触及流的四文件，不递归清空输出根。

完整四流用原 manifest 的版本、类型哈希和流校验和，以及三文件头校验和复用。组不完整或校验和不一致时重新链接，避免只重建 IMP/RELO 而缺少 BIN 链接阶段填充的缓冲区。成功不再要求 `.linked-streams` 的第二份快照、索引和事务代际目录。

会话在 Core 成功后按原格式保存，`/psc:true` 仍允许失败/取消时保存已完成的有用工作，`/psc:false` 不在失败流的捕获路径保存。冻结、压缩选择、依赖失效、CDATA 导入与原生资产缓存保留。缓存更新不等待日志、终端事件或最终报告成功。

进程仍对编译、打包、取消和诊断错误返回错误码。已写好的 BIG/SKUDEF 不因后续日志、报告、事件失败而撤销。失败构建不会把磁盘上既存文件列成本轮成功结果；消费者应按本次报告/退出码判断成功，不能扫描旧目录推断。

## 删除清单

| 文件/类型 | 删除内容 |
| --- | --- |
| `Application/ArtifactPublisher.cs` | `ArtifactPublisher`、`ArtifactPublication`、`PublicationResult`、`PublicationLock`；Prepare/Commit/Complete/Rollback、`.publish-*`/`.previous-*`、整目录交换、备份、回滚、整树比对、CopyAndHash。 |
| `Application/LinkedOutputCache.cs` | 四流的独立快照树、`.index.json`、`.linked-candidate-*`/`.linked-previous-*`、发布事务和重复输出持久化。实际复用改为 Core 原地四流校验。 |
| `Application/BuildAssetCache.cs` | App 层未接入生产的通用事务字节存储、候选代、收据和完整文件复制/哈希。**保留** `Managed/Core/BinaryAssetBuilder.Core/BuildAssetCache.cs` 原 BAB 实现。 |
| `Application/SessionPackage.cs` | 未接入生产的会话包包装、整包恢复/复制、候选与回滚。保留真实 BAB `SessionCache`。 |
| `Application/CacheInputSnapshot.cs` | 上述未使用包装的整树指纹和扫描。 |
| `BuildSession` | `.build/<BuildId>` 的创建/销毁；正式产物 Prepare/Commit/Complete/Rollback；缓存事务参与者调用；发布/缓存撤销逻辑与相应性能阶段。 |
| `FileHashCache` | PublicationKey/PublicationReceipt、MatchesPublication/RecordPublished、通用收据入口、输出 inode 缓存，以及 FileIdentity.Stamp 和仅供已删除指纹使用的递归枚举。 |
| `LegacyCacheStore` | candidate/previous/installed/backups、Stage/Commit/ConfirmPartial/Complete/Rollback、整树 CDATA 扫描和重复内容比较。使用小型 `CacheLock` 保持原会话目录的进程排他访问，无发布生命周期。 |
| `Artifact` / `StreamContext` / `IBuildCacheCompiler` | 输出 SHA256、虚拟 BackingPath/Verified、StagingDirectory/CacheManagedBySession，以及 CommitCache/RollbackCache/CompleteCache。缓存接口仅报告实际统计。 |
| 打包与协议 | `package.hash`、输出完整文件 SHA256、`publicationUnchanged`、发布回滚状态、Additional 整份快照复制。验证器在外部采集字节身份。 |
| 测试 | 删除 ArtifactPublisherTests、LinkedOutputCacheTests、App BuildAssetCacheTests、SessionPackageTests、SessionCacheTransactionTests、SessionPublicationTests、CacheInputSnapshotTests；删除已不存在扫描器的镜像测试。其余测试按直接产物语义更新，保留原 SDK 字节对照、冷热/编辑、失败/取消与真实锁定错误。 |

旧运行留下的 `.build`、`.linked-streams`、`.publish-*` 等目录不会被新代码创建或复用，也不会为清理它们扫描整个输出根。

## I/O 变化与计量方法

设 BIG 大小为 B、Additional 有效输入总量为 A。以下是生产路径的结构性次数；文件重命名不计作内容写入或复制。原版这里指修改前的新编译器基线 `5c1843ae`，不是 EA 原 BAB。

| 工作 | 修改前 | 修改后 |
| --- | --- | --- |
| 正常 BIG 内容生成 | 1 次 | 1 次，最终目录内临时写入后替换 |
| 完整 BIG 落盘（含发布副本） | 新产物/变化时 2 次，未变热态 1 次 | 始终 1 次 |
| 完整 BIG 发布复制 | 新产物/变化时 1 次，未变热态 0 次 | 0 次 |
| 完整 BIG SHA256 | 每次新打包 1 次（首次检查旧输出可能再读旧 BIG） | 0 次 |
| Additional 完整输入快照 | 每次 1 次，读 A、写 A | 0 次 |
| 四流缓存持久化 | 新结果另建快照；硬链接失败会完整复制 | 0 份额外快照，原地复用 |
| 发布目录事务耗时 | Prepare/Commit/收据/清理 | 该阶段删除，无发布扫描/复制/事务耗时 |

不含初次旧输出检查时，变化 BIG 消除至少 2B+A 的附加逻辑读取和 B+A 的附加内容写入；未变热态消除 B+A 的附加读取和 A 的附加写入。Core 原有输入读取、资产缓存、BIG 从成员读入写出、诊断文件 I/O 仍有其成本。不能把公式当作物理磁盘或完整进程的实测收益。

`MeasureDirectOutput.py` 在同 VM 重建基线/当前 Release，使用完全相同的混合资产输入与可选 16 MiB Additional；无缓存、冷、热各三对，交替执行顺序。控制器比较完整 BIG/SKUDEF 字节，子进程外计算身份；Win32 `GetProcessIoCounters` 记录含运行时加载和诊断的逻辑进程读写量。OS 页缓存未受控，不宣称物理磁盘写入或完整 MOD 的普遍加速。

## 本轮验证

最终生产/测试来源 `784920357d1a268360a082cd3e51b42a5fa69356`，[运行 41049481674](https://github.com/wu162/NewSdk/actions/runs/41049481674)：Debug/Release App、完整 Win32 产品、Core 生命周期、CLI 真实取消/重试和完整 UI fixture、会话与资产缓存、Audio/W3X 载荷、BIG/LoD/Additional/SKUDEF 原工具字节对照、完整未精简 mymod 原 SDK 差分、56 样本热缓存/编辑/修复矩阵及两配置报告协议均已通过。后续仅整理文档和原收据，没有变更生产或测试输入。原始归档和二进制身份核对见[最终收据](../evidence/refactor/direct-output/41049481674-1/summary.json)。

首个实现检查点 `a1f5a059` 的 App 和 Win32 构建通过，但冷输出目录初始化错误使生产用例失败；错误已修复，[失败来源](../evidence/refactor/direct-output/41045720372-1/summary.json) 保留，不把该轮标为通过。

第二轮生产/计量来源 `f6c3c009`，[41046820725](https://github.com/wu162/NewSdk/actions/runs/41046820725)：Core、两类原缓存、Audio/W3X 载荷及完整 mymod 原 SDK 差分两配置通过；CLI 浅克隆 fixture、BIG 旧路径断言和资产缓存恢复 CDATA 的旧发布时间戳断言失败，均已保留并修正测试。完整归档已下载复算，见[原收据与独立通过范围](../evidence/refactor/direct-output/41046820725-1/summary.json)。

同 VM 三对中位数（Release；逻辑进程 I/O，不是物理磁盘；生产源为 f6c3c009，后续仅删除不使用的元数据辅助方法）：

| 输入 | 缓存 | 完整进程 ms：前→后 | 逻辑读 MiB：前→后 | 逻辑写 MiB：前→后 | 发布阶段 ms：前→后 |
| --- | --- | --- | --- | --- | --- |
| 混合资产 | 关闭 | 1075.73→973.54 | 2.98→2.27 | 1.17→0.91 | 19.88→0.00 |
| 混合资产 | 冷 | 1037.40→1039.67 | 3.14→2.42 | 1.35→1.09 | 18.11→0.00 |
| 混合资产 | 热 | 722.14→668.63 | 2.35→2.07 | 0.52→0.32 | 14.05→0.00 |
| 混合资产 + 16 MiB Additional | 关闭 | 1073.48→1018.88 | 66.98→18.27 | 49.17→16.91 | 26.59→0.00 |
| 混合资产 + 16 MiB Additional | 冷 | 1098.70→1028.40 | 67.14→18.42 | 49.35→17.09 | 25.80→0.00 |
| 混合资产 + 16 MiB Additional | 热 | 744.25→694.20 | 50.35→18.07 | 32.52→16.32 | 26.09→0.00 |

36 次均比较完整 BIG/SKUDEF 字节，并从原报告复核了关闭/冷/热的真实缓存计数。小输入冷态完整耗时 1037.40→1039.67 ms（约 +0.22%），如实保留；不能仅凭删除发布阶段推断所有输入都会整体加速。加入 16 MiB Additional 的冷态逻辑读减少约 72.56%、逻辑写减少约 65.37%；它是该合成场景，不代表完整 MOD 的通用比例。完整计量见[原始 JSON](../evidence/refactor/direct-output/41046820725-1/measurement.json)。
