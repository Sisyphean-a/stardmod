---
scope: context:toolbox
code-paths:
  - packages/Toolbox
---

# 工具箱领域上下文

工具箱为农场管理和环境表现提供若干小型、可组合的游戏便利功能。

## 通用语言

**动物自动抚摸**：玩家进入农场或畜棚、并在其中移动时，自动抚摸扫描范围内尚未抚摸且友好度未满的动物。

**光源半径倍率**：家具光源或普通物体光源相对其基础半径的放大倍数。

**栅栏防腐朽**：主机阻止栅栏和大门在时间流逝时损失耐久。

**自动开关门**：玩家面对关闭大门时自动打开；离开其相邻格后按配置延迟关闭。

**镰刀收割**：开启后，镰刀可收割普通作物、花朵和地面觅食物；不允许用剑替代镰刀。

**快速堆叠到附近箱子**：从背包按钮触发，在配置距离内按距离顺序把背包物品合并到当前地点普通箱子或大箱子的相同物品堆叠中。

**穿过作物**：开启后，农民可以穿过配置允许的作物、茶树、树木生长阶段、果树、杂草、洒水器、稻草人、觅食物和自定义物体。

**NPC 地图位置**：在原版世界地图和可拖动 HUD 小地图上显示 NPC、特殊商人、多人农民和农场建筑的位置。

**自动输入法控制**：仅在 Windows 上，游戏没有活动文本输入框时解除游戏实际 Windows 窗口的系统输入法上下文；文本框获得键盘焦点时恢复该窗口原先的输入法上下文。Android 和其他非 Windows 平台不执行该桌面原生行为。

**Android 兼容运行时**：工具箱作为跨平台 `net6.0` 托管 Mod DLL 运行；平台专属能力必须在运行时隔离，不能把 Mod 改成 `net6.0-android` 应用。

**工具箱设置页**：游戏菜单中的独立工具箱页签，分功能开关和参数两页，直接编辑工具箱配置。

## 稳定规则

- 动物自动抚摸只作用于农场和畜棚，并受 `EnableAutoPet`、`CheckInterval`、`ScanRange`、`wasPet` 和友好度限制。
- 家具光源使用 `EnableFurnitureLightRadius` 和 `FurnitureLightRadius`；非家具光源使用 `EnableObjectLightRadius` 和 `ObjectLightRadius`。关闭或调整光源配置后，当前场景立即重算半径，且只有主机更新同步光源。
- 栅栏防腐朽受 `EnableFenceDecay` 控制，只由主机写入同步的栅栏生命值；大门保持原版双倍耐久。
- 自动开关门不接管手动打开的大门，只关闭自身已经打开、且玩家已离开相邻格的大门。
- 镰刀收割只受 `EnableHarvestWithScythe` 控制；开启时沿用默认配置：地面觅食物不要求位于耕地上，普通作物和花朵可徒手或用镰刀收割，原本仅镰刀作物仍不能徒手收割；剑永远不作为镰刀使用。检测到旧的 `bcmpinc.HarvestWithScythe` 时跳过工具箱补丁并记录警告，避免重复 Harmony 转译。
- 快速堆叠只受 `EnableQuickStack` 和 `QuickStackRange` 控制；只扫描当前地点距离内的普通箱子和大箱子，先合并相同物品已有堆叠，若该箱子已有同类物品但堆叠已满，再使用空位新增堆叠；不处理没有同类物品的空箱，也不处理特殊库存。检测到 `gaussfire.ConvenientInventory` 时跳过工具箱补丁并记录警告。
- 穿过作物只对农民生效，除非开启 `PassableByAll`；`PassableTreeGrowth` 和 `PassableFruitTreeGrowth` 分别限制可穿过的树木阶段。`SlowDownWhenPassing`、`ShakeWhenPassing`、`PlaySoundWhenPassing` 和 `UseCustomDrawing` 控制经过时的附加效果。检测到 `NCarigon.PassableCrops` 时跳过工具箱补丁并记录警告，避免重复修改碰撞和绘制方法。
- NPC 地图位置只在 `EnableNpcMapLocations` 开启时运行；地图页通过 `WorldMapManager` 计算室内建筑回退位置，小地图通过 `ShowMinimap`、排除列表和缓存帧数控制。NPC 可按好感度、同位置、今日交谈、任务和生日过滤；Android 使用左触摸拖动小地图，桌面使用右键拖动；检测到 `Bouhm.NPCMapLocations` 时跳过工具箱地图功能并记录警告。
- 自动输入法控制只在 Windows 上生效：正常游戏操作先取消已有组合文本并关闭候选栏，再屏蔽输入法；屏蔽期间若系统按键切换又为游戏窗口关联输入法，则再次取消组合并屏蔽；游戏原生文本框或文字输入菜单活动时允许输入法；回到标题画面或关闭功能时恢复游戏窗口原有输入法上下文。Android 和其他非 Windows 平台直接跳过该功能，不调用 SDL Windows 信息或 `imm32.dll`。
- 工具箱主体保持 `net6.0` 托管 DLL 边界；Android 兼容指在支持 SMAPI 的 Android 运行环境中可加载并运行，不要求生成 Android 应用包。
- GMCM 是可选集成，未安装或 API 不兼容时不阻止工具箱主体加载。
- 输入法控制发生 Windows 原生查询错误时，错误仅归属输入法控制事件，不中断动物自动抚摸更新。
- 工具箱设置页每次调整后立即写入配置并应用；配置重置和保存作用于同一个工具箱配置对象。
