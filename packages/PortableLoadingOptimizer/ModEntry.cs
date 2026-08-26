using System.Reflection;
using HarmonyLib;
using PortableLoadingOptimizer.Services;
using StardewModdingAPI;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Events;

namespace PortableLoadingOptimizer;

public sealed class ModEntry : Mod
{
    private const string OriginalOptimizerId = "neoiw.StardewLoadingOptimizer";

    private ModConfig config = null!;
    private PlatformPolicy platform = null!;
    private Harmony harmony = null!;
    private SaveMenuDelayOptimizer saveDelay = null!;
    private BackgroundFilePrefetcher prefetcher = null!;
    private FastWarpTransition? fastWarp;
    private bool disabledDueToConflict;

    public override void Entry(IModHelper helper)
    {
        if (helper.ModRegistry.IsLoaded(OriginalOptimizerId))
        {
            Monitor.Log("检测到 Stardew Loading Optimizer。为避免重复补丁和双重预读，本 Mod 已停用；请只保留其中一个。", LogLevel.Error);
            return;
        }

        config = helper.ReadConfig<ModConfig>();
        if (config.Normalize())
            helper.WriteConfig(config);

        platform = PlatformPolicy.Create(config);
        string modsPath = Directory.GetParent(helper.DirectoryPath)?.FullName ?? helper.DirectoryPath;
        prefetcher = new BackgroundFilePrefetcher(modsPath, GetSavesPath(), config, platform);
        harmony = new Harmony(ModManifest.UniqueID);
        saveDelay = new SaveMenuDelayOptimizer(config);
        saveDelay.Apply(harmony);

        if (platform.SupportsFastWarp)
        {
            fastWarp = new FastWarpTransition(config);
            fastWarp.Apply(harmony);
        }

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Specialized.LoadStageChanged += OnLoadStageChanged;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.Saved += OnSaved;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.ConsoleCommands.Add(
            "portable_loading",
            "跨平台加载优化器：portable_loading status | prefetch | stop",
            OnCommand);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        if (Helper.ModRegistry.IsLoaded(OriginalOptimizerId))
        {
            disabledDueToConflict = true;
            harmony.UnpatchAll(ModManifest.UniqueID);
            prefetcher.Stop("duplicate-optimizer");
            Monitor.Log("GameLaunched 时发现 Stardew Loading Optimizer 已加载；本包已撤销自己的补丁并停用。请只保留一个加载优化器。", LogLevel.Warn);
            return;
        }

        string fastWarpStatus = platform.SupportsFastWarp
            ? config.EnableFastWarpTransitions.ToString()
            : "false (当前平台使用原生淡入淡出)";
        Monitor.Log(
            $"跨平台加载优化器已启动：platform={platform.Name}, saveDelay={config.RemoveSaveSelectionDelay}, "
            + $"prefetch={config.EnableBackgroundFilePrefetch} ({platform.PrefetchMaximumMegabytes}MB @ {platform.PrefetchMegabytesPerSecond}MB/s), "
            + $"fastWarp={fastWarpStatus}。",
            LogLevel.Info);
        prefetcher.Start();
        DrainMessages();
    }

    private void OnLoadStageChanged(object? sender, LoadStageChangedEventArgs e)
    {
        if (disabledDueToConflict)
            return;
        if (e.NewStage is LoadStage.None or LoadStage.Ready)
            prefetcher.Resume(2);
        else
            prefetcher.Pause($"save-load:{e.NewStage}");
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (disabledDueToConflict)
            return;
        fastWarp?.Reset();
        prefetcher.Resume(2);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (disabledDueToConflict)
            return;
        prefetcher.Pause("saving");
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        if (disabledDueToConflict)
            return;
        prefetcher.Resume(2);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        if (disabledDueToConflict)
            return;
        fastWarp?.Reset();
        prefetcher.Pause("returned-to-title");
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (disabledDueToConflict)
            return;
        DrainMessages();
    }

    private void OnCommand(string command, string[] args)
    {
        if (disabledDueToConflict)
            return;
        string action = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "status";
        switch (action)
        {
            case "status":
                Monitor.Log($"[PLATFORM] {platform.Name}; Android={platform.IsAndroid}; Windows={platform.IsWindows}", LogLevel.Info);
                Monitor.Log(saveDelay.GetStatus(), LogLevel.Info);
                Monitor.Log(prefetcher.GetStatus(), LogLevel.Info);
                if (fastWarp is not null)
                    Monitor.Log(fastWarp.GetStatus(), LogLevel.Info);
                else
                    Monitor.Log("[FAST WARP] unsupported-on-this-platform; native fade is active", LogLevel.Info);
                break;
            case "prefetch":
                prefetcher.Start(restartIfCompleted: true);
                prefetcher.Resume();
                break;
            case "stop":
                prefetcher.Stop("console");
                break;
            default:
                Monitor.Log("未知指令。请使用 portable_loading status | prefetch | stop。", LogLevel.Warn);
                break;
        }
    }

    private void DrainMessages()
    {
        while (saveDelay.TryDequeueMessage(out WorkerMessage saveDelayMessage))
            Monitor.Log(saveDelayMessage.Text, saveDelayMessage.Level);
        while (prefetcher.TryDequeueMessage(out WorkerMessage prefetchMessage))
            Monitor.Log(prefetchMessage.Text, prefetchMessage.Level);
        if (fastWarp is not null)
        {
            while (fastWarp.TryDequeueMessage(out WorkerMessage fastWarpMessage))
                Monitor.Log(fastWarpMessage.Text, fastWarpMessage.Level);
        }
    }

    private static string GetSavesPath()
    {
        try
        {
            PropertyInfo? property = typeof(Constants).GetProperty("SavesPath", BindingFlags.Public | BindingFlags.Static);
            if (property?.GetValue(null) is string savesPath && !string.IsNullOrWhiteSpace(savesPath))
                return savesPath;
        }
        catch
        {
            // Older Android SMAPI builds may not expose Constants.SavesPath.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves");
    }
}
