# 可关闭的构建性能打点

大型混合负载的单次 `>8s` 热缓存 Benchmark、35/60 分钟预算及手动运行入口见 [L8 Benchmark](HOT_CACHE_BENCHMARK.md)。旧短矩阵的计时不能替代 L8 负载标定。

默认 `--performance off`。开启后仍执行正常构建，不自动循环、不更改缓存策略或产物；性能结果位于原有 `build-report.json` 的可选 `performance` 字段。普通日志/JSONL 不逐次记录打点。

```powershell
NewModCompiler.exe build --sdk-root D:\SDK --mod D:\Mods\MyMod --out D:\Build\MyMod --performance summary
NewModCompiler.exe build --sdk-root D:\SDK --mod D:\Mods\MyMod --out D:\Build\MyMod --performance detailed
NewModCompiler.exe build --sdk-root D:\SDK --mod D:\Mods\MyMod --out D:\Build\MyMod --performance off

# 查看自身耗时最大的部分；streamId 为空的是整个请求的阶段。
$r = Get-Content D:\Build\MyMod\build-report.json -Raw | ConvertFrom-Json
$r.performance.entries | Sort-Object selfMs -Descending | Select-Object -First 25 name,streamId,count,totalMs,selfMs,maxMs
```

也可在 MOD 的 `newmodcompiler.json` 设置 `"performance": "detailed"`，命令行优先，可用 `--performance off` 覆盖。`plan` 也支持打点。独立 Core 宿主默认没有回调，需要时可提供 `Settings.MeasurePerformance`；CLI 只在 detailed 档安装回调，结束后恢复原 Settings。

| 档位 | 粒度 |
| --- | --- |
| off | 无新增性能报告；不读取性能时钟、不分配计时 scope、不建立计时字典。保留既有构建阶段事件与缓存计数。 |
| summary | plan/compile/package；输入元数据初始化；后端初始化/销毁；每流设置、提供程序、Schema、字符串哈希、缓存准备/读取/保存（原格式序列化）、Core 主处理、产物采集、清理；BIG/LoD/Additional/中立资产修复，无事务发布阶段。 |
| detailed | summary 加 Schema 读取解析/编译/依赖整理、文档打开/递归包含、XML 解析/表达式/覆盖/校验、资产处理/编译/验证/提交、按资产类型汇总的编译器调用、输出引用解析/manifest/link/清理、真实目录枚举、Core 按需读取原 FastHash（无 SHA256、无哈希侧车）。 |

`streamId` 对应构建计划的流 ID。名称固定到阶段/资产类型，不含文件名、实例名，重复调用只累加计数，不生成每文件事件。热态没有执行的操作不生成条目，例如复用成功时可能没有 `core.compiler.Texture`；结合原有 `cache.xmlParse`、`instanceCompile`、`fileHashReads` 判断。

- `count`：进入并完成计时范围的次数，包含异常/取消退出的尝试。
- `totalMs`：累计墙钟耗时，包含嵌套打点。
- `selfMs`：累计墙钟耗时扣除直接子打点，便于定位同一层级内部成本。
- `maxMs`：最慢单次调用。平均可用 `totalMs / count` 计算。

采用单调 `Stopwatch.GetTimestamp()` 与其 `frequency`，以 double 毫秒保留亚毫秒信息。收集器由请求独占，跟随现有单线程同步 Core 调用；没有全局性能开关/共享结果，不污染下一次构建。失败和取消会关闭 scope 并把已完成的测量写入失败报告。

不同层级的 `totalMs` 有重叠，不可相加；递归文档/包含也会把子调用时间计入父调用。`selfMs` 只扣掉已开启的子打点，因此 summary 与 detailed 的 self 值不能直接当作同一口径比较。计时本身的管理开销可能计入外层 self；detailed 用于定位，关闭后测最终吞吐。

报告标记 `boundary: before-report-write`：范围截至最终报告写入前，不包括 CLI 参数解析、诊断通道打开、报告序列化/写出、终端事件/日志关闭。它不是完整进程耗时；完整耗时需由调用方计量。不要为测报告写入而再写一遍报告。

