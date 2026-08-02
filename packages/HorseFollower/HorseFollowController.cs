using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Pathfinding;

namespace HorseFollower;

internal sealed class HorseFollowController : PathFindController
{
    private const int SearchLimit = 10000;
    private const int StuckTimeoutMilliseconds = 750;

    private static readonly Point[] NeighborOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
        new(1, 1),
        new(1, -1),
        new(-1, 1),
        new(-1, -1)
    };

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
        Action<Horse, GameTime, int, float> animate)
        : base(CreatePath(horse, location, targetTile, targetPosition, stoppingDistancePixels), location, horse, targetTile)
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

        while (remainingMovement > 0f && HasPath && !IsWithinStoppingDistance())
        {
            while (HasPath && HasReached(pathToEndPoint.Peek()))
            {
                pathToEndPoint.Pop();
                timerSinceLastCheckPoint = 0;
            }

            if (!HasPath)
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

    private static Stack<Point>? CreatePath(
        Horse horse,
        GameLocation location,
        Point targetTile,
        Vector2 targetPosition,
        float stoppingDistancePixels)
    {
        Point start = horse.TilePoint;
        float stoppingDistanceSquared = stoppingDistancePixels * stoppingDistancePixels;
        Rectangle horseBounds = horse.GetBoundingBox();
        PathFindController? activeController = horse.controller;
        horse.controller = null;
        try
        {
            PriorityQueue<Point, (int EstimatedSteps, float RemainingDistance)> open = new();
            Dictionary<Point, int> costs = new() { [start] = 0 };
            Dictionary<Point, Point> previous = new();
            HashSet<Point> closed = new();
            int startHeuristic = GetHeuristic(start, targetPosition, stoppingDistancePixels);
            open.Enqueue(
                start,
                (startHeuristic, Vector2.Distance(GetTileCenter(start), targetPosition)));

            int searched = 0;
            while (open.TryDequeue(out Point current, out _))
            {
                if (!closed.Add(current))
                    continue;
                if (current != start
                    && IsWithinTargetRadius(current, targetPosition, stoppingDistanceSquared))
                {
                    return ReconstructPath(current, start, previous);
                }
                if (++searched >= SearchLimit)
                    return null;

                foreach (Point offset in NeighborOffsets)
                {
                    Point next = new(current.X + offset.X, current.Y + offset.Y);
                    if (closed.Contains(next) || !location.isTileOnMap(next))
                        continue;
                    if (!CanTraverse(current, next, horseBounds, horse, location))
                        continue;

                    int nextCost = costs[current] + 1;
                    if (costs.TryGetValue(next, out int existingCost) && existingCost <= nextCost)
                        continue;

                    costs[next] = nextCost;
                    previous[next] = current;
                    int heuristic = GetHeuristic(next, targetPosition, stoppingDistancePixels);
                    float remainingDistance = Vector2.Distance(GetTileCenter(next), targetPosition);
                    open.Enqueue(next, (nextCost + heuristic, remainingDistance));
                }
            }

            return null;
        }
        finally
        {
            horse.controller = activeController;
        }
    }

    private static bool CanTraverse(
        Point current,
        Point next,
        Rectangle horseBounds,
        Horse horse,
        GameLocation location)
    {
        if (!CanHorseStandAt(next, horseBounds, horse, location))
            return false;

        int horizontalOffset = next.X - current.X;
        int verticalOffset = next.Y - current.Y;
        if (horizontalOffset == 0 || verticalOffset == 0)
            return true;

        Point horizontalNeighbor = new(current.X + horizontalOffset, current.Y);
        Point verticalNeighbor = new(current.X, current.Y + verticalOffset);
        return location.isTileOnMap(horizontalNeighbor)
            && location.isTileOnMap(verticalNeighbor)
            && CanHorseStandAt(horizontalNeighbor, horseBounds, horse, location)
            && CanHorseStandAt(verticalNeighbor, horseBounds, horse, location);
    }

    private static bool CanHorseStandAt(
        Point tile,
        Rectangle horseBounds,
        Horse horse,
        GameLocation location)
    {
        Vector2 center = GetTileCenter(tile);
        Rectangle bounds = new(
            (int)center.X - horseBounds.Width / 2,
            (int)center.Y - horseBounds.Height / 2,
            horseBounds.Width,
            horseBounds.Height);
        return !location.isCollidingPosition(
            bounds,
            Game1.viewport,
            isFarmer: false,
            damagesFarmer: 0,
            glider: false,
            character: horse,
            pathfinding: true,
            projectile: false,
            ignoreCharacterRequirement: false,
            skipCollisionEffects: true);
    }

    private static bool IsWithinTargetRadius(
        Point tile,
        Vector2 targetPosition,
        float stoppingDistanceSquared)
    {
        Vector2 offset = GetTileCenter(tile) - targetPosition;
        return offset.LengthSquared() <= stoppingDistanceSquared;
    }

    private static int GetHeuristic(
        Point tile,
        Vector2 targetPosition,
        float stoppingDistancePixels)
    {
        Vector2 offset = GetTileCenter(tile) - targetPosition;
        float remainingDistance = Math.Max(
            0f,
            Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)) - stoppingDistancePixels);
        return (int)MathF.Ceiling(remainingDistance / 64f);
    }

    private static Stack<Point> ReconstructPath(
        Point end,
        Point start,
        Dictionary<Point, Point> previous)
    {
        Stack<Point> path = new();
        Point current = end;
        while (current != start)
        {
            path.Push(current);
            if (!previous.TryGetValue(current, out current))
                throw new InvalidOperationException("Path reconstruction did not reach its start node.");
        }

        return path;
    }
}
