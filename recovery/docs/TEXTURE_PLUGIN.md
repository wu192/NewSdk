# 内建 Texture 插件

## 本轮：The New World 7.1 TGA 组件与小CLI验收通过

`ColorMapType=0` 且色表 origin/count 为0时接受真实32-bit输入遗留的 entry-depth=32；
其余色表字段以及灰度/16-bit路由的原有边界保留。原DLL `41259222360` 明确拒绝
新增的16-bit/灰度非空色表说明用例，现按真实观察保留负例，不把未知格式推为正例。
TGA 2.0 footer 指向完整495字节扩展、
但扩展 size word 写成0的旧写入器现可读取（实际 `VIPEMP.tga`）；扩展/辅助区边界、
footer、像素和 RLE 检查继续保留，没有忽略任意尾部数据。

新增 `TestNewWorldTextureCodecs.ps1 -CompilerDirectory <Debug/Release父目录>
-ModDirectory <原始7.1目录> -OutputDirectory <证据目录>`：原 DLL 与生产对象分别运行，
245张原始图片（3张基础、234张带未用色表字段的APT、两流另外8张打包图）与10个边界
输入集中记录；246个正例直接比较完整三缓冲区原字节。无损 A8R8G8B8 将图像解码
与压缩搜索分开，真实原图不作头部规范化。非法元数据的原版接受情况单独保留，
不将严格子集边界冒充原版所有行为。生产 `24f83d2b` 的 Windows `41260620635`
已在新D/R及相同生产对象host逐一完成上述原字节对照，原三路、31格式及小CLI同时通过。

`fe_shared_mainMenuLib` 与 `TacticalHUDRadar` 的 Texture 声明均明确输出 A8R8G8B8，
先前跨输入观察到的差异不能归因于BC1；同输入基线 `41256811133` 的全部APT四流/CDATA身份相同。
该基线真正不同的330个DXT1 Texture位于基础流，差异限BC1块负载。

