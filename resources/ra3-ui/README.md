# RA3 UI：原始资产、FLA 与 ActionScript

资源分支：`resources/ra3-ui-assets`；起点：`recovery/legacy-sdk-cache` 的 `52d44c6267385055df6da91ecdc6ee2b44f467a9`。

原始导入保存了全部 **6,284 个文件、174 个真正的二进制 MX 2004 FLA、4,485 个 AS 文件**。本分支的直接 APT 修改例外见下文。FLA 的 Contents 修订为 `0x38`，可被本仓库完整解析。

AS 文件名根据实际元件、实例、帧、标签、函数或事件生成，53 份类/接口文件使用实际类名。例如：

- `A01_Global_Briefing_frame_0001_define_playSound_CrossHairFlash_label_wait.as`
- `tabHelpSite_frame_0001_construct.as`
- `fem_m_gameSetup.as`

FLA 通过相对 `#include` 读取时间轴/事件 AS，通过 `import` 使用类文件。保留每部影片的目录结构即可直接编辑。`script-map.json` 记录文件与原场景、元件、帧的对应；没有纯数字 AS 文件名。

## 文件

| 路径 | 内容 |
| --- | --- |
| `converted/<Movie>/` | FLA、实际参与编译的 AS、脚本映射 |
| `source/ra3ui/` | 原包资产及本分支直接编辑的 APT 资产；断电按钮以该目录为实际输入 |
| `original/ra3ui.7z.001`–`.003` | 原包按 64 MiB 分块，避免 GitHub 单文件限制；无 Git LFS |
| `tooling/ffdec_26.4.0.zip` | 固定本地转换工具，含其许可证 |
| `provenance.json`、`source-inventory.json` | 来源、原包和工具 SHA-256、全部原文件校验 |
| `conversion-report.json` | 174 部影片、4,485 个 AS 的路径、哈希及依赖状态 |
| `validation/summary.json` | 最终验收摘要 |
| `validation/compiler-report.json` | 逐影片结构与最终四流编译结果 |
| `validation/frame-comparison.json` | 原 SWF 与最终 FLA 帧数、帧率及身份对照 |
| `validation/as-edit-proof.zip` | 改名后的真实 AS 修改、前后四流及证明报告 |
| `validation/as-renames.json` | 4,432 份时间轴/事件脚本的命名依据和改名前后映射 |

原包由 Jonwil 从 EA 发布的 EAF 重建 SWF，包含其最终补丁版 igm_pauseMenu 修正。本次 FLA/AS 是这些 SWF 的恢复产物。PPM 页面需要验证码，因此使用原作者公开的同名镜像，来源和核验范围见 `provenance.json`。原始归档和恢复结果保留导入版本，空目录由原包分块保留。本分支修改的 raw APT/CONST/XML/RU 另有明确来源与验证记录，不能再对整个 source 目录声称逐字节原版。

## 本分支：直接 APT 断电按钮

[源码与重建说明](source/ra3ui/TacticalHUDCommandModeBar/POWER_TOGGLE.md)。
新增按钮实际修改 `source/ra3ui/TacticalHUDCommandModeBar/` 下的 APT、CONST、SDK XML 和 RU，
以该目录的 `editing/*.apt.xml`、typed CONST JSON 直接重建；不使用 FLA/AS 构建按钮。
SDK 发布仍走原 `AptAptData/AptConstData/AptDatData/AptGeometryData` 资产入口。
原 SWF/EAF/TAC 是历史快照，`converted/` 是原恢复结果，两者不会自动随此按钮修改而更新。
`source-inventory.json`、`conversion-report.json` 及下述174部影片报告描述原始导入/恢复；
修改后的按钮另见该目录构建及验证收据，未作游戏实测。

## 验证结果与实际边界

