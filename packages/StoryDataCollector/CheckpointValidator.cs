using System;
using System.Linq;

namespace StoryDataCollector;

internal static class CheckpointValidator
{
    internal const int MaximumEvents = 4096;
    internal const int MaximumLocations = 2048;

    internal static bool IsValid(DailyCheckpoint checkpoint)
    {
        return checkpoint.SchemaVersion == 1
            && checkpoint.Date is not null
            && checkpoint.Context is not null
            && checkpoint.StartState is not null
            && checkpoint.Events is not null
            && checkpoint.LocationStays is not null
            && checkpoint.DroppedEventCounts is not null
            && checkpoint.Date.Year >= 1
            && checkpoint.Date.Day is >= 1 and <= 28
            && !string.IsNullOrWhiteSpace(checkpoint.Date.Season)
            && checkpoint.Events.Count <= MaximumEvents
            && checkpoint.LocationStays.Count <= MaximumLocations
            && checkpoint.DroppedLocationStays >= 0
            && checkpoint.DroppedEventCounts.Values.All(count => count >= 0)
            && checkpoint.Events.All(gameEvent => gameEvent.Details is not null
                && !string.IsNullOrWhiteSpace(gameEvent.Id)
                && !string.IsNullOrWhiteSpace(gameEvent.Type)
                && gameEvent.Sequence > 0
                && gameEvent.Importance is >= 0 and <= 5)
            && checkpoint.Events.Select(gameEvent => gameEvent.Id).Distinct(StringComparer.Ordinal).Count() == checkpoint.Events.Count
            && checkpoint.LastSequence >= checkpoint.Events.Select(gameEvent => gameEvent.Sequence).DefaultIfEmpty(0).Max();
    }
}