实现分支 `feat/texture-plugin` 从用户指定的 `recovery/legacy-sdk-cache` 建立，
[PR #39](https://github.com/wu162/NewSdk/pull/39)；参考
`recovery/texturecompiler-source` 的 `b30dffac5aeb027cf37a6dac0cd47beafe9fbea0`。

`Texture`、`OnDemandTexture`、`TerrainTextureAtlas` 直接编入 EXE。
运行目录只有 `NewModCompiler.exe`、`NewModCompiler.exe.config` 和
`antlr.runtime.dll`，不再生成或加载原纹理 DLL、补丁清单或独立纹理 codec DLL。
原 `SDK/tools` 二进制保留为隔离对照输入。三种纹理路由现已接入 worker：owner
只准备只读文件租约、检查目录并按输入头、DDS 长度及图集工作量预算，解码、过滤和
编码在 worker 执行。S3 scratch、插值策略及懒初始化表改为线程局部存储，
托管临时数组和 GDI+ 图像对象按调用独有；诊断按任务收集，由 owner 按提交顺序
输出。租约不可用、头部无法识别或预算不足时回到 owner，不在编码阶段加全局锁。
本轮身份恢复与并行改动已通过新的 Windows 组件、生产 CLI、SDK 和 ZIP 验收，
生产/测试源码为 `8fe12ca9b`。

| 输入 | 处理 |
| --- | --- |
| DDS / DDS 大写扩展名 | 原字节直通，保留已有 cube / volume DDS |
| TGA | true-color、灰度、索引和 RLE |
| PNG / BMP / DIB | 恢复版解码器优先；其他标准变体经 Windows GDI+ |
| JPEG | Windows GDI+ |

输出支持 SDK schema 中全部七个格式：`DXT1`、`DXT1A`、`DXT3`、`DXT5`、
`A8R8G8B8`、`X8R8G8B8`、`R5G6B5`，支持 mip 链、水平切片 volume、
OnDemand CDATA 及原 atlas 布局。NPOT 按恢复版规则向上调整为 2 的幂；
保留默认 DXT1 的 alpha 格式推断。栅格 cube 保留原拒绝行为，使用预制 DDS。
GIF/TIFF 首帧和 PPM 为额外输入能力，尚未纳入本轮主要验收矩阵。

DXT3/5 链接参考分支的公开 S3 源码及其兼容适配；DXT1/1A 使用新增通用 BC1
编码器。新 BC1 和 JPEG/GDI+ 扩展输入不承诺与旧 nvdxt/IJG 编码字节相同。
恢复版 DDS/raw/mip/图集/CDATA 布局继续接受原版差分；具体结果以验收收据为准。
来源、源文件指纹及原代码修改记录见
[源码说明](../src/Plugin/Texture/README.md) 和相邻 `SOURCE.json`。

插件版本保留原 DLL 的 `4202`，运行时 TypeHash、资产布局及三个 ProcessingHash
均保留原值：

| 路由 | ProcessingHash |
| --- | ---: |
| Texture | 2673862832 |
| OnDemandTexture | 4165508788 |
| TerrainTextureAtlas | 1182597302 |

原版隔离进程使用原始插件路由与元数据；Texture 组件对照比较含版本行的完整
原始转录，四流和 CDATA 保持原字节比较。当前不再使用测试专用身份 wrapper。

由于原身份保持不变，旧缓存中的 BC1 编码结果可能被直接复用。切换纹理实现
或进行实现对照时，可使用干净的输出目录，或通过 `/scd`、`/bcd`、`/iod` 指定
独立的会话、资产缓存和中间目录，避免混用不同编码实现的既有产物。没有新增
缓存指纹、缓存代次或自动清理；正常冷热复用仍按原 Core 合同执行。

## 验证入口

- `TestCompilerTexture.ps1`：三种路由的原版完整元数据/三缓冲/CDATA/错误对照，失败后复用。
- `TestTextureCommonFormats.ps1`：常见输入、独立 DDS/BC 解码、全部 mip 层、
  中文路径、DDS 直通和真实七资产 CLI 冷热/单图失效/独立重建/缺图重试。
- `CompilerPreparedBackend.cs` / `TestCompilerParallel.py`：三路纹理准备、输入租约、
  预算回退、串行/并行完整输出、worker 执行及诊断顺序的生产对象验证。
- Core lifecycle 工作流：Debug/Release、新生产对象、CLI/取消、缓存、完整
  mymod 原版差分和独立三文件 ZIP 验收。

## 当前验收：原身份与并行

[Windows 41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818) 整体
**success**，生产/测试源码 `8fe12ca9b1eea1ab0d67ebc964f5a4ac1b2f462d`：

- Debug/Release 各31格式用例、532断言通过；结果与上一轮已验收的31用例原始
  JSON全等，包括每份DDS哈希，TLS改动未改变这些既有编码结果。
- 16个真实贴图CLI用例通过；两配置的jobs1/4/auto冷态输出全等，jobs4/auto
  均实测4个任务重叠。热态切换jobs为零编译，单图同长同mtime修改仅重编1项。
- 三路8资产worker波次比较三缓冲和私有CDATA，覆盖冷S3灰度表、图集输入租约；
  owner诊断一次回放和贴图故障/取消后的完整join及临时文件回收通过。
- 原三路4202完整转录逐字节相同，无oracle身份包装；64生产并行/缓存用例、
  Core/CLI/取消、完整缓存矩阵、完整mymod原BAB四流/BIG/SKUDEF对照通过。
- 两个独立三文件ZIP及完整Upheaval22样本通过；全部样本原输出一致。

[常见格式及贴图CLI原始收据](../evidence/texture/plugin-validation/41185772818-1/common-summary.json)、
[完整验收与归档复算](../evidence/texture/plugin-validation/41185772818-1/full-summary.json)
记录二进制、原转录、线程区间与原始收据。
[当前正式产物](https://github.com/wu162/NewSdk/actions/runs/41185772818/artifacts/13037167160)
的 `artifacts/final-release/41185772818-1/` 提供 Debug/Release ZIP 与清单。
未新增游戏内验收，不从样本推导通用加速比例。

## 历史证据

以下为上一轮产品证据，不覆盖本轮原身份恢复、TLS 与 worker 并行改动。

上一轮生产/测试源码 `4c74bf64b`，
[Windows 41180859852](https://github.com/wu162/NewSdk/actions/runs/41180859852) 整体 **success**。
Debug/Release 各31个常见格式组件用例、完整mip与半透明边界、中文路径及
12个真实CLI缓存/失效/失败重试用例通过；两份组件结果原字节相同。
原三路纹理差分、Core/CLI/取消、64后端/cache用例、全部缓存组、完整mymod
原版四流/BIG/SKUDEF、两个独立三文件ZIP及完整Upheaval22样本通过。

[完整收据与归档复算摘要](../evidence/texture/plugin-validation/41180859852-1/full-summary.json)、
[常见格式原始收据](../evidence/texture/plugin-validation/41180859852-1/common-summary.json)
已入库。两个原归档已下载复算；六份运行文件和两个ZIP逐字节核对构建、
发布清单与common收据，原版/恢复版纹理转录按原字节压缩保存并去重。
[下载归档](https://github.com/wu162/NewSdk/actions/runs/41180859852/artifacts/13035436860)中
`artifacts/final-release/41180859852-1/` 含 Debug/Release ZIP 和身份清单。
该归档保留上一轮运行文件；当前版本使用上节的新构建和收据。
此前 Windows 构建、三文件部署、Core/CLI/取消、缓存、原纹理三路字节及 BIG
差分通过；整体运行因控制器残留变量、跨扫描行 RLE 测试输入及 indexed BMP
颜色问题失败。控制器/生成器已修正；标准 1/4/8-bit BI_RGB 调色板改为显式读取，
原 direct-color 解码器保留。原失败记录与已下载复算的归档身份见
[evidence/texture/plugin-validation](../evidence/texture/plugin-validation)。
常见格式矩阵现按独立用例集中收集失败，任一失败仍拒绝整组；没有降低像素阈值
或原始四流/CDATA 差分要求。原失败不冒充成功；本轮验收使用恢复原身份并启用并行后的新构建产品。

本轮 `24f83d2b` / [41260620635](https://github.com/wu162/NewSdk/actions/runs/41260620635) 已通过新生产D/R与相同对象host验证：255个输入中246个正例三缓冲与原DLL逐字节相同；9个边界/观察记录保留，原三路完整转录、31个CommonFormats及小型CLI的冷热/修改/失败重试均通过。原始归档已下载核验。[组件收据](../evidence/codecs/new-world/41260620635-1/summary.json)。用户要求的codec验收未编译完整MOD；PR#43已获授权合入性能分支，原始媒体只剔除89Joypad后的全15流计时41261694766已完成：原1921.681→新410.182秒，各4058文件，仅既有330个BC1块不同。BC1既有压缩字节差异不在本修复内。
