# MineBustle：由巴的祭坛

`MineBustle` 是一个独立的 Stardew Valley / SMAPI 模组包。玩家可以在山区矿井入口附近的由巴祭坛献祭金币，将当天矿井的怪物生成倍率调整为 `1.0x`–`10.0x`。

- UniqueID：`xixifu.MineBustle`
- 版本：`1.0.0`
- 入口 DLL：`MineBustle.dll`
- 要求：SMAPI `4.0.0+`、Stardew Valley `1.6.0+`

## 功能

- 通过祭坛滑块选择当天的怪物生成倍率。
- 献祭费用按照以下公式计算：

  ```text
  Cost = (BaseFee + InflationCoefficient × TotalEarnings)
         × (Multiplier - 1) ^ PenaltyExponent
  ```

- 每天开始时倍率重置为 `1.0x`；倍率保存在配置中，祭坛确认献祭后立即生效。
- 可选地按倍率降低石头生成概率，为矿井怪物腾出生成空间。
- 通过 Harmony Transpiler 在 `MineShaft.populateLevel` 的原版概率调整后修改怪物和石头概率；如果目标版本的 IL 结构不匹配，会记录错误并保留原版指令。
- 祭坛地图和贴图由 SMAPI 的资源请求 API 原生加载，不依赖 Content Patcher。

## 使用方法

1. 前往山区的矿井入口地图 `Mine`。
2. 点击祭坛交互区域（当前代码区域约为 `x=19–21`、`y=2–4`）。
3. 使用游戏操作键（通常为右键或手柄确认键）打开菜单。
4. 拖动滑块选择 `1.0x`–`10.0x`，确认费用后点击献祭按钮。
5. 当天进入矿井时，新的怪物生成倍率会参与矿层生成。

倍率为 `1.0x` 时，按照当前公式费用为 `0`；倍率越高，费用随玩家总收入和惩罚指数增长。

## 配置

安装 [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) 后可在游戏内配置；也可以直接编辑 `config.json`。GMCM 不存在时主体功能仍可运行。

| 配置项 | 默认值 | 作用 |
| --- | ---: | --- |
| `CurrentMultiplier` | `1.0` | 当前当天倍率，由祭坛和每日重置逻辑管理。 |
| `EnableAltar` | `true` | 是否显示祭坛地图并启用祭坛交互。 |
| `ReduceStones` | `true` | 是否随怪物倍率增加而降低石头生成概率。 |
| `BaseFee` | `500` | 献祭费用基础值。 |
| `InflationCoefficient` | `0.001` | 玩家总收入对费用的影响系数。 |
| `PenaltyExponent` | `1.0` | 倍率增量的惩罚指数，GMCM 范围为 `1.0`–`3.0`。 |

## 资源与兼容性

- `assets/altar2.tmx`：覆盖 `Maps/Mine` 的祭坛地图补丁。
- `assets/altar4.png`：祭坛图块集，由虚拟资源路径 `Mods/MineBustle/AltarTilesheet` 提供给地图使用。
- `i18n/default.json`、`zh.json`、`ja.json`：英文、中文和日文翻译。
- 不需要 Content Patcher；GMCM 是可选依赖。
- 祭坛位置目前硬编码在 `Mine` 地图，且没有单独的联机网络同步逻辑；多人游戏中的矿层生成效果应以实际主机/客户端环境验证。

## 工作区构建

在仓库根目录执行：

```powershell
dotnet build packages/MineBustle/MineBustle.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
```

完整可部署包输出到 `packages/MineBustle/dist/`，包括 DLL、`manifest.json`、`config.json`、`assets/` 和 `i18n/`。更新游戏中的 `Mods/MineBustle` 时请保留已有的 `config.json`。
