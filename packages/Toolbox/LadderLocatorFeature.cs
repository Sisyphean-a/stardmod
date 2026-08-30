using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace Toolbox;

/// <summary>
/// Shows a fixed, delayed visual hint for likely ladder rocks in the mine.
/// The hint is intentionally not configurable: it appears only after ten stones
/// on the current floor have been broken without spawning a ladder.
/// </summary>
internal sealed class LadderLocatorFeature
{
    private const int RevealAfterBrokenStones = 10;
    private const int GuaranteedDescentAfterBrokenStones = RevealAfterBrokenStones + 1;
    private static readonly Color[] RainbowPalette =
    {
        Color.Red,
        Color.Yellow,
        Color.Lime,
        Color.Cyan,
        Color.Blue,
        Color.Magenta
    };

    private readonly IModHelper helper;
    private readonly Dictionary<Vector2, LadderMarker> ladderMarkers = new();
    private readonly HashSet<Vector2> brokenStoneTiles = new();
    private readonly Texture2D pixelTexture;
    private GameLocation? trackedLocation;
    private int brokenStoneCount;
    private int? initialStonesLeft;
    private int objectListVersion;
    private Vector2? pendingGuaranteedDescentTile;
    private int pendingGuaranteeEarliestTick = -1;
    private LadderSearchState? lastSearchState;
    private bool descentHasSpawned;
    private bool revealActive;

    internal LadderLocatorFeature(IModHelper helper)
    {
        this.helper = helper;
        pixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
        pixelTexture.SetData(new[] { Color.White });
    }

    internal void RegisterEvents()
    {
        helper.Events.World.ObjectListChanged += OnObjectListChanged;
        helper.Events.Player.Warped += OnWarped;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.Display.RenderedWorld += OnRenderedWorld;
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer)
            return;

