---
scope: package:horse-follower
---

# 马匹跟随包

`packages/HorseFollower` 是用户明确指定为独立 mod 的马匹跟随包，运行时身份为 `xixifu.HorseFollower`，入口程序集为 `HorseFollower.dll`。

## 职责

- 记录玩家当天骑乘并下马的马匹。
- 在同一场景中让马通过原生寻路跟随玩家，保持约 `FollowDistance` 格距离。
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
- `PathFindController` 只在马与玩家仍处于同一 `GameLocation` 时运行，避免跨场景追随。
- 稳定范围以马匹所属 Stable 为准，不会因为靠近其他马棚而取消跟随。
