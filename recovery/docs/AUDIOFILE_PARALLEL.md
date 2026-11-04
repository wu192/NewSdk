# AudioFile 并行与预算

基线：`recovery/legacy-sdk-cache` / `b89fbadb`。实现分支：`perf/audiofile-parallel`。

内建恢复版 AudioFile 的并行资格不再受 `PCSampleRate` 和 `PCCompression` 属性限制。默认压缩、NONE、XAS、EALAYER3，以及显式原采样率和真正重采样均可进入 worker。编码器中的不支持输入/参数仍按原路径报错；未知或注入的插件不获得内建实现的资格。

AudioFile 外部文件的调度预算由 `文件长度 × 64 + XML 估算` 改为 `文件长度 × 4 + XML 估算`，文件部分降低 93.75%，与 MP3 passthrough 的现有倍率一致。2 MiB 输入约计 8 MiB，原来约 128 MiB。这个值是调度启发式估算，尤其高倍重采样的实际峰值可能更高；它不代表实测内存或吞吐提升。jobs、CPU、Win32 内存观测、单任务及总在途预算继续控制并发量。

采样转换的 scratch/输出均属于单次调用，系数表只读。EALAYER3 的进程级压缩提示改用原子首次标记，多个 worker 只输出一条完整提示。CDATA 私有路径、输入只读租约、有序验证提交和失败/取消 join 不变。

验证沿用 Core 生命周期完整工作流。组件波扩充为八个音频资产，比较串行与 worker 的三缓冲和 CDATA，覆盖四种压缩设置、升/降采样、streamed/RAM，并检查并发首次压缩提示仅一次。资格用例验证显式同采样率以及新预算的允许/拒绝边界。生产混合夹具增加同样八种设置，要求所有 AudioFile 均在 worker 执行，并继续比较 jobs 1/4/auto、冷热缓存、修改、失败重试的原字节输出。

本地 Python 语法、夹具生成及 SDK aggregate XSD 已通过。Windows [41058436061](https://github.com/wu162/NewSdk/actions/runs/41058436061) **success**，源码、二进制、测试来源均为 `6c2f08691450049ad88abd5ab067e52ccaf3f3be`；随后仅补充文档和证据。

- Debug/Release 完整新构建及组件均通过，八个音频资产均在 worker 执行，真实峰值并发 4，三缓冲与 CDATA 字节一致，压缩提示各进程仅一次。
- 64 个生产缓存/jobs 用例通过，其中 16 个冷态并行用例每次八个 AudioFile 均在 worker；冷热/修改/失败重试保持原字节。实际 CLI/CtrlBreak、原 BAB、缓存载荷与会话、完整 mymod 原 SDK 差分、BIG/Additional/SKUDEF 和独立五文件包全部通过。
- 完整 Upheaval 22 个样本全部 0 错误、1859 原警告，BIG/SKUDEF 字节一致。冷态 1289 次 AudioFile 调用，jobs4 实际峰值 4；仍有 117 个未知插件和 10 个任务的一般准备预算回退。该验证比较同一新二进制的 jobs 档位，不用于推算修改前后的收益。

171,240,197 字节[原归档](https://github.com/wu162/NewSdk/actions/runs/41058436061/artifacts/12979545672)已实际下载，SHA256 复算为 `7f821d56c940144c94f1d367c3426b34771bb6e92e9fa631a6c5d377d7e55ad2`，与 GitHub 元数据及上传日志一致。两个发布 ZIP 和十个运行文件均复算匹配。14 份原收据身份、组件原收据、生产矩阵及计量边界见[验收摘要](../evidence/refactor/audiofile-parallel/41058436061-1/summary.json)和[完整 MOD 计量](../evidence/refactor/audiofile-parallel/41058436061-1/measurement.md)。无新游戏内验收；用户随后已批准 [PR #30](https://github.com/wu162/NewSdk/pull/30) 合入。

合入准备时已同步目标分支 `d85b8c8c`（PR #28），保留清理后的预算估算和调度实现，测试改用成功准备结果判断资格。上面的 `6c2f0869` 是独立分支验收。

组合源码/二进制/测试来源 `cc10391be67dda57c7efa7fc06256b99e9372d9a`，[Windows 41068958541](https://github.com/wu162/NewSdk/actions/runs/41068958541) **success**：Debug/Release 全量新构建、八种音频设置的 worker 三缓冲/CDATA、64 生产缓存/jobs 用例、CLI/CtrlBreak、原 BAB、缓存载荷/会话、完整 mymod、BIG/Additional/SKUDEF 和独立五文件部署均通过；完整 Upheaval 22 样本仍全部 0 错误/1859 原警告，输出原字节一致。

新归档 167,579,889 字节已实际下载，SHA256 `13464d2e0bb1207538dd883fb86e3774f1ddcb0671901245a27970d9edf87adb` 与 GitHub 元数据匹配，两个发布 ZIP 和十个运行文件已复算匹配。[新摘要和 14 份原收据身份](../evidence/refactor/audiofile-parallel/41068958541-1/summary.json)、两配置组件原收据及[同候选 MOD 计量](../evidence/refactor/audiofile-parallel/41068958541-1/measurement.md)另行保存；旧收据不代替本次组合源码的新结果。用户已批准通过 PR #30 合入，禁止强推。
