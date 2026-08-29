---
scope: package:story-data-collector
---

# 故事数据采集器

`packages/StoryDataCollector` 是独立的 Stardew Valley / SMAPI 数据采集 mod，运行时身份为 `xixifu.StoryDataCollector`，入口程序集为 `StoryDataCollector.dll`。它当前只负责采集和保存游戏事实，不调用 AI、不生成故事、不提供游戏内故事 UI；长期产品主线由[连续故事日记上下文](../../requirements/contexts/story-data-collector.md)定义，本页只描述现行代码边界。

## 职责

- 以一个 `DailyRecord` 保存当天的日期、环境上下文、地点区间、事件时间线和辅助统计。
- 以 `GameEvent` 保存时间、地点、参与者、详情、重要度和 `Evidence`（`Observed` / `Unknown`）；不在数据层做文学推断。`SaveRawEvents` 开启时，JSON 额外保存名为 `DebugRawEvents` 的诊断副本；Phase 1 尚未启用聚合器，因此它可能与核心事件内容相同。
- 维护从当天首次可观察位置开始、由本地玩家 `Warped` 事件切分的 `LocationStay`；地点使用 `NameOrUniqueName` 和可读显示名，不使用原版地点枚举白名单。
- Phase 1 采集 NPC 对话、礼物、商店购买、无法归因的金钱变化、实际睡眠、疲劳昏倒和生命值归零，并通过 `Saving`、定时 checkpoint 和 `DayEnding` 写入 `data/<SaveUniqueId>/Year<year>-<Season>-<day>.json`。
- 同一 mod 拥有独立 manifest、配置和构建输出，不与 `Toolbox` 共用运行时身份或配置。

## 入口与代码锚点

- SMAPI 生命周期和事件注册：`packages/StoryDataCollector/ModEntry.cs`。
- 统一事件、地点区间、checkpoint 和 JSON 持久化：`packages/StoryDataCollector/TimelineDataCollector.cs`。
- 数据模型：`GameEvent.cs`、`DailyRecord.cs`、`LocationStay.cs`。
- 无可靠 SMAPI 事件时的集中 Harmony 入口：`packages/StoryDataCollector/HarmonyPatches.cs`。
- 配置和包身份：`ModConfig.cs`、`config.json`、`manifest.json`。
- 构建与 SMAPI / 游戏程序集引用：`StoryDataCollector.csproj`。

## 数据来源和边界

- 纯 SMAPI：`SaveLoaded`、`DayStarted`、`DayEnding`、`Saving`、`ReturnedToTitle`、`UpdateTicked`、本地玩家 `Player.Warped`。
- Harmony：`NPC.checkAction` 在同步打开对话框后记录 `NpcTalk`；`NPC.receiveGift` 记录实际可接收的礼物及喜好；`ShopMenu.tryToPurchaseItem` 记录成功购买并将已知金钱扣除与 `MoneyChanged` 兜底关联；`GameLocation.doSleep` 只在多人准备完成、实际进入睡眠流程时记录 `Sleep`；`Farmer.passOutFromTired` 记录疲劳或过晚昏倒。
- 生命值归零由 `UpdateTicked` 观察 `Farmer.health` 的正数到零变化，避免把可被护符即时恢复的短暂中间状态误记为昏倒。
- Harmony 目标逐个解析和注册；目标不存在或补丁失败只记录 Warning，对应采集项为 `Unsupported`，不阻止整个 mod 加载；启动日志和 `story_data status` 会列出每个入口的 `Supported` / `Unsupported` 状态。
- 金钱无法与实际行为关联时只写 `MoneyChanged`，详情中的 `reason` 保持 `Unknown`，不猜测购买或消费原因。

## 当前未实现

- Phase 2 的农业、动物、钓鱼、矿洞和战斗会话聚合。
- Phase 3 的任务、成就、节日、心事件、电影、制作、烹饪、机器、稀有物品、世界进度和 Nearby NPC 快照。
- AI 调用、故事生成、日记 UI 和跨 mod 复杂事件总线。
