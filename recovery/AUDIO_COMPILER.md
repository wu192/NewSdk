2026-10-31 新增真实 EALayer3 V1b 编码及 Windows/BAB/生产 CLI 验收，见
[EALayer3 当前能力及证据](audio/EaLayer3.md)。下面的固定历史验收记录保留原来源。

# AudioCompiler 当前状态

音频工作流使用 `contents: read`，保留全部构建、差分、真实 SDK 断言与产物上传。
每次运行的证据保存在对应 Actions artifact；工作流不再自动提交或推送仓库。
需要将证据长期登记到源码分支时，由主代理核对具体 run/sourceCommit 和文件后回收。
音频目录迁移会因工程 HintPath 修正改变源码树哈希；历史验收指纹不改写。
旧 Upheaval 里程碑的严格树匹配在新树尚未正式验收时会拒绝复用，不能作为新树通过证明。

2026-10-26。分支 `recovery/audio-compiler-source`；源码 `recovery/src/Managed/Audio`；仅PC/Win32。
约定PC编译功能范围已完成，组件及真实SDK已验收，待PR审查。合入仍须用户明确批准。
生产源码不加载原AudioCompiler；原DLL只作测试oracle。仓库SDK/tools/config未替换。

## 最终能力组

AIFF/AIFC NONE4/6：源码 `7abc0505f6340504f32767b86de7a34b04ac3367`，音频树 `8bbe47d084d879b38397bc45e4493965640731ec`；
[运行40257973827](https://github.com/wu162/NewSdk/actions/runs/40257973827) attempt1 success，证据提交 `47823e9451018654e61237ef32cfde36d463f22e`。
D/R各6336项完整三缓冲区/CustomData差分；独立源序/窗口/marker及安全拒绝检查、全部受影响回归和D/R工程构建通过。
真实SDK同6336资产全部实际回调，四产物、CustomData及三缓冲区一致，恢复侧无原目标DLL。
详细矩阵、边界与收据见[AIFF验收](audio/MultichannelAiffBoundary.md)；统一索引见[acceptance-current.json](evidence/audio/acceptance-current.json)。

## PC 能力与边界

| 范围 | 已实现 |
|---|---|
| BAB路由 |PathMusicMap/Track/Event、AudioFileMP3Passthrough、AudioFile|
| 输入 |WAVE（含EXTENSIBLE）、AIFF/AIFC NONE：PCM8/16/24，1/2/4/6声道；EA raw ADP4仅1/2（非Apple ima4）|
| 声道顺序 |WAVE4取源[0,1,3,2]、6取源[0,2,1,4,5,3]，仅一次；AIFF/AIFC NONE保持原序|
| 编码 |NONE/PCM16、XAS v1；RAM/streamed SNR/SNS；filter120重采样至少两源帧|
| Loop/COMM |WAVE首smpl/首range；AIFF/AIFC forward sustain、多INST/MARK与重复marker；最后COMM signed声明帧数先筛选marker，随后SSND修正actual frames|
| SSND |blockSize忽略；offset完整采样窗口必须在声明payload内，保留raw-length帧数|
| MP3透传 |七帧识别、EOF终止、MPEG头信息及有界容器优先级；原文件逐字节复制；原SIMEX_info不解PCM|

工程net472、无签名；Core仍引用SDK基线。
`dotnet build recovery/src/Managed/Audio/BinaryAssetBuilder.AudioCompiler.csproj -c Release`。
不承诺原混合模式C++指针helper二进制ABI。最终EXE整合、重构和非PC平台不在本任务范围。
约定范围内无剩余实现待办。

继续明确拒绝MARK-only未初始化mode、单端匹配、无效loop、结构损坏/marker超限、超SSND offset、单帧ADP4/import-resample。
PCM32/IEEE-float、sowt、多声道ADP4等原路径拒绝格式不是待恢复功能。
尚未审计的畸形容器不承诺普遍等价。

## 保留的历史验收

- WAVE4/6：source`ed1a7bcb`/run40250750964，D/R1728项及同1728资产SDK通过，见[WAVE验收](audio/MultichannelBoundary.md)。
- 完整固定Upheaval：source`c078bce6`/run40248486242，固定输入`068643219fded09a05fb7b75c6507b769c430316`，原/恢复共10次BAB调用，20产物、每侧1293份CustomData一致；1281唯一资产、1289回调。只替换音频，其他插件原SDK。语料mono WAV，未冒充本轮源码重跑。
- PC边界：source`2d646847`/run40244825458，D/R loop4224、optional3888、COMM7776、offset2704、MPEG1084及同1820资产SDK通过；4488资产旧SDK保留source`bb4a225`/run40243817293。
- AIFF原版源序：source`0d744a71`/run40251368891，192案例/384进程稳定，80496个PCM样本确认源序，见[原生审计](audio/MultichannelAiffNativeAudit.md)。

历史source/run不交叉套用；精确完整锚点与收据均保存在验收索引。原PR#10已按用户要求关闭，新的合入须新PR。

[Loop](audio/LoopMetadataBoundary.md) · [COMM](audio/AiffMarkerRangeBoundary.md) · [Offset](audio/SsndOffsetBoundary.md) · [MPEG](audio/MpegBoundary.md) · [重采样](audio/ResamplerKernelAudit.md) · [单帧安全](audio/SingleFrameImportBoundary.md)。
原DLL SHA256：`a14d15addef502c2df6f6ba272d6098c4c131fbea9c3ed0149c11c4556347139`。
