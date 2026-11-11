# 百合子护盾编译支持

分支基于 `compiler-main` 的 `f0adccd6`，配套 EnhancerCorona `feature/yuriko-shield`。
新增 `YurikoShieldSphereUpdate`，继承现有 `ShieldSphereUpdate`，因此半径、持续时间、过滤器、激活状态和模型条件沿用原字段。

新增 `ReflectionFX`、`AlternateReflectionFX` 和 `AlternateReflectionDamageTypes`。前两个为 FXList 引用，后者为 DamageBitFlags；普通反弹使用前者，所列伤害类型使用替代 FX。这三个公开字段名是语义别名，原始 XML 名称未恢复。

本轮追加 `InterceptedProjectileObjectStatus`，公开类型 `ObjectStatusBitFlags`，默认空。它对应起义时刻父 parser `0x4754A2..0x4754B9` 的 `0x91F93DE7/0xA7BD811E` 字段，在护盾接受投射物后、反射前设置到该弹丸；原名未恢复，使用上述语义别名。RA3 的父类没有这个字段，不能复用其 `ObjectStatus`（拥有者激活状态）或照搬起义时刻的8字状态数组；本字段适配为RA3的7个DWORD，索引225不进入此资源数组，活动护盾状态由配套运行时生命周期索引实现。

依据为 `wu162/ra3idb/ra3ep1_1.0.zip` 中 Uprising 1.0 `.game`，SHA256 `19b3542a7d2f7a050b16b2ea5e4ba0231b13c3fb7fcdb2c3e89767d4d354430b`；以下均为 image base `0x400000` 的 VA。输入 parser `0x475560` 的类型 `0x95D855B6`，父类型 `0x6C11B6F7`，三个字段 token 为 `0x945463F7/0x0D1C8E83`、`0x92285C9F/0x0D1C8E83`、`0xD9086106/0x07FAAEA4`。`0x0D1C8E83` 已核实为 FXListRef，不能当成 WeaponRef。运行时反弹另走原武器发射链。

编译器保留原三个字段的偏移264/268/272，在尾部偏移280追加28字节的弹丸状态掩码，完整数据大小由280增至308（`0x134`）字节；父类仍为RA3原有264字节。新token放入派生类型表，配套 EnhancerCorona parser读取相同布局，旧流缺少该token时默认为全0。普通 `ShieldSphereUpdate` 及其他未使用扩展模块的布局不变；新字段未设置的百合子模块仍使用新的token表，不能把它宣称为旧百合子流逐字节一致。原 Uprising 父结构更大，不能直接复制其偏移到 RA3。

例如 `<YurikoShieldSphereUpdate id="Shield" InterceptedProjectileObjectStatus=""><ObjectFilter Rule="ANY" Include="INFANTRY"/></YurikoShieldSphereUpdate>` 保持空掩码。只有需要拦截后附加状态的资源才填写RA3已有的状态名；不得填写起义时刻状态225。标准示例和玩法参数见配套 EnhancerCorona 的 `examples/yuriko-shield`。

本轮实际32位 C++ tokenizer（GCC14、QEMU i386，仅替代 Windows InitOnce 宿主接口）验证了LE/BE生成的原生token/type、31/32跨DWORD和最后合法枚举位222、默认全0、原有引用/伤害掩码/父字段，以及通用RA3状态writer填充7字时不触碰第8字。公开XSD路由在.NET8宿主通过（临时resolver仅适配Linux路径大小写和反斜杠），并拒绝225；Windows Debug/Release的原脚本仍须由本分支Actions验证。未执行本轮完整 SDK 四流、完整 MOD 或游戏内验证。

旧资产兼容检查：使用同一实际 32 位宿主分别编译 `f0adccd6` 和本分支，根对象、原 ShieldSphereUpdate、原 LiftObjectUpdate、原 StoreObjectsSpecialPower 四组输入各测 LE/BE，instance/relocation/import 三缓冲合计 23296 字节完全一致，SHA256 `4b80a8cf006d046d04c624af7431130c7522c89447589c16409ff7e28b305be3`。该检查覆盖这八组旧资产，不等于完整 SDK 差分。

本次追加字段另与本轮基线 `bfea136c6d64a7d02aed3ac44363137de99ff75e` 对比：同一实际32位宿主执行根对象、原ShieldSphereUpdate、原LiftObjectUpdate、原StoreObjectsSpecialPower各LE/BE，8组的instance/relocations/imports全部逐字节一致。按每缓冲长度加内容串联的收据为23296字节，SHA256 `e9379a0164532e0c2c6e59bf20bf0e0b625e71333c54180610bf19300c2a19eb`；这是本轮组件范围，未替代完整SDK差分。

历史Windows验证：[定向运行 42787855476](https://github.com/wu192/NewSdk/actions/runs/42787855476)，源码 `41c33abc9493ebd41b145021a6d43a24d1676cd4`，Win32 Debug/Release 均完成实际 native tokenizer 与公开 XML/XSD 路由检查。它不包含本次追加状态字段，亦不包含完整 SDK 四流或游戏执行。

本轮Windows验证已通过：[运行43020213993](https://github.com/wu192/NewSdk/actions/runs/43020213993)，生产源码 `6e95b347ae9cdedfa2d3d34d48a8443c7e8f7800`，Win32 Debug/Release均完成真实native tokenizer LE/BE和公开XSD路由。相同生产源码的PR事件运行43020221975也通过。

配套跨仓检查将本轮真实i386 tokenizer输出直接交给 EnhancerCorona `56d1aa61b2f0183dd363f9ae18d2a2f07f62756a` 的生产 `parseDerived`，已验证FX引用、伤害掩码、状态位31/32/222、默认值、旧token缺省、父数据保留和末尾哨兵。该组件检查不代替完整SDK四流、MOD或游戏验收。
