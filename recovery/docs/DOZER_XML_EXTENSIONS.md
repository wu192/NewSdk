# 原生推土机 XML 与命令配置

`feature/integration` 恢复真正的 `DozerAIUpdateModuleData` 编译路径。
运行时使用 RA3 保留的原生 Dozer parser、模块工厂和状态机。
建筑无需专用节点、模板名单或扩展 token；同一普通 RA3 建筑模板可由原建造流程和推土机引用。

## 配置

```xml
<GameObject xmlns="uri:ea.com:eala:asset" id="ExampleDozer"
  KindOf="VEHICLE DOZER" CommandSet="ExampleDozerCommandSet">
  <AI>
    <DozerAIUpdate id="ModuleTag_DozerAI"
      RepairHealthPercentPerSecond="2.5" BoredTime="3.25" BoredRange="64" />
  </AI>
  <Body>
    <ActiveBody id="ModuleTag_Body" MaxHealth="100" />
  </Body>
</GameObject>
```

工程车命令集中的 `LogicCommand Type="DOZER_CONSTRUCT"` 指向目标建筑的
`GameObject`，并配置对应按钮。运行时从工程车实例的当前命令集判断建造资格，
切换命令集即时生效。目标必须为 `STRUCTURE`；科技、资金、数量与放置限制
继续由原生建造流程处理。不要把 `UNIT_BUILD` 或建造场命令当作推土机命令。
完整命令及按钮示例见
[EnhancerCorona examples/generals-dozer](https://github.com/wu192/EnhancerCorona/tree/feature/integration/examples/generals-dozer)。

三个 Dozer 属性都是原生 `SageReal`，默认 `0`，按原始浮点值写入；
`RepairHealthPercentPerSecond` 不经过百分数除以 100，`BoredTime` 不使用
`Time` 的单位转换。AIUpdate 原有属性与 `UnitAITargetChooserData` 子节点继续有效。
`TypeId` 由标准 XML/Core 管线生成，MOD XML 无须手写。
XML 继承继续由标准 Core 预处理负责。

保留运行时全局开关 `enableNativeDozer`。建造、续建、修理、Stop 与取消通过
游戏 UI、同步命令和原生 Dozer 行为完成，无专用地图 Lua API。
工厂保留正常 `ProductionUpdate`；必要的施工协调按工地实例来源进行，不能按模板截获所有建筑。

## 原生二进制合同

Dozer 类型 ID 为 `0x8D093323`；继承 token 为
`(1, 0x08DBC00E, AIUpdate 子表偏移)`。原生数据大小 `0x58`，前 `0x4C`
字节沿用 AIUpdate；三个浮点字段位于 `0x4C/0x50/0x54`，字段 token ID
分别为 `0x8229777E/0x18C1A7F3/0xC492A6B5`，标量类型均为 `0x645864F3`。

来源是反编译基线 `BinaryAssetBuilder.Tokenizer/-Module-.cs` 中的
`Tokenize(DozerAIUpdateModuleData)` 与多态 token 分派。原 DLL 没有对应 XML
Marshal 入口，因此不能把原 DLL 的 Dozer XML 输出作为该路径的差分 oracle。

GameObject 保留原生 592 字节序列化布局及固定 127 项根表分配（126 项和结束项）；
没有附加 bool 数据、动态根 token 数或解析侧表协议。未使用 Dozer 的原有资产
继续沿用原序列化路径。

## 定向验证

Windows x86 MSVC Developer Prompt：

```powershell
./recovery/scripts/TestEnhancerDozerXml.ps1 -OutputDirectory "$env:TEMP/native-dozer"
```

脚本集中构建 `/Od`、`/O2` 的真实生产序列化器和 C++/CLI XML bridge。
`GameObjectDozerNative.cpp` 检查原生大小、AI 父层、三个原始浮点数及默认值、
继承字符串/chooser 分配、原根表和大小端，输出
`dozer-{parameters,defaults}.{le,be}.bin/.relo`。
`GameObjectDozerSmoke.cs` 覆盖四平台 `0/1/2/99`、原生参数与继承 chooser、
无需标记的普通建筑，以及已移除节点的 schema 拒绝。

具备 multilib 的 Linux 可使用真实 32 位 ABI：

```sh
g++ -m32 -std=c++17 -O2 -Irecovery/tests/native-host -Irecovery/src/Native -o GameObjectDozerNative recovery/tests/GameObjectDozerNative.cpp recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/DataInitializers.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/ObjectFilter.cpp recovery/src/Native/WeaponSlotOrder.cpp recovery/src/Native/GameObject.cpp
./GameObjectDozerNative
```

Linux 测试头只替代顺序执行的 Windows InitOnce，不加入 Windows 生产编译。
组件工作流将来源、耗时、fixture 大小与摘要收据写入 Actions 日志和 Job Summary；
临时 fixture 仅留在 runner，不上传任何工件或缓存，不占用 Actions 工件额度。
历史扩展协议收据保留在 git 及 `recovery/evidence/enhancer-xml`，不能作为本次修改的成功证据。

组件测试不代替完整 SDK/MOD、Enhancer DLL 或游戏内验收。特别是鼠标交互、
普通建筑/工厂兼容、工程车死亡后接续、存档载入和多人同步须在游戏中分别验证。

Windows 新验收：[`42151031943`](https://github.com/wu162/NewSdk/actions/runs/42151031943)，
源码 `a44f5836`，上述 `/Od`、`/O2` Native/bridge 全通过；
[逐模式来源、耗时与 fixture 摘要](../evidence/native-dozer/42151031943/validation.json)。
目标仓库只读，验证分支位于 `wu162/NewSdk:codex/native-dozer-commandset`，未合入目标分支。
