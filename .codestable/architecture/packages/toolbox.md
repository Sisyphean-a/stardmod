---
scope: package:toolbox
---

# 工具箱包

`packages/Toolbox` 是面向简单、低耦合便利功能的合并型 SMAPI mod 包，运行时身份为 `xixifu.Toolbox`，入口程序集为 `Toolbox.dll`。

## 职责

- 提供动物自动抚摸等轻量功能。
- 提供家具、物体光源半径倍率调整。
- 在 Windows 上提供自动输入法控制：常规游戏操作时屏蔽输入法，游戏文字输入框获得焦点时恢复输入法。
- 在农场与非自家住宅的农场建筑之间保持当前正在播放的任何音乐。
- 防止栅栏和大门因时间流逝而腐朽。
- 自动打开玩家面前的关闭大门，并在玩家离开后按配置延迟关闭。
- 保留恢复出的动物信息调试处理器，但当前不注册按钮事件。
- 为这些功能提供一个合并的 `ModConfig`、游戏菜单内的工具箱设置页和可选的 GMCM 配置入口。

## 边界与锚点

- SMAPI 入口与事件编排：`packages/Toolbox/ModEntry.cs`。
- 输入法控制：`packages/Toolbox/InputMethodFeature.cs`。
- 配置契约：`packages/Toolbox/ModConfig.cs`、`packages/Toolbox/config.json`。
- 光源 Harmony 补丁：`packages/Toolbox/LightRadiusFeature.cs`。
- 农场音乐保持补丁：`packages/Toolbox/FarmMusicFeature.cs`。
- 栅栏防腐朽补丁：`packages/Toolbox/FenceDecayFeature.cs`。
- 自动开关门事件控制器：`packages/Toolbox/AutomaticGatesFeature.cs`。
- 游戏菜单设置页和页签：`packages/Toolbox/ToolboxOptionsPage.cs`、`packages/Toolbox/ToolboxOptionsTab.cs`。
- 包身份：`packages/Toolbox/manifest.json`。
- 构建和游戏程序集引用：`packages/Toolbox/Toolbox.csproj`。

## 运行约束

- 动物自动抚摸只在农场或畜棚中运行，并按配置的检查间隔、扫描范围和动物状态决定是否抚摸。
- 光源补丁通过工具箱的 UniqueID 保存新的基础半径键；读取旧 LightRadiusMod 产生的键，避免合并后重复放大已有光源。
- 农场音乐补丁拦截农场与非住宅农场建筑之间的场景音乐切换，并保持任何当前音乐；进入 FarmHouse（包括 Cabin）不拦截。
- 栅栏防腐朽补丁只由主机更新同步的栅栏生命值，并阻止原版的时间流逝损耗；大门维持原版双倍耐久。
- 自动开关门只处理已由该功能打开的大门；玩家面对关闭的大门时打开，离开其相邻格后按 `AutomaticGateCloseDelay` 关闭，关闭功能不会强制关闭已打开的大门。
- 游戏菜单中的工具箱页签分为功能开关和参数两页；每次点击都立即写入配置并应用对应功能，GMCM 修改也应即时反映到运行中的功能。
- GMCM 重置配置时必须同步光源功能持有的配置引用，并立即刷新当前场景的光源半径。
- 功能开关包括自动抚摸、两类光源半径、农场音乐保持、栅栏防腐朽、自动开关门和自动输入法控制；光源配置变化会立即重算当前场景的光源半径，且只有主机写入同步光源。
- 自动输入法控制只在 Windows 生效；它通过 SDL 取得实际 Windows 窗口句柄并保留该窗口原有的输入法上下文，屏蔽前取消正在组合的文本并关闭候选栏。屏蔽期间每次更新都会确认窗口仍未关联输入法；若系统按键切换导致重新关联，则取消组合并再次屏蔽。离开文字输入状态、返回标题或关闭该配置时恢复该窗口原有输入法上下文。
- 输入法控制的 SDL 窗口查询失败会作为独立事件错误暴露，不中断工具箱的动物自动抚摸更新。
