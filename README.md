# StardewMods

这是一个单仓多包的《星露谷物语》SMAPI Mod 工作区。每个 `packages/<Package>/` 目录都是一个可以独立构建和部署的 Mod 包，拥有自己的 `manifest.json`、`config.json`、入口 DLL 和 `dist/` 构建输出。

## 包总览

以下信息以各包当前的 `manifest.json` 为准：

| 包目录 | 版本 | 作者 | UniqueID | 入口 DLL | 最低 SMAPI |
| --- | ---: | --- | --- | --- | ---: |
| [`Toolbox`](packages/Toolbox) | `1.8.1` | xixifu、irocendar、EnderTedi、Rakiin aKa ScheKaa | `xixifu.Toolbox` | `Toolbox.dll` | `4.0.0` |
| [`HorseFollower`](packages/HorseFollower) | `1.6.0` | xixifu | `xixifu.HorseFollower` | `HorseFollower.dll` | `4.0.0` |
| [`HotkeyViewer`](packages/HotkeyViewer) | `1.0.0` | xixifu | `xixifu.HotkeyViewer` | `HotkeyViewer.dll` | `4.0.0` |

| 包 | 定位 | 配置入口 |
| --- | --- | --- |
| `Toolbox` | 合并多个低耦合的农场、地图和操作便利功能。 | [config.json](packages/Toolbox/config.json)，支持 GMCM 游戏内配置 |
| `HorseFollower` | 下马后让当天骑过的马跟随玩家。 | [config.json](packages/HorseFollower/config.json) |
| `HotkeyViewer` | 在游戏内查看本体和已加载 Mod 的键鼠快捷键及潜在冲突。 | [config.json](packages/HotkeyViewer/config.json)，可选 GMCM |

所有包的目标框架都是 `net6.0`。包描述、版本、作者、`UniqueID` 和入口 DLL 发生变化时，应以对应的 `manifest.json` 为准。

## 安装与配置

