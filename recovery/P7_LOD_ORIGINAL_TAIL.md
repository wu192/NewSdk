# 原 LoD 工具的来源表尾字节差异

2026-10-27。此页保留未通过子集及诊断事实，不把原工具异常转成生产补丁，也不降低原始字节断言。

## 原始失败

[联合后处理运行 40419575140](https://github.com/wu162/NewSdk/actions/runs/40419575140)，source `0ed54a8469cd48fde787e9883ec787fc6a302e84`，artifact `base-postprocess-40419575140-1`（ID `12697221618`）。
`Debug/evidence/no-references` 的 HashFix、ModAssetResolver 主流四文件逐字节一致；LoD 后首次失败。原输入 `mod.manifest` 为174字节，SHA256 `d17ba4bef6bee77569371eec3ec043b0c99275498c2ef342532dc9ae65644828`。原与恢复版主输入前后均未改变。

低、中清 manifest 都为138字节，只有偏移137不同：来源文件表最后的 NUL，恢复版保留 `00`，原工具输出 `01`。名称表12字节、来源表16字节，合计28字节。失败终态及原始八输出保留，不能称此联合组已经通过。

## 重复诊断

[原工具重复探针 40420127629](https://github.com/wu162/NewSdk/actions/runs/40420127629)，source `eb96bf1b586fdd9db79414f0360de47366a89a27`，artifact `prepared-lod-40420127629-1`（ID `12697497696`，ZIP SHA256 `7d1a759ce6796db77b124c32a8effe4234ebebeb8624b07e2e38783cbbf97b1f`）。
原 exe SHA256 `e28b665a17cbbf597b47a2069a6efd979da00b44021f8f73b6beb0fb076fac0b`。
原始证据入口是归档内 `original-tail-probe/probe.json` 及各 `names-sources-<长度>-repeat-<编号>/` 的完整输入、八输出和stdout/stderr。

| 名称表＋来源表字节数 | 同输入三次的低/中清尾字节 | 与完整逻辑表复制相比 |
| --- | --- | --- |
| 28 | 均 `01`，三次输出哈希相同 | 仅偏移137的 `00→01` |
| 29 | 均 `00`，三次输出哈希相同 | 无差异 |
| 30 | 均 `00`，三次输出哈希相同 | 无差异 |
| 31 | 均 `00`，三次输出哈希相同 | 无差异 |
| 32 | 均 `00`，三次输出哈希相同 | 无差异 |

15次运行的主输入均未改变，输出长度均符合表长度。28字节样本与原失败输入逐字节相同。**本次没有观察到相同输入在重复进程间随机变化**；边界实验仅证明本例28字节异常、29–32字节正常，不能推断所有28字节表或其他分配布局均具有同一结果。
同一运行的既有组件原工具完整raw差分仍在Debug/Release各17用例通过，包括Static/Worldbuilder实际manifest；这些不同输入的成功范围保留，不代替失败的联合子集。

## 静态依据与处理边界

`SDK/tools/LoDStreamBuilder.exe` 的v6分支：

- `0040faf1–0040fb0c` 按名称表＋来源表总长度分配动态byte数组；`004073f4` 从数组指针前四字节读取Length。
- `0040fb20–0040fb2c` 把数组指针加1作为BlockRead目标，仍读取Length字节。
- `0040fcbc–0040fcc8` 把数组指针加1作为BlockWrite来源，仍写Length字节。

这直接访问逻辑byte数组索引1到Length，最后一字节超出0到Length−1的数组范围。结合长度边界实验，**原数组尾越界受后续分配影响是当前解释**；具体堆分配器状态尚未单独证明，不能把它包装成已确认的通用尾字节公式。

生产LoD保持完整表复制，不添加无依据的“尾字节写1”规则。原联合raw断言继续保留且该子集未验收；独立真实base/BIG组必须用自身当前输入与原SDK八输出逐字节比较，取得成功后才接受那个明确输入范围。本诊断不替代真实组，也不阻止复用输入未变化且原始证据有效的既有成功范围。后续若需要覆盖此异常子集，先单独明确原工具缺陷的兼容合同；不可把诊断成功写成联合后处理或任意base输入已通过。

## 结果主线的兼容策略（2026-10-27）

本次明确采用确定性修复合同：复制完整逻辑名称/来源表，保持长度与末尾 NUL；不模拟原工具数组越界所读取的内存。该异常样本属于有意修复的原工具缺陷，不再作为未决实施任务，也不宣称其与原工具逐字节相等。

`recovery/tests/App/LoDCanonicalTests.cs` 独立构造28–32字节表，每个输入重复三次，校验全部八输出、依赖表替换、资源长度清零、原校验和和主输入不变。原联合差分的全部原始断言、失败终态和已知偏移差异继续保留，不能改为相等；普通真实base/BIG仍须原工具逐字节对照。新增专项已通过Debug/Release应用测试，最终生产/BIG/独立ZIP验收亦已通过，准确身份见 COMPILER_RELEASE.md。

## compiler-main 的 CI 呈现（2026-11-04）

合并后的 `72aa8f0c` 运行 [41815206785](https://github.com/wu192/NewSdk/actions/runs/41815206785)
在 Debug/Release 的同五个合成输入保留 raw 失败：`no-references`、
`hash-fixed-before-resolver` 及三个 Static/Global/Audio 纹理引用输入。
HashFix/Resolver 的十组均原字节一致，其余五组 LoD 也原字节一致。

`TestCompilerBasePostprocess.py` 的原始断言及 `validation.json` 不变。
工作流另外运行 `CheckCanonicalLoD.py`，校验全部十组、三个阶段和每组十二文件，
并独立按 v6 格式验证候选的表复制、依赖替换、三项资源长度清零和四字节校验和。
只有原 LoD EXE SHA256 及五个输入的 SHA256 都匹配时，才允许低/中清 manifest
最后一个字节为原工具 `01`、候选 `00`；其他任意差异、缺文件/组、错误收据均失败。
单独的 `canonical-validation.json` 明示 `rawByteIdentical` 和全部已知差异位置，
不能把它当作联合 raw 相等、生产 BIG 或 SDK 通过。正常组件 raw 差分仍独立执行。
只要原始 raw 对比失败，工作流仍上传精简 JSON/日志（保留一天），即使 canonical
合同通过也保留失败收据；不上传可重建二进制或扩大普通成功产物的保留策略。
