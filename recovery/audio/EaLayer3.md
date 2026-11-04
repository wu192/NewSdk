# RA3 EALayer3 V1

## 本轮：8声道WAVE已通过组件与小CLI验收并合入性能分支

生产源码 `24f83d2b`，[Windows41260620635](https://github.com/wu162/NewSdk/actions/runs/41260620635) success。
标准/EXTENSIBLE WAVE8 PCM8/16/24已接通NONE/XAS、循环、重采样和四对EALayer3；
8声道保持原序，既有4/6重排不变；AIFF/AIFC仍限1/2/4/6，ADP4仍限mono/stereo。
原SDK与候选D/R每配置4347条三缓冲/CDATA转录一致，含864条Wave8和4条真实WAVE；
真实 `FX_kanade_spell.wav` 的XAS输出397648字节/SHA256 `892b3c9b00929a2adb28d88d8c455401ebac72d82313b7cb878c54851a75fd67` 三方相同。
50个EALayer3输入在D/R共100次独立解码、声道/长度/Loop/波形与jobs1/4通过；
生产CLI52个音频资产的冷jobs1/4、热、改WAV、独立重编及再次热态通过。正式运行仍为EXE/config/ANTLR三文件。

[验收收据与运行文件身份](../evidence/codecs/new-world/41260620635-1/summary.json) 已保存。
按用户要求未额外编译完整MOD或mymod用于本次codec验收。用户授权的[PR#43](https://github.com/wu162/NewSdk/pull/43)
已以 `a15e3c1e` 合入性能分支；[修复版全15流性能41261694766](https://github.com/wu162/NewSdk/actions/runs/41261694766) 已完成，原SDK1921.681→修复版410.182秒；新旧均用原始媒体、仅剔除89Joypad。
各4058输出，真实8声道XAS原SHA一致；仅330个既有BC1资产块不同，未声称全输出等价。
[完整同机计量与边界](../docs/NEW_WORLD_71_SDK_COMPARISON.md)。

实现分支：`feature/ra3-ealayer3`，基线 `recovery/legacy-sdk-cache` / `eb503513`。
实现及 Windows 验收已通过；[PR #34](https://github.com/wu162/NewSdk/pull/34)，未经用户批准不合并。

## 编码与来源

`AudioFile PCCompression="EALAYER3"` 现在选择 AudioPolicy representation 31，输出 SNR
codec **5 / EL31 / EALayer3 V1b**，不再输出 PCM 降级警告或 PCM 替代数据。
V2 PCM/spike、TW 的旧 EALayer3、Xbox/XMA 不在实现中。

选取 sagetools `49373be5e922e8d7ae0b2caa2c74fa6c64d6cf4c` 的
`mp3toea`、LAME 3.96 和起始混合所需 V1 解码器依赖闭包；编码调度按
`encealayer31.cpp` 的算法接入。包含 MPEG reservoir 展开、每 576 帧 granule、
声道对交错、1105 帧延迟补偿、47 帧原始 PCM/解码结果 crossfade、末尾补齐。
仅复用必要音频源码，没有引入 SIMEX、外部转换程序或第三方工程/工具链。

参考与验证：

- RA3 插件 `jonwil/ealayer3compilerplugin` / `ed407302ae63cd6ba849af586ef765c03ca008b3`：
  其源码选择 codec31，但随附 audio.dll（SHA256 `149de43e1e7c914b8e44dd5e0cdcbed45de33890ebd2708c8223b278874a610f`）
  实测输出 SNR6/V2 PCM；保留原样的元数据/波形对照，并另以 codec30 及对应质量分支生成
  明确标记的 V1 参照。V2 quality0 的参考/候选波形相关系数约 0.67，保留测量，
  不声称跨变体波形等价；候选自身必须通过对源波形的独立严格检查。
  显式选择 V1 后，随附旧库的 LSF（16/22.05/24kHz）参照只有静音或至多起始 47 帧，
  完整记录为不可用参照；MPEG1 的 32/44.1/48kHz 和重采样 Loop 共 4 例保留严格波形对照。
  这不降低候选自身在六种声明采样率上的源波形、长度、Loop、jobs 检查。质量默认 75、范围 0–100，RAM/streamed 的资产合同沿用原插件。
- `jonwil/snrtool` / `34308631b0bf94a57d1d254b651ee191346377ab`：
  `ealayer3_int=30` → SNR5、`ealayer3pcm_int=31` → SNR6、流式块、下一 64 字节边界填充和循环段。
- `jonwil/lame-3.96` / `ed9d6f623109775b89159ed506c20afb1e8f20b4`：核对 LAME 来源。
- vgmstream r2168：独立解码 EL31b。Windows ZIP SHA256
  `692d45070852a35c1fa42726aa3c9de528e44eec925d37d99b1074256b99a229`。

许可证/版权保留在第三方源码及 `Native/Audio/EaLayer3/NOTICE.md`、COPYING 文件。
按用户要求，不嵌入 EXE/DLL，也不向运行时部署包增加协议文件。
GPL/LGPL 的源码分发条件仍适用，分发二进制时应标明匹配的公开源码版本。

## 接入与边界

沿用 `PcmWaveAudioCompiler`、现有 WAVE/AIFF 读取、filter120 重采样、Loop 读取、
`PcmEncodingResult`、`AssetBuffer`、字幕及 CustomData 输出；没有平行资产管线。

- 输入沿用 integral WAVE/EXTENSIBLE、AIFF/AIFC NONE 的既有解析能力。编码器接收
  1/2/4/6/8 声道浮点 PCM，4/6/8 声道以两个/三个/四个独立 stereo 编码器交错。
  WAVE4/6 原有重排仅做一次，WAVE8及AIFF保持原序；AIFF/AIFC输入仍限1/2/4/6。
- 采样率：16000、22050、24000、32000、44100、48000 Hz。`PCSampleRate` 先走
  既有重采样。其它输出率明确拒绝，不自动改率或降级 PCM。
  所选上游 V1 启动解码器的 MPEG2.5 `sfreq` 分支没有初始化，未将
  8000/11025/12000 标为支持；更高/任意非 MPEG 采样率亦不标为支持。
- `PCQuality` 按 RA3 插件的十进制/SageInt 解析，默认 75，范围 0–100；映射
  到 LAME `VBR_q=int((100-quality)*9/100)`，VBR mtrh、算法 quality2、joint stereo。
- RAM：SNR 头及帧块放 CustomData。streamed：SNR 放运行时 HeaderData、SNS
  放 CustomData，独立段的结束标记和下一 64 字节边界填充包含在块尺寸中。
  RAM 单段必须符合 SNR 24 位块长度；超限明确提示使用 streamed。
- Loop：保持既有 WAVE inclusive、AIFF end-marker exclusive 的转换；在重采样后
  分别编码 intro/loop、各自补偿延迟，streamed SNR 写入真实 loop byte offset。
  SNR 在 loopEnd+1 结束，运行时 NumberOfSamples 保持原输入/重采样后的总帧数。

每个任务/段独立持有 LAME、reservoir、granule 队列及启动解码器。固定 LAME 表只
初始化一次；上游合成 scratch 改为调用内数组，Huffman count descriptors 改为实例字段。
没有 AudioFile 全局锁、外部进程、独立音频缓存或输入 SHA256。

正常 `BuildCompiler.ps1` 将选定原生源码直接链接进 EXE，仍为原五文件运行时。
独立 AudioCompiler csproj 自动构建、复制并 publish `NewSdk.EaLayer3.dll`；机器需
正常 Windows MSVC x86 构建工具，不要求用户另装编码器。

缓存身份按宿主区分。独立 AudioCompiler DLL 供原 BAB 使用，仍保留插件版本
548405253、类型级 AudioFile ProcessingHash 2414318215，原 BAB 无实例级扩展入口。

单进程 EXE 让 NONE/XAS/默认压缩恢复原 ProcessingHash 2414318214；只有显式
`PCCompression="EALAYER3"` 的实例使用 2414318215。Core 在 XSD/继承展开完成后选择
实例身份，正常 CDATA 文件名与原 SDK 保持一致，新 EALayer3 不会命中旧 PCM 降级键。
EXE Audio 插件版本为 548405254：沿用会话中已有的 AssetCompilersVersion，版本变化时
只让缓存音频声明重新加载，以区分前一版统一哈希和原 SDK 的旧 EALayer3；新会话再次
热构建可复用两种实例身份。其它资产继续按原 TypeHash/ProcessingHash 检查，不增加
缓存格式或侧车。TypeHash、SessionCache18、DocumentProcessor11、编码算法及数据合同不改。
本次生产源码 `8050f22d` 的 [Windows 41244132060](https://github.com/wu162/NewSdk/actions/runs/41244132060)
已整体成功；Debug/Release、Core/CLI/取消、缓存/会话、完整 SDK、两个 ZIP 和完整
Upheaval 均通过。正式归档与 D/R 三文件、两份 ZIP 已下载核对。

[原 SDK 修复后对照 41245972797](https://github.com/wu162/NewSdk/actions/runs/41245972797)
也已 success：完整 mymod 与历史完整 Upheaval 输入，base loose 流、冷/热/禁缓存，
20 配对/40 构建的所有文件名、长度、SHA256 和原始字节相同。Upheaval 的1289份
AudioFile CDATA及四个链接流差异归零，没有替换原版元数据或归一化输出。
旧会话升级实测只重编译2个 NONE/XAS 身份，44个 EALayer3 的名称与负载哈希不变；
升级热态0解析/0编译，修改同一WAV只重编译3个依赖资产并与46资产独立重建原字节相同。
[全部新收据和原始计时](../evidence/refactor/original-mod/41245972797-1/summary.json)
包含本次补收的9个混合音频用例；初轮详细JSON未被上传路径包含，原成功日志保留。
运行文件身份和四流原始文件已下载复算。未新增游戏内验收或其它平台支持。

## 验证

入口 `.github/workflows/audio-ealayer3.yml`：先 Debug/Release 真实插件调用、jobs1/4
完整缓冲区及 CustomData 比较、vgmstream 解码/波形/声道顺序/长度/Loop，再运行
jonwil RA3 插件格式/解码信号对照、既有 NONE/XAS/MP3/PathMusic 原字节回归。
专项通过后才运行正式 BAB 加载新 DLL、正常产品 D/R 构建、生产 CLI 缓存/并行/音频
修改/独立重编译，最后集中执行一次现有完整 mymod SDK 验收。

Windows [41149785200](https://github.com/wu162/NewSdk/actions/runs/41149785200) **success**，
源码/二进制均为 `4ddac274b859be7fdb537df8d21d1e25e7ef8e06`；之后仅补文档与原收据归档。

- 独立 DLL 的 Debug/Release × jobs1/4：44 输入 × 4 次实际编码共 176 个资产；
  88 个候选产物逐一由 vgmstream 解码并检查源波形、声道顺序、SNR/SNS/运行时元数据、
  Loop、块尺寸/填充/终止。每配置 jobs1/4 的完整三缓冲区和 CustomData 原字节一致。
- 原 jonwil RA3 插件：29 条元数据/解码/差异测量；明确输出 V2，不声称跨变体波形等价。
  显式 V1 参照：4 条有效 MPEG1/重采样 Loop 严格波形对照通过；25 条 LSF 不可用参照
  的静音/起始采样及原始输出保留，没有把它们计为 V1 波形等价通过。
- 既有格式每配置 521 条完整原字节比较通过：business/MP3/PathMusic 77、PCM8、XAS12、
  真实输入8、WAVE Loop216、AIFF Loop200；另有每配置 12311 条元数据记录，仅允许明确的
  插件版本/AudioFile ProcessingHash 更新，以及原有平台和输入边界拒绝检查。
- 正式 BAB 加载新 AudioCompiler/原生 DLL，实际完成 44 个 AudioFile 回调；三缓冲区和
  CustomData 与组件输出完全一致。
- 正常生产 EXE Debug/Release 新构建成功，两个配置均保留五文件运行时。
  生产 CLI 冷 jobs1/4 各编译44资产、全部输出原字节一致，实测最大 backend concurrency=4；
  热态 xmlParse/instanceCompile 均0；修改1个 WAV 只编译1资产，与独立无缓存44资产重建
  的全部输出一致，再次热态恢复0编译。没有新增音频缓存或外部编码进程。
- 完整、未精简仓库 mymod 的 Debug/Release 均通过原 SDK 四流/CDATA、LoD、BIG/SKUDEF
  原字节对照；输入未变，每配置0错误/82警告。该 MOD 实际只有 base/Mod，空的 APT/maps
  继续为空，不据此声称额外 APT/map 语料或完整 Upheaval 新验收。

两个 Actions ZIP 已下载并实际复算 SHA256。原始 JSON 收据、二进制/运行文件身份及
[统一摘要](../evidence/audio/ealayer3-validation/41149785200-1/summary.json) 已归档。
早期失败保留于 Actions：输出路径引号、测试宿主路径/CLR 配置、上游静态 synthesis
scratch 的并行差异、AIFF fixture 的 end marker，以及新工作流缺少既有 dnfile 审计依赖。
没有修改 Texture 生产代码或降低原审计要求。游戏内运行未执行，不能把解码成功视为
游戏内播放验收。
