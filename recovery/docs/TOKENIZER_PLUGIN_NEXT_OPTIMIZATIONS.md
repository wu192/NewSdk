# GameObject / Weapon 原生编译层进一步优化研究

依据：`9f9d176761ff28cee993c27ba61b864041a26299` 的源码；本轮已实现部分见 [插件计量](TOKENIZER_PLUGIN_PERFORMANCE.md)。以下保留当时的研究与原型依据。用户授权继续后，六个方向现已接入正式插件，最终源码为 `d60904bc`，实现范围及 Windows 实测见 [第二阶段报告](TOKENIZER_NATIVE_STAGE2.md)。下面的 Linux 算法数据仍是历史研究，不能替代正式插件或完整 MOD 成绩。

## 优先顺序

| 优先级 | 位置与现状 | 建议改法 | 主要覆盖 |
| --- | --- | --- | --- |
| P0 | `GameObject.h:22`、`WeaponTemplate.h:20` 等使用普通字符串 map；`find(const char*)` 隐式构造 string | 属性类型统一透明比较器 `std::less<>`，查找直接使用字符指针；复用一次 find 结果 | 根属性、所有模块和 Nugget；空输入也有查找成本 |
| P0 | `Relocation.cpp:141` 对每个 member 调用 `GetMemberOffset`，后者从头扫描所有 block | 数据 owner 完成分配后，建立一次按原生地址排序的区间索引，批量修正时查区间 | Draw/模型状态/大量 Nugget 等复杂资产 |
| P1 | `EnumBitset.cpp:700` 每词 substr/erase，再线性查表 | token 用指针范围/string_view；已证明排序的表用二分或预建索引 | ModelCondition、ObjectStatus、KindOf 等多词 mask |
| P1 | 根/模块/Nugget 每次重新查找并解析缺省字符串 | 在元数据层预解码标量缺省值或只读无指针模板，实际输入覆盖；提供值时借用原字符串 | 常见小资产、默认属性很多的模块/Nugget |
| P2 | ModelCondition 每个列表两次扫描同一 children | 每节点一次分组/计数，仍按原 schema 次序分配并按原列表次序输出 | 模型状态、动画、Contain 等嵌套结构 |
| P2 | token、data 各成缓冲后再合并，最后复制到托管数组 | 一次确定总长度，按固定区间直接写出 token/data | 较大资产；降低临时峰值与内存搬运 |

## 1. 属性查找的隐藏原生分配

