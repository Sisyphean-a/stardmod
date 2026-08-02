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
    private const float StartDistancePaddingPixels = 32f;

    private readonly ModConfig config;

    private int ReplanIntervalTicks => config.CheckInterval * 3;

    private int RetryIntervalTicks => config.CheckInterval * 3;

    private Horse? trackedHorse;
    private HorseFollowController? followController;
    private bool wasMounted;
    private bool followSessionActive;
    private int ticksSincePlan;
    private Point plannedTargetTile;
    private bool hasPlannedTarget;
    private int lastAnimationDirection = -1;
    private Horse? speedAdjustedHorse;
    private int originalHorseSpeed;
    private float originalHorseAddedSpeed;
    private bool hasOriginalHorseSpeed;

    internal HorseFollowerService(ModConfig config)
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
        if (mountedHorse is null && trackedHorse?.rider == Game1.player)
            mountedHorse = trackedHorse;

        if (mountedHorse is not null)
        {
            StopFollowController();
            RestoreHorseSpeed();
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

            BeginFollowSession(trackedHorse);
        }

        if (!followSessionActive || trackedHorse is null)
            return;

        UpdateFollow(trackedHorse);
    }

    private bool CanStartFollow(Horse horse)
    {
        return horse.currentLocation == Game1.currentLocation && !IsNearHorseStable(horse);
    }

    // Flow: start outside the stop radius, keep one live route while it is useful, then replan on target change or blockage.
    private void UpdateFollow(Horse horse)
    {
        if (horse.currentLocation != Game1.currentLocation)
        {
            StopFollowController();
            return;
        }

        float distanceSquared = GetDistanceSquared(horse, Game1.player);
        float stopDistance = config.FollowDistance * 64f;
        float startDistance = stopDistance + StartDistancePaddingPixels;

        if (distanceSquared <= stopDistance * stopDistance)
        {
            StopFollowController();
            return;
        }

        if (followController is null && distanceSquared <= startDistance * startDistance)
            return;

        ApplyFollowSpeed(horse, MathF.Sqrt(distanceSquared), stopDistance);
        ticksSincePlan++;
        Point targetTile = GetFollowTargetTile();
        bool controllerAttached = followController is not null && ReferenceEquals(horse.controller, followController);
        if (horse.controller is not null && !controllerAttached)
            return;

        bool targetChanged = !hasPlannedTarget || HasTargetMovedEnough(plannedTargetTile, targetTile);
        bool shouldReplan;
        if (controllerAttached)
        {
            shouldReplan = followController!.IsStuck
                || (targetChanged && followController.MadeProgress && ticksSincePlan >= ReplanIntervalTicks);
        }
        else if (followController is null)
        {
            shouldReplan = ticksSincePlan >= RetryIntervalTicks;
        }
        else if (!followController.HasPath)
        {
            shouldReplan = true;
        }
        else if (followController.IsStuck)
        {
            shouldReplan = ticksSincePlan >= RetryIntervalTicks;
        }
        else
        {
            shouldReplan = true;
        }

        if (shouldReplan)
            TryPlanPath(horse, targetTile, stopDistance);

        if (followController is not null && ReferenceEquals(horse.controller, followController))
            horse.Sprite.loop = true;
    }

    private void TryPlanPath(Horse horse, Point targetTile, float stopDistance)
    {
        ticksSincePlan = 0;
        plannedTargetTile = targetTile;
        hasPlannedTarget = true;

        if (followController is not null && ReferenceEquals(horse.controller, followController))
            horse.controller = null;

        HorseFollowController nextController = new(
            horse,
            Game1.currentLocation,
            targetTile,
            Game1.player.getStandingPosition(),
            stopDistance,
            UpdateFollowAnimation);
        if (!nextController.HasPath)
        {
            followController = null;
            horse.stopWithoutChangingFrame();
            SetHorseIdle(horse);
            return;
        }

        followController = nextController;
        horse.Sprite.CurrentAnimation = null;
        horse.controller = nextController;
    }

    private Point GetFollowTargetTile()
    {
        return Game1.player.TilePoint;
    }

    private static bool HasTargetMovedEnough(Point previousTarget, Point currentTarget)
    {
        return Math.Abs(previousTarget.X - currentTarget.X)
            + Math.Abs(previousTarget.Y - currentTarget.Y) >= 2;
    }

    private void BeginFollowSession(Horse horse)
    {
        speedAdjustedHorse = horse;
        originalHorseSpeed = horse.speed;
        originalHorseAddedSpeed = horse.addedSpeed;
        hasOriginalHorseSpeed = true;
        followSessionActive = true;
        ticksSincePlan = RetryIntervalTicks;
        hasPlannedTarget = false;
    }

    private void ApplyFollowSpeed(Horse horse, float distancePixels, float stopDistancePixels)
    {
        if (!hasOriginalHorseSpeed || !ReferenceEquals(speedAdjustedHorse, horse))
            return;

        float excessDistanceTiles = Math.Max(0f, distancePixels - stopDistancePixels) / 64f;
        float catchUpSpeed = MathHelper.Clamp(excessDistanceTiles * 0.5f, 0.5f, 2f);
        float followSpeed = Math.Max(2f, Game1.player.speed + Game1.player.addedSpeed + catchUpSpeed);
        horse.speed = (int)MathF.Floor(followSpeed);
        horse.addedSpeed = followSpeed - horse.speed;
    }

    private void UpdateFollowAnimation(Horse horse, GameTime time, int direction, float distanceMoved)
    {
        int startFrame = direction switch
        {
            0 => 15,
            1 or 3 => 8,
            2 => 1,
            _ => 1
        };
        bool interruptedAnimation = horse.Sprite.CurrentAnimation is not null;
        if (lastAnimationDirection != direction || interruptedAnimation)
        {
            horse.Sprite.CurrentAnimation = null;
            horse.Sprite.CurrentFrame = startFrame;
            horse.Sprite.timer = 0f;
            lastAnimationDirection = direction;
        }

        horse.FacingDirection = direction;
        horse.flip = direction == 3;
        horse.drawOffset = direction == 3 ? Vector2.Zero : new Vector2(-16f, 0f);
        horse.Sprite.loop = true;
        float animationInterval = MathHelper.Clamp(160f - distanceMoved * 12.5f, 70f, 140f);
        horse.Sprite.Animate(time, startFrame, 6, animationInterval);
    }

    private static void SetHorseIdle(Horse horse)
    {
        int idleFrame = horse.FacingDirection switch
        {
            0 => 14,
            1 or 3 => 7,
            _ => 0
        };
        horse.Sprite.CurrentAnimation = null;
        horse.Sprite.CurrentFrame = idleFrame;
        horse.Sprite.timer = 0f;
        horse.flip = horse.FacingDirection == 3;
    }

    private static float GetDistanceSquared(Character first, Character second)
    {
        Vector2 offset = first.getStandingPosition() - second.getStandingPosition();
        return offset.LengthSquared();
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
        if (trackedHorse is not null)
        {
            if (followController is not null && ReferenceEquals(trackedHorse.controller, followController))
                trackedHorse.controller = null;
            trackedHorse.stopWithoutChangingFrame();
        }

        if (trackedHorse is { rider: null } horse)
            SetHorseIdle(horse);

        followController = null;
        ticksSincePlan = RetryIntervalTicks;
        hasPlannedTarget = false;
        lastAnimationDirection = -1;
    }

    private void RestoreHorseSpeed()
    {
        if (hasOriginalHorseSpeed && speedAdjustedHorse is not null)
        {
            speedAdjustedHorse.speed = originalHorseSpeed;
            speedAdjustedHorse.addedSpeed = originalHorseAddedSpeed;
        }

        speedAdjustedHorse = null;
        hasOriginalHorseSpeed = false;
    }

    private void ClearTracking()
    {
        StopFollowController();
        RestoreHorseSpeed();
        trackedHorse = null;
        followSessionActive = false;
        wasMounted = false;
    }
}
