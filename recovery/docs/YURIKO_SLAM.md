# 百合子拾取与投掷编译支持

基于 `compiler-main/f0adccd6`，配套 EnhancerCorona `feature/yuriko-slam`。
技能包含三个协作模块：

- `AddObjectsToLiftUpdateSpecialPower`：继承 StoreObjectsSpecialPower，用原有 `TeleportLinkID` 标识被拾取对象集合，`LiftObjectLinkID` 指向悬浮模块。
- `FlingStoredObjectsSpecialPower`：继承 SpecialPower，`StoreObjectsLinkID` 选择前述集合，`LiftObjectLinkID` 选择悬浮模块；`FlingSpeedMax/Min` 默认 300/100。
- `YurikoLiftObjectUpdate`：继承原有 LiftObjectUpdate 并增加 `LiftObjectLinkID`，沿用 LiftVelocity、MaxElevationFromGround、武器和时间等字段。

原有 `LiftObjectUpdate` 数据仍为 40 字节，不给旧资产添加 token 或填充字节。新增的派生包装类型为 `0xAA9E10D6`，完整 44 字节，父 token 类型为原始 `0xBF91BFCD`。这是 RA3 移植用的名称，不能声称 Uprising 原模块叫 YurikoLiftObjectUpdate。

```xml
<YurikoLiftObjectUpdate id="Lift" LiftObjectLinkID="7" LiftVelocity="8"
    MaxElevationFromGround="80" MaxTimeLifted="30s"/>
<AddObjectsToLiftUpdateSpecialPower id="Pickup" SpecialPowerTemplate="YurikoPickup"
    TeleportLinkID="4" LiftObjectLinkID="7" Radius="100"/>
<FlingStoredObjectsSpecialPower id="Throw" SpecialPowerTemplate="YurikoThrow"
    StoreObjectsLinkID="4" LiftObjectLinkID="7" FlingSpeedMin="100" FlingSpeedMax="300"/>
```

依据为 ra3idb 的 Uprising 1.0 `.game`，SHA256 `19b3542a7d2f7a050b16b2ea5e4ba0231b13c3fb7fcdb2c3e89767d4d354430b`。以下 VA 基于 `0x400000`：AddLift parser `0x4AE1B0` 经指针 `0x0186F2CC` 转到 `0x026C0000` 的可读代码；类型 `0xD9B247DF` 继承 StoreObjects 类型 `0xA74A234C`，自身字段 `0xDDCCDDF3/0x23E650B8` 是已散列核实的 LiftObjectLinkID。Fling parser `0x4AE440`、类型 `0x12D1FAD9` 继承 SpecialPower。StoreObjectsLinkID 的 token `0xFEE927FC` 由比较器 `0x42355C` 的原始常数核实；其后两个浮点 token `0x7B773FB9` 和 `0x80503B2D` 对应默认 300/100，公开速度名为语义别名。

本分支没有暴露原 Fling 的可选对象替换表（token `0x891C564B/0xEE9C04C0`），保留空表数据；替换目标为另一种对象的机制尚未移植。当前运行时范围以配套分支文档为准，不能以此宣称完整复现所有战役道具。

本地实际 32 位 C++ tokenizer 的 LE/BE 用例通过，覆盖拾取、存储、悬浮链接、原生速度默认值及父类 LiftVelocity；宿主为 GCC14/QEMU i386，Windows InitOnce 由宿主接口替代。Actions 继续验证 Win32 Debug/Release 与无 TypeId 注入的 XSD 路由。真实 SDK 四流、完整 MOD 和实机投掷尚未验证。
