# 百合子模块跨仓 token 审计

`recovery/tools/yuriko-wire/audit.py` 把三个 NewSdk 分支的真实 C++ 编译结果交给对应 EnhancerCorona 分支的生产 parser，检查两个仓库是否理解同一份数据。脚本从当前运行时源文件提取 parser 函数，并编译实际合约头文件；不保存生产函数副本或大型生成缓冲。它是手动诊断工具，不修改现有 CI。

本轮七个模块均在 GCC14、实际 32 位地址空间和 QEMU i386 下通过。运行目录只产生临时 C++、二进制和 `receipt.json`。收据记录源提交、已跟踪文件是否修改、实际编译的 parser 宿主散列、缓冲长度和 SHA256；有未提交代码时以宿主源码散列及现场源文件为准。

| 模块 | 数据大小 | 本轮输入与断言 | token 缓冲大小 |
| --- | ---: | --- | ---: |
| LureObjectsUpdate | 68 | 链接 7、半径平方 22500、守卫范围 99、帧间隔 12、七字状态、标记弱 ID、三个 Vector3 | 2784 |
| AddObjectsToLureUpdateSpecialPower | 468 | 原生 Store 父层解包、TeleportLinkID 7、释放时恢复父类型 | 3844 |
| AddObjectsToLiftUpdateSpecialPower | 472 | Store 集合 4、悬浮链接 7 | 3860 |
| FlingStoredObjectsSpecialPower | 476 | 集合 4、悬浮链接 7、速度 420/180、三个模板替换条目及释放 | 4020 |
| YurikoLiftObjectUpdate | 44 | 原生 40 字节父数据、LiftVelocity 8、链接 7 | 2568 |
| YurikoShieldSphereUpdate | 280 | 原生护盾半径 90、两条 FX 引用的一基编码、伤害类型 64 位掩码 | 3068 |
| DamageDynamicsCollide | 20 | DynamicsCollide→Behavior 父层、速度 25、两个直接内联的 152 字节 DamageNugget、伤害 250、自毁位及释放所有权 | 3736 |

编译器组件另外覆盖 LE/BE、完整伤害条目字段、嵌套过滤器与标量，以及旧资产字节兼容；跨仓工具使用 LE token 输入。最新投掷 Win32 Debug/Release 及四模块公开 XSD 路由为 [42829667772](https://github.com/wu192/NewSdk/actions/runs/42829667772)，源 `b4149eb564a5a7d2a6df0b0710992d19e99b867a`。旧 GameObject 八组输入三缓冲 23296 字节、旧 Weapon 六组输入三缓冲 13304 字节均与 `f0adccd6` 相同；具体散列见 [投掷说明](YURIKO_SLAM.md)。

## 复跑

准备六个已有工作树，其目录分别为前缀加 `-domination`、`-slam`、`-shield`，检出各仓库相同后缀的 `feature/yuriko-*` 分支。需要 Python 3 和可运行的 32 位 C++17/C++20 编译器。输出目录必须位于仓库外。

Windows 在 **x86 Native Tools Command Prompt** 中执行（路径按本机修改）：

```text
python recovery/tools/yuriko-wire/audit.py --sdk-prefix C:/work/NewSdk-yuriko --runtime-prefix C:/work/EnhancerCorona-yuriko --out C:/temp/yuriko-wire --cxx cl --kind msvc
```

此 MSVC 命令为可用入口，尚未用该手动脚本在 Windows 复跑；本轮 Win32 组件/XSD 证据来自上面的专用 Actions。

本次 Linux 宿主的实际命令如下；这些本地工具路径不是仓库依赖，其他主机可换成自己的 multilib GCC 和 ELF32 执行入口：

```bash
python recovery/tools/yuriko-wire/audit.py \
  --sdk-prefix /workspace/work/NewSdk-yuriko \
  --runtime-prefix /workspace/work/EnhancerCorona-yuriko \
  --out /workspace/work/yuriko-wire-repeat \
  --cxx /workspace/work/sdk-host-test/gxx32 --kind gnu \
  --runner-json '["/workspace/work/sdk-host-test/usr/bin/qemu-i386","/workspace/work/sdk-host-test/sysroot/usr/lib32/ld-linux.so.2","--library-path","/workspace/work/sdk-host-test/sysroot/usr/lib32"]'
```

宿主能直接执行 ELF32 时省略 `--runner-json`。GNU 模式只替代 Windows InitOnce 接口；生产 C++ 编组与解析代码原样编译。全部断言通过后输出 `All seven wire contracts passed` 和收据路径，任一编译、执行或断言失败都会终止并返回非零。

## 证据边界与后续验证

原生父数据 parser、完整 DamageNugget 字段 parser、游戏分配器和资源回调由受控接口替代。断言检查传给这些接口的父 token 层级、直接/多态条目形状、大小和引用编码；完整 DamageNugget 本身由已有编译器组件用例检查。工具不能证明原生函数调用约定、Hook、状态图、伤害执行、存档重定位或游戏行为，也没有运行 C# 前端、完整 SDK 四流、完整 MOD 或实机。

仅修改运行时内部算法、状态或 Hook，且 XML/XSD、编组/token/endian、数据头和 parser 均未变时，保留有效的 SDK 组件/XSD 结果，并运行该运行时改动所需的宿主回归和 Win32 DLL 检查。无需为了同一份编译协议重复完整 SDK 验证。修改上述协议依赖时，集中重跑对应组件/XSD及本审计；新增原生调用仍需独立 ABI 和运行时验证。该判断不跳过或关闭已有 Actions。
