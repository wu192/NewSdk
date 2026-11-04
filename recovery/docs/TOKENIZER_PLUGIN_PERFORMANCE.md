# GameObject / Weapon 插件编译优化

基线：`recovery/legacy-sdk-cache` / `8f854606ac20ce372045e3661b38e58e4e933ca2`。成果分支：`perf/gameobject-weapon-plugins`，PR #33。本页记录第一阶段的五个 GameObject/Weapon Bridge、Native 生产文件改动。后续六项原生优化涉及共享助手，见 [第二阶段报告](TOKENIZER_NATIVE_STAGE2.md)；第一阶段成功证据不能代替新源码验收。

## 实现

- GameObject 将已解析的嵌套节点移动到父树，避免逐层复制 map/vector；递归路径按引用传递。删除已无校验工作的 NoStructuredText、闲置 navigator 克隆及重复形状查询。
- GameObject 的模块表按原来 59 个家族的优先顺序建立一次只读哈希索引；emplace 保留首个匹配，未知类型仍返回空。
- Weapon 单次读取根子节点，代替七次单例查找、额外 modifier XPath 和重复遍历。首个单例、列表顺序及命名空间选择保留；原生层仍按原 schema 顺序分配，XML 顺序不改变布局。
- Weapon 的位枚举组预建一次并只读复用，避免每资产为五个 mask 字段重新分配和填充 vector。Nugget、Filter、属性集合转交时使用移动语义。
- 两个索引采用仓库既有的原生 InitOnce 进程级元数据方式，初始化失败不发布半成品；表只读共享，退出由操作系统回收，不注册 C++/CLI 关闭阶段析构回调。

当前状态：独立插件、Debug/Release 完整产品/SDK、完整 Upheaval 22 样本及三轮前后配对均通过；PR #33 待审，未合并。

## 验证范围

完整产品回归和插件计量分别记录来源。计量不能替代产品与 SDK 验收。

独立组件源码 `25aedf8b8ebfcde211e735c7f8113850bd68770d`，[41138099602](https://github.com/wu162/NewSdk/actions/runs/41138099602) **success**，hostExitCode=0。完整产品/SDK 源码 `9f9d176761ff28cee993c27ba61b864041a26299`，[41138338003](https://github.com/wu162/NewSdk/actions/runs/41138338003) **success**：Debug/Release 新构建、完整 GameObject/Weapon 四平台原字节/tokenizer、64 worker/cache 用例、Core/CLI/缓存/取消、完整 mymod 原 SDK、五文件 ZIP 及完整 Upheaval 22 样本均通过。两者生产源码相同；后者仅改变工作流注释及计量控制器。

产品原归档 265,207,423 字节已下载复算 SHA256；14 份主收据、两份独立原 BAB 基线及最终发布收据已核对，十个运行文件及两个发布 ZIP 实际内容身份一致。Upheaval 22 样本均 0 错误/1859 原警告，输出字节相同。原收据压缩保存在验收目录的 original-receipts.zip，外层归档、内层收据归档与运行文件身份见 archive.json。

## 插件阶段计量

同一 Windows VM、x86 CLR、相同 /O2 与 /fp:precise；重建基线和优化后独立组件。四类已解析、模拟 BAB 注解/依赖预处理后的 XML：空 GameObject，8 个 Draw×8 个嵌套模型状态，空 WeaponTemplate，32 个 DamageNugget 加时长/音频/Filter/Scatter 辅助块。先将优化前后与原 Tokenizer DLL 的 InstanceData、RelocationData、ImportsData 在大小端下逐字节比较，再预热并执行五轮交替顺序的 Win32 样本。

计时仅含插件输入转换及原生编译；XML 创建/解析、输出比较、显式 GC、Core 预处理、缓存和文件 I/O 不计。managedAllocatedBytes 是 AppDomain CLR 托管分配，**不含原生堆**。数据为合成组件负载，不代表完整 MOD 的加速比例；默认 GameObject 的路径基本未变，小幅波动保留。

成功运行 41138099602 的五轮中位数（ms 为整批耗时）：

| 输入 | 每轮调用 | 基线 ms | 优化后 ms | 耗时减少 | 托管分配减少 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 空 GameObject | 4000 | 125.117 | 132.729 | **-6.08%** | 0% |
| 嵌套 Draw GameObject | 160 | 228.556 | 210.146 | 8.05% | 13.01% |
| 空 WeaponTemplate | 4000 | 177.480 | 117.597 | 33.74% | 33.60% |
| 32 Nugget WeaponTemplate | 240 | 331.376 | 317.512 | 4.18% | 3.78% |

空 GameObject 没有模块树，其本轮托管分配完全相同；耗时较差，不能宣称所有输入均加速。各轮原始数值均保留，不丢弃较慢样本。

## 完整 MOD 配对

[41141062587](https://github.com/wu162/NewSdk/actions/runs/41141062587) **success**，计量控制器来源 `18ae3f0240c712e6a3ecba539e08dec64bc70a5e`。复用产品验收的 `9f9d1767` Release，重建精确 `8f854606` 基线；严格核对五个生产改动文件。相同 Windows VM、jobs4、双缓存关闭、每次全新输出，完整历史 Upheaval，三轮交替顺序；全部 BIG/SKUDEF/Additional 实际字节相同，六次均 0 错误/1859 原警告。

| 配对 | 基线秒 | 优化后秒 |
| --- | ---: | ---: |
| 1（基线先） | 23.617 | 21.638 |
| 2（优化后先） | 22.185 | 21.052 |
| 3（基线先） | 21.318 | 21.643 |
| 中位数 | 22.185 | 21.638 |

中位耗时减少 **2.47%**，第三对优化后约慢 1.53%，保留该不利样本。无预热、OS 页缓存不受控，三样本不作为通用性能保证；不能用插件合成输入的 8%/34% 代替完整 MOD 数值。817,554 字节原归档已实际下载复算 SHA256，原始收据、报告、发布文件清单在 original-evidence.zip 中。

## 保留的失败

- 41137058730：四个组件编译通过；旧 csc 不接受相对正斜杠路径，宿主未编译。修复为 Resolve-Path。
- 41137277732、41137544777：四类大小端三缓冲及计量均完成，随后进程退出失败；后者捕获 `0xC0020001`。函数内静态 map 的混合运行时析构造成退出异常，不能以日志 PASS 作为验收。改为原生 InitOnce 只读元数据；没有使用 Environment.Exit 或忽略退出码。
- 41137876172 / 41137876257：Windows 头文件的 small 宏与既有局部变量冲突，构建失败。变量改名，不改变几何序列化。
- 41136790449：早期静态容器版本完整回归 failure，GameObject/Weapon 和 worker 等进程关闭失败，不用于修复后的验收。

失败和成功的原收据、归档 SHA256、实际下载复算结果见 `recovery/evidence/refactor/tokenizer-plugins/`。本轮没有新增游戏内验收，PR 未合并，禁止强推。

## 进一步优化研究

用户追加的源码深入研究见 [下一批可优化点](TOKENIZER_PLUGIN_NEXT_OPTIMIZATIONS.md)：优先属性异构查找、member offset 地址索引；其次处理已排序 mask、纯标量缺省值、子节点重复扫描及最终缓冲直写。六个方向随后已接入正式插件，Windows 实测与共享助手回归范围单独记在 [第二阶段报告](TOKENIZER_NATIVE_STAGE2.md)。
