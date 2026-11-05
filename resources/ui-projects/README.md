# Tactical 集中编辑工程

分支 `resources/tactical-editing-project`，基于 `66c58660`。这是新建的编辑工程，原 `resources/ra3-ui/` 中已经恢复的全部 FLA、AS、映射、原始包和报告保持原样。

现有 **一个 Tactical.fla、39 个独立发布入口、1,323 个 AS 文件**：涵盖27个Tactical界面和12个可确定的加载依赖。其余Frontend、Briefings等分类尚未迁入此主工程。

```text
Tactical/
  Tactical.fla          日常图形、时间轴编辑入口
  Common/Scripts/       真正共用的一份公共AS
  Scripts/<Movie>/     各组件自己的AS
  project.json          实际发布配置，必须保留
  runtime-dependencies.json
  Authoring/            导入时的XFL镜像及验证夹具
```

## 如何编辑

打开 `Tactical/Tactical.fla`，在库里选：

- `Roots/`：各组件的完整时间轴，例如 `Root_TacticalHUD`、`Root_TacticalHUDMainCommandBar`、`Root_TacticalHUDBuildQueuePage`、`Root_TacticalHUDRadar`。
- `Common/`：已确认可共用的元件，例如 `Common_WhiteRectangle`、`Common_SimpleRenderingSurface`、`Common_HelpBoxSite`。修改一次会用于引用它的组件。
- `Components/<Movie>/`：组件私有元件；有原始linkage或实例名的条目已语义化，无可靠名字的条目保留原Symbol编号。

AS继续由FLA的相对include读取。公共代码在 `Common/Scripts/` 维护一份，私有代码在各组件自己的Scripts目录。脚本文件名保留真实元件、帧、事件或函数信息。

建筑断电工具按钮的独立AS、EnhancerCorona本地协议及单电影BIG发布入口见 [POWER_TOGGLE.md](POWER_TOGGLE.md)。

**发布配置由 project.json 控制。** 各组件的舞台尺寸、FPS、背景、导出别名和依赖清单均独立保存；改主FLA的总舞台属性不会自动改这些配置。新增/改名库项、AS文件或导出Linkage时，需要同步更新相应清单。普通位置、颜色、文字、时间轴和现有AS内容的修改直接来自主FLA/源AS。

这里的project.json参与实际发布，与旧工程中仅供追溯/修改测试使用的script-map.json用途不同。

HelpBoxSite原生导出和导入包装的图形结构存在真实差别，保留了图形变体；其完全相同的公共AS已共享。没有为了去重强行合并不同图形。

## 构建

需要.NET 8和已有项目依赖。构建过程不下载UI资产，也不启动GitHub Actions：

```bash
dotnet run --project resources/ui-projects/tools/UiProject.Build -- resources/ui-projects/Tactical/project.json --out work/tactical-build
```

仅发布一个组件：

```bash
dotnet run --project resources/ui-projects/tools/UiProject.Build -- resources/ui-projects/Tactical/project.json --out work/tactical-build --movie TacticalHUDMainCommandBar
```

每个组件分别输出 `work/tactical-build/<Movie>/<Movie>.manifest/.bin/.relo/.imp`。发布根是真正的独立电影根，保留原电影名、实例层级及loadMovie关系。默认批量构建只解析主文档一次；所有组件编译成功后开始写入各自四流。工具明确拒绝把输出写到旧 `resources/ra3-ui/` 树内。

主场景用于集中编辑。Flash编辑器GUI、完整Windows SDK产品和游戏内播放尚未实测；直接运行主FLA不能替代游戏内HUD验收。当前通过新增独立发布工具使用此工程，尚未将project.json接入NewModCompiler原有自动发现。

## 验证

- **39/39组件、156/156四流** 与原独立FLA发布结果SHA-256完全相同。
- 逐组件编译与批量编译全部一致。
- 实改一份公共AS，14个使用者变化，其余保持不变。
- 实改命令栏私有AS，仅命令栏变化。
- 实改公共WhiteRectangle图形，7个使用者变化，其余保持不变。
- 建造队列的6个独立加载位置保持原名称和顺序。
- UI组件回归 **797 checks passed**；net472类库构建零警告、零错误。
- 原资源整棵Git树保持 `53dddf9509736adb148ff834eaa290e60295c28d`。

实际收据见 `evidence/tactical-validation.json`、`evidence/summary.json` 和 `evidence/immutable-resources.json`。

`TacticalPlayerStatusDialog`仍保留原包缺失ShellButtons的标记，生成器没有虚构补齐内容。`runtime-dependencies.json`记录34个确定加载关系和6处变量加载位置；变量目标保持原逻辑，未推测。

## 离线重新导入

日常修改以 **Tactical.fla及AS为准**。Authoring是导入时镜像，不会自动反映用户后来在FLA里的修改；重新导入不会覆盖已有主FLA。

从原始SWF重新构造一份新的工作副本，需要本地JDK21及已入库FFDec工具：

```bash
python -m zipfile -e resources/ra3-ui/tooling/ffdec_26.4.0.zip work/ffdec
python resources/ui-projects/tools/build_tactical.py --group tactical --output work/tactical-reimport --ffdec work/ffdec --work work/tactical-seed
```

工具只写新的output/work目录，拒绝旧资源树及已有Tactical.fla。输入无需依赖本次会话的临时XFL目录；已实际验证从原始SWF离线导入四组件，并发布出相同的16个四流文件。
