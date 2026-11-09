# 原生车辆维修台示例

`ResearchRepairDepot.xml` 配合 EnhancerCorona 的 `feature/ra3-repair-dock` 使用，采用原生 `RepairDockUpdate`，没有新增模块工厂或资产协议。构建分支以两仓的 `feature/integration` 为基线。

`FramesForFullHeal="150"` 是浮点数；表达式求值后须为有限正数，不能写成 `150s`。`NumberApproachPositions` 为等待位数量，须至少为 1；维修位始终只有一个。首版显式使用 `MinDockTime="0s"`、`AllowsPassthrough="false"` 和 `GoToRallyPointAfterDock="false"`。

也支持 Schema 的派生写法：

```xml
<DockUpdate xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:type="RepairDockUpdateModuleData" id="RepairDock"
    NumberApproachPositions="2" FramesForFullHeal="150"
    AllowsPassthrough="false" MinDockTime="0s" />
```

Core 使用实际 SchemaType 写入原维修 TypeId `0x14B9EF65`；writer 生成 32 字节模块，保留 Dock 基类及可空的语音撤退安全港 ObjectFilter。该 filter 不用于维修准入。writer 处理指纹已更新，旧 GameObject 编译缓存会失效，根资源 TypeId/TypeHash 不变。

示例引用的 `ResearchRepairDepot_SKN` 模型需另行提供，并在所有可用模型状态中公开一致的 `DockWaiting01`、`DockWaiting02`、`DockStart`、`DockAction`、`DockEnd`。点位与开放车道的美术合同见 XML 注释；示例没有建造菜单或阵营接入。Schema 或编译结构检查不能证明实际车辆通行、存档或联机行为。

实施依据：[车辆进站维修实施方案](https://github.com/wu162/ra3idb/blob/main/research/ra3-repair-dock/07-implementation-plan.md)。
