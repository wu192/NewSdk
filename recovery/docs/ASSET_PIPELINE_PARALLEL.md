# 资产全流程并行

实现分支：`feat/asset-pipeline-parallel`。方案与源码基线：
`1810fff87727b70779f31328740bb0c2133157d2`（`recovery/legacy-sdk-cache`）。

正式生产源码 `12c82011676d06047df4368b3c9058104eeffa34`，
[Windows 41204304969](https://github.com/wu162/NewSdk/actions/runs/41204304969)
整体成功：Debug/Release 新构建、Core/CLI/取消、缓存与会话、原 SDK、BIG、
两份三文件 ZIP 及完整 Upheaval 均已通过。[原始归档和运行文件核验](../evidence/refactor/asset-pipeline/41204304969-1/summary.json)
已完成，
[同机旧新配对 41206438783](https://github.com/wu162/NewSdk/actions/runs/41206438783)
也已成功：24/24 样本、1313 个发布文件原字节一致，正式默认冷态中位耗时减少
10.93%，热态增加 0.92%。[配对身份与原始计量](../evidence/refactor/asset-pipeline/paired-41206438783-1/summary.json)。

本地 Roslyn 和 Linux 合成行为检查仅为提前预检；正式结论使用上述 Windows
原生构建和真实 SDK 结果。

## 执行与所有权

- Owner 完成 Include、表达式、继承和 XSD，提取 Schema 的惰性元数据。
  每个资产的原始 XML Hash、引用解析及引用尾 Hash 进入纯计算任务；同一文档的
  只读批次全部结束后，Owner 按原次序改写 DOM、登记字符串 Hash 和文件依赖。
- `HashingWriter` 继续使用原 512 字符分组和 ANSI 算法；引用尾仍 Hash
  `MemoryStream.GetBuffer()` 的容量范围。没有整文档 XML 深拷贝。
- `OutputManager` 以完整 FileBase 保持唯一 `BinaryAsset`；探测对象仅在首次
  注册前绑定实际声明，注册后不替换。调度器保留物理任务，重复请求各自验证、
  订阅完成，只编码、写入、计数一次。
- 已认证的验证器、插件和独立文件 IO 在 Worker 执行。验证在编码之前，
  线程本地诊断和纹理消息由 Owner 在原文档上下文回放。未知验证器/插件留在
  Owner 兼容路径；FLA 已展开缓冲直接交给独立 IO，不重新编码。
- `PrepareCommit` 在 Owner 捕获路径和状态；`BinaryAssetCommitWork.ExecuteIO`
  写入私有结果；`PublishCommit` 在 Owner 发布 Header、availability 和自定义
  输出回调。复制缓冲由工作私有持有，没有全局复制锁。
- 外部输入租约和私有 `.cdata` 持有到本资产 IO 结束。共享 BuildCache 文件先
  写入唯一临时文件，再发布最终名称，避免不同输出流看到半文件。

## 调度和文档版本

CPU、IO 和在途内存分别计量。完成的编码任务立即释放 CPU 名额，结果缓冲仍
计入内存；缓存物理 IO 不占编码名额。Owner 只收割已完成任务，资源不足时等待
能释放相应资源的任意任务，没有 FIFO 队头或 committedSequence。

每个文档版本、每个输出流维护独立订阅和只读输入引用。稳定 Include 复用已
发布前端；需要重新加载时创建新的文档和 DOM，SessionCache 的请求内替换不改
磁盘格式。旧版本完成时不能覆盖新版本。只有相关工作成功，Owner 才能执行
MakeComplete、MakeCacheable、缓存保存和 Reset。

输出屏障等待对应 OutputManager 的全部订阅。引用闭包、排序、Manifest、
LinkStream 和 BIG/SKUDEF 沿用最终输出阶段及原格式；失败回收所有已启动任务，
不为未完成版本写入成功缓存，不新增全流程回滚事务。

## 验证与计量

关键测试覆盖慢首任务与快后任务、同 FileBase 独立验证和唯一物理工作、CPU 满载
时缓存 IO、文档引用计数、失败/取消后的任务与租约回收，以及不同命名空间的
并发验证和 Owner 诊断回放。原 Core、CLI、缓存、SDK 和取消门槛继续保留。

统一 Windows 入口为 `refactor-core-lifecycle.yml`，最终代码提交标记
`[refactor-core-lifecycle]`；该运行新构建 Debug/Release 并执行既有完整验证。
通过后复用候选二进制，对基线验收运行 `41192817627` 做完整 Upheaval 冷/热
原字节与耗时配对比较。来源以实际 sourceCommit/runId 为准。

报告在 `cache.parallel` 中分别输出 XML 前端/纯计算、验证、编码、资产 IO、
Owner 确认、CPU/内存资源等待及 IO 排队耗时，另含 CPU/IO/Owner 待收割和在途
结果字节峰值。各 Worker 累计耗时可重叠，不作为端到端墙钟耗时相加。
`xmlFrontendMs` 是纯计算批次的墙钟时间（包含资源等待），Owner 的 XML 准备与
应用分别见 `core.xml.instance.prepare`、`core.xml.instance.references`，整个 XML
验证见 `core.xml.validate`；`frontendComputeMs` 为 Worker 累计计算时间。
在途字节使用准备阶段的工作集估算与实际缓冲大小计量预算，`sampledPeakPrivateBytes`
另记录进程私有内存采样峰值，预算峰值不等同于实测内存占用。
`core.asset.commit` 在新流程记录 Owner 的状态确认，文件 IO 见 `assetIoMs` 和
`core.asset.io`；旧流程该阶段包含同步 IO，因此不能直接用该单阶段的前后差值
声称提速。跨版本比较以完整进程计量和原字节输出为准。
正式吞吐和分段计量见文末；不将不同计量边界或累计 Worker 时间当作墙钟收益。

## 首次正式运行及修复

候选 `408458b8` 的 [Windows 41201449133](https://github.com/wu162/NewSdk/actions/runs/41201449133)
失败，保留 [归档身份和逐组结果](../evidence/refactor/asset-pipeline/41201449133-1/summary.json)。
Debug/Release 新原生构建、Prepared 工作/去重/完成顺序、验证器并发诊断、真实 CLI/
CtrlBreak、构建缓存、输入根原 BAB 差分、音频/W3X 缓存载荷及纹理检查已通过；
不能据此将完整运行标为通过。

集中修复：等待集合保留已完成但尚未 Owner 回收的任务，消除完成时机竞态；
共享物理任务还须等待首次声明的验证依赖；缓存保存回调抛错时仍恢复三组遍历栈；
异步 Owner 确认补回原 `core.asset.commit` 计量。XML 批次测试改为检查私有声明/
文档视图及借用的原 Node/Handle，保留原字节断言。另修正同 Processor 失败重试的
旧版本记录，以及 BasePatchStream 的 AlwaysTouchCache 分支。

首轮未到达完整 mymod 原 SDK、最终三文件 ZIP 和完整 Upheaval 22 组，修复后必须
取得新候选的完整成功结果。新增旧版本回调测试也因前面的保存失败用例提前失败而
未执行，不计为正式通过。

修复候选 `44b5fd01` 的 [Windows 41202545747](https://github.com/wu162/NewSdk/actions/runs/41202545747)
通过上述首轮失败组，新增版本回调用例也已在 Debug/Release 正式执行；仅原有
`original-partial-retry` 会话用例失败。重试产物与禁用缓存对照原字节相同，但
前一个有效 Include 尚未 Owner 确认就因后续 XML 错误被 Dispose，导致部分会话
没有可复用的完成文档。[第二轮身份与范围](../evidence/refactor/asset-pipeline/41202545747-1/summary.json)。

修复仅在原有 `psc:true`、启用会话缓存且非冻结/取消时，先收尾已闭合且未失败
文档的既派发工作，再保存部分会话。失败根文档不得完成缓存回调，未开始且不被
成功文档依赖的 IO 不再分发；没有新编译注册、Manifest 或 BIG。`psc:false`、
冻结及取消保留既有清理，原部分保存和重试断言保持不变。

## 最终正式回归

`12c82011` / `41204304969` 的完整新运行成功；只复用未修改的底层组件矩阵，
本次流程、原生组合构建及真实集成门槛均重新执行。

| 范围 | 新结果 |
| --- | --- |
| Debug/Release 完整构建、Core、后端、应用、协议和取消 | 通过 |
| 并行/缓存/jobs、输入根、音频/W3X 载荷、FLA 与纹理 | 通过 |
| 原会话缓存矩阵 | Debug/Release 各 108 用例通过 |
| 部分保存重试 | 两配置均解析 2、编译 0、复用文档 1，原字节相同 |
| 完整 mymod 原 BAB/SDK、BIG/SKUDEF | 通过 |
| Debug/Release 三文件 ZIP | 内容与本次运行文件逐字节匹配，部署通过 |
| 完整 Upheaval jobs1/4/auto、冷/热/修改场景 | 22/22 样本成功，原字节一致 |

完整归档仅下载并复算一次，SHA256
`a7772baa773a037a85935044f2c0479d2164658f16c892f29e1219a298987f5b`
与 GitHub digest 一致。两配置 EXE/config/ANTLR、两个 ZIP 及 build/foundation/link
来源收据已核对；未归档的原始链接对象不计为已复算。逐样本真实耗时、编译数与
分段指标在正式摘要中保留；旧新性能结论以下一节同机配对为准。

## 同机旧新配对结果

[41206438783](https://github.com/wu162/NewSdk/actions/runs/41206438783) 复用上述
候选 Release，与基线 `1810fff` 的已验收二进制 `b385337e` / `41192817627` 比较。
候选三文件 SHA 与正式验收完全一致；同一 Windows VM、同一份未扩容完整
Upheaval，固定 jobs4，24/24 样本成功，1313 个发布文件共 156,625,850 字节原字节相同。

| 模式/阶段 | 每实现样本数 | 基线中位秒 | 候选中位秒 | 候选耗时变化 |
| --- | ---: | ---: | ---: | ---: |
| 默认缓存，冷态，performance off | 3 | 26.869635 | 23.931469 | 减少 10.93% |
| 默认缓存，热态，performance off | 6 | 1.368179 | 1.380744 | 增加 0.92% |
| 双缓存关闭，冷态补充样本 | 1 | 24.764817 | 22.850944 | 减少 7.73%，仅描述 |

冷态默认 CPU 时间中位数从 46.531 秒增加到 48.453 秒；墙钟耗时减少并不代表
CPU 工作量减少。热态的小幅回退与所有原始慢样本保留。OS 页缓存未控制；冷态
指编译器缓存/输出目录初始不存在。两次热样本共享各自冷态命名空间，样本量有限，
不作普遍速度保证。[原始 measurement.json](../evidence/refactor/asset-pipeline/paired-41206438783-1/measurement.json)、
[逐组报告](../evidence/refactor/asset-pipeline/paired-41206438783-1/report.md)。

候选冷态另取单次 detailed 归因，不混入 off 中位数：

| 计量边界 | 毫秒 |
| --- | ---: |
| 整个进程墙钟 | 23861.693 |
| XML 纯计算批次墙钟（含等待）/累计纯计算 | 1528.188 / 750.315 |
| 验证累计 / 插件编码累计 | 20.112 / 30245.587 |
| 资产 IO 累计 / Owner 汇总 | 4707.122 / 87.219 |
| CPU 资源等待 / 内存资源等待 / IO 排队累计 | 3963.520 / 0 / 1276.131 |
| 最终引用闭包与排序 / Manifest / LinkStream | 211.997 / 8.933 / 106.476 |
| BIG / SKUDEF | 125.251 / 0.777 |

实测峰值 CPU 任务 4、IO 任务 4、待 Owner 回收 8；在途预算峰值 410,060,440 字节，
进程私有内存采样峰值 167,972,864 字节。预算包含准备工作集估算，不能当实测内存。
3354 次编码与基线相同；Worker 阶段累计可重叠，不相加为进程墙钟。

详细单样本的 XML 验证总阶段从基线 9177.882 毫秒变为候选 10412.932 毫秒，新增
前端准备和调度有成本。本轮证明的是这份负载的整体冷态吞吐改善，未证明 XML
单阶段或热态普遍提速。[完整分段及计量限制](../evidence/refactor/asset-pipeline/paired-41206438783-1/attribution.md)。
