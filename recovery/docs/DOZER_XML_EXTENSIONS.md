# 原生推土机 XML 与 Enhancer 侧表协议

`feature/integration` 在 `compiler-main` 上恢复真正的
`DozerAIUpdateModuleData` 编译路径。运行时直接使用 RA3 保留的原生 Dozer
parser、模块工厂与状态机，不需要按对象 JSON 或把普通 AIUpdate 数据改造为推土机。

## XML

```xml
<GameObject xmlns="uri:ea.com:eala:asset" id="ExampleDozer">
  <AI>
    <DozerAIUpdate id="ModuleTag_DozerAI"
      RepairHealthPercentPerSecond="2.5"
      BoredTime="3.25"
      BoredRange="64" />
  </AI>
  <Body>
    <ActiveBody id="ModuleTag_Body" MaxHealth="100" />
  </Body>
</GameObject>
```

三个新增属性都是原生 `SageReal`，默认 `0`，按原始浮点值写入；
`RepairHealthPercentPerSecond` 不经过编译器百分数除以 100，`BoredTime`
也不使用 `Time` 的单位转换。AIUpdate 原有属性与
`UnitAITargetChooserData` 子节点仍可使用。`TypeId` 由正常 XML/Core 管线生成，
MOD XML 不必手写。

需要使用推土机建造流程的建筑，在其 `GameObject` 普通子节点之后添加：

```xml
<EnhancerSettings DozerConstruction="true" />
```

该直属节点最多一个；属性默认 `false`。省略整个节点不生成扩展 token，
现有资产的原字节路径保持不变。XML 继承由标准 Core 预处理负责；运行时只读取
最终编译结果，不增加另外一套继承规则。

## 二进制合同

Dozer 原生类型 ID 为 `0x8D093323`；继承 token 为
`(1, 0x08DBC00E, AIUpdate 子表偏移)`。原生数据大小 `0x58`，前 `0x4C`
字节沿用 AIUpdate；三个浮点字段位于 `0x4C/0x50/0x54`，字段 token ID
分别为 `0x8229777E/0x18C1A7F3/0xC492A6B5`，标量类型均为 `0x645864F3`。
来源是既有反编译基线 `BinaryAssetBuilder.Tokenizer/-Module-.cs` 中的
`Tokenize(DozerAIUpdateModuleData)` 与多态 token 分派；原 DLL 没有对应的 XML
Marshal 入口，因此不能把原 DLL 的 Dozer XML 输出当作新增路径的差分 oracle。

Enhancer 扩展只附加 token 和独立数据，不增加原生 GameObject 字段。所有 token
均为三个 32 位字：`name, type, base-relative offset`。名字使用 EA 大小写敏感
FastHash：

| 位置 | name | type | offset |
| --- | --- | --- | --- |
| GameObject 根表 | `0x09135D3F` (EnhancerSettings) | `0x09135D3F` | 子表位置 |
| 扩展子表 | `0x45945FF5` (DozerConstruction) | `0x77E3F4F7` (bool) | 独立 bool 字节位置 |
| 扩展子表结束 | `0` | `0` | `0` |

bool 有效值占一个字节，分配器按四字节对齐。显式节点即使属性省略或为
`false`，也生成值为 `0` 的字段。原生解析后，Enhancer 按 definition 指针保存
侧表；释放 definition 时清除侧表。根 token 表偏移为 `0`。

## 验证与 fixture 再生成

`GameObjectDozerNative.cpp` 直接调用生产 `CompileGameObject`，验证原生大小、
AI 父层、三个原始浮点数、继承字符串/chooser 分配、扩展默认值与大小端。
输出 `dozer-{true,false,omitted}.{le,be}.bin/.relo`，均使用根偏移 `0`。

在仓库根目录，用 x86 MSVC Developer Prompt 执行：

```bat
mkdir fixtures
cl /nologo /std:c++14 /EHsc /MD /O2 /Irecovery/src/Native /Fe:GameObjectDozerNative.exe recovery/tests/GameObjectDozerNative.cpp recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/DataInitializers.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/ObjectFilter.cpp recovery/src/Native/WeaponSlotOrder.cpp recovery/src/Native/GameObject.cpp
GameObjectDozerNative.exe fixtures
```

具备 multilib 的 Linux 可使用真实 32 位 ABI：

```sh
mkdir -p fixtures
g++ -m32 -std=c++14 -O0 -Irecovery/tests/native-host -Irecovery/src/Native -o GameObjectDozerNative recovery/tests/GameObjectDozerNative.cpp recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/DataInitializers.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/ObjectFilter.cpp recovery/src/Native/WeaponSlotOrder.cpp recovery/src/Native/GameObject.cpp
./GameObjectDozerNative fixtures
```

宿主内核不支持 i386 时，可用 `qemu-i386 -L <32-bit-sysroot>` 运行。Linux
测试头只替代顺序执行的 Windows InitOnce；不得把它加入 Windows 生产编译。

`GameObjectDozerSmoke.cs` 另行验证真正 C++/CLI XML bridge、SDK XSD、四个平台
`0/1/2/99` 的 token 结果与拒绝行为。其引用为 `Recovered.GameObject.dll`
及 `BinaryAssetBuilder.Core.dll`，连同 `PluginSchemaBoundary.cs` 编译；可选
命令行参数为 fixture 输出目录。

本阶段本地证据：GCC 14 `-m32 -O0` + QEMU 的完整 Native harness 通过；
完整 CnC3Types XSD 的四个合法与五个拒绝文档通过（Linux lxml 验证时仅在内存
中规整既有 Windows 路径及 QName 尾部空格）。Native fixture 已被 Enhancer
真实侧表 parser 读取验证。MSVC/C++/CLI、真实 SDK 发布链及游戏内结果必须分别
以对应执行证据为准，本地 host 测试不代替这些验收。
