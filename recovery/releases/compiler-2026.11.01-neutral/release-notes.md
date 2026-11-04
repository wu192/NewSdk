# NewModCompiler 中立地编修复版

发布标签：`compiler-2026.11.01-neutral`。本版使用独立新标签，保留已有 `compiler-2026.11.01` Release。

本版加入中立/地编资产处理链修复及定向验证脚本，支持在 MOD 的 `newmodcompiler.json` 或编译器 `NewModCompiler.exe.config` 的 `appSettings` 中设置 `fix-neutral-assets`，命令行可覆盖。默认关闭；启用后在包含 base 的 BIG 打包副本中执行 HashFix → ModAssetResolver → LoD，并补入引用的官方 Worldbuilder 资产。BIG 的 `--mod-version` 仍须每次在命令行显式指定。

官方 WorldBuilder 1.12 参考数据作为独立 ZIP 提供，解压到现有 SDK 根目录即可。156 个独立压缩块完整保留官方 15,931 资产，新编译器按需读取；不要求先还原 1.3 GB `.bin`。官方 manifest 与原仓库文件相同，完整还原的原始数据 SHA256 已核对一致。

## 下载与安装

- `NewModCompiler-Win32-Release.zip`：日常使用，只有 EXE、EXE.config、antlr.runtime.dll 三个运行文件。
- `NewModCompiler-Win32-Debug.zip`：排查问题，保留该包完整的三个文件。
- `RA3-Worldbuilder-1.12-References.zip`：解压到 SDK 根目录，使数据位于 `builtmods/sagexml/`；保留原有 SDK 参考文件。
- `USAGE.zh-CN.md`：配置优先级、安装目录和编译命令。

目标机器需要 Windows、.NET Framework 4.8 与匹配构建工具链的 x86 Visual C++ 运行库。SDK 和 MOD 源码另备。

参考包：500,207,147 字节，SHA256：

```text
58a3f59dbd6b2e726bffd51ce42197ca04ee5ff0434a99a556d81cd5b7455c9a
```

## 验证记录

生产源码：`f74da90fd8461fcb983acebcb694d66aeb8dbfdd`。

[Windows 验证运行 41319194413](https://github.com/wu162/NewSdk/actions/runs/41319194413) **整体通过**：Debug/Release 新构建，各1879项 App 合同、10组原版 HashFix/Resolver 差分、12组真实 Core 合同及31个 CLI 用例通过。两资产小 MOD 各8个用例通过，真实修正1项哈希、递归补入5个官方资产，12个 BIG 成员均与原版 HashFix→Resolver→LoD→MakeBig 原始字节一致。两份三文件 ZIP 均独立解压并完成冷编译，BIG/SKUDEF 与已验收产品相同。运行文件与 ZIP 的 SHA256 见 `compiler-manifest.json` 和 `SHA256SUMS.txt`。

MOD 源码和 SDK 参考的字节及修改时间保持不变。历史合成 LoD 的原工具越界差异按已登记的确定性修复合同保留，不宣称这些已知差异逐字节相等。源码及官方参考数据已保存在 `fix/neutral-worldbuilder-config`，本版已带入指定 harness 分支的修改。

验证范围不包含完整大型 MOD 或游戏内运行。未完成游戏内验证。
