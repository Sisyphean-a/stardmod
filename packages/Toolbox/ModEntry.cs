using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Toolbox;

public sealed class ModEntry : Mod
{
    private const string StandaloneHarvestWithScytheId = "bcmpinc.HarvestWithScythe";
    private const string StandaloneConvenientInventoryId = "gaussfire.ConvenientInventory";
    private const string StandalonePassableCropsId = "NCarigon.PassableCrops";
    private const string StandaloneNpcMapLocationsId = "Bouhm.NPCMapLocations";
    private const string StandaloneLadderLocatorId = "ChaosEnergy.LadderLocator";
    private AutomaticGatesFeature automaticGatesFeature = null!;
    private InputMethodFeature inputMethodFeature = null!;
    private ModConfig Config = null!;
    private Vector2 lastPlayerPosition;
    private bool isInFarmArea;
    private bool standaloneHarvestWithScytheLoaded;
    private bool standaloneConvenientInventoryLoaded;
    private bool standalonePassableCropsLoaded;
    private bool standaloneNpcMapLocationsLoaded;
    private bool standaloneLadderLocatorLoaded;
    private NpcMapLocationsFeature? npcMapLocationsFeature;
    private LadderLocatorFeature? ladderLocatorFeature;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        automaticGatesFeature = new AutomaticGatesFeature(() => Config);
        inputMethodFeature = new InputMethodFeature(() => Config.EnableInputMethodControl, Monitor);
        Harmony harmony = new(ModManifest.UniqueID);
        LightRadiusFeature.Initialize(Config, ModManifest);
        LightRadiusFeature.ApplyPatches(harmony);
        FenceDecayFeature.Initialize(() => Config);
        FenceDecayFeature.ApplyPatches(harmony);

        standaloneConvenientInventoryLoaded = helper.ModRegistry.IsLoaded(StandaloneConvenientInventoryId);
        if (standaloneConvenientInventoryLoaded)
        {
            Monitor.Log("检测到独立版“Convenient Inventory”，已跳过工具箱内置快速堆叠功能；请只保留一个版本。", LogLevel.Warn);
        }
        else
        {
            QuickStackFeature.Initialize(helper, () => Config, Monitor);
            QuickStackFeature.ApplyPatches(harmony);
        }

        standalonePassableCropsLoaded = helper.ModRegistry.IsLoaded(StandalonePassableCropsId);
        if (standalonePassableCropsLoaded)
        {
            Monitor.Log("检测到独立版“合格作物”，已跳过工具箱内置补丁；请只保留一个版本。", LogLevel.Warn);
        }
        else
        {
            PassableCropsFeature.Initialize(() => Config);
            PassableCropsFeature.ApplyPatches(harmony);
        }

        standaloneNpcMapLocationsLoaded = helper.ModRegistry.IsLoaded(StandaloneNpcMapLocationsId);
        if (standaloneNpcMapLocationsLoaded)
        {
            Monitor.Log("检测到独立版“NPC地图位置”，已跳过工具箱内置功能；请只保留一个版本。", LogLevel.Warn);
        }
        else
        {
            npcMapLocationsFeature = new NpcMapLocationsFeature(helper, ModManifest, () => Config);
            npcMapLocationsFeature.RegisterEvents();
        }

        standaloneLadderLocatorLoaded = helper.ModRegistry.IsLoaded(StandaloneLadderLocatorId);
        if (standaloneLadderLocatorLoaded)
        {
            Monitor.Log("检测到独立版“梯子定位器”，已跳过工具箱内置版本；请移除独立版，否则仍会使用它原有的高亮行为。", LogLevel.Warn);
        }
        else
        {
            ladderLocatorFeature = new LadderLocatorFeature(helper);
            ladderLocatorFeature.RegisterEvents();
        }

