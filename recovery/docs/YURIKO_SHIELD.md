# 百合子护盾编译支持

分支基于 `compiler-main` 的 `f0adccd6`，配套 EnhancerCorona `feature/yuriko-shield`。
新增 `YurikoShieldSphereUpdate`，继承现有 `ShieldSphereUpdate`，因此半径、持续时间、过滤器、激活状态和模型条件沿用原字段。

新增 `ReflectionFX`、`AlternateReflectionFX` 和 `AlternateReflectionDamageTypes`。前两个为 FXList 引用，后者为 DamageBitFlags；普通反弹使用前者，所列伤害类型使用替代 FX。这三个公开字段名是语义别名，原始 XML 名称未恢复。

依据为 `wu162/ra3idb/ra3ep1_1.0.zip` 中 Uprising 1.0 `.game`，SHA256 `19b3542a7d2f7a050b16b2ea5e4ba0231b13c3fb7fcdb2c3e89767d4d354430b`；以下均为 image base `0x400000` 的 VA。输入 parser `0x475560` 的类型 `0x95D855B6`，父类型 `0x6C11B6F7`，三个字段 token 为 `0x945463F7/0x0D1C8E83`、`0x92285C9F/0x0D1C8E83`、`0xD9086106/0x07FAAEA4`。`0x0D1C8E83` 已核实为 FXListRef，不能当成 WeaponRef。运行时反弹另走原武器发射链。

编译器保留上述输入 token，在 RA3 的 264 字节父数据之后放入两个引用和 8 字节伤害掩码，完整 280 字节；没有改变已有模块的布局。原 Uprising 父结构更大，不能直接复制其偏移到 RA3。

当前本地使用实际 32 位 C++ tokenizer（GCC14、QEMU i386，仅替代 Windows InitOnce 宿主接口）检查了基本编译与执行。分支 Windows 工作流进一步检查 Debug/Release、LE/BE、引用、掩码、父字段及没有注入 TypeId 的 XSD 路由。工作流执行结果以 Actions 为准。本轮尚未完成真实 SDK 四流、完整 MOD 或游戏内验证。
