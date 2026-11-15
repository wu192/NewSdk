# Legacy Tokenizer Dozer-only：候选包已构建，严格 DLL-only 门槛失败

2026-11-15。分支 `feat/legacy-tokenizer-dozer-only`，草稿 [PR #17](https://github.com/wu192/NewSdk/pull/17)，未合入。
测试源码 `6ba78d5b8bc0b8e36e7f13c50c7e3db950e64920`，[运行 43698049060](https://github.com/wu192/NewSdk/actions/runs/43698049060) attempt1。
CLR 2/x86 单 DLL 加载、程序集身份、原三类型 28 样本/84 缓冲区逐字节对照、8 个 Dozer 合同样本均通过。
补齐必要 XSD 后，原仓库老 SDK 定向示例编译无输入错误，最终 Mod 的 NativeDozer 三缓冲区合同通过。
严格保留原 XSD 时 exit -1：基线 AIModule 缺少 DozerAIUpdate 声明，因此整体方案未完成验收，工作流 failure 属实。
候选包、精确限制、DLL 指纹与续接条件见 [交付说明](docs/LEGACY_TOKENIZER_DOZER_DELIVERY.md)；[结构化证据](evidence/legacy-dozer/validation.json)。
此前 SDK 稀疏检出缺少 Art 的运行不能算完整集成，已由本次 39 项原始依赖及最终流检查替代；28 个 Art 输入是原基线自带占位声明，未重建模型贴图。
剩余：严格 DLL-only 要求与原 XSD 缺项矛盾；不得把候选包宣称为完全按该门槛验收通过。未增加其他功能或修改 EXE/config/其他 SDK DLL。

---

# EALA.Hash 交接：源码恢复与原版差分完成

2026-10-26。工作分支 `recovery/eala-hash-source`，目标 `recovery/compiler-source`，PR #12 等用户批准后合入。
纯 C# 源码 `recovery/src/Managed/Hash`，全部六个公开重载和可构造类恢复。
源码 `e300225c`；Windows run `40262767940` attempt1 success，D/R各84380行转录与原版一致。
非法越界长度恢复侧明确拒绝，原版该输入是未定义内存读取。详细行为、原IL与公开实现对比见 [HASH.md](HASH.md)。
未替换SDK/tools或改变Core引用，未做真实SDK集成验收。本任务没有剩余源码实现待办。

---

# AudioCompiler 交接：PC 功能已验收，等待 PR 审查

2026-10-26（北京时间）。唯一工作与交付分支 `recovery/audio-compiler-source`。
仅PC/Win32；不负责最终EXE或重构。禁止强推；任何合入必须先PR并由用户明确批准。

最后一组AIFF/AIFC NONE4/6已完成。源码 `7abc0505f6340504f32767b86de7a34b04ac3367`，音频树 `8bbe47d084d879b38397bc45e4493965640731ec`；
[运行40257973827](https://github.com/wu162/NewSdk/actions/runs/40257973827) attempt1 success，证据提交 `47823e9451018654e61237ef32cfde36d463f22e`。
D/R各6336项三缓冲区/CustomData差分、7740源序样本、72窗口、432marker独立检查、108安全拒绝通过；
全部受影响回归及D/R完整工程构建通过。真实SDK同6336资产全部实际回调，四产物/CustomData一致，恢复侧原音频DLL缺席。
细节与准确范围见[AIFF验收](audio/MultichannelAiffBoundary.md)及[验收索引](evidence/audio/acceptance-current.json)。

当前支持WAVE/AIFF/AIFC NONE的PCM8/16/24、1/2/4/6声道；raw ADP4仅1/2。
WAVE4/6导入重排，AIFF/AIFC保持源序。PCM/XAS、RAM/stream、loop、filter120重采样、MPEG信息/透传及五个BAB路由已验收。
多声道ADP4原native不支持；非PC、未定义原生读取、单端marker、越界loop/SSND及单帧ADP4/import-resample继续拒绝。

约定PC功能范围内无剩余实现待办。下一步审查[PR #11](https://github.com/wu162/NewSdk/pull/11)，目标 `recovery/compiler-source`；未获用户明确批准不得合入。
SDK/tools/config未替换。完整固定Upheaval仍保留source`c078bce6`/run40248486242原锚点，
不冒充本轮完整MOD重跑；本轮另有6336资产专用SDK。旧WAVE/1820/4488等证据保持原范围。
