---
scope: package:horse-follower
---

# 马匹跟随包

`packages/HorseFollower` 是用户明确指定为独立 mod 的马匹跟随包，运行时身份为 `xixifu.HorseFollower`，入口程序集为 `HorseFollower.dll`。

## 职责

- 记录玩家当天骑乘并下马的马匹。
- 在同一场景中让马按实际碰撞范围规划路线并跟随玩家，保持约 `FollowDistance` 格距离。
- 跟随移动使用与骑乘时相同的六帧马匹步态，避免未骑乘 Horse 默认动画造成卡顿感。
- 玩家换场景时暂停马匹跟随，不把马带入新场景；玩家回到马所在场景后恢复。
- 在马棚内部或马棚周围 `StableRadius` 格范围内下马时取消本次跟随。
- 新的一天清除跟随状态，重新骑马后才建立新的跟随会话。

## 边界与锚点

- SMAPI 入口和事件注册：`packages/HorseFollower/ModEntry.cs`。
- 状态机与寻路：`packages/HorseFollower/HorseFollowerService.cs`。
- 配置契约：`packages/HorseFollower/ModConfig.cs`、`packages/HorseFollower/config.json`。
- 包身份：`packages/HorseFollower/manifest.json`。
- 构建和游戏程序集引用：`packages/HorseFollower/HorseFollower.csproj`。

## 运行约束

- 跟随只针对玩家实际骑乘后下马的马；重新骑马会结束当前跟随会话。
- `HorseFollowController` 使用马的真实碰撞框规划路线，并在一个游戏更新内连续消费路点和位移预算，避免逐格空帧或起步回正。
- 移动直接更新位置，不调用会同时驱动默认四帧动画的 `Character.MovePosition`；方向变化时切换骑乘六帧步态，移动期间不重启动画。
- 玩家目标移动、路线卡住或路线结束后重新规划；失败路径按 `CheckInterval` 派生的冷却重试，外部 controller 临时接管时不抢占。
- 跟随距离使用三格停止、三格半启动的像素级滞回；速度按玩家步行速度加随距离递增的追赶量计算，避免突进式追赶，结束时恢复马的原始速度。
- 稳定范围以马匹所属 Stable 为准，不会因为靠近其他马棚而取消跟随。
