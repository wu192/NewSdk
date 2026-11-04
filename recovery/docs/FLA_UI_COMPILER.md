# FLA / ActionScript 直接编译 UI

基线：`recovery/legacy-sdk-cache` / `665468cb`。实现分支：`feat/fla-direct-mod`，
[PR #36](https://github.com/wu162/NewSdk/pull/36)。转换核心移植自
`feat/flash8-fla-to-swf` / `5b52a0d3` 的 `ui/`，直接生成最终资产缓冲。

## 使用

把源文件放在 MOD 的 `Data/AptUI/<movie>/` 下；保持 FLA、同名时间轴 AS、AS2
类文件及 include 的相对结构。自动发现会递归查找 FLA，每部影片生成一个 APT 流：

```text
NewModCompiler.exe build --mod C:\Mods\CoronaUI --sdk-root C:\NewSdk\SDK --out C:\Build\CoronaUI --package-content apt --package-format loose
```

例如 `Data/AptUI/CoronaUIMain/CoronaUIMain.fla` 生成
`published/apt/CoronaUIMain/streams/coronauimain.manifest`、`.bin`、`.relo`、`.imp`。
源目录不需要现成 SWF、APT、CONST、DAT、DDS 或外部 Flash 程序。
同名的顶层 XML 保留为原入口；所选 XML 及其 Include 已引用的 FLA 不再自动生成第二个任务。
同名独立影片仍按现有流名冲突规则报错。

也可以在 `Data/Mod.xml` 或已有 APT XML 中声明，与普通 XML 资产混合：

```xml
<AssetDeclaration xmlns="uri:ea.com:eala:asset">
  <ArmorTemplate id="UiMixedArmor" Default="72.5%" />
  <AptAptData id="CoronaUIMain_apt"
              File="AptUI/CoronaUIMain/CoronaUIMain.fla" />
</AssetDeclaration>
```

`id` 必须以 `_apt` 结尾，其余部分作为影片名。`File` 也可指向只含一部 FLA 的目录；
路径遵循原 Core 的相对路径和 `data:` / `art:` 解析规则。已有二进制
`AptAptData File="...apt"` 仍交给原 XML 后端。

## 集成与缓存

`UiAptPlugin` 注册到内建编译器的 `AptAptData` 路由。Core 在 XML/路径验证后，
将 FLA 展开为 `AptAptData`、`AptConstData`、`AptDatData`，以及实际需要的
`AptGeometryData` 和 `Texture`。这些资产使用真实类型和名称，走原 Core
提交、排序、链接和失败处理。纹理包含 inline DDS，不新增 CDATA。

实例身份混合完整源内容和 UI 编译器版本；会话缓存记录 FLA/AS 依赖及目录成员，
AS 修改、添加、删除和 UI 实现升级会使相关文档失效。生成资产保存在原缓存中，
缺失输出按原重载路径重建。声明与生成资产同名时拒绝构建。
失败沿原直接输出合同清理本次已开始流的四文件，保留可复用中间资产及允许的部分会话；
修复源文件后重试重建完整四流。
裸 FLA 的内部 XML 包装保存在当前 SDK 缓存分区的 `generated/fla/`，不改源目录，
不作为最终 MOD 文件发布。

仅直接 FLA 流将该合成输入目录放在 DATA 根首位，保持 `DATA:<movie>.xml` 逻辑源名，避免把 `SuperCompilerCache/<ModKey>` 写入 manifest 源路径；普通 MOD 的 DATA 搜索顺序不变。loose 导出的公开布局保持上例形式，BIG 直接读取缓存流。完整目录合同见 [集中缓存说明](SUPERCOMPILER_CACHE.md)。

UI 源码随 Foundation 编入单 EXE。LibTessDotNet、Clipper2Lib、StbImageSharp
按 `ui/dependencies.json` 固定版本和包身份恢复，并作为 EXE 资源加载；正式部署仍为
五文件布局。来源和许可保留在 [ui/VENDOR.md](../../ui/VENDOR.md) 和 `ui/licenses/`。

## 覆盖边界与验证

支持二进制 Flash MX 2004 (`0x38`) 和 Flash 8 (`0x3F`) 的 FLA、常用 AS1/AS2、
AS2 类/继承、互相 import，以及时间轴、矢量、位图、按钮和文本。
不支持 AS3、ZIP/XFL、滤镜/混合模式、音视频等；完整限制见 [UI README](../../ui/README.md)。
静态文字轮廓需要 Windows 中安装所用字体。按钮状态帧中的编辑器脚本按 Flash
发布语义忽略；按钮实例事件仍按事件记录编译。

两份用户附件中的 FLA/AS 原样保留在 [fixtures/ui](../fixtures/ui/README.md)，
不将附件文档当作任务指令。CoronaUIMain 组件转换生成 3 个资产，EnhancerApt
生成 62 个资产；已有运行时布局和 AS 执行基线继续保留。

正式验收由 `.github/workflows/ui-fla.yml` 执行：Debug/Release 产品重建、精确部署与
路由、两附件混合 MOD 四流、源缓存失效/失败重试、原 Core/CLI/会话缓存及完整 mymod
原 SDK 差分。当前运行状态以 Actions 实际终态和归档收据为准，不能将本地组件通过
视为正式产品或游戏内验收。未新增游戏内 UI 播放验收。

## 本轮验收结果

生产/测试源码 `75d29895be464d9a560f5f946d612e359bf4b396`，
[Windows 41165126334](https://github.com/wu162/NewSdk/actions/runs/41165126334) 整体 success。
Debug/Release 均为本轮新构建，精确五文件部署、全部路由、两附件混合四流和裸 FLA
自动发现通过；每模式 15 个真实 UI CLI 用例，热态零实例编译，AS 等长同 mtime
修改、新增、删除、失败清理及重试原字节一致。原 Core、CLI/取消、完整会话缓存、
完整 mymod 原 SDK 四流/打包差分和两个独立运行 ZIP 均通过。

原始归档 SHA256 为 `e88d0b947f89e6e1cdf98444d675372dc6a7b3ff7803da1e139acecbb5bacf10`；
已实际下载复算，十个运行文件及两个 ZIP 均与产品收据/发布清单一致。
[身份摘要与原收据](../evidence/ui/fla-validation/41165126334-1/summary.json) 保留二进制、
源码、测试和运行来源；较大的原收据仅做 gzip 压缩，解压后原始 SHA256 不变。
[历史失败](../evidence/ui/fla-validation/history.json) 保留按钮兼容修复及新增测试错误预期的原因，
没有放宽生产错误退出、依赖失效或重试字节断言。后续文档提交不改变已验收生产输入。

[下载构建产物（含 Debug / Release ZIP）](https://github.com/wu162/NewSdk/actions/runs/41165126334/artifacts/13028968508)，
通常使用其中 `ui-tests/release/NewModCompiler-Win32-Release.zip`。未合并 PR，未新增游戏内验收。
