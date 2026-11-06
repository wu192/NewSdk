# 直接编辑 APT：建筑断电按钮

本分支从 `resources/ra3-ui-assets@66c5866070896bebc37fe5e9bdafba42a8822b9d` 拉出。
修改入口就是本目录的 APT/CONST/RU 资产；不使用 FLA 或 AS 作为此按钮的构建输入。

配套 native 实现：[wu192/EnhancerCorona PR #3](https://github.com/wu192/EnhancerCorona/pull/3)，
基于 `feature/integration`。需匹配该分支的 Windows x86 DLL、Steam 1.12，以及 MOD
`Data/enhancer/config.json` 根对象的 `"enablePowerToggle": true`；其余原配置保留。
本 UI 分支不修改或附带 DLL。

## 实际文件与发布入口

- `TacticalHUDCommandModeBar.apt`：RA3 `Apt Data:74`，64个字符，11个导出，保留 HelpBox import。
- `TacticalHUDCommandModeBar.const`：194个 typed values；前121项保持原类型及原值。
- `TacticalHUDCommandModeBar_geometry/57.ru`、`59.ru`：白矩形和矢量闪电。
- `TacticalHUDCommandModeBar.xml`：原 SDK `AssetDeclaration`，仍走
  `AptAptData/AptConstData/AptDatData/AptGeometryData`，新增两份 RU 已完整声明。
- `editing/TacticalHUDCommandModeBar.apt.xml`：可编辑的角色、帧、导出和 AVM1 指令 XML；
  这是编辑器输入，不能当成 SDK 的 `AssetDeclaration`。
- `editing/TacticalHUDCommandModeBar.const.json`：与指令索引对应的 typed CONST 表。
- `editing/migration.json`：原文件身份、既有编译产物中根动作的迁移来源及常量映射。
- `power-toggle-apt-build.json`、`editing/validation.json`：重建及离线验证收据。

原 DAT、原 RU/纹理和 SWF/EAF/TAC/FLM 均保留原字节。SWF/EAF/TAC 是历史快照，
不会随本次 APT XML 编辑自动重建；它们不代表新增按钮的当前指令。
`converted/` 的恢复 FLA/AS 也不参与此按钮发布。

原 SDK/原始 APT 流程继续使用本目录 `TacticalHUDCommandModeBar.xml` 与它引用的资产，
保留相对目录及纹理闭包。已有可用的同电影入口时，以这份 APT/CONST/RU 闭包替换对应输入；
不要另行将 `editing/*.xml` 作为资产入口编译，也不要同时加载另一份覆盖同电影的旧 HUD。

## 重建已修改的 APT/CONST

工具格式来自 [Jonwil cc3tools](https://github.com/jonwil/cc3tools/tree/ae47b0996927bcf3c43ba3494805afa37b8744de)。
准备该版本的 `xml2apt` 后，在本目录运行：

```sh
python tools/rebuild_apt.py --xml2apt /path/to/xml2apt --out .
```

Linux 若使用该工具的 x86 构建且宿主不直接执行 x86 ELF，可追加
`--runner /path/to/qemu-i386`。所有临时 XML/APT 写入临时目录，成功后更新目标 APT、CONST
及构建收据。此步骤只读本目录的编辑 XML/typed CONST，不调用 UI/FLA/AS 编译器。
RU 和 SDK AssetDeclaration 是独立的原资产输入，需与生成文件一并发布。

`xml2apt` 能重定位绝对指针，但 XML 中 `goto.pos` 和 `definefunction.size` 是显式字节数，
修改指令长度后须同步修订，不能认为工具会自动推导跳转距离。本次迁移保持原根动作指令长度，
只按 typed value 映射 Push 常量索引，并检查全部函数大小及跳转边界。

## 操作与协议

闪电按钮位于维修左侧 `(-40, 0)`，避开右侧资源计数；悬停显示 `Power off / restore`。
文字使用原 HelpBox 同名字体 `Lucida Sans`，高度14像素。面板、图标各有 Sprite wrapper；
通用按钮包装复用原角色56，维修/出售/暂停脚本与原放置参数不变。

按钮起初隐藏且禁用，等待 native 在可用状态下回写；选中表示正在选择断电目标。
按下只显示按下态；可用时释放只发一次 Toggle。模式内目标操作和资格由 native 处理，
再次点击或原生取消退出模式；退出不会给先前关闭的建筑自动复电。

| 方向 | 本地协议 |
| --- | --- |
| UI→native | `CallGameFunction`, `%PowerToggle:Ready,<Pointer.encode(root)>` |
| UI→native | `CallGameFunction`, `%PowerToggle:Toggle` |
| UI→native | `CallGameFunction`, `%PowerToggle:Unload,<Pointer.encode(root)>` |
| native→root | `SetPowerModeState(active, enabled)`，参数字符串0/1 |
| root getter | `GetPowerButton()` 返回 encoded 实际按钮 |

动态实例等原控制器初始化后再安装专属回调；保留未完成初始化时卸载的清理路径。
根动作位于原四个 placeobject 后。没有匹配 native 时按钮保持隐藏；新光标 alias 不在本包内，
对应 native 提供原游戏光标回退。

## 验证边界

原始74素材先经成熟 `apt2xml→xml2apt→apt2xml` 基线往返，解码 XML 完全一致；
序列化会重排数据，不能据此声称 APT 字节不变。新增素材也经独立 `apt2xml` 核对：
原角色1–56、导入、原七项导出及原放置参数的解码语义不变，DAT/原 RU/纹理仍逐字节相同。

受控字节码检查直接读取新增 APT/CONST，没有 FLA/AS 输入：

```sh
dotnet run --project tools/bytecode-smoke/RawSmoke.csproj -c Release -- /absolute/path/to/TacticalHUDCommandModeBar
```

20项 mock 检查覆盖 Ready、默认隐藏/禁用、实际按钮 getter、native状态回写、hover、
按下无命令、释放单次 Toggle、不可用门、延迟初始化及匹配根卸载。
另核841条根指令、18个函数体长度和27个分支目标，与既有实现的根指令语义图一致。
渲染、原共享控制器的实际 frame 调度、native 调用均未在游戏里执行。

本轮未运行完整 Windows SDK 产品，也未进行游戏内播放、鼠标命中、首帧时序、断/复电、
存读档、联机或录像验收；离线资产/受控交互通过不能记作这些项目已通过。
