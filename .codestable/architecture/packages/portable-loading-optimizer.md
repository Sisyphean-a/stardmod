---
scope: package:portable-loading-optimizer
---

# 跨平台加载优化器包

`packages/PortableLoadingOptimizer` 是独立的 SMAPI mod 包，运行时身份为 `xixifu.PortableLoadingOptimizer`，入口程序集为 `PortableLoadingOptimizer.dll`。

## 职责

- 在不替换 `SaveGame.Load` 或存档对象图的前提下，尝试移除 `SaveFileSlot` 的固定激活等待。
- 以平台相关的速率和总量上限，在后台预读近期存档文件和 Mod 资源文件，帮助操作系统文件缓存升温。
- 仅在 Windows 上启用经过桌面游戏版本验证的快速普通传送淡入淡出；Android 和其他非 Windows 平台使用原生淡入淡出。
- 发现原版 `neoiw.StardewLoadingOptimizer` 已加载时停用自身，避免重复 Harmony 补丁与双重预读。

## 边界与锚点

- SMAPI 入口、事件编排和控制台命令：`packages/PortableLoadingOptimizer/ModEntry.cs`。
- 平台分流与 Android 资源预算：`packages/PortableLoadingOptimizer/PlatformPolicy.cs`。
- 配置和范围校正：`packages/PortableLoadingOptimizer/ModConfig.cs`、`packages/PortableLoadingOptimizer/config.json`。
- 选档等待补丁：`packages/PortableLoadingOptimizer/Services/SaveMenuDelayOptimizer.cs`。
- 有界预读、暂停/恢复和文件访问回退：`packages/PortableLoadingOptimizer/Services/BackgroundFilePrefetcher.cs`。
- Windows 快速传送补丁：`packages/PortableLoadingOptimizer/Services/FastWarpTransition.cs`。
- 包身份与构建引用：`packages/PortableLoadingOptimizer/manifest.json`、`packages/PortableLoadingOptimizer/PortableLoadingOptimizer.csproj`。

## 运行约束

- 目标为同一份 `net6.0` 托管 Mod DLL；不生成 Android 应用，也不携带游戏、SMAPI、MonoGame 或 Harmony 运行时 DLL。
- `SaveFileSlot` 类型、`ActivateDelay` 成员或构造函数找不到时，选档等待保持原生，不阻止 Mod 启动。
- 预读只读文件；目录枚举、文件打开和读取失败都视为可选路径失败，原生加载不受影响；存档加载阶段、保存阶段和回到标题时暂停预读。
- `FastWarpTransition` 只在 `PlatformPolicy.SupportsFastWarp` 为真时实例化。当前只有 Windows 为真；Android 明确跳过 `Game1` 私有淡入淡出补丁。
- 快速传送默认排除多人会话；只有显式开启 `EnableFastWarpTransitionsInMultiplayer` 才允许多人会话尝试。
- 原插件的同步编译地图缓存、SMAPI 私有已解码图片缓存、SpaceCore 序列化器改造和性能诊断未纳入本包：它们分别依赖版本耦合的 `TMXFormat`/xTile 对象、SMAPI 私有 `ModContentManager`/`RawTextureData`、可选 Mod 私有实现或桌面验证路径，当前没有足够的 Android 契约证据。
- 本包不改写存档 XML、不发布自定义多人协议字段，也不拥有游戏状态对象图。

## 许可

实现采用 GPL-3.0-only；`NOTICE` 记录其参考并独立重实现 Stardew Loading Optimizer 的可移植思路，运行时依赖仍遵循各自许可。
