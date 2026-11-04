# Tokenizer 并行与借用后端输入

2026-10-30，`recovery/legacy-sdk-cache`。本轮基于 fbf51cee；生产源码、测试和新编译二进制均为 `5be40ac421fa4562654d34cc3d62050eca7a389e`，统一验收 [40983673225](https://github.com/wu162/NewSdk/actions/runs/40983673225) **success**。旧实现的二进制与验收见 [历史记录](BACKEND_PARALLEL_MIGRATION.md)，不能代替本轮结果。

## 开放的三种类型

`PreparedBackendWork.Eligibility` 仅允许精确内建插件及其唯一 TypeId：GameObject 2486173485、WeaponTemplate 2496977262、ArmorTemplate 980180622。未知实现、错误 TypeId 与原有未认证路径保持 owner 屏障。

三种 Bridge 的 `ProcessInstance` 只读 `Node`、`Document.NamespaceManager`、`Handle.TypeId` 和初始化后不变的 platform。`CompileXml` 创建局部 map/vector/输入结构；Native 的 `CompileGameObject`、`CompileWeapon`、`CompileArmor` 使用局部 Relo::Tracker，常量字段/模块/枚举表没有运行期写入。CRT 标量解析不改变全局 locale。输出数组属于每次调用。三个插件可选 trace 的 GUID 文件名独立，共享 calls.tsv 追加由各自 TraceGate 同步；正常编译没有增加全局锁。

## 可以删除的复制及源码依据

| 删除项 | 可以直接删除的依据 |
| --- | --- |
| `CopyBackendXml` 全树及属性复制、私有 XmlDocument/NameTable | `DocumentProcessor.ProcessDocumentContents` 先完成 ProcessExpressions/ProcessOverrides/Validate，再调用 ProcessInstances；认证 Bridge 不修改 DOM。直接借用还保留原默认属性的 Specified/SchemaInfo 及祖先 XML 上下文。 |
| namespace 字典、prefix 列表和新 XmlNamespaceManager | 绑定在 AssetDeclarationDocument.Load 时构造，认证后端只读取；OpenDocument 在重开 pending 文档前 Flush。XPath 位置和查询对象仍为每次调用局部值。 |
| InstanceHandle 和三种引用列表/引用句柄的复制 | 后端只用 TypeId，引用插槽已由前端写入 XML；不遍历目标资产、读取引用缓存或注册 HashProvider 状态。OutputManager.GetBinaryAsset 在替换同资产声明前 FlushAsset。 |
| 普通任务的新 InstanceDeclaration/CurrentState | worker 结束后 owner 才 Verify/Commit/RecordCommit；其中清除 XmlNode 的行为在 GetResult 之后。无 CDATA 后端无需写声明。 |
| CDATA 声明的语义重新初始化 | 仅浅拷贝 CurrentState，隔离 CustomDataPath；所有只读引用共用。绕过 XmlNode setter，避免重新计算句柄及移除 inheritFrom。 |
| 调度器的一次重复整树估算、纯内存任务的 List<FileStream> | 准备不再分配 DOM，先按队列容量等待，在 TryPrepare 内估算一次，随后用实际工作集估算控制总 retained。没有外部文件就不建立租约集合。 |

文档 MakeComplete/MakeCacheable/Reset 已通过 DeferCompletion 延后到该文档所有序列提交；引用 stream 和 manifest/link 前 Flush。同一资产及同文档复用都保留现有屏障。失败/取消先 join 所有 native 调用，再释放 Core/插件和输入租约。借用契约不允许在 standalone TryPrepare 与 Execute 之间任意修改原输入；旧的防御性 owner mutation 测试已改为实际生产生命周期和借用身份检查。

## 必须保留的隔离和转换

- CDATA 私有路径及提交时移动：Audio 会写入文件，不能与原声明/目标路径共享写状态。
- 每次调用的 XPath cursor、Native 字符串和树、Relo::Tracker 及三个托管输出数组：对应 C++ ABI/拥有期和后端写入，无法通过删除托管准备阶段的复制一并删除。
- Audio/APT 外部输入只读租约：后端按原文件路径重新打开；取消和失败的 join 前不能释放。
- 内存工作集估算：DOM 不复制仍有 Bridge/Native 的输入展开和输出分配。原 DOM 字符/节点估算保留作工作集界限，三种 tokenized 类型加入四倍模块/过程树余量；W3X 帧/通道及外部输入倍率保留。

## 集中验证范围

新增生产夹具的24个 tokenizer 资产及其重复文档已通过原 SDK aggregate Schema；缓存/jobs 矩阵要求每类只编译8次，并确认三类都实际进入 worker。组件用同一 DOM/NameTable 的36次 tokenizer 调用覆盖四平台0/1/2/99，比较三缓冲、DOM 未修改及 trace 索引24条/类型；原 XML284×四平台矩阵继续执行。原缓存热态零准备、部分失败/重试、真实 CtrlBreak/native drain、mymod 原 BAB 四流/BIG/SKUDEF 与完整 Upheaval 同 VM jobs1/4/auto 均由既有生命周期工作流统一执行。以上范围均已在本轮 Debug/Release 新构建上通过；完整结果见下文。


## 新验收与性能结果

来源：源码/测试/二进制 `5be40ac4`，运行40983673225，artifact12947247376。原ZIP归档SHA256 `8b4be2840e1feb7b27c48135139f81cf890a89e53630053d2f9701f1837288c5` 已实际下载复算匹配。Release五文件ZIP SHA256 `0a79bdf99ac31d3b365fdaafc3fec5f4c263dd48d975b58766ced1e1b8f75638`；二进制和每个原收据身份见[摘要](../evidence/refactor/tokenizer-parallel/40983673225-1/summary.json)。完整原收据、日志、对照输出及二进制保留在[运行归档](https://github.com/wu162/NewSdk/actions/runs/40983673225/artifacts/12947247376)。

- 三 tokenizer 同一DOM/NameTable worker波在Debug/Release、平台0/1/2/99均真实峰值4，三缓冲逐字节一致、原DOM未修改、trace索引完整。XML284×四平台保持全部通过。
- 64个生产缓存/jobs用例通过，三类8资产各只编译8次，重复文档/同资产复用屏障和worker路由均验证。原BAB差分、实际CtrlBreak、native失败/取消join、全部缓存组、完整mymod原SDK四流/BIG/SKUDEF/Additional和独立五文件包门槛全部通过。
- 完整Upheaval仍为2,723文件/5流/每冷3354后端调用；121 GameObject、108 WeaponTemplate、12 ArmorTemplate加入worker。非认证插件回退117次，正好为115 Texture+2 OnDemandTexture；10次大任务预算回退保留。旧358次非认证回退减少241次，由此次路由范围直接解释。

| jobs | 无缓存冷秒 | 默认冷秒 | 默认热中位秒 | 单资产修改秒 | detailed准备秒 | 后端峰值 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 70.480 | 73.489 | 2.823 | 3.092 | 0 | 1 |
| 4 | 41.610 | 43.611 | 2.868 | 3.098 | 2.126 | 4 |
| auto | 41.716 | 43.632 | 2.823 | 3.104 | 2.029 | 4 |

本轮同VM默认冷jobs4比jobs1耗时减少40.7%。22样本全部0错误、1859原警告；18个原输入样本完整BIG/SKUDEF一致，4个修改样本与独立串行改变流重建一致；热缓存三档均xmlParse/instanceCompile/prepareMs为0。单动画修改只重编译1资产、解析1文档。

旧实现准备阶段jobs4/auto为4.561/4.710秒，本轮2.126/2.029秒；旧结果来自不同VM，不能把差值当作严格的去拷贝收益。此次确定删除的是复制操作本身，吞吐表比较的是同一新二进制的jobs档位。每档冷一次、热两次，未控制OS页缓存；off用于吞吐、detailed用于归因，累计阶段不可相加。派生[计量摘要](../evidence/refactor/tokenizer-parallel/40983673225-1/measurement-summary.json)、[生产矩阵](../evidence/refactor/tokenizer-parallel/40983673225-1/production-cases.json)和两配置组件收据均已归档；摘要明确记录原始归档路径和原收据SHA256，原始完整报告保留在运行ZIP。
