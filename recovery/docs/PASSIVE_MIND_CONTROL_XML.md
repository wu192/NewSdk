# 心灵控制塔 XML

`feature/passive-mind-control-tower` 基于 `feature/integration`，配套运行时为
[`wu192/EnhancerCorona` 同名分支](https://github.com/wu192/EnhancerCorona/tree/feature/passive-mind-control-tower)。
将 `PassiveMindControlUpdate` 放在塔的 `Behaviors` 中：

```xml
<PassiveMindControlUpdate id="ModuleTag_PassiveMindControl"
                          Capacity="3" AcquireRadius="200"
                          CaptureIntervalFrames="15">
  <TargetFilter Rule="ANY" Relationship="ENEMIES" Include="INFANTRY VEHICLE" />
</PassiveMindControlUpdate>
```

`Capacity` 为正 uint32，默认 3；`AcquireRadius` 必填，必须有限且大于 0；
`CaptureIntervalFrames` 默认 15，范围为 `1..1073741822`。`TargetFilter` 必填，
复用原生 ObjectFilter，包括 `IncludeThing/ExcludeThing` 模板过滤。
运行时逐个取得目标，满额停止获取；出圈保持，目标死亡腾位，塔死亡/销毁归还。
实际行为依赖配套 DLL，单独更新 schema 不会使原版游戏支持新模块。

[容量 3/8 示例](../../examples/passive-mind-control/PassiveMindControlTower.xml) 已补齐可选中建筑、
库存光谱塔模型、生命/几何/视野、普通 Stop 命令集、`CAN_ATTACK`、
`Behaviors/WeaponSetUpdate → WeaponSlotHardpoint → Weapon` 与独立 `AI/AIUpdate`。
`AIUpdate` 使用 `OffensiveStructureAIStateMachine`，`AutoAcquireEnemiesWhenIdle="NO"`，
让被动模块继续承担自动心控。MOD 将该文件加入资产流，再通过普通建造命令集或地图放置
容量 3/8 模板；没有新增技能或鼠标模式。

实际挂载武器 `PassiveMindControlAssignedTargetWeapon` 的 `<Nuggets/>` 为空，
不发射弹体、不施加伤害或状态。它的 `WeaponAiHintInfo.UseAsWarheadForDamageCalculations`
只引用独立的 `PassiveMindControlDamageEstimateOnly` 非零伤害估算模板；该模板不装入武器槽。
武器射速不决定心控频率，取得仍共用 `CaptureIntervalFrames`。`AttackRange` 与
`AcquireRadius` 示例均为 200，但最终资格仍以运行时三维中心距离、过滤器和可见性复检为准。
此配置是无副作用普通攻击候选，游戏中的光标/CanAttack/选武器/视线/范围门尚未验收；
空 Nuggets 的编译通过不能作为普通右键输入已通过的证据。

编译器沿用 GameObject XML bridge、ModuleChoice/ModuleLayout、重定位和 token 服务。
新 kind 为 292；`PassiveMindControlUpdateModuleData` TypeId 为 `0xEE78914B`。
数据固定为 `0x84` 字节：公共 TypeId/ModuleId、`+8` Capacity、`+0xC` AcquireRadius、
`+0x10` CaptureIntervalFrames、`+0x14` 内联 112 字节 ObjectFilter。
过滤器 token 使用 mode 8，外层为单条 12 字节多态 wrapper。
GameObject TypeId/TypeHash 保持原值，ProcessingHash 版本推进至 4103/4104/4105，
避免旧编译缓存绕过新增模块逻辑。

定向验证沿用 `recovery/scripts/TestEnhancerDozerXml.ps1` 和
`.github/workflows/feature-enhancer-xml.yml`，保留 `/Od`、`/O2` 与四平台范围。
新增 native harness 校验数据布局、默认值、容量 1/3/8/uint32 上限、非法值、
内联过滤器及模板列表重定位；XML harness 处理相同配置与正式示例，比较 native/XML
三缓冲结果，并用原 Tokenizer DLL 比较四组未使用扩展的旧 GameObject 三缓冲字节。
普通攻击增量同时通过生产 Weapon bridge 编译实际/估算武器，检查空 Nugget 列表、
非零估算与 hint 引用；完整塔去除扩展后的普通攻击骨架及两武器继续比较原 DLL 三缓冲。
生产示例还直接检查 native `CAN_ATTACK`、关闭 idle auto-acquire 及攻击建筑状态机。
新模块没有原 DLL 实现，不能用旧编译器作为其语义 oracle。
每轮结果、来源提交、耗时和 fixture 身份写入 Actions 日志及 Job Summary，
不上传工件、不增加矩阵或持久缓存。

此前 Windows [42229679853](https://github.com/wu192/NewSdk/actions/runs/42229679853)
验证源码 `ef68ccfe`：上述 `/Od`、`/O2` native/CLR 全部通过，两个模式分别约
23.3/20.9 秒；Actions 工件数量为 0。
[原始组件收据及两次失败原因](../evidence/passive-mind-control-ci-20261106.json)
保留来源提交、逐模式摘要和 fixture 身份。
首轮修正了测试对 Microsoft unsigned XSD 词法的假设；次轮发现 Microsoft XSD
仅凭数值上下限仍接受 NaN，因此生产 schema 增加十进制浮点词法约束。
非法值拒绝断言均保留。未执行完整 SDK/MOD、游戏、存取档、回放或多人验收。

本轮源码 `52338d0c62ede6c9d25f44ea1197a7feb3f17fa5` 的
[Windows 42307637203](https://github.com/wu192/NewSdk/actions/runs/42307637203) 已 success。
`/Od`、`/O2` 的 native/CLR 与四平台 XML bridge 全部通过，包括完整容量 3/8 塔、
无实际效果/估算武器、空 Nuggets、非零估算和准确的 hint/挂载武器依赖索引，
完整原生塔骨架及两武器的原 Tokenizer DLL 三缓冲对照也通过。
两模式分别 26.829/31.316 秒，components job 82 秒；API 确认工件数量为 0。
[本轮收据](../evidence/manual-mind-control-ci-20261107.json) 保存来源、逐模式摘要、
每模式 100 份 fixture 的大小/摘要及原始失败终态。

首轮 [42307169648](https://github.com/wu192/NewSdk/actions/runs/42307169648) 的 native/CLR
构建通过，但 `/Od` 在 hint 非空断言失败：组件宿主跳过了 Core 的依赖预处理，
原版与恢复版当时都将原始资产名编为空引用。修正仅在测试副本上加入 Core 形式的
`name\index`，精确核对输出的 index+1、引用 token 类型/地址及实际武器槽；
正式示例中的真实资产名保留，实际效果为空等断言没有放宽。

本轮没有改变 TypeId、kind 292、`0x84` 布局、schema 字段或生产编译分派。
这证明组件编译与对应二进制合同；组件宿主提供依赖索引，不代替完整 Core 解析/链接，
也未执行完整 SDK/MOD 或游戏。普通右键攻击资格、无副作用表现、攻击 order/Shift 队列、
存取档、回放与双客户端仍须分别进行游戏验收。
