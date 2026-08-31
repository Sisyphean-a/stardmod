# ADR-003：剧情事件以有界语义线索进入叙事输入

- 状态：accepted
- 范围：package:story-data-collector、context:story-data-collector
- 日期：2026-08-29

## 背景

玩家参与的过场、红心和模组剧情通常是一天中最有故事价值的内容。只记录 NPC 相遇和地点访问无法表达剧情含义；只记录事件 ID 对 AI 没有语义，而把完整事件脚本送入 AI 会泄漏内部命令、包含未执行分支，并让输入随事件复杂度增长。

## 决定

运行时通用观察 `Game1.CurrentEvent`，从游戏已经解析、本地化且实际执行过的事件命令提取参与者、关键台词和动作线索，在选择生效后保存玩家实际选择，并记录完成或跳过状态。原始 `GameEvent` 保留事件 ID 与来源资产用于追踪；schema 2 的 `NarrativeDailyInput` 不暴露内部事件 ID或完整脚本，只暴露最多 16 名参与者、12 条台词、12 条动作线索和 8 个玩家选择。`StoryEvent` 使用最高叙事优先级，不会被普通地点、库存差分或重复行为挤出事实预算。

## 备选方案

- 硬编码原版事件标题与摘要：可读性高，但无法覆盖模组事件，且随游戏版本和本地化维护成本增长。
- 保存完整事件脚本供 AI 解释：覆盖通用，但包含技术命令、未执行分支和无界文本，违背有界输入规则。
- 只保存事件 ID：体积最小，但 AI 无法知道剧情发生了什么。
- 提取有界语义线索：兼容原版和模组剧情，保留 AI 可理解证据，同时维持固定输入上限。

## 后果

- 新生成的叙事输入使用 schema 2；既有 schema 1 文件保持可读且不迁移。
- 无台词或使用自定义运行时代码的剧情可能只有参与者和动作线索，不能保证自动得到完整自然语言摘要。
- 玩家选择保存实际显示的选项文本；静态脚本中的其他分支不会被当作已发生结果。
- 每 Tick 正常路径只比较当前事件引用；语义解析仅在剧情命令实际执行时发生。

## 代码锚点

- `packages/StoryDataCollector/TimelineDataCollector.cs`
- `packages/StoryDataCollector/StoryEventScriptParser.cs`
- `packages/StoryDataCollector/HarmonyPatches.cs`
- `packages/StoryDataCollector/NarrativeProjectionBuilder.cs`
- `packages/StoryDataCollector/NarrativeInput.cs`
- `packages/StoryDataCollector.Tests/Program.cs`
