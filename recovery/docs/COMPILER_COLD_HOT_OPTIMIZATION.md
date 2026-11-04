# 编译器冷、热缓存性能优化

本轮从 `recovery/legacy-sdk-cache` / `665468cb` 拉出 `perf/compiler-cold-hot-cache`，范围是编译器冷缓存与热缓存的主要重复工作。第一轮生产源码 `4d732a14` 已完成验收与同机测量。深入分析后继续实现第二组，第二组 `d3b3150f` 也已完整验收。远端同期合入FLA支持后，已同步 `074bf768`，最终组合源码为 `e2dba3c321504752438b4e940d651bbfcdb58fff`，已通过含真实FLA/AS的完整组合验收；最新UI基线的最终同机吞吐已取得，原始MOD全通过；扩容正式吞吐完整、追加detailed因预算终止。第一轮数据单独保留。

## 最终组合实测（最新UI基线）

基线074bf768/二进制75d29895与组合e2dba3c3，同机jobs4、off正式计时；冷n=3、热n=6。原始MOD [41171620746](https://github.com/wu162/NewSdk/actions/runs/41171620746) 24/24样本通过，[完整原始样本](../evidence/refactor/cache-paired/41171620746-1/measurement.json)、[归档身份](../evidence/refactor/cache-paired/41171620746-1/archive.json)已下载复算。

| 负载 | 模式 | 基线中位秒 | 组合中位秒 | 耗时减少 |
| --- | --- | ---: | ---: | ---: |
| 完整原始Upheaval | 冷态 | 36.723 | 33.501 | 8.78% |
| 完整原始Upheaval | 热态 | 2.273 | 1.691 | 25.63% |
| 完整Upheaval+49,152 Armor | 冷态 | 70.806 | 61.259 | 13.48% |
| 完整Upheaval+49,152 Armor | 热态 | 10.099 | 6.594 | 34.71% |

扩容[41171623163](https://github.com/wu162/NewSdk/actions/runs/41171623163)完成所有18个默认off正式样本与2个无缓存补充样本，发布原字节均一致；随后基线detailed冷态因剩余43.12秒预算超时，总720.03秒，运行整体failed、候选detailed与最终输入库存复查未执行。未降门槛或把整组标passed；[原计量](../evidence/refactor/cache-paired/41171623163-1/measurement.json)、[分范围审核](../evidence/refactor/cache-paired/41171623163-1/review.json)、[归档身份](../evidence/refactor/cache-paired/41171623163-1/archive.json)保留完整失败。上表扩容只引用已全部完成的off正式吞吐，输入按脚本只读合同保持同一树，不冒称补齐详细归因。另有无缓存冷单样本34.411→32.182秒，不混默认中位数。首个基线冷40.997秒及其他较慢样本保留；OS页缓存未控，有限样本不作通用保证。

## 优先级与实现

| 优先级 | 问题与改动 | 保留的行为 |
| --- | --- | --- |
| 首要：冷态 XML 校验 | 每份 XML 不再把已编译的共享 `XmlSchemaSet` 合并进另一个集合，改为直接绑定本次请求的共享 Schema 快照，避免重复合并/编译。 | 每份 XML 仍执行完整 XSD 校验；非法输入仍在插件派发前拒绝。Schema 身份及变化失效继续生效。 |
| 首要：多流会话加载 | 一条流成功保存会话后，同次请求的后续流复用已保存的 `SessionCache` 对象，避免反复解压、解析整个会话文件。每流仍调用 `InitializeCache`。 | 只复用成功保存状态；冻结、失败、部分保存及请求结束均不把未完成的内存状态交给下一流。文件、Schema、处理器版本及依赖检查保留。 |
| 次要：资产与实例查询 | `KeyedCollection2` 的查询索引改为哈希字典；`OutputManager` 的资产、旧资产及基础流身份查询改用 Ordinal 哈希索引。 | 集合遍历继续使用独立 `Items`；实际输出仍经 `OutputInstances` 与原排序规则生成。完整资产身份用于相等判断，哈希碰撞不合并不同身份。 |

会话对象复用必须重现反序列化后的瞬态状态。`AssetDeclarationDocument.ResetState` 同时清理保存实例的当前状态、Define 的原始表达式/所属文档以及 Include 的文档指针；已持久化的 Define 计算值保留，实例重新通过原有惰性初始化恢复。这样不会把上一流的已解析引用、XML 或 CDATA 输出路径带入下一流。

生产入口分别为 [XML 校验与实例输出](../src/Managed/Core/BinaryAssetBuilder.Core/AssetDeclarationDocument.cs)、[请求内会话复用](../src/App/Application/CoreStreamCompiler.cs)、[实例索引](../src/Managed/Core/BinaryAssetBuilder.Core/KeyedCollection2.cs)及[资产索引](../src/Managed/Core/BinaryAssetBuilder.Core/OutputManager.cs)。本轮没有改写排序算法，也未恢复 BIG/SKUDEF 包缓存；包仍每次正常生成。

## 历史证据的使用范围

已实际下载并复算当前生产基线 [41152199159](https://github.com/wu162/NewSdk/actions/runs/41152199159) 的归档，SHA256 为 `2ba381a049201eacf2de5ba6668e9f6061d81ccdf1eb878bda3af8d8d593138b`，五个 Release 文件也与原收据一致。[完整 Upheaval、jobs4 冷态 detailed](../evidence/refactor/cache-paired/baseline-41152199159/diagnostic-cold-4.json)中，报告总耗时 34.718 秒，925 份 XML/3354 次编译，base `core.xml.validate` 自耗时 **14.064 秒**（910 份文档）；GameObject 插件累计仅 0.302 秒。Audio/Animation 后端累计工作分别为 17.089/12.636 秒，可与前端或其他 worker 重叠，不能相加为总耗时。这份最近证据比旧 13.48 秒记录更直接支持本轮的冷态优先级，但仍不作为跨 VM 收益对照。

同一归档的[纯热态 detailed](../evidence/refactor/cache-paired/baseline-41152199159/defaults-hot-diagnostic-4.json)报告总耗时 2.210 秒，零解析/编译；五流 `cache.load` 合计约 0.812 秒，base 引用阶段约 0.307 秒。它说明原始完整 MOD 与下面 L8 扩容负载的规模差异，不将两者绝对值混用。

[L8 41139062989](https://github.com/wu162/NewSdk/actions/runs/41139062989) 的三个 `detailed` 样本是 **C08/C09/C12 修改后的增量构建**。其五流 `cache.load` 合计约 2.9–3.0 秒，base 的 `core.output.references` 约 2.46–2.58 秒，缓存文档重初始化 self 约 1.18–1.27 秒。计时来自旧二进制 `cc10391b`、扩容负载和另一台 VM；它们支持调查方向，不能解释为纯热态的阶段拆分，也不能与本轮结果直接计算收益。原始数据见 [L8 phases.csv](../evidence/refactor/l8-hot-cache/41139062989-1/phases.csv)及[负载/采样说明](HOT_CACHE_BENCHMARK.md)。

本轮新增批量计时，将 XML 校验分为 Schema 绑定、实际 XSD 校验、实例校验及文档哈希，将输出引用阶段分为引用闭包、资产可用性及排序。保留原上层计时，不把嵌套阶段相加当作总耗时；正常吞吐不逐资产计时。

## 同机测量方法

[已登记的 Benchmark 入口](../../.github/workflows/compiler-cache-benchmark.yml)在指定 `candidate_run_id` 后调用[配对工作流](../../.github/workflows/compiler-cache-paired.yml)和 [MeasureCompilerCachePaired.py](../scripts/MeasureCompilerCachePaired.py)，只有指定源码的 Core lifecycle 运行完整成功后才接受其候选 Release。第一轮基线复用 [41152199159](https://github.com/wu162/NewSdk/actions/runs/41152199159) 的已验收 Release，二进制源码为 `1a3e4bc6`；脚本核对它与本轮起点 `665468cb` 的生产输入闭包未变，并固定校验五个运行文件身份。

最终组合改用已验收UI基线 `75d29895` / [41165126334](https://github.com/wu162/NewSdk/actions/runs/41165126334)，与 `074bf768` 的SDK/Core/build/ui/恢复脚本闭包相同；[原归档、十运行文件与两个ZIP](../evidence/refactor/cache-paired/baseline-41165126334/archive.json)已重新下载复算。这样最终对照只反映优化差异，不混入同期FLA功能。旧665基线的历史结果单独保留。

- 每组配对在同一 Windows VM、同一 SDK、同一份完整历史 Upheaval 输入上固定 `--jobs 4`；默认不扩容。可另跑可达 Armor 分区扩容，但各负载分别报告。
- 默认三组独立冷态配对，交替基线/候选先后顺序；每个全新输出/编译器缓存目录之后各跑两个全新热态进程。两个热态样本共用同一冷态命名空间，并非独立冷态重复。
- 正式中位数只包含 `--performance off`：每实现冷态 n=3、热态 n=6。另有每实现一次双缓存关闭冷态，以及单独的 `detailed` 冷/热归因，均不混入正式中位数。
- 冷态指编译器缓存与输出命名空间初始不存在；不控制操作系统页缓存。同一冷进程的后续流可以复用此前已完成流的文档。
- 每次进程计时后比较全部实际发布文件原始字节；热态还要求 XML 解析和后端编译为零、缓存文档复用为正。记录外部 wall、报告 elapsed、诊断数、缓存计数、机器与运行文件身份。
- 工作流最长 15 分钟，测量脚本预算 720 秒、单进程最多 180 秒；超时与部分数据保留。小样本仅描述该机器和负载，不作通用速度承诺。

性能配对不替代生产验收；生命周期、非法 XSD、保存会话重入、碰撞/顺序、冷热/失效/冻结/取消、完整 mymod 原版字节及五文件包等仍以本轮 Windows 实际收据为准。

## 第一轮同机实测

第一轮生产 `4d732a14`，计量脚本 `d64f2dcc`。两组各24/24进程成功，发布文件原字节全部一致，归档已下载复算，基线与候选五文件身份在原计量中核对。正式吞吐只用 off，中位数按每实现冷态n=3、热态n=6；两次热态共享一个冷态命名空间。

| 负载 | 模式 | 基线中位秒 | 第一轮中位秒 | 耗时减少 |
| --- | --- | ---: | ---: | ---: |
| 完整原始 Upheaval | 默认冷态 | 35.943 | 34.804 | 3.17% |
| 完整原始 Upheaval | 默认热态 | 2.217 | 1.737 | 21.63% |
| 完整 Upheaval +49,152 Armor | 默认冷态 | 52.163 | 48.445 | 7.13% |
| 完整 Upheaval +49,152 Armor | 默认热态 | 7.649 | 5.050 | 33.98% |

原始 MOD 为954份声明文档/3852资产声明、300,900,885输入字节；扩容为2491文档/53004声明、304,923,770字节。库存声明数不等于实际编译次数。每组各有一个无缓存冷样本：原始34.236→33.393秒、扩容48.377→43.721秒；n=1仅作补充，未混入默认中位数。

- 原始 MOD：[运行41167387701](https://github.com/wu162/NewSdk/actions/runs/41167387701)，[完整计量](../evidence/refactor/cache-paired/41167387701-1/measurement.json)、[归档/来源](../evidence/refactor/cache-paired/41167387701-1/archive.json)，实际测量435.94秒。
- 扩容：[运行41167389790](https://github.com/wu162/NewSdk/actions/runs/41167389790)，[完整计量](../evidence/refactor/cache-paired/41167389790-1/measurement.json)、[归档/来源](../evidence/refactor/cache-paired/41167389790-1/archive.json)，实际测量640.94秒。

首个原始基线冷样本41.772秒、首个扩容基线59.505秒均保留；未用它们计算夸大的单点收益。输入只读、编译器冷缓存初始为空，OS页缓存未控；不从两台不同VM的两组结果计算互相的速度差。扩容属于本轮配对负载，不是L8 Common的重新验收。

同机原始 MOD 的独立 detailed 样本：热态 `cache.load` 合计743.64→362.27ms，引用总段287.39→273.08ms；冷态 XML 校验总段14457.73→13384.65ms。后者拆为实际XSD5660.78ms、实例校验7673.63ms，其中大量XML哈希/引用重复工作仍在，故第二组优先针对它，而不继续放大约100ms排序段。

## 热缓存后的编辑：参考文档与定向诊断

用户上传的2026-10-30调查固定于旧 `5c1843ae`。本轮已[逐项对照当前源码](HOT_CACHE_REFERENCE_RECONCILIATION.md)：文档前端与资产后端粒度的区分仍成立；旧运行身份封包已移除，每流重新读取会话也已由本轮改为请求内复用。不会据旧说明恢复全树SHA，也没有简单删除instance整父文件依赖。

第一轮[增量诊断41168768320](https://github.com/wu162/NewSdk/actions/runs/41168768320) 32/32进程成功，[原始计量](../evidence/refactor/cache-paired/41168768320-1/measurement.json)及[归档来源](../evidence/refactor/cache-paired/41168768320-1/archive.json)已下载复算。输入为37文档、515个Armor声明，512个主体资产加父/兄弟/子；各事件只做一轮detailed配对，下面是诊断单次观察，不是off吞吐中位数。

| 修改场景 | 基线毫秒 | 第一轮毫秒 | 两方解析/编译次数 |
| --- | ---: | ---: | --- |
| 256声明文档只改单个资产 | 526.28 | 506.67 | 1 / 1 |
| 实际继承源字段 | 525.92 | 514.39 | 2 / 2 |
| instance父无关兄弟 | 516.67 | 513.99 | 2 / 1；发布字节不变 |
| 已使用Define | 534.82 | 523.16 | 3 / 2 |
| 仅丢会话元数据 | 552.50 | 519.75 | 37 / 0；发布字节不变 |

所有修改都保持输入字节长度与mtime；各代两方发布原字节一致，修改后重新变热为0解析/0编译。较慢的候选热样本也保留，例如首场hot_before为464.57ms、基线444.80ms；不能由这批n=1短样本宣称所有编辑都稳定提速。第二组将重新测这组，并补未使用Define，以确认已有的精确使用集合，而非将全局Define文件任何变化当成全量资产失效。

## 最终组合的六类增量诊断

[41171625562](https://github.com/wu162/NewSdk/actions/runs/41171625562)在最新UI基线75d29895与组合e2dba3c3上完成38/38新进程，[原始计量与全部样本](../evidence/refactor/cache-paired/41171625562-1/measurement.json)、[原归档身份](../evidence/refactor/cache-paired/41171625562-1/archive.json)已下载核对。相同37文档/515个Armor输入增加一个未使用Define；这是每事件n=1的detailed诊断，不能与上轮或off吞吐横比。

| 修改 | 基线/组合毫秒 | 两方解析/编译 | 结果 |
| --- | ---: | --- | --- |
| 大文档只改单资产 | 635.87 / 602.43 | 1 / 1 | 当前代原字节一致 |
| 有效继承字段 | 612.67 / 604.24 | 2 / 2 | 当前代原字节一致 |
| 父无关兄弟 | 596.10 / 580.85 | 2 / 1 | 发布内容不变 |
| 已使用Define | 613.97 / 601.06 | 3 / 2 | 当前代原字节一致 |
| 未使用Define | 545.42 / 547.13 | 1 / 0 | 仅定义文档重载，发布内容不变 |
| 仅丢会话元数据 | 639.76 / 595.40 | 37 / 0 | 发布内容不变 |

全部修改保持等长及mtime，再热均0解析/0编译。未使用Define样本稍慢，继承场景hot_after组合539.79ms、基线509.16ms也原样保留。结果证明当前失效与资产复用合同，不能用这些短n=1样本给出稳定编辑加速保证。

## 验收和第二组状态

| 项目 | 当前状态 |
| --- | --- |
| 第一轮 Windows 全门槛 | [41165490728](https://github.com/wu162/NewSdk/actions/runs/41165490728) success；D/R、Core/CLI/缓存/SDK/ZIP与22个完整Upheaval样本通过；[归档及十运行文件/两个ZIP身份](../evidence/refactor/cache-paired/acceptance-41165490728/archive.json)已下载核对 |
| 较早检查点 | [41165381586](https://github.com/wu162/NewSdk/actions/runs/41165381586) success；随后补齐会话瞬态重置及专门重入回归，最终验收使用后一运行 |
| 第二组源码 | `d3b3150fba886708cf3e34fcce3ec55d145e27a8`：512字符XML哈希缓冲、Schema事实预分类、删除纯写FileConfigs |
| 第二组 Windows | [41167799236](https://github.com/wu162/NewSdk/actions/runs/41167799236) success；[原归档/十文件/两ZIP](../evidence/refactor/cache-paired/acceptance-41167799236/archive.json)重新下载核对 |
| 最新分支组合 | 已同步FLA基线074bf768；组合e2dba3c3，[41169755141](https://github.com/wu162/NewSdk/actions/runs/41169755141)全success，含真实FLA/AS；[归档/十文件/两ZIP](../evidence/refactor/cache-paired/acceptance-41169755141/archive.json)已下载复算 |
| 最终组合吞吐 | 原始41171620746全通过；扩容41171623163正式off全部完成、额外detailed预算超时，运行failed保留；以上表为准 |

[深入源码分析](COMPILER_CACHE_SOURCE_ANALYSIS.md)记录新打点、优先级、已实施和暂缓方案、兼容边界及后续验证。失败/旧运行与所有较慢样本保留。未经用户明确批准不得合并，禁止强推。
