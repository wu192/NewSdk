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

[容量 3/8 示例](../../examples/passive-mind-control/PassiveMindControlTower.xml) 是可通过
完整 SDK schema 的模块配置资产，未配置模型、命令或建造入口。集成可玩 MOD 时，
把行为并入已有塔的继承配置。

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
新模块没有原 DLL 实现，不能用旧编译器作为其语义 oracle。
每轮结果、来源提交、耗时和 fixture 身份写入 Actions 日志及 Job Summary，
不上传工件、不增加矩阵或持久缓存。

本地仅完成完整 schema 组合/示例与非法值检查及 C++ harness 语法检查；
Windows native/CLR、完整 SDK/MOD、游戏、存取档、回放和多人结果须分别核实，
不能由 schema 或组件通过推定。
