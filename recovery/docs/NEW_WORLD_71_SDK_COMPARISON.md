# The New World 7.1 新旧 SDK 编译性能

TGA与8声道WAVE修复已通过组件/小CLI验收，并按用户授权通过[PR#43](https://github.com/wu162/NewSdk/pull/43)以 `a15e3c1e` 合入 `benchmark/new-world-71-sdk`。
[修复后同机性能41261694766](https://github.com/wu162/NewSdk/actions/runs/41261694766) 已完成：两引擎全部15流退出0，各4058文件；总墙钟1921.681→410.182秒，减少78.66%，约4.68倍。**整体状态为 outputs-differ，不能称为全输出原字节等价。**

| 完整冷编译部分 | 原 SDK 秒 | 修复版秒 |
| --- | ---: | ---: |
| 基础数据1流 | 1872.394 | 378.099 |
| 全部APT13流 | 33.800 | 11.480 |
| 全局数据1流 | 15.487 | 13.768 |
| 总计 | 1921.681 | 410.182 |

总计即32分01.681秒→6分50.182秒。原版逐流启动15个串行BAB进程；新版单CLI jobs4。总计均从进程启动到退出；新版分项用正常StreamStarted/Completed单调时钟，6.835秒共享启动/收尾开销仅计总计。下载、解压、哈希和字节比较不计入。新旧缓存/输出独立且最初不存在，先测新版再测原版；OS页缓存未控制。每引擎仅1个cold样本，不推断统计显著性，loose不含BIG/SKUDEF。

CPU总计1917.766→743.656秒；峰值工作集1069.891→930.980 MiB；缓存1489029327→1788864322字节（增加20.14%）。机器为4逻辑CPU Windows2022、AMD Family25 Model1，Image20261026.347.1。相同物理MOD路径、搜索根顺序及Mod自带xsd；SDK、输入、运行文件、搜索根存在性均未变化，Schema结束后完整还原。

此次双方使用原始贴图、音频和全部编译入口，仅移除89个不支持的Joypad声明，限2份XML；无TGA头规范化，无AudioFile/AudioEvent/Texture剔除。真实8声道 `FX_kanade_spell` 的397648字节XAS CDATA，两方SHA256均为 `892b3c9b00929a2adb28d88d8c455401ebac72d82313b7cb878c54851a75fd67`。源码仍在本地 `SDK/Mods/The New World`，原始归档与备份保留，源包SHA256为 `0c59ed425b1ab55d926bfec858c6f60a0186a5d46e4cb5b295e42891a5ecdb41`。

仅 `base/mod/mod.bin` 不同，另外4057个文件长度/SHA256相同；归档全部60个链接流直接比较59个原始字节相同。按共同manifest定位17712个基础资产，330个不同资产全部为DXT1 Texture，Texture根和128字节DDS头相同，仅BC1块负载不同，共59846863字节。该资产集合和差异字节数与修复前完全一致；DXT3/5、raw纹理、其他全部资产（含恢复的媒体）、音频，以及APT/global四流/CDATA身份相同。此为[已登记BC1编码边界](../src/Native/Texture/PublicS3/PATCHES.md)，没有归一化或忽略差异后宣称等价，也未声称游戏内/视觉等价。

本次实际复用生产 `24f83d2b` / [组件验收41260620635](https://github.com/wu162/NewSdk/actions/runs/41260620635) Release，EXE SHA256为 `5aa008d05ca4288265f7b51a15b09c01a1d10be305fbdf9616ec4946468cd4db`；三运行文件和生产闭包核对一致，没有为计时重建。计量源为 `299286c5`。组件验收包含原图246个正例/9个边界、原三路/31格式、D/R每配置4347条音频原转录、100次独立EALayer3解码及52资产小CLI冷热/改WAV/独立重编。用户要求不为codec额外编译整个MOD或mymod；本轮全部15流只用于已授权的性能计量，无额外完整MOD预检。

[本轮摘要与CPU/内存/缓存/运行身份](../evidence/refactor/new-world-71/41261694766-1/summary.json)、[逐项CSV](../evidence/refactor/new-world-71/41261694766-1/cold-comparison.csv)、[330个BC1资产差异](../evidence/refactor/new-world-71/41261694766-1/base-asset-differences.json)、[89条调整清单](../evidence/refactor/new-world-71/41261694766-1/source-adjustments.json)、[原始归档](https://github.com/wu162/NewSdk/actions/runs/41261694766/artifacts/13067086051)。归档SHA256 `1c497e945be89d53f6a645fefb0457aedfdd2b76fbadad46079aebd715a73343` 已下载复算。

历史supported94基线 [41256811133](https://github.com/wu162/NewSdk/actions/runs/41256811133) 用8050f22d，双方裁剪94声明并规范化234个APT头，各4057文件；同机1457.887→286.933秒（5.08倍），亦仅mod.bin的相同330个BC1资产不同。[旧摘要与原始记录](../evidence/refactor/new-world-71/41256811133-1/summary.json)完整保留。它与本轮VM和输入不同，不能拼算修复前后速度变化。

完整MOD热缓存的历史失败 [41255726294](https://github.com/wu162/NewSdk/actions/runs/41255726294) 为AlliedLegionnaireInfantry→BaseInfantry继承源XML缺失，本次未改Core、未作完整热态或编辑速度比；小型codec CLI热态通过不冒充整个7.1热缓存通过。PR#42向legacy分支的合入未获用户授权。
