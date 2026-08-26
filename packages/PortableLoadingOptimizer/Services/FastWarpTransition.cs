using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewModdingAPI.Utilities;

namespace PortableLoadingOptimizer.Services;

internal sealed class FastWarpTransition
{
    private const float NativeOrdinaryFadeRate = 0.0019f;
    private const double MaximumArmedSeconds = 8d;
    private static readonly FieldInfo? ForcedRemoteWarpField = AccessTools.Field(typeof(Game1), "warpingForForcedRemoteEvent");
    private static readonly FieldInfo? NewDayField = AccessTools.Field(typeof(Game1), "newDay");
    private static readonly FieldInfo? EventOverField = AccessTools.Field(typeof(Game1), "eventOver");
    private static readonly FieldInfo? ExitToTitleField = AccessTools.Field(typeof(Game1), "exitToTitle");
    private static readonly FieldInfo? KillScreenField = AccessTools.Field(typeof(Game1), "killScreen");
    private static readonly FieldInfo? FestivalLocationField = AccessTools.Field(typeof(Game1), "whereIsTodaysFest");
    private static FastWarpTransition? instance;

    private readonly ModConfig config;
    private sealed class WarpState
    {
        internal bool Active;
        internal long Started;

        internal void Reset()
        {
            Active = false;
            Started = 0;
        }
    }

    private readonly PerScreen<WarpState> states = new(() => new WarpState());
    private readonly Queue<WorkerMessage> messages = new();
    private long armed;
    private long completed;
    private long excluded;

    internal FastWarpTransition(ModConfig config)
    {
        this.config = config;
    }

    internal void Apply(Harmony harmony)
    {
        instance = this;
        if (!config.EnableFastWarpTransitions)
            return;

        MethodInfo? performWarp = AccessTools.Method(
            typeof(Game1),
            "performWarpFarmer",
            new[] { typeof(LocationRequest), typeof(int), typeof(int), typeof(int) });
        MethodInfo? updateFade = AccessTools.Method(typeof(ScreenFade), "UpdateFadeAlpha", new[] { typeof(GameTime) });
        if (performWarp is null || updateFade is null)
        {
            messages.Enqueue(new WorkerMessage("[FAST WARP] 当前游戏版本没有匹配的淡入淡出入口，已保留原生速度。", LogLevel.Warn));
            return;
        }

        try
        {
            // 规则：先补观察器；如果后续启用失败，观察器只会看到默认的未激活状态。
            harmony.Patch(updateFade, postfix: new HarmonyMethod(typeof(FastWarpTransition), nameof(UpdateFadeAlphaPostfix)));
            harmony.Patch(performWarp, postfix: new HarmonyMethod(typeof(FastWarpTransition), nameof(PerformWarpPostfix)));
        }
        catch (Exception ex)
        {
            messages.Enqueue(new WorkerMessage($"[FAST WARP] 补丁失败，已保留原生速度：{ex.GetBaseException().Message}", LogLevel.Warn));
        }
    }

    internal void Reset() => states.Value.Reset();

    internal string GetStatus()
    {
        return $"[FAST WARP] enabled={config.EnableFastWarpTransitions}, armed={Interlocked.Read(ref armed)}, completed={Interlocked.Read(ref completed)}, excluded={Interlocked.Read(ref excluded)}";
    }

    internal bool TryDequeueMessage(out WorkerMessage message)
    {
        lock (messages)
        {
            if (messages.Count > 0)
            {
                message = messages.Dequeue();
                return true;
            }
        }

        message = default;
        return false;
    }

    private static void PerformWarpPostfix(LocationRequest locationRequest)
    {
        FastWarpTransition? current = instance;
        if (current is null || !current.config.EnableFastWarpTransitions)
            return;

        WarpState state = current.states.Value;
        state.Reset();
        state.Active = current.CanAccelerate(locationRequest);
        if (state.Active)
        {
            state.Started = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref current.armed);
        }
        else
            Interlocked.Increment(ref current.excluded);
    }

    private static void UpdateFadeAlphaPostfix(ScreenFade __instance, GameTime time)
    {
        FastWarpTransition? current = instance;
        if (current is null || !current.states.Value.Active)
            return;

        WarpState state = current.states.Value;
        if (GetElapsedMilliseconds(state.Started) > MaximumArmedSeconds * 1000d || !CanContinue(__instance))
        {
            state.Reset();
            return;
        }
        if (!__instance.fadeToBlack)
        {
            state.Reset();
            Interlocked.Increment(ref current.completed);
            return;
        }

        int elapsedMilliseconds = Math.Clamp(time.ElapsedGameTime.Milliseconds, 0, 100);
        float extra = NativeOrdinaryFadeRate * (float)(current.config.FastWarpTransitionMultiplier - 1d) * elapsedMilliseconds;
        __instance.fadeToBlackAlpha = __instance.fadeIn
            ? Math.Min(1.101f, __instance.fadeToBlackAlpha + extra)
            : Math.Max(-0.101f, __instance.fadeToBlackAlpha - extra);
    }

    private bool CanAccelerate(LocationRequest? request)
    {
        if (Context.IsMultiplayer && !config.EnableFastWarpTransitionsInMultiplayer)
            return false;
        if (!Context.IsWorldReady || !Game1.hasLoadedGame || Game1.player is null || Game1.currentLocation is null)
            return false;
        if (request?.Location is null || Game1.eventUp || Game1.farmEvent is not null || Game1.currentMinigame is not null)
            return false;
        if (Game1.player.passedOut || Game1.globalFade || Game1.nonWarpFade)
            return false;
        if (FestivalLocationField?.GetValue(null) is string festivalLocation
            && !string.IsNullOrWhiteSpace(festivalLocation)
            && string.Equals(request.Name, festivalLocation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !ReadBoolean(ForcedRemoteWarpField)
            && !ReadBoolean(NewDayField)
            && !ReadBoolean(EventOverField)
            && !ReadBoolean(ExitToTitleField)
            && !ReadBoolean(KillScreenField);
    }

    private static bool CanContinue(ScreenFade fade)
    {
        return !Game1.eventUp
            && Game1.farmEvent is null
            && Game1.currentMinigame is null
            && !fade.globalFade
            && !fade.nonWarpFade
            && !ReadBoolean(ForcedRemoteWarpField)
            && !ReadBoolean(NewDayField)
            && !ReadBoolean(EventOverField)
            && !ReadBoolean(ExitToTitleField);
    }

    private static bool ReadBoolean(FieldInfo? field) => field?.GetValue(null) is true;

    private static double GetElapsedMilliseconds(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }
}
