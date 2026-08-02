using GenericModConfigMenu;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Toolbox;

public sealed class ModEntry : Mod
{
    private InputMethodFeature inputMethodFeature = null!;
    private ModConfig Config = null!;
    private Vector2 lastPlayerPosition;
    private bool isInFarmArea;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        inputMethodFeature = new InputMethodFeature(() => Config.EnableInputMethodControl);
        Harmony harmony = new(ModManifest.UniqueID);
        LightRadiusFeature.Initialize(Config, ModManifest);
        LightRadiusFeature.ApplyPatches(harmony);
        FarmMusicFeature.ApplyPatches(harmony);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Player.Warped += OnPlayerWarped;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.UpdateTicked += inputMethodFeature.OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += inputMethodFeature.OnReturnedToTitle;
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
            },
            save: () => Helper.WriteConfig(Config));
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
        api.AddNumberOption(
            ModManifest,
            () => Config.FurnitureLightRadius,
            value => Config.FurnitureLightRadius = value,
            () => "家具光线倍率",
            () => "家具（室内）光源的半径倍率。");
        api.AddNumberOption(
            ModManifest,
            () => Config.ObjectLightRadius,
            value => Config.ObjectLightRadius = value,
            () => "物体光线倍率",
            () => "所有非家具光源的半径倍率。");
        api.AddBoolOption(
            ModManifest,
            () => Config.EnableInputMethodControl,
            value => Config.EnableInputMethodControl = value,
            () => "自动输入法控制",
            () => "游戏操作时关闭系统输入法；游戏出现文字输入框时自动启用。关闭后立即恢复。");
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
        if (!isInFarmArea || !Context.IsWorldReady || !e.IsMultipleOf((uint)Config.CheckInterval))
            return;

        Vector2 position = Game1.player.Position;
        if (Vector2.DistanceSquared(position, lastPlayerPosition) < 1024f)
            return;

        lastPlayerPosition = position;
        CheckAndPetAnimals();
    }

    private void CheckAndPetAnimals()
    {
        if (Game1.currentLocation is not Farm && Game1.currentLocation is not AnimalHouse)
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
