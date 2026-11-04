# GameObject / Weapon 第二阶段原生优化

分支 `perf/gameobject-weapon-plugins`，沿用 [PR #33](https://github.com/wu162/NewSdk/pull/33)。本阶段基线为上一阶段已验收的 `7cfdd4314d8cafa2f2c7313e64f5ffbacf94eaf9`，候选源码 `d60904bcb4fcdc6b02ff78db6b66410568543f57`。研究提出的六个方向均已接入插件；插件专项、最终产品/SDK、共享 XML63 与完整 MOD 配对均已取得成功范围。PR 待审，未合并。

## 已接入的实现

1. **透明属性查找**：GameObject、Weapon、Nugget 及 ObjectFilter 输入使用 `AttributeMap`，比较器为 `std::less<>`，字段名字符指针查找不再隐式创建字符串。共享 XML 的普通 map 在原有拥有式边界转换；复用模块分派和 Nugget 字段的一次 find，Weapon 先排除 Absent 描述项。
2. **member 地址索引**：FixupMembers 对大 owner 一次建立原生地址区间表，二分查找并复用上次命中；block≤16 或 member 少于 block 一半仍走线性查询。索引跳过空块，逻辑偏移仍包含对齐块大小。公开 GetMemberOffset、失败行为与原序列化排序保留。
3. **mask 无分配 token**：使用 C++14 指针/长度视图替代 substr/erase。ModelCondition、ObjectStatus、KindOf 及 Weapon 九组已核实排序的表二分查找；通用/未排序表仍按原顺序线性查询。保留减号 XOR、ALL 短路/尾部 padding、大小写、未知词及 NUL 截止语义。
4. **标量默认值预解码**：根对象默认字节模板与模块/Nugget 字段元数据在加载时解码。缺属性或显式文本等于原默认文本时直接复制数值；其余输入借用字符串并使用同一解码器。模块的累积 mask、引用、字符串及列表不进入标量缓存。GameObject DurationDefault 的缺省特例与显式值转换分开处理。默认元数据只有固定字节/数值，静态断言析构平凡，不引入混合 CRT 退出回调。
5. **Draw 子节点一次分组**：ModelCondition 的 12 组及 AnimationState 的 5 组在 children>8 时使用一个连续数组稳定分组，小节点保留无分配扫描。仍按原 schema 顺序分配、原列表顺序写入，单例取首个。没有把 Contain 全家族宣称为已重写。
6. **最终缓冲直接写出**：MakeTokenized 一次分配 token+data 最终向量，各区间直接 Flatten，显式传递 token 前缀基数，避免两向量再 insert。只导出 data 的 relocation/import 表；大小端转换只处理 token 区间。原始重定位排序的相等键顺序保留。

本阶段改变插件 Bridge/Native 和共享原生助手，不改 Core 调度、资产/会话缓存、Schema 或发布算法。CLR byte[] 的最终边界复制仍存在。

## 验证与计量来源

廉价原生预检比较上一阶段与当前 EnumBitset/Relocation：6,173 组 mask 语义、两端字节序、1/8/17/64/512 块 owner、嵌套指针/引用以及三缓冲区均相同。该预检在 Linux GCC C++14 执行，只作语义补充，不作为 Windows 性能成绩。

首个完整六项候选 `d457f99a` 的 [Windows 专项 41145351988](https://github.com/wu162/NewSdk/actions/runs/41145351988) success，真实进程退出码 0。五类输入先对原 Tokenizer DLL 和前后两实现比较大小端完整三缓冲区，再做五轮交替计量。中位耗时分别减少 63.90% / 26.15% / 15.01% / 71.37% / 54.06%（空 GameObject / 嵌套 Draw / 宽模型状态 / 空 Weapon / 32 Nuggets）。这是插件合成负载，相对上一阶段的增量收益；托管分配不变，当前宿主不计原生堆。

最终候选增加对 Core/XSD 显式默认文本的复用，并增加 GameObject/Weapon SDK Schema 根默认值两种 fixture。最终 [专项 41146357838](https://github.com/wu162/NewSdk/actions/runs/41146357838) success，七类输入大小端与原 SDK/前后组件的三缓冲区一致，真实进程退出码 0。五轮中位数如下，单位为每批 ms：

| 输入 | 调用/批 | 前 | 后 | 耗时减少 |
| --- | ---: | ---: | ---: | ---: |
| 空 GameObject | 4000 | 126.417 | 47.993 | 62.04% |
| 嵌套 Draw | 160 | 208.754 | 154.011 | 26.22% |
| 宽模型状态 | 160 | 120.259 | 102.623 | 14.67% |
| 空 Weapon | 4000 | 116.272 | 34.185 | 70.60% |
| 32 Nuggets | 240 | 314.551 | 134.105 | 57.37% |
| Schema 默认 GameObject | 4000 | 233.129 | 174.530 | 25.14% |
| Schema 默认 Weapon | 4000 | 226.733 | 165.626 | 26.95% |

计时在 DLL 加载、默认元数据初始化和预热之后进行；完整 MOD 配对另行包含进程启动。每类前后托管分配相同；没有以此推断原生堆分配量。Schema fixture 从 SDK XSD 根类型 AttributeUses 获取并展开默认属性，不包含完整 Core 预处理。原始 CSV、编译/计量日志、输入 DLL 身份及原收据在 [证据目录](../evidence/refactor/tokenizer-native-stage2/41146357838/validation.json)，4627 字节外层归档已下载复算 SHA256。完整 MOD 配对成绩另列。

共享 XML 的完整 63 组在祖先 `d457f99a` 独立验证；后续候选只改四个 GO/Weapon cpp/runtime 文件及专项 CS/PS。使用 SelectXmlComponentGroups.py 的递归依赖闭包比较，63 组实际输入均未变，SDK、Schema、生成代码及构建参数也未变。仅在祖先完整 XML 原收据成功后复用该范围；最终产品、GO/Weapon 与 SDK 仍使用最终候选重新构建并独立验收。

第一阶段结果与失败保留在 [原插件报告](TOKENIZER_PLUGIN_PERFORMANCE.md)。未合并、禁止强推；本轮不包含游戏内验收。

## 最终产品与 SDK

[41146357892](https://github.com/wu162/NewSdk/actions/runs/41146357892) 整体 success，源码/二进制/测试均为 `d60904bc`：Debug/Release 新构建，完整 GameObject/Weapon 四平台原版矩阵，64 worker/cache，Core/CLI/真实取消、两类缓存、载荷/输入根/BIG、完整 mymod 原 SDK、两个五文件 ZIP 和完整 Upheaval 22 样本通过。22 样本全部实际输出原字节相同、0 错误/1859 原警告。

265,450,740 字节原归档已下载复算 SHA256 `619fd795ec556fdbb81d18f103823e082cc0157024484b7e7308cc414d7be4e3`；十个运行文件及两个内层 ZIP 内容已核验。14 份主收据、两份原 BAB 基线及发布收据连同输入身份保存在 [original-receipts.zip](../evidence/refactor/tokenizer-native-stage2/41146357892/original-receipts.zip)，详见 [archive.json](../evidence/refactor/tokenizer-native-stage2/41146357892/archive.json)。

## 共享 XML63 的独立成功与复用

祖先源码 `d457f99a` 的 [41145352024](https://github.com/wu162/NewSdk/actions/runs/41145352024) 中，路由 smoke 与 XML 完整矩阵步骤 **success**：Debug/Release 各 63 组、四平台含 99，合计 1,918,904 样本 / 11,064,384 缓冲区比较。原始 validation、每组宿主/原 oracle 日志及构建收据在 [original-xml-receipts.zip](../evidence/refactor/tokenizer-native-stage2/41145352024/original-xml-receipts.zip)。127,209,348 字节完整原归档已下载复算。

该运行整体终态为 **cancelled**，不是完整产品通过：XML 独立成功后，一次性 [控制作业 41149921242](https://github.com/wu162/NewSdk/actions/runs/41149921242) 核实最终产品 41146357892 已 success，停止后面的重复产品检查，由既有 always-upload 保存证据。取消中的 GO 检查与未运行的后续组均不复用；最终产品/SDK 只使用上节独立全新成功运行。一次性控制工作流在归档后删除。

[递归输入身份](../evidence/refactor/tokenizer-native-stage2/xml-input-identity.json) 记录 d457f99a→d60904bc 的全部六个变更文件与63组闭包：全部未受影响。两次构建的 MSVC 14.44.35207、Windows SDK 10.0.26100.0、Roslyn 4.0.1、Foundation/App 身份也已核对；构建参数/Schema/原 DLL/生成器无变更。XML 与最终产品的 EXE 不同，分别记录二进制来源，不冒称在最终 EXE 重跑了63组。

## 完整 MOD 三轮配对

[41148441137](https://github.com/wu162/NewSdk/actions/runs/41148441137) success；控制器源码 `324db4c8`，重建精确基线 `7cfdd431`，复用最终验收 `d60904bc` 的 Release，EXE 身份与最终产品一致。严格核对20个生产改动文件；同一 Windows VM、完整历史 Upheaval、jobs4、双缓存关闭、每次全新输出、三轮交替顺序。

| 配对 | 基线秒 | 优化后秒 |
| --- | ---: | ---: |
| 1（基线先） | 36.409 | 34.659 |
| 2（优化后先） | 34.766 | 35.127 |
| 3（基线先） | 34.452 | 34.089 |
| 中位数 | 34.766 | 34.659 |

完整编译中位耗时减少 **0.31%**，基本持平；第二对优化后慢约1.04%。不按插件合成负载外推完整 MOD 加速，也不与第一阶段不同 VM 的绝对秒数拼接。六次全部0错误/1859原警告，BIG/SKUDEF/Additional 的实际字节相同。只有每方三次样本、没有预热、OS 页缓存未控制，不作通用性能保证。

814,673 字节原归档已下载复算；[原始 measurement](../evidence/refactor/tokenizer-native-stage2/41148441137/measurement.json)、六份报告和发布清单保存在 [original-evidence.zip](../evidence/refactor/tokenizer-native-stage2/41148441137/original-evidence.zip)。没有新增游戏内验收。

## 同步最新目标后的组合验收

用户批准合入后，目标已前进至 `0d7b685c`，包含 PR #32 Benchmark 与 PR #34 EALayer3。合并只有 RESUME 内容冲突，保留两边全部成果。插件20个生产文件与63组 XML 组件实际依赖均未变；组件证据继续分别使用上文来源。组合 EXE 输入已新增音频实现，因此重新构建产品并回归。

组合源码 `1a3e4bc6` 的 [41152199159](https://github.com/wu162/NewSdk/actions/runs/41152199159) **整体 success**：Debug/Release 新链接、部署/路由元数据、Core/CLI/真实取消、64 worker/cache、全部缓存及载荷/输入根/BIG、完整 mymod 原 SDK、两个五文件包、完整 Upheaval22样本均通过，全部0错误/1859原警告且输出原字节相同。不重复运行输入未变的 GO/Weapon/XML 完整组件矩阵。

164,598,490 字节原归档已下载复算 SHA256 `2ba381a049201eacf2de5ba6668e9f6061d81ccdf1eb878bda3af8d8d593138b`，十个运行文件与两个 ZIP 内容身份匹配。来源与复用范围在 [input-identity.json](../evidence/refactor/tokenizer-native-stage2/41152199159/input-identity.json)，原收据在 [original-receipts.zip](../evidence/refactor/tokenizer-native-stage2/41152199159/original-receipts.zip)。前面的配对性能数字仍属于 `d60904bc` 的独立旧组合，没有将它冒作新 EALayer3 组合的性能计量。
