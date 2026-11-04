# 编译器缓存性能：深入源码分析

本轮入口为 `recovery/legacy-sdk-cache` / `665468cb`，成果分支 `perf/compiler-cold-hot-cache`、[PR #38](https://github.com/wu162/NewSdk/pull/38)。先据真实计时定位，再决定改动；以下阶段耗时用于排序，不能直接当作可节省时间或端到端收益。

第一轮生产源码 `4d732a14` 的 [Windows 41165490728](https://github.com/wu162/NewSdk/actions/runs/41165490728) 已完整成功。第二轮源码 `d3b3150f` 已提交，[Windows 41167799236](https://github.com/wu162/NewSdk/actions/runs/41167799236) 已完整success，原归档已复算；随后同步074bf768的组合e2dba3c3已通过[41169755141](https://github.com/wu162/NewSdk/actions/runs/41169755141)完整验收（含真实FLA/AS），最终同机原始MOD冷/热耗时减少8.78%/25.63%；扩容正式吞吐减少13.48%/34.71%，但追加detailed预算超时、整组failed保留，见主报告的范围审核；不以第一轮成功替代第二轮验收。完整原始 MOD 与扩容 MOD 的同机测量也分别报告，详见[优化与计量说明](COMPILER_COLD_HOT_OPTIMIZATION.md)。

## 当前主次与计时边界

第一轮候选的完整 Upheaval、jobs4 冷态 detailed 报告总耗时约 33.452 秒。base 的实例校验段 `core.xml.instances.validate` 自耗时约 **7.45 秒**，实际 XSD 校验 `core.xml.schema.validate` 约 **5.54 秒**，XML 解析约 **2.82 秒**；历史同类覆盖中的 overrides 约 2.17 秒。Audio/Animation 是可与前端重叠的后端累计工作，不能与这些时间相加推算总耗时。实例校验段同时包含 XML 哈希、引用处理、类型信息及少量文件依赖工作，7.45 秒不是单一函数的独占收益。

同一轮纯热态 detailed 总耗时约 1.608 秒，五流 `cache.load` 合计约 **375 毫秒**；base 引用闭包约 **150 毫秒**、输出排序约 **117 毫秒**。热态零 XML 解析和后端编译；进一步优化应围绕元数据初始化及输出准备。跨 VM 的旧报告只支持调查方向，收益使用同机、同输入配对。

| 优先级 | 路径与判断 | 本轮状态 |
| --- | --- | --- |
| 已完成的必要基础 | 每文档复制 Schema、每流反序列化完整会话、纯查找树索引属于明确重复工作。 | 第一轮实现并通过完整 Windows/SDK/ZIP/完整 MOD 验收。 |
| 冷态首要：XML 哈希写入 | `HashingWriter` 的逐字符字符串创建、拼接、截取和逐块 ANSI 非托管分配，位于两次必经 XML 哈希中。 | 已实现并通过最终组合验收；保持原 512 字符分块和字节算法。 |
| 冷态同组：Schema 类型分类 | 每个元素和属性重复沿类型继承链判断引用类别、扫描 `refType`、计算固定 TypeId 字符串。 | 已实现并通过最终组合验收；按本次已编译 Schema 对象缓存事实，仍处理每个实例的实际引用。 |
| 热态同组：删除只写的 FileConfig | `SessionCache.CurrentState.FromLast` 反复构建额外路径索引和列表，但实际查找与保存只用 `Files`。 | 已实现并通过最终组合验收；删除纯写索引，保留 `Files` 的排序、序列化与失效检查。 |
| 次优：解析诊断元数据 | 每个 XML 节点插入都分配元数据对象及链表节点，成功校验后丢弃；现有错误查找还存在相等比较缺陷。 | 已定位，暂不与本组主改动混合。 |
| 暂缓的结构性改变 | 共享 NameTable、复用/替换 XML validator、继承合并索引、重写排序。 | 需要进一步分段和正确性证据，不在当前组冒进。 |

第二组新增打点已确认：其独立完整MOD诊断中，实例hash约910.61ms、引用处理5889.47ms、实际XSD5955.83ms，热态cache.load合计271.32ms。这是第二组自身分段，不能与另一VM第一轮毫秒直接算速度收益。下一层应优先拆解XPath/SchemaInfo遍历、字符串hash登记、显式强引用的类型对判断与文件路径解析；进一步削减不足1秒的hash微成本优先级降低。

## 两次 XML 哈希的真实来源

[AssetDeclarationDocument.cs](../src/Managed/Core/BinaryAssetBuilder.Core/AssetDeclarationDocument.cs) 中，`ProcessOverrides` 对每个实例调用 `OverrideInstance`；即使没有继承，方法末尾也调用 `HashProvider.GetXmlHash` 生成 `PrevalidationXmlHash`。之后 `ValidateInstances` 在 XSD 校验已插入默认值后再次生成实例身份哈希。因此 overrides 计时不能全部归因于 `NodeJoiner`；优化哈希写入能同时覆盖这两个阶段。

[HashProvider.GetXmlHash](../src/Managed/Core/BinaryAssetBuilder.Core/HashProvider.cs) 使用 `XmlWriter` 将节点写到 [HashingWriter](../src/Managed/Core/BinaryAssetBuilder.Core/HashingWriter.cs)。原 writer 的 `Write(char)` 每次建立单字符 string；各 `Write` 继续合并已有尾串，再按 512 字符 `Substring`。每块 `GetTextHash` 又经 [FastHash](../src/Managed/Hash/FastHash.cs) 的 `Marshal.StringToHGlobalAnsi` 分配、转换和释放。它们不产生新的语义信息，分配量随序列化字符增长。

第二轮使用复用的 `char[512]` 和 `byte[512]` 收集字符，ASCII 块直接调用原 FastHash 字节算法；含非 ASCII 的整块仍走原 ANSI 字符串路径。必须保持以下合同：

- 512 指 UTF-16 字符，不是编码后的字节数；代理对跨块、DBCS 及 ANSI 转换仍按旧块边界处理。
- FastHash 的原字符串包装器使用 UTF-16 `Length` 作为字节数；非 ASCII 不能擅自换成 UTF-8 或完整 ANSI 编码字节长度。
- ASCII 路径使用显式 `runningHash` 与实际块长；不能使用以复用数组总长作默认 seed 的重载。内嵌 NUL 是字节 0，不能用字符串终止长度替代实际长度。
- `GetFinalHash` 只观察状态，重复调用及调用后继续 `Write` 不应提前消耗尾块或改变分组。

定向验证使用旧 writer 的字符串分块规则作参照，覆盖 0/1/511/512/513/1023/1024/1025 字符、多个 seed、碎片化 char/string/array 写入、非 ASCII 和代理对跨边界、重复 final/继续 append，以及真实 `XmlWriter` 序列化。随后仍需原资产/manifest 原字节和会话冷热/失效/取消回归。

## Schema 分类事实与会话冗余索引

`AssetDeclarationDocument.HandleReferenceType` 原先对每个元素和属性重复调用 `XmlSchemaType.IsDerivedFrom`，判定 WeakReference、AssetReference、FileReference，随后还查是否为可哈希类型；输出 `TypeId` 及读取 `xas:refType` 也反复计算。它们取决于 [SchemaSet](../src/Managed/Core/BinaryAssetBuilder.Core/SchemaSet.cs) 的已编译快照，可按 `XmlSchemaType` / `XmlSchemaAnnotated` 对象缓存，重载 Schema 时清空。实际引用目标、引用顺序、文件解析及实例依赖不能缓存成跨实例结果。

[SessionCache](../src/Managed/Core/BinaryAssetBuilder.Core/SessionCache.cs) 的 `FileConfigs` 将路径映射到 `FileConfig.All/LastCached`。当前生产代码只写这个映射；`TryGetFileItem`、`SaveDocumentToCache` 和最终保存均以 `Files` 为依据。第一轮内存复用仍每流调用 `InitializeCache`，所以这些无消费者的对象和树操作会重复发生。删除它们比改变真正持久化索引的比较器/排序风险更低；新增 `cache.initialize`、`cache.check-files`、`cache.from-last` 分段可解释残余 375 毫秒，但不能预先假定都能消除。

## 次优和暂缓项

**节点行号映射。** `LoadXml` 为 `.xml` 注册 `NodeInsertedHandler`，每个节点分配 `XmlNodeWithMetaData` 与 `LinkedListNode`，校验成功后清空。在 `HandleValidationEvents` 中用新建元数据调用 `LinkedList.Find`，但该类型只有 `Equals(XmlNodeWithMetaData)`，没有 `IEquatable<T>` 或 `Equals(object)`；默认泛型相等比较不会调用该重载，实际错误行号匹配失效。可改为 `Dictionary<XmlNode,int>`，减少分配并恢复 O(1) 行号查找。需验证普通解析/XInclude 的来源行号、非法 XSD 的错误类型/文本，以及节点被覆盖复制后无映射时的回退。收益上限属于解析段的一部分，不能把全部 2.82 秒算给它。

**继承合并。** [NodeJoiner](../src/Managed/Core/BinaryAssetBuilder.Core/NodeJoiner.cs) 的 `FindInBase`、`FindPrevNode` 重复扫描固定 Schema 粒子，`SelectSame` 在大兄弟列表中线性匹配，可考虑父类型/子名索引及保序节点索引。不过 overrides 同时包含前述 XML 哈希，尚无 inherit-only 计量。继承的 base 优先级、重复 id、choice 替换、insertPosition 和 +/- bitflag 规则都影响原字节，先取得合并独占成本再决定是否改写。

**共享 NameTable。** 解析器和 DOM 目前逐文档创建名称表，共享请求级 Schema 名称表可能减少 atomization。但认证 worker 直接借 DOM/NamespaceManager，owner 可在 worker 工作时继续解析；共享可变名称表可能引入跨文档读写并发。需要检查全部桥接 XPath/命名空间路径，不能以 owner 单线程推定安全。

**复用 XML validator。** 实际 XSD 的 5.54 秒是重要残余，但 `XmlDocument.Validate` 还负责默认属性、PSVI/SchemaInfo、身份约束、错误 SourceObject 等。直接复用一个 `XmlSchemaValidator` 或用简化遍历替换，不是删除重复检查；它要求重现 BCL wrapper 合同。当前保留统一完整 XSD，避免把功能裁剪计作性能收益。

**StableSort：优先级随规模变化。** 原 MOD 的排序约117毫秒，是次要项；第一轮同机扩容候选的详细热样本报告4870毫秒，其中 `core.output.order` 为1720.16毫秒，已成为大型负载主要残余。这是独立detailed归因，不混入off中位数。完整输出仍需稳定的依赖、base manifest前缀、TypeId哈希别名及culture相等行为；`List2.Sort` 按16384块排序并稳定合并，直接换排序器会改变跨块相等项顺序。下一步可先原型化请求内culture sort key缓存，比较原comparer的逐对结果与完整排序序列，再测NLS比较/分配收益；尚未实现或测得加速。保留原数值排序输入与legacy插入规则，不能把总引用阶段全部算给排序。

上述分析的优化上限受所在独占阶段、阶段重叠与真实负载限制。本轮收益以主报告最终同机表和范围审核为准，所有较慢样本和未通过/未测范围均保留。
