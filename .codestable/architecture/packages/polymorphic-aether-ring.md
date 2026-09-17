---
scope: package:polymorphic-aether-ring
---

# PolymorphicAetherRing 模组包

`packages/PolymorphicAetherRing` 是独立的 `net6.0` Stardew Valley / SMAPI 模组包，运行时身份为 `xixifu.PolymorphicAetherTrinket`，入口程序集为 `PolymorphicAetherRing.dll`。

## 职责

- 注册以太多态戒指物品，并在首次读档时检查玩家已有物品后补发戒指。
- 将玩家选择的近战武器及其战斗属性、附魔类型和等级保存到戒指中。
- 提供桌面和紧凑/移动熔铸菜单，并在已装备戒指时执行 360 度范围光环攻击。
- 为戒指和熔铸武器提供悬浮说明、多语言资源和可选的 GMCM 配置入口。

## 外部边界与锚点

- SMAPI 入口、资产注册、读档赠送、输入路由和配置：`packages/PolymorphicAetherRing/ModEntry.cs`、`ModConfig.cs`。
- 熔铸数据模型、版本兼容、校验和原子写入：`Framework/FusedWeaponData.cs`、`FusedEnchantmentData.cs`、`FusedWeaponModDataUpdate.cs`。
- 武器恢复、附魔实例化和临时战斗武器：`Framework/FusedWeaponRestorer.cs`。
- 戒指悬浮说明：`Framework/FusedWeaponTooltip.cs`。
- 桌面熔铸菜单：`Framework/FusionMenu.cs`。
- 紧凑菜单状态、交互和绘制：`Framework/MobileFusionMenu.cs`、`MobileFusionMenu.Interaction.cs`、`MobileFusionMenu.Rendering.cs`。
- 装备戒指后的光环战斗、冷却和临时物品：`Framework/RingCombatManager.cs`。
- 包身份、构建和发布资源：`manifest.json`、`PolymorphicAetherRing.csproj`、`config.json`、`assets/trinket.png`、`i18n/*.json`。

## 运行流程

1. `OnSaveLoaded` 创建 `RingCombatManager`，递归检查背包、装备槽和组合戒指；已有目标戒指只补记领取标记，否则赠送一个戒指。
2. `OnButtonPressed` 处理鼠标左键和手柄确认输入；Android 由左键长按达到配置阈值后打开菜单。视口任一边小于 `1064×768` 或运行于 Android 时使用紧凑菜单，否则使用桌面菜单。
3. 熔铸菜单只允许近战武器，把武器 ID、战斗数值、武器类型和每个附魔的完整类型身份与等级写入戒指 `modData`。
4. `RingCombatManager.Update` 根据已装备戒指及其 `modData` 签名刷新缓存；冷却结束且范围内有存活怪物时，临时恢复熔铸武器和附魔并对所有目标执行一次光环攻击。

## 状态与不变量

- 熔铸状态只存放在戒指自身、前缀为 `xixifu.AetherTrinket/` 的 `modData` 中；缓存按签名失效，不能依赖物品对象引用。
- 每个戒指最多记录一把近战武器。已装备戒指位于组合戒指内部时仍参与光环攻击。
- 武器返还按原版复制语义逐项恢复附魔类型及等级，不合并重复锻造或替换多个主附魔；背包已满时掉落到玩家位置。
- 旧 `EnchantmentIds` 数据没有等级，只按一级返还并明确警告；新数据在返还旧武器前完成校验和序列化，写入失败回滚，不覆盖旧状态或消耗新武器。
- 缺字段、孤立前缀字段、空附魔项、坏 JSON、无效数值、无法实例化的附魔或附魔类型歧义均视为损坏数据。损坏数据保留在戒指中，清空战斗缓存，悬浮说明显示损坏状态，菜单可关闭但禁止覆盖。
- 光环攻击只临时使用熔铸武器，不写入玩家 `CurrentTool` 或覆盖快捷栏物品；攻击或清理失败后恢复玩家已有的临时物品和附魔效果。
- 基础攻击半径为 `80 + AreaOfEffect × 16` 像素，再乘范围倍率；匕首、锤子和其他武器的基础间隔分别为 250、500、400 毫秒，再乘冷却倍率，最终不少于 100 毫秒。没有命中目标时不消耗已积累冷却，卡顿造成的过量积累不会同帧连发。
- GMCM 重置会原地恢复同一个配置对象，已创建的战斗管理器继续读取更新后的配置。

## 界面与资源

- `FusionMenu` 负责桌面布局；当前无标题和装饰标题纹理，空武器槽显示“+”。
- `MobileFusionMenu` 负责窄屏和触控布局；其绘制矩形与点击/触控命中区域共用同一组边界，武器名称超出空间时截断。
- `assets/trinket.png` 是戒指物品图标；`i18n/default.json`、`zh.json`、`zh-CN.json` 和 `ja.json` 提供界面、物品和悬浮说明翻译。

## 依赖

- 编译时直接引用目标游戏目录中的 SMAPI、Stardew Valley、Stardew Valley GameData、MonoGame、xTile 和 Harmony 程序集；这些运行时文件不复制到 `dist`。
- Generic Mod Config Menu 是可选运行集成；未安装时主体功能仍加载，但游戏内配置入口不可用。
- `packages/PolymorphicAetherRing.Tests` 保存熔铸数据、附魔恢复、配置校验、原子写入和临时物品恢复的回归测试，按工作区惯例不加入主解决方案。

## 决定

- 附魔等级格式、旧数据兼容和武器返还事务顺序见[保留附魔等级并按复制语义返还武器](../../requirements/adrs/005-preserve-fused-weapon-enchantment-levels.md)。
