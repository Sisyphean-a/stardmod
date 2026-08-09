using GenericModConfigMenu;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Toolbox;

public sealed class ModEntry : Mod
{
    private const string StandaloneHarvestWithScytheId = "bcmpinc.HarvestWithScythe";
    private AutomaticGatesFeature automaticGatesFeature = null!;
    private InputMethodFeature inputMethodFeature = null!;
    private ToolboxOptionsTab toolboxOptionsTab = null!;
    private ModConfig Config = null!;
    private Vector2 lastPlayerPosition;
    private bool isInFarmArea;
    private bool standaloneHarvestWithScytheLoaded;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        automaticGatesFeature = new AutomaticGatesFeature(() => Config);
        inputMethodFeature = new InputMethodFeature(() => Config.EnableInputMethodControl, Monitor);
        toolboxOptionsTab = new ToolboxOptionsTab(helper, () => Config, PersistConfig);
        Harmony harmony = new(ModManifest.UniqueID);
        LightRadiusFeature.Initialize(Config, ModManifest);
        LightRadiusFeature.ApplyPatches(harmony);
        FarmMusicFeature.Initialize(() => Config);
        FarmMusicFeature.ApplyPatches(harmony);
        FenceDecayFeature.Initialize(() => Config);
        FenceDecayFeature.ApplyPatches(harmony);
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
        helper.Events.Display.MenuChanged += toolboxOptionsTab.OnMenuChanged;
        helper.Events.Input.ButtonPressed += toolboxOptionsTab.OnButtonPressed;
        helper.Events.Display.RenderedActiveMenu += toolboxOptionsTab.OnRenderedActiveMenu;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi? api = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null)
            return;

        api.Register(
            ModManifest,
            reset: () =>
            {
                Config = new ModConfig();
                LightRadiusFeature.SetConfig(Config);
                LightRadiusFeature.RefreshCurrentLocation();
            },
            save: () => Helper.WriteConfig(Config));
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
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableFarmMusic,
            value => Config.EnableFarmMusic = value,
            () => "农场音乐保持",
            () => "在农场与农场建筑之间换场时保持音乐播放器音乐。");
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
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableInputMethodControl,
            value => Config.EnableInputMethodControl = value,
            () => "自动输入法控制",
            () => "游戏操作时关闭系统输入法；游戏出现文字输入框时自动启用。关闭后立即恢复。");
        if (!standaloneHarvestWithScytheLoaded)
        {
            api.AddBoolOption(
                ModManifest,
                () => Config.EnableHarvestWithScythe,
                value => Config.EnableHarvestWithScythe = value,
                () => "镰刀收割",
                () => "允许用镰刀收割作物、花朵和地面觅食物；不支持用剑代替镰刀。");
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

    private void PersistConfig(bool refreshLights, bool petAnimals)
    {
        Helper.WriteConfig(Config);

        if (refreshLights)
            LightRadiusFeature.RefreshCurrentLocation();
        if (petAnimals && Config.EnableAutoPet)
            CheckAndPetAnimals();
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
