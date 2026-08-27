---
scope: package:toolbox
---

# 工具箱包

`packages/Toolbox` 是面向简单、低耦合便利功能的合并型 SMAPI mod 包，运行时身份为 `xixifu.Toolbox`，入口程序集为 `Toolbox.dll`。

## 职责

- 提供动物自动抚摸等轻量功能。
- 提供家具、物体光源半径倍率调整。
- 在 Windows 上提供自动输入法控制：常规游戏操作时屏蔽输入法，游戏文字输入框获得焦点时恢复输入法；Android 和其他非 Windows 平台不调用该原生功能。
- 防止栅栏和大门因时间流逝而腐朽。
- 自动打开玩家面前的关闭大门，并在玩家离开后按配置延迟关闭。
- 提供用镰刀收割作物、花朵和地面觅食物的简化功能，不把剑当作镰刀；检测到旧的独立版 HarvestWithScythe 时跳过内置补丁，避免重复修改游戏方法。
- 提供背包内快速堆叠到附近箱子功能，距离可配置；只处理当前地点的普通箱子和大箱子，检测到独立版 ConvenientInventory 时跳过内置补丁，避免重复按钮和物品转移。
- 提供玩家箱子命名功能；在箱子物品菜单中为普通、大型、冰箱和其他特殊玩家箱子提供“改名”入口，关闭箱子后将名称贴在箱子本体上部显示，并保留名称的存档与联机同步。
- 提供可配置的穿过作物、茶树、树苗、果树、杂草、洒水器、稻草人和觅食物功能；检测到旧的独立版 PassableCrops 时跳过内置补丁，避免重复修改碰撞和绘制方法。
- 在原版世界地图和 HUD 小地图上显示 NPC、特殊商人、多人农民和农场建筑的位置，并提供 NPC 过滤与小地图配置；检测到旧的独立版 NPCMapLocations 时跳过内置地图功能，避免重复替换地图页。
- 在矿井按当前矿层累计普通石头的破坏次数；加入者通过原版同步的剩余石头数补齐主机造成的破坏；若此前未生成楼梯且该层存在下一层，则第 11 块石头安排必出的楼梯。
- 保留恢复出的动物信息调试处理器，但当前不注册按钮事件。
- 为这些功能提供一个合并的 `ModConfig`，配置入口统一使用 Generic Mod Config Menu（GMCM）；工具箱不再创建自定义游戏菜单。

## 边界与锚点

- SMAPI 入口与事件编排：`packages/Toolbox/ModEntry.cs`。
- 输入法平台门面：`packages/Toolbox/InputMethodFeature.cs`；Windows 实现：`packages/Toolbox/WindowsInputMethodFeature.cs`。
- 配置契约：`packages/Toolbox/ModConfig.cs`、`packages/Toolbox/config.json`。
- 光源 Harmony 补丁：`packages/Toolbox/LightRadiusFeature.cs`。
- 栅栏防腐朽补丁：`packages/Toolbox/FenceDecayFeature.cs`。
- 自动开关门事件控制器：`packages/Toolbox/AutomaticGatesFeature.cs`。
- 镰刀收割 Harmony 补丁：`packages/Toolbox/HarvestWithScytheFeature.cs`。
- 快速堆叠背包按钮和箱子转移：`packages/Toolbox/QuickStackFeature.cs`、`packages/Toolbox/assets/quickStackIcon.png`。
- 箱子命名菜单、箱子菜单按钮、关闭箱子后的名称显示和替换箱子时的名称保留：`packages/Toolbox/ChestNameFeature.cs`。
- 穿过作物 Harmony 补丁：`packages/Toolbox/PassableCropsFeature.cs`。
- NPC 地图位置与小地图：`packages/Toolbox/NpcMapLocationsFeature.cs`。
- 矿井梯子保证：`packages/Toolbox/LadderLocatorFeature.cs`。
- 可选 GMCM 运行时桥接：`packages/Toolbox/GenericModConfigMenuAdapter.cs`。
- 包身份：`packages/Toolbox/manifest.json`。
- 构建和游戏程序集引用：`packages/Toolbox/Toolbox.csproj`。

## 运行约束

