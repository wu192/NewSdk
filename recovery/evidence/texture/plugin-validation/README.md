# Texture 插件验证入口

当前生产/测试源码 `8fe12ca9b`，Windows
[41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818) 整体成功。
版本4202及三个原ProcessingHash保留，三路Texture进入有界worker调度。

- [常见格式与16个贴图CLI用例](41185772818-1/common-summary.json)：D/R各31用例，
  结果与旧4c构建原字节全等；实测4worker并发，jobs1/4/auto输出一致，热态零编译。
- [完整验收与归档复算](41185772818-1/full-summary.json)：原4202完整转录、三路worker、
  诊断/失败回收、64并行缓存矩阵、完整缓存/SDK/BIG、两个三文件ZIP及22Upheaval样本。
- [当前正式归档](https://github.com/wu162/NewSdk/actions/runs/41185772818/artifacts/13037167160)：
  `artifacts/final-release/41185772818-1/` 提供Debug/Release ZIP及清单；已下载复算。

[41180859852-1](41180859852-1/full-summary.json) 是此前串行和新身份版本的成功证据。
其他既有目录保留原失败/范围，不覆盖历史，不代表当前构建。编码兼容边界见
[Texture说明](../../../docs/TEXTURE_PLUGIN.md)，未新增游戏内运行验收。
