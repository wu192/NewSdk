# 百合子拾取与投掷编译支持

基于 `compiler-main/f0adccd6`，配套 EnhancerCorona `feature/yuriko-slam`。
技能包含三个协作模块：

- `AddObjectsToLiftUpdateSpecialPower`：继承 StoreObjectsSpecialPower，用原有 `TeleportLinkID` 标识被拾取对象集合，`LiftObjectLinkID` 指向悬浮模块。
- `FlingStoredObjectsSpecialPower`：继承 SpecialPower，`StoreObjectsLinkID` 选择前述集合，`LiftObjectLinkID` 选择悬浮模块；`FlingSpeedMax/Min` 默认 300/100。可选的重复 `ObjectReplacement Source/Replacement` 按原模板选择带物理模块的投掷代理，使缺少 Dynamics 的步兵、道具也能进入投掷路径。
- `YurikoLiftObjectUpdate`：继承原有 LiftObjectUpdate 并增加 `LiftObjectLinkID`，沿用 LiftVelocity、MaxElevationFromGround、武器和时间等字段。

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
```

依据为 ra3idb 的 Uprising 1.0 `.game`，SHA256 `19b3542a7d2f7a050b16b2ea5e4ba0231b13c3fb7fcdb2c3e89767d4d354430b`。以下 VA 基于 `0x400000`：AddLift parser `0x4AE1B0` 经指针 `0x0186F2CC` 转到 `0x026C0000` 的可读代码；类型 `0xD9B247DF` 继承 StoreObjects 类型 `0xA74A234C`，自身字段 `0xDDCCDDF3/0x23E650B8` 是已散列核实的 LiftObjectLinkID。Fling parser `0x4AE440`、类型 `0x12D1FAD9` 继承 SpecialPower。StoreObjectsLinkID 的 token `0xFEE927FC` 由比较器 `0x42355C` 的原始常数核实；其后两个浮点 token `0x7B773FB9` 和 `0x80503B2D` 对应默认 300/100，公开速度名为语义别名。

替换表保留原 Fling 外层 token `0x891C564B/0xEE9C04C0`，在 RA3 数据偏移 468/472 保存数量和指针，每条 8 字节，源模板弱 ID 在 +0，物理代理模板弱 ID 在 +4。该布局和源/目标用途由 Uprising `0x6FC1E0` 的查询代码核实。原条目 parser `0x42D0B0` 经 `[0x0186A824]` 进入受保护代码 `0x01906CD0`，因此不声称恢复原条目字段名。公开 `Source`/`Replacement` 及其 token `0xD4EDC301`/`0xB955F215` 是明确的 RA3 移植协议，类型均为 GameObjectWeakRef `0x148ED75D`。配套运行时按此协议读取，不调用未知的原始条目 parser。

代理模板由 MOD 提供，必须带所需的 Dynamics/物理模块和显示定义；上面两个代理名是示例引用，不代表 SDK 已附带资源模型。无映射对象沿用既有物理路径，替换时的运行时资格和生命周期范围以配套分支文档为准。

本地实际 32 位 C++ tokenizer 的 LE/BE 用例通过，覆盖拾取、存储、悬浮链接、原生速度默认值、三条源/代理映射、空表及父类 LiftVelocity；宿主为 GCC14/QEMU i386，Windows InitOnce 由宿主接口替代。Actions 继续验证 Win32 Debug/Release 与无 TypeId 注入的 XSD 路由。真实 SDK 四流、完整 MOD 和实机投掷尚未验证。

旧资产兼容检查：使用同一实际 32 位宿主分别编译 `f0adccd6` 和本分支，根对象、原 ShieldSphereUpdate、原 LiftObjectUpdate、原 StoreObjectsSpecialPower 四组输入各测 LE/BE，instance/relocation/import 三缓冲合计 23296 字节完全一致，SHA256 `4b80a8cf006d046d04c624af7431130c7522c89447589c16409ff7e28b305be3`。该检查覆盖这八组旧资产，不等于完整 SDK 差分。

Windows 验证已通过：[定向运行 42789466290](https://github.com/wu192/NewSdk/actions/runs/42789466290)，源码 `fb2843a00ed8d3ff79ecaca04627c0aa91444232`，Win32 Debug/Release 均完成实际 native tokenizer 与公开 XML/XSD 路由检查。该结果不包含完整 SDK 四流或游戏执行。