- 动物自动抚摸只在农场或畜棚中运行，并按配置的检查间隔、扫描范围和动物状态决定是否抚摸。
- 光源补丁通过工具箱的 UniqueID 保存新的基础半径键；读取旧 LightRadiusMod 产生的键，避免合并后重复放大已有光源。
- 栅栏防腐朽补丁只由主机更新同步的栅栏生命值，并阻止原版的时间流逝损耗；大门维持原版双倍耐久。
- 自动开关门只处理已由该功能打开的大门；玩家面对关闭的大门时打开，离开其相邻格后按 `AutomaticGateCloseDelay` 关闭，关闭功能不会强制关闭已打开的大门。
- 镰刀收割由单一 `EnableHarvestWithScythe` 开关控制；开启时沿用默认行为：普通作物、花朵和觅食物可用镰刀收割，地面觅食物不要求位于耕地上，原本仅镰刀作物仍不能徒手收割；只识别实际镰刀，不支持剑替代。若检测到 `bcmpinc.HarvestWithScythe`，工具箱跳过这组内置补丁并记录警告，避免两个 mod 同时转译相同方法。
- 快速堆叠由 `EnableQuickStack` 和 `QuickStackRange` 控制；背包按钮按距离排序扫描当前地点的普通箱子/大箱子，把背包物品合并到已有相同堆叠，已有堆叠装满时才在同一箱子中新增堆叠。不会处理空箱中没有同类物品的物品，也不会处理冰箱、梳妆台、磨坊或其他特殊库存。若检测到 `gaussfire.ConvenientInventory`，工具箱跳过补丁并记录警告。
- 箱子命名不新增配置项；打开任意 `playerChest` 后，物品菜单显示“改名”按钮，普通、大型、冰箱、聚宝盆等特殊玩家箱子均可使用。关闭箱子且没有活动菜单时，自定义名称贴在箱子本体上部显示；被玩家占用或正在打开菜单时不显示标签。名称沿用原版 `NamingMenu` 的输入过滤，最多 32 个字符；提交空白名称恢复默认名称“Chest”。名称写入箱子的原生名称字段并用工具箱 `modData` 保留自定义值和原始默认值，存档、联机和箱子类型替换都会保留；非玩家临时战利品箱不提供入口。
- 穿过作物由 `EnablePassableCrops` 控制；分类开关和树木生长阶段沿用 PassableCrops 配置，只有农民可穿过，除非开启 `PassableByAll`。碰撞、减速、摇晃、声音和可选自定义绘制由同一补丁负责。检测到 `NCarigon.PassableCrops` 时跳过内置补丁并记录警告。
- NPC 地图功能由 `EnableNpcMapLocations` 控制；地图页使用原版 `WorldMapManager` 计算室外和建筑室内位置；小地图场景显示规则见[工具箱领域上下文](../../requirements/contexts/toolbox.md)，并按缓存帧数更新。检测到 `Bouhm.NPCMapLocations` 时跳过内置地图页和小地图事件并记录警告。
- NPC 地图默认按任务、生日、好感度、同位置和已交谈状态过滤；切换工具箱功能配置会立即刷新标记和当前地图页。农场建筑使用工具箱绘制的简化标记，不依赖独立 mod 的外部图片资源；Android 触摸使用左键拖动小地图，桌面继续使用右键拖动。
- 配置统一由 GMCM 展示和保存；GMCM 修改应即时反映到运行中的功能，重置和保存作用于同一个工具箱配置对象。
- GMCM 重置配置时必须同步光源功能持有的配置引用，并立即刷新当前场景的光源半径。
- 功能开关包括自动抚摸、两类光源半径、栅栏防腐朽、自动开关门、自动输入法控制、镰刀收割、快速堆叠、穿过作物和 NPC 地图位置；光源配置变化会立即重算当前场景的光源半径，且只有主机写入同步光源。
- 工具箱以 `net6.0` 托管 DLL 作为跨平台运行时边界；不依赖 `net6.0-android` 应用目标，也不在包内引入 Android 原生 UI 或桌面原生库。
- 自动输入法控制只在 Windows 生效；`InputMethodFeature` 在其他平台不实例化 Windows 实现，因此 Android 不加载 SDL Windows 信息和 `imm32.dll` 调用。Windows 实现仍通过 SDL 取得实际窗口句柄并保留原有输入法上下文。
- GMCM 是可选外部集成；工具箱通过反射桥接查询 API，未安装或版本不兼容时不阻止工具箱主体加载，但不会创建自定义配置菜单，用户需安装兼容的 GMCM 才能使用游戏内配置入口。
- 输入法控制的 Windows 查询错误会作为该可选功能的日志暴露，不中断工具箱的动物自动抚摸和其他功能。
