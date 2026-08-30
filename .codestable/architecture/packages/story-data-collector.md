---
scope: package:story-data-collector
---

# 故事数据采集器

`packages/StoryDataCollector` 是独立的 Stardew Valley / SMAPI 数据采集 mod，运行时身份为 `xixifu.StoryDataCollector`，入口程序集为 `StoryDataCollector.dll`。它当前采集并保存游戏事实，同时在日结生成有界的 AI 叙事输入；它不调用 AI、不生成故事、不提供游戏内故事 UI。长期产品主线由[连续故事日记上下文](../../requirements/contexts/story-data-collector.md)定义，本页只描述现行代码边界。

## 职责

- 以一个 `DailyRecord` 保存当天的日期、环境上下文、起止玩家状态、地点区间、事件时间线和辅助统计；原始事件、地点区间均有配置上限，低价值溢出只累计类型计数，不无限增长。
- 以 `GameEvent` 保存时间、地点、参与者、详情、重要度和 `Evidence`（`Observed` / `Unknown`）；它是原始事实档案，不在数据层做文学推断，也不再保存重复的 `DebugRawEvents` 或受调试开关影响的真实时间戳。
- 通用剧情事件采集器观察 `Game1.CurrentEvent`，记录稳定事件 ID 和来源资产供原始事实追踪，从已经本地化的事件命令提取有界参与者、关键台词、动作线索与玩家选择，并区分完成和跳过；不硬编码 Shane 或原版事件目录。
- 日结由 `NarrativeProjectionBuilder` 把事件、地点时长、起止背包差分和状态变化聚合为固定条数的 `NarrativeDailyInput`，写入 `data/<SaveUniqueId>/narrative-input/Year<year>-<Season>-<day>.json`。未来 AI 消费该文件，不直接消费 `DailyRecord`。`StoryEvent` 使用最高叙事优先级，AI 只接收有界语义线索，不接收内部事件 ID 或完整事件脚本。
- 维护从当天首次可观察位置开始、由本地玩家 `Warped` 事件切分的 `LocationStay`；地点使用 `NameOrUniqueName` 和可读显示名，不使用原版地点枚举白名单。
- Phase 1 采集 NPC 对话、礼物、商店购买、无法归因的金钱变化、实际睡眠、疲劳昏倒和生命值归零，并通过有界 checkpoint 快照、`Saving` 和 `DayEnding` 写入 `data/<SaveUniqueId>/Year<year>-<Season>-<day>.json`。checkpoint 只用于恢复未完成当日，完成的日记录与叙事输入都写入成功后删除。
- 同一 mod 拥有独立 manifest、配置和构建输出，不与 `Toolbox` 共用运行时身份或配置。

## 入口与代码锚点

- SMAPI 生命周期和事件注册：`packages/StoryDataCollector/ModEntry.cs`。
- 统一事件、地点区间、checkpoint 和 JSON 持久化：`packages/StoryDataCollector/TimelineDataCollector.cs`。
- AI 输入投影、剧情脚本语义提取、事件预算和 checkpoint 校验：`NarrativeProjectionBuilder.cs`、`NarrativeInput.cs`、`StoryEventScriptParser.cs`、`DailyEventBudget.cs`、`CheckpointValidator.cs`。
- 数据模型：`GameEvent.cs`、`DailyRecord.cs`、`LocationStay.cs`、`DailyCheckpoint.cs`。
- 无可靠 SMAPI 事件时的集中 Harmony 入口：`packages/StoryDataCollector/HarmonyPatches.cs`。
- 配置和包身份：`ModConfig.cs`、`config.json`、`manifest.json`。
- 构建与 SMAPI / 游戏程序集引用：`StoryDataCollector.csproj`。

## 数据来源和边界

- 纯 SMAPI：`SaveLoaded`、`DayStarted`、`DayEnding`、`Saving`、`ReturnedToTitle`、`UpdateTicked`、本地玩家 `Player.Warped`。
- Harmony：`NPC.checkAction` 在同步打开对话框后记录 `NpcTalk`；`NPC.receiveGift` 记录实际可接收的礼物及喜好；`ShopMenu.tryToPurchaseItem` 记录成功购买并将已知金钱扣除与 `MoneyChanged` 兜底关联；`GameLocation.doSleep` 只在多人准备完成、实际进入睡眠流程时记录 `Sleep`；`Farmer.passOutFromTired` 记录疲劳或过晚昏倒；`Event.tryEventCommand` 只提取实际执行过的剧情台词和动作，`Event.skipEvent`、`Event.exitEvent` 和 `Event.answerDialogue` 在原调用成功后补充跳过、完成与玩家选择状态。剧情开始及参与者由 `UpdateTicked` 对 `Game1.CurrentEvent` 的引用切换观察，正常每 Tick 只做引用比较。
- 剧情语义提取遵循 [ADR-003](../../requirements/adrs/003-bounded-story-event-semantics.md)：每个事件最多保存 16 名参与者、12 条台词、12 条动作线索和 8 个玩家选择，不保存完整脚本。
- 生命值归零由 `UpdateTicked` 观察 `Farmer.health` 的正数到零变化，避免把可被护符即时恢复的短暂中间状态误记为昏倒；起止背包快照只表达物品持有量变化，不推断其来源。
- checkpoint 以单个当前日快照和待完成日期索引运行；索引保留叙事输入尚未写成的日期，加载时只处理当前日和一个历史待处理日，避免全量扫描。恢复前校验结构和硬上限，损坏文件保留并报警而不覆盖原始日记录。恢复始终重新应用当前事件与地点预算。
- Harmony 目标逐个解析和注册；目标不存在或补丁失败只记录 Warning，对应采集项为 `Unsupported`，不阻止整个 mod 加载；启动日志和 `story_data status` 会列出每个入口的 `Supported` / `Unsupported` 状态。
- 金钱无法与实际行为关联时只写 `MoneyChanged`，详情中的 `reason` 保持 `Unknown`，不猜测购买或消费原因。

## 当前未实现

- Phase 2 的农业、动物、钓鱼、矿洞和战斗会话聚合。
- Phase 3 的任务、成就、电影、制作、烹饪、机器、稀有物品、世界进度和 Nearby NPC 快照；剧情事件现已通用采集，但尚未为每个事件提供人工编写的标题或摘要目录。
- AI 调用、故事生成、日记 UI 和跨 mod 复杂事件总线。
