# PolymorphicAetherRing（以太多态戒指）

这是一个独立的 Stardew Valley / SMAPI 模组包。它注册一个可吸收近战武器的戒指；装备已熔铸武器的戒指后，会按该武器的战斗属性和附魔对范围内的存活怪物发动 360 度光环攻击。

- 显示名：`Polymorphic Aether Trinket`
- UniqueID：`xixifu.PolymorphicAetherTrinket`
- 版本：`1.8.0`
- 入口 DLL：`PolymorphicAetherRing.dll`
- 要求：SMAPI `4.0.0+`、Stardew Valley `1.6.0+`

## 功能

- 首次读档时检查背包、装备槽和组合戒指；没有目标戒指时自动赠送。
- 只允许熔铸近战武器，并保存武器 ID、伤害、速度、暴击、击退、范围、攻击类型以及附魔等级。
- 装备戒指后，对有效半径内的所有存活怪物执行光环攻击；没有目标时不消耗冷却。
- 可选择在熔铸新武器时返还旧武器；背包已满时掉落到玩家位置。
- 戒指悬浮说明会显示熔铸武器属性和附魔。损坏的数据会保留并禁止覆盖，避免静默丢失武器状态。
- Android 和窄视口使用支持长按、滚动和触控的紧凑熔铸面板；桌面使用完整面板。
- 提供英文、简体中文、繁体中文和日文翻译。

## 使用方法

1. 进入存档后取得自动赠送的戒指，或使用已经拥有的目标戒指。
2. 手持戒指，在桌面按鼠标左键或手柄确认键；Android 手持戒指长按左键打开熔铸菜单。
3. 选择一把近战武器并点击“熔铸”。熔铸会消耗选中的新武器。
4. 将戒指装备到任一戒指槽，即可在附近有怪物时触发光环攻击。

## 配置

安装 [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) 后可在游戏内配置；也可以直接编辑 `config.json`。

| 配置项 | 默认值 | 范围 / 说明 |
| --- | ---: | --- |
| `DamageMultiplier` | `1.0` | 伤害倍率，`0.1`–`5.0` |
| `RangeMultiplier` | `1.0` | 攻击范围倍率，`0.5`–`3.0` |
| `CooldownMultiplier` | `1.0` | 冷却倍率，`0.1`–`2.0`，越低越快 |
| `ReturnFusedWeapon` | `false` | 熔铸新武器时是否返还旧武器 |
| `AndroidLongPressMs` | `500` | Android 长按阈值，`200`–`1500` 毫秒 |

熔铸数据写在戒指自身的 `modData` 中，不创建独立存档文件。旧版本只保存附魔类型名时，返还会按 1 级恢复并显示警告；新格式会保存附魔的完整类型身份和等级。

## 工作区构建与测试

在仓库根目录执行：

```powershell
dotnet build packages/PolymorphicAetherRing/PolymorphicAetherRing.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
dotnet test packages/PolymorphicAetherRing.Tests/PolymorphicAetherRing.Tests.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
```

可部署包输出到 `packages/PolymorphicAetherRing/dist/`，包括 DLL、manifest、config、`assets/trinket.png` 和 `i18n/`。更新游戏安装时请保留已有的 `Mods/PolymorphicAetherRing/config.json`。

变更记录：[`CHANGELOG.md`](CHANGELOG.md)。
