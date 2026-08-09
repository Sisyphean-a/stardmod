---
处理方式: 裁决
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: [decisions/02-route-model.md, decisions/05-follow-boundary.md]
---

# 自动驾驶状态机

## 问题

自动寻路如何在按钮打开、路线建立、同图行驶、跨图等待、暂停、取消、失败、到达和重新骑乘之间转换？

## 答案

采用独立的骑乘导航状态机，自动控制权归 `Farmer.controller`，马匹继续由原版骑乘同步跟随玩家。状态机保留终态结果，直到下一次导航、天数重置或返回标题；所有终态都释放玩家控制权并保持玩家骑乘。

### 状态

- `Idle`：没有自动导航。
- `Planning`：目的地已选，正在分帧构建跨图路线；此时不移动玩家。
- `Navigating`：当前室外 segment 已确认，由独立的骑乘玩家 controller 消费路点。
- `WaitingForWarp`：已触发计划中的普通室外出口，等待 `Player.Warped` 确认目标地图。
- `Paused`：原状态是 `Planning` 或 `Navigating`，因活动菜单暂停；保存恢复状态、路线游标和缓存，不重新规划。
- `Completed`：已到最终停车候选，停止 controller，不进入室内、不堵门。
- `Canceled`：玩家主动按方向键，或主动结束骑乘导航。
- `Failed`：路线不可达、计划外传送、异常下马、地图/碰撞失效或控制权被其他 controller 抢走。

`Completed`、`Canceled`、`Failed` 都记录原因和最后目标，供 HUD/日志显示；不会自动把失败伪装成成功，也不会回退到下马跟随。

### 关键转换

| 当前状态 | 触发 | 下一状态与动作 |
| --- | --- | --- |
| `Idle` / 终态 | 选择可用目的地且玩家仍骑马、处于支持的室外地图 | `Planning`；锁定目标马匹、路线版本和目标停车候选 |
| `Planning` | 路线计划完成 | `Navigating`；为当前 segment 创建骑乘玩家 controller |
| `Planning` | 无可达路线/停车点 | `Failed`；清理计划并交还控制 |
| `Planning` / `Navigating` | 玩家按当前设置的任一方向键 | `Canceled`；在原版移动处理前清除 controller，不抑制这次方向输入 |
| `Planning` / `Navigating` | `Game1.activeClickableMenu` 变为非空 | `Paused`；停止/暂停路线推进，菜单期间不写玩家位置 |
| `Paused` | 活动菜单关闭且骑乘、地图和路线仍有效 | 恢复进入 `Planning` 或 `Navigating`；继续原游标，不重复整条规划 |
| `Navigating` | 到达当前 segment 的普通出口并触发预期 Warp | `WaitingForWarp`；清除当前 controller，等待 Warp 事件 |
| `WaitingForWarp` | `Warped` 的旧地图、新地图、目标 tile 与计划边匹配 | 有剩余 segment 则 `Navigating`，否则继续最终停车 segment |
| `WaitingForWarp` | 室内、特殊传送、目标地图或目标 tile 不匹配 | `Failed`；立即交还玩家 |
| `Navigating` | 到达安全停车候选并满足真实碰撞/门口距离验证 | `Completed`；停止 controller，保持骑乘 |
| 任一活动状态 | 下马、骑乘马匹被替换、天数/存档生命周期结束 | `Canceled` 或清理到 `Idle`；不建立下马跟随会话 |

### 控制器与事件职责

- 新增独立的骑乘玩家路径 controller，挂到 `Game1.player.controller`；它只消费玩家路径 segment，不复用 `HorseFollowController`、`HorsePathSearch` 的马匹实例、跟随失败缓存或速度备份。
- controller 通过 `Farmer.MovePosition` 的原版碰撞/方向语义移动玩家；不直接写 `Farmer.Position`，也不直接驱动 `Horse`。原版 `Horse.SyncPositionToRider` 负责马的位置和骑乘动画。
- `Input.ButtonPressed` 在方向键到达原版移动前执行取消；方向键判断沿用当前游戏绑定，不能硬编码 WASD。菜单中的方向键只操作菜单，不取消被暂停的路线。
- `Player.Warped` 只接受当前计划的本地普通室外 Warp，并校验旧地图、新地图和落点 tile；事件成功后推进路线游标，下一更新周期再创建下一个 segment。
- `UpdateTicking`/`UpdateTicked` 负责世界状态、骑乘身份、计划构建和终态检查；活动菜单期间不推进搜索或移动。`MenuChanged` 可用于维护暂停/恢复显示，但路线正确性以每 tick 的实际菜单状态为准。
- 与现有 `HorseFollowerService` 只通过“互斥移动拥有者”隔离：骑乘导航不设置 `followSessionActive`，现有服务发现骑乘后继续清理自己的下马跟随状态；自动导航结束后玩家下马，原有流程才可重新建立跟随会话。

## 依据

- 现有 `HorseFollowerService.OnUpdateTicked` 在玩家骑马时停止马 controller、清理下马路线并将 `followSessionActive` 置为 `false`；`OutdoorWarpTracker` 也明确拒绝 mounted player。
- `Farmer.Update` 在 `controller` 非空时调用 `controller.update(time)`，否则调用 `MovePosition`；`PathFindController.update` 在 `Game1.activeClickableMenu` 非空时不推进移动。因此骑乘导航可挂玩家 controller 并天然实现菜单暂停。
- `Farmer.MovePositionImpl` 负责按方向调用 `isCollidingWithWarp`、执行普通 Warp、检查碰撞并更新玩家位置；直接改马位置会被原版骑乘同步覆盖。
- `Game1.ShouldDismountOnWarp` 证明室外到室外 Warp 不强制下马；[跟随与自动导航边界](05-follow-boundary.md)已确定两种移动模式互斥。
- SMAPI `ButtonPressed`、`ButtonsChanged`、`MenuChanged` 与游戏 `Player.Warped` 的实际 API 已记录在[游戏地图与 API 证据](06-game-facts.md)。

