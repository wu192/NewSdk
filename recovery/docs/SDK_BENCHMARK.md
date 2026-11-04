# 原 SDK 与新版性能对照

工作流 `compiler-sdk-benchmark.yml` 新增可选 `accepted_run`：显式指定已成功完成全部 Core lifecycle 门槛的运行，可复用该运行的五文件 Release 测量后续优化；留空继续使用原固定来源。准备器从 GitHub 成功运行及产物 digest 确认来源，再校验发行收据、五文件内容和实际生产输入闭包。二进制和测试来源分别记录，未经完整验收的运行不能用于该选项。下文区分优化后新结果与优化前历史，不混用两轮来源。

`BenchmarkCompilerSdkComparison.py` 在同一台 Windows VM 上交替执行原 `BinaryAssetBuilder.exe` 和已验收的新版 Win32 Release。比较独立进程从启动到退出的端到端成本、CPU 时间、峰值工作集及缓存占用；新版还承担报告、JSONL、独立 staging 和事务发布的成本，原工具直接写出流文件。结果用于比较实际命令行使用成本。

## 六项热态优化后的结果（2026-10-29）

[运行 40848391545](https://github.com/wu162/NewSdk/actions/runs/40848391545) success。生产二进制、基准源码均为 `84261243`；通过 `accepted_run=40845943601` 复用完整验收的精确五文件 Release，没有用旧二进制替代本轮变化。原归档和基准归档均已通过插件下载并在本地复算 SHA256，与 GitHub digest 相同。[来源与摘要](../evidence/refactor/sdk-benchmark/40848391545-1/summary.json)、[完整 JSON](../evidence/refactor/sdk-benchmark/40848391545-1/comparison.json)、[CSV](../evidence/refactor/sdk-benchmark/40848391545-1/comparison.csv)、[全模式表](../evidence/refactor/sdk-benchmark/40848391545-1/comparison.md)。

同一台 VM、同一物理输入/Schema/有序资源根，交替启动两个编译器，独立缓存，预热排除。108 对/216 次计量及两次预热合计约 123.1 秒；所有四流/CDATAs 的成员、长度、SHA256、原始字节一致。SDK、MOD、运行库及资源根存在性均保持不变。新版耗时仍包含 JSONL、报告、staging 和发布流程，比较的是命令行启动到退出。

默认 `/bc:true /sc:true /csc:true` 热态，每边 5 次中位数，单位 ms：

| 工作量 | 老 SDK | 本轮新版 | 新版耗时变化 |
| --- | ---: | ---: | ---: |
| 完整 mymod 输入，base/loose | 450.6 | 446.1 | -1.0% |
| Upheaval Armor 子集，12 资产 | 426.3 | 417.5 | -2.1% |
| 合成 Texture ×16 | 429.5 | 454.5 | +5.8% |

mymod 和 Armor 默认热态已略快于老 SDK；纹理仍慢约 5.8%，不能表述为所有场景均更快。其余热模式也有差异：mymod 的 XML/仅会话模式约慢 2%–3%，纹理 XML 双缓存约慢 9%，详见全模式表。mymod/Armor 类型未启用原生资产缓存，bc-only 结果不据此声称资产缓存命中。样本规模有限，OS 页缓存未受控；不同 VM 的历史绝对时间和比例变化不能作为六项改动的精确因果收益。未测完整 Upheaval、BIG 性能、游戏内行为；完整 mymod BIG/ZIP 正确性由生产验收另行覆盖。

## 上一轮优化后结果（历史，2026-10-29）

[运行 40837902446](https://github.com/wu162/NewSdk/actions/runs/40837902446) success；生产二进制 `4ad5d0d9` / 完整验收 `40836177604`，基准源码 `0fb98df2`，直接复用精确五文件 Release。54 对/108 次计量及两次预热约 83.4 秒，全部流/CDATAs 的成员、长度、SHA256 和原始字节相同。[CSV](../evidence/refactor/sdk-benchmark/40837902446-1/comparison.csv)、[完整 JSON](../evidence/refactor/sdk-benchmark/40837902446-1/comparison.json)、[来源与摘要](../evidence/refactor/sdk-benchmark/40837902446-1/summary.json)。

默认两缓存 Deflate 热态，各边 n=2，单位 ms：

| 工作量 | 原 SDK | 优化版 | 新/原 |
| --- | ---: | ---: | ---: |
| 完整 mymod 输入，base/loose | 581.2 | 620.1 | 1.067 |
| Upheaval Armor 子集 | 539.5 | 596.1 | 1.105 |
| 合成 Texture ×16 | 547.5 | 593.9 | 1.085 |

该阶段仍比原 SDK 慢约 6.7%–10.5%。本轮同机配对可比较当前两引擎；各边仅两个热态样本，OS 页缓存未受控。历史轮次在另一 VM 上，不能把跨轮绝对毫秒差或比例变化当作精确因果收益。BIG 复用由生产原收据另外确认，不包含在这组 loose 基准。`cacheBytes` 延续既有 asset/session 目录口径，不包含新增私有链接快照/哈希索引，不把 none 模式的 0 字节解释为完全无派生状态。

## 优化前实测结论（历史，2026-10-29）

[运行 40823369828](https://github.com/wu162/NewSdk/actions/runs/40823369828) 整体 success。基准源码 `1c7ff21f098e972105c410eb586c4f6c8ba32d29`；新版生产来源 `39039961847cd1ca9cb93743fd51741a4e5c9f49`，原版为同源树中未修改的 SDK EXE/DLL。Windows Server 2022、4 逻辑 CPU。54 对/108 个计量样本均通过，另含 2 次启动预热，计量循环合计 91.21 秒；无生产重编译。

本次同机结果中，新版默认热缓存端到端耗时比原版约多 25%～29%；mymod 无缓存基本相当，Armor 子集与合成纹理无缓存约多 8%～9%。这是完整命令行成本对照，包含新版报告、staging 和事务发布；本轮未做纯 Core 的分段归因。

后续[原因分析及只读诊断](CACHE_PERFORMANCE_ANALYSIS.md)确认热态未重新解析 XML/编译资产，主要额外工作位于身份校验和输出处理；发布本身只占 11–30.5 ms。相同 826 文件的独立身份 I/O 复现稳定中位数约 59.5 ms，不等同于正式进程中的直接打点。

墙钟中位数，单位 ms，单元格均为“老 SDK → 新版”：

| 模式 | mymod（47资产） | Upheaval Armor 子集（12资产） | 合成纹理（16资产） |
| --- | ---: | ---: | ---: |
| 无缓存（每边3次） | 899.9 → 882.1 | 618.3 → 667.2 | 897.7 → 977.4 |
| 仅资产冷（每边1次） | 875.3 → 839.5 | 611.3 → 673.3 | 889.1 → 968.4 |
| 仅资产热（每边2次） | 895.6 → 879.1 | 592.4 → 672.6 | 581.9 → 658.4 |
| 仅会话 XML 热 | 576.7 → 746.6 | 578.6 → 711.0 | 562.8 → 745.9 |
| 仅会话 Deflate 热 | 593.2 → 724.3 | 576.2 → 731.6 | 563.2 → 734.4 |
| 两缓存 XML 热 | 581.0 → 739.0 | 569.4 → 713.5 | 554.0 → 737.0 |
| 默认两缓存 Deflate 冷 | 906.4 → 957.1 | 595.9 → 763.7 | 928.3 → 1097.3 |
| 默认两缓存 Deflate 热 | 593.4 → 741.9 | 571.6 → 713.6 | 567.9 → 731.9 |

mymod 和 Armor 的类型未启用原生资产缓存，bc-only 的两边均不据此声称复用；合成纹理的资产热态，原版日志每次均观察到 16 次缓存复制，新版报告也是 16 hits、0 misses、0 编译。默认热会话在两边均实际载入，新版对应 XML 解析/实例编译为 0。

默认热态的 CPU/峰值工作集（同样老 → 新）：

| 工作量 | CPU ms | 峰值工作集 MiB | 新/老墙钟比 |
| --- | ---: | ---: | ---: |
| mymod | 585.9 → 742.2 | 43.91 → 45.69 | 1.250 |
| Armor 子集 | 570.3 → 710.9 | 44.12 → 44.39 | 1.248 |
| 合成纹理 | 570.3 → 757.8 | 44.10 → 44.23 | 1.289 |

原版与新版的三组基线均恰好四流，所有 108 个样本都与对应原版基线成员/长度/SHA256 相同，逐对原始字节比较通过。本轮未包含产生 CDATA 的输入，CDATA 性能不由本轮证明。全部共享 SDK（14715 文件、125887928 字节）、MOD 文件、原/新版运行库、资源根存在性保持不变；每个实际新报告都确认了共享路径及搜索顺序。完整旧 SDK 默认工具及业务 DLL 未替换，只适配 CLR4 startup。

[完整 CSV](../evidence/refactor/sdk-benchmark/40823369828-1/comparison.csv)、[108 次原始计量 JSON](../evidence/refactor/sdk-benchmark/40823369828-1/comparison.json)、[所有统计及 min–max](../evidence/refactor/sdk-benchmark/40823369828-1/comparison.md)、[原版准备与共同路径](../evidence/refactor/sdk-benchmark/40823369828-1/original-preparation.json)、[输入摘要](../evidence/refactor/sdk-benchmark/40823369828-1/shared-inputs.summary.json)、[来源校验](../evidence/refactor/sdk-benchmark/40823369828-1/provenance.json)。完整共享输入逐文件清单、逐次请求/报告/日志和两边原始四流保存在[运行产物](https://github.com/wu162/NewSdk/actions/runs/40823369828/artifacts/12876025513)，外层归档 SHA256 `1d99621681939a5ea55c56635ae8ca67f77dc58794a89c4e7b4f8fafa0d7be8a` 已在本地复算并逐项验证。

无缓存样本每边 n=3，热态 n=2，冷态 n=1；小样本只描述本次 VM 及这些工作量，不推广到完整 Upheaval。不同运行 VM 的绝对速度有差异，本表完全来自同一次配对运行，不混用先前单引擎结果。

## 输入一致性

- 两边共用同一个物理 MOD 目录和同一个 `Data/Mod.xml`，不为某一边删改 XML、改类型或缩小资源。完整 mymod、11 个原始 Upheaval Armor 文档/12 资产的子集、16 个合成 Texture 分别统计；工作量沿用已有[缓存基准](CACHE_BENCHMARK.md)。
- 两边共用相同 SDK、Schema 及资源文件。按 MOD 对应目录优先、再按 SDK 配置补充的顺序，冻结绝对 Data/Art/Audio 搜索路径；重复和不存在的候选也保留，逐项显式传给两边。新版的真实报告必须确认路径、顺序、Schema 和入口完全一致。
- 原 EXE 和所有 DLL 对照固定源树校验，复制后保持原字节及插件路由；配置仅适配 CLR4 startup，`assetbuilder` 与 LowLOD/MediumLOD 声明保留。新版直接使用原验收的精确五文件包，生产源码未重新编译。
- 缓存选项、Win32、链接、稳定排序、预编译引用、慢清理和诊断级别显式对齐。两边均单次运行，关闭驻留、GUI、监视和旧指标上报；不包含旧驻留 worker 的跨调用复用。
- 两边使用独立的会话、资产和中间目录，空缓存开始，不共享另一引擎生成的缓存。无会话缓存时每次换新中间及会话元数据目录，保留本模式的资产缓存供热态使用；会话模式保留本引擎、本模式的中间结果。原工具即使 `/sc:false` 仍会保存 `StringHashes.xml`，所以无会话模式还检查日志未载入上次字符串哈希表，避免给原工具留下额外的跨进程状态。
- 准备时保存完整共享 SDK、MOD 及原/新运行库清单。逐对检查 MOD 不变，最后确认全部输入、运行库及输入根的存在性未变。

## 样本与有效条件

6 模式为无缓存、仅资产缓存、仅会话 XML、仅会话 Deflate、两缓存 XML、两缓存 Deflate。每个引擎、每个工作量：无缓存 3 次，其他模式各 1 次冷态和 2 次热态。合计 54 对、108 个计量进程；另各 1 次相同 mymod 无缓存构建作启动预热，不计入构建成绩，但计入整组预算。

每轮改变模式及引擎执行顺序，减少固定先后顺序的偏差。单进程最多 30 秒，整组最多 180 秒；准备步骤最多 2 分钟、Windows job 最多 8 分钟。OS 页缓存未清空，“冷”只指编译器缓存为空。少量样本报告中位数和 min–max，不宣称统计显著。

每对必须成功退出且无编译错误，所有输出的成员、长度、SHA256 及原始字节一致；不同模式还必须匹配同工作量的无缓存基线。只去掉两种宿主不同的流目录前缀，不改输出字节，不能删除额外引用流来取得相同结果。原始四流/CDATAs 的首组基线保存在产物中。

原工具不提供新版同格式的持久计数，保留其完整日志和可识别的会话载入/资产缓存复制消息，不能把未观测到的计数写为 0。新版按真实报告校验热会话复用、编译次数和适用类型的资产命中。GameObject/Weapon/Armor 在原 SDK 的类型配置中未启用资产缓存，bc-only 不据此宣称发生了复用。

## 重复运行

```powershell
python recovery/scripts/PrepareCompilerCacheBenchmark.py --repo . --output "$env:RUNNER_TEMP/cb"
python recovery/scripts/PrepareCompilerSdkBenchmark.py --preparation "$env:RUNNER_TEMP/cb/preparation.json" --repo . --output "$env:RUNNER_TEMP/ob"
python recovery/scripts/BenchmarkCompilerSdkComparison.py --preparation "$env:RUNNER_TEMP/cb/preparation.json" --original-preparation "$env:RUNNER_TEMP/ob/original-preparation.json" --output recovery/evidence/refactor/sdk-benchmark/local --work-directory "$env:RUNNER_TEMP/sbw" --timeout-seconds 30 --budget-seconds 180 --hot-runs 2
```

准备器支持复用本地已下载归档，详见基础缓存基准。输出及工作目录必须不存在；原版混合工具使用短工作路径。工作流为 `.github/workflows/compiler-sdk-benchmark.yml`，显式 `[sdk-benchmark]` 提交或手动触发；不重复运行生产编译与完整 SDK 验收。

## 保留的前序记录

[40822449926](https://github.com/wu162/NewSdk/actions/runs/40822449926) 为 Windows 路径别名/斜杠测试断言失败，计量未启动，保留 failure。

[40822714583](https://github.com/wu162/NewSdk/actions/runs/40822714583) 已 success，输入及原始输出一致，108 次计量及预热约 70.6 秒；日志复核发现 `/sc:false` 下原版仍加载前轮 `StringHashes.xml`，后续将无会话模式的元数据目录也逐轮更新。原成功终态及其[统计](../evidence/refactor/sdk-benchmark/40822714583-1/comparison.csv)保留，当前正式比较统一使用上面的修正后同机运行。
