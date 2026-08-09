using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace HorseFollower;

internal sealed class OutdoorRouteGraph
{
    private readonly Dictionary<string, List<OutdoorRouteEdge>> edgesBySource;

    private OutdoorRouteGraph(Dictionary<string, List<OutdoorRouteEdge>> edgesBySource)
    {
        this.edgesBySource = edgesBySource;
    }

    internal static OutdoorRouteGraph Build(IMonitor monitor)
    {
        var edgesBySource = new Dictionary<string, List<OutdoorRouteEdge>>(StringComparer.Ordinal);
        foreach (string locationName in OutdoorWarpTracker.SupportedOutdoorLocationNames)
        {
            GameLocation? source = Game1.getLocationFromName(locationName);
            if (source is null || !OutdoorWarpTracker.IsSupportedOutdoorLocation(source))
                continue;

            var edges = new List<OutdoorRouteEdge>();
            foreach (Warp warp in source.warps)
            {
                if (!OutdoorWarpTracker.IsSupportedOutdoorLocationName(warp.TargetName))
                    continue;

                GameLocation? target = Game1.getLocationFromName(warp.TargetName);
                if (target is null || !OutdoorWarpTracker.IsSupportedOutdoorLocation(target))
                    continue;

                edges.Add(new OutdoorRouteEdge(
                    source,
                    target,
                    new Point(warp.X, warp.Y),
                    new Point(warp.TargetX, warp.TargetY)));
            }

            edgesBySource[source.NameOrUniqueName] = edges;
            monitor.Log($"[HorseFollower] route-graph map={source.NameOrUniqueName} edges={edges.Count}", LogLevel.Trace);
        }

        return new OutdoorRouteGraph(edgesBySource);
    }

    internal OutdoorRoutePlan? FindPlan(
        GameLocation startLocation,
        Point startTile,
        HorseNavigationDestination destination,
        ISet<string> blockedEdges)
    {
        GameLocation? targetLocation = Game1.getLocationFromName(destination.MapName);
        if (targetLocation is null || !OutdoorWarpTracker.IsSupportedOutdoorLocation(targetLocation))
            return null;

        var start = new RouteState(startLocation.NameOrUniqueName, startTile);
        var queue = new PriorityQueue<RouteState, int>();
        var distances = new Dictionary<RouteState, int>();
        var previous = new Dictionary<RouteState, (RouteState Previous, OutdoorRouteEdge Edge)>();
        queue.Enqueue(start, 0);
        distances[start] = 0;

        RouteState? bestTerminal = null;
        int bestTerminalCost = int.MaxValue;
        while (queue.TryDequeue(out RouteState current, out int currentCost))
        {
            if (!distances.TryGetValue(current, out int knownCost) || knownCost != currentCost)
                continue;

            if (string.Equals(current.LocationName, targetLocation.NameOrUniqueName, StringComparison.Ordinal))
            {
                int terminalCost = currentCost + GetNearestParkingDistance(current.Tile, destination.ParkingCandidates);
                if (terminalCost < bestTerminalCost)
                {
                    bestTerminal = current;
                    bestTerminalCost = terminalCost;
                }
            }

            if (!edgesBySource.TryGetValue(current.LocationName, out List<OutdoorRouteEdge>? edges))
                continue;

            foreach (OutdoorRouteEdge edge in edges)
            {
                if (blockedEdges.Contains(edge.Key))
                    continue;

                Point approachTile = GetApproachTile(edge.SourceLocation, edge.SourceExitTile, current.Tile);
                int cost = currentCost + GetTileDistance(current.Tile, approachTile) + 1;
                var next = new RouteState(edge.TargetLocation.NameOrUniqueName, ClampToMap(edge.TargetLocation, edge.TargetEntryTile));
                if (distances.TryGetValue(next, out int existingCost) && existingCost <= cost)
                    continue;

                distances[next] = cost;
                previous[next] = (current, edge);
                queue.Enqueue(next, cost);
            }
        }

        if (bestTerminal is null)
            return null;

        var route = new List<OutdoorRouteEdge>();
        RouteState cursor = bestTerminal.Value;
        while (!cursor.Equals(start))
        {
            if (!previous.TryGetValue(cursor, out (RouteState Previous, OutdoorRouteEdge Edge) step))
                return null;

            route.Add(step.Edge);
            cursor = step.Previous;
        }

        route.Reverse();
        return new OutdoorRoutePlan(targetLocation, route);
    }

