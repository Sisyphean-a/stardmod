# StardewMods

这是一个单仓多包的《星露谷物语》SMAPI Mod 工作区。每个目录都是可以独立构建和部署的 Mod 包，拥有自己的 `manifest.json`、`UniqueID`、配置文件和 `dist` 构建输出。

## 包概览

| 包目录 | Mod 名称 / UniqueID | 主要作用 |
| --- | --- | --- |
| [`packages/Toolbox`](packages/Toolbox) | 工具箱 / `xixifu.Toolbox` | 整合多个简单、低耦合的游戏便利功能。 |
| [`packages/HorseFollower`](packages/HorseFollower) | 马匹跟随 / `xixifu.HorseFollower` | 让下马后的马匹跟随玩家，并提供骑乘自动导航。 |
| [`packages/HotkeyViewer`](packages/HotkeyViewer) | 快捷键查看器 / `xixifu.HotkeyViewer` | 查看游戏本体和已加载 Mod 的快捷键，并排查潜在冲突。 |

## 各包功能

### Toolbox

`Toolbox` 面向可以独立开关、无需单独生命周期的轻量便利功能：

- **动物自动抚摸**：在农场或畜棚中，自动抚摸扫描范围内尚未抚摸且友好度未满的动物。
- **光源半径调整**：分别调整家具光源和普通物体光源的半径倍率，修改后可立即刷新当前场景。
- **自动输入法控制**：仅在 Windows 上，正常游戏操作时屏蔽系统输入法；进入文字输入状态时恢复输入法。Android 和其他非 Windows 平台自动跳过该功能，不调用 Windows 原生 API。
- **栅栏防腐朽**：阻止栅栏和大门因时间流逝损失耐久。
- **自动开关门**：面对关闭的大门时自动打开，离开相邻格后按配置延迟关闭由本功能打开的大门。
- **镰刀收割**：允许使用真正的镰刀收割作物、花朵和地面觅食物；不支持用剑代替镰刀。检测到独立版 `bcmpinc.HarvestWithScythe` 时会跳过内置补丁，避免重复修改游戏方法。
- **快速堆叠到附近箱子**：在背包内使用按钮，将物品合并到配置距离内当前地点普通箱子或大箱子的相同物品堆叠；检测到独立版 `gaussfire.ConvenientInventory` 时跳过内置功能。
- **配置入口**：提供游戏菜单内的工具箱设置页，也支持 Generic Mod Config Menu（GMCM）。

配置文件：[`packages/Toolbox/config.json`](packages/Toolbox/config.json)

### HorseFollower

`HorseFollower` 是独立的马匹跟随和骑乘导航 Mod：

- **下马后跟随**：玩家当天骑马后下马，马匹会在满足场景和马棚范围条件时跟随玩家；同一场景内按碰撞范围规划路线，并根据距离追赶。
- **跨室外地图跟随**：玩家通过原版室外地图的普通步行出口换图时，马匹会依次走到相同出口并跨图继续跟随。
- **传送边界**：不会通过室内入口、公交、矿车、图腾、权杖等特殊传送建立跨图跟随路线，马匹会停在最后一个可达的室外地图。
- **骑乘自动导航**：骑马时可从 HUD 打开目的地菜单，前往皮埃尔杂货店、乔家超市、铁匠铺、星露谷酒吧、哈维诊所、博物馆/图书馆、社区中心、木匠店、冒险家公会、玛妮牧场或威利鱼店，并停在室外入口附近，不进入店内。
- **导航状态控制**：导航期间方向键可取消导航，打开菜单时会暂停；社区中心尚未开放时会在目的地菜单中置灰。

配置文件：[`packages/HorseFollower/config.json`](packages/HorseFollower/config.json)

### HotkeyViewer

`HotkeyViewer` 用于在游戏内集中查看快捷键：

- 默认按 `?` 所在的 `OemQuestion` 按键打开面板，也可通过 GMCM 或配置文件修改。
- 收集星露谷本体 `Game1.options` 中的键鼠按键。
- 优先读取 GMCM 注册的 Mod 按键选项；无法读取时，再从已加载 Mod 的 `config.json` 推测可能的快捷键。
- 按相同按键组合标记潜在冲突，并支持按来源、冲突状态和文本搜索筛选。
- 默认不纳入手柄按键；配置推测会过滤疑似 API key、token、密码、凭证和私钥等敏感字段。

配置文件：[`packages/HotkeyViewer/config.json`](packages/HotkeyViewer/config.json)

## 项目结构

```text
StardewMods.sln          # Visual Studio / dotnet solution
Directory.Build.props    # 所有包共用的 C#、Nullable 和确定性构建设置
packages/
├── Toolbox/             # 工具箱
├── HorseFollower/       # 马匹跟随与骑乘导航
└── HotkeyViewer/        # 快捷键查看器
```

每个包通常包含：

- `*.csproj`：目标框架、游戏程序集和 Mod API 引用；
- `manifest.json`：SMAPI 包身份和入口 DLL；
- `config.json`：默认配置；
- `*.cs`：实现代码；
- `dist/`：构建后生成的 DLL、`manifest.json` 和 `config.json`，该目录不纳入 Git。

## 构建

构建需要 .NET 6 SDK，以及包含目标 SMAPI 和游戏程序集的《星露谷物语》运行时目录。工具箱输出的是跨平台的 `net6.0` 托管 Mod DLL，不应改成 `net6.0-android` 应用；面向支持 SMAPI 的 Android 运行环境时，应使用对应环境导出的程序集目录作为 `GamePath`。通过 `GamePath` 指定目录：

```powershell
dotnet build StardewMods.sln -c Release -p:GamePath="D:\Games\Stardew Valley"
```

构建单个包时，将解决方案路径替换为对应的项目文件，例如：

```powershell
dotnet build packages/Toolbox/Toolbox.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
```

构建完成后，包的可部署文件位于对应的 `packages/<Package>/dist/`。工具箱对 GMCM 使用可选运行时桥接；未安装 GMCM 时仍可加载，安装后才注册 GMCM 配置项。

## 实现文档

- [工具箱包说明](.codestable/architecture/packages/toolbox.md)
- [马匹跟随包说明](.codestable/architecture/packages/horse-follower.md)
- [快捷键查看器包说明](.codestable/architecture/packages/hotkey-viewer.md)
