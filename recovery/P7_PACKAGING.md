> 当前路径与交付方式见 [集中缓存说明](docs/SUPERCOMPILER_CACHE.md)：BIG 从 SDK 分区读取流，Additional 直接读取输入，打包工作文件仅位于本 BuildId 的缓存临时目录。本页的旧 staging、SHA 快照及发布事务描述仅属历史阶段。

# P7：内建 BIG/SKUDEF

状态：APT/maps 的三字节版本标记修正与五文件发行包已通过两配置验收。当前新候选已接入默认 base LoD、显式 Additional、可选中立资产修复和从原 C# MakeBig 恢复的代码，生产构建、Core/CLI及全部缓存独立通过；固定同一生产五文件的P7/ZIP定向验收已在Debug/Release全部通过。新范围为默认base/apt/maps、显式Additional及中立修复零插入链，不泛称完整游戏后处理。P5/P6 既有验收保持独立。

```powershell
./NewModCompiler.exe build --sdk-root C:\NewSdk\SDK --mod C:\Mods\Example --out C:\Build\Example --package-content base,apt,maps --package-format big
```

正常编译直接生成 manifest/bin/imp/relo 与 CDATA。原脚本 step5 在未启用可选修复时直接运行 LoD；HashFix→ModAssetResolver→LoD 不是默认必需链。**2026-10-27 用户确认不需要 AssetMerge：不纳入新流程，也不作为 P7 完成条件。** 已保存的独立恢复准备不代表产品需要或启用了它。

成功非空构建只发布 `<MOD目录名>_<mod-version>.big` 与 `<skudef-name>_<mod-version>.skudef`。默认 mod-version 为1.0，skudef-name为MOD目录名，game-version为1.12；可用同名长参数或项目配置更改，规则见 `CLI_COMPATIBILITY.md`。SKUDEF 保持 ASCII/CRLF 的 `mod-game <game-version>`、`add-big <包名>`；游戏侧 Unicode 名称合同仍未启用。原 MakeBig 本身的 UTF-8 行为另见 `MAKEBIG_RECOVERY.md`。

| 本次产物 | BIG 内路径 |
| --- | --- |
| base 四输出与 CDATA | `data/mod.*`、`data/mod/cdata/*.cdata` |
| base 派生 LoD 八输出 | `data/mod_l.*`、`data/mod_m.*`，内嵌依赖精确保留原参数 `mod.manifest` |
| APT 四输出与 CDATA | `data/aptui/<流名>.*`、`data/aptui/<流名>/cdata/*.cdata` |
| APT 派生版本 | `data/aptui/<流名>.version`，原命令实测 `20 0d 0a` |
| maps 四输出与 CDATA | `data/additionalmaps/<流名>.*`、`data/additionalmaps/<流名>/cdata/*.cdata` |

默认成员仅来自本次已确认的流产物清单；不扫描陈旧输出、中间文件或缓存。base 不派生 `.version`。空可选集合保持上一代，不生成空 BIG。

BIG 默认自动包含 Additional，可用 `--include-additional=false` 关闭；显式 loose 不自动包含，`--include-additional=true` 仍只允许 BIG：按原 step8 先 SDK/Additional、后 MOD/Additional 递归覆盖；相对路径进入 BIG 根，`.asset` 按原 MakeBig 规则排除。同名文件可覆盖本次编译结果或 APT 版本，这是原脚本的覆盖行为，记录 `ADDITIONAL_OVERWRITE` 警告。所有输入先快照到本次 staging 并核对 SHA256，不改原 SDK/MOD/流文件；之后仍走已有发布事务。项目配置可使用布尔 `include-additional`。

`--fix-neutral-assets=true` 默认关闭，只允许选中 base 的 BIG 构建。先在独立包工作区复制主流四输出及 CDATA，再执行恢复的 HashFix→ModAssetResolver，最后派生 LoD；原 Core/缓存及 SDK 输入不变。编译前预检必须的参考文件，缺失时以 `PACKAGE_NEUTRAL_REFERENCE_MISSING` 拒绝并保留上一代。HashFix 使用 SageXml Worldbuilder manifest；Resolver 优先完整 builtmods 根四件，根完全不存在时才使用完整 SageXml 四件，不拼接两代。当前不访问游戏安装注册表，也未实现从游戏 `WBData_12.big` 提取；需要已有完整 Worldbuilder 四输出。实际 CLI 验收用自包含参考验证哈希确实修正、零插入链、缺失/恢复；真正插入由独立组件原始差分验证，不扩大为完整游戏中立资产验收。

源码来源：HashFix 根据 SDK 原 EXE 的行为恢复并做原程序差分；ModAssetResolver 根据 SDK 自带的 Bibber Delphi/Pascal 源码 `SDK/tools/Source/ModAssetResolver.dpr` 移植为 C#，并与原 EXE 差分。两者均已编入生产 EXE。

2026-10-27 用户取消 WBData 相关规划：不再安排大包下载、提取或读取器开发，也不安排依赖真实 WBData 的完整游戏中立资产插入验收；这些不作为本轮重构完成条件。已有可选修复实现保留，仍要求用户环境已有完整参考文件，缺失时明确拒绝。取消规划不改变已有验证范围，不将未做的真实数据验证标记为通过。

生产路径为 `BigPackage → Application.MakeBig → BigArchiveWriter` 严格清单/边界/原子保护 → **恢复的原 MakeBig.Lumpy/FixupBinaryWriter**。保持 BIG4、原端序、NUL 路径、L219 尾和连续 payload；不启动 MakeBig 或编译 worker。原目录发现/参数解析保留在恢复源码供差分，但生产只消费明确清单。

`TestCompilerBig.ps1` 保留 APT/maps 原组，新增 base、完整组合、真实 base CDATA、显式 Additional、原 CopyFolders/LoD/MakeBig 原始内容对照，以及取消/发布故障/旧代替换和重试。原工具目录遍历顺序可能变化，跨目录归档比较成员内容；同输入目录 MakeBig 组件仍比较整包字节。原工具只在隔离验收中启动。

原 LoD 在特定来源表长度上有已复现的末字节差异，详见 `P7_LOD_ORIGINAL_TAIL.md`：原始失败及重复探针保留，不能把其他输入的成功范围泛称完整 LoD/修复链通过。生产正确表复制未按异常样本盲改，也未放宽原始字节断言。

候选真实主组通过后，按成功 source/run/archive 重 pin 发行准备器；独立解压五文件 ZIP 必须再通过 base/Additional BIG 和协议验收。先前已验收的包不会自动包含本候选。包版本/名称参数已接入；LoD采用明确的完整逻辑表复制合同，不复刻原工具越界尾字节。可选中立资产修复与完整 UI/runtime 分别记录范围，不启动游戏、安装 MOD 或写 CurrentMOD.ini。

布尔选项的 CLI 写法是 `--include-additional=true`、`--fix-neutral-assets=true`（也支持单独裸开关启用），不是空格后另传 `true`。结果主线 `compiler-main` 已修正新布尔选项的 help 示例；最终新源发行构建与验收以 `COMPILER_RELEASE.md` 记录为准。
