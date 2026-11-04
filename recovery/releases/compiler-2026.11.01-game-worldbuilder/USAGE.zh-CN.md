# NewModCompiler：自动读取游戏目录的地编数据

适用标签：`compiler-2026.11.01-game-worldbuilder`。需要 Windows、.NET Framework 4.8 和匹配构建工具链的 **x86 Visual C++ 运行库**；64 位 Windows 也需 x86 运行库。

## 安装

通常选择 `NewModCompiler-Win32-Release.zip`；Debug ZIP 用于排查问题。完整解压所选 ZIP，保留三个文件：`NewModCompiler.exe`、`NewModCompiler.exe.config`、`antlr.runtime.dll`。

SDK 和 MOD 源码另备。本版不附带 500 MB 官方参考包。启用中立地编修复时，可使用现有 SDK 中的解包参考文件，也可从已安装地编数据的游戏目录读取 `Data\WBData_12.big`。**这个 BIG 来自 SDK/WorldBuilder 地编数据的安装，普通游戏安装不保证包含它。** 仅指定游戏目录不能补齐实际缺失的 BIG。

## 自动发现与手动指定

未指定 `game-root` 时，编译器优先使用完整的现有 SDK 参考；缺失时从 Windows 注册表的 Red Alert 3 安装项读取 `Install Dir`，并检查其下 `Data\WBData_12.big`，兼容 32/64 位注册表视图。显式指定 `game-root` 时使用所选游戏目录的 BIG 准备参考。

自动发现不到时，可显式指定游戏安装根目录：

```powershell
./NewModCompiler.exe build --sdk-root "C:\NewSdk\SDK" --mod "C:\Mods\Example" --out "C:\Build\Example" --game-root "C:\Games\Red Alert 3" --package-content base --package-format big --mod-version 1.0 --fix-neutral-assets=true
```

`--game-root` 指向包含 `Data` 子目录的根目录，不是 BIG 文件本身。编译器只读原 BIG，将必要参考缓存放入当前 SDK 的 MOD 专属 `SuperCompilerCache` 分区；源路径、长度或修改时间变化后重新准备，后续编译复用缓存。不会把解包结果写回游戏 Data 或 SDK 原参考目录。

## 保存配置

在 MOD 根目录的 `newmodcompiler.json` 中保存：

```json
{
  "package-format": "big",
  "fix-neutral-assets": true,
  "game-root": "C:\\Games\\Red Alert 3"
}
```

`game-root` 可省略，使用自动发现。也可在编译器旁 `NewModCompiler.exe.config` 的现有 `<configuration>` 中修改：

```xml
<appSettings>
  <add key="fix-neutral-assets" value="true"/>
  <add key="game-root" value="C:\Games\Red Alert 3"/>
</appSettings>
```

保留原有 `startup` 等节点，已有 `appSettings` 时只修改或添加对应键。配置优先级为：**命令行 > MOD 项目 JSON > EXE appSettings**。`fix-neutral-assets` 出厂默认 `false`；命令行 `--fix-neutral-assets=false` 可临时关闭。显式 `game-root` 覆盖注册表自动发现的目录。

修复用于包含 `base` 的 BIG。BIG 的 `build` 和 `plan` **每次必须在命令行显式提供 `--mod-version`**；项目预设不能代替。编译 loose 或仅有 APT/地图元数据时应关闭修复，否则报告 `PACKAGE_NEUTRAL_CONTENT`。输出目录须与 SDK、MOD 输入目录分离；成功产物位于输出根的 `published` 目录。

## 验证范围

本版使用 runner 中模拟游戏目录布局的小型 MOD 检查自动参考准备和打包行为。未在本地安装并启动 Windows 地编，也未完成游戏内验证。最终构建与测试结果以 Release 附带的验证元数据为准。
