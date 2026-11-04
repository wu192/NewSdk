# Actions Artifact 存储策略与审查记录

本次仅修改 CI 证据与归档行为；编译器、MOD 源码、测试样本、冷/热编译、缓存验证、输出哈希和完整正确性比较不变。未运行大型 MOD Benchmark，未发布 Release，未删除远程 Artifact，未合并分支。

## 分支范围

仅针对用户指定的 `recovery/legacy-sdk-cache`，以本次读取的最新提交 `65670409eda9df01a528dc6b6fcb32f1bf0be13c` 为起点，审查该分支全部 88 个工作流。只提交独立优化分支的 PR，不合并分支、不引入其他功能分支代码。

该分支当前不含 `content-parallel.yml`、`new-world-content-sdk-performance.yml`、`compiler-release-final.yml` 或 `compiler-binary-release.yml`。这些其他分支/历史工作流不在本轮修改范围，也不为本次优化新增它们。

## 上传策略

- 普通构建、测试：成功只写 Job Summary；失败保存必要 JSON、错误日志和差异文本，保留 1 天。
- 性能测试：保留指标、缓存命中、耗时、正确性、输出清单和哈希报告，保留 1 天；原始日志/事件诊断只在失败时上传。
- 明确下载依赖：保留必要传递文件，维持 Artifact 名称、目录层级及下载步骤；同次运行保留 1 天，跨运行复用的最小运行包保留 7 天并就地说明理由。
- 原始输出、缓存、测试工作目录、原始调用表不归档。音频 `Original/Recovered-calls.tsv` 和成功原版转录文本只供当次计算；保留结果及差异诊断。
- `WriteCiSummary.py` 只对匹配文件统计大小，读取有界的 Markdown/结果 JSON 展示状态。排除规则只过滤已选文件，不扫描无关 SDK/MOD 目录；不增加哈希、归档暂存或二进制复制。
- 手动打包导出：Joypad 的 `export_review_package` 默认关闭，显式开启后仅保留已验证 ZIP/manifest/校验文件 1 天。正式发行使用现有 GitHub Releases 人工批准流程；本次不创建 tag、Release 或自动发布动作。

## 大型 MOD Benchmark

`MeasureCompilerOriginalMod.py` 原来在首轮冷编译后把两套引擎的 `.manifest/.bin/.relo/.imp` 再复制到 `out/baseline-streams`，随后整个证据目录上传。完整 7.1 的既有报告包含 15 个流，即 120 个重复归档文件（15 × 4 × 2）；其中 `base/mod/mod.bin` 单引擎为 528,374,648 字节，两份超过 1.05 GB 未压缩数据，CDATA 尚未计入这些备份。

真正的运行期参考副本在 `work/reference`，后续冷热/缓存比较读取该位置。本次只删除 `baseline-streams` 的额外归档复制；保留参考数据、全文件集合/字节比较、SHA256、所有测试组合、测量边界和失败判定。当前分支脚本的 AST 校验确认除此以外计算逻辑完全一致。新的成功包精确选择 measurement/report/preparation/source/process/request/build-report 等报告；失败另存 stdout/stderr/events。

## 体积预估与保留例外

以下“之前”采用用户提供的历史 ZIP 体积；“之后”是路径收窄后的预估，尚未运行完整 Benchmark，不能当作新实测结果。Summary 会显示每次选择文件的未压缩字节数，便于核对实际情况。

| 工作流 | 历史单次 Artifact | 调整后常规成功上传 |
| --- | ---: | --- |
| new-world-71-performance | 最高约 461 MB | 报告约 1–5 MB 级；不含原始输出 |
| new-world-content-sdk-performance | 约 458 MB | 当前分支不存在，未改动 |
| content-parallel | 约 97 MB | 当前分支不存在，未改动 |
| compiler-release-final | 约 109 MB（历史） | 当前分支不存在，未改动 |
| refactor-core-lifecycle | 最高约 167 MB | 报告 + 必需 Release 运行文件/ZIP；预计数 MB 至数十 MB，取决于实际二进制大小 |
| neutral-worldbuilder | 最高约 77 MB | 0；失败仅必要报告/日志 |

另以仓库已有文件仅做 stat 抽样：旧 `recover-compilers` 的全证据规则会选中约 1,683.59 MiB；新规则只选提取收据、摘要、PE 信息及提取日志，约 0.022 MiB。普通失败规则按历史单次运行分组检查，现有样本均低于 5 MiB；这不是未来日志大小的硬上限。

