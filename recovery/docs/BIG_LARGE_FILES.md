# BIG 中立资源修复超过 2 GiB

修复源码/测试：`788566c07449eb0e223ad21e64bc28d15c3d8c56`，基于用户指定的
`fix/neutral-worldbuilder-config` / `9a1c93a1`。[PR #50](https://github.com/wu162/NewSdk/pull/50) 待审，未合并。

中立资源修复原先对 MOD `.bin/.imp/.relo` 执行 `File.ReadAllBytes`，随后通过
`MemoryStream` 和 `ToArray` 再复制载荷。原代码在实际 resolver 调用处理
2 GiB + 4 KiB 文件时复现 `IOException: ... limited to supporting files less than 2 gigabytes`；
x86 下还会因多份载荷分配提前耗尽内存。BIG 写入器本来已经流式写入。

现在只保留待插入资产的索引，按原递归顺序写入原 4 字节 checksum、WorldBuilder
资产片段与原 MOD 载荷。普通文件按 64 KiB 复制；压缩 WorldBuilder 仍使用原分块缓存，
不累积所有插入数据。无资产插入时校验长度并保留原载荷。写入支持取消，并清理失败的临时文件。

[Windows x86 快测 41433492176](https://github.com/wu162/NewSdk/actions/runs/41433492176)
整体 success，Debug/Release 均通过：

- 原 App 合同（各 1901 断言），包括跨压缩块插入、失败/取消与包处理。
- 实际 `ModAssetResolver.Apply` → `BigArchiveWriter.Write`，使用 2 GiB + 4 KiB
  稀疏载荷；覆盖无插入、有插入、checksum、首尾/跨 2 GiB 内容、BIG 长度和后续成员偏移。
- 原 HashFix/resolver 的 10 个小用例逐字节对照，包含递归插入、重复引用和纹理解析。

该合成大文件用例 Debug 12.785 秒、峰值工作集 15,257,600 字节；Release 12.430 秒、
16,285,696 字节。它不是大型 MOD 性能测试；未运行全量 SDK 或游戏验证。
本地 Mono x64 同一大文件用例通过；完整 App 合同因 Windows 专用 `CreateHardLink` 无法在 Linux 完成，以上述 Windows 结果为准。

回归宿主：[LargeBigTests.cs](../tests/large-big/LargeBigTests.cs)，
重跑入口：[big-large.yml](../../.github/workflows/big-large.yml)。
[验收摘要及输入身份](../evidence/big-large/41433492176-1/summary.json)记录源码与测试来源；
[原始日志/对照产物](https://github.com/wu162/NewSdk/actions/runs/41433492176/artifacts/13135212220)
已下载并核对归档 SHA256。没有放宽 BIG 原有的 uint 格式上限。

## 修复版编译器运行包

按用户追加要求构建并打包。[下载 Release/Debug 运行包](https://github.com/wu162/NewSdk/actions/runs/41435748800/artifacts/13137145304)，
下载归档根目录含下面两个 ZIP 与 `manifest.json`；每个运行 ZIP 严格包含
`NewModCompiler.exe`、`NewModCompiler.exe.config`、`antlr.runtime.dll`。

| 配置 | ZIP 字节数 | SHA256 |
| --- | ---: | --- |
| Debug | 2104124 | `a6159704e64c2c929f2ef4f807fd60440713ede8b5d72d79eed2acc774a8cc53` |
| Release | 1609940 | `7e78a123467d4758f5bb2baa1bc213a15fd689249eae559b153dce5e92d1ab96` |

二进制来源仍为 `788566c0`，原运行 `41434055130` 的两配置构建及 Core 通过。
该运行随后因稀疏检出的历史 UI blob 缺失、无凭据延迟下载失败而终止；保留 failure。
已移除常规打包工作流的稀疏检出；一次性重放使用完整历史，核对 70 个应用源码和 EXE
身份后复用已有产品/Core，未重复编译或放宽 CLI 断言。首轮重放因 PowerShell 保留变量名失败，修正后保留该终态。

[重放 41435748800](https://github.com/wu162/NewSdk/actions/runs/41435748800)整体 success：
真实 CLI 成功/失败/取消/重试、官方中立小 MOD 与原工具 BIG 字节对照、最终 ZIP
独立解压冷编译及 BIG/SKUDEF 对照通过。已下载小交付归档与完整验收归档，核对各归档、
两运行 ZIP、三个成员和修复源码身份；交付包与验收包逐字节相同。
[清单](../evidence/big-large/41435748800-1/manifest.json)、
[准确来源和范围](../evidence/big-large/41435748800-1/summary.json)。
一次性重放工作流已移除，固定执行配方保留在 `0f378398` 历史中；未发布 GitHub Release，未合并 PR。
