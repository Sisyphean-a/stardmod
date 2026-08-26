using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;

namespace HorseFollower;

internal sealed class RiderPathSearch
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

    private readonly Farmer rider;
    private readonly GameLocation location;
    private readonly Point start;
    private readonly Vector2 targetPosition;
    private readonly float stoppingDistancePixels;
    private readonly float stoppingDistanceSquared;
    private readonly Rectangle riderBounds;
    private readonly PriorityQueue<Point, (int EstimatedSteps, float RemainingDistanceSquared)> open = new();
    private readonly Dictionary<Point, int> costs = new();
    private readonly Dictionary<Point, Point> previous = new();
    private readonly HashSet<Point> closed = new();
    private readonly Dictionary<Point, bool> standability = new();

    internal RiderPathSearch(
        Farmer rider,
        GameLocation location,
        Vector2 targetPosition,
        float stoppingDistancePixels)
    {
        this.rider = rider;
        this.location = location;
        this.targetPosition = targetPosition;
        this.stoppingDistancePixels = stoppingDistancePixels;
        stoppingDistanceSquared = stoppingDistancePixels * stoppingDistancePixels;
        start = rider.TilePoint;
        riderBounds = rider.GetBoundingBox();
        costs[start] = 0;
        int startHeuristic = GetHeuristic(start, targetPosition, stoppingDistancePixels);
        open.Enqueue(
            start,
            (startHeuristic, GetDistanceSquared(GetTileCenter(start), targetPosition)));
    }

    internal bool IsComplete { get; private set; }

    internal Stack<Point>? Path { get; private set; }

    internal int SearchedNodeCount { get; private set; }

    // Flow: mounted movement is owned by Farmer.MovePosition, so search uses the rider collision rules; only a bounded amount runs per game update.
    internal void Advance(int nodeBudget)
    {
        if (IsComplete)
            return;
        if (nodeBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(nodeBudget));

        standability.Clear();
        int processedNodes = 0;
        while (processedNodes < nodeBudget && open.TryDequeue(out Point current, out _))
        {
            processedNodes++;
            if (!closed.Add(current))
                continue;

            if (IsWithinTargetRadius(current))
            {
                Path = current == start ? new Stack<Point>() : ReconstructPath(current);
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
                int heuristic = GetHeuristic(next, targetPosition, stoppingDistancePixels);
                float remainingDistanceSquared = GetDistanceSquared(GetTileCenter(next), targetPosition);
                open.Enqueue(next, (nextCost + heuristic, remainingDistanceSquared));
            }
        }

        if (open.Count == 0)
            IsComplete = true;
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
        if (standability.TryGetValue(tile, out bool canStand))
            return canStand;

        Vector2 center = GetTileCenter(tile);
        Rectangle bounds = new(
            (int)center.X - riderBounds.Width / 2,
            (int)center.Y - riderBounds.Height / 2,
            riderBounds.Width,
            riderBounds.Height);
        canStand = !location.isCollidingPosition(
            bounds,
            Game1.viewport,
            isFarmer: true,
            damagesFarmer: 0,
            glider: false,
            character: rider,
            pathfinding: true,
            projectile: false,
            ignoreCharacterRequirement: false,
            skipCollisionEffects: true);
        standability[tile] = canStand;
        return canStand;
    }

    private bool IsWithinTargetRadius(Point tile)
    {
        Vector2 offset = GetTileCenter(tile) - targetPosition;
        return offset.LengthSquared() <= stoppingDistanceSquared;
    }

    private Stack<Point> ReconstructPath(Point end)
    {
        var path = new Stack<Point>();
        Point current = end;
        while (current != start)
        {
            path.Push(current);
            if (!previous.TryGetValue(current, out current))
                throw new InvalidOperationException("骑乘路线回溯没有到达起点。");
        }

        return path;
    }

    private static int GetHeuristic(Point tile, Vector2 targetPosition, float stoppingDistancePixels)
    {
        Vector2 offset = GetTileCenter(tile) - targetPosition;
        float remainingDistance = Math.Max(
            0f,
            Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)) - stoppingDistancePixels);
        return (int)MathF.Ceiling(remainingDistance / 64f);
    }

    private static float GetDistanceSquared(Vector2 first, Vector2 second)
    {
        return Vector2.DistanceSquared(first, second);
    }

    private static Vector2 GetTileCenter(Point tile)
    {
        return new Vector2((tile.X + 0.5f) * 64f, (tile.Y + 0.5f) * 64f);
    }
}