    internal static Point GetApproachTile(
        GameLocation location,
        Point sourceExitTile,
        Point fromTile,
        int clearanceTiles = 1)
    {
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"地图 {location.NameOrUniqueName} 没有有效尺寸。");

        if (clearanceTiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(clearanceTiles));

        Point sourceTile = new(
            Math.Clamp(sourceExitTile.X, 0, width - 1),
            Math.Clamp(sourceExitTile.Y, 0, height - 1));
        int dx = sourceTile.X - fromTile.X;
        int dy = sourceTile.Y - fromTile.Y;
        if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0)
        {
            sourceTile.X = Math.Clamp(sourceTile.X - Math.Sign(dx) * clearanceTiles, 0, width - 1);
        }
        else if (dy != 0)
        {
            sourceTile.Y = Math.Clamp(sourceTile.Y - Math.Sign(dy) * clearanceTiles, 0, height - 1);
        }

        return sourceTile;
    }

    internal static int GetPortalDirection(GameLocation location, Point sourceExitTile, Point currentTile)
    {
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        if (sourceExitTile.X < 0)
            return 3;
        if (sourceExitTile.X >= width)
            return 1;
        if (sourceExitTile.Y < 0)
            return 0;
        if (sourceExitTile.Y >= height)
            return 2;

        int dx = sourceExitTile.X - currentTile.X;
        int dy = sourceExitTile.Y - currentTile.Y;
        if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0)
            return dx > 0 ? 1 : 3;
        if (dy != 0)
            return dy > 0 ? 2 : 0;
        if (sourceExitTile.X == 0)
            return 3;
        if (sourceExitTile.X == width - 1)
            return 1;
        if (sourceExitTile.Y == 0)
            return 0;
        if (sourceExitTile.Y == height - 1)
            return 2;

        int left = sourceExitTile.X;
        int right = width - 1 - sourceExitTile.X;
        int up = sourceExitTile.Y;
        int down = height - 1 - sourceExitTile.Y;
        int min = Math.Min(Math.Min(left, right), Math.Min(up, down));
        return min == left ? 3 : min == right ? 1 : min == up ? 0 : 2;
    }

    private static Point ClampToMap(GameLocation location, Point tile)
    {
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        return new Point(
            Math.Clamp(tile.X, 0, Math.Max(0, width - 1)),
            Math.Clamp(tile.Y, 0, Math.Max(0, height - 1)));
    }

    private static int GetNearestParkingDistance(Point tile, IReadOnlyList<Point> candidates)
    {
        if (candidates.Count == 0)
            return 1000000;

        return candidates.Min(candidate => GetTileDistance(tile, candidate));
    }

    private static int GetTileDistance(Point first, Point second)
    {
        return Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));
    }

    private readonly record struct RouteState(string LocationName, Point Tile);
}

internal sealed record OutdoorRouteEdge(
    GameLocation SourceLocation,
    GameLocation TargetLocation,
    Point SourceExitTile,
    Point TargetEntryTile)
{
    internal string Key =>
        $"{SourceLocation.NameOrUniqueName}:{SourceExitTile.X},{SourceExitTile.Y}>{TargetLocation.NameOrUniqueName}:{TargetEntryTile.X},{TargetEntryTile.Y}";
}

internal sealed record OutdoorRoutePlan(
    GameLocation TargetLocation,
    IReadOnlyList<OutdoorRouteEdge> Edges);