        Reset(e.NewLocation is MineShaft ? e.NewLocation : null);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        Reset(null);
    }

    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (!e.IsCurrentLocation || e.Location is not MineShaft mine)
            return;

        // 规则：SMAPI 已提供当前地点语义判断；其他模组替换地点实例时仍保留本层计数。
        if (trackedLocation is not MineShaft trackedMine || trackedMine.mineLevel != mine.mineLevel)
            Reset(mine);
        else
            trackedLocation = mine;

        objectListVersion++;
        Vector2? guaranteedDescentTile = null;
        int removedStones = 0;
        foreach (KeyValuePair<Vector2, StardewValley.Object> removed in e.Removed)
        {
            if (!IsStone(removed.Value))
                continue;

            removedStones++;
            brokenStoneTiles.Add(removed.Key);
            brokenStoneCount++;
            if (brokenStoneCount == GuaranteedDescentAfterBrokenStones)
                guaranteedDescentTile = removed.Key;
        }

        if (removedStones <= 0)
            return;

        if (HasDescent(mine))
        {
            ClearPendingGuarantee();
            ClearMarkers();
            return;
        }

        if (guaranteedDescentTile is Vector2 tile)
        {
            // Flow：等原版的下降点网络事件落地一帧，再决定是否补生成，避免把同格的洞覆盖成楼梯。
            pendingGuaranteedDescentTile = tile;
            pendingGuaranteeEarliestTick = Game1.ticks + 1;
            ClearMarkers();
            return;
        }

        if (brokenStoneCount < RevealAfterBrokenStones)
        {
            ClearMarkers();
            return;
        }

        RefreshMarkers(mine);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Game1.currentLocation is not MineShaft mine
            || (!e.IsMultipleOf(5) && pendingGuaranteedDescentTile is null))
        {
            return;
        }

        if (!IsTrackedMine(mine))
        {
            Reset(mine);
            return;
        }

        trackedLocation = mine;
        UpdateBrokenStoneCountFromMine(mine);
        if (HasDescent(mine))
        {
            ClearPendingGuarantee();
            ClearMarkers();
            return;
        }

        if (pendingGuaranteedDescentTile is Vector2 tile)
        {
            if (Game1.ticks < pendingGuaranteeEarliestTick)
                return;

            if (!mine.shouldCreateLadderOnThisLevel())
            {
                ClearPendingGuarantee();
                ClearMarkers();
                return;
            }

            if (!Game1.IsMasterGame)
                return;

            ClearPendingGuarantee();
            mine.createLadderDown((int)tile.X, (int)tile.Y);
            descentHasSpawned = true;
            ClearMarkers();
            return;
        }

        if (brokenStoneCount >= RevealAfterBrokenStones
            && (lastSearchState is null || !lastSearchState.Equals(GetSearchState(mine))))
        {
            RefreshMarkers(mine);
        }
    }

    private void RefreshMarkers(MineShaft mine)
    {
        LadderSearchState searchState = GetSearchState(mine);
        if (lastSearchState is not null && lastSearchState.Equals(searchState))
            return;

        lastSearchState = searchState;
        ladderMarkers.Clear();
        revealActive = false;

        if (HasDescent(mine)
            || searchState.MustKillAllMonsters
            || !searchState.ShouldCreateLadder)
        {
            return;
        }

        int stonesLeft = mine.stonesLeftOnThisLevel;
        int stonesLeftAfterBreak = Math.Max(0, stonesLeft - 1);
        double chance = 0.02
            + 1.0 / Math.Max(1, stonesLeftAfterBreak)
            + Game1.player.LuckLevel / 100.0
            + Game1.player.DailyLuck / 5.0;
        if (mine.EnemyCount == 0)
            chance += 0.04;
        if (Game1.player.hasBuff("dwarfStatue_1"))
            chance *= 1.25;

        foreach ((Vector2 tile, StardewValley.Object obj) in mine.Objects.Pairs)
        {
            if (!IsStone(obj))
                continue;

            Random random = Utility.CreateDaySaveRandom(
                tile.X * 1000f,
                tile.Y,
                mine.mineLevel);
            random.NextDouble();
            double roll = random.NextDouble();
            if (stonesLeft != 0 && roll >= chance)
                continue;

            ladderMarkers[tile] = new LadderMarker(obj);
        }

        revealActive = ladderMarkers.Count > 0;
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady
            || !revealActive
            || ladderMarkers.Count == 0
            || !IsCurrentTrackedMine())
        {
            return;
        }

        double seconds = Game1.currentGameTime.TotalGameTime.TotalSeconds;
        Rectangle viewport = new(0, 0, Game1.viewport.Width, Game1.viewport.Height);
        Game1.InUIMode(() =>
        {
            foreach ((Vector2 tile, LadderMarker marker) in ladderMarkers)
            {
                Rectangle bounds = marker.Bounds;
                bounds.Offset(-Game1.viewport.X, -Game1.viewport.Y);
                if (!bounds.Intersects(viewport))
                    continue;

                float phase = (float)((seconds * 0.28 + tile.X * 0.07 + tile.Y * 0.11) % 1.0);
                Color color = GetRainbowColor(phase);
                float pulse = 0.5f + 0.5f * MathF.Sin((float)seconds * 5f + tile.X + tile.Y);
                DrawMarker(bounds, color, pulse);
            }
        });
    }

    private void DrawMarker(Rectangle bounds, Color color, float pulse)
    {
        int glowSize = 7 + (int)(pulse * 6f);
        Rectangle glow = bounds;
        glow.Inflate(glowSize, glowSize);
        DrawFilledRectangle(
            glow,
            new Color(color.R, color.G, color.B, (byte)(42 + pulse * 34f)));
        DrawBorder(
            glow,
            new Color(color.R, color.G, color.B, (byte)(170 + pulse * 60f)),
            3);
        DrawBorder(bounds, color, 5);

        int centerX = bounds.Center.X;
        int arrowTop = Math.Max(8, glow.Top - 44);
        Color arrowColor = GetRainbowColor((color.R + color.G + color.B) / (255f * 3f) + 0.18f);
        DrawFilledRectangle(new Rectangle(centerX - 4, arrowTop, 8, 30), arrowColor);
        DrawFilledRectangle(new Rectangle(centerX - 17, arrowTop + 24, 34, 7), arrowColor);
        DrawFilledRectangle(new Rectangle(centerX - 17, arrowTop + 17, 7, 14), arrowColor);
        DrawFilledRectangle(new Rectangle(centerX + 10, arrowTop + 17, 7, 14), arrowColor);

        int sparkleSize = 4 + (int)(pulse * 3f);
        DrawFilledRectangle(
            new Rectangle(glow.Left - sparkleSize, glow.Top + 8, sparkleSize, sparkleSize),
            GetRainbowColor(0.08f + pulse * 0.2f));
        DrawFilledRectangle(
            new Rectangle(glow.Right, glow.Bottom - 12, sparkleSize, sparkleSize),
            GetRainbowColor(0.55f + pulse * 0.2f));
    }

    private void DrawFilledRectangle(Rectangle rectangle, Color color)
    {
        Game1.spriteBatch.Draw(pixelTexture, rectangle, color);
    }

    private void DrawBorder(Rectangle rectangle, Color color, int thickness)
    {
        DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, thickness), color);
        DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Bottom - thickness, rectangle.Width, thickness), color);
        DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Top + thickness, thickness, rectangle.Height - thickness * 2), color);
        DrawFilledRectangle(new Rectangle(rectangle.Right - thickness, rectangle.Top + thickness, thickness, rectangle.Height - thickness * 2), color);
    }

    private static Color GetRainbowColor(float hue)
    {
        hue -= MathF.Floor(hue);
        float scaledHue = hue * RainbowPalette.Length;
        int index = (int)scaledHue;
        float amount = scaledHue - index;
        return Color.Lerp(RainbowPalette[index], RainbowPalette[(index + 1) % RainbowPalette.Length], amount);
    }

    private LadderSearchState GetSearchState(MineShaft mine)
    {
        return new LadderSearchState(
            objectListVersion,
            mine.mineLevel,
            mine.stonesLeftOnThisLevel,
            mine.EnemyCount,
            mine.ladderHasSpawned,
            mine.mustKillAllMonstersToAdvance(),
            mine.shouldCreateLadderOnThisLevel());
    }

    // 规则：加入者可能收不到主机本地的 ObjectListChanged，使用原版同步的剩余石头数补齐本地计数。
    private void UpdateBrokenStoneCountFromMine(MineShaft mine)
    {
        if (initialStonesLeft is not int initial
            || mine.stonesLeftOnThisLevel < 0
            || mine.stonesLeftOnThisLevel > initial)
        {
            return;
        }

        int syncedBrokenStoneCount = initial - mine.stonesLeftOnThisLevel;
        if (syncedBrokenStoneCount > brokenStoneCount)
        {
            brokenStoneCount = syncedBrokenStoneCount;
            objectListVersion++;
        }
    }

    private bool IsTrackedMine(MineShaft mine)
    {
        return trackedLocation is MineShaft trackedMine && trackedMine.mineLevel == mine.mineLevel;
    }

    private bool IsCurrentTrackedMine()
    {
        return Game1.currentLocation is MineShaft mine && IsTrackedMine(mine);
    }

    private void Reset(GameLocation? location)
    {
        trackedLocation = location;
        initialStonesLeft = location is MineShaft mine ? mine.stonesLeftOnThisLevel : null;
        brokenStoneCount = 0;
        objectListVersion = 0;
        brokenStoneTiles.Clear();
        ClearPendingGuarantee();
        lastSearchState = null;
        descentHasSpawned = location is MineShaft currentMine && ContainsDescentTile(currentMine);
        ClearMarkers();
    }

    private bool HasDescent(MineShaft mine)
    {
        if (descentHasSpawned || mine.ladderHasSpawned)
        {
            descentHasSpawned = true;
            return true;
        }

        foreach (Vector2 tile in brokenStoneTiles)
        {
            if (IsDescentTileIndex(mine.getTileIndexAt((int)tile.X, (int)tile.Y, "Buildings", "mine")))
            {
                descentHasSpawned = true;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDescentTile(MineShaft mine)
    {
        int width = mine.Map.Layers[0].LayerWidth;
        int height = mine.Map.Layers[0].LayerHeight;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (IsDescentTileIndex(mine.getTileIndexAt(x, y, "Buildings", "mine")))
                    return true;
            }
        }

        return false;
    }

    private static bool IsDescentTileIndex(int tileIndex) => tileIndex is 173 or 174;

    private void ClearPendingGuarantee()
    {
        pendingGuaranteedDescentTile = null;
        pendingGuaranteeEarliestTick = -1;
    }

    private void ClearMarkers()
    {
        ladderMarkers.Clear();
        revealActive = false;
    }

    private static bool IsStone(KeyValuePair<Vector2, StardewValley.Object> pair)
    {
        return IsStone(pair.Value);
    }

    private static bool IsStone(StardewValley.Object obj)
    {
        return string.Equals(obj.Name, "Stone", StringComparison.Ordinal);
    }

    private sealed record LadderSearchState(
        int ObjectListVersion,
        int MineLevel,
        int StonesLeft,
        int EnemyCount,
        bool LadderHasSpawned,
        bool MustKillAllMonsters,
        bool ShouldCreateLadder);

    private sealed class LadderMarker
    {
        internal LadderMarker(StardewValley.Object obj)
        {
            Bounds = obj.GetBoundingBox();
        }

        internal Rectangle Bounds { get; }
    }
}
