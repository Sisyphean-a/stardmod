using HarmonyLib;
using StardewValley;
using StardewValley.Locations;

namespace Toolbox;

internal static class FarmMusicFeature
{
    private static Func<ModConfig> GetConfig = null!;

    internal static void Initialize(Func<ModConfig> getConfig)
    {
        GetConfig = getConfig;
    }

    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(GameLocation), nameof(GameLocation.HandleMusicChange))!,
            prefix: new HarmonyMethod(typeof(FarmMusicFeature), nameof(HandleMusicChangePrefix)));
    }

    private static bool HandleMusicChangePrefix(GameLocation? oldLocation, GameLocation? newLocation)
    {
        return !GetConfig().EnableFarmMusic || !ShouldKeepFarmMusic(oldLocation, newLocation);
    }

    private static bool ShouldKeepFarmMusic(GameLocation? oldLocation, GameLocation? newLocation)
    {
        if (oldLocation is FarmHouse || newLocation is FarmHouse)
            return false;

        bool leavingFarm = oldLocation is Farm && IsFarmBuilding(newLocation);
        bool returningToFarm = IsFarmBuilding(oldLocation) && newLocation is Farm;
        return leavingFarm || returningToFarm;
    }

    private static bool IsFarmBuilding(GameLocation? location)
    {
        return location is not null
            && location is not FarmHouse
            && location.ParentBuilding?.GetParentLocation() is Farm;
    }
}
