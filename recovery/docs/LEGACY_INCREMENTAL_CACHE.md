> 历史记录：本页的句柄固定、SHA 收据、侧车及发布事务不代表当前实现。当前路径与缓存合同见 [SUPERCOMPILER_CACHE](SUPERCOMPILER_CACHE.md)，直接产物阶段历史见 [DIRECT_OUTPUT](DIRECT_OUTPUT.md)。

# 原 SDK 缓存：行为与实现

当前实现见[直接产物与原 SDK 缓存](DIRECT_OUTPUT.md)：已删除 MOD 发布事务、整代 Staging、独立四流快照及输出哈希/收据。原 BAB SessionCache/BuildAssetCache 与依赖失效仍保留。下面为既往版本的验证历史；涉及回滚、发布去重或旧暂存目录的描述不适用于当前实现。


本页记录缓存恢复与热态优化的行为、代码分工和验收范围；当前分支、准确源码及结果见 [续接入口](../RESUME.md)。旧阶段交接与验收在后文保留。



## BIG/SKUDEF 包缓存移除（第 9 项，已验收）

用户最新明确要求移除整个功能，本提交删除 `BigPackage` 的包指纹计算、`package|v1` 收据读写和 published BIG/SKUDEF 复用路径。每次正常打包，包括 LoD、Additional 合并、MakeBig 与 SKUDEF 生成；不再为了包指纹额外计算 Additional 文件哈希。原缓存目录中的旧包收据不会被读取，并随正常文件身份缓存维护清退。

Core 会话/资产缓存、链接流复用和通用未变发布跳过继续工作；打包完成后仍按实际新产物确认是否需要替换 published，失败/取消保持原回滚合同。原版 MakeBig/LoD、Additional、锁定未变输出、真实替换故障及重试用例均保留；增加包阶段两产物必须来自本轮 staging、不得持久化包收据的断言。

