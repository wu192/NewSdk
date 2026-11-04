# 原 SDK 缓存分支：全认证后端并行实现与验收

> 此页为6d39f833旧实现的历史快照。当前5be40ac4已开放GameObject/Weapon/Armor并删除准备阶段深拷贝，且统一验收通过；见[本轮结果](TOKENIZER_PARALLEL_INPUT.md)与[续接入口](../RESUME.md)。

2026-10-30（上海时间）。成果集中在 `recovery/legacy-sdk-cache`。生产源码/二进制来源 **`6d39f833bfbad40e3576b5517e1da685a995a77a`**；性能控制器来源 **`02987cbc41e6d4ba62469c62aa22fc73830b5a28`**；后续提交只归档文档与证据。起点 `096f60db`，参考 `054e8d9a`。未合并其它主分支，未强推，未发布 Release。

## 实际交付

| 范围 | 生产资格与边界 | 新运行证据 |
| --- | --- | --- |
| W3X | W3DAnimation、W3DMesh 全量接线；独占 XML/namespace/reference、native scratch/result；按帧/通道估算内存 | 两配置同类/跨类 worker 三缓冲对照，峰值4；完整 MOD 调用194动画、134 mesh |
| Audio | 精确 PcmWaveAudioCompiler、NONE/XAS 或原默认策略、无显式 PCSampleRate；MP3 passthrough；Win32 只读文件租约及私有 CDATA | 两配置四个 PCM/XAS worker 峰值4、MP3 实际 worker 三缓冲及 CDATA 相同；完整 MOD 有1289次 AudioFile 调用（含预算回退） |
| XML metadata | 原样闭合清单284个 TypeId、62模块族；不是全部 XML 或 XML 前端并行 | Debug/Release 各284×四平台（0/1/2/99）全部成功，实际 worker/owner 三缓冲相同；复杂 environment 和 schema 默认值单独覆盖 |
| APT | AptAptData、AptConstData、AptDatData、AptGeometryData；ASCII native 路径、真实可读输入和稳定只读租约 | 两配置四类 worker 峰值4；缺失/非 ASCII/读冲突/租约变更检查；完整 MOD 调用3/3/3/89次 |
| 调度/生命周期 | 有界 CPU/Win32 物理及可用虚拟内存准入，独立文档在途；发现顺序验证/提交，引用/重复 include/同资产复用/manifest/link 屏障 | 64个实际生产用例；重复文档/相同资产不重复编译；实际 native 故障/取消时3个调用在途，全部安全 join，零失败文档完成 |
| 原 SDK 缓存 | SessionCache、BuildCache、LinkedOutputCache、FileHashCache 与原格式/事务保留；Texture 三路缓存保留 | sc/bc四组合，jobs1/4/auto，csc/psc/fsc/atc 冷热/失败，单改、CtrlBreak、回滚、完整 BIG/SKUDEF/Additional |

资格总表见 [XmlBackendCertification](../src/App/Application/XmlBackendCertification.cs) 与保留原 sourceCommit 的 [原认证审计](XML_BACKEND_REENTRY.json)。组件矩阵覆盖全部284个路由；完整 Upheaval 本身实际使用47种后端，不能说这份 MOD 包含全部284种。其它后端继续 owner 路径，包括 GameObject/Weapon/Armor、Texture/OnDemandTexture/TerrainTextureAtlas、TestTexture、PathMusic、未认证 Audio 条件及未知新增类型。

缓存命中先执行原 GetLocation/Verify/Commit(location)，不调用 TryPrepare、不复制 DOM、不取得新输入租约、不计算动作 Key/额外 SHA256。只有确认未命中才在 owner 解包已知 LazyPlugin 并准备私有输入；版本聚合、热态惰性初始化和原缓存身份不变。jobs 不进入缓存身份。全热态 backendCalls=0、prepareMs=0、xmlParse=0、instanceCompile=0。

Worker 只调用已认证插件的 ProcessInstance。owner 移动私有 CDATA 到原逻辑路径、验证、执行原 BinaryAsset.Commit(location)、更新原计数/LastWrittenAsset/文档状态。BinaryAsset 最终没有源码差异。按同资产在途序列汇合后才能替换 BinaryAsset.Instance。失败先 join 所有 native 调用，再保存已完成的部分会话，最后释放 Core/插件/Settings；第一原异常保留。没有围绕 ProcessInstance 的全局锁。

