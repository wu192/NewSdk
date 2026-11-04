# 编译与缓存生命周期

本轮 `perf/direct-sdk-output` 已移除 MOD 发布事务与整代 Staging。设计、删除清单、目录用途及验证来源见[直接产物与原 SDK 缓存](docs/DIRECT_OUTPUT.md)。早期事务审计保留在 Git 历史，不再作为当前合同。

1. BuildSession 解析计划，在 `published/<content>/<entry>` 顺序调用 Core；每次只接收本次实际完成的文件。
2. Core 使用原 BAB SessionCache/BuildAssetCache、持久中间资产及直接四流输出。原清单和三文件头校验和决定是否跳过重链接。
3. Core 成功后保存原格式会话。失败/取消按 `/psc` 保存有用的部分状态；冻结/压缩/触碰选项沿用原行为。会话单文件 `.tmp/.old` 是原 SDK 缓存行为，不是 MOD 发布事务。
4. Core 关闭所有工作任务、哈希/trace/处理器作用域与插件，释放会话锁；输入身份缓存独立保存。失败仅使触及的四流失效，不删除中间资产或递归清空输出根。
5. BIG 模式从当前文件清单与 Additional 直接读取输入，在最终目录生成 BIG/SKUDEF；既有单文件临时写入支持覆盖。LoD、APT 版本和显式中立修复仅清理各自实际工作目录。
6. 最终报告和事件继续报告错误并设置退出码，但不回滚已经完成的 MOD 或缓存更新。无 CommitCache/RollbackCache/CompleteCache、发布收据或整目录代际交换。

取消和编译失败不能把旧磁盘文件当成本轮成功产物。目录内允许存在旧版本或未选择的流；打包仅消费本次 Core 完成列表。
