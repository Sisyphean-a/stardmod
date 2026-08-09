---
处理方式: 调查
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: []
---

# 游戏地图与 API 证据

## 问题

当前 Stardew Valley/SMAPI 版本中，11 个地点的地图名称、室外步行 Warp、骑马可行性、UI 入口和菜单输入 API 的实际契约是什么？

## 答案

当前本地运行环境为 Stardew Valley `1.6.15.24356`、SMAPI `4.5.2`。

### 地图与普通 Warp

- 11 个目的地的室外地图只有 `Town`、`Mountain`、`Forest`、`Beach`；四张地图资源都标记 `Outdoors=T`，并在现有 `OutdoorWarpTracker` 的室外白名单中。
- 当前地图的普通跨图出口来自地图属性 `Warp`，原版 `GameLocation.updateWarps` 按五个字段解析：`fromX fromY targetLocation toX toY`。`NPCWarp`、门的 `LockedDoorWarp`、`WarpCommunityCenter` 和其他 Action 不属于普通步行出口，路线图必须排除它们。
- 当前目标相关的室外连接至少包括：`Town` ↔ `BusStop` / `Mountain` / `Forest` / `Beach`，`Mountain` ↔ `Town` / `Railroad` / `Backwoods`，`Forest` ↔ `Town` / `Farm` / `Woods`，`Beach` ↔ `Town`；实际出口坐标已由[店铺入口与安全停车点](01-destination-anchors.md)和本地地图资源调查确认。
- 原版 `Game1.ShouldDismountOnWarp` 只在目标地图不是室外时要求下马，因此普通室外到室外 Warp 可以保持骑乘；自动导航仍需在 `Warped` 事件中校验实际目标是否符合预期。

### UI 与输入 API

- SMAPI `Display.RenderedHud` 提供 `SpriteBatch`，适合在原版 HUD 绘制完成后绘制右下角入口；仓库现有 UI 使用 `Game1.uiViewport` / `Game1.getMouseX(true)` 等游戏 UI 坐标语义。
- SMAPI `Input.ButtonPressed` 提供 `SButton`、光标位置和抑制状态，适合处理 HUD 按钮点击及方向键取消；`ButtonsChanged` 提供 `Pressed`、`Held`、`Released`，需要连续按键状态时使用。不能硬编码 WASD，应沿用玩家当前移动方向/按键绑定。
- SMAPI `Display.MenuChanged` 可观察弹窗生命周期；游戏 `Game1.activeClickableMenu` 接受 `IClickableMenu`。自定义目的地弹窗可继承 `IClickableMenu`，实现 `draw`、`receiveLeftClick`、`receiveKeyPress`、`receiveGamePadButton` 和 `readyToClose`，关闭使用 `exitThisMenu`。
- 仓库 `ToolboxOptionsPage` 已验证 `IClickableMenu`、`drawDialogueBox`、矩形点击区域和原版字体/纹理的使用方式；HorseFollower 只能复用 API 约定，不能依赖 Toolbox 运行时包。

### 对实现的约束

- 目的地目录读取固定入口锚点；普通跨图路线只读取地图 `Warp`，入口 Action 只用于最终停车目标和可用性判断。
- HUD 按钮只在世界已加载、玩家骑马且没有活动菜单时绘制；弹窗打开后由原版菜单输入分发负责鼠标、键盘和手柄。
- 自动导航的方向键取消应在 `ButtonPressed`/玩家移动状态进入普通移动前生效；菜单打开期间路线控制器只暂停，不继续写入玩家位置。

## 依据

- 游戏程序集：`D:\SteamLibrary\steamapps\common\Stardew Valley\Stardew Valley.dll`，文件版本 `1.6.15.24356`。
- SMAPI 程序集：`D:\SteamLibrary\steamapps\common\Stardew Valley\StardewModdingAPI.dll`，文件版本 `4.5.2.0`。
- 原版 API：`GameLocation.updateWarps`、`GameLocation.isCollidingWithWarp`、`Game1.ShouldDismountOnWarp`、`Game1.activeClickableMenu`、`IClickableMenu`。
- SMAPI API：`IDisplayEvents.RenderedHud`、`IInputEvents.ButtonPressed` / `ButtonsChanged`、`MenuChangedEventArgs`。
- 代码锚点：`packages/HorseFollower/OutdoorWarpTracker.cs`、`packages/HorseFollower/ModEntry.cs`、`packages/Toolbox/ToolboxOptionsPage.cs`、`packages/Toolbox/ToolboxOptionsTab.cs`。

