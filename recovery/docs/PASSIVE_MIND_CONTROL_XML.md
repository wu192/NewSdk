# 心灵控制塔 XML

`feature/passive-mind-control-tower` 配套
[`wu192/EnhancerCorona` 同名分支](https://github.com/wu192/EnhancerCorona/tree/feature/passive-mind-control-tower)，
集成目标仍为两仓 `feature/integration`。编译器与运行时必须同步部署，并重编旧心控资产。

```xml
<PassiveMindControlUpdate id="ModuleTag_PassiveMindControl"
    SpecialPowerTemplate="SpecialPowerPassiveMindControlWeapon"
    Capacity="3" AcquireRadius="200" CaptureIntervalFrames="15"
    DisableWhenStatus="POWERED_DOWN_EMP USER_POWERED_DOWN NO_SPECIAL_ABILITY">
  <TargetFilter Rule="ANY" Relationship="ENEMIES" Include="INFANTRY VEHICLE" />
</PassiveMindControlUpdate>
```

`SpecialPowerTemplate` 必填，类型为已有 `SpecialPowerTemplateRef`，必须与实际武器中
唯一单目标即时 `SpecialPowerNugget` 引用同一模板。没有新 Nugget 类型或独立技能模块。
`Capacity` 是正 uint32，默认 3；`AcquireRadius` 必填、有限且大于 0；
`CaptureIntervalFrames` 默认 15，范围为 `1..1073741822`。`TargetFilter` 必填，
复用原生 ObjectFilter，包括 `IncludeThing/ExcludeThing` 模板过滤。

`DisableWhenStatus` 可选，使用原有 `ObjectStatusBitFlags` 的 223 位、7 个 uint32。
来源塔出现其中任意一个状态即拒绝新心控；缺省或空掩码不按状态禁用。
禁用期间继续维护已有控制关系和死亡清理，不释放已控单位或连线，也不重置捕获截止帧。
这与目标过滤器状态及 `DisabledBitFlags` 无关，不新增状态枚举。

[容量 3/8 完整示例](../../examples/passive-mind-control/PassiveMindControlTower.xml)
保留 `CAN_ATTACK`、`WeaponSetUpdate`、`AIUpdate`、`OffensiveStructureAIStateMachine`、
库存光谱塔模型、生命/几何/视野和普通 Stop 命令集。MOD 将该文件加入资产流，
再通过建造命令集或地图放置对应塔。

手动显式攻击由原生武器的 `SpecialPowerNugget` 驱动，只读 Check 后由 Apply 调用
同一实例的捕获事务。`AIUpdate.AutoAcquireEnemiesWhenIdle="NO"`，没有活动显式攻击时，
Update 直接按原有三维距离、Filter、侦测和距离/ObjectID 排序搜索并捕获，不触发武器开火。
活动显式攻击期间不转抓其他目标，也不消耗自动轮次的冷却。
手动和自动共享 `CaptureIntervalFrames`、容量、占用及归还状态。

实际挂载的 `PassiveMindControlAssignedTargetWeapon` 只含一个即时单目标 SP Nugget，
没有弹道；示例每帧提供武器执行机会，实际射速仍受原生武器周期及模块截止帧共同限制。
`WeaponAiHintInfo.UseAsWarheadForDamageCalculations` 只引用独立的
`PassiveMindControlDamageEstimateOnly` 非零估算模板；估算模板不装入武器槽、不执行伤害。
最终范围与资格由运行时重新检查。普通右键/AI、Shift/Stop、原生栈重入、存取档、
CRC、回放及多人仍需游戏验收。

编译器沿用 GameObject XML bridge、ModuleChoice/ModuleLayout、引用 marshal、
ObjectStatus marshal、重定位和 token 服务。kind 为 292，TypeId 保持 `0xEE78914B`。

| 偏移 | 数据 |
| --- | --- |
| `0x00..0x13` | 原 TypeId/ModuleId、Capacity、AcquireRadius、CaptureIntervalFrames |
| `0x14..0x83` | 内联 112 字节 ObjectFilter，token mode 8 |
| `0x84..0x87` | 必填真实 SP 资产引用，name/type=`81D85EFA/AEE83888` |
| `0x88..0xA3` | 内联 7-word ObjectStatus 掩码，name/type=`7E6EE20B/A7BD811E` |

总大小由 `0x84` 扩大为 `0xA4`，外层仍为单条 12 字节多态 wrapper；body 有终止项。
新字段 hash 通过当前生产 `ExactStringHash` 生成并与既有原生 token 对照。
SP 字段走 `ModuleValue::Reference/MarshalReference`，不能当作普通整数做 endian swap；
掩码走 `ModuleValue::ObjectStatus/MarshalObjectStatus`，全部七字参与 endian。
tokenized 输出沿既有 BAB 合同由解析器重建 imports，不新增旁路引用表。
GameObject TypeId/TypeHash 不变，ProcessingHash 版本推进为 Win32 4106、Xbox360 4107、PS3 4108，
内置编译器版本同步，避免旧编译缓存保留旧布局。未使用扩展的既有资产三缓冲保持兼容。

定向验证继续使用 `recovery/scripts/TestEnhancerDozerXml.ps1` 和
`.github/workflows/feature-enhancer-xml.yml`，保留已有 `/Od`、`/O2`、四平台及原 DLL
三缓冲比较，不新增矩阵、工件或持久缓存。native 与真实 XML bridge 对照固定布局、
SP 引用 token/index、默认/空/多位/七字状态掩码、大小端和非法配置；完整示例继续对照
原生塔骨架及实际/估算武器的原 DLL 输出。

历史 [Windows 42307637203](https://github.com/wu192/NewSdk/actions/runs/42307637203)
验证的是旧 `0x84` 空 Nugget 合同，不能作为本次新布局/SP 执行的验收。
本次验证结果由当前提交对应 Actions 日志及 Job Summary 记录；组件通过不代替完整
SDK/MOD、Core 链接或游戏验收。
