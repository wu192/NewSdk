# 热缓存修改后构建优化

已减少前端工作，**本次小合成负载没有稳定的端到端编辑提速结论**。无关父兄弟修改的子文档不再解析，但三组 off 中位总耗时略慢；纯热态也略慢，数据全部保留。

[PR #38](https://github.com/wu162/NewSdk/pull/38) 未合并。生产源码为 `8e8cf766df1016c6095158b62ea18f48a5c37cfe`，组合验收 [41178094135](https://github.com/wu162/NewSdk/actions/runs/41178094135) success，验收驱动 `2b4e97f9`；后续测试/文档提交不代表新二进制。基线为已验收 `e2dba3c3` / 41169755141。禁止强推，未经用户明确批准不合并。

## 实际改动

- instance Include 与 XInclude/实际素材依赖分开。父文档仍完整处理和验证；子文档在父完成前端后，比较实际父实例的最终 XML/类型/处理哈希、物理来源、实际祖先闭包及文件绑定/内容。物理路径或有效继承内容变化仍影响后代，同一路径兼作 XInclude/素材时仍保留原文件依赖。
- 读取旧 v18 会话时，没有来源证据的继承文档安全重解析一次，重新采集实际依赖；写 v19，让旧二进制拒绝窄依赖格式。没有直接删除旧模糊记录里的父文件哈希。
- 请求内按需保存成功父实例原验证节点，保留默认属性、PSVI 和引用处理后的阶段；跨流先清理 CurrentState、验证依赖/Define，再恢复只读源。失败/取消不发布未完成快照，快照不持久化。
- 引用处理改等序 DOM 遍历，缓存 Schema 引用元数据、显式强引用类型对、全局类型查询和可继承性；同次验证复用文件解析，引用哈希缓冲首次写入才分配，保留原 capacity 哈希字节。完整 XSD、原引用拒绝、输出顺序及失败清理继续保留。

仅五份 Managed Core 生产文件改变。SDK、原生/Bridge 插件、生成代码及构建输入未改，既有独立插件组件证据沿用；新 Core 集成和 SDK 另取本轮结果。不恢复全图 Define 指纹、全树 SHA 扫描或 BIG/SKUDEF 包缓存。

## 同机编辑配对

[41178837554](https://github.com/wu162/NewSdk/actions/runs/41178837554) success，80/80 样本通过。[全部原始数据](../evidence/refactor/hot-edit/measurement-41178837554/measurement.json)、[精选三对/阶段归因](../evidence/refactor/hot-edit/measurement-41178837554/selected-measurement.json)、[归档及产物身份](../evidence/refactor/hot-edit/measurement-41178837554/archive.json)。

同一 Windows VM、相同输入、jobs4，每场景三组独立 off 配对；每次新进程，OS 页缓存未控。负载为 512 Armor 加继承/Define 的小合成 MOD。冷准备和原字节比较在计时外，detailed 独立测，不进入 off 中位数。

| 修改后 off，中位毫秒 | 基线 → 候选 wall | 基线 → 候选报告耗时 | XML 解析 | 后端编译 |
| --- | ---: | ---: | --- | --- |
| 父有效字段，n=3/实现 | 518.893 → 511.192（减少1.48%） | 429 → 421 | 2 → 2 | 2 → 2 |
| 父无关兄弟，n=3/实现 | 490.765 → 493.781（增加0.61%） | 401 → 403 | 2 → 1 | 1 → 1 |

有效父三对 wall 为 491.929→509.558、551.379→527.904、518.893→511.192ms；无关兄弟为 492.825→520.779、490.597→493.781、490.765→493.634ms。保留全部较慢样本，不据此宣称稳定编辑加速，也未为改善数字追加测试。

**无修改纯热态单列**：两场景 hot-before/after 合计 n=12/实现，wall 中位 427.883→438.657ms（增加2.52%），报告耗时336.5→347ms。全部0解析/0编译。这是本次小负载结果，不能与上轮不同 VM 的完整 MOD 纯热态收益混算。

六类 detailed 的解析/编译分别为：大文档单资产1/1→1/1；有效父2/2→2/2；无关兄弟2/1→1/1；使用Define3/2→3/2；未用Define1/0→1/0；丢会话元数据37/0→37/0。继承取 DOM 的 inheritanceTarget/reloadAvoided 均0。无关兄弟的基线 dependentFileChanged 子重载消失；候选有效父仍有 inheritanceChanged。每代实际发布原字节一致，每次再热均0解析/0编译。

单次无关兄弟 detailed 中，继承合并阶段5.9683→1.3891ms、引用0.5093→0.4360ms、XSD0.2270→0.1802ms；候选身份计算0.0645ms，wall509.764→512.853ms。它证明工作范围收窄，不是稳定阶段提速估计；本场景 Schema 初始化仍约179ms/实现。

## 父快照减少的工作及成本

[父快照 D/R 成功结果](../evidence/refactor/hot-edit/repair-41176937044/Release/CompilerInheritanceSnapshot.json)。相同输入和同三份第一流中间资产：控制读序列化会话，候选保留请求内原节点。Armor 不使用 BuildCache，两边以相同普通中间产物保证后端可用，从而单独比较父前端。

| 工作 | 无快照控制 | 请求内快照 |
| --- | ---: | ---: |
| XML解析 / 继承重载 / 后端编译 | 3 / 1 / 1 | 2 / 0 / 1 |
| 父解析 / XSD / 合并 / 引用 ms | 0.0909 / 0.0478 / 0.0208 / 0.0044 | 全0 |
| 单次 detailed wall ms | 17.295 | 13.5745 |

仅 n=1 机制诊断，不作为稳定吞吐或完整 MOD 收益。默认属性、PSVI、瞬态清理、独立冷重建四产物原字节、父变化拒绝及失败/取消均通过。候选仍持有整份 OwnerDocument；该微型输入保留1文档、4节点、15属性、228 XML字符，不是托管内存字节，未量化完整 MOD 的快照内存。

现有完整 MOD jobs4 cold detailed（sessionCache=false）解析925/编译3354，按需捕获38文档/84实例、构造86个源身份；inheritanceTarget/reloadAvoided均0。因此该完整 MOD 样本没有证明避免父重载的收益。只有依赖/类型/输出证据成立才跳过取 DOM 的前端，缺少中间产物时仍加载。core.xml.inheritance.frontend只覆盖PrevalidatedLoad，Shallow Load的成本由父文档调用帧计时，不能把前者当全部继承重载成本。

同一现有诊断中，引用1143.0022ms、XSD4412.7276ms、实例哈希648.0809ms，见[原诊断摘要](../evidence/refactor/hot-edit/acceptance-41178094135/fullmod-cold-jobs4-detailed.json)。没有同机版本配对，不能与历史5.89s等数据计算引用降幅；未追加完整 MOD 基线或新 detailed 运行。

## 验收来源与失败保留

[组合原收据和十文件/两ZIP身份](../evidence/refactor/hot-edit/acceptance-41178094135/archive.json)、[组合验收清单](../evidence/refactor/hot-edit/acceptance-41178094135/repair.json)。所有复用都核对实际输入闭包、源码/测试来源和产物身份；新归档首次核验后复用记录，没有重建未变基线。

| 运行 | 可用范围 | 原终态 |
| --- | --- | --- |
| [41174952443](https://github.com/wu162/NewSdk/actions/runs/41174952443) | 8e8cf766 D/R产品、12既有Core fixture、扩展热缓存、FLA、64worker、Texture、CLI/CtrlBreak、cache/root/payload/session/BIG | failed保留 |
| [41176937044](https://github.com/wu162/NewSdk/actions/runs/41176937044) | 父快照/引用探针D/R成功，后续按相同实际源码复用 | failed保留 |
| [41177598230](https://github.com/wu162/NewSdk/actions/runs/41177598230) | 性能级别/原字节/非法输入、原BAB四目标、30报告/978事件协议成功 | failed保留 |
| [41178094135](https://github.com/wu162/NewSdk/actions/runs/41178094135) | Include D/R各140检查/4组、完整mymod原SDK、两五文件包、完整Upheaval22样本、组合清单 | success |

Include 覆盖父无关1解析/有效祖孙3解析、同文档继承链、Define/XInclude、非法父/重复声明拒绝和修复、v18迁移、同路径双来源真实依赖、同字节搜索根切换，逐代独立冷控制原字节和再热0/0。完整Upheaval22样本全原字节一致，均0错误/1859原警告，全部hot0解析/0编译。

失败来自新探针误用请求初始化时机/处理器生命周期、Armor缓存资格、xsi:type既有拒绝、旧缓存缩进格式，以及旧性能脚本要求已删除的identity.sha256。只修测试，断言中的原字节、失效/拒绝、失败取消和完整门槛继续保留。原Core对xsi:type属性缺PSVI类型的拒绝未变；实际类型缓存隔离另用明确的内部PSVI探针，不声称新增公开支持。

Debug ZIP SHA256为e79df24e769e1ca6dc746816e36a9d2de69a347328ce889db4e8b341d3168061，Release为281b08d7bc8042e57d509d96e3dd0b9afc737ebcd358ca1cdd4c79881432abcb。首次核验后不再下载/解压/哈希这些有效归档。

## 未完成及下一步

局部修改仍对整份 XML 做前端，未实现实例级复用；未新增游戏内验收、完整MOD版本间同机性能配对或完整MOD快照内存字节测量。本次小负载纯热态和无关兄弟总耗时略慢，原因未完成稳定归因，不能把减少解析次数等同端到端提速。

现有会话不保存XML。最小请求内下一步可保存成功的“表达式/继承合并后、XSD前”实例阶段，跳过未变实例的表达式和合并，继续整文档完整XSD。独立CLI编辑宜先限无继承实例，保存原始实例身份、所有实际Define读值和表达式补丁；ExpressionEvaluator.LookupDefine现在只登记外部Define，不能直接当实例读集，未复现残留不改原集合。

继承实例还需合并阶段树或重放记录；最终节点含绝对文件路径、强引用索引和TypeId，不能重新执行早期前端。复用引用阶段还需重放字符串哈希登记与strong/weak/file元数据，继续清理每流索引/CDATAPATH。SDK未检出key/unique/keyref不证明可配置Schema没有跨实例约束，完整XSD仍须保留。下一轮以实际编辑的前端范围和当前XSD/启动成本为主线，父DOM剪裁另需证明PSVI/命名空间/所有权不变。

历史完整证据继续引用[上一轮报告](COMPILER_COLD_HOT_OPTIMIZATION.md)和[源码分析](COMPILER_CACHE_SOURCE_ANALYSIS.md)，不重展开或重建。
