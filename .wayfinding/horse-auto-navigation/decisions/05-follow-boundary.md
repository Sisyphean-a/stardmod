---
处理方式: 调查
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: []
---

# 跟随与自动导航边界

## 问题

自动导航如何与现有下马跟随会话、HorsePathSearch、HorseFollowController 和 OutdoorWarpTracker 隔离或复用，才能保持既有行为不变？

## 答案

现有下马跟随和骑乘自动导航是两个互斥的移动模式，边界如下：

- `HorseFollowerService`、`HorseFollowController`、`HorsePathSearch` 和 `OutdoorWarpTracker` 保持现有职责，只服务于玩家下马后的跟随会话。骑乘时不建立或推进 `followSessionActive`，不复用跟随 controller、失败缓存、速度备份和动画状态。
- 自动导航使用独立的骑乘导航状态和控制器，并由共享的导航模式协调保证同一时刻只有一个移动拥有者。自动导航期间跟随服务只能被动观察，不能调用 `StopFollowController`、恢复跟随速度或清理会影响骑乘动画的状态。
- 自动控制的移动源是 `Farmer`/骑乘玩家，而不是 `Horse`。原版 `Horse.SyncPositionToRider` 会在骑乘时把马的位置同步为玩家位置；直接给 `HorseFollowController` 或 `horse.controller` 接管会与原版同步冲突。骑乘导航应复用玩家的碰撞、方向、动画和移动语义，马由原版骑乘更新同步位置与六帧步态。
- 可复用的是地图碰撞判断、八方向无穿角规则、增量 A* 的搜索思路和普通室外 Warp 的校验方式；需要为骑乘玩家提供独立的路径/控制器入口，不能把现有只接受 `Horse` 的搜索和 controller 原样套用。
- `OutdoorWarpTracker` 明确拒绝 `player.mount != null`，因此自动导航使用独立的室外路线跟踪器。自动导航只接受预期的普通室外 Warp；`Game1.ShouldDismountOnWarp` 表明室外到室外换图不会强制下马，室内或非普通传送则取消导航并交还玩家。
- 自动导航取消或到达后只清除自己的路线和控制权，玩家仍保持骑乘；之后玩家正常下马时，原有 `wasMounted` 流程照常决定是否建立跟随会话。

## 依据

- `HorseFollowerService.OnUpdateTicked` 在发现 `Game1.player.mount` 后会停止跟随 controller、清理跟踪器并把 `followSessionActive` 置为 `false`；只有玩家随后下马才调用 `BeginFollowSession`。
- `OutdoorWarpTracker.CaptureCandidate` 要求 `followSessionActive` 且拒绝已骑乘玩家；`HandlePlayerWarp` 也只接受跟随会话和未脱离的马匹路线。
- `HorseFollowController` 直接挂到 `horse.controller` 并移动马；原版 `Horse.update` 在有 rider 时调用 `SyncPositionToRider`，其位置源是 rider。
- 原版 `Farmer.MovePosition`/`MovePositionImpl` 承担玩家的碰撞和方向移动；原版 `Game1.ShouldDismountOnWarp` 仅在目标地图不是室外时要求下马。
- 现有边界与锚点：`packages/HorseFollower/HorseFollowerService.cs`、`HorseFollowController.cs`、`HorsePathSearch.cs`、`OutdoorWarpTracker.cs`。

