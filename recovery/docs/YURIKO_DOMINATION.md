# 百合子魅惑编译支持

基于 `compiler-main/f0adccd6`，配套 EnhancerCorona `feature/yuriko-domination`。
技能对应 `LureObjectsUpdate` 和 `AddObjectsToLureUpdateSpecialPower`；控制后的跟随守卫与普通永久换主心控不同。

`LureObjectsUpdate` 提供 `LureObjectLinkID`、`MaxRangeSquared`、`GuardRange`、`GuardStatus`、`UpdateRate`、`TargetMarkerObjectRef`、`DisabledTypesToProcess`，以及重复的 `GuardOffset x/y/z`。`GuardOffset` 的数量就是容量，不能另造一个 `MaxObjects` 标量替代。配套运行时按来源朝向旋转 `GuardOffset`，把结果写入原生守卫中心；来源只转向时也会触发归位。`GuardRange` 限制来源到攻击目标的 XY 距离，同时检查新目标和已保留的射击目标，越界后由原生状态图返回编队。跟随速度使用 `max(目标基础最大速度, 来源基础最大速度 + 1)`；解除关系后恢复原路径。`MaxRangeSquared` 按距离平方填写；原生默认值 1000000。`UpdateRate` 是逻辑帧间隔，默认 10。

`AddObjectsToLureUpdateSpecialPower` 继承 `StoreObjectsSpecialPower`，沿用 `SpecialPowerTemplate`、`Radius`、过滤器等字段。自身的 `LureObjectLinkID` 选择同对象的 `LureObjectsUpdate.LureObjectLinkID`，继承的 `TeleportLinkID` 保留独立存储集合标识，复用原生 SpecialPower 冷却、存储和生命周期。目的链接为 0 时选择首个 Lure 模块；0 是本移植的显式默认值，原生缺省值未恢复。

```xml
<LureObjectsUpdate id="Lure" LureObjectLinkID="7" UpdateRate="10"
    MaxRangeSquared="1000000" GuardRange="200" GuardStatus="UNSELECTABLE">
  <GuardOffset x="-30" y="0" z="0"/>
  <GuardOffset x="30" y="0" z="0"/>
  <GuardOffset x="0" y="-30" z="0"/>
</LureObjectsUpdate>
<AddObjectsToLureUpdateSpecialPower id="Dominate" SpecialPowerTemplate="YurikoDomination"
    TeleportLinkID="4" LureObjectLinkID="7" Radius="150"/>
```

依据为 ra3idb 的 Uprising 1.0 `.game`（SHA256 `19b3542a7d2f7a050b16b2ea5e4ba0231b13c3fb7fcdb2c3e89767d4d354430b`）。VA 基于 `0x400000`：Lure 数据 parser `0x42EB80`、类型 `0xB65AB72A`；AddLure 类型 `0xA7DEFFCC`。`LureObjectLinkID`、`GuardOffset`、`GuardStatus`、`UpdateRate`、`DisabledTypesToProcess` 已由字段名散列与二进制 token 对上；另外三个公开名是语义别名。范围默认 1000000、守卫距离默认 99999、间隔默认 10 来自 parser 的赋值。RA3 七字对象状态掩码令尾部偏移比起义时刻少 4 字节，完整数据 68 字节。表中 token 与 Vector3 列表结构保留原生协议。

本地实际 32 位 C++ tokenizer 的 LE/BE 用例通过，覆盖默认值、容量、向量列表、弱引用、父类 ID、独立的 Store 集合 4/目的 Lure 链接 7，以及省略目的链接的移植默认值 0；宿主为 GCC14/QEMU i386，Windows InitOnce 由宿主接口替代。分支 Actions 进一步构建 Win32 Debug/Release，并从无 TypeId 注入的 XML 验证 XSD 路由。SDK 四流和游戏内行为尚未验证，不能把组件测试当作实机结论。

旧资产兼容检查：使用同一实际 32 位宿主分别编译 `f0adccd6` 和本分支，根对象、原 ShieldSphereUpdate、原 LiftObjectUpdate、原 StoreObjectsSpecialPower 四组输入各测 LE/BE，instance/relocation/import 三缓冲合计 23296 字节完全一致，SHA256 `4b80a8cf006d046d04c624af7431130c7522c89447589c16409ff7e28b305be3`。该检查覆盖这八组旧资产，不等于完整 SDK 差分。

此前组件 Windows 验证：[定向运行 42787898814](https://github.com/wu192/NewSdk/actions/runs/42787898814)，源码 `8e4d10932dd843d26a97ccc38928f35521b875d4`，Win32 Debug/Release 均完成实际 native tokenizer 与公开 XML/XSD 路由检查。该结果不包含此次独立目的链接；新的 Win32/XSD 结果另行记录，也不包含完整 SDK 四流或游戏执行。

配套运行时补齐了旋转队形、守卫范围和强制跟随速度的内部实现；这些改动沿用上述字段及 68 字节协议，没有新增 XML 参数。最终运行时 Win32 验证状态见 [EnhancerCorona 魅惑说明](https://github.com/wu192/EnhancerCorona/blob/feature/yuriko-domination/docs/YurikoDomination.md)，不把先前编译器组件结果扩大为运行时或实机通过。

原生 AddLure 回调 `0x7FA760` 读取起义数据 +0x1EC 的独立目的链接，查找函数 `0x6E9DF0` 对 0 选择首个模块、对非零比较 Lure 数据 +8。RA3 的原生 Store 前缀为 468 字节，AddLure 增加一个有符号链接至 +468，完整数据 472 字节。原字段 parser `0x4AE230` 和构造器 `0x43B8A0` 进入保护代码；`AddObjectsToLureUpdateSpecialPower.LureObjectLinkID`、token `0x16E57839/0x23E650B8` 及缺省 0 是明确的移植协议，不声称恢复了原生 AddLure 字段拼写或缺省值。原生 LureUpdate 同名字段散列已经核实。
