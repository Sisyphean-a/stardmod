using System.Globalization;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Objects;

namespace Toolbox;

internal static class LightRadiusFeature
{
    private const string RecoveredLegacyRadiusKey = "{this.ModManifest.UniqueID}/base-radius";
    private const string OriginalRadiusKey = "irocendar.LightRadius/base-radius";

    private static ModConfig Config = null!;
    private static string RadiusKey = null!;

    internal static void Initialize(ModConfig config, IManifest manifest)
    {
        Config = config;
        RadiusKey = $"{manifest.UniqueID}/base-radius";
    }

    internal static void SetConfig(ModConfig config)
    {
        Config = config;
    }

    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(StardewValley.Object), "initializeLightSource")!,
            postfix: new HarmonyMethod(typeof(LightRadiusFeature), nameof(InitializeLightSourcePostfix)));
        harmony.Patch(
            AccessTools.Method(typeof(Furniture), "addLights")!,
            postfix: new HarmonyMethod(typeof(LightRadiusFeature), nameof(AddLightsPostfix)));
    }

    private static void AddLightsPostfix(ref Furniture __instance)
    {
        StardewValley.Object item = __instance;
        GameLocation? location = item.Location;
        if (location is null
            || (__instance.furniture_type.Value != 7
                && __instance.furniture_type.Value != 17
                && item.QualifiedItemId != "(F)1369")
            || item.lightSource is null)
        {
            return;
        }

        UpdateLightSource(
            item,
            Config.EnableFurnitureLightRadius ? Config.FurnitureLightRadius : 1f);
    }

    private static void InitializeLightSourcePostfix(ref StardewValley.Object __instance)
    {
        if (__instance.lightSource is null)
            return;

        UpdateLightSource(
            __instance,
            Config.EnableObjectLightRadius ? Config.ObjectLightRadius : 1f);
    }

    internal static void RefreshCurrentLocation()
    {
        if (!Context.IsWorldReady)
            return;

        GameLocation location = Game1.currentLocation;
        foreach (StardewValley.Object item in location.objects.Values)
            RefreshLightSource(item);
        foreach (Furniture furniture in location.furniture)
            RefreshLightSource(furniture);
    }

    private static void RefreshLightSource(StardewValley.Object item)
    {
        if (item.lightSource is null)
            return;

        float multiplier = item is Furniture furniture
            && (furniture.furniture_type.Value == 7
                || furniture.furniture_type.Value == 17
                || item.QualifiedItemId == "(F)1369")
            ? (Config.EnableFurnitureLightRadius ? Config.FurnitureLightRadius : 1f)
            : (Config.EnableObjectLightRadius ? Config.ObjectLightRadius : 1f);
        UpdateLightSource(item, multiplier);
    }

    private static void UpdateLightSource(StardewValley.Object item, float multiplier)
    {
        LightSource source = item.lightSource!;
        float baseRadius = GetBaseRadius(item, source.radius.Value);
        item.lightSource = new LightSource(
            source.Id,
            source.textureIndex.Value,
            source.position.Value,
            baseRadius * multiplier,
            source.color.Value,
            source.lightContext.Value,
            source.PlayerID,
            null);

        if (Game1.IsMasterGame && item.Location is not null)
            GameExtensions.AddLight(item.Location.sharedLights, item.lightSource.Clone());
    }

    private static float GetBaseRadius(StardewValley.Object item, float currentRadius)
    {
        if (!item.modData.ContainsKey(RadiusKey))
        {
            string? legacyKey = null;
            if (item.modData.ContainsKey(RecoveredLegacyRadiusKey))
                legacyKey = RecoveredLegacyRadiusKey;
            else if (item.modData.ContainsKey(OriginalRadiusKey))
                legacyKey = OriginalRadiusKey;

            string baseRadius = legacyKey is null
                ? currentRadius.ToString(CultureInfo.InvariantCulture)
                : item.modData[legacyKey];
            item.modData.Add(RadiusKey, baseRadius);
        }

        float.TryParse(
            item.modData[RadiusKey],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float result);
        return result;
    }
}
