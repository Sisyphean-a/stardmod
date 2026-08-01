using HarmonyLib;
using StardewValley;
using StardewValley.Locations;

namespace Toolbox;

internal static class FarmMusicFeature
{
    private const string MusicPlayerTrack = "sam_acoustic1";

    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(GameLocation), nameof(GameLocation.HandleMusicChange))!,
            prefix: new HarmonyMethod(typeof(FarmMusicFeature), nameof(HandleMusicChangePrefix)));
    }

    private static bool HandleMusicChangePrefix(GameLocation? oldLocation, GameLocation? newLocation)
    {
        return !ShouldKeepFarmMusic(oldLocation, newLocation);
    }

    private static bool ShouldKeepFarmMusic(GameLocation? oldLocation, GameLocation? newLocation)
    {
        if (Game1.getMusicTrackName() != MusicPlayerTrack)
            return false;

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
