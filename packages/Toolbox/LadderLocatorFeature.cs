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
    private readonly IMonitor monitor;
    private readonly Dictionary<Vector2, LadderMarker> ladderMarkers = new();
    private readonly Texture2D pixelTexture;
    private GameLocation? trackedLocation;
    private int brokenStoneCount;
    private bool revealActive;

    internal LadderLocatorFeature(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
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
        if (!e.IsCurrentLocation
            || e.Location is not MineShaft mine
            || !ReferenceEquals(trackedLocation, e.Location))
        {
            return;
        }

        if (mine.ladderHasSpawned)
        {
            ClearMarkers();
            return;
        }

        int removedStones = e.Removed.Count(IsStone);
        if (removedStones <= 0)
            return;

        brokenStoneCount += removedStones;
        if (brokenStoneCount < RevealAfterBrokenStones)
        {
            ClearMarkers();
            return;
        }

        RefreshMarkers(mine);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!e.IsMultipleOf(5)
            || trackedLocation is not MineShaft mine
            || !ReferenceEquals(Game1.currentLocation, trackedLocation))
        {
            return;
        }

        if (mine.ladderHasSpawned)
        {
            ClearMarkers();
        }
        else if (brokenStoneCount >= RevealAfterBrokenStones && !revealActive)
        {
            RefreshMarkers(mine);
        }
    }

    private void RefreshMarkers(MineShaft mine)
    {
        bool wasVisible = revealActive;
        ladderMarkers.Clear();
        revealActive = false;

        if (mine.ladderHasSpawned
            || mine.mustKillAllMonstersToAdvance()
            || !mine.shouldCreateLadderOnThisLevel())
        {
            return;
        }

        int stonesLeft = mine.stonesLeftOnThisLevel;
        double chance = 0.02
            + 1.0 / Math.Max(1, stonesLeft)
            + Game1.player.LuckLevel / 100.0
            + Game1.player.DailyLuck / 5.0;
        if (mine.EnemyCount == 0)
            chance += 0.04;

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
        if (revealActive && !wasVisible)
        {
            monitor.Log(
                $"矿井已连续破坏 {brokenStoneCount} 块石头仍未出现梯子，已显示下一层入口提示。",
                LogLevel.Trace);
        }
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady
            || !revealActive
            || ladderMarkers.Count == 0
            || trackedLocation is not MineShaft
            || !ReferenceEquals(Game1.currentLocation, trackedLocation))
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

    private void Reset(GameLocation? location)
    {
        trackedLocation = location;
        brokenStoneCount = 0;
        ClearMarkers();
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

    private sealed class LadderMarker
    {
        internal LadderMarker(StardewValley.Object obj)
        {
            Bounds = obj.GetBoundingBox();
        }

        internal Rectangle Bounds { get; }
    }
}
