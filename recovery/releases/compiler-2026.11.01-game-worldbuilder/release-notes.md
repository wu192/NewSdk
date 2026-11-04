# NewModCompiler：恢复从游戏目录自动准备地编参考

发布标签：`compiler-2026.11.01-game-worldbuilder`，保留已有 Release。

本版恢复老 SDK 的参考来源流程：需要中立地编参考时，根据 Red Alert 3 注册表安装路径读取游戏目录下的 `Data\WBData_12.big`；也可通过 `--game-root`、MOD 项目 JSON 或 EXE `appSettings` 指定游戏根目录。兼容旧的解包参考输入。

参考准备只读源 BIG，必要缓存位于当前 SDK 的 MOD 专属 `SuperCompilerCache` 分区；根据源路径、长度和修改时间复用或更新。BIG 条目支持原始数据和 RefPack 数据。缺少 `SageXml/Worldbuilder.manifest` 时，可使用同一 BIG 提取的官方 manifest 作为 HashFix 参考。

`fix-neutral-assets` 仍默认关闭，支持 JSON/EXE 配置和命令行覆盖。包含 base 的 BIG 启用后执行 HashFix → ModAssetResolver → LoD；BIG 的 `--mod-version` 每次仍须在命令行显式指定。

## 附件

- `NewModCompiler-Win32-Release.zip`：日常使用，三个运行文件。
- `NewModCompiler-Win32-Debug.zip`：排查问题，三个运行文件。
- 发布元数据、校验清单与 `USAGE.zh-CN.md`。

需要 Windows、.NET Framework 4.8、匹配构建工具链的 x86 Visual C++ 运行库，以及外部 SDK/MOD 数据。本版不发布 500 MB 参考包。`WBData_12.big` 必须已存在于外部游戏 Data 目录，或已具备完整的旧解包参考文件；该 BIG 来自 SDK/WorldBuilder 地编安装，普通游戏安装不保证附带。

## 验证结果

生产源码：`3cf65b6de24358647447ed242621e8460243f847`。
[Windows 41322569300](https://github.com/wu162/NewSdk/actions/runs/41322569300) 整体通过：Debug/Release新构建，各1894项App检查、9个实际小MOD用例，覆盖CLI/JSON/EXE路径优先级、32/64位注册表发现并恢复、首次提取和热缓存复用、缺游戏BIG失败及恢复重试。游戏/SDK/MOD输入保持不变。

使用官方完整四流构成模拟游戏Data/WBData_12.big；小MOD实际修正1个哈希、补入5个资产，全部BIG成员与原HashFix→Resolver→LoD→MakeBig原始字节一致。两份三文件ZIP均独立解压后从游戏BIG冷提取并完成编译，BIG/SKUDEF与验收产品一致。

Release EXE为5,022,720字节，比上一中立版增加10,752字节；没有把地编资产内嵌进EXE。日常下载Release ZIP约1.61MB。

文件身份见compiler-manifest.json、release-metadata.json及SHA256SUMS.txt。本轮是快速定向验证，未重新执行未变的完整Core/CLI大矩阵、完整大型MOD或游戏内验证。