- **174/174** 完整解析 Contents、全部引用页和媒体；全部帧数、帧率与原 SWF 一致。
- **173/174** 通过本地生产 `Plugin.CompileFolder`，实际生成 `.manifest/.bin/.relo/.imp`。
- `sampleMissionBriefing` 的 FLA 完整通过解析，但当前编译器明确拒绝嵌入音频。恢复时按真实解码 WAV 修正 sampleCount；全部 PCM 字节保留，未声称完成 MP3 延迟的逐样本补偿。
- 修改 A01 的真实语义命名 AS 后，FLA SHA-256 不变，最终 `.bin` 改变、含唯一标记，AS 依赖被记录。
- 157 部原先已通过的影片，改名并更新 include 后四流 SHA-256 **全部逐一相同**。
- UI 全套 net8 组件验收：**778 checks passed**；net472 类库构建：零警告、零错误。为真实源代码补充空 marker interface / implements 支持，保留 AVM1 ImplementsOp 和 instanceof；完整接口类型系统仍不支持。

原包另有 **7 部影片、15 个被实际使用的缺失符号**。有的缺整个库，有的库内没有所需导出；原 SWF 与 EAF 均有相同缺项。已保留原文件和恢复结果，未伪造缺失内容：

| 影片 | 原包缺项 |
| --- | --- |
| `LoadScreen` | GameWindowGadgets、ShellContent、Commander、MpGameSetup |
| `LoadScreenImage` | ShellContent |
| `LoadScreenCampaign` | ShellContent、ShellButtons |
| `InGameChat` | GameWindowGadgets |
| `TacticalPlayerStatusDialog` | ShellButtons |
| `testCafeUI` | std_mouseScrollTextBox，以及下拉菜单库缺少指定导出 |
| `skins` | std_MouseCheckBox 缺少三个导出，实际用于位图填充 |

具体符号、引用路径、SWF/EAF 哈希和字节偏移见 `validation/missing-original-imports.json`。这些界面即使能生成四流，也不能据此视为显示和行为完整。167 部依赖闭合，其中 166 部同时通过组件编译；另一个为上述音频示例。此处未执行完整 Windows SDK 产品或游戏内播放验收。

## 使用与离线复现

下述方式用于原恢复 FLA；本分支断电按钮使用上面的直接 APT 入口。选择已验证且依赖闭合的原恢复影片，将整个 `converted/<Movie>/` 放入 MOD 的 `Data/AptUI/<Movie>/`，保留 AS 相对目录。本仓库的 FLA 自动发现会处理该目录。

原包、全部素材和固定工具均已入库。本次未新增或调度下载它们的 GitHub Actions；资源分支的检查点推送未产生 Actions 运行。

恢复原压缩包：

```bash
python resources/ra3-ui/tools/restore_archive.py work/ra3ui.7z
```

使用 JDK 21（含 jdk.compiler 模块）和 Python 标准库重做转换，无需下载器：

```bash
python -m zipfile -e resources/ra3-ui/tooling/ffdec_26.4.0.zip work/ffdec
python resources/ra3-ui/tools/convert.py --source resources/ra3-ui/source/ra3ui --destination work/ra3-ui-recovered --ffdec work/ffdec/ffdec.jar --work work/ra3-ui-conversion --workers 4
python resources/ra3-ui/tools/verify_exports.py work/ra3-ui-recovered
```

转换保留旧式补间；解析并内联存在的共享库；从真实 DoInitAction 恢复类并去重；使用 FFDec 官方设置消除推断字段并保留明确变量绑定；为每帧/元件生成可编辑 include，再按语义命名并生成 FLA。`normalize_xfl.py` 依据原 SWF 最后导出绑定处理重复名称，保留全部形状。

本地重新编译和 AS 修改验证（需要 .NET 8 及项目依赖）：

```bash
dotnet run --project resources/ra3-ui/tools/validation/UiValidation.csproj -c Release -- resources/ra3-ui/converted work/ra3-ui-validation --mutate-real A01_Global_Briefing
```

全量命令会因明确不支持的音频示例返回非零；查看逐电影结果。验证器修改临时副本，原 FLA 和 AS 保持不变。
