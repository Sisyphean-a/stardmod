using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;

namespace HorseFollower;

internal sealed class OutdoorWarpTracker
{
    private static readonly int[] MovementCheckOrder = { 0, 2, 1, 3 };

    private static readonly HashSet<string> SupportedOutdoorLocations = new(StringComparer.Ordinal)
    {
        "Farm",
        "BusStop",
        "Town",
        "Forest",
        "Mountain",
        "Backwoods",
        "Railroad",
        "Beach",
        "Desert",
        "Woods",
        "Summit",
        "WitchSwamp",
        "IslandSouth",
        "IslandNorth",
        "IslandWest",
        "IslandEast",
        "IslandSouthEast",
        "IslandFarm"
    };

    private readonly IMonitor monitor;
    private readonly List<OutdoorTransition> transitions = new();
    private PendingOutdoorWarp? pendingWarp;

    internal OutdoorWarpTracker(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    internal bool HasTransitions => transitions.Count > 0;

    internal OutdoorTransition? CurrentTransition => transitions.Count > 0 ? transitions[0] : null;

    // Flow: capture only a regular map Warp reached by active movement; action-based transport never creates a candidate.
    internal void CaptureCandidate(Farmer player, bool followSessionActive)
    {
        if (!followSessionActive)
        {
            pendingWarp = null;
            return;
        }

        if (pendingWarp is not null
            && (Game1.isWarping || Game1.locationRequest is not null))
        {
            return;
        }

        pendingWarp = null;
        GameLocation? sourceLocation = player.currentLocation;
        if (sourceLocation is null
            || !player.IsLocalPlayer
            || player.mount is not null
            || !player.CanMove
            || player.UsingTool
            || Game1.eventUp
            || Game1.activeClickableMenu is not null
            || !IsSupportedOutdoorLocation(sourceLocation))
        {
            return;
        }

        foreach (int direction in MovementCheckOrder)
        {
            if (!player.movementDirections.Contains(direction))
                continue;

            Warp? warp = sourceLocation.isCollidingWithWarp(player.nextPosition(direction), player);
            if (warp is null)
                continue;

            GameLocation? targetLocation = Game1.getLocationFromName(warp.TargetName);
            if (targetLocation is null || !IsSupportedOutdoorLocation(targetLocation))
                continue;

            pendingWarp = new PendingOutdoorWarp(
                sourceLocation,
                targetLocation,
                new Point(warp.X, warp.Y));
            monitor.Log(
                $"Captured outdoor exit {sourceLocation.NameOrUniqueName} ({warp.X}, {warp.Y}) -> {targetLocation.NameOrUniqueName}.",
                LogLevel.Trace);
            return;
        }
    }

    // Guarantee: a transition is accepted only when the Warped event matches the regular outdoor Warp captured before fading.
    internal bool HandlePlayerWarp(WarpedEventArgs e, Horse? horse, bool followSessionActive)
    {
        if (!e.IsLocalPlayer)
            return false;

        PendingOutdoorWarp? candidate = pendingWarp;
        pendingWarp = null;
        if (!followSessionActive || horse is null)
            return false;

        if (IsSameLocation(horse.currentLocation, e.NewLocation))
        {
            transitions.Clear();
            return true;
        }

        bool candidateMatches = candidate is not null
            && IsSameLocation(candidate.SourceLocation, e.OldLocation)
            && IsSameLocation(candidate.TargetLocation, e.NewLocation);
        if (candidateMatches)
        {
            OutdoorTransition transition = new(
                candidate!.SourceLocation,
                candidate.TargetLocation,
                candidate.SourceExitTile,
                e.Player.TilePoint);
            return QueueTransition(horse, transition);
        }

        if (IsSupportedOutdoorLocation(e.OldLocation)
            && IsSupportedOutdoorLocation(e.NewLocation))
        {
            monitor.Log(
                $"Discarded outdoor transition {e.OldLocation.NameOrUniqueName} -> {e.NewLocation.NameOrUniqueName} because no matching walking exit was captured.",
                LogLevel.Trace);
            transitions.Clear();
            return true;
        }

        return transitions.Count == 0;
    }

    internal void CompleteCurrentTransition()
    {
        if (transitions.Count == 0)
            throw new InvalidOperationException("There is no outdoor transition to complete.");

        transitions.RemoveAt(0);
    }

    internal void ClearPending()
    {
        pendingWarp = null;
    }

    internal void ClearTransitions()
    {
        transitions.Clear();
    }

    internal void Clear()
    {
        pendingWarp = null;
        transitions.Clear();
    }

    internal static bool IsSameLocation(GameLocation? first, GameLocation? second)
    {
        return first is not null
            && second is not null
            && (ReferenceEquals(first, second)
                || string.Equals(first.NameOrUniqueName, second.NameOrUniqueName, StringComparison.Ordinal));
    }

    private bool QueueTransition(Horse horse, OutdoorTransition transition)
    {
        if (IsSameLocation(horse.currentLocation, transition.TargetLocation))
        {
            transitions.Clear();
            return true;
        }

        int existingTargetIndex = transitions.FindIndex(candidate =>
            IsSameLocation(candidate.TargetLocation, transition.TargetLocation));
        if (existingTargetIndex >= 0)
        {
            int removeIndex = existingTargetIndex + 1;
            if (removeIndex < transitions.Count)
                transitions.RemoveRange(removeIndex, transitions.Count - removeIndex);
            return false;
        }

        GameLocation? expectedSource = transitions.Count == 0
            ? horse.currentLocation
            : transitions[^1].TargetLocation;
        if (!IsSameLocation(expectedSource, transition.SourceLocation))
        {
            transitions.Clear();
            return true;
        }

        bool startsNewRoute = transitions.Count == 0;
        transitions.Add(transition);
        monitor.Log(
            $"Queued outdoor horse route {transition.SourceLocation.NameOrUniqueName} ({transition.SourceExitTile.X}, {transition.SourceExitTile.Y}) -> {transition.TargetLocation.NameOrUniqueName}.",
            LogLevel.Trace);
        return startsNewRoute;
    }

    private static bool IsSupportedOutdoorLocation(GameLocation location)
    {
        return location.IsOutdoors
            && !location.IsTemporary
            && SupportedOutdoorLocations.Contains(location.NameOrUniqueName);
    }

    private sealed record PendingOutdoorWarp(
        GameLocation SourceLocation,
        GameLocation TargetLocation,
        Point SourceExitTile);
}

internal sealed class OutdoorTransition
{
    internal OutdoorTransition(
        GameLocation sourceLocation,
        GameLocation targetLocation,
        Point sourceExitTile,
        Point destinationTile)
    {
        SourceLocation = sourceLocation;
        TargetLocation = targetLocation;
        SourceExitTile = sourceExitTile;
        DestinationTile = destinationTile;
    }

    internal GameLocation SourceLocation { get; }

    internal GameLocation TargetLocation { get; }

    internal Point SourceExitTile { get; }

    internal Point DestinationTile { get; }

    internal bool TransferRequested { get; set; }
}
