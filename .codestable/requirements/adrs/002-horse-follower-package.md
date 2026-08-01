# ADR-002：马匹跟随保持独立包

- 状态：accepted
- 范围：workspace、package:horse-follower、context:horse-follower
- 日期：2026-08-01

## 背景

马匹跟随虽然功能规模不大，但用户明确要求它作为独立 mod，不并入工具箱。它拥有独立的骑乘状态、场景暂停和新日清理生命周期。

## 决定

将马匹跟随实现为 `packages/HorseFollower`，使用 `xixifu.HorseFollower` 和 `HorseFollower.dll`，不依赖工具箱运行时身份或配置。

## 备选方案

- 并入 `packages/Toolbox`：安装数量更少，但会把马匹生命周期与工具箱功能配置、发布身份耦合。
- 保持独立包：多一个 mod，但生命周期、配置和故障范围清晰，符合用户指定的边界。

## 后果

- 马匹跟随有自己的 manifest、config 和输出目录。
- 工具箱不会注册马匹事件，也不会持有马匹状态。
- 未来修改马匹跟随不会改变工具箱的运行时身份。

## 代码锚点

- `packages/HorseFollower/manifest.json`
- `packages/HorseFollower/ModEntry.cs`
- `packages/HorseFollower/HorseFollowerService.cs`
- `packages/HorseFollower/ModConfig.cs`
- `StardewMods.sln`