生产二进制与测试来源均为 `7fca1e4f38c304bfc107e5301b1b72b70cbfacea`；完整 Windows [40872280934](https://github.com/wu162/NewSdk/actions/runs/40872280934) success。Debug/Release 应用测试均通过重复构建、忽略损坏旧包收据、正常清退旧收据和未变发布。生产 BIG 每配置 23 个用例、13 组原版对比通过；归档原事件独立核对每配置 14 个成功 BIG 用例，两份包均来自对应 buildId 的 staging，包括锁定未变公开包时重新打包且跳过发布替换。完整 mymod 原版差分为零差异，独立五文件发行包也再次通过。[验收与归档核对摘要](../evidence/refactor/big-cache-removal/40872280934-1/summary.json) 保存准确来源及有界原收据；下文旧包复用结果仅属历史。

## 第二轮热路径与 StableSort 优化（第 9 项之前，历史验收）

本轮继续在 `recovery/legacy-sdk-cache` 修改，生产与测试来源 `ae4654a892446724bf870348e56f2e8072fafa58`；完整 Windows [40868109538](https://github.com/wu162/NewSdk/actions/runs/40868109538) success。原归档已下载并复算摘要，与 GitHub 和上传日志一致；[收据摘要](../evidence/refactor/hot-cache-second/40868109538-1/summary.json) 保留准确来源、排序样本、会话热态、SDK/发行收据及早期失败。

- Schema 在每个构建请求首次加载时按父目录采集文件身份；请求内全部流直接复用同一已编译闭包。新进程初次解析也复用 `.file-identities.json` 的已有收据，身份未变不重新计算 SHA256；身份变化时才核对内容。首次计算直接使用 XML 解析已读取的字节，不再为哈希另开文件。独立 Core 没有 Windows 身份提供器时保留内容校验。
- 显式输入根的 CLI 预检结果与解析后的 SDK 配置由同一请求传给正式后端。后端按有效根、输出/诊断路径及 cwd 核对签名；边界变化重新检查。每个有效输出路径统一物理解析一次，相同根也合并解析。
- 缓存实例保留已确认的 HasCustomData；输出可用时不准备 BuildCache 路径或重复强制查询输出。资产提交复用调用者已取得的位置，显式 `/atc` 仍执行 touch。
- 链接四流查询与尺寸读取共用目录元数据。当时 BIG 公共包复用合并了存在性/身份查询；该包复用功能已由用户第 9 项移除。
- 发布收据 v2 用目录拓扑 stamp 与批量文件身份核对精确输出；目录 stamp 不能代替文件 ChangeTime。收据失效时，reparse 检查、文件集合枚举及尺寸读取合为一次树遍历。仅真正替换时创建准备/备份路径；失败、取消和必需输出故障继续使用原事务回滚。
- 插件在 Initialize/ReInitialize 读取 trace 配置，保留原始成功差分捕获；删除 XML 插件重复 TypeId 自检和失败输入转储包装，异常直接保留。生成器同步到简化后的分派结构，元数据与编译器路由不变。

- StableSort 为一次排序建立依赖成员索引及前缀反向依赖索引；当前键严格大于所有已放置键、同 TypeId 实例名也严格靠后且前缀没有反向依赖时直接追加。其余情况保留原插入规则，包含文化比较相等、类型符号哈希碰撞和基础流前缀。常见追加路径按资产/依赖数量线性推进；初始 schema 排序不变，回退路径不宣称统一降低最坏复杂度。基础 manifest 用 TypeId/InstanceId 索引查找并一次压缩剩余列表，保持原先顺序。
- Core 只在文档和实例校验成功后提供当前 XML 的只读校验收据；加载新 XML 或校验失败时清除，收据不写入旧会话格式。已核实的 LogicCommandSet 生产路径利用收据跳过重复属性/子树校验；直接 CompileXml/未校验 ProcessInstance 保留检查。编译器恢复范围、原字节布局及外部载荷检查继续承担各自合同，其它组件检查没有笼统删除。

文件身份优先用于缓存有效性，SHA256 不再作为热态文件检查的前置步骤；协议中的 SHA256 内容收据保留兼容。原始差分、冻结/部分会话、缓存缺损修复和真正替换失败的门槛不变。

Debug/Release 热态原收据均为 `xmlParse=0`、`instanceCompile=0`、`fileHashReads=0`、`fileHashBytesRead=0`、`fileHashReuses=837`、`linkedStreamsRestored=2`、`publicationUnchanged=true`。StableSort 每配置通过 66 组冻结原算法顺序对照；1024 个不同类型身份的独立资产样本中，schema 比较器调用从 523,776 次变为 1,023 次。单次辅助函数样本 Debug 69.2511→0.2361 ms、Release 58.6808→0.2396 ms；排除初始 schema 排序、序列化、Native 编译和发布，不作整体 SDK 加速推断。完整未精简 mymod 原版差分及独立五文件 ZIP 均已通过。

## 第一轮热态优化（历史，2026-10-29）

直接开发于 `recovery/legacy-sdk-cache`，生产与测试源码 `84261243`；完整构建/生产差分 [40845943601](https://github.com/wu162/NewSdk/actions/runs/40845943601) success。独立应用、完整生产矩阵、mymod 原始字节差分和独立五文件 ZIP 均通过；[原始收据摘要](../evidence/refactor/hot-cache/40845943601-1/summary.json) 保存归档哈希及来源。两配置热态 `xmlParse=0`、`instanceCompile=0`、`fileHashReads=0`、`fileHashBytesRead=0`、`publicationUnchanged=true`，会话/CDATAs 写时间保持不变。新旧 SDK 配对 [40848391545](https://github.com/wu162/NewSdk/actions/runs/40848391545) 也已 success，具体计量见 [SDK 对照](SDK_BENCHMARK.md)。

- 生产插件按实际请求的类型创建；缓存版本从原 Win32 元数据取得，不为汇总版本加载 Texture/W3X/Audio 等无关实现。doctor 和显式全路由目录检查继续完整初始化；跨流只重新初始化已经加载的实现。
- 会话 Schema 身份来自 `SchemaSet` 已读取的实际 XSD include 闭包及其内容哈希，不再递归枚举整个 `Schemas`。同一构建请求内复用已编译 Schema；未参与闭包的孤立文件不再导致冷失效。
- Core 原 manifest/type/stream checksum 确认四流未变化后，BuildSession 直接接受缓存文件的内部引用。未变化的一代不创建 staging 四流、候选目录、硬链接或快照，也不做链接目录交换；缺失组仍重建，移除的 include 通过当代完整成员表清理。直接调用后端的现有物理输出合同保留。
- 会话直接只读载入已确认的 XML/Deflate，避免复制到 staging 再读取；Core 跟踪实际文档、文件、编译器版本和最终 stream hints 变化。同格式且未变化时不序列化、不建立 LegacyCacheStore 候选、不比较或安装会话。CDATA 只按实际使用的资产定位和引用，避免整树复制。压缩切换、原格式导入、部分/冻结及失败取消合同保留。
- 发布以产物列表及其内容身份定位上一代收据，再检查已知文件/目录的 FileId、ChangeTime、mtime 和长度。正常热态不递归枚举 `published`、不重复读文件内容或大小、不创建发布候选和目录交换；真实内容/成员变化仍走边界检查与回滚事务。
- Core 输入 SHA256 接入同一文件身份缓存，避免下游再次全读未变化输入。删除生产内部产物的重复祖先检查、属性遍历和发布前后重复整树检查；外部输入/输出重叠、重解析点边界、路径合同、锁和真实失败回滚保留。

本轮新增少量关键检查：虚拟四流复用不落盘也不替换旧文件，发布收据识别嵌套新增文件和等长/恢复 mtime 编辑，真实热态会话及 CDATA 不重写。完整原版字节差分和正式 ZIP 门槛继续保留。历史验收见后文，不将其作为本轮新代码的结果。

## 上一轮链接与发布复用（历史，2026-10-29）

实现分支 `codex/legacy-cache-completion`，[PR #20](https://github.com/wu162/NewSdk/pull/20)；该阶段完整生产源码 `4ad5d0d9`，统一验收 [40836177604](https://github.com/wu162/NewSdk/actions/runs/40836177604) 已 success，完整 mymod 原版字节差分及独立五文件 ZIP 同时通过。[原收据核对摘要](../evidence/refactor/performance-implementation/40836177604-1/summary.json) 记录下载归档 SHA256、来源、两配置零热态 SHA 读取与实际 BIG 复用；旧验收不替代本批结果。

- `LinkedOutputCache` 在输出根 `.linked-streams` 保留四流，按实际创建的 `OutputManager` 懒恢复完整四文件，沿用原 manifest/type/stream checksum 决定重写。删除的 include 不会混入输出；该能力独立于 `/sc`、`/bc`，BIG 与 loose 共用。
- 私有四流快照优先硬链接，不支持时复制；原链接器通过 unlink、临时文件和 rename 更新四流。公开文件及恢复的 CDATA 使用独立存储。会话、安装与回滚备份改为移动，CDATA 相同则不重新安装。
- `FileIdentity` 用 Windows 批量目录记录的卷标识、FileId、ChangeTime、mtime 和大小替代每进程 826 文件的内容 SHA256。等长且恢复 mtime 的写入仍由 ChangeTime 识别，替换由 FileId 识别。
- `FileHashCache` 将已确认收据保存在 `.file-identities.json`，仅保留本轮使用项，内容不变时不重写。公开文件、运行库与 Additional 的键包含 ChangeTime；编译器私有且只以临时文件替换的四流按 inode/mtime/大小复用，硬链接计数变化不触发重读。CDATA 使用完整身份键。报告 `cache.fileHashReads/fileHashReuses/fileHashBytesRead` 给出实际读取量。
- 发布核对精确文件集合和已确认身份，未变时不创建 `.publish-*`、不复制、不交换目录。报告保持 `publicationStatus=PublishedDirectory`，新增 `publicationUnchanged=true`；公开文件被修改、缺失或多出文件时仍执行完整事务。
- BIG 的输入指纹包含当前四流/CDATAs、Additional 实际覆盖后的内容、任务布局、版本/SKUDEF 选项及宿主模块身份。相同时直接引用既有 BIG/SKUDEF，跳过 LoD、Additional 快照和 MakeBig；公开包仍通过包含 ChangeTime 的身份确认，实际覆盖 warning 与空目录碰撞保持。显式 neutral repair 继续使用实际参考输入链，不复用该包指纹。
- 可信内部收据贯通 BuildSession、打包和发布，不重复哈希；Texture 的运行时 EXE/DLL 部署校验现已删除，构建时 PE 审核保留。删去内置路由启动时逐项自审，插件版本按每个实际插件计算一次并按原路由数累加。
- 全部生产源码已无 `Flush(true)`。JSONL/文本以 64 KiB 字符缓冲，控制边界、warning/error 即时刷新，正常事件按 250 ms 批量刷新；普通 trace 不再逐条同步写 stderr，仍完整保留文件。

必需日志/报告失败、取消和真正的替换故障继续撤销未确认产物与链接缓存，`/psc` 语义保留。首轮 `40832233489` 的三个 `publication-fault` 失败保留：旧 fixture 以“锁住未变文件”要求失败，现已新增该场景的成功断言，并用额外旧文件强制真实替换后保留原失败及重试断言。

验证集中于已有完整生产矩阵，另增加少量关键组件/行为断言。生产会话用例检查原链接器四项 up-to-date、原始字节、发布时间及无 CDATA 热态 `fileHashReads=0`，Debug/Release 原收据均确认 2 个复用流、18 次哈希收据命中、0 次完整文件 SHA256 读取、0 字节 SHA256 输入，且 `publicationUnchanged=true`。BIG 热态包阶段事件直接引用 published 中的两文件。配对性能运行 `40837902446` 已通过，准确数据与小样本边界见 [SDK 对照](SDK_BENCHMARK.md)。

## 选项与目录

| 选项 | 默认值 | 行为 |
| --- | --- | --- |
| `/sc`、`/bc` | true | 会话复用与原类型/实例哈希资产缓存，独立开关 |
| `/csc` | true | 原 Deflate 会话格式；false 使用 XML。旧 defaultscript 显式传 `/csc:false` |
| `/psc` | true | 中止后保留已完成的可复用会话工作 |
| `/fsc` | false | 读取但不更新会话，不冻结资产或中间文件 |
| `/atc` | false | 复用中间结果时也进入原资产缓存文件 touch 路径 |
| `/scd`、`/scp` | 输出根 | 原会话目录及别名 |
| `/bcd`、`/bcp` | 输出根 `cache` | 原资产目录及别名 |
| `/iod` | 输出根 | 中间目录 |

SDK 的 `buildCache`、`buildCacheRoot` 继续生效，显式命令行/项目设置优先。相关参数可保存在项目预设；报告中的 `sdkConfig` 来源已加入协议 Schema。

## 生产路径

- `CoreStreamCompiler` 接回原 Core 的文档、文件依赖、定义、继承和逐资产复用。不再以整个 SDK/MOD 内容清单作为缓存命名空间；外部资源根保留原解析语义。
- Texture、OnDemandTexture、TerrainTextureAtlas 使用原描述符的会话及资产缓存。测试从固定原版 DLL 读取类型身份和 `HasCustomData`，以原版四流及完整 CDATA 字节对照验收。
- `LegacyCacheStore` 读写 `BinaryAssetBuilder.SessionCache.xml[.deflate]`，保留原资产哈希布局，支持无新侧车的旧目录导入。新宿主的 CDATA 状态位于会话根 `.NewModCompiler/cdata`；恢复时只带入当前流，避免无关流数据混入输出。
- Schema、宿主和解析设置的身份侧车用于会话保守冷失效。`CacheInputSnapshot`、`SessionPackage`、`BuildAssetCache` 旧包装组件仍有独立测试，但不再承担当前生产缓存路径。

## 失败与确认

最终 MOD 始终在独立 staging 中生成并发布；部分缓存保存不改变失败/取消终态，也不发布部分 MOD。

`/psc:true` 保存已完成的可复用会话工作；`/psc:false` 的会话安装跟随发布和必需输出结果提交，可回滚。`/bc` 保留原生共享资产的独立写入语义，即使会话不保存，中止前完成的资产仍可供重试使用。

## 验证入口

- `tests/App/LegacyCacheStoreTests.cs`：原目录导入、格式选择、CDATA、身份失效、部分确认与替换回滚。
- `scripts/TestCompilerBuildCache.ps1`、`TestCompilerBuildCachePayloads.ps1`：真实资产命中/失效、共享、失败重试、Audio/W3X 原始输出。
- `scripts/TestCompilerSessionCache.ps1`：会话冷热/依赖失效、缺损修复、取消/失败、三种纹理、全部高级模式与原版目录互操作。
- `tests/CompilerLegacyCacheMetadata.cs`、`scripts/TestCompilerBackendBaseline.ps1`：原 DLL 元数据、原 BAB 字节差分及缓存导出/导入证据。

## 原缓存恢复旧验收（39039961，2026-10-29）

[运行 40814401237](https://github.com/wu162/NewSdk/actions/runs/40814401237) 整体 success；二进制及测试源码均为 `39039961847cd1ca9cb93743fd51741a4e5c9f49`，Debug/Release 均重新构建。[状态与日志摘要](../evidence/refactor/legacy-cache/40814401237-1/summary.json) 保留运行、任务、产物及来源，原始收据位于该运行的归档内。

下列用例名来自 `TestCompilerSessionCache.ps1`，两配置会话协议各检查 101 份报告、4030 条事件，错误为 0。

| 范围 | 已通过的直接用例及断言 |
| --- | --- |
| 三种纹理 | 各类型 `*-session-cold/hot/invalidated`、`*-changed-hot`、`*-build-cold/hot`；同长度/mtime 图像修改失效，修改后对照原 BAB 四流及完整 CDATA |
| 原默认值、压缩切换 | `original-defaults-cold/hot`、`original-xml-switch`；实际 Deflate、文档复用、XML 替换且无旧格式歧义 |
| 冻结/解冻 | `original-freeze-edit/repeat`、`original-unfreeze/hot`；修改输出正确，冻结会话字节不变，解冻后更新可复用 |
| touch | `original-touch-disabled/enabled`；检查实际资产缓存文件 mtime |
| 独立目录及别名 | `original-cache-directories`、`original-cache-directory-aliases`；`/scd /bcd /iod` 与 `/scp /bcp` 共享真实缓存 |
| SDK 配置及 CLI 优先级 | `original-sdk-cache-config`、`original-sdk-cache-config-cli-override`；`buildCache`、`buildCacheRoot`、报告来源 |
| 原 BAB 导入 | `original-cache-export` → `original-bab-cache-import`；导入旧 XML 会话及中间资产，文档复用且无资产编译 |
| 部分失败保存 | `original-partial-failure/retry`、`original-no-partial-failure/retry`；psc true 保存完成文档，psc false 不保存会话但保留独立资产供重试 |
| 真实取消与重试 | `actual-cancel`、`retry-after-actual-cancel`；CtrlBreak、退出码 6、无部分发布、psc false 的原确认会话不变 |

Texture CDATA 数量由固定原 DLL 的 `HasCustomData` 确定，并非每种纹理都产生 CDATA。`/psc:true` 完成文档后遭 CtrlBreak 的持久化、原 BAB 导出的 Deflate 直接导入，未有独立生产用例；不能将其他组合的成功改写为这两个组合已独立验收。

同次运行还通过完整未精简 mymod 的原 BAB 四流（两配置差异均为 0）、原 LoD/MakeBig 及 SKUDEF 差分，以及独立五文件 ZIP 的生产 BIG/协议门槛。mymod 本身无 APT/maps 输入，两组覆盖来自独立生产用例；完整游戏数据、WBData、游戏内运行、完整 UI/runtime 和中文纹理路径未新增验收。

以上为原缓存恢复阶段的历史记录。当时续接未更改生产输入，沿用运行 40814401237，且曾记录下载 HTTP 403；本轮性能实现另以顶部的 40836177604 验收，并已通过 GitHub 产物工具取得新归档、在本地复算 SHA256 和核对原始收据。


## 2026-10-29：删除重复 Schema/文件检查与 Texture 部署审核

一次 CoreStreamCompiler 请求持有一个已编译 SchemaSet，全部流共享该快照；新的请求重新读取闭包。删除配置加载的提前 Schema File.Exists 和每流 ReloadIfChanged，Schema 加载结束复用读取时记录的目录身份，避免第二轮枚举。独立 Core 的显式 ReloadIfChanged 仍用于可变宿主及其失效回归；它不是生产流之间的热路径。

FileHashItem 使用 Settings.FileIdentity 一次获得存在性、LastWriteTime 和已有 SHA256 收据，宿主复用 FileHashCache 的目录记录；未变输入连同跨进程持久收据均不打开文件体。确定变化的文件延迟到 Hash 访问时一起计算 FastHash 与 SHA256；时间未变且没有可用收据时，IsDirty 在一次读取中同时计算两者，Hash 提交结果时不再读文件。保留旧 FastHash 的 1 MiB 分块及游戏值，新的 SHA256 写回宿主收据，缺失/重建和旧序列化条目继续失效。

LegacyTextureAdapter 直接从编译器旁加载准备好的混合 DLL，不再进行运行时部署审核（patch JSON、EXE/DLL SHA256、保护 PE 审核回执和程序集枚举）。PE 重定向与构建时审核继续用于生成可运行 DLL，Texture 原版载荷/CustomData/错误、重用与销毁差分仍执行；对应测试改为确认缺少运行时部署清单也可工作；测试宿主也删除重复清单审核，继续检查类型和程序集归属。

Windows 完整验收：生产/测试来源 `639a5ff73e1df8a12ec0fa77e5b4324186bae8fb`，[40878062262](https://github.com/wu162/NewSdk/actions/runs/40878062262) success，Debug/Release 全矩阵、完整 mymod 原版差分及最终五文件 ZIP 均通过。首轮失败 40876849281 为测试宿主退场时重复读取已删除清单，修复后完整重验；保留原失败记录。有界新旧 SDK benchmark 已通过，见下表和原始报告。


短 benchmark：二进制来源 `639a5ff73e1df8a12ec0fa77e5b4324186bae8fb`，测量脚本来源 `0c6ea99fe2973fe3e3e0232ab9e1e3e9f7ea9fc6`；[40880915003](https://github.com/wu162/NewSdk/actions/runs/40880915003) success。测量实际 49.06 秒，108 次/54 对，同一物理 SDK、Schema、MOD、顺序一致的搜索根，原版仅调整 CLR4 启动配置。全部流/CDATAs 成员、长度、SHA256 和原始字节相等。默认 both-deflate：

| workload | 老 SDK 冷态 ms | 新 SDK 冷态 ms | 老 SDK 热态 ms | 新 SDK 热态 ms | 新版热态耗时变化 |
| --- | ---: | ---: | ---: | ---: | ---: |
| mymod | 502.76 | 485.05 | 316.98 | 328.16 | +3.5% |
| upheaval-armor | 323.37 | 343.63 | 317.69 | 295.75 | -6.9% |
| texture-16 | 518.96 | 546.75 | 328.27 | 335.51 | +2.2% |

冷态 n=1、热态 n=2/engine，中位数；OS 页缓存不受控，仅编译器缓存清空。mymod 为完整输入副本的 base 流，Upheaval 仅 11 个 Armor 文档/12 个资产，texture-16 为合成样例；未测完整 Upheaval、BIG、增量编辑或跨流整体耗时。新 CLI 包含 JSONL/报告、staging 与原子发布，原 BAB 直接输出四流。三组默认热态新 SDK 均为零 XML 解析、零资产编译、零完整文件哈希读取；这些小样本没有显示普遍端到端提速。不得将本次与不同 VM 的历史绝对耗时差解释为本轮优化因果收益。

[全部缓存模式报告](../evidence/refactor/sdk-benchmark/40880915003-1/comparison.md)、[CSV](../evidence/refactor/sdk-benchmark/40880915003-1/comparison.csv)、[来源/归档 SHA256/热态计数](../evidence/refactor/sdk-benchmark/40880915003-1/summary.json)。原 benchmark ZIP 和完整 Windows ZIP 均已下载并复算摘要，与 GitHub artifact digest 一致。
