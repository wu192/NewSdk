# NewSdk Mod Tools 0.4.0

在 Windows 本地、可信的 VS Code 1.96+ 工作区中进行 XML/XSD 智能编辑、编译 MOD，以及预览和编辑 CPU/GPU、Trail、Lightning、Swarm 特效。远程窗口、WSL、容器及其他系统不在运行支持范围内。

## 使用

1. 安装 `NewSdk-Mod-Tools-FX.vsix`，打开 MOD 目录。
2. 在 NewSdk 侧栏选择已有 `NewModCompiler.exe`，或下载 HTTPS ZIP；认证保存在 VS Code SecretStorage。VSIX 不包含编译器。
3. 配置 SDK 和独立输出目录；BIG/SKUDEF 构建还需填写 MOD 版本。执行“检查编译环境”“查看编译计划”，再编译。输出目录必须与 MOD/SDK 分离。
4. 打开 MOD XML，使用补全、悬停、转到定义、查找引用和 F2 重命名预览。资产上的 CodeLens 可查看引用和可能受影响资产。
5. 运行“NewSdk: 预览当前特效”，或从特效树/搜索进入 FX 页。可与原生 XML 并排；不接管所有 XML 的默认打开方式。

默认下载来源见 [compiler-release.json](compiler-release.json)，其中固定实际编译器版本、源码提交、下载地址和 SHA-256；也可选择本地 EXE 或其他 HTTPS ZIP。私有下载认证只保存在 SecretStorage，VSIX 不包含编译器。

## XML 智能编辑与共享索引

普通 XML 编辑器与 FX 共用同一 MOD 的文档记录、Include 作用域和资产引用服务。未保存文档覆盖磁盘内容；单文件变化使相关记录和依赖失效，复用未变文件的解析结果。切换项目或 SDK 时释放旧语义会话和文件监听。FX 渲染、贴图解码和 GPU 资源仍按各自生命周期加载、释放。

- 从用户 SDK 的 XSD 读取元素、属性、枚举、必填项、默认值和引用类型；支持 `DATA:`、`ART:`、`AUDIO:` 路径补全及 Include/`xi:include` 跳转。
- 资产身份包含类型、命名空间、实际定义位置和作用域。引用与继承查询保留 Include `all`、`instance`、`reference` 的可见性；SDK `builtmods` manifest 提供只读原版资产信息，MOD 覆盖不直接当作同文件重复 ID。
- 悬停显示已解析的继承与字段来源；引用和影响查询使用同一套语义边。影响列表表示当前已知源码关系，实际是否重编译由编译器及其缓存决定。FX 专属继承、合并和预览语义仍由 FX 层处理。
- F2 只为可唯一确认的 MOD 定义及引用生成跨文件修改预览；排除 SDK/manifest、动态表达式及歧义引用。提交使用公共事务检查文档版本与原文，保留 VS Code 撤销；缺失字段的 Code Action 也通过同一事务修改。
- 对已确认的 SDK XML 定义，可通过 Code Action 或“NewSdk XML: 复制 SDK 资产到 MOD”选择目标和新 ID，预览后一次复制并修改当前 MOD 引用。SDK 依赖保留为只读 Include；只有 manifest、动态表达式或不能安全移动的相对路径不会自动复制。

索引先提供当前文件和入口 Include 数据，再补全项目 XML。预算受限、SDK 模型或 manifest 不完整时，未解析引用显示“待确认”，CodeLens 数量标为“已知”；跨文件重命名等待完整索引。磁盘 W3X 只扫描顶层声明，不把顶点、贴图或 FX GPU 数据装进通用索引。实时检查覆盖 XML 语法、必填项、枚举、Include、已知类型引用、重复 ID 和 `$DEFINE`；完整资源和 schema 验证以真实编译器结果为准。

持久文档记录存放在扩展的 `globalStorageUri/xml-index`，按 MOD 根、SDK 根和 XSD 版本隔离；不向 MOD 源码目录新建缓存。编译器缓存继续使用 SDK 的 `SuperCompilerCache/<ModKey>/`，与编辑器索引独立。插件不创建 `.vscode`，不改写 `newmodcompiler.json`，也不清理编译器缓存。

## 编译配置与诊断

“NewSdk: 设置项目编译选项”可覆盖打包内容、输出格式和 Additional，并可恢复跟随预设。优先级为本次显式设置、已保存项目覆盖、`newmodcompiler.json`、编译器默认值；高级预设继续由编译器读取，插件不再固定传入 `auto`、`big` 或启用 Additional。BIG 的 MOD 版本仍显式传入 CLI；明确使用 loose 输出时可不填版本。

工作台按编译器 `plan` 展示 Base、APT、Global 的实际选择与跳过原因，并显示 `effectiveOptions` 的值和来源。缓存与性能只展示已有报告字段，打开工作台不会启用详细性能采样。首次 build 或编译器、SDK 配置及旁置 DLL 变化时先运行 doctor；同一窗口的后续 build 复用未变化工具链的成功检查，手动“检查编译环境”始终重新执行。检查结果只保存在内存，依据真实路径和文件元数据失效，不扫描或哈希整套 XSD；构建仍由编译器验证实际输入并给出诊断。

