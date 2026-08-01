---
scope: package:toolbox
---

# 工具箱包

`packages/Toolbox` 是面向简单、低耦合便利功能的合并型 SMAPI mod 包，运行时身份为 `xixifu.Toolbox`，入口程序集为 `Toolbox.dll`。

## 职责

- 提供动物自动抚摸等轻量功能。
- 提供家具、物体光源半径倍率调整。
- 在农场与非自家住宅的农场建筑之间保持音乐播放器音乐。
- 保留恢复出的动物信息调试处理器，但当前不注册按钮事件。
- 为这些功能提供一个合并的 `ModConfig` 和一个可选的 GMCM 配置入口。

## 边界与锚点

- SMAPI 入口与事件编排：`packages/Toolbox/ModEntry.cs`。
- 配置契约：`packages/Toolbox/ModConfig.cs`、`packages/Toolbox/config.json`。
- 光源 Harmony 补丁：`packages/Toolbox/LightRadiusFeature.cs`。
- 农场音乐保持补丁：`packages/Toolbox/FarmMusicFeature.cs`。
- 包身份：`packages/Toolbox/manifest.json`。
- 构建和游戏程序集引用：`packages/Toolbox/Toolbox.csproj`。

## 运行约束

- 动物自动抚摸只在农场或畜棚中运行，并按配置的检查间隔、扫描范围和动物状态决定是否抚摸。
- 光源补丁通过工具箱的 UniqueID 保存新的基础半径键；读取旧 LightRadiusMod 产生的键，避免合并后重复放大已有光源。
- 农场音乐补丁只拦截农场与农场建筑之间的场景音乐切换；进入 FarmHouse（包括 Cabin）不拦截。
- GMCM 重置配置时必须同步光源功能持有的配置引用。
