---
scope: package:fishing-bar-growth
---

# 钓鱼条成长包

`packages/FishingBarGrowth` 是独立的 Stardew Valley / SMAPI mod 包，运行时身份为 `xixifu.FishingBarGrowth`，入口程序集为 `FishingBarGrowth.dll`。

## 职责

- 根据玩家已有的有效鱼类捕获总数，为原版钓鱼条增加可配置的像素奖励。
- 通过 Harmony 调整 `BobberBar` 的高度，并在宝藏判定中排除本包实际增加的高度，保留游戏本体和其他 mod 的高度影响。
- 在玩家手持鱼竿时显示捕获数量、基础高度、奖励高度和最终高度，并通过 GMCM 提供可选的游戏内配置入口。
- 提供中英文配置和 HUD 翻译；不拥有独立存档数据，不改变多人游戏协议。

## 边界与锚点

- SMAPI 入口、事件编排和 GMCM 注册：`packages/FishingBarGrowth/ModEntry.cs`。
- 捕获统计与鱼类识别：`packages/FishingBarGrowth/FishCounter.cs`。
- 钓鱼条构造函数 Postfix、宝藏高度兼容转译和当前统计快照：`packages/FishingBarGrowth/BobberBarPatch.cs`。
- HUD 绘制：`packages/FishingBarGrowth/FishingHUD.cs`。
- 配置契约：`packages/FishingBarGrowth/ModConfig.cs`、`packages/FishingBarGrowth/config.json`。
- GMCM 接口边界和翻译：`packages/FishingBarGrowth/IGenericModConfigMenuApi.cs`、`packages/FishingBarGrowth/i18n`。
- 包身份与构建引用：`packages/FishingBarGrowth/manifest.json`、`packages/FishingBarGrowth/FishingBarGrowth.csproj`。

## 运行约束

- 包只读取 `Game1.player.fishCaught`；每个条目的 `stats[0]` 作为捕获总数，并通过物品数据的 `ObjectType` 或 `Category == -4` 判断是否为鱼类。
- 开启排除选项时，物品 ID `152`、`153`、`157` 以及凝胶 ID `812`、`851`、`852` 不计入有效鱼类总数；ID 会先标准化为 `(O)` qualified ID。
- 奖励像素使用整数除法 `totalFish / FishPerPixel`；`MaxBarHeight` 大于 0 时限制最终高度，设置为 0 表示不限制。配置异常导致 `FishPerPixel` 不大于 0 时不增加奖励。
- Harmony 动态选择 `BobberBar` 的公共构造函数。若游戏版本的 `BobberBar.update` 宝藏逻辑不是预期的两处高度读取，兼容转译会失败并记录错误，不伪造钓鱼条高度。
- 宝藏判定只减去本包实际加入的像素；达到最大高度上限时，未实际加入的奖励不会被宝藏判定排除，其他来源的高度调整也不会被移除。
- HUD 只在启用功能且玩家当前手持鱼竿时绘制；首次获得成长快照前显示当前鱼类数量和提示。HUD 位置、显示开关和调试日志均由本包配置控制。
- Generic Mod Config Menu 是可选外部集成；未安装时包主体仍加载，用户可直接编辑 `config.json`。
- 包以 `net6.0` 托管 DLL 作为运行时边界，游戏、SMAPI、MonoGame、xTile 和 Harmony 程序集只用于编译，不复制到 `dist`。构建输出必须包含 DLL、manifest、config 和 `i18n` 翻译目录。
- 这是客户端 mod；它只使用本地玩家的捕获统计和本地钓鱼条显示，不写入自有存档字段或跨包消息。