## 后端并行计量

在 `recovery/legacy-sdk-cache` 中，summary/detailed 还提供 `cache.parallel`，协议 `NewModCompiler.ParallelBackend.v1`；off 为 null，不建立并行计量字典/区间或读取计量时钟。用于调度的 CPU/内存准入与 100ms 节流时钟不属于性能打点，off 下仍工作。

- `requestedJobs` 为 auto 或用户上限；`effectiveJobs`、`minimumEffectiveJobs` 是资源准入，不能用来证明并发。
- `backendCalls`、`backendCallsByType`、`maxBackendConcurrency` 来自实际 ProcessInstance 调用区间，包含 owner 兼容调用。
- `prepareMs`、`workerWaitMs`、`workerComputeMs`、`ownerCommitMs` 分别表示输入准备、owner 等待、后端累计工作和有序验证/提交。它们可重叠，不能相加得到进程耗时。
- `fallbackByReason` 记录实际拒绝条件；`sampledPeakPrivateBytes` 是 owner 节流采样峰值，不能冒充操作系统连续峰值。`resourceQuerySucceeded` 表示资源观测是否全部成功。
- detailed 的 `backendIntervals` 最多 512 条，带 type/startTicks/endTicks/threadId，`clockFrequency` 给出单调时钟频率；这是有限样本，不代表全部调用。

Worker 只写私有计时数据；owner 合并 `core.asset.compile` 和 `core.compiler.<Type>`，不修改同步计时 scope 栈。异步编译条目的 total/self 均是后端累计区间；不从 owner 前端 scope 的 self 中扣除可能重叠的异步时间。热态应有 backendCalls=0、prepareMs=0、xmlParse=0、instanceCompile=0。

`MeasureCompilerParallel.py` 在同 VM 同生产 EXE 上运行完整历史 Upheaval：jobs1/4/auto 的无缓存冷态、默认缓存冷/热、独立冷控制与单动画修改；off 比较吞吐，另以 detailed 冷态诊断及热态零准备断言归因，所有发布输出检查实际原字节。新结果与验收状态统一记录在 [迁移记录](BACKEND_PARALLEL_MIGRATION.md)，旧结果不代替本轮生产验收。

## 有界验证

### 热缓存范围与失效传播计数

summary/detailed 的 `performance.counters` 以 `streamId/name/value/aggregation` 输出聚合计数，`sum` 求和、`max` 取最大值，未出现的计数视为零。按流隔离；off 不创建计数集合或回调，不增加哈希、文件检查、依赖扫描或逐事件日志。detailed 同时提供 `core.cache.reinitialize`、`core.dependencies.files/defines/inheritance` 耗时；Include 沿用 `core.includes`。

| 计数 | 含义 |
| --- | --- |
| `cache.session.notFound/identityChanged/documentMissing/invalidState/formatChanged/processorChanged/disabled` | 实际会话查找/加载/初始化分支；notFound 和 disabled 是冷态/配置状态，不算文档内容失效 |
| `cache.document.new` | 首次加载文档，不混入失效数 |
| `cache.invalidation.contentChanged/dependentFileChanged/outputMissing/typeChanged/pluginChanged/inheritanceChanged/definesChanged/other` | 实际重载原因，计数单位为重载决策；同一文档可因不同原因重载多次 |
| `cache.invalidation.reloads/documents/selfInstances` | 重载事件总数、当前处理器中去重的失效文档数、这些文档首次失效前的自有资产数；selfInstances 不等于实际重编译数，实际值仍看 cache.instanceCompile |
| `cache.reload.inheritanceTarget` | 为继承准备 XML 的重载，独立列出，不冒充内容失效 |
| `cache.asset.keyMiss` | 实际提交时确认的持久资产键未命中；可能为首次构建，不自动推断内容失效 |
| `dependency.file.checks` | 现有依赖文件哈希比较链访问数，不附加读取 |
| `dependency.define/inheritance.checks/changes` | 已执行的定义/继承检查及遇到的首个变化；短路检查不推算未访问的变化数 |
| `dependency.propagation.reloads/documents/selfInstances` | 依赖文件、定义或继承变化触发的实际重载、去重文档及首次传播重载前自有资产数；不含仅为继承取 XML 的重载 |
| `dependency.include.visits/affectedVisits/affectedDocuments/maxDepth` | 实际 Include 访问、涉及失效子文档或其上游传播的访问次数、去重的受影响父文档数、最大 Include 边深度（根到直接子文档为1） |

