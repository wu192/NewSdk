# 建筑断电工具按钮

`feat/power-toggle` 在集中 Tactical 工程的维修按钮左侧增加独立闪电按钮。
按钮只切换目标工具模式；模式内点击合法建筑才由 EnhancerCorona 发送原生561。
退出工具不会替用户恢复之前关闭的建筑。原维修和出售控件、FLA主文件及Authoring镜像不变。

## 源码与资源

- 根接线：`Tactical/Scripts/TacticalHUDCommandModeBar/TacticalHUDCommandModeBar/TacticalHUDCommandModeBar_frame_0001_define_GetSellButton.as`。
- Power专属交互：`Tactical/Scripts/TacticalHUDCommandModeBar/powerButton/PowerModeControl.as`。
- 实际发布闭包：`Tactical/project.json` 的 `TacticalHUDCommandModeBar` 条目。

新增导出复用主FLA中已有的通用按钮包装、白矩形、HelpBox闪电和动态文字元件。
闪电来自 `HelpBox_Symbol_64 / HelpBox_Bitmap_62.png`，不会用维修扳手冒充电源图标。
Power的按下、悬停、选中反馈独立更新；鼠标悬停显示 `Power off / restore`。
按钮位于工具栏局部坐标 `(-40, 0)`，保留原维修/出售/暂停位置，避开工具栏右侧的资源计数。
这些额外导出只加入该电影，不修改公共按钮脚本或其他电影的发布配置。

按钮初始隐藏且不可用；只有匹配的native实现完成登记并回写可用状态时才显示。
动态实例等待现有frame1控制器初始化后再安装Power回调，兼容attachMovie的延后执行。
未启用、缺少扩展或不支持当前版本时，不会留下一个能点击但没有业务的按钮。

## 与 EnhancerCorona 的本地协议

所有UI请求使用 `fscommand("CallGameFunction", args)`：

| 时机 | args |
|---|---|
| Power实例初始化完成 | `%PowerToggle:Ready,` + `Pointer.encode(this)`，this为工具栏根 |
| Power按钮可用时释放 | `%PowerToggle:Toggle`，一次释放只发送一次 |
| 工具栏卸载 | `%PowerToggle:Unload,` + 同一根引用 |

根提供 `GetPowerButton()` 返回真实控件引用。
native保存Ready中的根，在游戏支持的调用时机调用 `SetPowerModeState(activeArg, enabledArg)`，
参数为字符串0/1。根再调用Power实例的SetSelected、SetEnabled及SetVisibility。
高亮表示正在选择断电目标，不表示某栋建筑正在断电；隐藏/不可用不会直接改变建筑电力。

此协议不传对象指针或目标ObjectId，不替代游戏命令流。建筑资格、owner、光标、
模式互斥和取消路由由匹配的EnhancerCorona实现处理；它应设置 `enablePowerToggle=true`。

## 发布和打包

安装.NET 8和项目依赖后，在仓库根运行：

```sh
python resources/ui-projects/tools/build_power_toggle_ui.py --out work/power-toggle-ui
```

工具调用现有UiProject.Build，仅编译 `TacticalHUDCommandModeBar`，然后生成：

```text
work/power-toggle-ui/
  PowerToggleUI.big
  PowerToggleUI_1.0.skudef
  power-toggle-ui-build.json
  streams/TacticalHUDCommandModeBar/TacticalHUDCommandModeBar.{manifest,bin,relo,imp}
```

BIG使用现有MakeBig的未压缩BIG4/L219布局，四流位于 `Data\AptUI\`，
并包含SDK要求的同名 `.version`（空格+CRLF）。可将这五个成员合入已有MOD的BIG，
保留同名电影，或按现有MOD加载顺序加入此BIG；不要让另一个HUD包随后覆盖同一电影。
集中工程尚未接入NewModCompiler自动发现，因此不要把整个Tactical.fla直接作为单电影素材发布。

这个包只包含HUD电影。配套native DLL、配置和新光标alias的base资产流需由对应MOD一起提供，
不能把UI-only包误报为完整native扩展。构建收据记录实际输入、四流和BIG身份。
编译与离线交互检查不替代游戏内布局、鼠标命中、tooltip、模式/光标及联机回放验收。

本轮离线验证记录见 [power-toggle-ui-validation.json](evidence/power-toggle-ui-validation.json)：
集中工程的39个电影均完成基线/修改后编译，38个无关电影的152个流文件逐字节相同；
新增按钮的已编译AVM1使用mock验证本地协议与事件门控，未进行游戏内运行验收。
