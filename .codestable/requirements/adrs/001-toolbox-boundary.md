# ADR-001：工具箱与独立包边界

- 状态：accepted
- 范围：workspace、package:toolbox
- 日期：2026-08-01

## 背景

工作区需要逐步恢复多个 mod。把每个简单功能都发布为独立 mod 会增加安装和配置负担，但较大型功能又需要自己的生命周期、依赖或发布身份。

## 决定

在单仓多包结构中，简单且低耦合的功能合并到 `packages/Toolbox`，使用 `xixifu.Toolbox` 作为唯一运行时身份；较大型功能在 `packages/<name>` 中作为独立包维护。

## 备选方案

- 所有功能都保持独立 mod：边界清晰，但小功能的安装和配置成本更高。
- 所有未来功能都并入工具箱：安装简单，但会让配置、依赖和生命周期互相耦合。

## 后果

- 工具箱拥有一个合并配置和一个 GMCM 入口；新增小功能必须避免与现有配置和补丁冲突。
- 独立包可以拥有自己的 manifest、UniqueID、依赖和发布节奏，不受工具箱运行时身份影响。

## 代码锚点

- `packages/Toolbox/manifest.json`
- `packages/Toolbox/ModConfig.cs`
- `packages/Toolbox/ModEntry.cs`
- `packages/Toolbox/LightRadiusFeature.cs`
- `StardewMods.sln`