| 必须保留的传递 | 内容与原因 | 保留 |
| --- | --- | ---: |
| refactor-core-lifecycle | 动态 accepted-run/paired Benchmark 需要 Release build.json、运行文件及 Release ZIP/manifest；保留 recovery 根目录布局和原名称 | 7 天 |
| joypad-parallel | reuse_run_id/cli_acceptance_run_id 需要 Debug/Release 三文件运行时、组件结果、CLI JSON 与 build.events.jsonl；常规证据不重复上传 ZIP | 7 天 |
| supercompiler-cache-sdk | 显式 `[cache-sdk-reuse:RUN]` 需要运行时、原 build/SDK 收据及二进制来源 | 7 天 |
| new-world-codecs | 跨运行完整 MOD 对照下载接受的 Release 运行时及收据，省去重新编译 | 7 天 |
| recovered-xmlcompiler-priority-x86 | SDK 复用所需的已链接编译器 DLL | 7 天 |
| audio-ealayer3 | 同次 integration Job 需要两个 DLL 及完整 jobs1 的 runtime/relo/imp/cdata 原始缓冲区作严格比较 | 1 天 |
| audio-resampler oracle | 下游验证需要 original.tsv.gz 原版结果 | 1 天 |
| XML priority 分组报告 | 同次 evidence Job 下载聚合，成功时也必须上传这些小报告 | 1 天 |

固定 run/artifact ID 的历史基线、回放、发布收据未改写，既有哈希校验和下载均保留。缩短未来产物保留期不能延长已经上传的历史基线寿命；清理这些固定依赖前必须另行迁移其来源。本次没有删除任何依赖。

## 验证与边界

所有修改工作流经 YAML 解析和 actionlint 检查。静态对比确认原测试命令、矩阵、超时、触发/失败门槛及下载步骤不变，仅移出旧 Markdown 展示到统一 Summary，并新增手动导出输入。当前 Benchmark 控制器用 AST 对比证明只移除归档复制；Summary 的 9 个轻量测试覆盖选择/排除、去重、无文件、统计大小、读取边界和大型正确性 JSON 状态展示。未执行 Windows 原生构建及大型 MOD 完整测试；因此不宣称新 Benchmark 性能或真实 ZIP 体积实测通过。

未发现 actions/cache 用法；编译器缓存仍保留在测试运行期间。所有上传有显式保留期，没有剩余的整个测试证据目录/中间产物无条件上传。上述必要的运行包/原版 oracle 是明确的依赖例外。免费 0.5 GB 是否足够仍取决于运行次数、保留期内失败数量和必须传递的运行包大小，不能仅凭路径修改保证。

## 手动清理旧 Artifact

1. 打开仓库 Actions，进入历史大体积运行；在 Artifacts 区域逐个确认并删除不再需要的证据包，优先处理上述数百 MB 原始输出包。
2. 先对照上述固定历史下载/发布依赖；这些运行包应保留或先迁移消费者，避免破坏回放和发布校验。
3. 如使用 CLI，可先只读列出：`gh api --paginate 'repos/wu162/NewSdk/actions/artifacts?per_page=100' --jq '.artifacts[] | [.id,.name,.size_in_bytes,.workflow_run.id,.expired] | @tsv'`；确认单个 ID 后手动执行 `gh api -X DELETE repos/wu162/NewSdk/actions/artifacts/ID`。本次未执行删除。

优化对合入后未来上传生效。删除旧包只释放后续占用，不能消除已经累计的计费用量；GitHub 的额度/用量显示也可能延迟更新。

## 逐文件审查清单

“失败”代表成功写 Summary、仅失败上传；“报告”代表性能指标或必要聚合报告；“传递”代表上表中的真实依赖；“手动导出”默认关闭。精确路径和条件以对应工作流为准。

