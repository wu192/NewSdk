# NewModCompiler 中立地编修复版使用说明

适用版本：`compiler-2026.11.01-neutral`。需要 Windows、.NET Framework 4.8 和与编译工具链匹配的 **x86 Visual C++ 运行库**；64 位 Windows 也需安装 x86 运行库。

## 安装

1. 通常选择 `NewModCompiler-Win32-Release.zip`；Debug ZIP 用于排查问题。将所选包解压到独立工具目录，保留全部三个文件：`NewModCompiler.exe`、`NewModCompiler.exe.config`、`antlr.runtime.dll`。两个配置使用各自完整的一套文件。
2. 准备现有 SDK 与 MOD 源码。编译器三文件 ZIP 不包含 SDK。
3. 将独立附件 `RA3-Worldbuilder-1.12-References.zip` **解压到 SDK 根目录**，使数据位于 `SDK/builtmods/sagexml/`，保留现有 Static、Global、Audio 参考数据。`Worldbuilder.bin.parts` 保持原样，新编译器直接按需读取，无需还原成大 `.bin` 或安装 WorldBuilder。

在 PowerShell 的工具目录检查环境：

```powershell
./NewModCompiler.exe doctor --sdk-root "C:\NewSdk\SDK"
```

## 启用中立地编修复

出厂默认值为 `false`。在 MOD 根目录创建或编辑 `newmodcompiler.json`：

```json
{
  "package-format": "big",
  "fix-neutral-assets": true
}
```

也可在编译器旁 `NewModCompiler.exe.config` 的现有 `<configuration>` 中修改：

```xml
<appSettings>
  <add key="fix-neutral-assets" value="true"/>
</appSettings>
```

保留现有 `startup` 等节点，已有 `appSettings` 时只修改该键。优先级为：**命令行 > MOD 项目 JSON > EXE appSettings > false**。命令行 `--fix-neutral-assets=true` 可启用，`--fix-neutral-assets=false` 可临时关闭配置中启用的修复。

## 编译

```powershell
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" --package-content base --package-format big --mod-version 1.0 --fix-neutral-assets=true
```

输出目录须与 SDK、MOD 输入目录分离。**BIG 的 `build` 和 `plan` 每次必须在命令行显式提供 `--mod-version`**，项目配置中的版本不能代替。成功产物位于输出根的 `published` 目录，明细见 `build-report.json`。

修复适用于包含 `base` 的 BIG：在私有打包副本中执行 HashFix → ModAssetResolver → LoD，修正中立/地编资产身份，并递归补入 MOD 引用的官方 Worldbuilder 资产。缺失参考数据报告 `PACKAGE_NEUTRAL_REFERENCE_MISSING`。编译 loose 或仅有 APT/地图元数据时应关闭此开关，否则报告 `PACKAGE_NEUTRAL_CONTENT`。

本版不代表已完成游戏内验证；小型 MOD 的编译和原工具字节对照结果以 Release 验证记录为准。

## 官方参考包身份

`RA3-Worldbuilder-1.12-References.zip`：500,207,147 字节。

```text
SHA256 58a3f59dbd6b2e726bffd51ce42197ca04ee5ff0434a99a556d81cd5b7455c9a
```

来源及官方四流身份见包内 `builtmods/sagexml/Worldbuilder.provenance.json`。
