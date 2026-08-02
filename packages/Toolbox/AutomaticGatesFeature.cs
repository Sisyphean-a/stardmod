using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Toolbox;

internal sealed class AutomaticGatesFeature
{
    private readonly Func<ModConfig> getConfig;
    private readonly Dictionary<Fence, GateState> openedGates = new();

    internal AutomaticGatesFeature(Func<ModConfig> getConfig)
    {
        this.getConfig = getConfig;
    }

    internal void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!getConfig().EnableAutomaticGates)
        {
            openedGates.Clear();
            return;
        }

        if (!Context.IsPlayerFree)
            return;

        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        if (location is null)
            return;

        if (e.IsMultipleOf(5))
            OpenFacingGate(player, location);

        if (openedGates.Count > 0)
            CloseDepartedGates(player, location, DateTime.UtcNow);
    }

    internal void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        openedGates.Clear();
    }

    private void OpenFacingGate(Farmer player, GameLocation location)
    {
        Vector2 gateTile = player.Tile + GetFacingOffset(player.FacingDirection);
        if (!location.objects.TryGetValue(gateTile, out StardewValley.Object? item)
            || item is not Fence gate
            || !gate.isGate.Value
            || gate.isPassable())
        {
            return;
        }

        gate.toggleGate(player, open: true);
        if (gate.isPassable())
            openedGates[gate] = new GateState(location);
    }

    private void CloseDepartedGates(Farmer player, GameLocation location, DateTime now)
    {
        foreach ((Fence gate, GateState state) in openedGates.ToArray())
        {
            if (!ReferenceEquals(state.Location, location)
                || !state.Location.objects.TryGetValue(gate.TileLocation, out StardewValley.Object? item)
                || !ReferenceEquals(item, gate)
                || !gate.isGate.Value
                || !gate.isPassable())
            {
                openedGates.Remove(gate);
                continue;
            }

            if (IsAdjacentToGate(player.Tile, gate.TileLocation))
            {
                state.CloseAfter = null;
                continue;
            }

            state.CloseAfter ??= now.AddMilliseconds(getConfig().AutomaticGateCloseDelay);
            if (state.CloseAfter > now)
                continue;

            gate.toggleGate(player, open: false);
            openedGates.Remove(gate);
        }
    }

    private static Vector2 GetFacingOffset(int facingDirection)
    {
        return facingDirection switch
        {
            0 => new Vector2(0, -1),
            1 => new Vector2(1, 0),
            2 => new Vector2(0, 1),
            3 => new Vector2(-1, 0),
            _ => Vector2.Zero
        };
    }

    private static bool IsAdjacentToGate(Vector2 playerTile, Vector2 gateTile)
    {
        return playerTile == gateTile || Vector2.DistanceSquared(playerTile, gateTile) == 1;
    }

    private sealed class GateState
    {
        internal GateState(GameLocation location)
        {
            Location = location;
        }

        internal GameLocation Location { get; }

        internal DateTime? CloseAfter { get; set; }
    }
}
