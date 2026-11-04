# NewModCompiler SDK共享地编缓存

同一SDK的所有Mod现在共用 `SuperCompilerCache/shared/neutral-worldbuilder/` 中的一份地编四流，避免每个Mod重复占用约1.33GB。提取和消费使用共享锁，两个Mod并发首次使用只保留一份数据；锁等待可取消，普通Mod编译缓存仍各自隔离。

当前Mod的有效旧缓存直接移动到共享位置，无需再次解包；共享已有效时清理当前Mod的同来源有效旧副本。其它Mod在各自下次使用时处理，不扫描删除其它分区。

生产源码：`57c0c78375c2a41ecb206c04dd761f5d9db1adcc`。
[Windows41324686715](https://github.com/wu162/NewSdk/actions/runs/41324686715)整体通过：D/R新构建，各1901项App检查、原9个游戏布局用例、第二Mod共享命中、两个Mod实际重叠冷提取、原SDK整个BIG字节一致，以及独立三文件ZIP冷编译。SDK、游戏、Mod源不变；磁盘只存在一份共享Worldbuilder.bin。

日常下载NewModCompiler-Win32-Release.zip（约1.61MB）。Release EXE=5024256字节；仍不内嵌地编资产，不附500MB参考包。需要游戏Data/WBData_12.big已由SDK地编安装，或者使用已有完整参考流；支持注册表自动发现和game-root。完整使用与SHA256见附件。

本次快速定向验证没有重新执行完整Core/CLI大矩阵、完整大型Mod或游戏内测试。保留此前发行，源码变更仍在fix/neutral-worldbuilder-config。
