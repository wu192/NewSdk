# 编译器最小发行包

## 当前分支候选：完整缓存恢复（2026-10-29）

`recovery/legacy-sdk-cache` 的 [PR #19](https://github.com/wu162/NewSdk/pull/19) 已通过[完整运行 40814401237](https://github.com/wu162/NewSdk/actions/runs/40814401237)，待审查，未合入 `compiler-main`。生产二进制、测试及发行源码均为 `39039961847cd1ca9cb93743fd51741a4e5c9f49`，Debug/Release 在该运行重建；后续文档提交不改变来源。

候选恢复缓存默认开启、三种纹理、XML/Deflate、部分/冻结会话、touch 及旧目录。完整生产矩阵、mymod 原 BAB/LoD/MakeBig/SKUDEF 差分，以及五文件包独立解压后的生产 BIG 和协议均通过。[准确覆盖边界](docs/LEGACY_INCREMENTAL_CACHE.md) 与[核对摘要](evidence/refactor/legacy-cache/40814401237-1/summary.json)。

[下载候选及验收产物](https://github.com/wu162/NewSdk/actions/runs/40814401237/artifacts/12871674302)。外层归档内的发行包路径：

- `artifacts/final-release/40814401237-1/NewModCompiler-Win32-Release.zip`
- `artifacts/final-release/40814401237-1/NewModCompiler-Win32-Debug.zip`

外层归档为 137119679 字节，SHA256 `6c395456add02dec40c7759e9cc5a26d34ea353f5e3ee588a12a78df3bfab044`，GitHub 元数据、上传日志及后续本地重新计算一致，保留至 2027-01-27。首次 CLI 下载 HTTP 403 的历史保留；性能基准续接时已通过 GitHub 产物下载工具取得原始 ZIP，读取原始清单/收据，并校验完整 Release 五文件和 Texture 绑定。此候选链接不更改主线既有发行来源。

| 正式 ZIP | 字节数 | SHA256 |
| --- | ---: | --- |
| NewModCompiler-Win32-Debug.zip | 2227129 | `e4e2df5e76c16f51406aea3ee141d72a96b0f65a1245da582fb84c7e4fce8d92` |
| NewModCompiler-Win32-Release.zip | 1726007 | `343198bc65976bf0cbad852ef2b64419e8ee9b44add8f14c6e0fdd9fa07f9b8d` |

## 前次主线修复版工具（2026-10-27）

默认 `/ss:true /pc:true /slowclean:true` 已恢复，并支持逐项关闭；缓存默认关闭。本轮 [运行40440661815](https://github.com/wu162/NewSdk/actions/runs/40440661815) 整体成功，二进制、测试和发行源码均为 `57354fbc013369eac1ad4c6e3346134d81a68a6c`。后续文档提交不冒充新的二进制构建。

完整未精简仓库 mymod 的新旧 MOD 四文件逐字节一致，此前12个额外引用流文件已消除；两配置真实 MOD BIG 的12成员与原 BAB→LoD→MakeBig 原始内容一致，SKUDEF也通过。完整应用/Core/CLI/Texture/全部缓存/P7及最终五文件ZIP独立运行均通过。[详细证据和保留的原失败](docs/SDK_SCRIPT_VALIDATION.md)。

[下载本轮工具和完整验收产物](https://github.com/wu162/NewSdk/actions/runs/40440661815/artifacts/12707135831)。解压外层归档后，取 `artifacts/final-release/40440661815-1/NewModCompiler-Win32-Release.zip`；Debug ZIP也在同处。运行ZIP内部仍严格五文件，外层归档包含测试/构建证据。Actions产物可能要求GitHub登录，当前保留至2027-01-25。

| 正式 ZIP | 字节数 | SHA256 |
| --- | ---: | --- |
| NewModCompiler-Win32-Debug.zip | 2223949 | `580bded497b05dd04fbba9e5ea7e3e1abe049d1855b71d1a23daac2013926017` |
| NewModCompiler-Win32-Release.zip | 1721549 | `e88e9368e7fbcd158fc29e00f0c588ae625b5a6435c9a9a3f69d4e7e1b6e96ff` |

外层归档 SHA256：`511afe2ebb3117b5c79e0c9adad34bc0a735b4f16c3386a08dd754ba131d61dd`。使用方法见 [COMPILER_USAGE.md](COMPILER_USAGE.md)。

本轮只证明声明的 MOD/组件范围；完整游戏数据、WBData和游戏内运行未新增验收。原LoD已知越界仍采用保留逻辑表末尾NUL的确定性修复合同，不复制越界内存。历史失败和旧发行来源保留在详细证据及git历史中，不改标为本轮成功。

## 发布与验收机制

`recovery/scripts/PublishCompiler.ps1` 为 P6 的发布入口。它要求已有成功的 Debug/Release 真实生产 CLI 验收收据；构建成功本身不能发布。已取得 Debug/Release Windows 运行及独立解压证据；当前接受版本与后续候选以 `REFACTOR_RESUME.md` 和对应 Actions 区分。

```powershell
./recovery/scripts/PublishCompiler.ps1 `
  -CompilerDirectory recovery/artifacts/compiler-tests/<build-id> `
  -AcceptanceReceipt recovery/evidence/refactor/cli/<run-id>/validation.json `
  -OutputDirectory recovery/artifacts/releases/<release-id>
```

需要 Windows、PowerShell 7、Python、现有 `TextureRedirect/requirements.txt` 和 `app-protocol-requirements.txt` 依赖，以及构建外部 CLI 控制器所需的 VS Roslyn/.NET Framework 4.8 targeting pack。发布脚本不重建生产 EXE、不重新修补 Texture，也不修改 SDK。调用者提供的 CLI 收据必须有两个成功配置、完整二进制源码提交和五文件实际运行哈希；若其使用历史二进制，源码来源保留为 `binarySourceCommit`，不冒充发布脚本当前提交的新构建。

每个 ZIP 根目录只有以下五个文件：

| 文件 | 用途 |
| --- | --- |
| `NewModCompiler.exe` | 包含已恢复业务代码的 Win32 编译器 |
| `NewModCompiler.exe.config` | .NET Framework 4.8 混合运行配置 |
| `antlr.runtime.dll` | 固定版本的必要解析运行库 |
| `BinaryAssetBuilder.TextureCompiler.dll` | 直接引用该 EXE 的兼容 Texture 实现 |
| `BinaryAssetBuilder.TextureCompiler.dll.patch.json` | 原版、补丁、EXE 身份及受保护 PE 审计清单 |

旧业务 DLL、测试宿主、补丁工具、控制器、对象文件和临时 SDK 均不入包。每配置的 `.manifest.json` 放在 ZIP 外，记录五文件长度/SHA256、ZIP 哈希、生产构建与 CLI 收据来源、工具链、补丁清单及运行依赖说明；`release.json` 是整轮终态。运行依赖复用现有 G2 的导入保护审计和运行证据（见 `recovery/probes/TextureRedirect/README.md`），不扩展新的依赖审计。目标机器仍须具备 Windows x86 兼容环境、.NET Framework 4.8、原混合 Texture 所需 x86 VC80 CRT、与构建工具链匹配的 x86 VC 运行库，以及既有 DirectX 9/D3DX 环境。这些系统组件不随包复制；hosted runner 上通过不能推断裸 Windows 已具备它们。

发布前从 ZIP 解压至独立短目录，以现有单进程控制器分别运行原 bin 和解压 bin 的真实 `doctor` 与小型 Armor MOD。逐字节比较四个链接输出，检查进程、JSONL 生命周期、报告、精确产物与运行文件不变，最后用现有协议 Schema 校验器验收两个配置。验证证据保存在 `evidence/`，不进入 ZIP。仅两配置全部通过后生成正式 ZIP 文件名和清单；失败保留错误收据与候选 ZIP，不把候选标为正式发行物。

这个门槛证明五文件包的基本部署与构建能力。完整 P5/G3/SDK、完整 UI/runtime、Unicode Texture、BIG/skudef 和 P7 均需各自证据，不能由本脚本的小型 MOD 推断完成。

`PrepareCompilerRelease.py` 固定复用已成功的三字节 APT 版本修正生产归档；重新核对完整生命周期的成功步骤、归档 SHA256、两配置收据、协议原始文件与五文件运行身份。它还直接解析 Debug/Release 的 APT、APT+maps 生产 BIG 和原 MakeBig BIG，共八份原始归档，要求所有 APT `.version` 精确为 `20 0d 0a`；两字节旧候选不能通过。独立 HashFix/LoD 准备文件不属于生产链接输入，允许保留；生产源码、SDK、构建脚本或依赖改变仍拒绝复用。准备成功仅表示可运行 ZIP 部署验收，正式包仍必须通过后续发布步骤。

完整 mymod 证据继续保留原二进制及测试来源；本次候选只对已声明的受影响组取得新验收，不能把历史完整 MOD 结果改标为当前二进制重测。后续 base 后处理候选需要自己的生产验收与重新固定来源；不得使用该 APT/maps 归档发布未验证的 base 能力。

解压后的典型调用（SDK 与 MOD 单独提供）：

```powershell
./NewModCompiler.exe doctor --sdk-root C:\NewSdk\SDK
./NewModCompiler.exe build --sdk-root C:\NewSdk\SDK --mod C:\Mods\Example --out C:\Build\Example --package-content base,apt,maps
```

新版 base BIG 发行使用 `PublishCompiler.ps1 -VerifyBigDeployment`。该开关在正式 ZIP 命名前，把两配置候选解压到独立的精确五文件目录，再调用 `TestCompilerBig.ps1 -UsePackagedRuntime`。测试直接复制包内五文件执行，不重新生成 Texture 补丁；运行 base、apt、maps 单选及组合，对照当前 loose 输入上的原 LoD 八输出和原 MakeBig 全成员字节，并覆盖 CDATA、取消、发布失败及重试。还必须通过显式 `--include-additional=true` 组，逐字节对照原 `CopyFolders` 的 SDK→MOD 覆盖和原 MakeBig，保留原始覆盖目录与完整清单；仅 base 组通过不足以验收新候选。全部通过且源目录、解压目录和执行目录五文件哈希不变后才发布；发布清单记录这份独立 ZIP BIG 验收收据。

`PrepareCompilerRelease.py` 中 `REQUIRE_BASE_BIG` 必须与新的完整成功生产 source/run/archive 一起启用；不能仅改变开关使旧 APT/maps 归档变成 base 证据。启用后额外核查 base 与 base+apt+maps 两配置真实 CLI 用例、原始产品/原 MakeBig BIG 的完整清单与字节、原 LoD 新鲜八输出及其原始库存，同时强制核查 `all-with-additional`、原 CopyFolders 源码/运行身份、覆盖后的原始文件，以及产品/原 MakeBig BIG 的完整字节。此前已验收的 ZIP 保留原范围，后续候选不得沿用其验收标签。

MakeBig 的恢复实现直接并入生产 EXE；原 SDK MakeBig 只在隔离验收中充当 oracle。AssetMerge 不在本次需求与发行必做项中。

包含可选中立修复的新候选还必须通过显式 `--fix-neutral-assets=true` 的真实 ZIP CLI、缺失参考拒绝及恢复重试。准备器核对原 HashFix→ModAssetResolver→LoD 工具与参考输入、实际非零哈希修正、零资产插入范围、原始输出及最终 MakeBig 全成员字节；不开启新候选的发行标签，除非该组同样成功。此验收仅涵盖已证明的零插入修复范围，不自动读取游戏目录，也不代表完整资产插入已支持。

当前含 MakeBig/Additional/中立修复候选使用 `.github/workflows/refactor-p7-replay.yml`：`PrepareCompilerP7Replay.py` 固定已通过生产构建、App/Core/CLI/Texture/全部缓存的原始运行，保留其 BIG 测试宿主编译失败终态。精确原始归档/运行文件哈希、源代码与构建闭包必须不变；测试脚本同时记录 Git blob 与 Windows 实际读取字节哈希。它直接运行 `PublishCompiler -VerifyBigDeployment`，候选 ZIP 必须从独立目录通过完整新 P7/协议门槛后才取得正式名称，不重复编译未变化生产代码。原整体失败不会被改写为通过，新接受范围来自独立重放结果；二进制与测试来源分别记录。旧 `PrepareCompilerRelease` 入口仍只固定旧 APT/maps 范围。

本轮新正式包已通过：[P7与独立ZIP验收](https://github.com/wu162/NewSdk/actions/runs/40425186743)。二进制来源 `5f81f664f4970176d104b1aaa725501beffac410`；测试/发行来源 `7e933d7b9ace36ebd3bad906d037666322cf194f`。两配置包含base/apt/maps完整成员、Additional原CopyFolders覆盖、可选中立哈希修复零插入链、原LoD/MakeBig全内容差分、CDATA、回滚/取消/重试及协议。原主运行的测试宿主失败保持failure，新的独立成功收据补齐P7/ZIP范围，不改写历史终态。
