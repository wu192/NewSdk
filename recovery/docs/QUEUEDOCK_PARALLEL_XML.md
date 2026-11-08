# QueueDock 容量数据合同

目标为 `feature/queuedock-multi-unload`，集成目标 `feature/integration`，配套
`wu192/EnhancerCorona` 同名分支。实施依据为
[QueueDock 多矿车方案](https://github.com/wu162/ra3idb/blob/main/research/ra3-refinery-unloading/RA3_QueueDock_MultiUnload_Plan.md)。

原 `QueueDockBehavior` 新增 `MaxConcurrentMiners:SageUnsignedInt`，缺省为 `1`。
原 TypeId `0xAF4C1F21`、kind `128`、五个骨骼字符串、动画字段及 XML
`ReadModules` 通路保留。生产 writer 的 ModuleData 为 56 字节：最后一个
四字节无符号值在 `+52`；原继承和七个 token 后追加
`{0x3A9B0156,0x1E0413C5,+52}`，共八个 token。容量无独立数据 relocation。

Core 展开后由原生产 marshaller 严格校验十进制整数 `1..10`；`0`、`11`、
负数、溢出、非法词法均编译失败，不裁剪。`08/09/010` 分别写为
`8/9/10`，不沿用旧通用标量 parser 的八进制解释。该严格校验复用此前
心控字段使用的正整数入口，不改变其他原生数值字段的既有解释。

PC GameObject 处理版本由 `4106` 提升为 `4109`，Xbox360/PS3 同步为
`4110/4111`；内置 lazy compiler 的缓存身份同步。旧缓存须重编。
新 QueueDock 资产必须与配套扩展 parser 一起部署：原版 runtime 的
52 字节分配器不能读取新增字段。配套 parser 把旧 52 字节资产解析为
默认容量 1 的 56 字节运行时对象；资产布局与存档版本是不同合同。

[最小 XML 示例](../../examples/queuedock/QueueDock.xml)含默认、1、2、10 四个
真实 GameObject 节点（含 schema 必需的普通 ActiveBody），供编译/parser 验证；它们没有可玩的矿厂模型、
经济或完整骨骼。实际并行卸矿还依赖运行时 Gate、独立 Context、九处路由、
生命周期和存档接线；仅编译 XML 成功不能证明并行卸矿已工作。

验证入口为现有 `TestEnhancerDozerXml.ps1`，继续使用同一 Win32 `/Od`、
`/O2` 原生对象与 C++/CLI bridge，不增加构建矩阵或工件上传：

- `GameObjectQueueDockNative.cpp` 调用生产 GameObject writer，检查缺省、
  1/2/10、边界拒绝、标量 token 地址、56 字节布局、五个 ReloString、
  relocation 与大小端，生成原生三缓冲 fixture。
- `GameObjectQueueDockSmoke.cs` 编译真实 GameObject XML、检查四平台，
  与上述 native fixture 三缓冲比较，并验证最小 XML 示例和处理缓存版本。
- 原 `GameObjectQueueParkingCases.cs` 的原 DLL 三缓冲比较只移除已验证的
  默认容量 word 和新增 token，并按这两处删除精确重映射所有指针/offset；
  其余原字节、字符串、继承和 ParkingPlace 分支保持比较。新增布局则独立
  检查八 token、原字段偏移及默认容量。

这些是组件验证入口，不是完整 Core/SDK/MOD、原生资源加载、游戏、存档
或多人 CRC 验收。实际通过记录以对应提交的 Actions 终态/Job Summary 为准。

源码 `5f71da84` 的 [Windows 42450345003](https://github.com/wu192/NewSdk/actions/runs/42450345003)
已通过 `/Od` 与 `/O2` 上述全部组件及原有推土机/心控回归，零工件。
[完整组件收据](../evidence/queuedock/42450345003/validation.json)保留逐模式输出、
fixture 身份与首轮 schema fixture 失败；本地 i386/qemu 结果单独记录。
