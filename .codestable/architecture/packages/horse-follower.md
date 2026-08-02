---
scope: package:horse-follower
---

# 马匹跟随包

`packages/HorseFollower` 是用户明确指定为独立 mod 的马匹跟随包，运行时身份为 `xixifu.HorseFollower`，入口程序集为 `HorseFollower.dll`。

## 职责

- 记录玩家当天骑乘并下马的马匹。
- 在同一场景中让马按实际碰撞范围规划八方向路线并跟随玩家，保持约 `FollowDistance` 格距离。
- 跟随移动使用与骑乘时相同的六帧马匹步态，避免未骑乘 Horse 默认动画造成卡顿感。
- 玩家通过原版室外地图的普通步行出口换图时，马依次走到相同出口并跨图继续跟随。
- 房屋等室内入口以及图腾、权杖、矿车、公交等非步行传送不会建立跨图路线；马停留在最后一个可达室外地图。
- 在马棚内部或马棚周围 `StableRadius` 格范围内下马时取消本次跟随。
- 新的一天清除跟随状态，重新骑马后才建立新的跟随会话。

## 边界与锚点

- SMAPI 入口和事件注册：`packages/HorseFollower/ModEntry.cs`。
- 状态机、路线调度与失败缓存：`packages/HorseFollower/HorseFollowerService.cs`。
- 增量 A* 路线搜索：`packages/HorseFollower/HorsePathSearch.cs`。
- 原版室外步行出口识别与跨图队列：`packages/HorseFollower/OutdoorWarpTracker.cs`。
- 配置契约：`packages/HorseFollower/ModConfig.cs`、`packages/HorseFollower/config.json`。
- 包身份：`packages/HorseFollower/manifest.json`。
- 构建和游戏程序集引用：`packages/HorseFollower/HorseFollower.csproj`。

## 运行约束

- 跟随只针对玩家实际骑乘后下马的马；重新骑马会结束当前跟随会话。
- `HorsePathSearch` 使用马的真实碰撞框以固定节点预算分帧规划八方向路线；找到路线后由 `HorseFollowController` 连续消费受限数量的路点和位移预算，避免逐格空帧、起步回正或高移速时阻塞帧。
- 斜向移动同时使用完整横轴和纵轴速度，与玩家同时按两个方向键一致；只有目标格和两个相邻直角方向均可通行时才允许斜穿。
- 移动直接更新位置，不调用会同时驱动默认四帧动画的 `Character.MovePosition`；方向变化时切换骑乘六帧步态，移动期间不重启动画。
- 玩家目标移动、路线卡住或路线结束后重新规划；同一位置、同一目标的失败路线会缓存，直到马或目标明显变化才重试，外部 controller 临时接管时不抢占。
- 跨图只接受玩家主动移动触发、且前后都在原版可骑马室外白名单中的普通 `Warp`；离屏地图继续更新马的 controller，到达出口或其周围最近可站区域后使用游戏原生角色迁移 API 进入下一地图。
- 连续室外步行换图按出口顺序排队；玩家折返会截短路线，室外传送会清空路线，进入室内则保留已有路线并让马停在最后一个室外地图。普通出口候选在淡出期间保留至 `Warped` 事件，Trace 日志记录出口捕获、排队、无路线和迁移。
- 跟随距离使用三格停止、三格半启动的像素级滞回；速度按玩家步行速度加随距离递增的追赶量计算，避免突进式追赶，结束时恢复马的原始速度。
- 稳定范围以马匹所属 Stable 为准，不会因为靠近其他马棚而取消跟随。
