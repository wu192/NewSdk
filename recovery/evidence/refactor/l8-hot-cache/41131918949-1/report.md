# L8 热缓存性能报告

**状态：`partial`；组别：common；累计 1.35 分钟。**

测试提交：`33572fd91bd4e22b73f41e3e418a488fa222a5eb`；Release 来源：`cc10391be67dda57c7efa7fc06256b99e9372d9a`；EXE SHA256：`882fec0b34565230c38347f3cc0c0646a5e36cd4ffb31ac5056a01d2752e9ac0`。

正式成绩仅采用 performance=off、正常退出、目标路径已执行，且单次 wallMs 与编译器 elapsedMs 均严格 >8000 的样本。每次启动独立进程；准备、快照恢复、预热和报告均计入总预算。OS 页缓存未受控，样本数较少。

L8 为完整固定版本 Upheaval 加合法扩容分区的混合性能负载，区别于原始未修改 Upheaval。

## 场景完成程度

| 场景 | 状态 | 说明 |
| --- | --- | --- |
| C00 | not_completed |  |
| C01 | not_completed |  |
| C02 | not_completed |  |
| C03 | not_completed |  |
| C04 | not_completed |  |
| C05 | not_completed |  |
| C06 | not_completed |  |
| C07 | not_completed |  |
| C08 | not_completed |  |
| C09 | not_completed |  |
| C10 | not_completed |  |
| C11 | not_completed |  |
| C12 | not_completed |  |
| F01-delete | not_completed |  |
| F01-rename | not_completed |  |
| F01-move | not_completed |  |
| F01-entry | not_completed |  |
| F02 | not_completed |  |
| F03-independent | not_completed |  |
| F03-shared | not_completed |  |
| F04-independent | not_completed |  |
| F04-shared | not_completed |  |
| F05 | not_completed |  |
| F06 | not_completed |  |
| F07-session-partial | not_completed |  |
| F07-session-all | not_completed |  |
| F07-assets | not_completed |  |
| F07-linked | not_completed |  |
| F09-move | not_completed |  |
| F09-mtime | not_completed |  |
| F09-same-stamp | not_completed |  |
| F09-shadow | not_completed |  |
| F10 | not_completed |  |
| F12 | not_completed | 未提供合法可运行且确实改变身份的另一版本，不模拟身份哈希 |
| F08-base-Mod | not_completed |  |
| F08-apt-TacticalHUDBuildQueuePage | not_completed |  |
| F08-apt-TacticalHUDCommandListBox | not_completed |  |
| F08-apt-TacticalHUDMainCommandBar | not_completed |  |
| F08-maps-MapMetaData_Global | not_completed |  |
| F08-cross | not_completed |  |
| F11-mode-none | not_completed |  |
| F11-mode-asset | not_completed |  |
| F11-mode-session-xml | not_completed |  |
| F11-mode-session-deflate | not_completed |  |
| F11-mode-both-xml | not_completed |  |
| F11-mode-both-deflate | not_completed |  |
| F11-jobs-1 | not_completed |  |
| F11-jobs-4 | not_completed |  |
| F11-jobs-auto | not_completed |  |

## 三阶段耗时

| 场景 / 缓存 / jobs | 热前 n / 中位 ms | 修改 n / 中位 ms | 再热 n / 中位 ms | 首改额外 ms | 再热变化 ms |
| --- | ---: | ---: | ---: | ---: | ---: |

## 较大增量来源（本机）

尚无合格的三阶段成绩。

纯热态成本尚无合格成绩。

## 未纳入成绩的正式样本

| 样本 | 状态 | wall ms | elapsed ms | 原因 |
| --- | --- | ---: | ---: | --- |

detailed 条目见 phases.csv，按实际 streamId 列出；阶段可能嵌套或并行，totalMs 不得相加为总耗时。
off 不提供详细阶段/失效原因；未出现的诊断数据保持空白，不推算为零。
本报告只记录性能及最少路径检查，不代表功能、原 SDK 字节或游戏内验收。

停止原因：ValueError: 000-calibration-0-warmup: Compiler did not complete normally
