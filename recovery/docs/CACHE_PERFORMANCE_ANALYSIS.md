# 缓存热态为何比原 SDK 慢

本页保留优化前 `39039961` 的原因分析。2026-10-29 已按用户性能要求在 `4ad5d0d9` 实现原链接复用、文件身份/哈希收据、无变化发布和 BIG 复用、会话搬运去重、缓冲事件及清理逐条 stderr；826 文件内容 SHA256 已改为批量元数据。具体合同见[缓存说明](LEGACY_INCREMENTAL_CACHE.md)。最终生产验收 [40836177604](https://github.com/wu162/NewSdk/actions/runs/40836177604) 已 success；[同输入基准 40837902446](https://github.com/wu162/NewSdk/actions/runs/40837902446) 复用该精确 Release，54 对全部字节一致。当前默认热态相对原 SDK 仍慢约 6.7%–10.5%，各边 n=2，详见 [SDK 对照](SDK_BENCHMARK.md)。下列旧数字不表示优化后的耗时，不将不同 VM 的跨轮绝对毫秒差当作因果收益。

当前证据将问题定位到缓存命中后的固定工作与输出策略。默认热态中，新版已经复用文档和资产，额外成本包括每次进程的会话身份校验、缓存状态搬运，以及全新 staging 中的重新链接、重复输出哈希和事务发布。不能仅用“写 JSON 慢”概括，也尚未取得完整进程中每项的精确贡献比例。

## 正式基准中的观测

来源是同机、共享物理输入及有序搜索根的[配对运行 40823369828](https://github.com/wu162/NewSdk/actions/runs/40823369828)，生产来源 `39039961847cd1ca9cb93743fd51741a4e5c9f49`。对照方法和完整 CSV 见 [SDK_BENCHMARK](SDK_BENCHMARK.md)。本次分析只使用已完成的 108 次计量，没有重新执行完整矩阵。

下表为默认热态中位数，单位 ms；每边 n=2。compile 包括宿主初始化、提供器初始化、缓存校验/恢复/保存、Core 处理及输出收集，并非纯资产编译时间。

| 工作量 | 老 SDK / 新版进程 | 差值 | 新版 compile 阶段 | 新版 publish 阶段 |
| --- | ---: | ---: | ---: | ---: |
| mymod | 593.4 / 741.9 | +148.5 | 536.0 | 11.0 |
| Upheaval Armor 子集 | 571.6 / 713.6 | +142.0 | 491.5 | 21.0 |
| 合成纹理×16 | 567.9 / 731.9 | +164.0 | 506.0 | 30.5 |

publish 的 11–30.5 ms 只覆盖总差距的一部分，不能把整段差距都归给发布。新版 plan 约 6–6.5 ms，报告计时窗内其他工作约 17–18.5 ms。墙钟减 report.elapsed 的剩余约 171–176 ms，混合了启动、预检、报告落盘及收尾；原版 trace 的边界不同，不能把两者相减当成纯启动或纯 Core 的差异。各分量中位数也不保证相加等于整进程中位数。

默认热态的新报告实际计数如下；这表明资产重编译并没有发生：

| 工作量 | documentReuse | xmlParse | instanceCompile |
| --- | ---: | ---: | ---: |
| mymod | 11 | 0 | 0 |
| Armor 子集 | 12 | 0 | 0 |
| 合成纹理 | 1 | 0 | 0 |

新版两缓存 XML 与 Deflate 热态中位数，分别为 mymod 739.0/741.9 ms、Armor 713.5/713.6 ms、纹理 737.0/731.9 ms；这次样本中仅差 0.1–5.2 ms，压缩选择不能解释百毫秒级差距。[可复算的全部时间窗及旧日志判断](../evidence/refactor/cache-profile/40823369828-1/timing-windows.json)由 `AnalyzeCompilerSdkTimings.py` 从原始归档生成。

## 会话身份每次仍执行大量小文件操作

`CoreStreamCompiler.Compile` 在启用 SC、首次建立本进程 `legacyStore` 时调用 [LegacyCacheIdentity](../src/App/Application/CoreStreamCompiler.cs#L563)。每个新进程都重新枚举并 SHA256 校验：

- 821 个 Schema，共 1,520,895 字节。
- 4 个运行文件，共 5,133,001 字节。
- SDK 配置 1 个，2,945 字节。
- 合计 **826 文件、6,656,841 字节**。每个文件都重新走 `DirectoryInfo` 父目录链检查 reparse，没有跨文件复用目录检查。

它位于 compile 阶段，热会话也执行；无 SC 的模式不执行。同一进程内多个流共享 `legacyStore`，这项身份计算在该进程只做一次，不能按流数重复计入。

[只读诊断运行 40826280008](https://github.com/wu162/NewSdk/actions/runs/40826280008) 复用精确 Release 五文件，独立 helper 按生产源码相同的文件集合、排序、目录检查和 SHA/BinaryWriter 合同复现文件工作。辅助程序的完整计时操作合计不足 1 秒，进程仍有 20 秒硬上限，运行库前后哈希保持一致。

| 独立微基准 | 5 次重复中位数 ms |
| --- | ---: |
| 完整身份文件工作 | 59.53 |
| 仅枚举与排序 | 2.21 |
| 仅逐文件祖先检查 | 28.11 |
| 仅逐文件打开并 SHA256 | 27.97 |

这些分量来自分别执行的实验，**不能相加后当作生产方法分段计时**，也不能直接除以另一台 VM 上的正式差距计算贡献率。helper 中第一次完整调用为 328.85 ms，包含首次 JIT/框架初始化等状态，不应套用为正式进程每次身份计算时间；生产在此之前已经做过其他 SHA 和路径操作。稳定重复值用于证明这一小文件机制的开销量级。

原本尝试直接反射调用验收 EXE 的私有方法，但 CLR 拒绝将该混合可执行文件作为普通程序集加载（0x80131019），因此没有取得生产方法的直接计时。失败[40825481917](https://github.com/wu162/NewSdk/actions/runs/40825481917)及[原始错误](../evidence/refactor/cache-profile/40825481917-1/profile.json)保留。后续 helper 没有修改 PE、没有重编译生产，结果明确标记 `productionMethodDirectlyMeasured=false`、`preflightMeasured=false`。[原始微基准](../evidence/refactor/cache-profile/40826280008-1/profile.json)及[来源](../evidence/refactor/cache-profile/40826280008-1/provenance.json)。

## 老工具跳过链接输出，新版重新生成并发布

六个原版默认热态日志均明确出现 `Old manifest is up to date.`，binary/import/relocation 三项也都显示 `up to date`。原 [OutputManager](../src/Managed/Core/BinaryAssetBuilder.Core/OutputManager.cs#L371) 能利用同一输出目录已有文件跳过写出。

新版每次使用新的私有 staging；该目录没有上一轮四流，链接输出会重新生成。随后每个输出经历：

1. [Core 收集](../src/App/Application/CoreStreamCompiler.cs#L677)时 SHA256。
2. [BuildSession 验证](../src/App/Application/BuildSession.cs#L355)时再次读取同一源文件并 SHA256。
3. [ArtifactPublisher](../src/App/Application/ArtifactPublisher.cs#L75)复制到发布候选、逐文件 `Flush(true)`，再读取副本并 SHA256，最后交换发布目录。

因此热态仍有链接、三遍输出哈希、一遍发布复制和强制刷盘的成本。发布阶段本身已量化为 11–30.5 ms；链接和前两遍哈希在 compile 中，尚未单独取得其精确耗时。本轮纹理输出约 4 MiB，更容易显现这类重复 I/O。此策略同时提供失败/取消时保留上一代产物的保证，优化应保留该合同。

## 其他已确认存在、尚未单独量化的成本

- [LegacyCacheStore](../src/App/Application/LegacyCacheStore.cs)读取原会话到 staging，再复制到 candidate，安装回外部会话目录并备份旧文件。带 active 与 `.old` 的稳态热会话可有 7 次会话文件复制，另有身份侧车；原 Core 保存本身使用临时序列化加 rename。本轮无 CDATA 输入，不将 CDATA 多段复制归为本次原因。
- [BuiltinCompilers](../src/App/Application/BuiltinCompilers.cs)每个新进程初始化 Texture 路由，即使输入只有 Armor。[LegacyTextureAdapter.Load](../src/App/Compatibility/LegacyTextureAdapter.cs#L64)还审核 EXE 一次、Texture DLL 两次，共读取约 5.95 MB，并校验补丁 JSON 和程序集引用。该固定工作在无 SC/仅 BC 模式也存在，不能只用它解释 SC 特有的差距。
- 同输入 benchmark 显式设置了资源根，[Program.Main](../src/App/Cli/Program.cs#L31)在事件计时开始前执行 `PreflightInputRoots`，额外加载设置并校验物理目录。原版接受同一根列表，但没有新宿主相同的这层预检。预检与 Texture 审核的独立耗时目前没有直接测量。
- JSONL/文本事件和最终报告也有序列化、flush、原子写入及目录固定成本，现有窗口不能把它们与所有启动/收尾工作精确分开，所以不将其指定为唯一主因。

## 优化优先级

先处理证据最明确的重复工作：在保持身份覆盖和路径保证的前提下复用本轮已固定的祖先目录检查；合并输出读取/哈希与复制；研究未变四流的链接及发布复用，同时保留原子发布与回滚。之后再量化预检、提供器启动和序列化的固定成本。

本轮交付为分析及只读诊断，生产逻辑和已验收二进制保持原样。完整进程中各项的因果占比、优化后的收益及正确性仍需要后续定向测量与验收，不能由本次微基准直接宣称。
