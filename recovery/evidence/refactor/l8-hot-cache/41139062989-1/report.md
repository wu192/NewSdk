# L8 热缓存性能报告

**状态：`passed`；组别：common；累计 18.06 分钟。**

测试提交：`a286062ffcde48c097ecc8db6fb7f598dfdb0137`；Release 来源：`cc10391be67dda57c7efa7fc06256b99e9372d9a`；EXE SHA256：`882fec0b34565230c38347f3cc0c0646a5e36cd4ffb31ac5056a01d2752e9ac0`。

正式成绩仅采用 performance=off、正常退出、目标路径已执行，且单次 wallMs 与编译器 elapsedMs 均严格 >8000 的样本。每次启动独立进程；准备、快照恢复、预热和报告均计入总预算。OS 页缓存未受控，样本数较少。

L8 为完整固定版本 Upheaval 加合法扩容分区的混合性能负载，区别于原始未修改 Upheaval。

L8 输入库存：2753 文档、54653 声明、297.18 MiB；实际计划 5 个流；发布 103.79 MiB。
L8 指纹：`d893b3ac0deeabed72b63e655d81cb92cfca41a2ec564bff81b920931ff16cf2`。原始 Upheaval 库存：954 文档、3852 声明、286.96 MiB，指纹 `c41fdfd1d72f340351fba7b2828e92c33a0d63ff562ddc8b9962d2be002497f7`。

有限标定轮次的最终热态最低值为 9.20 秒，尚未达到约 10–14 秒的调参目标；该目标与严格 >8 秒的成绩门槛分开记录，正式样本仍逐条核验。

Full 扩展预算不可达：28 个条件、168 个正式进程的严格理论下限 >1344 秒，超过 960 秒阶段预算；保持双重复与修改幅度时不能声称 Full 完成。

## 场景完成程度

| 场景 | 状态 | 说明 |
| --- | --- | --- |
| C00 | complete |  |
| C01 | complete |  |
| C02 | complete |  |
| C03 | complete |  |
| C04 | complete |  |
| C05 | complete |  |
| C06 | complete |  |
| C07 | complete |  |
| C08 | complete |  |
| C09 | complete |  |
| C10 | complete |  |
| C11 | complete |  |
| C12 | complete |  |
| F01-delete | not_selected |  |
| F01-rename | not_selected |  |
| F01-move | not_selected |  |
| F01-entry | not_selected |  |
| F02 | not_selected |  |
| F02-depth | not_selected |  |
| F03-independent | not_selected |  |
| F03-shared | not_selected |  |
| F04-independent | not_selected |  |
| F04-shared | not_selected |  |
| F05 | not_selected |  |
| F06 | not_selected |  |
| F07-session-partial | not_selected |  |
| F07-session-all | not_selected |  |
| F07-assets | not_selected |  |
| F07-linked | not_selected |  |
| F09-move | not_selected |  |
| F09-mtime | not_selected |  |
| F09-same-stamp | not_selected |  |
| F09-shadow | not_selected |  |
| F09-entry | not_selected |  |
| F10 | not_selected |  |
| F12 | not_selected | 未提供合法可运行且确实改变身份的另一版本，不模拟身份哈希 |
| F08-base-Mod | not_selected |  |
| F08-apt-TacticalHUDBuildQueuePage | not_selected |  |
| F08-apt-TacticalHUDCommandListBox | not_selected |  |
| F08-apt-TacticalHUDMainCommandBar | not_selected |  |
| F08-maps-MapMetaData_Global | not_selected |  |
| F08-cross | not_selected |  |
| F11-mode-none | not_selected |  |
| F11-mode-asset | not_selected |  |
| F11-mode-session-xml | not_selected |  |
| F11-mode-session-deflate | not_selected |  |
| F11-mode-both-xml | not_selected |  |
| F11-mode-both-deflate | not_selected |  |
| F11-jobs-1 | not_selected |  |
| F11-jobs-4 | not_selected |  |
| F11-jobs-auto | not_selected |  |

## 三阶段耗时

