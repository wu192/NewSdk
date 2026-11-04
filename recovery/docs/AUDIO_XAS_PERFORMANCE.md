# XAS 数组区间编码

基线：`45d485d829170dc4fda3b3ce63b6af2759fc7ba0`（`recovery/legacy-sdk-cache`）。实现分支：`perf/audio-xas-ranges`。本批只修改 Audio 的三个编码器；没有修改其它插件或执行完整 SDK 验证。

- XAS 按输入帧偏移推进，删除 pending、GetRange/RemoveRange 和完整块 PCM 数组。
- 预先计算所有 76×声道块、8 字节分块头和末尾填充，一次分配最终 CustomData。streamed 原来对齐时仍追加 64 字节的规则保留。
- 128 帧块直接写入输出偏移。每次编码只用一个 float[30] 工作区，每组 30 个源样本只做一次原样 F() 转换，四预测器共用；原 F() 精度边界和量化运算顺序保留。
- 四组头和交错 nibble 直接写入最终位置，删除 byte[19]、byte[][] 和块输出数组。只有每段末尾不足 128 帧时分配填充块。
- Loop 直接读取原数组的 Intro/Loop 区间，各段分别结束、填充和计算 streamed 偏移，丢弃 inclusive loopEnd 之后的源样本但保留原 runtime 帧数。
- 共享 PCM 写入器提供相同区间接口，消除 Loop 的非 XAS 整段复制；普通 PCM 复用这条写入路径，格式不变。

## 验证方法

集中复用 TestAudioPlugin（pcm/xas/loops/aiffloops/multichannel/multichannel-aiff/resample-full）、TestAudioPcm 两种 codec 和 TestAudioXasPrecision。Windows .NET Framework Debug/Release x86 执行原 DLL 差分，不替换原版预期、不放宽断言。新增工作流不包含 SDK 步骤。

性能程序从上述固定提交提取三个基线源码，与候选在同一进程、同一运行时、相同输入下比较。先检查 3280 个 PCM/XAS、1/2/4/6 声道、rate400/431/22050/48000/96000、128 帧和 streamed 分块边界、Loop 区间的完整输出及 metadata；再测 30 秒/120 秒 RAM、120 秒 streamed 和 Loop、600 秒 streamed、30 秒六声道。48kHz、确定性 signed16 随机信号，非整块末尾；Loop 使用非整块 Intro 并舍弃尾部 7 帧。每项三轮交替先后次序，每轮检查完整 HeaderData/CustomData。

Windows 计量使用 net472 x86 Release 和 AppDomain.MonitoringTotalAllocatedMemorySize，记录 Stopwatch 耗时与三代 GC 次数。输入生成、字节比较和每轮测量前主动 GC 不计入编码区间；AppDomain 分配计数包含区间内运行时分配。单线程、workstation GC、禁用 tiered compilation。原始每轮数据归档为 CSV/JSON，不以 GC 后存活内存代替累计分配量。

## 验收结果

生产源码、二进制和测试来源均为 `b8e37a2e957f7a2465ad303110b92eec3269df4d`；后续提交只归档结果。固定基线为 `45d485d829170dc4fda3b3ce63b6af2759fc7ba0`。

[Windows 运行 40996371215](https://github.com/wu162/NewSdk/actions/runs/40996371215) **success**，只有本批音频验证，没有执行完整 SDK：

- Debug/Release x86 各 8924 个完整三缓冲/CustomData 原版逐字节对照全部一致：PCM 8、XAS 12、WAVE Loop 216、AIFF Loop 200、WAVE 多声道 1728、AIFF/AIFC 多声道 6336、已定义重采样 424 条。每模式另保留 8 个既有未定义单帧导入的候选拒绝断言，未将原版未初始化内存当作一致性目标。
- 现有 PCM/XAS 组件、每种 codec 的 504 个原分块标量、262 个原 XAS 块，以及含 2 个 binary32 回归的 264 块精度矩阵，两种配置均通过。四个预测器及 shift0..12 全覆盖。
- 同机 net472 x86 Release 完成 3280 个前后区间/分块/Loop/多声道边界比较；七种长音频各三轮，21 次完整 HeaderData/CustomData 和 metadata 比较一致。

## 同机计量

下表为三轮中位数，编码区间不含输入生成、字节比较和主动 GC。确定性随机 signed16 PCM、48kHz、workstation GC；只计编码器，不包含导入、重采样或完整 MOD 编译。

| 用例（48kHz） | 耗时 ms：前 → 后 | 加速 | 累计分配 MiB：前 → 后 | GC 0/1/2：前 → 后 |
|---|---:|---:|---:|---:|
| stereo-30s-ram | 1844.287 → 695.746 | 2.65× | 84.028 → 1.631 | 9/1/1 → 0/0/0 |
| stereo-120s-ram | 46243.162 → 2787.689 | 16.59× | 336.078 → 6.523 | 33/3/1 → 0/0/0 |
| stereo-120s-streamed | 2725.237 → 2784.604 | 0.98× | 207.003 → 6.549 | 37/0/0 → 0/0/0 |
| stereo-120s-loop-ram | 44495.803 → 2782.223 | 15.99× | 403.698 → 6.523 | 33/3/1 → 0/0/0 |
| stereo-120s-loop-streamed | 2864.132 → 2785.317 | 1.03× | 273.643 → 6.549 | 37/0/0 → 0/0/0 |
| stereo-600s-streamed | 13628.992 → 13935.740 | 0.98× | 1081.368 → 32.744 | 184/2/0 → 0/0/0 |
| six-channel-30s-streamed | 2031.415 → 2086.518 | 0.97× | 156.851 → 4.912 | 27/0/0 → 0/0/0 |

120 秒双声道 RAM 耗时减少 94.0%，分配减少 98.1%；相同长度 Loop RAM 分配减少 98.4%。RAM 输入由 30 秒增至 120 秒时，旧耗时增加约 25 倍，新耗时约 4 倍，直接读取数组消除了原 pending 头部删除的平方级搬运。

本次无 Loop streamed 双声道和六声道耗时增加约 2.2%–2.7%；Loop streamed 耗时减少约 2.8%。streamed 的主要收益是分配量：600 秒双声道从约 1.056 GiB 降至 32.744 MiB（减少 97.0%），测量内 GC 从184/2/0降至0/0/0。所有候选测量的三代 GC 均为0；本轮不宣称所有 streamed 用例都有 CPU 提速。

[原始 CSV、JSON、中位数及验证收据](../evidence/audio/xas-performance/40996371215-1/) 已归档；[archive.json](../evidence/audio/xas-performance/40996371215-1/archive.json) 记录 sourceCommit、runId、artifactId、完整归档 SHA256 和到期时间。已实际下载并复算完整 ZIP，SHA256 与 Actions digest 一致。仓库另保留七组完整原版输出的压缩包 `original-transcripts.tar.gz`；完整候选输出和输入 fixture 在该次 Actions artifact 中。
