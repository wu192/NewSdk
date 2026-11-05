# NewSdk Mod Tools 0.3

在 Windows 本地、可信的 VS Code 1.96+ 工作区中编译 MOD、预览和编辑普通 CPU/GPU 特效。远程窗口、WSL、容器及其他系统不在运行支持范围内。

## 使用

1. 安装 `NewSdk-Mod-Tools-FX-Round2.vsix`，打开 MOD 目录。
2. 在 NewSdk 侧栏选择已有 `NewModCompiler.exe`，或下载 HTTPS ZIP；认证保存在 VS Code SecretStorage。VSIX 不包含编译器。
3. 配置 SDK、独立输出目录和 MOD 版本，执行“检查编译环境”“查看编译计划”，再编译。输出目录必须与 MOD/SDK 分离。
4. 打开 MOD XML，运行“NewSdk: 预览当前特效”，或从特效树/搜索进入。可与原生 XML 并排；不接管所有 XML 的默认打开方式。

默认下载来源见 [compiler-release.json](compiler-release.json)，固定版本和 SHA-256；私有下载需要相应权限，也可选本地 EXE。`newmodcompiler.json` 只读，不创建 `.vscode`，不清理 SDK 编译缓存。编译成功要求本轮退出码、BuildFinished 与同 buildId 报告一致；停止按钮是强制中断。任务终端保留原始输出，产物只认本轮报告。

## 特效范围

- CPU：`PARTICLE / Physics.Default / Draw.Default / Update.Default`，独立 RGB/Alpha 八槽、逐帧递推、普通 EventFX 和附属系统排空/解绑。
- GPU：`GPU_PARTICLE / Default / Gpu`，`SIMPLE_QUAD`、`CENTERED_QUAD`、`TWO_CONCENTRIC_QUADS` 对应 `GPUParticle.fx`；`TWO_QUADS` 对应 `GpuParticlePerpendicularBottom.fx` 与 `Update/RenderObject`。
- 常用出生区域、初速、Atlas、六种材质，CPU/GPU 独立颜色路径、排序、合成平地深度的 soft particle；普通源/目标对象、矩阵与已支持条件分派。
- 类型化引用、Include/xi:include、继承及已核实的数值表达式；TGA、DDS（DXT1/3/5、掩码 RGBA、mip）按需解码。字段、单位、来源与具体限制见面板及 [能力清单](src/fx/capabilities.ts)。

参数调节先预览，提交为一次可撤销修改；原生 XML 与可视页共用文档。冲突不覆盖新内容。共享模板显示影响，SDK 只读，可复制到 MOD 再改引用。保存并编译先保存相关脏 XML；保存失败不会启动编译。损坏 XML 保留上一有效画面，缺资源、未实现节点和超预算均有定位诊断。

可显式导入 MOD、SDK 或已配置输出目录内的 DDS，选择源贴图或编译结果。导入只建立引用，不复制文件；编译结果初始标为“未核实”，输入变化后标为“过期”，不能据路径相同认定与当前 XML 一致。`newsdk.fxTextureBudgetMiB` 默认 32；4K 完整 mip DDS 的压缩与解码副本约 96 MiB，可显式设为 128。预算不足时暂停加载，不改 XML。

播放、单步、时间轴和可取消重放使用有界状态/快照；隐藏停算、关闭释放 Worker 和 GPU 资源。骨骼、任意 ObjectFilter、自定义 shader/几何、未核实表达式和特殊原生边界只给出具体诊断。预览使用局部初态与合成场景深度；未做游戏截图对照，不能据此声称实机像素一致。

## 开发与验收

```powershell
cd vscode-extension
npm ci
npm test
npm run test:render
npm run test:stress # NEWSDK_FX_STRESS=full 扩大长段重放和采样
npm run package
npm run test:host # Windows 真实 VS Code 宿主及实际编译器
```

固定样例在 `test/fixtures/fx`。浏览器验收覆盖固定帧、材质、软粒子、上下文恢复及编辑手势；压力验收覆盖长段重放、事件、预算、100 次切换和回收。软件 GPU 只用于回归，不代表参考显卡帧率；报告区分可控分配、测试进程 RSS 与完整 VS Code 内存。

Windows Actions 默认从本仓库固定提交 `a75840e` 构建实际 Win32 编译器，用小型 MOD 验证编译及编辑往返；协议假编译器仅验证任务异常流程。可用 Secrets `NEWSDK_COMPILER_URL`、`NEWSDK_COMPILER_SHA256`、`NEWSDK_COMPILER_AUTHORIZATION` 覆盖下载来源。手动 `full_acceptance` 增加长运行与编译器完整验收；以实际 JSON 摘要列出的用例为准。

Actions 不启用依赖缓存；成功只保存小型 JSON 摘要 1 天，失败仅保存有界日志/差异图 1 天。VSIX 只在手动或发布构建上传，保留 1 天；禁止上传 SDK、MOD、游戏素材和编译/解码缓存整包。此规则也记录在仓库 [AGENTS.md](../AGENTS.md)，后续任务沿用。