1. 准备 SMAPI `4.0.0` 或更高版本，以及与当前游戏版本匹配的运行时。
2. 按[构建](#构建)生成包的 `dist/` 目录。
3. 将 `packages/<Package>/dist/` 下的内容复制到游戏目录的 `Mods/<Package>/`。如果目标目录已有 `config.json`，更新 DLL 时应保留现有配置，除非确认需要重置配置。
4. 启动 SMAPI，查看对应包的日志前缀和加载结果。

配置文件：

- [`Toolbox/config.json`](packages/Toolbox/config.json)：所有 Toolbox 功能共用一份配置。
- [`HorseFollower/config.json`](packages/HorseFollower/config.json)：跟随检查间隔、跟随距离和马棚取消范围。
- [`HotkeyViewer/config.json`](packages/HotkeyViewer/config.json)：打开快捷键查看器的按键。

### 可选的 GMCM 集成

- `Toolbox`：没有 Generic Mod Config Menu（GMCM）时主体功能仍会加载，但不会创建游戏内设置页；安装 GMCM 后可配置已注册的功能。
- `HotkeyViewer`：没有 GMCM 时仍可通过 `config.json` 修改打开键，但查看其他 Mod 快捷键时会更多依赖 `config.json` 推测。
- `HorseFollower`：当前只提供 `config.json` 配置，没有注册 GMCM 设置页。

## 各包功能

### Toolbox

`Toolbox`（`xixifu.Toolbox`，当前版本 `1.8.1`）整合以下功能：

- **动物自动抚摸**：仅在农场和畜棚中，自动抚摸扫描范围内尚未抚摸且友好度未满的动物。当前配置文件默认每 `60` 帧检查一次，扫描范围为 `3` 格。
- **光源半径调整**：分别调整家具光源和普通物体光源的半径倍率，修改后立即刷新当前场景；默认倍率均为 `1.5`。
- **自动输入法控制**：仅在 Windows 上，正常游戏操作时屏蔽系统输入法，进入文字输入状态时恢复；Android 和其他非 Windows 平台跳过该功能，不调用 Windows 原生 API。
- **栅栏防腐朽**：阻止栅栏和大门因时间流逝损失耐久。
- **自动开关门**：面对关闭的大门时自动打开，离开相邻格后按配置延迟关闭由本功能打开的大门，不强制关闭原本已打开的大门。
- **镰刀收割**：允许使用真正的镰刀收割作物、花朵和地面觅食物；不支持用剑代替镰刀。
- **快速堆叠到附近箱子**：在背包中将物品合并到配置距离内当前地点普通箱子或大箱子的已有相同堆叠；默认范围为 `14` 格，不处理冰箱等特殊库存。
- **穿行与碰撞**：允许按配置穿过作物、稻草人、洒水器、地面觅食物、茶树、树苗、果树和杂草，并可调整穿过时的减速、摇晃和声音行为。
- **NPC 地图与小地图**：在原版世界地图和 HUD 小地图上显示 NPC、特殊商人、多人农民和农场建筑的位置，支持按任务、生日、好感度、所在位置和已交谈状态筛选。小地图默认切换键为 `OemPipe`。
- **矿井梯子提示**：当前矿层连续破坏 `10` 块石头仍未出现梯子后，显示可能的下一层入口提示；这是固定规则，没有单独配置项。

#### 与独立 Mod 的兼容

Toolbox 会检测下列独立版本，并跳过对应的内置实现。它们不是 Toolbox 的必需依赖，同一功能只应保留一个版本：

| 独立 Mod UniqueID | 跳过的 Toolbox 功能 |
| --- | --- |
| `bcmpinc.HarvestWithScythe` | 镰刀收割 |
| `gaussfire.ConvenientInventory` | 快速堆叠 |
| `NCarigon.PassableCrops` | 穿行与碰撞 |
| `Bouhm.NPCMapLocations` | NPC 地图与小地图 |
| `ChaosEnergy.LadderLocator` | 矿井梯子提示 |

配置文件：[`packages/Toolbox/config.json`](packages/Toolbox/config.json)

### HorseFollower

`HorseFollower`（`xixifu.HorseFollower`，当前版本 `1.6.0`）当前发布模式只启用下马后跟随，骑乘自动导航暂时关闭。

- **下马后跟随**：玩家当天骑马后下马，马匹在与玩家同一场景且不在所属马棚附近时开始跟随。
- **同场景寻路**：按马匹实际碰撞范围规划八方向路线，并在玩家移动、路线受阻或距离变化时重新规划。
- **跨室外地图跟随**：玩家通过原版室外地图的普通步行出口换图时，马匹会依次走到相同出口并跨图继续跟随。
- **传送边界**：不会通过室内入口、公交、矿车、图腾、权杖等特殊传送建立跨图路线；马匹会停在最后一个可达的室外地图。
- **跟随范围**：默认距离玩家 `4` 格内停止追赶，超过 `6` 格后重新开始追赶；在所属马棚 `3` 格范围内下马会取消本次跟随。
- **生命周期**：新的一天会清除跟随状态，重新骑马后才会建立新的跟随会话。

> 骑乘自动导航的目的地菜单、HUD 入口和导航事件代码仍保留在包内，但当前 `ModEntry` 不注册这些事件，因此游戏中不会显示或启用骑乘导航。

配置文件：[`packages/HorseFollower/config.json`](packages/HorseFollower/config.json)

| 配置项 | 默认值 | 作用 |
| --- | ---: | --- |
| `CheckInterval` | `10` | 跟随服务的检查间隔（帧）。 |
| `FollowDistance` | `4` | 马进入此距离后停止追赶（格）。 |
| `FollowStartDistance` | `6` | 马超过此距离后重新追赶（格）。 |
| `StableRadius` | `3` | 所属马棚周围取消跟随的范围（格）。 |

### HotkeyViewer

`HotkeyViewer`（`xixifu.HotkeyViewer`，当前版本 `1.0.0`）用于集中排查游戏和 Mod 的键鼠快捷键：

- 默认按 `?` 所在的 `OemQuestion` 按键打开面板，也可通过 GMCM 或配置文件修改。
- 收集星露谷物语 `Game1.options` 中的键鼠按键。
- 优先读取 GMCM 注册的 Mod 按键选项；无法读取时，再扫描已加载 Mod 的 `config.json` 推测可能的快捷键。
- 按相同按键组合标记潜在冲突，并支持按来源、冲突状态和文本搜索筛选。
- 默认不纳入手柄按键，避免键鼠排查场景出现噪音。
- 配置推测会过滤疑似 API key、token、密码、凭证和私钥等敏感字段。
- 快捷键只会在没有其他活动菜单、事件或对话时打开；再次按打开键可关闭已打开的面板。

配置文件：[`packages/HotkeyViewer/config.json`](packages/HotkeyViewer/config.json)

## 项目结构

```text
StardewMods.sln          # Visual Studio / dotnet solution
Directory.Build.props    # 所有包共用的 C#、Nullable 和确定性构建设置
packages/
├── Toolbox/             # 工具箱：便利功能、NPC 地图和矿井梯子提示
├── HorseFollower/       # 马匹跟随；骑乘自动导航当前关闭
└── HotkeyViewer/        # 快捷键查看器
```

每个包通常包含：

- `*.csproj`：目标框架、游戏程序集和 Mod API 引用；
- `manifest.json`：SMAPI 包身份和入口 DLL；
- `config.json`：默认配置；
- `*.cs`：实现代码；
- `dist/`：构建后生成的 DLL、`manifest.json`、`config.json` 和必要资源，该目录不纳入 Git。

## 构建

构建需要 .NET 6 SDK，以及包含目标 SMAPI 和游戏程序集的《星露谷物语》运行时目录。项目直接引用游戏安装目录中的程序集，不通过 NuGet 下载这些运行时文件。

通过 `GamePath` 指定游戏目录：

```powershell
dotnet build StardewMods.sln -c Release -p:GamePath="D:\Games\Stardew Valley"
```

也可以通过 `StardewValleyPath` 提供默认路径。构建单个包时，将解决方案路径替换为对应的项目文件：

```powershell
dotnet build packages/Toolbox/Toolbox.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
dotnet build packages/HorseFollower/HorseFollower.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
dotnet build packages/HotkeyViewer/HotkeyViewer.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
```

构建完成后，完整可部署包位于对应的 `packages/<Package>/dist/`。其中：

- `Toolbox` 还会复制 `assets/quickStackIcon.png`；
- 生成 `dist/` 前会清空旧输出，因此不要把用户运行中的配置目录直接设为 `ModOutputPath`；
- 安装更新时应保留游戏 `Mods/<Package>/config.json` 中已有的用户配置。

## 实现文档

- [工具箱包说明](.codestable/architecture/packages/toolbox.md)
- [马匹跟随包说明](.codestable/architecture/packages/horse-follower.md)
- [快捷键查看器包说明](.codestable/architecture/packages/hotkey-viewer.md)