| 场景 / 缓存 / jobs | 热前 n / 中位 ms | 修改 n / 中位 ms | 再热 n / 中位 ms | 首改额外 ms | 再热变化 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| C00 / both-deflate / auto | 3 / 9342.1 | 0 / — | 0 / — | — | — |
| C01 / both-deflate / auto | 2 / 9351.2 | 2 / 12215.2 | 2 / 9364.4 | 2864.0 | 13.2 |
| C02 / both-deflate / auto | 2 / 9377.6 | 2 / 12206.4 | 2 / 9396.2 | 2828.9 | 18.6 |
| C03 / both-deflate / auto | 2 / 9357.3 | 2 / 12174.5 | 2 / 9407.1 | 2817.2 | 49.8 |
| C04 / both-deflate / auto | 2 / 9361.5 | 2 / 12094.5 | 2 / 9305.1 | 2733.1 | -56.4 |
| C05 / both-deflate / auto | 2 / 9390.7 | 2 / 12068.8 | 2 / 9334.6 | 2678.1 | -56.1 |
| C06 / both-deflate / auto | 2 / 9428.3 | 2 / 12290.2 | 2 / 9347.1 | 2861.9 | -81.2 |
| C07 / both-deflate / auto | 2 / 9386.9 | 2 / 12281.5 | 2 / 9381.8 | 2894.6 | -5.0 |
| C08 / both-deflate / auto | 2 / 9391.4 | 2 / 12219.5 | 2 / 9333.3 | 2828.1 | -58.1 |
| C09 / both-deflate / auto | 2 / 9339.5 | 2 / 12313.4 | 2 / 9352.5 | 2973.9 | 13.0 |
| C10 / both-deflate / auto | 2 / 9324.0 | 2 / 36110.4 | 2 / 9321.6 | 26786.4 | -2.3 |
| C11 / both-deflate / auto | 2 / 9394.2 | 2 / 9370.5 | 2 / 9427.4 | -23.8 | 33.1 |
| C12 / both-deflate / auto | 2 / 9376.5 | 2 / 13624.3 | 2 / 9320.7 | 4247.7 | -55.9 |

## 实际改动与失效范围

| 场景 | 直接改动资产 | 素材/依赖影响资产 | 改动文档 | 素材文件 | 修改后 xmlParse | 修改后 instanceCompile |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| C00 | 0.0 | 0.0 | 0.0 | 0.0 | — | — |
| C01 | 128.0 | 0.0 | 16.0 | 0.0 | 16.0 | 128.0 |
| C02 | 128.0 | 0.0 | 1.0 | 0.0 | 1.0 | 128.0 |
| C03 | 32.0 | 0.0 | 9.0 | 0.0 | 9.0 | 32.0 |
| C04 | 0.0 | 32.0 | 0.0 | 32.0 | 8.0 | 32.0 |
| C05 | 0.0 | 32.0 | 0.0 | 32.0 | 1.0 | 32.0 |
| C06 | 32.0 | 0.0 | 8.0 | 0.0 | 8.0 | 32.0 |
| C07 | 16.0 | 128.0 | 16.0 | 0.0 | 32.0 | 144.0 |
| C08 | 64.0 | 0.0 | 16.0 | 0.0 | 32.0 | 64.0 |
| C09 | 0.0 | 128.0 | 3.0 | 0.0 | 34.0 | 128.0 |
| C10 | 0.0 | 0.0 | 0.0 | 0.0 | 2724.0 | 0.0 |
| C11 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 |
| C12 | 192.0 | 64.0 | 22.0 | 64.0 | 31.0 | 256.0 |

## 较大增量来源（本机）

- C10：首次修改额外 26786.4 ms。
- C12：首次修改额外 4247.7 ms。
- C09：首次修改额外 2973.9 ms。
- C07：首次修改额外 2894.6 ms。
- C01：首次修改额外 2864.0 ms。

纯热态成本：9342.1 ms（n=3）。

## detailed 耗时 Top（独立诊断）

| 样本 | streamId | 阶段 | 累计 ms | self ms |
| --- | --- | --- | ---: | ---: |
| 085-C09-0-modified | base/Mod | core.output.references | 2581.8 | 2581.8 |
| 086-C12-0-modified | base/Mod | core.output.references | 2530.9 | 2530.9 |
| 084-C08-0-modified | base/Mod | core.output.references | 2464.3 | 2464.3 |
| 084-C08-0-modified | base/Mod | core.output.link | 1601.3 | 1601.3 |
| 085-C09-0-modified | base/Mod | core.output.link | 1593.2 | 1593.2 |
| 086-C12-0-modified | base/Mod | core.output.link | 1582.6 | 1582.6 |
| 084-C08-0-modified | base/Mod | core.cache.reinitialize | 1333.7 | 1268.9 |
| 086-C12-0-modified | base/Mod | core.cache.reinitialize | 1313.5 | 1232.0 |
| 085-C09-0-modified | base/Mod | core.cache.reinitialize | 1284.9 | 1178.7 |
| 085-C09-0-modified | base/Mod | core.assets | 845.3 | 802.3 |
| 086-C12-0-modified | base/Mod | cache.serialize | 797.1 | 797.1 |
| 085-C09-0-modified | base/Mod | cache.serialize | 777.7 | 777.7 |

## 未纳入成绩的正式样本

| 样本 | 状态 | wall ms | elapsed ms | 原因 |
| --- | --- | ---: | ---: | --- |

detailed 条目见 phases.csv，按实际 streamId 列出；阶段可能嵌套或并行，totalMs 不得相加为总耗时。
off 不提供详细阶段/失效原因；未出现的诊断数据保持空白，不推算为零。
本报告只记录性能及最少路径检查，不代表功能、原 SDK 字节或游戏内验收。

补充说明：本 Markdown 从原始 benchmark.json 再生成，增加软目标未达的明确提示；原归档报告见 report.original.md，三个 CSV 已逐项核对不变。测量来源仍为 a286062f。
