---
处理方式: 调查
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: [decisions/03-riding-state.md]
---

# 入口按钮与店铺弹窗

## 问题

如何在骑马时稳定显示屏幕右下角入口按钮，并以不破坏原版输入和菜单的方式展示 11 个目的地、取消和状态反馈？

## 答案

- `RenderedHud` 只在世界已加载、玩家骑马、当前地图属于支持的室外地图且没有活动菜单时绘制右下角按钮；按钮使用游戏 UI 坐标和原版纹理框。
- `ButtonPressed` 只接管按钮矩形内的鼠标左键，并抑制这次点击；方向键取消逻辑优先于按钮点击，但菜单打开时方向键只交给菜单。
- 点击按钮把 `HorseNavigationMenu` 放入 `Game1.activeClickableMenu`。弹窗列出固定 11 个目的地，支持鼠标、Escape、方向键和手柄；社区中心在缺少 `ccDoorUnlock` 与 `JojaMember` 时保留在列表中但置灰并显示“尚未开放”。
- 自动导航中的菜单由 `MenuChanged` 和每 tick 的活动菜单状态暂停，关闭后恢复原规划/行驶状态；完成、取消和失败状态在 HUD 上显示结果类别，并以 `[HorseFollower]` Trace 日志记录具体原因。
- HorseFollower 只依赖 Stardew Valley/SMAPI 的 UI API，不依赖 Toolbox 的运行时类型。

## 依据

- 代码：`packages/HorseFollower/HorseNavigationService.cs`、`HorseNavigationMenu.cs`、`ModEntry.cs`。
- API：SMAPI `RenderedHud`、`ButtonPressed`、`MenuChanged`，游戏 `Game1.activeClickableMenu` / `IClickableMenu`。
- [自动驾驶状态机](03-riding-state.md)规定菜单暂停和方向键取消的状态边界。

