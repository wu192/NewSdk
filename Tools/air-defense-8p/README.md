# 地图改造工具

本目录保存从原始 6 人地图重建 8 人右扩版的源码。输入必须为本次原始压缩包，不能把已扩展的地图再次作为输入。

依赖：Git、.NET 8 SDK、Python 3、Pillow；预览绘制使用 Linux Noto Sans CJK 字体。

在本目录执行：

```sh
git clone https://github.com/dreamness-dnalm/Ra3MapSharp.git vendor/Ra3MapSharp
git -C vendor/Ra3MapSharp checkout 0e66431b9d0857a2927111ead8dccbbd03fe1173
git -C vendor/Ra3MapSharp apply ../../parser-compatibility.patch
python rebuild.py '/path/to/[cor]陆地空战塔防1.3(食堂作)_mix.zip' /path/to/build-output
```

`parser-compatibility.patch` 针对以上版本的 Apache-2.0 解析库，保留每位玩家的建造列表附加数据、兼容 Linux 资源名称，并修复 byte 平面的单字节序列化。`ScriptsExpansion` 还显式保留可读 JSON 不覆盖的脚本隐藏字段和枚举参数通道。

`rebuild.py` 解压原图、调用 C# 转换、重新压缩地图、生成启动器坐标/人数/MD5 元数据及预览，最后输出 `land-air-defense-8p.zip`。`reports/` 包含修改明细与重新解析后的检查数据。

C# 单独调用方式：

```sh
dotnet run --project Editor.csproj -- input.map output.map reports
```

主要文件：

- `TerrainExpansion.cs`：逐行扩展地形与各标志平面，校正纹理相位。
- `ObjectsExpansion.cs`：扩展两条线路的物体、路径点、乘员和隐藏模板。
- `PlayersExpansion.cs`：新增玩家及波次队伍。
- `MetadataExpansion.cs`：扩展多人位置、触发区域与热点。
- `ScriptsExpansion.cs`：扩展脚本、Lua 及其引用，保留原始参数。
- `verify_map.py`：独立二进制及模拟 Lua 验证器；执行 `python verify_map.py --help` 查看参数。

本目录的地图专用转换源码不包含第三方解析库；依赖通过上述命令获取。原图内容的权利属于原作者。
