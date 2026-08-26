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
    private const int FailedPathRetryCooldownTicks = 120;
    private const float CatchUpNormalSpeedDistanceTiles = 7f;
    private const float CatchUpFastSpeedDistanceTiles = 10f;
    private const float CatchUpNormalSpeedDistanceSquared = CatchUpNormalSpeedDistanceTiles * CatchUpNormalSpeedDistanceTiles * 4096f;
    private const float CatchUpFastSpeedDistanceSquared = CatchUpFastSpeedDistanceTiles * CatchUpFastSpeedDistanceTiles * 4096f;

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly OutdoorWarpTracker outdoorWarpTracker;
    private readonly HorseWalkAnimator horseAnimator = new();

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
    private int worldUpdateTicks;
    private Point plannedTargetTile;
    private bool hasPlannedTarget;
    private Horse? speedAdjustedHorse;
    private int originalHorseSpeed;
    private float originalHorseAddedSpeed;
    private bool hasOriginalHorseSpeed;
    private bool wasExternallyControlled;

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

        worldUpdateTicks++;

        Horse? mountedHorse = Game1.player.mount;
        if (mountedHorse is null && trackedHorse?.rider == Game1.player)
            mountedHorse = trackedHorse;

        if (mountedHorse is not null)
        {
            // The mounted state is stable across ticks; clean up only when entering it or changing horses.
            if (!wasMounted
                || !ReferenceEquals(trackedHorse, mountedHorse)
                || followSessionActive
                || followController is not null
                || pathSearch is not null
                || outdoorWarpTracker.HasTransitions
                || failedPath is not null
                || hasOriginalHorseSpeed)
            {
                StopFollowController(stopHorse: false);
                outdoorWarpTracker.Clear();
                ClearPathFailure();
                RestoreHorseSpeed();
            }

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
        if (followController is not null && ReferenceEquals(trackedHorse.controller, followController))
            horseAnimator.Tick(trackedHorse, Game1.currentGameTime);
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
        float stopDistance = config.FollowDistance * 64f;
        float startDistance = config.FollowStartDistance * 64f;
        float stopDistanceSquared = stopDistance * stopDistance;
        float startDistanceSquared = startDistance * startDistance;
        if (distanceSquared <= stopDistanceSquared)
        {
            RestoreOriginalHorseSpeed();
            if (followController is not null || pathSearch is not null)
            {
                LogFollow(() => $"service-stop reason=stopping-distance distance={MathF.Sqrt(distanceSquared):0.0}");
                StopFollowController();
            }
            return;
        }

        Point targetTile = GetFollowTargetTile();
        bool controllerAttached = followController is not null && ReferenceEquals(horse.controller, followController);
        if (horse.controller is not null && !controllerAttached)
        {
            if (!wasExternallyControlled)
                LogFollow(() => "service-pause reason=external-controller");
            wasExternallyControlled = true;
            RestoreOriginalHorseSpeed();
            CancelPathSearch();
            return;
        }

        if (wasExternallyControlled && horse.controller is null)
        {
            wasExternallyControlled = false;
            LogFollow(() => "service-resume reason=external-controller-ended");
        }

        ApplyFollowSpeed(horse, distanceSquared);

        if (followController is null && distanceSquared <= startDistanceSquared)
        {
            CancelPathSearch();
            return;
        }

        if (AdvancePathSearch(horse, Game1.currentLocation, targetTile))
            return;
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
                () => $"replan-request reason={(controllerAttached ? "owned-controller" : followController is null ? "no-controller" : !followController.HasPath ? "path-empty" : followController.IsStuck ? "stuck" : "target-changed")} "
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

            LogTrace(
                () => $"Horse arrived in {transition.TargetLocation.NameOrUniqueName}; completed outdoor transition.");
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
            LogTrace(
                () => $"Horse reached outdoor exit {transition.SourceLocation.NameOrUniqueName} ({transition.SourceExitTile.X}, {transition.SourceExitTile.Y}); crossing to {transition.TargetLocation.NameOrUniqueName}.");
            CompleteOutdoorTransition(horse, transition);
            return;
        }

        bool controllerAttached = followController is not null
            && ReferenceEquals(horse.controller, followController);
        if (horse.controller is not null && !controllerAttached)
        {
            if (!wasExternallyControlled)
                LogFollow(() => "service-pause reason=external-controller");
            wasExternallyControlled = true;
            RestoreOriginalHorseSpeed();
            CancelPathSearch();
            return;
        }

        if (wasExternallyControlled && horse.controller is null)
        {
            wasExternallyControlled = false;
            LogFollow(() => "service-resume reason=external-controller-ended");
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
                () => $"player target ({targetTile.X}, {targetTile.Y})"));
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
                () => $"outdoor exit {transition.SourceLocation.NameOrUniqueName} ({transition.SourceExitTile.X}, {transition.SourceExitTile.Y})"));
    }

    private void BeginPathSearch(Horse horse, PathRequest request)
    {
        if (IsKnownUnreachable(horse, request.Location, request.TargetTile))
        {
            ticksSincePlan = 0;
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
            horseAnimator.Reset();
        }

        LogFollow(
            () => $"path-search-start target=({request.TargetTile.X},{request.TargetTile.Y}) "
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
            return true;

        HorsePathSearch completedSearch = pathSearch;
        PathRequest completedRequest = pathRequest;
        CancelPathSearch();
        if (horse.TilePoint != completedSearch.StartTile)
        {
            if (completedSearch.Path is null
                || !completedSearch.TryReuseFrom(horse, out int trimmedWaypoints))
            {
                LogFollow(
                    () => $"path-search-discard reason=stale-start searchedStart=({completedSearch.StartTile.X},{completedSearch.StartTile.Y}) "
                        + $"current=({horse.TilePoint.X},{horse.TilePoint.Y})");
                BeginPathSearch(horse, completedRequest);
                return true;
            }

            LogFollow(
                () => $"path-search-reuse searchedStart=({completedSearch.StartTile.X},{completedSearch.StartTile.Y}) "
                    + $"current=({horse.TilePoint.X},{horse.TilePoint.Y}) trimmed={trimmedWaypoints} "
                    + $"remaining={completedSearch.Path.Count}");
        }

        if (completedSearch.Path is null)
        {
            RecordPathFailure(horse, completedRequest.Location, completedRequest.TargetTile);
            LogFollow(
                () => $"path-search-failed target=({completedRequest.TargetTile.X},{completedRequest.TargetTile.Y}) "
                    + $"searched={completedSearch.SearchedNodeCount}");
            LogTrace(
                () => $"Horse could not find {completedRequest.Description()} after {completedSearch.SearchedNodeCount} nodes; it will retry after the failure cooldown or when the horse or target changes.");
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
            horseAnimator.Animate,
            horseAnimator.Maintain,
            message => LogFollow(() => message),
            completedSearch.Path);
        LogFollow(
            () => $"controller-created target=({completedRequest.TargetTile.X},{completedRequest.TargetTile.Y}) "
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
        LogTrace(
            () => $"Warping horse from {transition.SourceLocation.NameOrUniqueName} to {transition.TargetLocation.NameOrUniqueName} ({destinationTile.X}, {destinationTile.Y}).");
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

    private void ApplyFollowSpeed(Horse horse, float distanceSquared)
    {
        float catchUpSpeed = distanceSquared >= CatchUpFastSpeedDistanceSquared
            ? 2f
            : distanceSquared > CatchUpNormalSpeedDistanceSquared
                ? 1f
                : 0f;
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

        if (catchUpSpeed <= 0f)
        {
            RestoreOriginalHorseSpeed();
            return;
        }

        float followSpeed = Math.Max(2f, Game1.player.speed + Game1.player.addedSpeed + catchUpSpeed);
        int targetSpeed = (int)MathF.Floor(followSpeed);
        float targetAddedSpeed = followSpeed - targetSpeed;
        if (horse.speed != targetSpeed)
            horse.speed = targetSpeed;
        if (horse.addedSpeed != targetAddedSpeed)
            horse.addedSpeed = targetAddedSpeed;
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
            && worldUpdateTicks - failure.RecordedAtTick < FailedPathRetryCooldownTicks
            && OutdoorWarpTracker.IsSameLocation(failure.Location, location)
            && !HasTargetMovedEnough(failure.TargetTile, targetTile)
            && Math.Abs(failure.HorseTile.X - horse.TilePoint.X) < 2
            && Math.Abs(failure.HorseTile.Y - horse.TilePoint.Y) < 2;
    }

    private void RecordPathFailure(Horse horse, GameLocation location, Point targetTile)
    {
        failedPath = new PathFailure(location, horse.TilePoint, targetTile, worldUpdateTicks);
    }

    private void ClearPathFailure()
    {
        failedPath = null;
    }

    private bool IsVerbose => monitor.IsVerbose;

    private void LogFollow(Func<string> messageFactory)
    {
        if (IsVerbose)
            monitor.Log($"[HorseFollower] {messageFactory()}", LogLevel.Trace);
    }

    private void LogTrace(Func<string> messageFactory)
    {
        if (IsVerbose)
            monitor.Log($"[HorseFollower] {messageFactory()}", LogLevel.Trace);
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

    private void StopFollowController(bool stopHorse = true)
    {
        CancelPathSearch();
        if (trackedHorse is not null)
        {
            if (followController is not null && ReferenceEquals(trackedHorse.controller, followController))
                trackedHorse.controller = null;
            if (stopHorse)
                trackedHorse.stopWithoutChangingFrame();
        }

        if (trackedHorse is { rider: null } horse)
            SetHorseIdle(horse);

        followController = null;
        wasExternallyControlled = false;
        ticksSincePlan = RetryIntervalTicks;
        hasPlannedTarget = false;
        horseAnimator.Reset();
    }

    private void RestoreOriginalHorseSpeed()
    {
        if (hasOriginalHorseSpeed && speedAdjustedHorse is not null)
        {
            if (speedAdjustedHorse.speed != originalHorseSpeed)
                speedAdjustedHorse.speed = originalHorseSpeed;
            if (speedAdjustedHorse.addedSpeed != originalHorseAddedSpeed)
                speedAdjustedHorse.addedSpeed = originalHorseAddedSpeed;
        }
    }

    private void RestoreHorseSpeed()
    {
        RestoreOriginalHorseSpeed();
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
        Func<string> Description);

    private sealed record PathFailure(
        GameLocation Location,
        Point HorseTile,
        Point TargetTile,
        int RecordedAtTick);
}
