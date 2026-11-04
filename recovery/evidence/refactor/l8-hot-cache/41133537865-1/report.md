# L8 热缓存性能报告

**状态：`budget_exhausted`；组别：common；累计 21.16 分钟。**

测试提交：`e667ea7091bd0ed136f4a3e9c4a03667125b0049`；Release 来源：`cc10391be67dda57c7efa7fc06256b99e9372d9a`；EXE SHA256：`882fec0b34565230c38347f3cc0c0646a5e36cd4ffb31ac5056a01d2752e9ac0`。

正式成绩仅采用 performance=off、正常退出、目标路径已执行，且单次 wallMs 与编译器 elapsedMs 均严格 >8000 的样本。每次启动独立进程；准备、快照恢复、预热和报告均计入总预算。OS 页缓存未受控，样本数较少。

L8 为完整固定版本 Upheaval 加合法扩容分区的混合性能负载，区别于原始未修改 Upheaval。

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
| C07 | budget_exhausted |  |
| C08 | budget_exhausted |  |
| C09 | budget_exhausted |  |
| C10 | budget_exhausted |  |
| C11 | budget_exhausted |  |
| C12 | budget_exhausted |  |
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
| C00 / both-deflate / auto | 3 / 17304.4 | 0 / — | 0 / — | — | — |
| C01 / both-deflate / auto | 2 / 17916.2 | 2 / 16437.2 | 2 / 12297.1 | -1479.0 | -5619.0 |
| C02 / both-deflate / auto | 2 / 17208.1 | 2 / 15824.1 | 2 / 12127.8 | -1384.0 | -5080.3 |
| C03 / both-deflate / auto | 2 / 17277.7 | 2 / 15767.1 | 2 / 12279.1 | -1510.6 | -4998.6 |
| C04 / both-deflate / auto | 2 / 17940.2 | 2 / 16553.2 | 2 / 12248.9 | -1387.0 | -5691.3 |
| C05 / both-deflate / auto | 2 / 18398.7 | 2 / 17491.0 | 2 / 13227.7 | -907.7 | -5171.0 |
| C06 / both-deflate / auto | 2 / 18008.4 | 2 / 17149.1 | 2 / 12681.7 | -859.4 | -5326.8 |
| C07 / both-deflate / auto | 1 / 17170.3 | 0 / — | 0 / — | — | — |

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
| C07 | 16.0 | 128.0 | 16.0 | 0.0 | — | — |

## 较大增量来源（本机）

- C06：首次修改额外 -859.4 ms。
- C05：首次修改额外 -907.7 ms。
- C02：首次修改额外 -1384.0 ms。
- C04：首次修改额外 -1387.0 ms。
- C01：首次修改额外 -1479.0 ms。

纯热态成本：17304.4 ms（n=3）。

## detailed 耗时 Top（独立诊断）

| 样本 | streamId | 阶段 | 累计 ms | self ms |
| --- | --- | --- | ---: | ---: |

## 未纳入成绩的正式样本

| 样本 | 状态 | wall ms | elapsed ms | 原因 |
| --- | --- | ---: | ---: | --- |
| 049-C07-1-modified | timed_out | 7591.5 | — | Compiler exceeded scenario/phase/global timeout: 049-C07-1-modified |

detailed 条目见 phases.csv，按实际 streamId 列出；阶段可能嵌套或并行，totalMs 不得相加为总耗时。
off 不提供详细阶段/失效原因；未出现的诊断数据保持空白，不推算为零。
本报告只记录性能及最少路径检查，不代表功能、原 SDK 字节或游戏内验收。

停止原因：Compiler exceeded scenario/phase/global timeout: 049-C07-1-modified