静态检查、正式编译和 FX 预览分别标为 `NewSdk XML`、`NewSdk Compiler`、`NewSdk FX`。新版编译器沿用 1.0 协议，在事件与报告中附加可靠的文件、1-based 行列和已知资产身份；插件兼容旧字段及结构化 `source`。旧版编译器缺少可选定位时仍可编译，错误保留在工作台列表；只有真实源码位置才进入 Problems，不从日志文本推测坐标。

“保存并编译”先保存相关脏 XML，保存失败不启动编译；“编译已保存内容”可保留编辑中的文本。输入在编译期间变化时，结果标记为对应上一保存版本。成功仍要求进程退出码、`BuildFinished` 和同一 `buildId` 的最终报告一致；停止按钮表示强制中断。原始输出留在任务终端和日志，产物只认本轮报告。

## 特效范围

- CPU：`PARTICLE / Physics.Default / Draw.Default / Update.Default`，独立 RGB/Alpha 八槽、逐帧递推、普通 EventFX 和附属系统排空/解绑。
- GPU：`GPU_PARTICLE / Default / Gpu`，`SIMPLE_QUAD`、`CENTERED_QUAD`、`TWO_CONCENTRIC_QUADS` 对应 `GPUParticle.fx`；`TWO_QUADS` 对应 `GpuParticlePerpendicularBottom.fx` 与 `Update/RenderObject`。
- 特殊粒子：`TRAIL / Draw.Trail` 带状拖尾、`STREAK / Draw.Lightning / Volume.Lightning` 双组随机波形闪电，以及 `SWARM / Draw.Swarm` 速度拉伸/透明度和可选环境贴图；`Physics.Swarm` 独立支持，也可与 `Draw.Trail` 组合。新建菜单提供“拖尾”“闪电”“群粒子”，参数修改与原生 XML 共用撤销和保存。
- 常用出生区域、初速、Atlas、六种材质，CPU/GPU 独立颜色路径、排序、合成平地深度的 soft particle；普通源/目标对象、矩阵与已支持条件分派。
- 类型化引用、Include/xi:include、继承及已核实的数值表达式；TGA、DDS（DXT1/3/5、掩码 RGBA、mip）按需解码。字段、单位、来源与具体限制见面板及 [能力清单](src/fx/capabilities.ts)。

参数调节先预览，提交为一次可撤销修改；原生 XML 与可视页共用文档。冲突不覆盖新内容。共享模板显示影响，SDK 只读，可复制到 MOD 再改引用。保存并编译先保存相关脏 XML；保存失败不会启动编译。损坏 XML 保留上一有效画面，缺资源、未实现节点和超预算均有定位诊断。

可显式导入 MOD、SDK 或已配置输出目录内的 DDS，选择源贴图或编译结果。导入只建立引用，不复制文件；编译结果初始标为“未核实”，输入变化后标为“过期”，不能据路径相同认定与当前 XML 一致。`newsdk.fxTextureBudgetMiB` 默认 32；4K 完整 mip DDS 的压缩与解码副本约 96 MiB，可显式设为 128。预算不足时暂停加载，不改 XML。

播放、单步、时间轴和可取消重放使用有界状态/快照；隐藏停算、关闭释放 Worker 和 GPU 资源。骨骼、任意 ObjectFilter、自定义 shader/几何、未核实表达式和特殊原生边界只给出具体诊断。合成上下文支持静止源或显式发射源轨迹、水平地面和已列质量路径；任意地面法线与阵营色未实现。Trail、Lightning、Swarm 使用重建的预览模型：SDK 仅有对应编译 shader，没有原 shader 源码；吸引运动、波形、环境反射和透明度不代表已核实的游戏算法。TrailLife 预览范围为 1～600 帧，超出预算明确暂停。依赖骨骼绑定、Swirly、未支持的 Update 旋转、地形发射条件或非正粒子寿命的 SDK 样例仍会阻止预览。普通 CPU/GPU 继续使用原有路径。未做游戏截图对照，不能据此声称实机像素一致。

轨迹在“预览场景”中设置：可用直线往返、闭合环形或自定义时间点；坐标相对源位置，按 30 Hz 采样，支持单次、循环和往返。轨迹只驱动新生粒子的发射位置，已出生粒子仍在世界坐标中运动，不自动继承源速度；源朝向沿用场景设置。它是显式合成输入，不代表普通 FXList 在游戏中自动绑定移动对象。配置仅保存到 VS Code 会话。

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

Windows Actions 使用 [compiler-release.json](compiler-release.json) 固定的实际 Win32 编译器，用小型 MOD 验证 XML 编辑往返、计划、打包及真实错误；协议假编译器验证任务异常流程。可用 Secrets `NEWSDK_COMPILER_URL`、`NEWSDK_COMPILER_SHA256`、`NEWSDK_COMPILER_AUTHORIZATION` 覆盖下载来源。手动 `full_acceptance` 增加长运行及编译器内容、错误和取消组合；可选源码定位断言仅对已核验的新版本开启，旧版兼容路径仍保留。实际通过范围以对应运行的 job summary 为准，新增用例不代表 Windows 完整验收已经通过。

Actions 不启用依赖缓存；常规成功结果写入 job summary，不上传摘要 artifact。失败仅上传有界日志及必要差异图，保留 1 天；VSIX 仅在显式手动或发布构建上传，保留 1 天。禁止上传 SDK、MOD、游戏素材及编译/解码缓存整包。遇到既有存储额度不足时不自动删除历史 artifact 或缓存。存储约束见仓库 [AGENTS.md](../AGENTS.md)，后续工作流沿用。
