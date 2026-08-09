---
处理方式: 调查
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: []
---

# 店铺入口与安全停车点

## 问题

11 个目的地在当前游戏版本分别对应哪个室外地图、店铺入口和入口外安全停车候选；如何保证停车不堵门？

## 调查结果

当前本地游戏程序集版本为 `1.6.15.24356`。从 `Content/Maps/*.xnb` 的 `Buildings` 层入口动作读取到以下固定锚点；`ScienceHouse (8,20)` 是 Maru 的门，不是木匠店，木匠店使用 `(12,25)` 的公共入口。

| 目的地 | 室外地图 | 入口 tile | 原版入口动作 | 2 格安全停车基准 |
| --- | --- | --- | --- | --- |
| 皮埃尔杂货店 | `Town` | `(43,56)`、`(44,56)` | `LockedDoorWarp ... SeedShop` | `(43,58)` 或 `(44,58)` |
| 乔家超市 | `Town` | `(95,50)`、`(96,50)` | `LockedDoorWarp ... JojaMart` | `(95,52)` 或 `(96,52)` |
| 铁匠铺 | `Town` | `(94,81)` | `LockedDoorWarp ... Blacksmith` | `(94,83)` |
| 星露谷酒吧 | `Town` | `(45,70)` | `LockedDoorWarp ... Saloon` | `(45,72)` |
| 哈维诊所 | `Town` | `(36,55)` | `LockedDoorWarp ... Hospital` | `(36,57)` |
| 博物馆/图书馆 | `Town` | `(101,89)` | `LockedDoorWarp ... ArchaeologyHouse` | `(101,91)` |
| 社区中心 | `Town` | `(52,19)`、`(53,19)` | `WarpCommunityCenter` | `(52,21)` 或 `(53,21)` |
| 木匠店 | `Mountain` | `(12,25)` | `LockedDoorWarp ... ScienceHouse` | `(12,27)` |
| 冒险家公会 | `Mountain` | `(76,8)` | `LockedDoorWarp ... AdventureGuild` | `(76,10)` |
| 玛妮牧场 | `Forest` | `(90,15)` | `LockedDoorWarp ... AnimalShop` | `(90,17)` |
| 威利鱼店 | `Beach` | `(30,33)` | `LockedDoorWarp ... FishShop` | `(30,35)` |

静态地图检查以约 `96x32` 的马碰撞框验证了这些基准点周围的门口外空间；它只能作为候选顺序，不能替代运行时判断。

停车实现应把入口 tile 集合视为禁止目标区域：优先尝试表中的 2 格基准及同一门洞两侧的固定候选，只接受满足以下条件的点：

1. 与所有入口 tile 保持至少 2 格距离，不直接占用门洞或其相邻格。
2. 使用当前马的 `GetBoundingBox()` 调用现有碰撞语义验证可站，包含地图静态碰撞、Action 门 tile 和运行时物体。
3. 通过现有马匹 A* 路径搜索确认从当前位置可达。
4. 没有候选点时显式报告“入口外没有可用停车点”，不把马停在门口，也不进入室内。

## 答案

11 个目的地使用调查结果中的固定室外入口锚点和 2 格停车基准。入口锚点以当前地图 `Buildings` 层的原版入口动作作为身份，不进入对应室内地图；木匠店使用 `Mountain (12,25) -> ScienceHouse`，不误用 Maru 的 `ScienceHouse (8,20)` 入口。

停车目标必须与该目的地的全部入口 tile 保持至少 2 格距离。实现时按表中基准和同一门洞两侧的固定候选顺序尝试，并用当前马的真实碰撞框、地图碰撞和现有 A* 路径验证；没有可用候选时显式失败，不堵门、不进入室内。社区中心未解锁时遵循[社区中心未解锁时的目的地状态](08-community-center-availability.md)，由 UI 置灰，不启动导航。

## 依据

- 游戏程序集：`D:\SteamLibrary\steamapps\common\Stardew Valley\Stardew Valley.dll`，文件版本 `1.6.15.24356`。
- 地图资源：`Content/Maps/Town.xnb`、`Mountain.xnb`、`Forest.xnb`、`Beach.xnb`。
- 原版 `GameLocation.getWarpFromDoor` 将 `LockedDoorWarp`、`WarpCommunityCenter` 等入口动作转换为室内 Warp；`GameLocation.performAction` 对社区中心额外检查 `ccDoorUnlock` / `JojaMember`。
- 原有 `HorsePathSearch` 已使用马的真实碰撞框和 `GameLocation.isCollidingPosition`，可作为停车候选的运行时验证语义。
