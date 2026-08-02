using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Pathfinding;

namespace HorseFollower;

internal sealed class HorsePathSearch
{
    private const int SearchLimit = 10000;

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
    private readonly GameLocation location;
    private readonly Point start;
    private readonly Vector2 targetPosition;
    private readonly float stoppingDistanceSquared;
    private readonly Rectangle horseBounds;
    private readonly PriorityQueue<Point, (int EstimatedSteps, float RemainingDistance)> open = new();
    private readonly Dictionary<Point, int> costs = new();
    private readonly Dictionary<Point, Point> previous = new();
    private readonly HashSet<Point> closed = new();

    internal HorsePathSearch(
        Horse horse,
        GameLocation location,
        Vector2 targetPosition,
        float stoppingDistancePixels)
    {
        this.horse = horse;
        this.location = location;
        this.targetPosition = targetPosition;
        stoppingDistanceSquared = stoppingDistancePixels * stoppingDistancePixels;
        start = horse.TilePoint;
        horseBounds = horse.GetBoundingBox();
        costs[start] = 0;
        int startHeuristic = GetHeuristic(start, targetPosition, stoppingDistancePixels);
        open.Enqueue(
            start,
            (startHeuristic, Vector2.Distance(GetTileCenter(start), targetPosition)));
    }

    internal bool IsComplete { get; private set; }

    internal Stack<Point>? Path { get; private set; }

    internal int SearchedNodeCount { get; private set; }

    // Flow: run a bounded amount of game-thread collision work per update, preserving the full A* frontier for later updates.
    internal void Advance(int nodeBudget)
    {
        if (IsComplete)
            return;
        if (nodeBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(nodeBudget));

        PathFindController? activeController = horse.controller;
        horse.controller = null;
        try
        {
            int processedNodes = 0;
            while (processedNodes < nodeBudget && open.TryDequeue(out Point current, out _))
            {
                processedNodes++;
                if (!closed.Add(current))
                    continue;

                if (current != start && IsWithinTargetRadius(current))
                {
                    Path = ReconstructPath(current);
                    IsComplete = true;
                    return;
                }

                SearchedNodeCount++;
                if (SearchedNodeCount >= SearchLimit)
                {
                    IsComplete = true;
                    return;
                }

                foreach (Point offset in NeighborOffsets)
                {
                    Point next = new(current.X + offset.X, current.Y + offset.Y);
                    if (closed.Contains(next) || !location.isTileOnMap(next))
                        continue;
                    if (!CanTraverse(current, next))
                        continue;

                    int nextCost = costs[current] + 1;
                    if (costs.TryGetValue(next, out int existingCost) && existingCost <= nextCost)
                        continue;

                    costs[next] = nextCost;
                    previous[next] = current;
                    int heuristic = GetHeuristic(next, targetPosition, MathF.Sqrt(stoppingDistanceSquared));
                    float remainingDistance = Vector2.Distance(GetTileCenter(next), targetPosition);
                    open.Enqueue(next, (nextCost + heuristic, remainingDistance));
                }
            }

            if (open.Count == 0)
                IsComplete = true;
        }
        finally
        {
            horse.controller = activeController;
        }
    }

    private bool CanTraverse(Point current, Point next)
    {
        if (!CanHorseStandAt(next))
            return false;

        int horizontalOffset = next.X - current.X;
        int verticalOffset = next.Y - current.Y;
        if (horizontalOffset == 0 || verticalOffset == 0)
            return true;

        Point horizontalNeighbor = new(current.X + horizontalOffset, current.Y);
        Point verticalNeighbor = new(current.X, current.Y + verticalOffset);
        return location.isTileOnMap(horizontalNeighbor)
            && location.isTileOnMap(verticalNeighbor)
            && CanHorseStandAt(horizontalNeighbor)
            && CanHorseStandAt(verticalNeighbor);
    }

    private bool CanHorseStandAt(Point tile)
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

    private bool IsWithinTargetRadius(Point tile)
    {
        Vector2 offset = GetTileCenter(tile) - targetPosition;
        return offset.LengthSquared() <= stoppingDistanceSquared;
    }

    private Stack<Point> ReconstructPath(Point end)
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

    private static Vector2 GetTileCenter(Point tile)
    {
        return new Vector2((tile.X + 0.5f) * 64f, (tile.Y + 0.5f) * 64f);
    }
}
