# Fishing Bar Growth (钓鱼条无限增长)

## 简介 / Description

这是一个《星露谷物语》(Stardew Valley) 模组,实现了基于捕获总量的钓鱼条无限增长机制。

A Stardew Valley mod that implements unlimited fishing bar growth based on total fish caught.

## 功能特性 / Features

- ✅ **可配置增长**: 钓鱼条长度不再受等级限制,可设置最大高度或完全不设上限
- ✅ **产量驱动**: 每钓10条有效鱼(可配置),钓鱼条增加1像素
- ✅ **精准统计**: 按物品数据识别鱼类,可排除藻类、海草和凝胶
- ✅ **追溯既往**: 安装后立即获得之前捕获数量带来的加成
- ✅ **HUD显示**: 手持鱼竿时在屏幕左下角显示钓鱼统计信息
- ✅ **完全可配置**: 通过Generic Mod Config Menu进行配置
- ✅ **多语言支持**: 支持中文和英文

## 安装方法 / Installation

1. 安装 [SMAPI](https://smapi.io/) 4.0.0 或更高版本。
2. (可选但推荐) 安装 [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)。
3. 将构建产物 `packages/FishingBarGrowth/dist/` 下的内容复制到 `Stardew Valley/Mods/FishingBarGrowth/`；更新时保留已有的 `config.json`。
4. 运行游戏并查看 SMAPI 日志。

## 配置选项 / Configuration

如果安装了Generic Mod Config Menu,可以在游戏内配置:

### 主要设置
- **启用模组**: 开启/关闭功能
- **每像素需要的鱼数**: 默认10条鱼增加1像素
- **最大钓鱼条高度**: 默认600像素,设为0表示无限制
- **排除藻类**: 是否将海草和藻类计入鱼类统计

### HUD显示设置
- **显示钓鱼HUD**: 手持鱼竿时显示统计信息
- **HUD水平位置**: 距离屏幕左边缘的距离(默认20像素)
- **HUD垂直位置**: 距离屏幕底部的距离(默认180像素)

### 调试设置
- **显示调试信息**: 在控制台显示详细信息

也可以手动编辑 `config.json` 文件。

## 工作原理 / How It Works

### 钓鱼条高度计算

**游戏原版计算** (基础高度):
- 0级: 96像素
- 每升1级: +8像素
- 10级: 176像素 (96 + 10×8)
- 装备加成: 软木塞浮标、高级鱼饵、附魔等
- 最高可达: 308像素 (沙漠节) 或 284像素 (常规)

**本Mod添加** (奖励高度):
1. 统计玩家 `fishCaught` 中的有效鱼类(可排除藻类、海草和凝胶)
2. 计算奖励像素: `奖励像素 = 总鱼数 ÷ 每像素鱼数`
3. 最终高度 = 基础高度 + 奖励像素
4. 应用最大高度限制(如果设置)

**示例**:
- 10级玩家,无装备: 基础 = 176px
- 钓了500条鱼,配置10条/像素: 奖励 = 50px
- 最终钓鱼条高度: 176 + 50 = **226px**

## 技术细节 / Technical Details

- **目标类**: `StardewValley.Menus.BobberBar`
- **补丁类型**: Harmony Postfix，并对宝藏判定做高度读取转译
- **兼容性**: 客户端模组,不写入自有存档数据,多人游戏中每个客户端独立生效
- **性能**: 钓鱼条奖励在开始钓鱼时计算；HUD 仅在手持鱼竿时绘制统计信息

在本工作区中，项目不再依赖 `Pathoschild.Stardew.ModBuildConfig`，而是使用仓库统一的游戏程序集引用和 `dist/` 打包目标。

## 兼容性 / Compatibility

- ✅ Stardew Valley 1.6+
- ✅ SMAPI 4.0.0+
- ✅ 单人和多人游戏
- ✅ Windows / Linux / macOS
- ⚠️ 可能与大幅修改 `BobberBar` 或钓鱼机制的模组冲突

## 工作区构建 / Workspace build

在仓库根目录执行：

```powershell
dotnet build packages/FishingBarGrowth/FishingBarGrowth.csproj -c Release -p:GamePath="D:\Games\Stardew Valley"
```

完整包输出到 `packages/FishingBarGrowth/dist/`，包含 `FishingBarGrowth.dll`、`manifest.json`、`config.json` 和 `i18n/`。

## 数据示例 / Examples

- 钓了 **50条鱼**: 钓鱼条增加 **5像素** (~0.6个等级)
- 钓了 **500条鱼**: 钓鱼条增加 **50像素** (~6个等级)
- 钓了 **5000条鱼**: 钓鱼条增加 **500像素** (超强!)

## 开源协议 / License

MIT License

## 作者 / Author

xixifu

## 致谢 / Credits

- 基于技术分析文档实现
- 使用 [SMAPI](https://smapi.io/) 和 [Harmony](https://github.com/pardeike/Harmony)
- 集成 [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)