文档去重范围是每流的实际 DocumentProcessor；重复 Include 会增加访问次数，不重复增加同一父文档数。传播计数沿现有遍历观察，不新建全图，也不把资产引用的传递闭包冒充本轮 Include/定义/继承统计。失败发生前收集的计数仍保留在失败报告中。

```powershell
$r.performance.counters | Where-Object name -Like 'cache.invalidation.*' | Format-Table streamId,name,value
$r.performance.counters | Where-Object name -Like 'dependency.*' | Format-Table streamId,name,value,aggregation
```

`TestCompilerHotCache.py` 在当前重建 Release 上补充短矩阵：32/256/2048 Armor，4/32/128 文档；中档增加8个纹理、2个PCM音频、2个动画与1个层级，以及跨文档定义/继承链。代表性覆盖五种有效缓存组合、jobs1/4/auto、off/summary/detailed、loose/BIG，每组合一次冷态和两次热态，轮次反转顺序；保留原短测试的无缓存和原BAB对照。分别修改 XML、TGA、PCM、动画、继承源和定义，保持等长及mtime，再与每代独立无缓存实际字节对照；验证失效原因、上游传播和修改后重新变热，并修复缺失发布文件及损坏会话。

每进程默认15秒，测量与字节核对共用120秒硬预算，最后一个进程受剩余预算限制；超时失败并保存部分证据，不降低覆盖后报成功。准备和生产重建单列，不计入测量。性能报告与 Actions 摘要同时列出失效/传播统计。

