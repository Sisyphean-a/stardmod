using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using Location = xTile.Dimensions.Location;
using Object = StardewValley.Object;

namespace Toolbox;

internal static class PassableCropsFeature
{
    private static ModConfig currentConfig = null!;
    private static Character? lastCharacter;
    private static DrawState drawState;
    private static readonly Dictionary<Object, ShakeState> shakeStates = new();
    private static GameLocation? shakeLocation;
    private static bool shakeStateEnabled;

    internal static void Initialize(Func<ModConfig> getConfig)
    {
        currentConfig = getConfig();
    }

    internal static void SetConfig(ModConfig config)
    {
        // GMCM replaces the config object on reset; keep the hot Harmony path on the current instance.
        currentConfig = config;
    }

    internal static void ApplyPatches(Harmony harmony)
    {
        Patch(
            harmony,
            AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.isPassable), new[] { typeof(Character) }),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(HoeDirtIsPassablePostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Bush), nameof(Bush.isPassable), new[] { typeof(Character) }),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(BushIsPassablePostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Bush), nameof(Bush.draw), new[] { typeof(SpriteBatch) }),
            prefix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(BushDrawPrefix)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(TerrainDrawPostfix)),
            transpiler: new HarmonyMethod(typeof(PassableCropsFeature), nameof(LocalDrawTranspiler)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Bush), nameof(Bush.getBoundingBox)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(BushGetBoundingBoxPostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Tree), nameof(Tree.isPassable), new[] { typeof(Character) }),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(TreeIsPassablePostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Tree), nameof(Tree.draw), new[] { typeof(SpriteBatch) }),
            prefix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(TreeDrawPrefix)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(TerrainDrawPostfix)),
            transpiler: new HarmonyMethod(typeof(PassableCropsFeature), nameof(LocalDrawTranspiler)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Tree), nameof(Tree.getBoundingBox)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(TreeGetBoundingBoxPostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(FruitTree), nameof(FruitTree.isPassable), new[] { typeof(Character) }),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(FruitTreeIsPassablePostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(FruitTree), nameof(FruitTree.draw), new[] { typeof(SpriteBatch) }),
            prefix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(FruitTreeDrawPrefix)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(TerrainDrawPostfix)),
            transpiler: new HarmonyMethod(typeof(PassableCropsFeature), nameof(LocalDrawTranspiler)));
        Patch(
            harmony,
            AccessTools.Method(typeof(FruitTree), nameof(FruitTree.getBoundingBox)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(FruitTreeGetBoundingBoxPostfix)));

        Patch(
            harmony,
            AccessTools.Method(
                typeof(GameLocation),
                nameof(GameLocation.isCollidingPosition),
                new[]
                {
                    typeof(Rectangle),
                    typeof(Rectangle),
                    typeof(bool),
                    typeof(int),
                    typeof(bool),
                    typeof(Character),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                }),
            prefix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(CollisionPrefix)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(CollisionPostfix)));
        Patch(
            harmony,
            AccessTools.Method(
                typeof(GameLocation),
                nameof(GameLocation.checkAction),
                new[] { typeof(Location), typeof(Rectangle), typeof(Farmer) }),
            prefix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(CheckActionPrefix)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(CheckActionPostfix)));
        Patch(
            harmony,
            AccessTools.Method(typeof(Object), nameof(Object.isPassable)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(ObjectIsPassablePostfix)));
        Patch(
            harmony,
            AccessTools.Method(
                typeof(Object),
                nameof(Object.draw),
                new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) }),
            prefix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(ObjectDrawPrefix)),
            postfix: new HarmonyMethod(typeof(PassableCropsFeature), nameof(ObjectDrawPostfix)),
            transpiler: new HarmonyMethod(typeof(PassableCropsFeature), nameof(LocalDrawTranspiler)));
    }

    private static void Patch(
        Harmony harmony,
        System.Reflection.MethodBase? method,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null,
        HarmonyMethod? transpiler = null)
    {
        if (method is not null)
            harmony.Patch(method, prefix, postfix, transpiler);
    }

    private static void HoeDirtIsPassablePostfix(HoeDirt __instance, ref bool __result, Character c)
    {
        if (!currentConfig.EnablePassableCrops || !currentConfig.PassableCrops || __instance.crop is null)
            return;

        if (CanPass(c))
        {
            __result = true;
            SlowDown(c);
        }
    }

    private static void BushIsPassablePostfix(Bush __instance, ref bool __result, Character c)
    {
        if (!currentConfig.EnablePassableCrops
            || !currentConfig.PassableTeaBushes
            || __instance.size.Value != Bush.largeBush
            || __instance.inPot.Value
            || !CanPass(c))
        {
            return;
        }

        __result = true;
        SlowDown(c);
        ShakeTerrain(__instance, c);
    }

    private static void TreeIsPassablePostfix(Tree __instance, ref bool __result, Character c)
    {
        if (!currentConfig.EnablePassableCrops
            || !CanPass(c)
            || currentConfig.PassableTreeGrowth < GetTreeGrowth(__instance))
        {
            return;
        }

        __result = true;
        SlowDown(c);
        ShakeTerrain(__instance, c);
    }

    private static void FruitTreeIsPassablePostfix(FruitTree __instance, ref bool __result, Character c)
    {
        if (!currentConfig.EnablePassableCrops
            || !CanPass(c)
            || currentConfig.PassableFruitTreeGrowth < Math.Min(__instance.growthStage.Value, 5))
        {
            return;
        }

        __result = true;
        SlowDown(c);
        ShakeTerrain(__instance, c);
    }

    private static void CollisionPrefix(Character character)
    {
        lastCharacter = character;
    }

    private static void CollisionPostfix()
    {
        lastCharacter = null;
    }

    private static void CheckActionPrefix(Farmer who)
    {
        lastCharacter = who;
    }

    private static void CheckActionPostfix()
    {
        lastCharacter = null;
    }

    private static void ObjectIsPassablePostfix(Object __instance, ref bool __result)
    {
        if (!currentConfig.EnablePassableCrops
            || !TryGetPassableObject(__instance, out PassableObjectType objectType)
            || !CanPass(lastCharacter))
        {
            return;
        }

        __result = true;
        SlowDown(lastCharacter!);
        if (lastCharacter is not FarmAnimal)
        {
            ShakeObject(__instance, objectType);
        }
    }

    private static void BushDrawPrefix(Bush __instance)
    {
        drawState = default;
        if (currentConfig.EnablePassableCrops && IsPassableBush(__instance))
            drawState = new DrawState(DrawObjectType.Terrain, 0f);
    }

    private static void TreeDrawPrefix(Tree __instance)
    {
        drawState = default;
        if (currentConfig.EnablePassableCrops && IsPassableTree(__instance))
            drawState = new DrawState(DrawObjectType.Terrain, 0f);
    }

    private static void FruitTreeDrawPrefix(FruitTree __instance)
    {
        drawState = default;
        if (currentConfig.EnablePassableCrops && IsPassableFruitTree(__instance))
            drawState = new DrawState(DrawObjectType.Terrain, 0f);
    }

    private static void TerrainDrawPostfix()
    {
        drawState = default;
    }

    private static void BushGetBoundingBoxPostfix(ref Rectangle __result)
    {
        if (drawState.Type == DrawObjectType.Terrain)
        {
            drawState = default;
            __result.Y -= 46;
        }
    }

    private static void TreeGetBoundingBoxPostfix(Tree __instance, ref Rectangle __result)
    {
        if (drawState.Type != DrawObjectType.Terrain)
            return;

        drawState = default;
        int offset = __instance.growthStage.Value switch
        {
            0 or 1 => -46,
            2 => -34,
            _ => -30
        };
        __result.Y += offset;
    }

    private static void FruitTreeGetBoundingBoxPostfix(FruitTree __instance, ref Rectangle __result)
    {
        if (drawState.Type != DrawObjectType.Terrain)
            return;

        drawState = default;
        int offset = __instance.growthStage.Value switch
        {
            0 or 1 => -46,
            2 => -34,
            _ => -30
        };
        __result.Y += offset;
    }

    private static void ObjectDrawPrefix(Object __instance)
    {
        PrepareShakeState();
        drawState = default;
        if (!currentConfig.EnablePassableCrops
            || !currentConfig.UseCustomDrawing
            || !TryGetPassableObject(__instance, out PassableObjectType objectType))
        {
            return;
        }

        drawState = new DrawState(GetDrawObjectType(objectType), GetShakeRotation(__instance));
    }

    private static void ObjectDrawPostfix()
    {
        drawState = default;
    }

    // Keep the transform on the object/terrain draw call sites; SpriteBatch.Draw is a global hot path.
    private static IEnumerable<CodeInstruction> LocalDrawTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.operand is MethodInfo method
                && method.DeclaringType == typeof(SpriteBatch)
                && method.Name == nameof(SpriteBatch.Draw))
            {
                MethodInfo? replacement = GetLocalDrawMethod(method);
                if (replacement is not null)
                    instruction.operand = replacement;
            }

            yield return instruction;
        }
    }

    private static MethodInfo? GetLocalDrawMethod(MethodInfo method)
    {
        Type[] parameterTypes = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        if (parameterTypes.Length == 8 && parameterTypes[1] == typeof(Rectangle))
        {
            return AccessTools.Method(
                typeof(PassableCropsFeature),
                nameof(DrawRectangleLocally),
                new[]
                {
                    typeof(SpriteBatch), typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?),
                    typeof(Color), typeof(float), typeof(Vector2), typeof(SpriteEffects), typeof(float)
                });
        }

        if (parameterTypes.Length == 9 && parameterTypes[1] == typeof(Vector2))
        {
            return parameterTypes[6] == typeof(Vector2)
                ? AccessTools.Method(
                    typeof(PassableCropsFeature),
                    nameof(DrawVectorLocally),
                    new[]
                    {
                        typeof(SpriteBatch), typeof(Texture2D), typeof(Vector2), typeof(Rectangle?),
                        typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float)
                    })
                : AccessTools.Method(
                    typeof(PassableCropsFeature),
                    nameof(DrawVectorScalarLocally),
                    new[]
                    {
                        typeof(SpriteBatch), typeof(Texture2D), typeof(Vector2), typeof(Rectangle?),
                        typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float)
                    });
        }

        return null;
    }

    private static void DrawRectangleLocally(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Rectangle destinationRectangle,
        Rectangle? sourceRectangle,
        Color color,
        float rotation,
        Vector2 origin,
        SpriteEffects effects,
        float layerDepth)
    {
        ApplyRectangleDrawState(ref destinationRectangle, ref rotation, ref origin);
        spriteBatch.Draw(texture, destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth);
    }

    private static void DrawVectorLocally(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Vector2 position,
        Rectangle? sourceRectangle,
        Color color,
        float rotation,
        Vector2 origin,
        Vector2 scale,
        SpriteEffects effects,
        float layerDepth)
    {
        ApplyVectorDrawState(ref position, ref rotation, ref origin, ref layerDepth);
        spriteBatch.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void DrawVectorScalarLocally(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Vector2 position,
        Rectangle? sourceRectangle,
        Color color,
        float rotation,
        Vector2 origin,
        float scale,
        SpriteEffects effects,
        float layerDepth)
    {
        ApplyVectorDrawState(ref position, ref rotation, ref origin, ref layerDepth);
        spriteBatch.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void ApplyRectangleDrawState(ref Rectangle destinationRectangle, ref float rotation, ref Vector2 origin)
    {
        if (drawState.Type is DrawObjectType.None or DrawObjectType.Terrain)
            return;

        if (currentConfig.ShakeWhenPassing)
            rotation = drawState.Rotation;

        if (drawState.Type == DrawObjectType.Scarecrow)
        {
            destinationRectangle.X += 32;
            destinationRectangle.Y += 120;
            origin += new Vector2(8f, 30f);
        }
    }

    private static void ApplyVectorDrawState(ref Vector2 position, ref float rotation, ref Vector2 origin, ref float layerDepth)
    {
        if (drawState.Type is DrawObjectType.None or DrawObjectType.Terrain)
            return;

        if (currentConfig.ShakeWhenPassing)
            rotation = drawState.Rotation;

        switch (drawState.Type)
        {
            case DrawObjectType.Weed:
                layerDepth += 24f / 10000f;
                position += new Vector2(0f, 32f);
                origin += new Vector2(0f, 8f);
                break;
            case DrawObjectType.Sprinkler:
                layerDepth += 45f / 10000f;
                break;
            case DrawObjectType.Forage:
                layerDepth += 32f / 10000f;
                position += new Vector2(0f, 32f);
                origin += new Vector2(0f, 8f);
                break;
        }
    }

    private static bool CanPass(Character? character)
    {
        return character is Farmer || (character is not null && currentConfig.PassableByAll);
    }

    private static void SlowDown(Character character)
    {
        if (character is Farmer farmer && currentConfig.SlowDownWhenPassing)
            farmer.temporarySpeedBuff = farmer.stats.Get("Book_Grass") == 0 ? -1f : -0.33f;
    }

    private static void ShakeTerrain(TerrainFeature feature, Character character)
    {
        if (!currentConfig.ShakeWhenPassing || character is null)
            return;

        switch (feature)
        {
            case Bush bush:
                bush.shake(character.Tile, true);
                PlayRustleSound(bush.Tile, bush.Location);
                break;
            case Tree tree:
                tree.shake(tree.Tile, true);
                PlayRustleSound(tree.Tile, tree.Location);
                break;
            case FruitTree fruitTree:
                fruitTree.shake(fruitTree.Tile, true);
                PlayRustleSound(fruitTree.Tile, fruitTree.Location);
                break;
        }
    }

    private static bool IsPassableBush(Bush bush)
    {
        return currentConfig.PassableTeaBushes && bush.size.Value == Bush.largeBush && !bush.inPot.Value;
    }

    private static bool IsPassableTree(Tree tree)
    {
        return currentConfig.PassableTreeGrowth >= GetTreeGrowth(tree);
    }

    private static bool IsPassableFruitTree(FruitTree tree)
    {
        return currentConfig.PassableFruitTreeGrowth >= Math.Min(tree.growthStage.Value, 5);
    }

    private static int GetTreeGrowth(Tree tree)
    {
        int growth = tree.growthStage.Value;
        if (tree.treeType.Value == "6" && growth >= 3)
            return 5;
        return Math.Min(growth, 5);
    }

    private static void ShakeObject(Object item, PassableObjectType type)
    {
        PrepareShakeState();
        if (!shakeStateEnabled || item.Location != Game1.currentLocation)
            return;

        if (shakeStates.ContainsKey(item))
            return;

        float maxShake = type is PassableObjectType.Scarecrow or PassableObjectType.Sprinkler
            ? MathF.PI / 16f
            : MathF.PI / 12f;
        shakeStates[item] = new ShakeState(maxShake, 0f, true);
        PlayRustleSound(item.TileLocation, item.Location);
    }

    private static float GetShakeRotation(Object item)
    {
        PrepareShakeState();
        if (!shakeStateEnabled || !shakeStates.TryGetValue(item, out ShakeState? state))
            return 0f;

        if (state.MaxShake > 0f)
        {
            state.Rotation += state.Left ? -MathF.PI / 100f : MathF.PI / 100f;
            if (MathF.Abs(state.Rotation) >= state.MaxShake)
                state.Left = false;
            state.MaxShake = Math.Max(0f, state.MaxShake - MathF.PI / 300f);
        }
        else
        {
            state.Rotation /= 2f;
            if (state.Rotation <= 0.01f)
            {
                shakeStates.Remove(item);
                return 0f;
            }
        }

        return state.Rotation;
    }

    private static void PrepareShakeState()
    {
        bool enabled = Context.IsWorldReady
            && currentConfig.EnablePassableCrops
            && currentConfig.ShakeWhenPassing
            && Game1.currentLocation is not null;
        GameLocation? location = enabled ? Game1.currentLocation : null;
        if (enabled == shakeStateEnabled && ReferenceEquals(location, shakeLocation))
            return;

        shakeStates.Clear();
        shakeLocation = location;
        shakeStateEnabled = enabled;
    }

    private static bool TryGetPassableObject(Object item, out PassableObjectType type)
    {
        type = PassableObjectType.None;
        ModConfig config = currentConfig;
        if (!config.EnablePassableCrops || item is null || IsNamed(item, config.ExcludeObjects))
            return false;

        if (config.PassableSprinklers && item.IsSprinkler())
            type = PassableObjectType.Sprinkler;
        else if (config.PassableScarecrows && item.IsScarecrow())
            type = PassableObjectType.Scarecrow;
        else if (config.PassableForage && item.isForage() && item.Category != -9 && item.ParentSheetIndex != 590)
            type = PassableObjectType.Forage;
        else if (config.PassableWeeds && IsWeed(item) && item.ParentSheetIndex is not 319 and not 320 and not 321)
            type = PassableObjectType.Weed;
        else if (IsNamed(item, config.IncludeObjects))
            type = PassableObjectType.Custom;

        return type != PassableObjectType.None;
    }

    private static bool IsWeed(Object item)
    {
        return item.IsWeeds()
            || item.HasContextTag("item_weeds")
            || item.HasContextTag("item_greenrainweeds");
    }

    private static bool IsNamed(Object item, IEnumerable<string>? names)
    {
        if (names is null)
            return false;

        foreach (string name in names)
        {
            if (string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, item.QualifiedItemId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static DrawObjectType GetDrawObjectType(PassableObjectType type)
    {
        return type switch
        {
            PassableObjectType.Scarecrow => DrawObjectType.Scarecrow,
            PassableObjectType.Sprinkler => DrawObjectType.Sprinkler,
            PassableObjectType.Forage => DrawObjectType.Forage,
            PassableObjectType.Weed => DrawObjectType.Weed,
            _ => DrawObjectType.Custom
        };
    }

    private static void PlayRustleSound(Vector2 tile, GameLocation? location)
    {
        if (!currentConfig.PlaySoundWhenPassing
            || location is null
            || location != Game1.currentLocation
            || !Utility.isOnScreen(new Point((int)tile.X, (int)tile.Y), 2, location))
        {
            return;
        }

        Grass.PlayGrassSound();
    }

    private enum PassableObjectType
    {
        None,
        Scarecrow,
        Sprinkler,
        Forage,
        Weed,
        Custom
    }

    private enum DrawObjectType
    {
        None,
        Terrain,
        Scarecrow,
        Sprinkler,
        Forage,
        Weed,
        Custom
    }

    private struct DrawState
    {
        internal DrawState(DrawObjectType type, float rotation)
        {
            Type = type;
            Rotation = rotation;
        }

        internal DrawObjectType Type;
        internal float Rotation;
    }

    private sealed class ShakeState
    {
        internal ShakeState(float maxShake, float rotation, bool left)
        {
            MaxShake = maxShake;
            Rotation = rotation;
            Left = left;
        }

        internal float MaxShake;
        internal float Rotation;
        internal bool Left;
    }
}
