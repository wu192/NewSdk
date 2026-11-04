# Joypad 资产与并行编译

恢复原先排除的 11 个 Joypad 全局资产入口：`JoypadCommandBarTemplate`、
`JoypadCommandBarButtonTemplate`，以及 `UIJoypadCommandBar` 的 ButtonBuild、
ControlGroups、CoopCommunicator、HomogenousGroup、MixedGroup、PlayerPowers、
SingleUnit、Stances、TopMenu。既有 UIConfigList 内嵌 Joypad 组件继续使用原布局。

## 当前并行扩展（2026-11-01）

11 个独立根现已显式加入 `XmlBackendCertification`，metadata worker 注册从 284 增至
295 路、63 个 family。四类 APT 继续保留单独的文件读取租约，TestTexture 和未知类型
仍不进入这份 metadata 注册表。34 个运行时 Data 分支随 Joypad 根执行；既有
UIConfigList 内嵌 Joypad 继续走其已认证路径。

源码审计确认桥接输入树、Builder/Tracker、endian word 和 ObjectFilter 列表均由单次调用
持有，根/多态布局和枚举表只读，没有外部文件读取或全局哈希注册。沿用既有只读 DOM
借用、私有 document view、准备/内存预算、worker 执行和 owner 提交，不引入新锁、线程池
或持久缓存；TypeId、TypeHash、ProcessingHash、插件版本与原字节布局均未改变。
[闭合认证审计](../../XML_BACKEND_REENTRY.json) 的 `joypadAudit` 记录了这次显式扩展。

生产源码 `a75840ef7d0351cedc11c91b1b4510d295b911a7` 的 Debug/Release 在
[41299605157](https://github.com/wu162/NewSdk/actions/runs/41299605157) 构建。该运行的组件
独立成功组覆盖四平台 0/1/2/99、295 路最小输入和每平台 89 个 Joypad 默认/完整输入，
包括全部 11 根、34 个 Data 分支的真实 worker 与 owner 三缓冲原字节一致。

测试源码 `6c098c0c240ae6a29ec2ea3688e53a39cd906f32` 的最终
[41300821811](https://github.com/wu162/NewSdk/actions/runs/41300821811) **success**，严格核对生产
闭包后复用同一二进制；CLI/取消独立成功结果来自
[41300380253](https://github.com/wu162/NewSdk/actions/runs/41300380253)。原失败总运行保留，
不把重放脚本提交当作新编译器源码。

本轮只编译小型 MOD：43 个既有资产加两个 256-Slot 列表资产，共 45 个，保留全部
11 根、29 个合法 XSD Data 选择与四类支持资产。生产 bin 与独立解压 ZIP 各运行
Debug/Release 共 18 次实际 CLI：jobs1/4/auto、默认缓存冷/热、换输出目录/并行度、
一次实际 Ordinal 编辑及独立串行对照。每配置实际 Joypad 调用有多 worker 时间区间重叠，
四流始终逐字节一致；两配置串行输出相同，其中 Release 与独立原 BAB 的完整四流相同。
冷对照显式隔离中间目录，因为 `/sc:false /bc:false` 不会取消既有中间产物复用。

[待确认新运行包](https://github.com/wu162/NewSdk/actions/runs/41300821811/artifacts/13081475771)
只含 D/R 三文件 ZIP、两个 manifest、说明、构建元数据和 SHA256SUMS。已下载复算全部
校验项，包内与生产字节匹配，不含源码。没有运行大型 MOD、完整 mymod/全量 SDK 编译、
性能基准或游戏内 UI 验收；小负载的真实并行不能直接推出大型 MOD 提速。
用户已明确批准并发布 [compiler-2026.11.01](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01)。两个发行 ZIP 与确认包逐字节相同；独立标签快照仅含 README 和元数据，自动归档也无源码。七份附件大小/SHA256 与远端一致，见[发行收据](../../../evidence/releases/compiler-2026.11.01/published.json)。源码 PR #49 未合并。

## 独立资产恢复与历史原字节证据

`Xmljoypad.cpp` 保留原根大小、引用提交顺序、列表分配顺序及全部 34 个按钮 Data
多态布局。包括 AutoControlGroup 的完整内联 ObjectFilter、两类 targeted power
的 nullable RGB、额外 power/radius 引用列表、多个目标的 typed id 哈希和默认值。
7 个简单设置根的引用位于 offset 4，CoopCommunicator 使用 `AICoopTemplate`。
原 TypeId、TypeHash 和引用/重定位表合同不变。

Data 包装节点仅有一个子节点时编译；未知 TypeId 分配 4 字节并写 0。未提供的
普通可选属性保留零；GroupShortcut 与 TargetCount 缺省 1，MinTargetDistance
缺省 0，target outline 缺省 HOVER_ATTACK。未知枚举按原 DLL 返回值处理，比较保留
C 字符串语义。桥接器按原 Node 契约读取无命名空间属性、剥去一个属性值前导反斜杠，
单子节点选择同命名空间的首项，命名列表使用 ea 命名空间，列表值保留完整子树文本。
非 Win32（包括未知平台 99）执行完整 scalar / ObjectFilter endian
转换，引用和 pointer endian 交给 Tracker。

原始依据位于 `recovery/evidence/xml-source`：原 Module blob
`2d2f471c8008b7c2e1f78e39752d0726da395cea`，Marshal 方法 48411–49058、列表和
Data 多态方法 74635–75822；7 个简单根另核对原 Plugin.cs 的 HandleType 内联实现。
生成器 `GenerateXmlJoypadLayouts.py` 从固定原方法提取 34 个 TypeId/分配大小、从原
struct 提取 11 个根大小，并读取 6 份原枚举映射；运行不需要原 DLL。

源码 `1f0cea03083f2219e43fa35f3dd44df73158310e` 的
[Windows 41270490335](https://github.com/wu162/NewSdk/actions/runs/41270490335) 整体 success。
组件 Od/O2 各 6,000 个样本、36,000 次三缓冲对照，覆盖平台 0/1/2/99；
1,480 个生成输入外，另有命名空间、首子节点、反斜杠及 NUL 边界。全部与原 DLL
原字节一致，direct 与 ProcessInstance 两路均检查；完整布局及拒绝结果见原收据。

生产 Debug/Release 均重新构建，通过全部 310 条内建路由元数据、重新初始化和
拒绝检查。自制小 MOD 有 43 个资产，包含 11 个 Joypad 根、全部 29 个 XSD Data
分支及 4 个支持资产；不改 SDK Schema，使用 jobs1、禁用两类缓存编译 base loose。
两配置的 manifest/bin/imp/relo 均与独立原 BAB 进程逐字节一致。运行归档已单次
下载复算 SHA256，核对 D/R 三文件、验证入口的生产 link 输入和八对原始四流。
[摘要及原始证据](../../../evidence/joypad/41270490335-1/summary.json)。

历史 82 类及 207 类目录保持原样；新增 Joypad 单独登记为 11 类。当前 XML 路由
为 300 类（278 全局、22 非全局），只排除 TestGameObject。旧 289 类 SDK 验收不
冒充新增 300 类的全局全量 SDK 验收；本轮仅增加 Joypad 组件和上述小 MOD 证明。
上述历史版本的新类型沿用 owner 编译路径，当时没有新增并行/缓存矩阵、完整大型 MOD、性能或游戏内
UI 验收。最初两次脚本预检失败及修复来源保留在摘要中。
