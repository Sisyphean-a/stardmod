using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace FishingBarGrowth;

/// <summary>
/// Mod主入口类
/// </summary>
public class ModEntry : Mod
{
    private ModConfig _config = null!;
    private FishingHUD? _fishingHUD;

    /// <summary>
    /// Mod入口点
    /// </summary>
    public override void Entry(IModHelper helper)
    {
        // 加载配置
        _config = helper.ReadConfig<ModConfig>();

        // 初始化HUD
        _fishingHUD = new FishingHUD(_config, key => Helper.Translation.Get(key));

        // 初始化FishCounter
        FishCounter.Initialize(LogDebug);

        // 初始化补丁
        BobberBarPatch.Initialize(_config, LogDebug);

        // 应用Harmony补丁
        try
        {
            var harmony = new Harmony(ModManifest.UniqueID);
            harmony.PatchAll();
            Monitor.Log("Harmony补丁已成功应用", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Monitor.Log($"应用Harmony补丁失败: {ex}", LogLevel.Error);
        }

        // 注册事件
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Display.RenderedHud += OnRenderedHud;
    }

    /// <summary>
    /// HUD渲染事件
    /// </summary>
    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        _fishingHUD?.Draw(e.SpriteBatch);
    }

    /// <summary>
    /// 游戏启动后的事件处理
    /// </summary>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        // 集成Generic Mod Config Menu
        SetupConfigMenu();
    }

    /// <summary>
    /// 设置配置菜单
    /// </summary>
    private void SetupConfigMenu()
    {
        // 获取Generic Mod Config Menu的API
        var configMenuApi = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");

        if (configMenuApi == null)
        {
            Monitor.Log("未检测到Generic Mod Config Menu,配置UI将不可用", LogLevel.Info);
            return;
        }

        // 注册mod配置
        configMenuApi.Register(
            mod: ModManifest,
            reset: () =>
            {
                _config = new ModConfig();
                _fishingHUD?.UpdateConfig(_config);
                BobberBarPatch.Initialize(_config, LogDebug);
            },
            save: () => Helper.WriteConfig(_config)
        );

        // 添加主要设置部分
        configMenuApi.AddSectionTitle(
            mod: ModManifest,
            text: () => Helper.Translation.Get("config.main.title")
        );

        // 启用/禁用Mod
        configMenuApi.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.enable.name"),
            tooltip: () => Helper.Translation.Get("config.enable.tooltip"),
            getValue: () => _config.EnableMod,
            setValue: value => _config.EnableMod = value
        );

        // 每多少条鱼增加1像素
        configMenuApi.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.fishPerPixel.name"),
            tooltip: () => Helper.Translation.Get("config.fishPerPixel.tooltip"),
            getValue: () => _config.FishPerPixel,
            setValue: value => _config.FishPerPixel = value,
            min: 1,
            max: 100,
            interval: 1
        );

        // 最大钓鱼条高度
        configMenuApi.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.maxHeight.name"),
            tooltip: () => Helper.Translation.Get("config.maxHeight.tooltip"),
            getValue: () => _config.MaxBarHeight,
            setValue: value => _config.MaxBarHeight = value,
            min: 0,
            max: 1000,
            interval: 10
        );

        // 排除藻类
        configMenuApi.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.excludeAlgae.name"),
            tooltip: () => Helper.Translation.Get("config.excludeAlgae.tooltip"),
            getValue: () => _config.ExcludeAlgae,
            setValue: value => _config.ExcludeAlgae = value
        );

        // HUD显示部分
        configMenuApi.AddSectionTitle(
            mod: ModManifest,
            text: () => Helper.Translation.Get("config.hud.title")
        );

        // 显示HUD
        configMenuApi.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.showHUD.name"),
            tooltip: () => Helper.Translation.Get("config.showHUD.tooltip"),
            getValue: () => _config.ShowFishingHUD,
            setValue: value => _config.ShowFishingHUD = value
        );

        // HUD X位置
        configMenuApi.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.hudX.name"),
            tooltip: () => Helper.Translation.Get("config.hudX.tooltip"),
            getValue: () => _config.HudXOffset,
            setValue: value => _config.HudXOffset = value,
            min: 0,
            max: 500,
            interval: 10
        );

        // HUD Y位置
        configMenuApi.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.hudY.name"),
            tooltip: () => Helper.Translation.Get("config.hudY.tooltip"),
            getValue: () => _config.HudYOffset,
            setValue: value => _config.HudYOffset = value,
            min: 0,
            max: 500,
            interval: 10
        );

        // 调试部分
        configMenuApi.AddSectionTitle(
            mod: ModManifest,
            text: () => Helper.Translation.Get("config.debug.title")
        );

        // 显示调试信息
        configMenuApi.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.debugInfo.name"),
            tooltip: () => Helper.Translation.Get("config.debugInfo.tooltip"),
            getValue: () => _config.ShowDebugInfo,
            setValue: value => _config.ShowDebugInfo = value
        );

        Monitor.Log("Generic Mod Config Menu集成成功", LogLevel.Debug);
    }

    /// <summary>
    /// 记录调试信息
    /// </summary>
    private void LogDebug(string message, bool isError)
    {
        Monitor.Log(message, isError ? LogLevel.Error : LogLevel.Debug);
    }
}