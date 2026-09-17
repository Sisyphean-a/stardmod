---
scope: package:mine-bustle
---

# MineBustle 模组包

`packages/MineBustle` 是独立的 `net6.0` Stardew Valley / SMAPI 模组包，运行时身份为 `xixifu.MineBustle`，入口程序集为 `MineBustle.dll`。

## 职责

- 在 `Mine` 地图的矿井入口附近提供由巴祭坛。
- 通过献祭费用设置当天的矿井怪物生成倍率，并在每天开始时重置。
- 使用 Harmony 修改 `MineShaft.populateLevel` 的怪物概率，按配置决定是否同步降低石头概率。
- 原生加载祭坛 TMX 地图和 PNG 图块集，提供祭坛菜单、HUD 消息、三套翻译和可选 GMCM 配置。

## 外部边界与锚点

- SMAPI 入口、配置、GMCM、读档生命周期和资源请求：`packages/MineBustle/ModEntry.cs`、`ModConfig.cs`、`IGenericModConfigMenuApi.cs`。
- 祭坛点击区域和操作键路由：`AltarInteractionHandler.cs`。
- 献祭滑块、费用计算、扣款、配置保存和 HUD 消息：`AltarMenu.cs`。
- 矿层生成概率补丁：`MineShaftPatches.cs`。
- 地图和图块资源：`assets/altar2.tmx`、`assets/altar4.png`；虚拟图块路径为 `Mods/MineBustle/AltarTilesheet`。
- 包身份、默认配置、翻译和构建发布：`manifest.json`、`config.json`、`i18n/*.json`、`MineBustle.csproj`。

## 运行流程

1. `ModEntry.Entry` 读取 `ModConfig`，注册祭坛交互、读档生命周期和资源请求事件，注册可选 GMCM，并按 manifest 身份应用 Harmony 补丁。
2. 请求 `Mods/MineBustle/AltarTilesheet` 时从包内加载 `assets/altar4.png`；请求 `Maps/Mine` 且 `EnableAltar` 开启时加载 `assets/altar2.tmx`，把其图块集重定向到虚拟路径后覆盖到目标地图的 `(19, 2)`、`2×3` 区域。
3. `AltarInteractionHandler` 在 `Mine` 地图的交互区域（当前为 `x=19–21`、`y=2–4`）拦截操作键并打开 `AltarMenu`；关闭祭坛配置时地图和交互入口都停用。
4. `AltarMenu` 将滑块值映射到 `1.0x`–`10.0x`，按玩家总收入计算费用。确认且金币足够时扣款、保存 `CurrentMultiplier`、显示 HUD 消息并关闭菜单。
5. `MineShaftPatches` 尝试在 `adjustLevelChances` 调用之后读取并修改怪物和石头概率；每日开始时 `CurrentMultiplier` 在内存中重置为 `1.0`。

## 规则与兼容边界

- 献祭费用公式为 `(BaseFee + InflationCoefficient × TotalEarnings) × (Multiplier - 1)^PenaltyExponent`；`1.0x` 对应的费用为 `0`。余额不足时不扣款、不修改倍率。
- `ReduceStones` 开启时用当前正倍率除以石头生成概率；关闭时石头概率保持原值。非正倍率在概率辅助方法中回退为 `1.0`。
- Transpiler 依赖当前 `MineShaft.populateLevel` 的 IL 形状；找不到目标调用或局部变量加载形式不匹配时记录错误并返回原始指令，不伪造注入成功。
- `EnableAltar` 关闭时不编辑 `Maps/Mine`，且不接受祭坛交互。祭坛位置当前硬编码，改变地图布局时必须同步更新地图覆盖区域和交互区域。
- GMCM 是可选运行时集成，不是 manifest 必需依赖；没有 GMCM 时仍可通过 `config.json` 使用主体功能。
- 包没有自定义多人网络同步逻辑；多人场景下的矿层生成端行为需要在目标主机/客户端组合中进行游戏内验证。

## 资源与构建

- `dist/` 必须包含 `MineBustle.dll`、`manifest.json`、`config.json`、`assets/altar2.tmx`、`assets/altar4.png` 以及 `i18n/default.json`、`zh.json`、`ja.json`。
- 构建直接引用目标游戏目录中的 SMAPI、Stardew Valley、GameData、MonoGame、xTile 和 Harmony 程序集，不使用 `Pathoschild.Stardew.ModBuildConfig`，也不把这些运行时程序集复制到发布包。
