# 百合子拾取与投掷编译支持

基于 `compiler-main/f0adccd6`，配套 EnhancerCorona `feature/yuriko-slam`。
技能包含拾取、抛出、悬浮与碰撞伤害四个模块：

- `AddObjectsToLiftUpdateSpecialPower`：继承 StoreObjectsSpecialPower，用原有 `TeleportLinkID` 标识被拾取对象集合，`LiftObjectLinkID` 指向悬浮模块。
- `FlingStoredObjectsSpecialPower`：继承 SpecialPower，`StoreObjectsLinkID` 选择前述集合，`LiftObjectLinkID` 选择悬浮模块；`FlingSpeedMax/Min` 默认 300/100。可选的重复 `ObjectReplacement Source/Replacement` 按原模板选择含 `DamageDynamicsCollide` 的逻辑投掷代理；代理必须移除继承的 `Physics` / `DynamicsUpdate`，由逻辑飞行持有位置。普通 `Physics` 步兵也能作为受击目标，无需给受击方添加 Dynamics。
- `YurikoLiftObjectUpdate`：继承原有 LiftObjectUpdate 并增加 `LiftObjectLinkID`，沿用 LiftVelocity、MaxElevationFromGround、武器和时间等字段。
- `DamageDynamicsCollide`：以固定 30Hz 逻辑帧推进飞行，复用原生 PartitionManager 的 `SPHERE` 查询及逻辑包围球做弦段命中判定；同帧命中按最早接触比例、ObjectID 稳定排序。`FullDamageSpeed` 默认 10；伤害消费逻辑飞行速度（世界单位/秒），上限为配置的 `DamageNugget.Damage`，不读取 Havok 接触、刚体速度或渲染 LOD。其重复 `DamageNugget` 保留完整 DamageNuggetType 的编译布局；运行时消费接触伤害字段及 `OnlyKillOwnerWhenTriggered`，不执行武器的半径伤害流程。

原有 `LiftObjectUpdate` 数据仍为 40 字节，不给旧资产添加 token 或填充字节。新增的派生包装类型为 `0xAA9E10D6`，完整 44 字节，父 token 类型为原始 `0xBF91BFCD`。这是 RA3 移植用的名称，不能声称 Uprising 原模块叫 YurikoLiftObjectUpdate。

```xml
<YurikoLiftObjectUpdate id="Lift" LiftObjectLinkID="7" LiftVelocity="8"
    MaxElevationFromGround="80" MaxTimeLifted="30s"/>
<AddObjectsToLiftUpdateSpecialPower id="Pickup" SpecialPowerTemplate="YurikoPickup"
    TeleportLinkID="4" LiftObjectLinkID="7" Radius="100"/>
<FlingStoredObjectsSpecialPower id="Throw" SpecialPowerTemplate="YurikoThrow"
    StoreObjectsLinkID="4" LiftObjectLinkID="7" FlingSpeedMin="100" FlingSpeedMax="300">
  <ObjectReplacement Source="SovietAntiInfantryInfantry" Replacement="FlingConscriptProxy"/>
  <ObjectReplacement Source="CivilianBarrel" Replacement="FlingBarrelProxy"/>
</FlingStoredObjectsSpecialPower>
<!-- Add this module to a logical proxy; remove inherited Physics/DynamicsUpdate. -->
<DamageDynamicsCollide id="ThrownCollision" FullDamageSpeed="10">
  <DamageNugget Damage="250" DamageType="CRUSH" DeathType="NORMAL"/>
  <DamageNugget OnlyKillOwnerWhenTriggered="true" DeathType="NORMAL"/>
</DamageDynamicsCollide>
```

依据为 ra3idb 的 Uprising 1.0 `.game`，SHA256 `19b3542a7d2f7a050b16b2ea5e4ba0231b13c3fb7fcdb2c3e89767d4d354430b`。以下 VA 基于 `0x400000`：AddLift parser `0x4AE1B0` 经指针 `0x0186F2CC` 转到 `0x026C0000` 的可读代码；类型 `0xD9B247DF` 继承 StoreObjects 类型 `0xA74A234C`，自身字段 `0xDDCCDDF3/0x23E650B8` 是已散列核实的 LiftObjectLinkID。Fling parser `0x4AE440`、类型 `0x12D1FAD9` 继承 SpecialPower。StoreObjectsLinkID 的 token `0xFEE927FC` 由比较器 `0x42355C` 的原始常数核实；其后两个浮点 token `0x7B773FB9` 和 `0x80503B2D` 对应默认 300/100，公开速度名为语义别名。

替换表保留原 Fling 外层 token `0x891C564B/0xEE9C04C0`，在 RA3 数据偏移 468/472 保存数量和指针，每条 8 字节，源模板弱 ID 在 +0，替换代理模板弱 ID 在 +4。该布局和源/目标用途由 Uprising `0x6FC1E0` 的查询代码核实。原条目 parser `0x42D0B0` 经 `[0x0186A824]` 进入受保护代码 `0x01906CD0`，因此不声称恢复原条目字段名。公开 `Source`/`Replacement` 及其 token `0xD4EDC301`/`0xB955F215` 是明确的 RA3 移植协议，类型均为 GameObjectWeakRef `0x148ED75D`。配套运行时按此协议读取，不调用未知的原始条目 parser。

