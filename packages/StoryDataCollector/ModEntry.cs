using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace StoryDataCollector;

public sealed class ModEntry : Mod
{
    private TimelineDataCollector? collector;

    public override void Entry(IModHelper helper)
    {
        ModConfig config = helper.ReadConfig<ModConfig>();
        if (config.Normalize())
            helper.WriteConfig(config);

        if (!config.Enabled)
        {
            Monitor.Log("故事数据采集器已在配置中停用。", LogLevel.Info);
            return;
        }

        collector = new TimelineDataCollector(helper, config, Monitor);
        helper.Events.GameLoop.SaveLoaded += collector.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += collector.OnDayStarted;
        helper.Events.GameLoop.DayEnding += collector.OnDayEnding;
        helper.Events.GameLoop.Saving += collector.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += collector.OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += collector.OnUpdateTicked;
        helper.Events.Player.Warped += collector.OnPlayerWarped;

        Harmony harmony = new(ModManifest.UniqueID);
        HarmonyPatches.Apply(harmony, collector, Monitor);
        HarmonyPatches.LogStatus(Monitor);

        helper.ConsoleCommands.Add(
            "story_data",
            "故事数据采集器：story_data status | flush（写入有界 checkpoint 快照）",
            OnConsoleCommand);

        Monitor.Log("故事数据采集器已加载（有界事实采集、剧情事件、checkpoint 与 AI 叙事输入）。", LogLevel.Info);
    }

    private void OnConsoleCommand(string command, string[] args)
    {
        if (collector is null)
            return;

        string action = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "status";
        switch (action)
        {
            case "status":
                collector.LogStatus();
                HarmonyPatches.LogStatus(Monitor);
                break;
            case "flush":
                collector.FlushCheckpoint();
                break;
            default:
                Monitor.Log("未知指令。请使用 story_data status | flush（写入有界 checkpoint 快照）。", LogLevel.Warn);
                break;
        }
    }
}
