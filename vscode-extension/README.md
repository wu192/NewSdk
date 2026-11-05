# NewSdk Mod Tools

在 **Windows 本地 VS Code 1.96 或更新版本**中配置和编译 NewSdk MOD。支持工作区信任检查；远程窗口、WSL、容器和其他操作系统不属于 P0 范围。

从仓库 `main` 开发；编译器命令和协议参考 `recovery/legacy-sdk-cache` 的 `680c4f0c4439bd7435addebfc57a1a16c3c320e3`。VSIX 不包含编译器，也不从源码生成编译器。

默认下载包已配置为用户提供的 [`compiler-2026.11.01` Win32 Release](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01)。[compiler-release.json](compiler-release.json) 固定下载地址、SHA-256 和二进制来源提交 `a75840e`；该提交的 CLI 和事件/报告协议与上述参考一致。下载对话框预填地址和校验值，仍可选择其他 ZIP 或已有 EXE，不自动后台升级。

## 使用

1. 安装 `NewSdk-Mod-Tools-P0.vsix`，在 VS Code 中打开 MOD 目录。
2. 点击左侧 NewSdk 图标，进入配置。选择已有 `NewModCompiler.exe`，或输入已打包编译器的 HTTPS ZIP 下载链接。需要认证时，认证信息保存在 VS Code SecretStorage；链接不预设。
3. 选择 SDK 和输出目录，填写 MOD 版本。输出目录必须与 MOD 和 SDK 完全分离。全局路径保存在用户设置，项目输出和版本保存在扩展存储。
4. 执行“检查编译环境”和“查看编译计划”，然后点击“编译 MOD”。原始输出显示在任务终端；工作台显示阶段、数据流数量、问题及本轮报告确认的产物。

`newmodcompiler.json` 只读。插件不创建 `.vscode`，不修改 MOD 源文件，不搬运或清理 SDK 的 `SuperCompilerCache`。编译使用单个编译器进程，参数使用数组传递；默认 `auto` 内容发现、BIG/SKUDEF 和 Additional，保留编译器自己的 jobs 和缓存设置。

同一物理 MOD 在多个 VS Code 窗口中也只能运行一个任务。项目锁保存在当前用户的系统临时目录；只有确认宿主和编译器进程均已退出时才回收旧锁。计划记录使用 `plan.events.jsonl`、`plan-report.json`、`plan.log`，不会覆盖最近一次构建的记录。

停止按钮使用 VS Code 任务终止 API，显示为**强制中断**。只有编译器的完整事件与报告明确返回 `cancelled/6` 才显示正常取消。成功必须同时有进程退出码 0、本轮 `BuildFinished`、同一 `buildId` 的成功报告。缺少报告、崩溃和旧报告都不会显示为成功。

没有真实位置的诊断保留在扩展问题列表，不伪造 Problems 面板行号。进度不显示资产百分比或 ETA。产物来自本轮报告，不扫描目录认领旧文件。

## 开发与验证

```powershell
cd vscode-extension
npm ci
npm test
npm run package
npm run test:host # Windows；启动真实 VS Code 扩展宿主
```

Windows 工作流 `.github/workflows/vscode-p0.yml` 执行协议、配置、ZIP 边界和真实扩展宿主测试，打包 VSIX，并上传测试结果。宿主测试使用单独编译的协议假编译器检查计划、错误、缺失报告、停止、重新编译、只读源目录和跨进程配置持久化；假编译器不是生产编译器。

VS Code 在 `--extensionTestsPath` 模式下主动使用内存存储。因此宿主测试通过临时驱动扩展，在使用同一独立磁盘配置的两个普通 VS Code 窗口中运行；驱动不进入 VSIX，也不写入 MOD。

CI 默认使用上述公开发布包并校验 SHA-256；需要覆盖下载来源或认证时使用仓库 Secrets：

| Secret | 含义 |
| --- | --- |
| `NEWSDK_COMPILER_URL` | 可选：覆盖默认编译器 ZIP 的 HTTPS 链接 |
| `NEWSDK_COMPILER_SHA256` | 可选 SHA-256；未提供时记录下载文件的实际哈希 |
| `NEWSDK_COMPILER_AUTHORIZATION` | 可选 HTTP `Authorization` 值；仅用于下载，不写进日志或源码 |

常规提交执行小型 Armor 数据流、BIG/SKUDEF、源文件只读和缓存保留的真实编译冒烟测试。手动运行 `full_acceptance` 增加真实 SDK 的文件型 APT 业务资产、MapMetaData、完整内容组合、Additional 覆盖与排除、仅 Additional 无输入、无效 XML、插件强制停止后重试，以及独立进程组中的正常 Ctrl+Break 取消后重试。测试检查实际 BIG 成员和链接清单，区分两种停止结果。SDK 与 MOD 使用临时副本，不重建生产编译器，也不重复完整 Upheaval 基准。

完整验收以对应 Windows Actions 的实际结果和 `real-compiler.json` 为准；仅通过冒烟测试不代表 P0 全部完成。下载来源、版本、SHA-256、真实事件、报告、日志及取消控制器证据随 Actions 产物保存；VSIX 提供单独下载。此验收不包含游戏内运行或大型 MOD 性能结论。

## 特效工作区（0.2）

Windows 本地可信工作区中，打开 MOD XML，运行“NewSdk: 预览当前特效”；也可从“特效”树和搜索进入。SDK、MOD 与编译器沿用现有配置。可视页与原生 XML 共用保存及撤销，默认不接管 XML 打开方式。

支持普通 `GPU_PARTICLE / Default / Gpu / SIMPLE_QUAD / GPUParticle.fx`、五种出生区域、四种初速、RGBA 四槽、Atlas 四模式和常用 FXList 粒子组合。TGA、DDS（DXT1/3/5、掩码 RGBA、mip）按需解码；播放、单步、时间轴、图层、中文参数、共享定义与独立复制均在同一工作区。新增模板需选择真实 Texture 声明。

调节先预览，提交一次修改；冲突时保留最新 XML。保存并编译按编译入口收集脏文档，范围不确定时列出当前 MOD 的待保存 XML。未保存修改和编译期间的落盘变化分别标示。损坏 XML 保留上一有效预览；缺素材、未实现分支与预算超限可在“能力 / 问题”中定位。

能力清单为 `src/fx/capabilities.ts`；固定样例在 `test/fixtures/fx`。CPU、附属事件、soft particle 场景深度、复杂对象/骨骼、任意 shader/几何留在第二轮或既定范围外。预览采用固定局部初态；同系矩阵推导和未作游戏截图对照的差异会显式提示，不能据此声称实机像素一致。超出已实现 Include/继承合同的节点保留原文并报告。

验证：`npm test`；`npm run test:render`；Windows 上 `npm run test:host`。浏览器测试使用固定 Chromium 软件 GPU，只用于画面与生命周期回归，不代表用户 GPU 帧率。`npm run package` 输出 `NewSdk-Mod-Tools-FX-Round1.vsix`。Actions 证据保留 3 天，手动构建的 VSIX 保留 1 天。
