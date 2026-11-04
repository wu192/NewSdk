# SDK 集中缓存

缓存由本次 `--sdk-root` 和物理 MOD 路径决定，统一位于
`<sdk-root>/SuperCompilerCache/<ModKey>/`。`ModKey` 由清理并截短至 12 字符的 MOD
目录名，以及规范化物理 MOD 根路径 SHA-256 的前 16 字节组成。计算 ModKey 时只散列路径字符串，
不扫描或散列 MOD 文件。同一个 MOD 更换输出目录、版本或构建时间不会更换分区；
同名但物理路径不同的 MOD 各自隔离。

地编参考数据为SDK共享：`<sdk-root>/SuperCompilerCache/shared/neutral-worldbuilder/` 保存从游戏BIG提取的官方四流，
所有Mod复用同一份，不按Mod复制1.33GB。`shared/neutral-worldbuilder.lock`覆盖提取及参考消费，
避免不同Mod并发首次提取或切换来源时混用数据；等待可取消。普通Mod编译缓存仍按下表隔离。

| 分区内路径 | 内容 |
| --- | --- |
| `session/` | 原 BAB SessionCache XML/Deflate、`.tmp/.old`、既有字符串哈希与 `.NewModCompiler/cdata` |
| `build-cache/Win32/` | 原类型、实例键的 `.asset/.cdata` |
| `intermediate/` | 按逻辑源相对路径生成的中间资产 |
| `streams/<base\|apt\|maps>/<entry>/` | 可复用四流、CDATA 和任务期间的 `backend-work` |
| `generated/fla/` | 裸 FLA 的稳定 XML 声明 |
| `temp/<BuildId>/` | 打包 LoD、APT version 和显式中立修复输入，打包后清理本 BuildId |

整个请求共享一个不可变 `CacheLayout`。SDK 配置中的 `buildCacheRoot` 不决定缓存
落点；`/bc` 未显式指定时仍采用 SDK `buildCache` 开关。`/sc`、`/bc`、`/csc`、
`/psc`、`/fsc`、`/atc`、`/ss`、`/pc`、`/slowclean` 的原行为与 BAB 格式保留。

MOD 源码在计划、编译、失败清理及打包期间只读，`newmodcompiler.json` 只读取。
MOD 位于当前 SDK 的 `Mods/` 内也适用。`--out` 及诊断文件继续必须位于 SDK/MOD
源码目录外；SDK 内允许的编译写入目标为当前 MOD 分区，另有上述地编参考共享目录。物理路径别名、重解析点、
目录权限及 Win32 路径限制仍须通过原路径边界；失败不回退到其他缓存位置。
`plan`、`doctor` 只解析和检查布局，不创建缓存目录。

`/scd`（`/scp`）、`/bcd`（`/bcp`）、`/iod` 的命令行值和项目预设值都只允许
指向当前分区内部，否则报 `CLI_CACHE_PATH`。旧 `--out` 下遗留的会话、中间资产、
四流和 `.NewModCompiler` 不读取、不迁移、不自动删除；新布局允许首次冷构建。
需要清理缓存时，应先结束该 MOD 的构建，再清理其分区；更换 `--out` 不产生冷缓存。

BIG 直接读取本轮确认的缓存流，`published` 仅写最终 `.big/.skudef`；日志、事件和
报告位于输出根或合法诊断位置。loose（包括 development 派生格式）在 SDK 分区内编译，
只把本轮明确产出的四流/CDATA 导出到 `published/<content>/<entry>/...`，导出文件
独立于编译缓存。最终报告的 `artifacts` 列出交付路径。BIG build/plan 仍必须每次在
命令行显式提供 `--mod-version`。

同一 MOD 的分区锁覆盖完整 build：编译、Core 释放资源，直到 BIG 打包或 loose
导出结束。第二个并发 build 立即以 `CACHE_BUSY` 拒绝，不等待；不同 MOD 的分区
互不锁住。原会话文件锁与分区锁分别持有，不在 Core 释放资源时提前放开打包保护。
中立地编后处理另外持有SDK共享参考锁；它只串行化参考提取/读取阶段，不锁住其它Mod的普通编译。

裸 FLA 的合成目录只在该 FLA 流中优先作为 DATA 根，保持 `DATA:<movie>.xml`
逻辑源名；普通 MOD 的 DATA 搜索顺序不变。Additional 继续直接读取当前 SDK/MOD
输入，不在源码目录生成副本；不新增 BIG 包缓存或整代发布暂存。

本次按用户追加授权使用 mymod 与 Upheaval 做定向核验，没有运行完整大矩阵。
生产 `f8e8f4a0` 完成一次成功的 Release 构建；后续只复用同源 EXE，并核对生产输入闭包、
EXE 收据与 config/ANTLR 内容身份。首轮链接异常及观测器修复的失败记录均保留。

[最终 Windows 核验 41275643703](https://github.com/wu162/NewSdk/actions/runs/41275643703) 通过：

| 输入 | 冷态 XML 解析 / 资产编译 | 换 out 的热态 | 交付 |
| --- | --- | --- | --- |
| SDK 内完整 mymod，base | 11 / 47 | 0 / 0 | loose 4 文件；BIG/SKUDEF 2 文件 |
| SDK 外完整历史 Upheaval，base | 910 / 3235 | 0 / 0 | loose 1295 文件；BIG/SKUDEF 2 文件 |
| 小型 flash8-empty 直接 FLA，apt | 1 / 3 | 0 / 0 | loose 4 文件，manifest 无私有缓存路径 |

三个 MOD 的瞬时写事件为 0；SDK 输入按根目录与既有输入子树共 7 个监听边界完整监视，
避免合法缓存流量挤爆输入监听缓冲区。越区事件、监听溢出与 SDK 输入字节/mtime/属性差异均为零。
冷热 loose 原字节一致，BIG 输出只有正式包，打包临时输入清理完成。plan 无缓存创建、BIG
缺 CLI 版本拒绝、切输出复用分区均通过。1846 项 App 合同、全部 PowerShell 脚本语法与
73 项相关 Python 小测试通过；详见 [收据](../evidence/refactor/supercompiler-cache/summary.json)。

未实测本轮迁移的原 SDK 全字节差分、完整 APT/maps 与 FLA 样例集、完整缓存开关/失效/真实取消
矩阵或游戏运行。既往验收不冒充本次重新执行的结果。