        standaloneHarvestWithScytheLoaded = helper.ModRegistry.IsLoaded(StandaloneHarvestWithScytheId);
        if (standaloneHarvestWithScytheLoaded)
        {
            // Rule: the standalone mod patches the same game methods, so only one implementation may own them.
            Monitor.Log("检测到独立版“使用镰刀收割”，已跳过工具箱内置补丁；请只保留一个版本。", LogLevel.Warn);
        }
        else
        {
            HarvestWithScytheFeature.Initialize(() => Config);
            HarvestWithScytheFeature.ApplyPatches(harmony);
        }

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Player.Warped += OnPlayerWarped;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.UpdateTicked += automaticGatesFeature.OnUpdateTicked;
        helper.Events.GameLoop.UpdateTicked += inputMethodFeature.OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += automaticGatesFeature.OnReturnedToTitle;
        helper.Events.GameLoop.ReturnedToTitle += inputMethodFeature.OnReturnedToTitle;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        GenericModConfigMenuAdapter? api = GenericModConfigMenuAdapter.TryCreate(Helper.ModRegistry, Monitor);
        if (api is null)
        {
            Monitor.Log("未检测到 Generic Mod Config Menu；工具箱不会创建自定义设置页，请安装 spacechase0.GenericModConfigMenu 以配置工具箱。", LogLevel.Warn);
            return;
        }

        api.Register(
            ModManifest,
            reset: () =>
            {
                Config = new ModConfig();
                PassableCropsFeature.SetConfig(Config);
                LightRadiusFeature.SetConfig(Config);
                LightRadiusFeature.RefreshCurrentLocation();
                npcMapLocationsFeature?.OnConfigChanged();
            },
            save: () => Helper.WriteConfig(Config));
        api.AddSectionTitle(
            ModManifest,
            () => "自动抚摸动物",
            () => "控制农场和畜棚中的自动抚摸功能。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableAutoPet,
            value =>
            {
                Config.EnableAutoPet = value;
                if (value)
                    CheckAndPetAnimals();
            },
            () => "自动抚摸",
            () => "在农场和畜棚自动抚摸范围内的动物。");
        api.AddNumberOption(
            ModManifest,
            () => Config.CheckInterval,
            value => Config.CheckInterval = value,
            () => "检查间隔",
            () => "每隔多少帧检查一次（60帧=1秒）",
            5,
            60);
        api.AddNumberOption(
            ModManifest,
            () => Config.ScanRange,
            value => Config.ScanRange = value,
            () => "扫描范围",
            () => "检查玩家周围多少格范围内的动物",
            1,
            5);
        api.AddSectionTitle(
            ModManifest,
            () => "光照范围",
            () => "控制家具和普通物体的光照半径倍率。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableFurnitureLightRadius,
            value =>
            {
                Config.EnableFurnitureLightRadius = value;
                LightRadiusFeature.RefreshCurrentLocation();
            },
            () => "家具光线倍率",
            () => "调整家具光源半径。关闭后立即恢复原始半径。");
        api.AddNumberOption(
            ModManifest,
            () => Config.FurnitureLightRadius,
            value =>
            {
                Config.FurnitureLightRadius = value;
                LightRadiusFeature.RefreshCurrentLocation();
            },
            () => "家具光线倍率数值",
            () => "家具（室内）光源的半径倍率。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableObjectLightRadius,
            value =>
            {
                Config.EnableObjectLightRadius = value;
                LightRadiusFeature.RefreshCurrentLocation();
            },
            () => "物体光线倍率",
            () => "调整普通物体光源半径。关闭后立即恢复原始半径。");
        api.AddNumberOption(
            ModManifest,
            () => Config.ObjectLightRadius,
            value =>
            {
                Config.ObjectLightRadius = value;
                LightRadiusFeature.RefreshCurrentLocation();
            },
            () => "物体光线倍率数值",
            () => "所有非家具光源的半径倍率。");
        api.AddSectionTitle(
            ModManifest,
            () => "栅栏与自动门",
            () => "控制栅栏耐久保护和自动开关门。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableFenceDecay,
            value => Config.EnableFenceDecay = value,
            () => "栅栏防腐朽",
            () => "阻止栅栏和大门因时间流逝而损耗耐久。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableAutomaticGates,
            value => Config.EnableAutomaticGates = value,
            () => "自动开关门",
            () => "面对关闭的大门时自动打开，离开相邻格后自动关闭。关闭后不会处理已打开的大门。");
        api.AddNumberOption(
            ModManifest,
            () => Config.AutomaticGateCloseDelay,
            value => Config.AutomaticGateCloseDelay = value,
            () => "自动关门延迟",
            () => "玩家离开大门相邻格后，等待多少毫秒再关门。",
            0);
        api.AddSectionTitle(
            ModManifest,
            () => "输入与操作",
            () => "控制游戏操作期间的系统输入法。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableInputMethodControl,
            value => Config.EnableInputMethodControl = value,
            () => "自动输入法控制",
            () => "游戏操作时关闭系统输入法；游戏出现文字输入框时自动启用。关闭后立即恢复。");
        if (!standaloneHarvestWithScytheLoaded)
        {
            api.AddSectionTitle(
                ModManifest,
                () => "镰刀收割",
                () => "控制用镰刀收割作物、花朵和地面觅食物。");
            api.AddBoolOption(
                ModManifest,
                () => Config.EnableHarvestWithScythe,
                value => Config.EnableHarvestWithScythe = value,
                () => "镰刀收割",
                () => "允许用镰刀收割作物、花朵和地面觅食物；不支持用剑代替镰刀。");
        }

