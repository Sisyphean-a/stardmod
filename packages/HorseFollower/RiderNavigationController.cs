using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Pathfinding;

namespace HorseFollower;

internal enum RiderNavigationStopReason
{
    Finished,
    Stuck,
    InvalidLocation
}

internal sealed class RiderNavigationController : PathFindController
{
    private const int StuckTimeoutMilliseconds = 1500;
    private const int MaxReachedWaypointsPerUpdate = 8;

    private readonly Farmer rider;
    private readonly Horse horse;
    private readonly HorseWalkAnimator horseAnimator;
    private readonly Action<RiderNavigationStopReason> stopped;
    private readonly Action portalAttempt;
    private readonly OutdoorRouteEdge? portalEdge;
    private readonly int initialWaypointCount;
    private bool portalAttempted;
    private bool stopReported;

    internal RiderNavigationController(
        Farmer rider,
        Horse horse,
        HorseWalkAnimator horseAnimator,
        GameLocation location,
        Point targetTile,
        Stack<Point> path,
        OutdoorRouteEdge? portalEdge,
        Action portalAttempt,
        Action<RiderNavigationStopReason> stopped)
        : base(path, location, rider, targetTile)
    {
        this.rider = rider;
        this.horse = horse;
        this.horseAnimator = horseAnimator;
        this.portalEdge = portalEdge;
        this.portalAttempt = portalAttempt;
        this.stopped = stopped;
        initialWaypointCount = pathToEndPoint?.Count ?? 0;
    }

    internal bool HasPath => pathToEndPoint is { Count: > 0 };

    internal bool IsStuck => pausedTimer >= StuckTimeoutMilliseconds;

    internal bool MadeProgress => pathToEndPoint is not null && pathToEndPoint.Count < initialWaypointCount;

    public override bool update(GameTime time)
    {
        if (!OutdoorWarpTracker.IsSameLocation(rider.currentLocation, location))
        {
            ReportStop(RiderNavigationStopReason.InvalidLocation);
            return true;
        }

        if (Game1.activeClickableMenu is not null)
            return false;

        if (HasPath)
        {
            int reachedWaypoints = 0;
            while (HasPath
                && reachedWaypoints < MaxReachedWaypointsPerUpdate
                && HasReached(pathToEndPoint.Peek()))
            {
                pathToEndPoint.Pop();
                reachedWaypoints++;
                timerSinceLastCheckPoint = 0;
            }
        }

        if (!HasPath)
        {
            if (portalEdge is not null)
            {
                if (TryTriggerPortal(time))
                    return true;
                if (IsStuck)
                {
                    rider.stopWithoutChangingFrame();
                    ReportStop(RiderNavigationStopReason.Stuck);
                    return true;
                }

                return false;
            }

            rider.stopWithoutChangingFrame();
            ReportStop(RiderNavigationStopReason.Finished);
            return true;
        }

        Vector2 previousPosition = rider.Position;
        bool moved = TryMoveToward(pathToEndPoint.Peek(), time);
        if (!OutdoorWarpTracker.IsSameLocation(rider.currentLocation, location))
            return true;
        if (moved)
        {
            pausedTimer = 0;
            timerSinceLastCheckPoint = 0;
        }
        else
        {
            pausedTimer += time.ElapsedGameTime.Milliseconds;
        }

        if (IsStuck)
        {
            rider.stopWithoutChangingFrame();
            ReportStop(RiderNavigationStopReason.Stuck);
            return true;
        }

        if (!moved && rider.Position == previousPosition)
            return false;

        return false;
    }

    private bool TryTriggerPortal(GameTime time)
    {
        if (Game1.isWarping || Game1.locationRequest is not null)
            return true;

        int direction = OutdoorRouteGraph.GetPortalDirection(
            location,
            portalEdge!.SourceExitTile,
            rider.TilePoint);
        Warp? currentWarp = location.isCollidingWithWarp(rider.GetBoundingBox(), rider);
        if (currentWarp is not null
            && currentWarp.X == portalEdge.SourceExitTile.X
            && currentWarp.Y == portalEdge.SourceExitTile.Y
            && string.Equals(currentWarp.TargetName, portalEdge.TargetLocation.NameOrUniqueName, StringComparison.Ordinal)
            && currentWarp.TargetX == portalEdge.TargetEntryTile.X
            && currentWarp.TargetY == portalEdge.TargetEntryTile.Y)
        {
            if (!portalAttempted)
            {
                portalAttempted = true;
                portalAttempt();
                rider.warpFarmer(currentWarp, direction);
            }

            return true;
        }

        rider.movementDirections.Clear();
        SetMoving(rider, direction);
        Vector2 previousPosition = rider.Position;
        rider.MovePosition(time, Game1.viewport, location);
        if (!OutdoorWarpTracker.IsSameLocation(rider.currentLocation, location)
            || Game1.isWarping
            || Game1.locationRequest is not null)
        {
            return true;
        }
        if (rider.Position != previousPosition)
        {
            horseAnimator.Animate(horse, direction);
            pausedTimer = 0;
        }
        else
        {
            rider.movementDirections.Clear();
            pausedTimer += time.ElapsedGameTime.Milliseconds;
        }
        return false;
    }

    private bool TryMoveToward(Point waypoint, GameTime time)
    {
        Vector2 offset = GetTileCenter(waypoint) - rider.getStandingPosition();
        if (Math.Abs(offset.X) <= 4f && Math.Abs(offset.Y) <= 4f)
            return true;

        int direction;
        if (Math.Abs(offset.X) >= Math.Abs(offset.Y) && Math.Abs(offset.X) > 0f)
            direction = offset.X > 0f ? 1 : 3;
        else
            direction = offset.Y > 0f ? 2 : 0;

        rider.movementDirections.Clear();
        SetMoving(rider, direction);
        Vector2 previousPosition = rider.Position;
        // Guarantee: let the vanilla Farmer movement path perform the authoritative collision and Warp checks.
        rider.MovePosition(time, Game1.viewport, location);
        bool moved = rider.Position != previousPosition;
        if (moved)
            horseAnimator.Animate(horse, direction);
        else
            rider.movementDirections.Clear();
        return moved;
    }

    private void ReportStop(RiderNavigationStopReason reason)
    {
        if (stopReported)
            return;

        stopReported = true;
        stopped(reason);
    }

    private static void SetMoving(Farmer farmer, int direction)
    {
        switch (direction)
        {
            case 0:
                farmer.SetMovingUp(true);
                break;
            case 1:
                farmer.SetMovingRight(true);
                break;
            case 2:
                farmer.SetMovingDown(true);
                break;
            case 3:
                farmer.SetMovingLeft(true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }

    private bool HasReached(Point waypoint)
    {
        Vector2 offset = GetTileCenter(waypoint) - rider.getStandingPosition();
        return Math.Abs(offset.X) <= 4f && Math.Abs(offset.Y) <= 4f;
    }

    private static Vector2 GetTileCenter(Point tile)
    {
        return new Vector2((tile.X + 0.5f) * 64f, (tile.Y + 0.5f) * 64f);
    }
}
