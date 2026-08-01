---
scope: workspace
---

# 架构索引

这是一个单仓多包的 Stardew Valley SMAPI mod 工作区。

## 范围

- [package:toolbox](packages/toolbox.md)：合并简单实用功能的“工具箱”包，代码位置为 `packages/Toolbox`。
- 未来较大型功能：在 `packages/<name>` 建立独立包，并拥有独立的 SMAPI manifest、UniqueID 和输出目录。

## 共享机制

- `Directory.Build.props`：所有包共享的 C#、Nullable 和确定性构建设置。
- 各包的 `.csproj`：声明本包需要的游戏、SMAPI 和 mod API 引用，并将 DLL、manifest、config 输出到本包的 `dist` 目录。
- `StardewMods.sln`：工作区包入口。
