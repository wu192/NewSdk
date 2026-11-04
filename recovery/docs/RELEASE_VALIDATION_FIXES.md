# 发行验收脚本修复（2026-11-01）

后续 Joypad 并行扩展已通过限定小型验收，用户随后明确批准发布：[2026.11.01 Release](https://github.com/wu162/NewSdk/releases/tag/compiler-2026.11.01)。本页以下保留此前纯验收修复阶段与待确认包的历史范围，当前行为与发行来源见 [Joypad 说明](components/xml/XML_JOYPAD.md)。

基线为 `recovery/legacy-sdk-cache` 的 `680c4f0c4439bd7435addebfc57a1a16c3c320e3`。
本次发现并修复的是验收脚本对已合入行为的旧假设；没有修改编译器生产源码、SDK、UI、生成代码或编译链接输入。
修复位于 [PR #49](https://github.com/wu162/NewSdk/pull/49)，未合并。用户要求先修复、打包，确认后才能发布；本轮不创建 Release 或发行标签。

## 修复

| 触发 | 原误报 | 修正及保留的门槛 |
| --- | --- | --- |
| BIG build/plan 已要求 CLI 显式版本 | BIG、完整 mymod 和性能脚本仍依赖旧默认值，退出 `CLI_MOD_VERSION` | 验收调用显式传 `--mod-version 1.0`；已有自定义版本用例保持其参数，产品仍拒绝缺少版本的 BIG 请求 |
| 同一 SDK/MOD 的缓存独立于 `--out` | 后续“冷”用例读到其他输出目录留下的热会话，缺少解析/编译阶段或编译计数为零 | 并行、性能及完整 Upheaval 各独立场景显式使用分区内测试命名空间；同场景热态和修改态继续复用原命名空间，保留全原始字节、编译计数、实际 worker 和失效断言 |
| SDK 中允许生成 `SuperCompilerCache` 输出 | 完整 SDK 差分的两配置四流/BIG 均相同，但最后把缓存新增文件计为 SDK 输入变化 | 仅 SDK 根输入快照排除精确的 `SuperCompilerCache` 输出子树；Schema、配置、Art、MOD 等实际输入继续逐文件检查长度和 SHA256，原工具和产品运行目录检查不变 |
| 新增 11 个 Joypad 根尚未进入闭合并行认证表 | 旧 Core 测试仅允许四类 APT 和 TestTexture 留在主线程 | 精确列出这 11 个 Joypad 主线程例外；继续要求 284 路认证 worker 全覆盖和 16 路主线程集合完全匹配，不擅自认证新并行路线 |

## 二进制与验收来源

生产二进制由 [41295495489](https://github.com/wu162/NewSdk/actions/runs/41295495489) 在上述基线重新构建 Debug/Release。
该运行的构建、应用、真实 CLI/CtrlBreak、缓存、FLA、Texture 等独立成功组继续有效，原总运行 failure 保留。

[41296844301](https://github.com/wu162/NewSdk/actions/runs/41296844301) 对同一二进制通过性能、完整并行/缓存、BIG/原 LoD/MakeBig/Additional，以及两配置除 XML 注册集合外的其余 11 项 Core fixture；原 XML 旧集合及 SDK 输入审计的失败终态保留。
[41297071528](https://github.com/wu162/NewSdk/actions/runs/41297071528) 的修正 XML fixture 在 Debug/Release 均通过原四平台与三缓冲矩阵；该运行随后因等待的上游 SDK 审计失败而整体 failure，并未改写。

最终 [41297316677](https://github.com/wu162/NewSdk/actions/runs/41297316677) **success**：核对上述独立成功组及生产闭包，重新运行完整 mymod 原 SDK 四流/BIG/输入审计、两个精确三文件 ZIP 的独立解压 doctor 与生产 BIG 验收，以及完整 Upheaval jobs 1/4/auto 冷、热和修改共 22 样本。各对照均比较原始字节，未重建未变化的生产二进制，未放宽原有失败/取消、原工具差分或实际并行门槛。

生产源提交、构建运行、各验收运行和测试提交分别记录，不把回放工作流的提交当作新生产源码。临时回放工作流保存在运行对应的历史提交中；最终 PR 仅保留通用验收修复和证据。

运行包每配置严格为 `NewModCompiler.exe`、`NewModCompiler.exe.config`、`antlr.runtime.dll`。额外打包检查核对 ZIP CRC、包及三个成员的 SHA256、生产 bin 原始字节和 Win32 PE 类型，说明与校验数据置于运行 ZIP 外。仅交付待确认文件，不发布 Release。

[待确认纯运行产物](https://github.com/wu162/NewSdk/actions/runs/41298119902/artifacts/13079722889) 由 [41298119902](https://github.com/wu162/NewSdk/actions/runs/41298119902) 打包检查通过，外层 3,798,473 字节，SHA256 为 `b80478d802159882fbfac25f3be47e46a302933da771d693578575f457425c3a`。归档仅含两个运行 ZIP、两个 manifest、使用说明、构建元数据与 SHA256SUMS 共七份文件，无源码、fixture 或测试宿主。已下载原归档复算外层 SHA256、全部校验清单和两个三文件 ZIP；[身份收据](../evidence/refactor/release-validation/41297316677-1/summary.json) 记录各运行文件与包哈希。

未新增游戏内运行、完整 UI/runtime 或中文纹理路径验收，既有 BC1 与原编码器的差异边界不变。本次不把验收脚本修复描述成新的编译器功能或生产算法修复。
