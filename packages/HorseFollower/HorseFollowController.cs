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
        new(0, -1)
    };

    private readonly Horse horse;
    private readonly Vector2 targetPosition;
    private readonly float stoppingDistancePixels;
    private readonly Action<Horse, GameTime, int> animate;
    private readonly int initialWaypointCount;

    internal HorseFollowController(
        Horse horse,
        GameLocation location,
        Point targetTile,
        Vector2 targetPosition,
        float stoppingDistancePixels,
        Action<Horse, GameTime, int> animate)
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
        int direction = MoveAlongPath(time);
        if (horse.Position.Equals(previousPosition))
        {
            pausedTimer += time.ElapsedGameTime.Milliseconds;
            horse.stopWithoutChangingFrame();
        }
        else
        {
            pausedTimer = 0;
            animate(horse, time, direction);
        }

        return !HasPath || IsStuck || IsWithinStoppingDistance();
    }

    // Flow: spend the full frame movement budget, including across waypoint boundaries, so the horse never pauses per tile.
    private int MoveAlongPath(GameTime time)
    {
        float remainingMovement = GetMovementDistance();
        int lastDirection = horse.FacingDirection;

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
            int direction = GetDirection(offset);
            float distanceOnAxis = direction is 1 or 3 ? Math.Abs(offset.X) : Math.Abs(offset.Y);
            float step = Math.Min(remainingMovement, distanceOnAxis);
            if (step <= 0f || !TryMove(direction, step))
                break;

            remainingMovement -= step;
            lastDirection = direction;
        }

        return lastDirection;
    }

    private bool TryMove(int direction, float distance)
    {
        Vector2 motion = direction switch
        {
            0 => new Vector2(0f, -distance),
            1 => new Vector2(distance, 0f),
            2 => new Vector2(0f, distance),
            3 => new Vector2(-distance, 0f),
            _ => Vector2.Zero
        };
        if (motion == Vector2.Zero)
            return false;

        Vector2 currentPosition = horse.Position;
        Vector2 nextPosition = currentPosition + motion;
        Rectangle nextBounds = horse.GetBoundingBox();
        nextBounds.Offset(
            (int)nextPosition.X - (int)currentPosition.X,
            (int)nextPosition.Y - (int)currentPosition.Y);

        PathFindController? activeController = horse.controller;
        horse.controller = null;
        bool collides;
        try
        {
            collides = location.isCollidingPosition(
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
        finally
        {
            horse.controller = activeController;
        }

        if (collides)
            return false;

        horse.stopWithoutChangingFrame();
        switch (direction)
        {
            case 0:
                horse.SetMovingUp(true);
                break;
            case 1:
                horse.SetMovingRight(true);
                break;
            case 2:
                horse.SetMovingDown(true);
                break;
            case 3:
                horse.SetMovingLeft(true);
                break;
        }

        horse.Position = nextPosition;
        horse.FacingDirection = direction;
        return true;
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
            PriorityQueue<Point, int> open = new();
            Dictionary<Point, int> costs = new() { [start] = 0 };
            Dictionary<Point, Point> previous = new();
            HashSet<Point> closed = new();
            open.Enqueue(start, GetHeuristic(start, targetPosition, stoppingDistancePixels));

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
                    if (next != start && !CanHorseStandAt(next, horseBounds, horse, location))
                        continue;

                    int nextCost = costs[current] + 1;
                    if (costs.TryGetValue(next, out int existingCost) && existingCost <= nextCost)
                        continue;

                    costs[next] = nextCost;
                    previous[next] = current;
                    int priority = nextCost + GetHeuristic(next, targetPosition, stoppingDistancePixels);
                    open.Enqueue(next, priority);
                }
            }

            return null;
        }
        finally
        {
            horse.controller = activeController;
        }
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
        float remainingDistance = Math.Max(
            0f,
            Vector2.Distance(GetTileCenter(tile), targetPosition) - stoppingDistancePixels);
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