没有新增 ActionAssetCache、ActionKey、result.bin、read-closure 扫描、CacheDependencyRecorder 或 SessionPackage/BuildAssetCache 替代体系；只有构建 staging 下短期 CDATA 工作目录。已有原缓存/打包/未变发布优化保留，BIG/SKUDEF 每次正常打包。

## 验证来源与终态

- **功能：[40915444929](https://github.com/wu162/NewSdk/actions/runs/40915444929)**，生产与功能测试来源6d39f833。Debug/Release 构建、全部组件/生命周期、64个真实并行/缓存用例、原 BAB 差分、真实 CtrlBreak、Texture、缓存冷/热/失效/恢复、BIG/MakeBig/Additional、**完整未裁剪 mymod 原 SDK 差分与五文件包**的步骤全部 success。该运行整体为 failure：唯一失败是独立性能控制器把 FrameRate 写成31.0；不能把整个运行标为 success。
- **最终性能：[40920804580](https://github.com/wu162/NewSdk/actions/runs/40920804580)**，整体 **success**。测试控制器02987cbc，仅修正整数 FrameRate 和测量归档；严格校验上述功能门槛、生产/SDK/构建输入未变、归档 digest、EXE 与 host-bound Texture 身份，复用原6d39f833五文件，**没有重新编译 Native**。22个样本全部通过。
- 功能归档 `12916669037` SHA256 `74060c4774d760ac8684479d119457f329e14df815336746d4610487aea66b5a`；性能归档 `12919126300` SHA256 `3c599194e217008d28cfac4ab7810b16479b700662be7dfe3332a8e73fa5f91b`。**两份都已实际下载复算匹配**。五文件 Release ZIP SHA256 `4729239a1bbf99c68806d4d187a14d95c5d7e1592bfbed0f98eb0afe0849b4f3`，其中五个文件逐一与性能使用的运行时身份相同。
- [功能摘要](../evidence/refactor/backend-parallel/40915444929-1/summary.json)、[原功能组收据](../evidence/refactor/backend-parallel/40915444929-1/receipts)、[性能/完整调用清单](../evidence/refactor/backend-parallel-performance/40920804580-1/summary.json)、[22样本原始数据](../evidence/refactor/backend-parallel-performance/40920804580-1/measurement.json)、[CSV](../evidence/refactor/backend-parallel-performance/40920804580-1/samples.csv)、[二进制复用收据](../evidence/refactor/backend-parallel-performance/40920804580-1/reuse.json)。完整输出、日志和二进制保留在上述已核验 Actions ZIP。

组件由与生产相同模块链接的独立验证宿主执行；生产 CLI 的真实 worker 另由64用例和完整 MOD 数据证明。Linux Roslyn Foundation/独立 App 编译、7份 fixture 的50个原 SDK定义及真实 FrameRate 修改的44个原 SDK定义校验，只是预检，不代替 Windows 证据。

## 同 VM 完整 Upheaval A/B

未裁剪历史树 `5551fc26a6f96b62613443e0d0fa2958c14cbce0`：2,723文件、300,900,885字节、5流（base、3APT、global metadata），默认完整 BIG/SKUDEF 与 Additional。每冷态3354次实际后端调用、925份 XML；**XML 前端仍 owner**。吞吐比较统一 performance off；detailed 另用于归因。22进程测量/比较总用时587.48秒，不含生产构建和输入准备。

| jobs | 无缓存冷态秒 | 默认缓存冷态秒 | 默认热态中位秒 | 单动画修改秒 | detailed真实峰值 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 78.095 | 74.096 | 2.898 | 3.116 | 1 |
| 4 | 47.084 | 49.165 | 2.918 | 3.153 | 4 |
| auto | 47.192 | 48.580 | 2.908 | 3.141 | 4 |

默认缓存冷态，jobs4 比 jobs1 减少 **33.6%**，auto 减少 **34.4%**；无缓存冷态分别减少 **39.7% / 39.6%**。热态每档两个 off 样本；约10–20ms差异不能作可重复回退结论。detailed 热态三档都零解析、零后端调用、零并行准备。原日志中的1859条默认外部引用等警告保留，**全部22样本0错误，警告数一致**。

| jobs（detailed、无缓存冷态） | TryPrepare秒 | 有序等待秒 | 后端累计秒 | owner验证/提交秒 | 采样private MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 0.000 | 0.000 | 48.458 | 1.095 | 259.0 |
| 4 | 4.561 | 11.391 | 59.954 | 1.527 | 311.1 |
| auto | 4.710 | 11.174 | 59.804 | 1.502 | 307.2 |

这些累计区间可重叠，不能相加解释进程 wall；detailed 的秒数不与 off 直接归因比较。prepareMs 覆盖 TryPrepare（独占输入、租约与其内部预算判断）；前置廉价资格/初步大小估计及资源采样归 owner Core 范围。workerWaitMs 是有序结果等待，失败清理 join 属于 Core cleanup。采样 private 不冒充 OS 连续峰值，区间最多512条。

jobs4/auto 各有358次未认证插件 owner 兼容调用、10次大任务预算回退；实际3354次调用中其余2986次由 worker执行。effective/minimum=4/4，真实峰值4，资源查询成功；未知类型没有擅自放开。按类型全部调用计数见性能摘要。

**原始字节**：18个原输入样本的完整 BIG/SKUDEF 逐文件 raw compare 与独立报告哈希都一致。真实单动画 FrameRate30→31 后，jobs1/4/auto 各只重编译1资产、解析1文档；与独立 sc/bc关闭 owner 对照的全部文件相同。只有 `upheaval_1.0.big` 内容改变，SKUDEF不变。该独立对照保留已验证的未变预编译/链接输出，因此执行1292次后端调用；它是改变流的独立串行重建，**不是新输出根的3354次全冷编译**。

## 使用与剩余边界

`--jobs auto|N` 默认 auto；1 是 owner 串行，整数是上限。三个 profile、plan/build、项目JSON均支持，默认 `/sc /bc /csc /psc=true`、`/fsc /atc=false` 和 `/ss /pc /slowclean=true` 保持。[参数](../COMPILER_USAGE.md)、[协议与计量](PERFORMANCE.md)。

没有已知阻塞本次迁移的剩余问题。未认证后端和资源不足回退仍串行；10个大任务回退是本次实际观测。Win32 内存准入依赖保守估算/可用空间，未做硬内存预留。冷态每档一次、热态两次，OS页缓存未控制，性能数字只描述此VM/输入。未做游戏内验收，也未新增完整 Upheaval 原 DLL 差分；本轮完整原 DLL/SDK 差分对象是mymod，大型 MOD 使用同二进制串并行及增量原字节对照。

## 提交与源码清单

`d9de5f69` 初始全范围实现；`eb35dc6b` 统一矩阵；`ca81d41f` 资源恢复/安全汇合；`15b26f4f` 原夹具修正；`3d655274` 惰性包装器生产接线；**`6d39f833` 最终生产源码**（同资产复用、owner提交、实际计量、原构造夹具修正）；**`02987cbc` 独立测量控制器**。最终分支头还含本页/证据归档提交，源码与6d39f833相同。

先前40907675933、40912075609、40912889949均保留失败终态：mesh夹具、两个旧反射构造点先后修复；首批惰性包装器曾导致全部生产调用降串行，已经用真实 worker/重叠断言修复验证。它们不是最终成功证据，未通过取消或关闭检查掩盖。

实际生产源文件（相对起点）：

- `recovery/src/App/Application/BackendResourcePolicy.cs`
- `recovery/src/App/Application/BuiltinCompilers.cs`
- `recovery/src/App/Application/CoreStreamCompiler.cs`
- `recovery/src/App/Application/Model.cs`
- `recovery/src/App/Application/ParallelBackendScheduler.cs`
- `recovery/src/App/Application/PreparedBackendWork.cs`
- `recovery/src/App/Application/XmlBackendCertification.cs`
- `recovery/src/App/Cli/Options.cs`
- `recovery/src/App/Diagnostics/BuildPerformance.cs`
- `recovery/src/Managed/Audio/Plugin.cs`
- `recovery/src/Managed/Core/BinaryAssetBuilder.Core/AssetDeclarationDocument.cs`
- `recovery/src/Managed/Core/BinaryAssetBuilder.Core/DocumentProcessor.cs`
- `recovery/src/Managed/Core/BinaryAssetBuilder.Core/IAssetExecution.cs`
- `recovery/src/Managed/Core/BinaryAssetBuilder.Core/OutputManager.cs`

App/Managed Foundation 的现有通配构建纳入新文件；Native/Bridge 业务源码、原缓存存储格式和 BuildSession发布事务没有替换。