        if (!standaloneConvenientInventoryLoaded)
        {
            api.AddSectionTitle(
                ModManifest,
                () => "快速整理物品",
                () => "控制背包中的快速堆叠按钮和搜索范围。");
            api.AddBoolOption(
                ModManifest,
                () => Config.EnableQuickStack,
                value => Config.EnableQuickStack = value,
                () => "快速堆叠到附近箱子",
                () => "在背包页面显示按钮，将物品合并到附近普通箱子的已有堆叠中。");
            api.AddNumberOption(
                ModManifest,
                () => Config.QuickStackRange,
                value => Config.QuickStackRange = value,
                () => "快速堆叠距离",
                () => "搜索玩家周围多少格内的普通箱子。",
                1,
                64);
        }

        if (!standalonePassableCropsLoaded)
        {
            api.AddSectionTitle(
                ModManifest,
                () => "穿行与碰撞",
                () => "控制农民穿过作物、物体和其他环境元素的规则。");
            api.AddBoolOption(
                ModManifest,
                () => Config.EnablePassableCrops,
                value => Config.EnablePassableCrops = value,
                () => "穿过作物和物体",
                () => "允许农民穿过作物、茶树、树苗、杂草、洒水器和稻草人。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableCrops,
                value => Config.PassableCrops = value,
                () => "穿过作物",
                () => "允许穿过所有作物。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableScarecrows,
                value => Config.PassableScarecrows = value,
                () => "穿过稻草人",
                () => "允许穿过稻草人。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableSprinklers,
                value => Config.PassableSprinklers = value,
                () => "穿过洒水器",
                () => "允许穿过洒水器。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableForage,
                value => Config.PassableForage = value,
                () => "穿过觅食物",
                () => "允许穿过地面觅食物。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableTeaBushes,
                value => Config.PassableTeaBushes = value,
                () => "穿过茶树",
                () => "允许穿过成熟茶树。");
            api.AddNumberOption(
                ModManifest,
                () => Config.PassableTreeGrowth,
                value => Config.PassableTreeGrowth = value,
                () => "可穿过的树木生长阶段",
                () => "允许穿过不高于此阶段的普通树木，0 到 5。",
                0,
                5);
            api.AddNumberOption(
                ModManifest,
                () => Config.PassableFruitTreeGrowth,
                value => Config.PassableFruitTreeGrowth = value,
                () => "可穿过的果树生长阶段",
                () => "允许穿过不高于此阶段的果树，-1 表示不允许。",
                -1,
                5);
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableWeeds,
                value => Config.PassableWeeds = value,
                () => "穿过杂草",
                () => "允许穿过杂草。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PassableByAll,
                value => Config.PassableByAll = value,
                () => "允许所有生物穿过",
                () => "不只允许农民穿过，也允许其他生物穿过。");
            api.AddBoolOption(
                ModManifest,
                () => Config.SlowDownWhenPassing,
                value => Config.SlowDownWhenPassing = value,
                () => "穿过时减速",
                () => "穿过物体时像经过高草一样略微减速。");
            api.AddBoolOption(
                ModManifest,
                () => Config.ShakeWhenPassing,
                value => Config.ShakeWhenPassing = value,
                () => "穿过时摇晃",
                () => "经过物体时让物体摇晃。");
            api.AddBoolOption(
                ModManifest,
                () => Config.PlaySoundWhenPassing,
                value => Config.PlaySoundWhenPassing = value,
                () => "穿过时播放声音",
                () => "经过物体时播放草丛摩擦声。");
        }

        if (!standaloneNpcMapLocationsLoaded)
        {
            api.AddSectionTitle(
                ModManifest,
                () => "NPC 地图与小地图",
                () => "控制 NPC、农场建筑和小地图标记的显示规则。");
            api.AddBoolOption(
                ModManifest,
                () => Config.EnableNpcMapLocations,
                value =>
                {
                    Config.EnableNpcMapLocations = value;
                    OnNpcMapConfigChanged();
                },
                () => "NPC地图位置",
                () => "在游戏地图和小地图上显示NPC的当前位置。");
            api.AddBoolOption(
                ModManifest,
                () => Config.ShowMinimap,
                value =>
                {
                    Config.ShowMinimap = value;
                    OnNpcMapConfigChanged();
                },
                () => "显示NPC小地图",
                () => "在游戏界面左上角显示NPC小地图。");
            api.AddKeybindList(
                ModManifest,
                () => Config.MinimapToggleKey,
                value => Config.MinimapToggleKey = value,
                () => "小地图切换键",
                () => "按此键显示或隐藏NPC小地图。");
            api.AddNumberOption(
                ModManifest,
                () => Config.MinimapWidth,
                value =>
                {
                    Config.MinimapWidth = value;
                    OnNpcMapConfigChanged();
                },
                () => "小地图宽度",
                () => "小地图宽度（配置值会按游戏UI比例放大）。",
                45,
                180,
                15);
            api.AddNumberOption(
                ModManifest,
                () => Config.MinimapHeight,
                value =>
                {
                    Config.MinimapHeight = value;
                    OnNpcMapConfigChanged();
                },
                () => "小地图高度",
                () => "小地图高度（配置值会按游戏UI比例放大）。",
                45,
                180,
                15);
            api.AddNumberOption(
                ModManifest,
                () => Config.MinimapOpacity,
                value =>
                {
                    Config.MinimapOpacity = value;
                    OnNpcMapConfigChanged();
                },
                () => "小地图不透明度",
                () => "小地图的不透明度。",
                0.05f,
                1f,
                0.05f);
            api.AddBoolOption(
                ModManifest,
                () => Config.OnlySameLocation,
                value =>
                {
                    Config.OnlySameLocation = value;
                    OnNpcMapConfigChanged();
                },
                () => "只显示同位置NPC",
                () => "只显示与玩家处于同一室内或室外区域的NPC。");
            api.AddNumberOption(
                ModManifest,
                () => Config.HeartLevelMin,
                value =>
                {
                    Config.HeartLevelMin = value;
                    OnNpcMapConfigChanged();
                },
                () => "最小好感度",
                () => "只显示好感度不低于此值的NPC。",
                0,
                14);
            api.AddNumberOption(
                ModManifest,
                () => Config.HeartLevelMax,
                value =>
                {
                    Config.HeartLevelMax = value;
                    OnNpcMapConfigChanged();
                },
                () => "最大好感度",
                () => "只显示好感度不高于此值的NPC。",
                0,
                14);
            api.AddBoolOption(
                ModManifest,
                () => Config.ShowQuests,
                value =>
                {
                    Config.ShowQuests = value;
                    OnNpcMapConfigChanged();
                },
                () => "显示任务和生日",
                () => "在NPC标记上显示任务或生日提示。");
            api.AddBoolOption(
                ModManifest,
                () => Config.ShowHiddenVillagers,
                value =>
                {
                    Config.ShowHiddenVillagers = value;
                    OnNpcMapConfigChanged();
                },
                () => "显示隐藏村民",
                () => "显示原本未遇见或默认隐藏的村民。");
            api.AddBoolOption(
                ModManifest,
                () => Config.ShowHorse,
                value =>
                {
                    Config.ShowHorse = value;
                    OnNpcMapConfigChanged();
                },
                () => "显示马",
                () => "在地图上显示马的位置。");
            api.AddBoolOption(
                ModManifest,
                () => Config.ShowChildren,
                value =>
                {
                    Config.ShowChildren = value;
                    OnNpcMapConfigChanged();
                },
                () => "显示孩子",
                () => "在地图上显示孩子的位置。");
            api.AddBoolOption(
                ModManifest,
                () => Config.ShowFarmBuildings,
                value =>
                {
                    Config.ShowFarmBuildings = value;
                    OnNpcMapConfigChanged();
                },
                () => "显示农场建筑",
                () => "在地图上显示农场建筑标记。");
            api.AddNumberOption(
                ModManifest,
                () => (int)Config.NpcCacheTicks,
                value => Config.NpcCacheTicks = (uint)value,
                () => "NPC位置刷新间隔",
                () => "NPC标记更新间隔（帧）。",
                15,
                600,
                15);
        }

        if (!standaloneLadderLocatorLoaded)
        {
            api.AddSectionTitle(
                ModManifest,
                () => "矿井梯子提示",
                () => "固定规则：连续破坏十块石头仍未出现梯子后，才显示醒目的彩色下一层入口提示。");
            api.AddParagraph(
                ModManifest,
                () => "此功能没有可调整配置；进入矿井后前十块石头不会显示提示，之后才会标出可能的下一层入口。");
        }
    }

    private void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        isInFarmArea = e.NewLocation is Farm or AnimalHouse;
        lastPlayerPosition = e.Player.Position;

        if (isInFarmArea)
            CheckAndPetAnimals();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Config.EnableAutoPet
            || !isInFarmArea
            || !Context.IsWorldReady
            || !e.IsMultipleOf((uint)Config.CheckInterval))
            return;

        Vector2 position = Game1.player.Position;
        if (Vector2.DistanceSquared(position, lastPlayerPosition) < 1024f)
            return;

        lastPlayerPosition = position;
        CheckAndPetAnimals();
    }

    private void CheckAndPetAnimals()
    {
        if (!Config.EnableAutoPet
            || !Context.IsWorldReady
            || (Game1.currentLocation is not Farm && Game1.currentLocation is not AnimalHouse))
            return;

        Vector2 playerTile = new(
            (int)(Game1.player.Position.X / 64f),
            (int)(Game1.player.Position.Y / 64f));

        foreach (FarmAnimal animal in Game1.currentLocation.Animals.Values)
        {
            if (animal.wasPet.Value || animal.friendshipTowardFarmer.Value >= 1000)
                continue;

            Vector2 animalTile = new(
                (int)(animal.Position.X / 64f),
                (int)(animal.Position.Y / 64f));
            if (Math.Abs(animalTile.X - playerTile.X) > Config.ScanRange
                || Math.Abs(animalTile.Y - playerTile.Y) > Config.ScanRange)
            {
                continue;
            }

            animal.pet(Game1.player, false);
        }
    }

    private void OnNpcMapConfigChanged()
    {
        npcMapLocationsFeature?.OnConfigChanged();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        Vector2 position = Game1.player.Position;
        GameLocation currentLocation = Game1.currentLocation;
        Vector2 playerTile = new(
            (int)(position.X / 64f),
            (int)(position.Y / 64f));

        if (currentLocation is not Farm && currentLocation is not AnimalHouse)
            return;

        foreach (FarmAnimal animal in currentLocation.Animals.Values)
        {
            Vector2 animalTile = new(
                (int)(animal.Position.X / 64f),
                (int)(animal.Position.Y / 64f));
            if (Math.Abs(animalTile.X - playerTile.X) > 2f
                || Math.Abs(animalTile.Y - playerTile.Y) > 2f)
            {
                continue;
            }

            Monitor.Log("发现动物详细信息:", LogLevel.Debug);
            Monitor.Log($"- 名称: {animal.Name}", LogLevel.Debug);
            Monitor.Log($"- 类型: {animal.type.Value}", LogLevel.Debug);
            Monitor.Log($"- 年龄: {animal.age.Value} 天", LogLevel.Debug);
            Monitor.Log($"- 心情: {animal.happiness.Value}/255", LogLevel.Debug);
            Monitor.Log($"- 友好度: {animal.friendshipTowardFarmer.Value}/1000", LogLevel.Debug);
            Monitor.Log($"- 今日已被抚摸: {animal.wasPet.Value}", LogLevel.Debug);
            Monitor.Log($"- 位置: X={animalTile.X}, Y={animalTile.Y}", LogLevel.Debug);
            Monitor.Log($"- 产品质量: {animal.produceQuality.Value}", LogLevel.Debug);
            Monitor.Log("------------------------", LogLevel.Debug);
        }
    }
}