| YAML | 上传策略 | 保留天数 |
| --- | --- | --- |
| ability-finish-source.yml | 精确源码/JSON 交付 | 1 |
| audio-boundary-validation.yml | 失败 | 1 |
| audio-compiler-evidence.yml | 失败 | 1 |
| audio-compiler-validation.yml | 失败 | 1 |
| audio-ealayer3.yml | 必要传递 + 诊断 | 1 |
| audio-input-validation.yml | 失败 | 1 |
| audio-multichannel-aiff-original.yml | 失败 | 1 |
| audio-multichannel-aiff-validation.yml | 失败 | 1 |
| audio-multichannel-validation.yml | 失败 | 1 |
| audio-resampler-kernel.yml | 必要传递 + 诊断 | 1 |
| audio-upheaval-validation.yml | 失败 | 1 |
| audio-xas-performance.yml | 报告 + 失败诊断 | 1 |
| big-large.yml | 失败 | 1 |
| cli-progress.yml | 失败 | 1 |
| compiler-backend-parallel-performance.yml | 报告 + 失败诊断 | 1 |
| compiler-baseline.yml | 失败 | 1 |
| compiler-cache-benchmark.yml | 报告 + 失败诊断 | 1 |
| compiler-cache-paired.yml | 报告 + 失败诊断 | 1 |
| compiler-cache-profile.yml | 报告 + 失败诊断 | 1 |
| compiler-defensive-cleanup.yml | 失败 | 1 |
| compiler-direct-output.yml | 失败 | 1 |
| compiler-hot-cache.yml | 报告 + 失败诊断 | 1 |
| compiler-mod-scale.yml | 报告 + 失败诊断 | 1 |
| compiler-performance.yml | 报告 + 失败诊断 | 1 |
| compiler-sdk-benchmark.yml | 报告 + 失败诊断 | 1 |
| compiler-sdk-real.yml | 失败 | 1 |
| game-worldbuilder.yml | 失败 | 1 |
| gameobject-baseline.yml | 失败 | 1 |
| joypad-parallel.yml | 必要传递 + 诊断 | 1/7 |
| joypad-validation.yml | 失败 | 1 |
| neutral-worldbuilder.yml | 失败 | 1 |
| new-world-71-performance.yml | 报告 + 失败诊断 | 1 |
| new-world-codecs.yml | 必要传递 + 诊断 | 1/7 |
| original-mod-performance.yml | 报告 + 失败诊断 | 1 |
| power-continuation-source.yml | 精确源码/JSON 交付 | 1 |
| recover-build-infrastructure.yml | 失败 | 1 |
| recover-compilers.yml | 失败 | 1 |
| recover-eala-metrics.yml | 失败 | 1 |
| recover-managed-support.yml | 失败 | 1 |
| recover-mvp-xml.yml | 失败 | 1 |
| recover-utility.yml | 失败 | 1 |
| recovered-armor-sdk.yml | 失败 | 1 |
| recovered-armor.yml | 失败 | 1 |
| recovered-gameobject-sdk.yml | 失败 | 1 |
| recovered-gameobject.yml | 失败 | 1 |
| recovered-hash.yml | 失败 | 1 |
| recovered-managed-support.yml | 失败 | 1 |
| recovered-primitives.yml | 失败 | 1 |
| recovered-relocation.yml | 失败 | 1 |
| recovered-utility.yml | 失败 | 1 |
| recovered-w3x-managed.yml | 失败 | 1 |
| recovered-w3x-native.yml | 失败 | 1 |
| recovered-w3x-sdk.yml | 失败 | 1 |
| recovered-weapon.yml | 失败 | 1 |
| recovered-xml-priority-sdk.yml | 失败 | 1 |
| recovered-xmlcompiler-priority.yml | 必要传递 + 诊断 | 1/7 |
| refactor-assetmerge.yml | 失败 | 1 |
| refactor-base-postprocess.yml | 失败 | 1 |
| refactor-cache-replay.yml | 失败 | 1 |
| refactor-cli-reuse.yml | 无 Artifact 上传 | — |
| refactor-core-lifecycle.yml | 必要传递 + 诊断 | 1/7 |
| refactor-gates.yml | 失败 | 1 |
| refactor-guards.yml | 无 Artifact 上传 | — |
| refactor-hashfix.yml | 失败 | 1 |
| refactor-lod.yml | 失败 | 1 |
| refactor-makebig.yml | 失败 | 1 |
| refactor-native.yml | 失败 | 1 |
| refactor-p7-replay.yml | 失败 | 1 |
| refactor-probes.yml | 失败 | 1 |
| refactor-publisher.yml | 失败 | 1 |
| refactor-release.yml | 失败 | 1 |
| refactor-session-replay.yml | 失败 | 1 |
| refactor-ui-unicode.yml | 失败 | 1 |
| refactor-xml-product.yml | 失败 | 1 |
| sound-selector-source.yml | 无 Artifact 上传 | — |
| supercompiler-cache-app.yml | 失败 | 1 |
| supercompiler-cache-sdk.yml | 必要传递 + 诊断 | 1/7 |
| tokenizer-fullmod-performance.yml | 报告 + 失败诊断 | 1 |
| tokenizer-plugin-performance.yml | 报告 + 失败诊断 | 1 |
| ui-fla.yml | 失败 | 1 |
| w3d-draw-source.yml | 精确源码/JSON 交付 | 1 |
| w3x-compression-performance.yml | 报告 + 失败诊断 | 1 |
| w3x-fullmod-performance.yml | 报告 + 失败诊断 | 1 |
| w3x-historical-compression.yml | 失败 | 1 |
| w3xcompiler-evidence.yml | 失败 | 1 |
| weapon-slot-order.yml | 失败 | 1 |
| weapon-slot-sort-probe.yml | 失败 | 1 |
| xmlcompiler-source-evidence.yml | 失败 | 1 |
