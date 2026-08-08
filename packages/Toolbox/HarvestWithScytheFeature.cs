using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using xTile.Dimensions;

namespace Toolbox;

internal static class HarvestWithScytheFeature
{
    private static Func<ModConfig> GetConfig = null!;

    internal static void Initialize(Func<ModConfig> getConfig)
    {
        GetConfig = getConfig;
    }

    internal static void ApplyPatches(Harmony harmony)
    {
        // Flow: crops use the game's forced-scythe path; forage keeps the game's quality and experience hooks.
        harmony.Patch(
            AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performUseAction))!,
            transpiler: new HarmonyMethod(typeof(HarvestWithScytheFeature), nameof(ReplaceCropHarvestMethodForUseTranspiler)));
        harmony.Patch(
            AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performToolAction))!,
            transpiler: new HarmonyMethod(typeof(HarvestWithScytheFeature), nameof(ReplaceCropHarvestMethodForToolTranspiler)));
        harmony.Patch(
            AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.performToolAction))!,
            prefix: new HarmonyMethod(typeof(HarvestWithScytheFeature), nameof(ObjectPerformToolActionPrefix)));
        harmony.Patch(
            AccessTools.Method(typeof(GameLocation), nameof(GameLocation.checkAction))!,
            prefix: new HarmonyMethod(typeof(HarvestWithScytheFeature), nameof(GameLocationCheckActionPrefix)));
    }

    private static IEnumerable<CodeInstruction> ReplaceCropHarvestMethodForUseTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getPlayer = AccessTools.PropertyGetter(typeof(Game1), nameof(Game1.player))!;
        MethodInfo getCurrentTool = AccessTools.PropertyGetter(typeof(Farmer), nameof(Farmer.CurrentTool))!;
        return ReplaceCropHarvestMethodCall(
            instructions,
            new CodeInstruction(OpCodes.Call, getPlayer),
            new CodeInstruction(OpCodes.Callvirt, getCurrentTool));
    }

    private static IEnumerable<CodeInstruction> ReplaceCropHarvestMethodForToolTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceCropHarvestMethodCall(
            instructions,
            new CodeInstruction(OpCodes.Ldarg_1));
    }

    private static IEnumerable<CodeInstruction> ReplaceCropHarvestMethodCall(
        IEnumerable<CodeInstruction> instructions,
        params CodeInstruction[] toolInstructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo original = AccessTools.Method(typeof(Crop), nameof(Crop.GetHarvestMethod))!;
        MethodInfo replacement = AccessTools.Method(
            typeof(HarvestWithScytheFeature),
            nameof(GetHarvestMethodForTool),
            new[] { typeof(Crop), typeof(Tool) })!;

        int replaced = 0;
        for (int index = 0; index < code.Count; index++)
        {
            if (!code[index].Calls(original))
                continue;

            foreach (CodeInstruction toolInstruction in toolInstructions)
                code.Insert(index++, toolInstruction);

            code[index].opcode = OpCodes.Call;
            code[index].operand = replacement;
            replaced++;
        }

        if (replaced != 1)
        {
            throw new InvalidOperationException(
                $"Expected one Crop.GetHarvestMethod call, but replaced {replaced}.");
        }

        return code;
    }

    private static HarvestMethod GetHarvestMethodForTool(Crop crop, Tool? tool)
    {
        HarvestMethod vanillaMethod = crop.GetHarvestMethod();
        if (!GetConfig().EnableHarvestWithScythe || !IsScythe(tool))
            return vanillaMethod;

        return HarvestMethod.Scythe;
    }

    private static bool IsScythe(Tool? tool)
    {
        // Rule: only actual scythes qualify; swords never enter this path.
        return tool is MeleeWeapon meleeWeapon && meleeWeapon.isScythe();
    }

    private static bool ObjectPerformToolActionPrefix(
        StardewValley.Object __instance,
        Tool t,
        ref bool __result)
    {
        if (!ScytheForage(__instance, t))
            return true;

        __result = true;
        return false;
    }

    private static bool ScytheForage(StardewValley.Object item, Tool tool)
    {
        if (!GetConfig().EnableHarvestWithScythe || !IsForage(item) || !IsScythe(tool))
            return false;

        Vector2 tileLocation = item.TileLocation;
        if (tileLocation == Vector2.Zero)
        {
            GameLocation? location = item.Location;
            if (location is null)
                throw new InvalidOperationException("Forage object has no location.");

            foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
            {
                if (ReferenceEquals(pair.Value, item))
                {
                    tileLocation = pair.Key;
                    break;
                }
            }
        }

        HarvestForage(item, tool, tileLocation);
        return true;
    }

    private static void HarvestForage(StardewValley.Object item, Tool tool, Vector2 tileLocation)
    {
        // Effect: the caller removes the forage object after this method returns true.
        Farmer farmer = tool.getLastFarmerToUse() ?? Game1.player;
        GameLocation location = farmer.currentLocation;
        Random random = Utility.CreateDaySaveRandom(tileLocation.X, tileLocation.Y * 777f, 0f);

        item.Quality = location.GetHarvestSpawnedObjectQuality(
            farmer,
            item.isForage(),
            tileLocation,
            null);
        location.OnHarvestedForage(farmer, item);

        Vector2 pixelOrigin = tileLocation * 64f + new Vector2(32f, 32f);
        Game1.createItemDebris(item, pixelOrigin, -1);

        if (farmer.professions.Contains(13) && random.NextDouble() < 0.2)
        {
            location.OnHarvestedForage(farmer, item);
            Game1.createItemDebris(item.getOne(), pixelOrigin, -1);
        }
    }

    private static bool GameLocationCheckActionPrefix(
        GameLocation __instance,
        Location tileLocation,
        Farmer who,
        ref bool __result)
    {
        if (!GetConfig().EnableHarvestWithScythe || who.isRidingHorse())
            return true;

        Vector2 tile = new(tileLocation.X, tileLocation.Y);
        if (!__instance.objects.TryGetValue(tile, out StardewValley.Object? item)
            || item.Type is null
            || !MayTriggerScythe(item, who))
        {
            return true;
        }

        __result = true;
        return false;
    }

    private static bool IsForage(StardewValley.Object item)
    {
        return item.IsSpawnedObject && !item.questItem.Value && item.isForage();
    }

    private static bool MayTriggerScythe(StardewValley.Object item, Farmer who)
    {
        Tool? currentTool = who.CurrentTool;
        if (!IsForage(item) || !IsScythe(currentTool))
            return false;

        who.CanMove = false;
        who.UsingTool = true;
        who.canReleaseTool = true;
        who.Halt();

        GameLocation location = item.Location ?? who.currentLocation;
        currentTool.beginUsing(
            location,
            (int)who.lastClick.X,
            (int)who.lastClick.Y,
            who);
        ((MeleeWeapon)currentTool).setFarmerAnimating(who);
        return true;
    }
}
