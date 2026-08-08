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
    private const float MinimumOutdoorExitArrivalDistancePixels = 80f;
    private const int PathSearchNodesPerUpdate = 16;
    private const int HorseWalkAnimationFrameDurationMilliseconds = 70;

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly OutdoorWarpTracker outdoorWarpTracker;

    private int ReplanIntervalTicks => config.CheckInterval * 3;

    private int RetryIntervalTicks => config.CheckInterval * 3;

    private Horse? trackedHorse;
    private HorseFollowController? followController;
    private HorsePathSearch? pathSearch;
    private PathRequest? pathRequest;
    private PathFailure? failedPath;
    private bool wasMounted;
    private bool followSessionActive;
    private int ticksSincePlan;
    private Point plannedTargetTile;
    private bool hasPlannedTarget;
    private int activeAnimationDirection = -1;
    private Horse? speedAdjustedHorse;
    private int originalHorseSpeed;
    private float originalHorseAddedSpeed;
    private bool hasOriginalHorseSpeed;

    internal HorseFollowerService(ModConfig config, IMonitor monitor)
    {
        if (config.CheckInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.CheckInterval), "CheckInterval must be greater than zero.");
        if (config.FollowDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(config.FollowDistance), "FollowDistance must not be negative.");
        if (config.FollowStartDistance <= config.FollowDistance)
            throw new ArgumentOutOfRangeException(nameof(config.FollowStartDistance), "FollowStartDistance must be greater than FollowDistance.");
        if (config.StableRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(config.StableRadius), "StableRadius must not be negative.");

        this.config = config;
        this.monitor = monitor;
        outdoorWarpTracker = new OutdoorWarpTracker(monitor);
    }

    internal void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        ClearTracking();
    }

    internal void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
    {
        if (!Context.IsWorldReady)
        {
            outdoorWarpTracker.ClearPending();
            return;
        }

        outdoorWarpTracker.CaptureCandidate(Game1.player, followSessionActive);
    }

    internal void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        bool routeChanged = outdoorWarpTracker.HandlePlayerWarp(
            e,
            trackedHorse,
            followSessionActive);
        if (routeChanged)
        {
            ClearPathFailure();
            StopFollowController();
        }
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
            outdoorWarpTracker.Clear();
            ClearPathFailure();
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

    // Flow: keep a useful route active, but perform expensive route construction incrementally and never repeat an unchanged failure.
    private void UpdateFollow(Horse horse)
    {
        if (!OutdoorWarpTracker.IsSameLocation(horse.currentLocation, Game1.currentLocation))
        {
            UpdateOutdoorTravel(horse);
            return;
        }

        if (outdoorWarpTracker.HasTransitions)
            outdoorWarpTracker.ClearTransitions();

        float distanceSquared = GetDistanceSquared(horse, Game1.player);
        float distance = MathF.Sqrt(distanceSquared);
        float stopDistance = config.FollowDistance * 64f;
        float startDistance = config.FollowStartDistance * 64f;
        LogFollow(
            $"update distance={distance:0.0} stop={stopDistance:0.0} start={startDistance:0.0} "
            + $"horseSpeed={horse.speed + horse.addedSpeed:0.00} "
            + $"controller={(horse.controller is null ? "none" : ReferenceEquals(horse.controller, followController) ? "owned" : "external")} "
            + $"path={(followController?.HasPath == true ? "active" : "empty")} search={(pathSearch is null ? "none" : "active")} "
            + $"horsePos=({horse.Position.X:0.0},{horse.Position.Y:0.0}) playerPos=({Game1.player.Position.X:0.0},{Game1.player.Position.Y:0.0})");
        if (distanceSquared <= stopDistance * stopDistance)
        {
            LogFollow($"service-stop reason=stopping-distance distance={distance:0.0}");
            StopFollowController();
            return;
        }

        Point targetTile = GetFollowTargetTile();
        bool controllerAttached = followController is not null && ReferenceEquals(horse.controller, followController);
        if (horse.controller is not null && !controllerAttached)
        {
            LogFollow("service-pause reason=external-controller");
            CancelPathSearch();
            return;
        }

        if (followController is null && distanceSquared <= startDistance * startDistance)
        {
            LogFollow($"service-pause reason=start-hysteresis distance={distance:0.0}");
            CancelPathSearch();
            return;
        }

        if (AdvancePathSearch(horse, Game1.currentLocation, targetTile))
            return;

        ApplyFollowSpeed(horse, MathF.Sqrt(distanceSquared), stopDistance);
        ticksSincePlan++;
        controllerAttached = followController is not null && ReferenceEquals(horse.controller, followController);
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
        {
            LogFollow(
                $"replan-request reason={(controllerAttached ? "owned-controller" : followController is null ? "no-controller" : !followController.HasPath ? "path-empty" : followController.IsStuck ? "stuck" : "target-changed")} "
                + $"target=({targetTile.X},{targetTile.Y}) ticksSincePlan={ticksSincePlan}");
            BeginFollowPathSearch(horse, targetTile, stopDistance);
        }

        if (followController is not null && ReferenceEquals(horse.controller, followController))
            horse.Sprite.loop = true;
    }

    // Flow: an offscreen horse moves toward each recorded outdoor exit; the expensive path search is divided over game updates.
    private void UpdateOutdoorTravel(Horse horse)
    {
        OutdoorTransition? transition = outdoorWarpTracker.CurrentTransition;
        if (transition is null)
        {
            StopFollowController();
            return;
        }

        if (transition.TransferRequested)
        {
            if (!OutdoorWarpTracker.IsSameLocation(horse.currentLocation, transition.TargetLocation))
                return;

            monitor.Log(
                $"Horse arrived in {transition.TargetLocation.NameOrUniqueName}; completed outdoor transition.",
                LogLevel.Trace);
            outdoorWarpTracker.CompleteCurrentTransition();
            ClearPathFailure();
            ticksSincePlan = RetryIntervalTicks;
            hasPlannedTarget = false;
            return;
        }

        if (!OutdoorWarpTracker.IsSameLocation(horse.currentLocation, transition.SourceLocation))
        {
            outdoorWarpTracker.ClearTransitions();
            StopFollowController();
            return;
        }

        Vector2 exitPosition = GetTileCenter(transition.SourceExitTile);
        float exitArrivalDistance = GetOutdoorExitArrivalDistance(horse);
        float distanceSquared = Vector2.DistanceSquared(horse.getStandingPosition(), exitPosition);
        if (horse.TilePoint == transition.SourceExitTile
            || distanceSquared <= exitArrivalDistance * exitArrivalDistance)
        {
            monitor.Log(
                $"Horse reached outdoor exit {transition.SourceLocation.NameOrUniqueName} ({transition.SourceExitTile.X}, {transition.SourceExitTile.Y}); crossing to {transition.TargetLocation.NameOrUniqueName}.",
                LogLevel.Trace);
            CompleteOutdoorTransition(horse, transition);
            return;
        }

        bool controllerAttached = followController is not null
            && ReferenceEquals(horse.controller, followController);
        if (horse.controller is not null && !controllerAttached)
        {
            CancelPathSearch();
            return;
        }

        if (AdvancePathSearch(horse, transition.SourceLocation, transition.SourceExitTile))
            return;

        ApplyOutdoorTravelSpeed(horse);
        ticksSincePlan++;
        controllerAttached = followController is not null
            && ReferenceEquals(horse.controller, followController);
        bool shouldReplan = controllerAttached
            ? followController!.IsStuck
            : followController is null
                ? ticksSincePlan >= RetryIntervalTicks
                : !followController.HasPath || ticksSincePlan >= RetryIntervalTicks;
        if (shouldReplan)
        {
            BeginOutdoorPathSearch(
                horse,
                transition,
                exitPosition,
                exitArrivalDistance);
        }
    }

    private void BeginFollowPathSearch(Horse horse, Point targetTile, float stopDistance)
    {
        plannedTargetTile = targetTile;
        hasPlannedTarget = true;
        BeginPathSearch(
            horse,
            new PathRequest(
                Game1.currentLocation,
                targetTile,
                Game1.player.getStandingPosition(),
                stopDistance,
                $"player target ({targetTile.X}, {targetTile.Y})"));
    }

    private void BeginOutdoorPathSearch(
        Horse horse,
        OutdoorTransition transition,
        Vector2 exitPosition,
        float exitArrivalDistance)
    {
        BeginPathSearch(
            horse,
            new PathRequest(
                transition.SourceLocation,
                transition.SourceExitTile,
                exitPosition,
                exitArrivalDistance,
                $"outdoor exit {transition.SourceLocation.NameOrUniqueName} ({transition.SourceExitTile.X}, {transition.SourceExitTile.Y})"));
    }

    private void BeginPathSearch(Horse horse, PathRequest request)
    {
        if (IsKnownUnreachable(horse, request.Location, request.TargetTile))
        {
            LogFollow($"path-skip reason=cached-failure target=({request.TargetTile.X},{request.TargetTile.Y})");
            return;
        }

        CancelPathSearch();
        ticksSincePlan = 0;
        bool controllerAttached = followController is not null
            && ReferenceEquals(horse.controller, followController);
        if (!controllerAttached)
        {
            followController = null;
            horse.stopWithoutChangingFrame();
        }

        LogFollow(
            $"path-search-start target=({request.TargetTile.X},{request.TargetTile.Y}) "
            + $"stopping={request.StoppingDistancePixels:0.0} preserveController={controllerAttached}");
        pathRequest = request;
        pathSearch = new HorsePathSearch(
            horse,
            request.Location,
            request.TargetPosition,
            request.StoppingDistancePixels);
        AdvancePathSearch(horse, request.Location, request.TargetTile);
    }

    // Guarantee: collision checks are capped per update; a failed static request is cached until the horse or target materially changes.
    private bool AdvancePathSearch(Horse horse, GameLocation location, Point targetTile)
    {
        if (pathSearch is null || pathRequest is null)
            return false;

        if (!OutdoorWarpTracker.IsSameLocation(pathRequest.Location, location)
            || HasTargetMovedEnough(pathRequest.TargetTile, targetTile))
        {
            CancelPathSearch();
            return false;
        }

        pathSearch.Advance(PathSearchNodesPerUpdate);
        if (!pathSearch.IsComplete)
        {
            LogFollow(
                $"path-search-progress target=({targetTile.X},{targetTile.Y}) "
                + $"searched={pathSearch.SearchedNodeCount}");
            return true;
        }

        HorsePathSearch completedSearch = pathSearch;
        PathRequest completedRequest = pathRequest;
        CancelPathSearch();
        if (completedSearch.Path is null)
        {
            RecordPathFailure(horse, completedRequest.Location, completedRequest.TargetTile);
            LogFollow(
                $"path-search-failed target=({completedRequest.TargetTile.X},{completedRequest.TargetTile.Y}) "
                + $"searched={completedSearch.SearchedNodeCount}");
            monitor.Log(
                $"Horse could not find {completedRequest.Description} after {completedSearch.SearchedNodeCount} nodes; it will not retry until the horse or target changes.",
                LogLevel.Trace);
            horse.stopWithoutChangingFrame();
            SetHorseIdle(horse);
            return true;
        }

        ClearPathFailure();
        followController = new HorseFollowController(
            horse,
            completedRequest.Location,
            completedRequest.TargetTile,
            completedRequest.TargetPosition,
            completedRequest.StoppingDistancePixels,
            UpdateFollowAnimation,
            MaintainHorseWalkAnimation,
            LogFollow,
            completedSearch.Path);
        LogFollow(
            $"controller-created target=({completedRequest.TargetTile.X},{completedRequest.TargetTile.Y}) "
            + $"path={completedSearch.Path.Count}");
        horse.controller = followController;
        return true;
    }

    private void CompleteOutdoorTransition(Horse horse, OutdoorTransition transition)
    {
        StopFollowController();
        ClearPathFailure();
        Point destinationTile = FindOpenDestinationTile(horse, transition);
        transition.TransferRequested = true;
        monitor.Log(
            $"Warping horse from {transition.SourceLocation.NameOrUniqueName} to {transition.TargetLocation.NameOrUniqueName} ({destinationTile.X}, {destinationTile.Y}).",
            LogLevel.Trace);
        Game1.warpCharacter(horse, transition.TargetLocation, destinationTile.ToVector2());
        if (OutdoorWarpTracker.IsSameLocation(horse.currentLocation, transition.TargetLocation))
        {
            outdoorWarpTracker.CompleteCurrentTransition();
            ticksSincePlan = RetryIntervalTicks;
            hasPlannedTarget = false;
        }
    }

    private static Point FindOpenDestinationTile(Horse horse, OutdoorTransition transition)
    {
        Vector2 openTile = Utility.recursiveFindOpenTileForCharacter(
            horse,
            transition.TargetLocation,
            transition.DestinationTile.ToVector2(),
            maxIterations: 24,
            allowOffMap: false);
        return openTile == Vector2.Zero
            ? transition.DestinationTile
            : openTile.ToPoint();
    }

    private void BeginFollowSession(Horse horse)
    {
        speedAdjustedHorse = horse;
        originalHorseSpeed = horse.speed;
        originalHorseAddedSpeed = horse.addedSpeed;
        hasOriginalHorseSpeed = true;
        ClearPathFailure();
        CancelPathSearch();
        followSessionActive = true;
        ticksSincePlan = RetryIntervalTicks;
        hasPlannedTarget = false;
    }

    private void ApplyFollowSpeed(Horse horse, float distancePixels, float stopDistancePixels)
    {
        float excessDistanceTiles = Math.Max(0f, distancePixels - stopDistancePixels) / 64f;
        float catchUpSpeed = MathHelper.Clamp(excessDistanceTiles * 0.5f, 0.5f, 2f);
        SetFollowSpeed(horse, catchUpSpeed);
    }

    private void ApplyOutdoorTravelSpeed(Horse horse)
    {
        SetFollowSpeed(horse, catchUpSpeed: 2f);
    }

    private void SetFollowSpeed(Horse horse, float catchUpSpeed)
    {
        if (!hasOriginalHorseSpeed || !ReferenceEquals(speedAdjustedHorse, horse))
            return;

        float followSpeed = Math.Max(2f, Game1.player.speed + Game1.player.addedSpeed + catchUpSpeed);
        horse.speed = (int)MathF.Floor(followSpeed);
        horse.addedSpeed = followSpeed - horse.speed;
    }

    private void UpdateFollowAnimation(Horse horse, int direction)
    {
        if (activeAnimationDirection == -1)
            StartHorseWalkAnimation(horse, direction, preservePhase: false);
        else if (activeAnimationDirection != direction)
            StartHorseWalkAnimation(horse, direction, preservePhase: true);
        else
            MaintainHorseWalkAnimation(horse);
    }

    // Guarantee: while this controller owns the horse, Horse.update may not turn the walk sequence into a one-shot animation.
    private void MaintainHorseWalkAnimation(Horse horse)
    {
        if (activeAnimationDirection == -1)
            StartHorseWalkAnimation(horse, horse.FacingDirection, preservePhase: false);
        else if (!IsCurrentHorseWalkAnimation(horse, activeAnimationDirection))
            StartHorseWalkAnimation(horse, activeAnimationDirection, preservePhase: false);

        horse.FacingDirection = activeAnimationDirection;
        horse.flip = activeAnimationDirection == 3;
        horse.drawOffset = activeAnimationDirection == 3 ? Vector2.Zero : new Vector2(-16f, 0f);
        horse.Sprite.loop = true;
    }

    // Rule: visual turns apply only between full gait cycles; movement continues in the latest path direction.
    private void StartHorseWalkAnimation(Horse horse, int direction, bool preservePhase)
    {
        int animationIndex = preservePhase && IsCurrentHorseWalkAnimation(horse, activeAnimationDirection)
            ? horse.Sprite.currentAnimationIndex
            : 0;
        float animationTimer = preservePhase && IsCurrentHorseWalkAnimation(horse, activeAnimationDirection)
            ? horse.Sprite.timer
            : 0f;
        activeAnimationDirection = direction;
        horse.Sprite.loop = true;
        horse.Sprite.setCurrentAnimation(direction switch
        {
            0 => new List<FarmerSprite.AnimationFrame>
            {
                new(15, HorseWalkAnimationFrameDurationMilliseconds),
                new(16, HorseWalkAnimationFrameDurationMilliseconds),
                new(17, HorseWalkAnimationFrameDurationMilliseconds),
                new(18, HorseWalkAnimationFrameDurationMilliseconds),
                new(19, HorseWalkAnimationFrameDurationMilliseconds),
                new(20, HorseWalkAnimationFrameDurationMilliseconds),
            },
            1 => new List<FarmerSprite.AnimationFrame>
            {
                new(8, HorseWalkAnimationFrameDurationMilliseconds),
                new(9, HorseWalkAnimationFrameDurationMilliseconds),
                new(10, HorseWalkAnimationFrameDurationMilliseconds),
                new(11, HorseWalkAnimationFrameDurationMilliseconds),
                new(12, HorseWalkAnimationFrameDurationMilliseconds),
                new(13, HorseWalkAnimationFrameDurationMilliseconds),
            },
            3 => new List<FarmerSprite.AnimationFrame>
            {
                new(8, HorseWalkAnimationFrameDurationMilliseconds, secondaryArm: false, flip: true),
                new(9, HorseWalkAnimationFrameDurationMilliseconds, secondaryArm: false, flip: true),
                new(10, HorseWalkAnimationFrameDurationMilliseconds, secondaryArm: false, flip: true),
                new(11, HorseWalkAnimationFrameDurationMilliseconds, secondaryArm: false, flip: true),
                new(12, HorseWalkAnimationFrameDurationMilliseconds, secondaryArm: false, flip: true),
                new(13, HorseWalkAnimationFrameDurationMilliseconds, secondaryArm: false, flip: true),
            },
            _ => new List<FarmerSprite.AnimationFrame>
            {
                new(1, HorseWalkAnimationFrameDurationMilliseconds),
                new(2, HorseWalkAnimationFrameDurationMilliseconds),
                new(3, HorseWalkAnimationFrameDurationMilliseconds),
                new(4, HorseWalkAnimationFrameDurationMilliseconds),
                new(5, HorseWalkAnimationFrameDurationMilliseconds),
                new(6, HorseWalkAnimationFrameDurationMilliseconds),
            },
        });
        animationIndex %= horse.Sprite.CurrentAnimation.Count;
        horse.Sprite.currentAnimationIndex = animationIndex;
        horse.Sprite.CurrentFrame = horse.Sprite.CurrentAnimation[animationIndex].frame;
        horse.Sprite.timer = animationTimer;
    }

    private static int GetHorseWalkStartFrame(int direction)
    {
        return direction switch
        {
            0 => 15,
            1 or 3 => 8,
            _ => 1,
        };
    }

    private static bool IsCurrentHorseWalkAnimation(Horse horse, int direction)
    {
        int startFrame = GetHorseWalkStartFrame(direction);
        return horse.Sprite.CurrentAnimation is { Count: 6 }
            && horse.Sprite.CurrentFrame >= startFrame
            && horse.Sprite.CurrentFrame < startFrame + 6;
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

    private static Vector2 GetTileCenter(Point tile)
    {
        return new Vector2((tile.X + 0.5f) * 64f, (tile.Y + 0.5f) * 64f);
    }

    private static Point GetFollowTargetTile()
    {
        return Game1.player.TilePoint;
    }

    private static bool HasTargetMovedEnough(Point previousTarget, Point currentTarget)
    {
        return Math.Abs(previousTarget.X - currentTarget.X)
            + Math.Abs(previousTarget.Y - currentTarget.Y) >= 2;
    }

    private static float GetOutdoorExitArrivalDistance(Horse horse)
    {
        return Math.Max(
            MinimumOutdoorExitArrivalDistancePixels,
            horse.GetBoundingBox().Width + 32f);
    }

    private bool IsKnownUnreachable(Horse horse, GameLocation location, Point targetTile)
    {
        return failedPath is { } failure
            && OutdoorWarpTracker.IsSameLocation(failure.Location, location)
            && !HasTargetMovedEnough(failure.TargetTile, targetTile)
            && Math.Abs(failure.HorseTile.X - horse.TilePoint.X) < 2
            && Math.Abs(failure.HorseTile.Y - horse.TilePoint.Y) < 2;
    }

    private void RecordPathFailure(Horse horse, GameLocation location, Point targetTile)
    {
        failedPath = new PathFailure(location, horse.TilePoint, targetTile);
    }

    private void ClearPathFailure()
    {
        failedPath = null;
    }

    private void LogFollow(string message)
    {
        monitor.Log($"[HorseFollower] {message}", LogLevel.Trace);
    }

    private void CancelPathSearch()
    {
        pathSearch = null;
        pathRequest = null;
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
        CancelPathSearch();
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
        activeAnimationDirection = -1;
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
        outdoorWarpTracker.Clear();
        ClearPathFailure();
        RestoreHorseSpeed();
        trackedHorse = null;
        followSessionActive = false;
        wasMounted = false;
    }

    private sealed record PathRequest(
        GameLocation Location,
        Point TargetTile,
        Vector2 TargetPosition,
        float StoppingDistancePixels,
        string Description);

    private sealed record PathFailure(
        GameLocation Location,
        Point HorseTile,
        Point TargetTile);
}
