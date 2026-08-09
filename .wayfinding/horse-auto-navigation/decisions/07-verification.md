---
处理方式: 前置
状态: 关闭
认领者: "019fe46e-4c05-73b7-8bc6-5e9af0779752"
硬依赖: [decisions/01-destination-anchors.md, decisions/02-route-model.md, decisions/03-riding-state.md, decisions/04-ui-entry.md, decisions/05-follow-boundary.md, decisions/06-game-facts.md]
---

# 测试与安装验证

## 问题

如何用可重复的静态检查、构建、运行时验证和游戏安装检查证明新增自动寻路及原有跟随逻辑均满足完成判断？

## 答案

本次实现已完成静态、构建、安装和独立审查验证；游戏内手动驾驶测试未在本环境自动执行，作为明确残余风险保留。

### 已完成验证

- 本地地图资源和程序集调查确认 Stardew Valley `1.6.15.24356` 的 11 个固定入口锚点、入口外停车候选和普通室外 Warp 网络。
- 源码静态检查确认目的地数量为 11，社区中心同时检查 `ccDoorUnlock` / `JojaMember`，HUD、目的地菜单和骑乘玩家 controller 均已接线。
- `dotnet build packages/HorseFollower/HorseFollower.csproj --no-restore -p:GamePath="D:\\SteamLibrary\\steamapps\\common\\Stardew Valley"` 通过，0 警告、0 错误。
- `dotnet build StardewMods.sln --no-restore -p:GamePath="D:\\SteamLibrary\\steamapps\\common\\Stardew Valley"` 通过，Toolbox、HorseFollower、HotkeyViewer 均成功，0 警告、0 错误。
- `git diff --check` 无差异错误；仅有仓库现有的换行符提示。
- 独立对抗审查完成；初次发现的外部 controller 覆盖、起点即目标误判和 Warp 重入边界均已修复，最终审查无 P0-P2 阻塞。
- 最新 `packages/HorseFollower/dist/HorseFollower.dll` 与游戏安装目录 `Mods/HorseFollower/HorseFollower.dll` SHA-256 一致：`9a7b737d000d679a5c8925f86e1b43fb7b91cd8d7ad865fd6a9eb92da9dfc411`；manifest 也一致。安装过程保留了原有 `config.json`。

### 残余风险

- 首次实机观察发现导航规划成功但出口 A* 不动：日志显示 `navigation-plan` 后连续 `navigation-replan reason=计划出口不可达`。根因是把地图边界/阻挡的原始 Warp 源 tile 当成可站终点；现已改为从当前方向接近的地图内侧 tile，并保留原版 Warp 触发校验。
- 修复后尚未再次执行真实存档中的完整骑马、跨图、菜单暂停、方向键取消和最终停车验证；新 DLL 已重新构建并安装，需以新日志确认跨图继续和停车结果。
- 仓库没有现成自动化测试工程，因此没有新增低价值的模拟游戏测试；失败会通过 `[HorseFollower]` Trace 日志显式暴露。

## 依据

- 代码和配置：`packages/HorseFollower`。
- 构建产物：`packages/HorseFollower/dist`。
- 安装目标：`D:\\SteamLibrary\\steamapps\\common\\Stardew Valley\\Mods\\HorseFollower`。
- [店铺入口与安全停车点](01-destination-anchors.md)、[跨图路线模型](02-route-model.md)、[自动驾驶状态机](03-riding-state.md)、[入口按钮与店铺弹窗](04-ui-entry.md)、[游戏地图与 API 证据](06-game-facts.md)。