[Windows 41010073140](https://github.com/wu162/NewSdk/actions/runs/41010073140) success，生产/二进制/测试来源 `91b040d271b7a3cc25d32f0f923c018c740edf4a`。Release重建、App合同、5项快速预检、原14个打点/原BAB/打包/失败样本通过；扩展56样本 **42.85秒**，加原短测9.98秒，均在各自预算内。所有扩展输出与每代独立无缓存实际字节相等；热态不重写发布文件，BIG仍正常打包，缺失发布文件和损坏会话修复后重新变热。

| 修改 | 实际文档重载原因 | 失效文档 | 依赖触发重载文档 | 受影响Include父文档 |
| --- | --- | ---: | ---: | ---: |
| XML | contentChanged | 1 | 0 | 1 |
| TGA / PCM（分别运行） | dependentFileChanged | 各1 | 各1 | 0 |
| 单动画 | contentChanged | 1 | 0 | 1 |
| 继承源XML | contentChanged + dependentFileChanged | 2 | 1 | 2 |
| 单独定义文件 | contentChanged + definesChanged + inheritanceChanged | 3 | 2 | 3 |

定义用例Include访问39次，其中6次受影响，去重父文档3个、最大深度3；重复访问不冒充更多受影响文档。随后热态这些失效/传播计数为零，XML解析与资产编译也为零。继承源XML的instance Include先由依赖文件哈希拒绝；单独定义文件经all Include传播，才能独立到达定义和继承检查。这是实际判定优先级，不据测试名称伪造原因。

[完整收据/性能报告](../evidence/refactor/hot-cache/41010073140-1/validation.md)、[JSON](../evidence/refactor/hot-cache/41010073140-1/validation.json)、[来源及归档核对](../evidence/refactor/hot-cache/41010073140-1/execution.json)。原ZIP已下载复算SHA256 `9f8b1ad1bba8c3f330475586496722773714fa99143d75adf136ae464493680c`，五运行文件身份与产物一致，69份报告Schema通过。首轮41009339596因错误要求晚于文件依赖检查的继承原因而失败，原字节比较与48样本已保留；只修测试和归档配置，生产源码未变。本轮没有新增Debug、完整MOD或游戏内验收，小样本不用于宣称一般性能收益。

`TestCompilerPerformance.py` 使用临时 MOD：两个 16×16 Texture 和一个 ArmorTemplate、一个 include；三档各冷态一次/热态两次，交替顺序，校验所有流/CDATAs 原始字节相等，并和独立原版 SDK 比较；再验证三档 BIG/SKUDEF 相等以及无效 XML 的失败计时。每进程默认 10 秒，测量阶段总预算 60 秒，不运行完整 Upheaval。Windows 工作流重新构建 Release 生产组合；构建时间独立于测量预算。

App 合同同时覆盖默认关闭、CLI 值校验、嵌套/递归自身耗时、跨流汇总、异常与取消。Linux 的 Foundation/独立 App 编译只作廉价预检，不能代替 Windows 混合 EXE 运行验收。具体结果以 `RESUME.md` 和对应运行的 `validation.json` 为准。

## 本轮短测结果

[Windows 40886567000](https://github.com/wu162/NewSdk/actions/runs/40886567000) success，生产与测试来源 `239949602a8cc361fd6fb4f3bdb226e84abc6723`。Release 生产组合重建和全部 App 合同通过；测量实际 8.02 秒、14 个进程，包括三档冷态/热态、独立原版、BIG/SKUDEF 和失败 XML。全部指定字节比较通过；[原始摘要与代表报告](../evidence/refactor/performance/40886567000-1/summary.json)，完整样本/日志保留在 digest 已复算的 Actions artifact。

| 档位 | 热态进程耗时中位数 ms | 样本数 |
| --- | ---: | ---: |
| off | 445.85 | 2 |
| summary | 449.10 | 2 |
| detailed | 454.11 | 2 |

同 VM 小样本，OS 页缓存不受控，不能用这些差值精确估计打点开销。热态指编译器缓存热，仍是每次新进程：各档热态零 XML 解析、零资产编译、零完整文件哈希读取；detailed 中 Schema 读取解析 127–138ms、Schema 编译 23–26ms、依赖整理 14–16ms。这些条目解释了未重新编译资产时仍存在的初始化成本，不代表完整 MOD 的瓶颈分布。

完整 mymod、96 真实 Texture 中型子集及完整 Upheaval 的后续测量已完成：[大中小 MOD 耗时分析](MOD_SCALE_TIMINGS.md)，Windows 40890949858 success，9 次进程测量/校验实际 87.63 秒。此表与前面的 8 秒小输入开关验证使用不同负载和 VM，不横向比较绝对耗时。

## 全认证后端迁移验收（2026-10-30）

生产6d39f833，测量02987cbc；[同VM22样本40920804580](https://github.com/wu162/NewSdk/actions/runs/40920804580) success，复用功能门槛全部通过的原二进制，未重建Native。完整Upheaval，off默认冷jobs1/4/auto=74.096/49.165/48.580s，减少33.6%/34.4%；热中位2.898/2.918/2.908s，解析/编译/并行准备为0；单动画修改只重编1资产，3.116/3.153/3.141s。detailed真实后端峰值1/4/4，3354调用，XML前端925份仍串行。数据/归档/精确范围见 [迁移验收记录](BACKEND_PARALLEL_MIGRATION.md)。

TryPrepare秒只覆盖该函数，前置资格/初估/资源观测归owner Core；有序结果等待与失败清理join分属workerWait/core.cleanup。无缓存单改owner对照保留未变预编译/链接输出（1292调用），不是全新根的3354调用全冷。冷态n=1、热态n=2，不能用10–20ms小差值宣称回退或跨VM比较历史75.286/2.864s。
