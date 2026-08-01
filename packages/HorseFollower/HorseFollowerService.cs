using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Pathfinding;

namespace HorseFollower;

internal sealed class HorseFollowerService
{
    private readonly ModConfig config;
    private Horse? trackedHorse;
    private PathFindController? followController;
    private bool wasMounted;
    private bool followSessionActive;

    public HorseFollowerService(ModConfig config)
    {
        if (config.CheckInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.CheckInterval), "CheckInterval must be greater than zero.");
        if (config.FollowDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(config.FollowDistance), "FollowDistance must not be negative.");
        if (config.StableRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(config.StableRadius), "StableRadius must not be negative.");

        this.config = config;
    }

    internal void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        ClearTracking();
    }

    internal void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        if (followSessionActive)
            StopFollowController();
    }

    internal void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
        {
            ClearTracking();
            return;
        }

        Horse? mountedHorse = Game1.player.mount;
        if (mountedHorse is not null)
        {
            StopFollowController();
            followSessionActive = false;
            trackedHorse = mountedHorse;
            wasMounted = true;
            return;
        }

        if (wasMounted)
        {
            wasMounted = false;
            if (trackedHorse is null || !CanStartFollow(trackedHorse))
            {
                ClearTracking();
                return;
            }

            followSessionActive = true;
        }

        if (!followSessionActive || trackedHorse is null || !e.IsMultipleOf((uint)config.CheckInterval))
            return;

        UpdateFollow(trackedHorse);
    }

    private bool CanStartFollow(Horse horse)
    {
        if (horse.currentLocation is null || horse.currentLocation != Game1.currentLocation)
            return false;

        return !IsNearHorseStable(horse);
    }

    private void UpdateFollow(Horse horse)
    {
        if (horse.currentLocation is null || horse.currentLocation != Game1.currentLocation)
        {
            StopFollowController();
            return;
        }

        Vector2 distance = horse.Tile - Game1.player.Tile;
        float followDistance = config.FollowDistance;
        if (distance.LengthSquared() <= followDistance * followDistance)
        {
            StopFollowController();
            return;
        }

        if (followController is not null && ReferenceEquals(horse.controller, followController))
            return;

        StopFollowController();
        followController = new PathFindController(
            horse,
            Game1.currentLocation,
            IsWithinFollowDistance,
            -1,
            null,
            10000,
            Game1.player.TilePoint);
        horse.controller = followController;
    }

    private bool IsWithinFollowDistance(PathNode currentNode, Point endPoint, GameLocation location, Character character)
    {
        Vector2 playerTile = Game1.player.Tile;
        Vector2 horseTile = new(currentNode.x, currentNode.y);
        float followDistance = config.FollowDistance;
        return Vector2.DistanceSquared(horseTile, playerTile) <= followDistance * followDistance;
    }

    private bool IsNearHorseStable(Horse horse)
    {
        Stable? stable = horse.TryFindStable();
        if (stable is null)
            return false;

        GameLocation currentLocation = Game1.currentLocation;
        if (stable.GetIndoors() == currentLocation)
            return true;
        if (stable.GetParentLocation() != currentLocation)
            return false;

        Rectangle stableArea = stable.GetBoundingBox();
        int radius = config.StableRadius * 64;
        stableArea.Inflate(radius, radius);
        return stableArea.Intersects(Game1.player.GetBoundingBox());
    }

    private void StopFollowController()
    {
        if (trackedHorse is not null && followController is not null && ReferenceEquals(trackedHorse.controller, followController))
        {
            trackedHorse.controller = null;
            trackedHorse.Halt();
        }

        followController = null;
    }

    private void ClearTracking()
    {
        StopFollowController();
        trackedHorse = null;
        followSessionActive = false;
        wasMounted = false;
    }
}