[GameObject 根循环](../src/Native/GameObject.cpp#L355)、[模块字段循环](../src/Native/GameObjectModuleFieldsRuntime.h#L86)、[Weapon 根循环](../src/Native/WeaponTemplate.cpp#L262) 都以 `const char*` 字段名查询 `std::map<std::string,std::string>`。默认比较器不支持异构查询，先构造临时 `std::string`；长名字会使用原生堆。上一轮的 CLR managedAllocatedBytes **没有计入这部分**。

88 个 GameObject 根字段中有 44 个名字长于 15 字节，104 个 Weapon 描述项中有 62 个。独立查找模型合计 192 个键，GCC/libstdc++ 的一次完整遍历实际发生 **106 次查找用分配**，改用透明比较器后为 0。GameObject 正式循环跳过 Browser（短键），模型没有跳过；这不影响上述长键数量。这不是已实测的 Windows 整插件分配总数。

还存在 `WeaponNugget.cpp:676` 的 count 后 Text 内再次 find，以及 Text 按值返回字符串（330 行）。适合与输入属性类型一起成组处理；引用必须在原输入生命周期内有效，不能把 fallback 临时对象的引用返回出去。ObjectFilter 边界当前也用旧 map 类型，迁移时须统一或明确转换，不能只替换一个 typedef 造成隐式重建。

纯查找微模型：20 属性、20,000 次遍历，中位数 117.232→84.103 ms。它只说明查找开销可降低；不能按这个比例推算插件或 MOD 加速。原始分配计数在独立单次遍历采集，计时阶段关闭计数。

## 2. member offset 修正存在乘积复杂度

[GetMemberOffset](../src/Native/Relocation.cpp#L38) 对 block 逐个比较地址并累加逻辑偏移；[FixupMemberOffsets](../src/Native/Relocation.cpp#L141) 对全部 member 重复该过程。设数据块数 B、token 成员数 M，当前查找成本最坏 O(B×M)。两个插件都走这条路径：GameObject 1019 行、WeaponTemplate 462 行。

索引项只需 `{addressStart, addressEnd, serializedBase}`，建立一次后 O(log B) 查询。必须在 owner 分配完成后建立，且保留：四字节对齐后的逻辑 block size、区间边界、零大小块、失败时不修改调用方 offset，以及原 token 前缀基数。这个改法属于插件原生序列化辅助层，不需要改变 Core 调度或资产缓存。

临时模型使用原 Tracker 分配与原 GetMemberOffset，对每条查询比较结果，并检查 owner 外指针失败时保留 offset。下表为五轮中位数，查询按每块 24 个位置×8 次重复，单位 ms：

| B | 原线性查询 | 地址索引查询 | 建索引（另计） |
| ---: | ---: | ---: | ---: |
| 8 | 0.0044 | 0.0050 | 0.0013 |
| 32 | 0.0593 | 0.0276 | 0.0009 |
| 128 | 0.9124 | 0.1482 | 0.0020 |
| 512 | 14.5400 | 0.8263 | 0.0048 |
| 2048 | 230.4120 | 4.3033 | 0.0285 |

小 block 集合索引反而较差；不能全部无条件改为二分。下一步应先对真实插件样本采集 B/M/访问次数与阶段耗时，再定小集合保留线性查询的界线。这里只记录算法模型，未测真实 MOD 的相应阶段占比。

## 3. mask 解析与长枚举表

ModelCondition 有 457 个名字，ObjectStatus 有 223 个；临时探针确认两表严格排序、无重名。原实现每词分配/移动 token，再从表首扫描。固定四词、每轮 100,000 次解析的五轮中位数：ModelCondition 425.611→24.917 ms，ObjectStatus 217.004→22.438 ms。空字符串和 ALL 本来已有快路，不应从这个多词负载外推收益。

二分/string_view 原型与原 helper 比较了 4,143 组 ModelCondition 和 2,037 组 ObjectStatus 掩码：全部名字、三种初始掩码、符号、未知词、大小写、裸正负号、NUL 截止均相同。必须保留原 SDK 的特殊规则：减号用 XOR；ALL 忽略符号并直接结束；ALL 会设置尾部 padding；普通名字仍区分大小写；未知值仍按类型 fallback 处理。不能将未确认排序的通用枚举数组直接改成二分。

WeaponNugget 的普通枚举也线性查询：DamageFXType 114 项、DamageType 39 项、DeathType 34 项，见 333、674 行。这里可与缺省值预解码一起处理，而不是对每资产重建字典。

## 4. 缺省值、子节点扫描、最终缓冲

- GameObject 根 88 字段中有 71 个字符串缺省声明；DurationDefault 缺省已直接使用数值，其余仍走字符串处理，其中 22 个 float、7 个时间、1 个角度。Weapon 有 78 个字符串缺省字段，DamageNugget 有 25 个派生字段。模块字段循环也存在同样成本。预解码必须保存原 CRT/单位缩放、float 舍入、负零及帧时长转换；不能复用带指针、引用索引或可变 mask 的实例。
- `MarshalDrawModelCondition` 有 10 个列表组；列表 helper 总是先计数，非空再遍历写入，另有 Material/ShadowInfo 查找。十组均非空时最多扫描 children **22 遍**。分组后仍须按旧 schema 顺序分配，直接按 XML 顺序写会改变原始输出。对很小节点应避免额外为每组建立 heap vector。
- `MakeRelocatable` 先分配/清零向量，再逐 block 拷贝。两个插件分别产生 token/data 向量，之后 insert 合并，又复制到托管 byte[]。优化应合并最终输出分配与序列化，单纯在 insert 前 reserve 仍会复制原 token 前缀。token fixup 当前以“当时 token 缓冲长度”为基数，合并缓冲后必须显式传入该长度；大小端转换也只能处理 token 区间。

两个廉价清理点也已确认：`MarshalModule` 连续两次 FindModule 可复用结果；Weapon 根循环在排除 13 个 Absent 子节点描述项之前已经执行 attributes.find，可先排除再查找。单独预期收益有限，适合随上述能力组实现。

`Relocation.cpp` 的排序包含原 SDK 对相等键的堆分支顺序，**不建议直接替换为 std::sort**；字节兼容依赖该行为。

## 证据与后续验收

原型在 [probes/TokenizerCosts](../probes/TokenizerCosts/README.md)，原始 CSV、源码哈希、编译器及中位数在 [研究证据](../evidence/refactor/tokenizer-plugins/research/summary.json)。环境为 Linux x86_64、GCC 14.2、C++17、O2；所有算法比值都不是 Windows C++/CLI、完整插件或 MOD 收益。

建议下一批先做属性异构查找与 owner member 索引，再做已排序 mask 表与纯标量缺省值。先以真实插件样本确认分配、B/M 和阶段耗时；成组修改后集中重建与原字节/拒绝/四平台/SDK 回归。若改动共享 Relocation/EnumBitset，还要按依赖闭包覆盖受影响的其它插件，不能只跑两个小微基准。