代理模板由 MOD 提供，必须含 `DamageDynamicsCollide` 与显示定义，并显式移除继承的 `Physics` / `DynamicsUpdate`，避免旧运动模块驱动逻辑位置；无需接触伤害时可保留空伤害列表以使用逻辑飞行。上面两个代理名仅为示例引用，不代表 SDK 已附带资源模型；[配套运行时完整示例](https://github.com/wu192/EnhancerCorona/blob/feature/yuriko-slam/examples/yuriko-slam/YurikoSlam.xml) 给出普通维和步兵、多功能步兵车的映射与模块移除方式。

未被本模块持有逻辑位置的原生 `Motion=SIMULATED` 源仍走原表现发射，不能直接获得新增逻辑伤害；这类目标也从逻辑伤害候选中排除。需要新增玩法命中时应映射到无旧运动模块的逻辑代理。未配置本模块的旧 Dynamics/Drawable 发射继续提供原有表现。飞行、落地后的逻辑位置不交还旧刚体；替换资格、模型状态复制和生命周期范围见[运行时说明](https://github.com/wu192/EnhancerCorona/blob/feature/yuriko-slam/docs/YurikoSlam.md)。

运行时存档 v2 保存起点、初速度和两个逻辑帧，并保留原生调度截止帧；只在完整逻辑步之间允许保存，位移或伤害回调内的重入保存明确报 Xfer 错误。旧 v1 活动存档因缺少轨迹明确拒绝，未活动旧档可读。此处运行时修订不改变 20 字节模块资产或 152 字节内联 DamageNugget 协议。

本地实际 32 位 C++ tokenizer 的 LE/BE 用例通过，覆盖拾取、存储、悬浮链接、原生速度默认值、三条源/代理映射、空表、父类 LiftVelocity，以及完整内联 DamageNugget；宿主为 GCC14/QEMU i386，Windows InitOnce 由宿主接口替代。Win32 Debug/Release 与无 TypeId 注入的四模块 XSD 路由也已通过。真实 SDK 四流、完整 MOD 和实机投掷尚未验证。

旧资产兼容检查：使用同一实际 32 位宿主分别编译 `f0adccd6` 和本分支，根对象、原 ShieldSphereUpdate、原 LiftObjectUpdate、原 StoreObjectsSpecialPower 四组输入各测 LE/BE，instance/relocation/import 三缓冲合计 23296 字节完全一致，SHA256 `4b80a8cf006d046d04c624af7431130c7522c89447589c16409ff7e28b305be3`。该检查覆盖这八组旧资产，不等于完整 SDK 差分。

Windows 验证已通过：[定向运行 42829667772](https://github.com/wu192/NewSdk/actions/runs/42829667772)，源码 `b4149eb564a5a7d2a6df0b0710992d19e99b867a`，Win32 Debug/Release 均完成包含 DamageDynamicsCollide 的实际 native tokenizer 与四模块公开 XML/XSD 路由检查。该结果不包含完整 SDK 四流或游戏执行。

## 模块数据与原生来源

Uprising 的 `DamageDynamicsCollide` 是独立的新模块，不等同于 AudioDynamics。构造器 `0x718120`、名称 `0x718160`、碰撞回调 `0x7E8CB0`、数据 parser `0x499D60` 确认类型 `0x19BF3331`、20 字节数据：完整伤害速度标量在 +8，伤害条目数量/指针在 +12/+16。父类型 `0xF1A07B36` 是 DynamicsCollideModuleData；其 parser `0x432EA0` 直接继承 BehaviorModuleData，不能添加旧 CollideModuleData 的额外嵌套层。

原生字段 token 为 `0x00F63CA4/0x645864F3`（默认 10）和 `0xF4948415/0xC0F53E99`（准确对应 DamageNugget/DamageNuggetType）。`FullDamageSpeed` 是本移植的语义别名，原始字段拼写未恢复。伤害比例由 `0x7E8DFC..0x7E8E22` 的 `Damage * min(collisionSpeed, FullDamageSpeed) / FullDamageSpeed` 得出。这些构造、解析器及碰撞回调地址属于 Uprising 的原生来源证据，不是新版 RA3 的执行路径；新版 RA3 按上述逻辑命中合同，以逻辑接触速度计算 `Damage / FullDamageSpeed * min(logicalSpeed, FullDamageSpeed)`。

每条伤害数据是直接内联的完整 RA3 DamageNuggetType（152 字节），没有武器列表的 `{2, type, table}` 多态包装。Uprising 条目为 156 字节，多出的 4 字节来自第八个 InvalidTargetStatus 状态字。编译器复用 WeaponNugget.cpp 的完整现有编组、token 和大小端转换，不复制或缩减伤害字段；固定大小数组不接受 DOT 等扩展伤害类型。标准武器列表的数据结构和原字节保留。

新增组件用例检查两个内联条目、152 字节步长、原生速度默认值/覆盖、嵌套过滤器与 DamageScalarDetails、升级弱引用、EVA/揭雾对象强引用和自毁标志，LE/BE 均通过。修改后的编译器另与 `f0adccd6` 做六组原 Weapon 输入（含嵌套伤害标量）的三缓冲对照：13304 字节完全一致，SHA256 `25f056a36fcc735d70a7a9569ac9699594748df60dfe8d1f14bc173bbc080833`；此前八组 GameObject 仍为 23296 字节完全一致。以上为定向组件证据，不是 SDK 四流或实机结论。

七个新模块的实际 SDK token 缓冲已由配套运行时当前 parser 消费；验证包括新增碰撞父类层级、152 字节内联伤害、最终分配内的强引用修正地址及释放所有权。运行时改用原生字段 parser 直接写最终分配，避免临时父数据或条目复制后留下失效修正地址。[可复跑的跨仓审计](YURIKO_WIRE_AUDIT.md) 保存输入、命令与替代接口范围；不修改现有 CI，也不重复未变的完整 SDK 验证。
