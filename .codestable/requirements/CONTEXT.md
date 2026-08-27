---
scope: workspace
---

# 领域上下文

本工作区将多个 Stardew Valley SMAPI mod 源码组织为可独立构建的包，并把简单便利功能合并到工具箱中。

## 作用域

- [context:toolbox](contexts/toolbox.md)：工具箱内轻量游戏便利功能。代码位置：`packages/Toolbox`。
- [context:horse-follower](contexts/horse-follower.md)：玩家下马后的马匹跟随生命周期。代码位置：`packages/HorseFollower`。
- [context:hotkey-viewer](contexts/hotkey-viewer.md)：游戏本体与已加载模组快捷键查看和潜在冲突排查。代码位置：`packages/HotkeyViewer`。
- [context:portable-loading-optimizer](contexts/portable-loading-optimizer.md)：不接管存档所有权的跨平台加载优化与平台分流。代码位置：`packages/PortableLoadingOptimizer`。
- [context:story-data-collector](contexts/story-data-collector.md)：按时间线保存可观察游戏事实的数据采集。代码位置：`packages/StoryDataCollector`。

## 通用语言

**包**：拥有独立 SMAPI manifest、UniqueID、入口程序集和构建输出的可部署单元。

**工具箱**：承载简单、低耦合、无需独立生命周期的多个便利功能的包。

**独立包**：不适合工具箱边界、需要独立生命周期、依赖、配置或发布身份的较大型 mod。

## 稳定规则

- 一个可部署的 SMAPI mod 包拥有唯一的运行时身份，不与其他包共用 manifest 或 UniqueID。
- 工具箱只吸收简单功能；较大型功能保持独立包边界。
