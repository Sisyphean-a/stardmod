using HarmonyLib;
using StardewValley;

namespace Toolbox;

internal static class FenceDecayFeature
{
    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(Fence), nameof(Fence.minutesElapsed))!,
            postfix: new HarmonyMethod(typeof(FenceDecayFeature), nameof(MinutesElapsedPostfix)));
    }

    private static void MinutesElapsedPostfix(Fence __instance, ref bool __result)
    {
        if (!Game1.IsMasterGame)
            return;

        // Rule: 只有主机写入同步栅栏生命值；大门沿用原版的双倍耐久。
        __instance.health.Value = __instance.maxHealth.Value;
        if (__instance.isGate.Value)
            __instance.health.Value *= 2f;

        __result = false;
    }
}
