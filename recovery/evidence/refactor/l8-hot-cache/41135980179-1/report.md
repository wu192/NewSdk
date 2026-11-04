# L8 热缓存性能报告

**状态：`budget_exhausted`；组别：common；累计 21.64 分钟。**

测试提交：`873271d3a25d1c839c55af46417ff1a826ed6a41`；Release 来源：`cc10391be67dda57c7efa7fc06256b99e9372d9a`；EXE SHA256：`882fec0b34565230c38347f3cc0c0646a5e36cd4ffb31ac5056a01d2752e9ac0`。

正式成绩仅采用 performance=off、正常退出、目标路径已执行，且单次 wallMs 与编译器 elapsedMs 均严格 >8000 的样本。每次启动独立进程；准备、快照恢复、预热和报告均计入总预算。OS 页缓存未受控，样本数较少。

L8 为完整固定版本 Upheaval 加合法扩容分区的混合性能负载，区别于原始未修改 Upheaval。

L8 输入库存：3265 文档、71037 声明、298.42 MiB；实际计划 5 个流；发布 110.46 MiB。
L8 指纹：`f6e17139193d6091954c8491090f4f3b3adfd8ff970f94dba2da7eb014518939`。原始 Upheaval 库存：954 文档、3852 声明、286.96 MiB，指纹 `c41fdfd1d72f340351fba7b2828e92c33a0d63ff562ddc8b9962d2be002497f7`。

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
| C00 / both-deflate / auto | 3 / 12179.2 | 0 / — | 0 / — | — | — |
| C01 / both-deflate / auto | 2 / 12013.9 | 2 / 15354.1 | 2 / 12095.0 | 3340.2 | 81.1 |
| C02 / both-deflate / auto | 2 / 12018.5 | 2 / 15160.6 | 2 / 12060.4 | 3142.1 | 41.9 |
| C03 / both-deflate / auto | 2 / 11961.9 | 2 / 15215.5 | 2 / 12072.5 | 3253.6 | 110.6 |
| C04 / both-deflate / auto | 2 / 12190.5 | 2 / 15470.4 | 2 / 12094.9 | 3279.8 | -95.7 |
| C05 / both-deflate / auto | 2 / 11871.5 | 2 / 15079.7 | 2 / 11747.5 | 3208.2 | -123.9 |
| C06 / both-deflate / auto | 2 / 11706.8 | 2 / 15187.0 | 2 / 11863.6 | 3480.2 | 156.8 |
| C07 / both-deflate / auto | 2 / 11893.9 | 2 / 15208.0 | 2 / 12003.4 | 3314.1 | 109.5 |
| C08 / both-deflate / auto | 2 / 12224.7 | 2 / 15529.0 | 2 / 12226.6 | 3304.3 | 1.9 |
| C09 / both-deflate / auto | 2 / 12271.3 | 2 / 15715.3 | 2 / 12220.5 | 3444.0 | -50.8 |
| C10 / both-deflate / auto | 2 / 12230.9 | 2 / 37261.2 | 1 / 12249.4 | 25030.3 | 18.5 |

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
| C10 | 0.0 | 0.0 | 0.0 | 0.0 | 3236.0 | 0.0 |

## 较大增量来源（本机）

- C10：首次修改额外 25030.3 ms。
- C06：首次修改额外 3480.2 ms。
- C09：首次修改额外 3444.0 ms。
- C01：首次修改额外 3340.2 ms。
- C07：首次修改额外 3314.1 ms。

纯热态成本：12179.2 ms（n=3）。

## detailed 耗时 Top（独立诊断）

| 样本 | streamId | 阶段 | 累计 ms | self ms |
| --- | --- | --- | ---: | ---: |

## 未纳入成绩的正式样本

| 样本 | 状态 | wall ms | elapsed ms | 原因 |
| --- | --- | ---: | ---: | --- |
| 071-C10-2-hot_after | timed_out | 2122.6 | — | Compiler exceeded scenario/phase/global timeout: 071-C10-2-hot_after |

detailed 条目见 phases.csv，按实际 streamId 列出；阶段可能嵌套或并行，totalMs 不得相加为总耗时。
off 不提供详细阶段/失效原因；未出现的诊断数据保持空白，不推算为零。
本报告只记录性能及最少路径检查，不代表功能、原 SDK 字节或游戏内验收。

停止原因：Compiler exceeded scenario/phase/global timeout: 071-C10-2-hot_after
