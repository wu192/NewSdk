# Flash MX 2004 / Flash 8 UI 编译插件

纯 C# 类库，源码、依赖来源、测试均在根目录 `ui/`。不需要安装 Flash 8、Java、Python，也不启动 eaf2apt、flasm 或 triangle 进程。

输入是同一个素材目录里的二进制 Flash MX 2004 / Flash 8 `.fla` 和配套 `.as`。输出只有最终 Mod 的 `.manifest/.bin/.relo/.imp`。编译路径为 `FLA/AS → 时间轴、网格、像素、指令对象 → 最终 AssetBuffer → 四流`。内存中也不创建独立 SWF、APT、const、dat、RU、DDS 或编码后的 AVM1 中间缓冲区，不进行 SWF 序列化与重解析。游戏需要的 APT、常量表、几何和纹理运行时布局直接写入最终资产并链接到 `.bin`；这些是最终产物的组成部分。无需另建 cdata 文件。

## 宿主入口

`BinaryAssetBuilder.UiCompiler.Plugin` 实现与 AudioCompiler 相同的 `IAssetBuilderPlugin` 生命周期：`Initialize`、`ReInitialize`、`GetExtendedTypeInformation`、`ProcessInstance`、版本/类型哈希和 `Dispose`。

目录直接编译入口：

```csharp
using BinaryAssetBuilder.Core;
using BinaryAssetBuilder.UiCompiler;

using (var plugin = new Plugin())
{
    plugin.Initialize(null, TargetPlatform.Win32);
    // Main.fla、Main.as、其他 AS 类/包含文件都放在 sourceFolder 中。
    // 顶层多个 FLA 会一起生成同一组最终四流。
    ModStreams result = plugin.CompileFolder(sourceFolder, outputBase);
}
```

`outputBase` 例如 `Mods/MyMod/data/ui`，最终文件为 `ui.manifest`、`ui.bin`、`ui.relo`、`ui.imp`。不是独立命令行程序。

NewModCompiler 已内建此插件：Core 在验证阶段调用 `CompileUiFile(declaration, Win32)`，将所有真实兄弟资产交给原输出管理器，并追踪 FLA/AS 内容、目录成员和编译器版本。混合 MOD 不使用本目录的独立 `ModStreams` 写出器。用法见 [FLA UI 集成](../recovery/docs/FLA_UI_COMPILER.md)。其他宿主也可调用该接口。声明使用 `AptAptData`，`id="Main_apt"`，`File` 指向 FLA 或只含一个 FLA 的素材目录。

标准 `ProcessInstance` 只能返回一个 `AssetBuffer`。因此该入口要求 `UiCompilerConfiguration.Collector`：主 AptAptData 通过返回值交回，其余 AptConstData、AptDatData、AptGeometryData、Texture 及源文件依赖一次性交给 `IUiAssetCollector`。缺少收集器会明确报错，防止少编译资产。旧宿主若没有多资产展开阶段，应使用目录入口。插件自身不复用旧文件或缓存；宿主缓存必须考虑全部 `InputFiles`。

## AS 关联规则

默认遵循 Flash 8 项目约定：

- 同名 `Main.as` 中的时间轴代码加入 `Main.fla` 第一帧；如果同名 AS 声明的是类，则按类处理。
- FLA 内的帧/实例脚本、同名主脚本及类文件中的 `#include` 相对其所属源文件解析；包含文件必须在素材目录内。
- `import`、命名空间目录、元件的 AS2 链接类名解析目录中的类文件；支持显式导入和包通配导入。
- AS 支持 UTF-8（有/无 BOM）和带 BOM 的 UTF-16。类可相互 import；缺失类、缺失 include、include/继承环和语法错误会中止构建。

支持常用 AS1/AS2 表达式、变量、函数、成员、数组/对象、条件、循环、for-in、switch、事件处理器，以及 AS2 类、继承、实例/静态成员和 getter/setter。不是 AS3 编译器。`try/with` 源语法、外部运行时共享库和完整 AS2 接口类型系统尚未实现，会明确拒绝。

## 版本及能力边界

接受 OLE/CFB 二进制 FLA，Contents 修订号为 Flash MX 2004 的 `0x38` 或 Flash 8 的 `0x3F`；拒绝 CS3/CS4、ZIP/XFL 及改扩展名的 SWF。游戏目标是 Win32 RA3 APT。两份用户附件均为 `0x38`。

已实现矢量填充、孔洞、曲线、位图、Flash 8 渐变的贴图材料、时间轴/图层/蒙版、元件/按钮、常规运动与形状补间及文本。静态文字轮廓使用 Windows 字体，所需字体必须已安装；动态文字保留运行时字体名。

尚未支持 Flash 滤镜/混合模式、九宫格、引导线运动、预编译组件、嵌套 SWF、嵌入音视频及部分过程化描边。遇到这些能力会失败，不会把转换缺项当成成功。此插件不等同于覆盖 Flash 8 编辑器的所有发布功能。

## 构建与验证

```text
dotnet build ui/BinaryAssetBuilder.UiCompiler.csproj -c Release -f net472
dotnet run --project ui/tests/UiCompiler.Tests.csproj -f net8.0
```

独立插件部署使用 `ui/bin/Release/net472/` 的插件、LibTessDotNet、Clipper2Lib 和 StbImageSharp，Core 由 SDK/宿主提供。NewModCompiler 的正式构建将 UI 源码编入 EXE，三个固定依赖作为资源嵌入，部署仍为原来的五个文件。`net8.0` 用于跨平台组件测试；字体轮廓测试需要 Windows。`ui/build/Verify.ps1` 运行独立插件验证并在 `ui/artifacts/UiCompiler.zip` 打包部署文件。

最终资产编码依据附件工具对应的 SWF2APT 运行时布局；资产布局、类型哈希、重定位和 v6 四流逻辑依据指定 recovery 分支。固定来源、许可及移植修改见 [VENDOR.md](VENDOR.md)。自动化验证不能代替游戏内 UI 验收。

回归测试直接解析最终 AssetBuffer 并执行其中的 AS 指令、检查几何孔洞/描边面积与四流布局；62 个最终资产的 SHA-256 与删除中间格式前的基线逐一比对。产品程序集不再包含 SwfLib、SWF 读写器或 SWF→APT 转换器。
