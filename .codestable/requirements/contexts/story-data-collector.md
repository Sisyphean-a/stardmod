---
scope: context:story-data-collector
---

# 故事数据采集上下文

## 语义边界

故事数据采集器只保存游戏中可观察到的事实及其可验证的简单推导；文学解释、因果猜测和故事生成不属于该上下文。该上下文映射到 `packages/StoryDataCollector` 独立包。

## 稳定规则

- 当天核心数据是按时间排序的 `events` 和按进入/离开时间切分的 `locationStays`，不是地点集合或统计报表。
- 地点同时保存稳定内部 ID（`GameLocation.NameOrUniqueName`）和可读名称，并保留室内、室外、临时地图等运行时属性。
- 事件必须包含游戏内时间；重要度范围为 0 到 5；事实来源使用 `Observed`、`Derived` 或 `Unknown` 的约定。本阶段实际写入的事件主要是 `Observed`，无法归因的金钱变化写 `Unknown`。
- 连续低信息行为不在 Phase 1 中逐 Tick 记录；商店购买以一次实际购买调用为单位，金钱轮询只在发现无法关联的变化时补写 `MoneyChanged`。`SaveRawEvents` 仅用于额外保存 `DebugRawEvents` 诊断副本，默认核心消费面仍是 `Events`。
- 生活周期必须清理和保存内存状态：加载/新日建立记录，换图切分地点，保存时 checkpoint，日结时关闭地点区间并写出完整记录，返回标题时清理状态。
- 该上下文的包身份是 `xixifu.StoryDataCollector`，不并入 `xixifu.Toolbox`。

## 代码依据

- `packages/StoryDataCollector/TimelineDataCollector.cs`
- `packages/StoryDataCollector/HarmonyPatches.cs`
- `packages/StoryDataCollector/DailyRecord.cs`
- `packages/StoryDataCollector/GameEvent.cs`
- `packages/StoryDataCollector/LocationStay.cs`
