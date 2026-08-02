using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Pathfinding;

namespace HorseFollower;

internal sealed class HorseFollowController : PathFindController
{
    private const int StuckTimeoutMilliseconds = 750;
    private const int MaxRouteSegmentsPerUpdate = 8;
    private const int MaxReachedWaypointsPerUpdate = 8;

    private readonly Horse horse;
    private readonly Vector2 targetPosition;
    private readonly float stoppingDistancePixels;
    private readonly Action<Horse, GameTime, int, float> animate;
    private readonly int initialWaypointCount;

    internal HorseFollowController(
        Horse horse,
        GameLocation location,
        Point targetTile,
        Vector2 targetPosition,
        float stoppingDistancePixels,
        Action<Horse, GameTime, int, float> animate,
        Stack<Point> path)
        : base(path, location, horse, targetTile)
    {
        this.horse = horse;
        this.targetPosition = targetPosition;
        this.stoppingDistancePixels = stoppingDistancePixels;
        this.animate = animate;
        initialWaypointCount = pathToEndPoint?.Count ?? 0;
    }

    internal bool HasPath => pathToEndPoint is { Count: > 0 };

    internal bool IsStuck => pausedTimer >= StuckTimeoutMilliseconds;

    internal bool MadeProgress => pathToEndPoint is not null && pathToEndPoint.Count < initialWaypointCount;

    public override bool update(GameTime time)
    {
        if (!HasPath || IsWithinStoppingDistance())
        {
            horse.stopWithoutChangingFrame();
            return true;
        }

        if (Game1.activeClickableMenu is not null && !Game1.IsMultiplayer)
            return false;

        Vector2 previousPosition = horse.Position;
        (int direction, float distanceMoved) = MoveAlongPath();
        if (horse.Position.Equals(previousPosition))
        {
            pausedTimer += time.ElapsedGameTime.Milliseconds;
            horse.stopWithoutChangingFrame();
        }
        else
        {
            pausedTimer = 0;
            animate(horse, time, direction, distanceMoved);
        }

        return !HasPath || IsStuck || IsWithinStoppingDistance();
    }

    // Flow: spend the full update movement budget, including across diagonal waypoints, without per-tile pauses.
    private (int Direction, float DistanceMoved) MoveAlongPath()
    {
        float remainingMovement = GetMovementDistance();
        int lastDirection = horse.FacingDirection;
        float distanceMoved = 0f;

        int routeSegments = 0;
        int reachedWaypoints = 0;
        while (remainingMovement > 0f
            && routeSegments < MaxRouteSegmentsPerUpdate
            && HasPath
            && !IsWithinStoppingDistance())
        {
            while (HasPath
                && reachedWaypoints < MaxReachedWaypointsPerUpdate
                && HasReached(pathToEndPoint.Peek()))
            {
                pathToEndPoint.Pop();
                timerSinceLastCheckPoint = 0;
                reachedWaypoints++;
            }

            if (!HasPath || reachedWaypoints >= MaxReachedWaypointsPerUpdate)
                break;

            Point waypoint = pathToEndPoint.Peek();
            Vector2 target = GetTileCenter(waypoint);
            Vector2 offset = target - horse.getStandingPosition();
            float step = Math.Min(
                remainingMovement,
                Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)));
            Vector2 requestedMotion = new(
                MathHelper.Clamp(offset.X, -step, step),
                MathHelper.Clamp(offset.Y, -step, step));
            if (step <= 0f || !TryMove(requestedMotion, out Vector2 actualMotion))
                break;

            float movementSpent = Math.Max(Math.Abs(actualMotion.X), Math.Abs(actualMotion.Y));
            remainingMovement -= movementSpent;
            distanceMoved += actualMotion.Length();
            lastDirection = GetDirection(actualMotion);
            routeSegments++;
        }

        return (lastDirection, distanceMoved);
    }

    private bool TryMove(Vector2 requestedMotion, out Vector2 actualMotion)
    {
        actualMotion = Vector2.Zero;
        if (requestedMotion == Vector2.Zero)
            return false;

        PathFindController? activeController = horse.controller;
        horse.controller = null;
        try
        {
            if (requestedMotion.X != 0f && requestedMotion.Y != 0f)
            {
                Vector2 horizontalMotion = new(requestedMotion.X, 0f);
                Vector2 verticalMotion = new(0f, requestedMotion.Y);
                bool horizontalBlocked = IsColliding(horizontalMotion);
                bool verticalBlocked = IsColliding(verticalMotion);
                if (!horizontalBlocked && !verticalBlocked && !IsColliding(requestedMotion))
                {
                    actualMotion = requestedMotion;
                }
                else if (!horizontalBlocked
                    && (Math.Abs(requestedMotion.X) >= Math.Abs(requestedMotion.Y) || verticalBlocked))
                {
                    actualMotion = horizontalMotion;
                }
                else if (!verticalBlocked)
                {
                    actualMotion = verticalMotion;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (IsColliding(requestedMotion))
                    return false;

                actualMotion = requestedMotion;
            }
        }
        finally
        {
            horse.controller = activeController;
        }

        horse.stopWithoutChangingFrame();
        if (actualMotion.Y < 0f)
            horse.SetMovingUp(true);
        else if (actualMotion.Y > 0f)
            horse.SetMovingDown(true);
        if (actualMotion.X > 0f)
            horse.SetMovingRight(true);
        else if (actualMotion.X < 0f)
            horse.SetMovingLeft(true);

        horse.Position += actualMotion;
        horse.FacingDirection = GetDirection(actualMotion);
        return true;
    }

    private bool IsColliding(Vector2 motion)
    {
        Vector2 currentPosition = horse.Position;
        Vector2 nextPosition = currentPosition + motion;
        Rectangle nextBounds = horse.GetBoundingBox();
        nextBounds.Offset(
            (int)nextPosition.X - (int)currentPosition.X,
            (int)nextPosition.Y - (int)currentPosition.Y);
        return location.isCollidingPosition(
            nextBounds,
            Game1.viewport,
            isFarmer: false,
            damagesFarmer: 0,
            glider: false,
            character: horse,
            pathfinding: false,
            projectile: false,
            ignoreCharacterRequirement: false,
            skipCollisionEffects: true);
    }

    private float GetMovementDistance()
    {
        return Math.Max(1f, horse.speed + horse.addedSpeed);
    }

    private bool HasReached(Point waypoint)
    {
        Vector2 offset = GetTileCenter(waypoint) - horse.getStandingPosition();
        return Math.Abs(offset.X) <= 0.5f && Math.Abs(offset.Y) <= 0.5f;
    }

    private bool IsWithinStoppingDistance()
    {
        Vector2 offset = horse.getStandingPosition() - targetPosition;
        return offset.LengthSquared() <= stoppingDistancePixels * stoppingDistancePixels;
    }

    private static int GetDirection(Vector2 offset)
    {
        if (Math.Abs(offset.X) > Math.Abs(offset.Y))
            return offset.X > 0f ? 1 : 3;

        return offset.Y > 0f ? 2 : 0;
    }

    private static Vector2 GetTileCenter(Point tile)
    {
        return new Vector2((tile.X + 0.5f) * 64f, (tile.Y + 0.5f) * 64f);
    }
}
